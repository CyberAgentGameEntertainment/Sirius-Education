// --------------------------------------------------------------
// Copyright 2026 CyberAgent, Inc.
// --------------------------------------------------------------

// 本クラスはURPに依存せず、RenderGraphを利用してSSPRを実装するためのヘルパークラスです。

using Sirius.Core;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

namespace Sirius.PostProcessing
{
    public static class SSPRHelper
    {
        // UtilBlur.shader のパス番号契約
        private enum UtilBlurPass
        {
            DownSampling = 0,
            UpSampling
        }

        // must match compute shader's [numthread(x,y)]。呼び出し側はRTサイズをこの倍数へ切り上げて渡す
        public const int ShaderNumThreadX = 8;
        public const int ShaderNumThreadY = 8;

        private const string SsprCameraRTName = "_SSPR_CameraRT";
        private const string SsprColorRTName = "_SSPR_ColorRT";
        private const string PositionYWorldSpaceBufferName = "PosWSyRT";

        #region Descriptor

        public struct Descriptor
        {
            // 資産（呼び出し側がライフサイクルを管理。kernel IDは呼び出し側がFindKernelで解決する）
            public ComputeShader ComputeShader;
            public int ClearKernel;                // FindKernel("Clear")
            public int ColorRTDirectResolveKernel; // FindKernel("MobilePathSinglePassColorRTDirectResolve")
            public int FillHolesKernel;            // FindKernel("FillHoles")
            public Material BlurMaterial;          // Hidden/Sirius/UtilBlur

            // シーン入力
            public TextureHandle CameraColorTexture;  // 縮小コピー元（通常はカメラカラー）
            public TextureHandle CameraDepthTexture;  // compute へ明示束縛する。有効なhandleであること（必須）
            public BufferHandle PosWSyBuffer;         // import済みの永続バッファ（RTWidth×RTHeight, Raw uint）

            // RTサイズ（呼び出し側がShaderNumThreadX/Y倍数へ切り上げ済みの値）
            public int RTWidth;
            public int RTHeight;

            // Volume解決値
            public float HorizontalPlaneHeightWS;
            public float HorizontalPlaneHeightWSBias;
            public float FadeOutScreenBorderWidthVertical;
            public float FadeOutScreenBorderWidthHorizontal;
            public float ScreenLRStretchIntensity;
            public float ScreenLRStretchThreshold;
            public Color TintColor;
            public bool FillHoles;                 // 高品質時のみ穴埋めkernelを実行する
            public float BlurIntensity;
            public float BlurWidth;
            public int BlurTextureMipCount;

            // カメラ由来値（呼び出し側が解決して渡す）
            public Vector3 CameraForwardVector;                     // カメラのforward
            public Vector3 CameraPositionWS;                        // カメラのワールド座標（床と天井の判別に使う）
            public Matrix4x4 CameraViewProjectionMatrix;            // MetalでUNITY_MATRIX_VPが不正なため自前供給するVP行列（GPU射影規約）。逆行列はHelperが導出する
        }

        #endregion

        private static class ShaderPropertyIDs
        {
            public static readonly int ScreenSpacePlanarReflectionTex = Shader.PropertyToID("_CtScreenSpacePlanarReflectionTex");
            public static readonly int CameraDepthTexture = Shader.PropertyToID("_CameraDepthTexture");
            public static readonly int RTSize = Shader.PropertyToID("_RTSize");
            public static readonly int HorizontalPlaneHeightWS = Shader.PropertyToID("_HorizontalPlaneHeightWS");
            public static readonly int HorizontalPlaneHeightWSBias = Shader.PropertyToID("_HorizontalPlaneHeightWSBias");
            public static readonly int FadeOutScreenBorderWidthVertical = Shader.PropertyToID("_FadeOutScreenBorderWidthVertical");
            public static readonly int FadeOutScreenBorderWidthHorizontal = Shader.PropertyToID("_FadeOutScreenBorderWidthHorizontal");
            public static readonly int CameraDirection = Shader.PropertyToID("_CameraDirection");
            public static readonly int CameraPositionWS = Shader.PropertyToID("_CameraPositionWS");
            public static readonly int ScreenLRStretchIntensity = Shader.PropertyToID("_ScreenLRStretchIntensity");
            public static readonly int ScreenLRStretchThreshold = Shader.PropertyToID("_ScreenLRStretchThreshold");
            public static readonly int FinalTintColor = Shader.PropertyToID("_FinalTintColor");
            public static readonly int CopiedCameraRT = Shader.PropertyToID("_CopiedCameraRT");
            public static readonly int ColorRT = Shader.PropertyToID("ColorRT");
            public static readonly int PosWSyBuffer = Shader.PropertyToID(PositionYWorldSpaceBufferName);
            public static readonly int VpMatrix = Shader.PropertyToID("_VPMatrix");
            public static readonly int InvVpMatrix = Shader.PropertyToID("_InvVPMatrix");
            public static readonly int LowMipBlendRate = Shader.PropertyToID("_LowMipBlendRate");
            public static readonly int TexelSizeScale = Shader.PropertyToID("_TexelSizeScale");
        }

        private class ComputeReflectionTexturePassData
        {
            public ComputeShader Cs;
            public int ClearKernel;
            public int ColorRTDirectResolveKernel;
            public int FillHolesKernel;
            public TextureHandle CameraDepthTexture;
            public TextureHandle CopiedTexture;
            public TextureHandle ColorTexture;
            public BufferHandle PosWSyBuffer;
            public int RTWidth;
            public int RTHeight;
            public Vector3 CameraDirection;
            public Vector3 CameraPositionWS;
            public Matrix4x4 VPMatrix;
            public float HorizontalPlaneHeightWS;
            public float HorizontalPlaneHeightWSBias;
            public float FadeOutScreenBorderWidthVertical;
            public float FadeOutScreenBorderWidthHorizontal;
            public float ScreenLRStretchIntensity;
            public float ScreenLRStretchThreshold;
            public Color TintColor;
            public bool FillHoles;
        }

        /// <summary>
        /// カメラカラーの縮小コピーRTのdescを導出する。
        /// </summary>
        private static TextureDesc GetCopiedCameraTextureDesc(RenderGraph renderGraph, TextureHandle cameraColorTexture, int rtWidth, int rtHeight)
        {
            var textureDesc = renderGraph.GetTextureDesc(cameraColorTexture);
            textureDesc.width = rtWidth;
            textureDesc.height = rtHeight;
            textureDesc.msaaSamples = MSAASamples.None;
            textureDesc.depthBufferBits = 0;
            textureDesc.clearBuffer = false;
            textureDesc.name = SsprCameraRTName;
            return textureDesc;
        }

        /// <summary>
        /// computeが書き込む反射色RTのdescを導出する。
        /// 呼び出し側が永続反射RTを割り当てる際も本descを基点にすることで、導出式の二重実装を防ぐ。
        /// </summary>
        public static TextureDesc GetColorTextureDesc(RenderGraph renderGraph, TextureHandle cameraColorTexture, int rtWidth, int rtHeight)
        {
            var textureDesc = GetCopiedCameraTextureDesc(renderGraph, cameraColorTexture, rtWidth, rtHeight);
            textureDesc.enableRandomWrite = true;
            textureDesc.useMipMap = false;
            textureDesc.autoGenerateMips = false;
            textureDesc.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
            textureDesc.name = SsprColorRTName;
            return textureDesc;
        }

        /// <summary>
        /// SSPRの一連のパス（カメラカラー縮小Blit / compute反射生成 / DualBlur / グローバル公開）を記録する。
        /// </summary>
        /// <param name="desc">SSPRの入力一式。</param>
        /// <param name="outputTarget">反射結果の書き込み先。内容は全面上書きされる。
        /// 翌フレームに<see cref="BindReflectionTexture"/>で再バインドして参照するため、外部でimportした永続RTを渡すこと。</param>
        public static void AddSSPRPasses(this RenderGraph renderGraph, in Descriptor desc, TextureHandle outputTarget)
        {
            // copiedTexture生成
            var copiedTexture = renderGraph.CreateTexture(GetCopiedCameraTextureDesc(renderGraph, desc.CameraColorTexture, desc.RTWidth, desc.RTHeight));

            // colorTexture生成
            var colorTexture = renderGraph.CreateTexture(GetColorTextureDesc(renderGraph, desc.CameraColorTexture, desc.RTWidth, desc.RTHeight));

            // カメラカラーをcopiedTextureにBlitする
            renderGraph.AddBlitPass(desc.CameraColorTexture, copiedTexture, Vector2.one, Vector2.zero,
                filterMode: RenderGraphUtils.BlitFilterMode.ClampNearest, passName: "Blit Camera Texture");

            using (var builder = renderGraph.AddComputePass<ComputeReflectionTexturePassData>("Compute Reflection Texture", out var passData))
            {
                passData.Cs = desc.ComputeShader;
                passData.ClearKernel = desc.ClearKernel;
                passData.ColorRTDirectResolveKernel = desc.ColorRTDirectResolveKernel;
                passData.FillHolesKernel = desc.FillHolesKernel;
                passData.CameraDepthTexture = desc.CameraDepthTexture;
                passData.CopiedTexture = copiedTexture;
                passData.ColorTexture = colorTexture;
                passData.PosWSyBuffer = desc.PosWSyBuffer;
                passData.RTWidth = desc.RTWidth;
                passData.RTHeight = desc.RTHeight;
                passData.CameraDirection = desc.CameraForwardVector;
                passData.CameraPositionWS = desc.CameraPositionWS;
                passData.VPMatrix = desc.CameraViewProjectionMatrix;
                passData.HorizontalPlaneHeightWS = desc.HorizontalPlaneHeightWS;
                passData.HorizontalPlaneHeightWSBias = desc.HorizontalPlaneHeightWSBias;
                passData.FadeOutScreenBorderWidthVertical = desc.FadeOutScreenBorderWidthVertical;
                passData.FadeOutScreenBorderWidthHorizontal = desc.FadeOutScreenBorderWidthHorizontal;
                passData.ScreenLRStretchIntensity = desc.ScreenLRStretchIntensity;
                passData.ScreenLRStretchThreshold = desc.ScreenLRStretchThreshold;
                passData.TintColor = desc.TintColor;
                passData.FillHoles = desc.FillHoles;

                builder.UseTexture(passData.CameraDepthTexture, AccessFlags.Read);
                builder.UseTexture(passData.CopiedTexture, AccessFlags.Read);
                builder.UseTexture(passData.ColorTexture, AccessFlags.Write);
                builder.UseBuffer(passData.PosWSyBuffer, AccessFlags.Write);
                builder.SetRenderFunc(static (ComputeReflectionTexturePassData data, ComputeGraphContext context) =>
                {
                    var cmd = context.cmd;
                    var cs = data.Cs;
                    var copiedTexture = data.CopiedTexture;
                    var colorTexture = data.ColorTexture;
                    var posWSyBuffer = data.PosWSyBuffer;
                    var rtWidth = data.RTWidth;
                    var rtHeight = data.RTHeight;

                    var dispatchThreadGroupXCount = rtWidth / ShaderNumThreadX; // シェーダーの numthreads.x で割る
                    var dispatchThreadGroupYCount = rtHeight / ShaderNumThreadY; // シェーダーの numthreads.y で割る
                    var dispatchThreadGroupZCount = 1; // シェーダーの numthreads.z で割る

                    // パラメータセット
                    cmd.SetComputeVectorParam(cs, ShaderPropertyIDs.RTSize, new Vector2(rtWidth, rtHeight));
                    cmd.SetComputeVectorParam(cs, ShaderPropertyIDs.CameraDirection, data.CameraDirection);
                    cmd.SetComputeVectorParam(cs, ShaderPropertyIDs.CameraPositionWS, data.CameraPositionWS);
                    cmd.SetComputeFloatParam(cs, ShaderPropertyIDs.HorizontalPlaneHeightWS, data.HorizontalPlaneHeightWS);
                    cmd.SetComputeFloatParam(cs, ShaderPropertyIDs.HorizontalPlaneHeightWSBias, data.HorizontalPlaneHeightWSBias);
                    cmd.SetComputeFloatParam(cs, ShaderPropertyIDs.FadeOutScreenBorderWidthVertical, data.FadeOutScreenBorderWidthVertical);
                    cmd.SetComputeFloatParam(cs, ShaderPropertyIDs.FadeOutScreenBorderWidthHorizontal, data.FadeOutScreenBorderWidthHorizontal);
                    cmd.SetComputeFloatParam(cs, ShaderPropertyIDs.ScreenLRStretchIntensity, data.ScreenLRStretchIntensity);
                    cmd.SetComputeFloatParam(cs, ShaderPropertyIDs.ScreenLRStretchThreshold, data.ScreenLRStretchThreshold);
                    cmd.SetComputeVectorParam(cs, ShaderPropertyIDs.FinalTintColor, data.TintColor);
                    //computeはengine設定の内蔵行列に依存できない（MetalでUNITY_MATRIX_VPが不正になるのを実測）ため、VP/InvVPとも自前供給する
                    cmd.SetComputeMatrixParam(cs, ShaderPropertyIDs.VpMatrix, data.VPMatrix);
                    cmd.SetComputeMatrixParam(cs, ShaderPropertyIDs.InvVpMatrix, Matrix4x4.Inverse(data.VPMatrix));

                    // バッファのクリア
                    var clearKernel = data.ClearKernel;
                    cmd.SetComputeTextureParam(cs, clearKernel, ShaderPropertyIDs.ColorRT, colorTexture);
                    cmd.SetComputeBufferParam(cs, clearKernel, ShaderPropertyIDs.PosWSyBuffer, posWSyBuffer);
                    cmd.DispatchCompute(cs, clearKernel, dispatchThreadGroupXCount, dispatchThreadGroupYCount, dispatchThreadGroupZCount);

                    // ColorDirectResolve
                    var kColorRTDirectResolve = data.ColorRTDirectResolveKernel;
                    cmd.SetComputeTextureParam(cs, kColorRTDirectResolve, ShaderPropertyIDs.CopiedCameraRT, copiedTexture);
                    cmd.SetComputeTextureParam(cs, kColorRTDirectResolve, ShaderPropertyIDs.ColorRT, colorTexture);
                    cmd.SetComputeTextureParam(cs, kColorRTDirectResolve, ShaderPropertyIDs.CameraDepthTexture, data.CameraDepthTexture);
                    cmd.SetComputeBufferParam(cs, kColorRTDirectResolve, ShaderPropertyIDs.PosWSyBuffer, posWSyBuffer);
                    cmd.DispatchCompute(cs, kColorRTDirectResolve, dispatchThreadGroupXCount, dispatchThreadGroupYCount, dispatchThreadGroupZCount);

                    // 計算が行われなかったピクセルの補完
                    if (data.FillHoles)
                    {
                        var k_FillHoles = data.FillHolesKernel;
                        cmd.SetComputeTextureParam(cs, k_FillHoles, ShaderPropertyIDs.ColorRT, colorTexture);
                        cmd.DispatchCompute(cs, k_FillHoles, Mathf.CeilToInt(dispatchThreadGroupXCount / 2f), Mathf.CeilToInt(dispatchThreadGroupYCount / 2f), dispatchThreadGroupZCount);
                    }
                });
            }

            desc.BlurMaterial.SetFloat(ShaderPropertyIDs.LowMipBlendRate, desc.BlurIntensity);
            desc.BlurMaterial.SetFloat(ShaderPropertyIDs.TexelSizeScale, desc.BlurWidth);
            var dualBlurDesc = new DualBlurHelper.Descriptor()
            {
                Material = desc.BlurMaterial,
                SourceTexture = colorTexture,
                MaxMipCount = desc.BlurTextureMipCount,
                DownSamplePassIndex = (int)UtilBlurPass.DownSampling,
                UpSamplePassIndex = (int)UtilBlurPass.UpSampling,
                PassNamePrefix = "SSPR"
            };
            renderGraph.AddDualBlurPass(dualBlurDesc, outputTarget);
            BindReflectionTexture(renderGraph, outputTarget);
        }

        /// <summary>
        /// 反射テクスチャをグローバルテクスチャ（_CtScreenSpacePlanarReflectionTex）として設定するパスを記録する。
        /// RenderGraphはフレーム終了時にグローバルテクスチャをリセットするため、
        /// 効果実行時点に加え、翌フレームのBeforeRenderingで前フレームの反射RTを再バインドする用途でも呼ばれる。
        /// </summary>
        public static void BindReflectionTexture(RenderGraph renderGraph, TextureHandle reflectionTexture)
        {
            renderGraph.AddSetGlobalTexturePass(reflectionTexture, ShaderPropertyIDs.ScreenSpacePlanarReflectionTex);
        }
    }
}
