// --------------------------------------------------------------
// Copyright 2026 CyberAgent, Inc.
// --------------------------------------------------------------

// 本クラスはURPに依存せず、RenderGraphを利用してSSRを実装するためのヘルパークラスです。

using System.Collections.Generic;
using Sirius.Core;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

namespace Sirius.PostProcessing
{
    public static class SSRHelper
    {
        private const int SSRPassIndex = 0;
        private const int ComposePassIndex = 1;

        #region Descriptor

        public struct Descriptor
        {
            // Material（呼び出し側がライフサイクルを管理）
            public Material SSRMaterial;

            // シーン入力
            public TextureHandle CameraDepthTexture;     // ピラミッドの仮想lod0を兼ねる。トレース空間サイズの導出元として常時必須
            public TextureHandle CameraNormalsTexture;

            // 統一深度ピラミッド
            public TextureHandle HalfStartDepthMipMap;
            public int MipMapLevelCount;                 // HalfStartDepthMipMapの実mip数

            // 反射重み入力（呼び出し側が解決して渡す。無効handle可）
            public TextureHandle DiffuseTexture;
            public TextureHandle SmoothTexture;
            public TextureHandle MetallicTexture;
            public TextureHandle AOTexture;
            public int SmoothChannel;
            public int MetallicChannel;
            public int AOChannel;

            // Volume解決値
            public bool DownSample;
            public int MaxSteps;
            public int HiZTraceStartLevel;               // 生値（clampはHelper内で行う）
            public int HiZTraceMaxLevel;                 // 生値
            public float MinSmoothness;
            public bool ReflectSky;
            public int DitherMode;                       // (int)SSRDitherMode
            public int DitherTarget;                     // (int)SSRDitherTarget

            // 非URP経路向けの注入口。URPではengineがグローバル設定するため未設定（ゼロ）のままでよい
            public Matrix4x4 MatrixV;                    // unity_MatrixV
            public Matrix4x4 MatrixP;                    // glstate_matrix_projection
            public Matrix4x4 MatrixVP;                   // unity_MatrixVP
            public Matrix4x4 MatrixInvP;                 // unity_MatrixInvP
            public Matrix4x4 MatrixInvVP;                // unity_MatrixInvVP
            public Vector4 OrthoParams;                  // unity_OrthoParams（透視/正射分岐）
            public Vector3? WorldSpaceCameraPos;         // _WorldSpaceCameraPos（原点は正当値のため null で未設定を表す）
        }

        #endregion

        private static class NameContents
        {
            // Texture名
            public const string SSRReflectionTexName = "SSR_ReflectionTexture";
            public const string SSRCameraColorCopyTexName = "SSR_CameraColorCopyTexture";
            // Pass名
            public const string RenderSSRPassName = "Sirius.SSR.RenderSSR";
            public const string CopyCameraColorPassName = "Sirius.SSR.CopyCameraColor";
            public const string ComposeSSRPassName = "Sirius.SSR.ComposeSSR";
        }

        private static class ShaderPropertyID
        {
            public static readonly int _Frame = Shader.PropertyToID("_Frame");
            public static readonly int _DitherMode = Shader.PropertyToID("_DitherMode");
            public static readonly int _DitherTarget = Shader.PropertyToID("_DitherTarget");
            public static readonly int _CT_SSR_ReflectedUVMap = Shader.PropertyToID("_CT_SSR_ReflectedUVMap");
            public static readonly int _ScreenResolution = Shader.PropertyToID("_ScreenResolution");
            public static readonly int _CrossEpsilon = Shader.PropertyToID("_CrossEpsilon");
            public static readonly int _MaxSteps = Shader.PropertyToID("_MaxSteps");
            public static readonly int _HiZTraceStartLevel = Shader.PropertyToID("_HiZTraceStartLevel");
            public static readonly int _HiZTraceMaxLevel = Shader.PropertyToID("_HiZTraceMaxLevel");
            public static readonly int _MinSmoothness = Shader.PropertyToID("_MinSmoothness");
            public static readonly int _IsReflectSky = Shader.PropertyToID("_IsReflectSky");

            // シーン入力
            public static readonly int _CameraDepthTexture = Shader.PropertyToID("_CameraDepthTexture");
            public static readonly int _CameraNormalsTexture = Shader.PropertyToID("_CameraNormalsTexture");

            // Sirius Map
            public static readonly int _CT_SSR_DiffuseTexture = Shader.PropertyToID("_CT_SSR_DiffuseTexture");
            public static readonly int _CT_SSR_SmoothTexture = Shader.PropertyToID("_CT_SSR_SmoothTexture");
            public static readonly int _CT_SSR_SmoothChannel = Shader.PropertyToID("_CT_SSR_SmoothChannel");
            public static readonly int _CT_SSR_MetalicTexture = Shader.PropertyToID("_CT_SSR_MetalicTexture");
            public static readonly int _CT_SSR_MetalicChannel = Shader.PropertyToID("_CT_SSR_MetalicChannel");
            public static readonly int _CT_SSR_AOTexture = Shader.PropertyToID("_CT_SSR_AOTexture");
            public static readonly int _CT_SSR_AOChannel = Shader.PropertyToID("_CT_SSR_AOChannel");

            // 非URP経路向けの注入口
            public static readonly int MatrixV = Shader.PropertyToID("unity_MatrixV");
            public static readonly int MatrixP = Shader.PropertyToID("glstate_matrix_projection");
            public static readonly int MatrixVP = Shader.PropertyToID("unity_MatrixVP");
            public static readonly int MatrixInvP = Shader.PropertyToID("unity_MatrixInvP");
            public static readonly int MatrixInvVP = Shader.PropertyToID("unity_MatrixInvVP");
            public static readonly int OrthoParams = Shader.PropertyToID("unity_OrthoParams");
            public static readonly int WorldSpaceCameraPos = Shader.PropertyToID("_WorldSpaceCameraPos");
        }

        private class SSRPassData
        {
            internal Material SSRMaterial;
            internal TextureHandle ReflectionTexHandle;
            internal TextureHandle CameraColorCopyTexHandle;
            internal TextureHandle FullSizeDepthTexHandle;
            internal TextureHandle HalfStartDepthMipMapHandle;
            internal TextureHandle CameraDepthTexHandle;
            internal TextureHandle CameraNormalsTexHandle;
            internal TextureHandle DiffuseTexHandle;
            internal TextureHandle SmoothTexHandle;
            internal TextureHandle MetallicTexHandle;
            internal TextureHandle AOTexHandle;
            internal int SmoothTexChannel;
            internal int MetallicTexChannel;
            internal int AOTexChannel;
        }

        private static void SetMaterialProperties(Material material, in Descriptor desc, Vector2 ssrSize,
            Vector2 crossEpsilon, int depthMapMipCount)
        {
            DepthMipMapHelper.SetDepthPyramidFullSizeKeyword(material, !desc.DownSample);
            material.SetInteger(ShaderPropertyID._Frame, Time.frameCount);
            material.SetInteger(ShaderPropertyID._DitherMode, desc.DitherMode);
            material.SetInteger(ShaderPropertyID._DitherTarget, desc.DitherTarget);
            material.SetInteger(ShaderPropertyID._MaxSteps, desc.MaxSteps);
            var hiZTraceMaxLevel = Mathf.Min(desc.HiZTraceMaxLevel, depthMapMipCount - 1);
            var hiZTraceStartLevel = Mathf.Min(desc.HiZTraceStartLevel, hiZTraceMaxLevel);
            material.SetInteger(ShaderPropertyID._HiZTraceStartLevel, hiZTraceStartLevel);
            material.SetInteger(ShaderPropertyID._HiZTraceMaxLevel, hiZTraceMaxLevel);
            material.SetFloat(ShaderPropertyID._MinSmoothness, desc.MinSmoothness);
            material.SetInteger(ShaderPropertyID._IsReflectSky, desc.ReflectSky ? 1 : 0);
            material.SetVector(ShaderPropertyID._ScreenResolution, ssrSize);
            material.SetVector(ShaderPropertyID._CrossEpsilon, crossEpsilon);

            // 非URP経路向けの注入口。URPではengineがグローバル設定するため、ゼロ（未設定）なら何もしない
            if (desc.MatrixV != Matrix4x4.zero)
            {
                material.SetMatrix(ShaderPropertyID.MatrixV, desc.MatrixV);
            }
            if (desc.MatrixP != Matrix4x4.zero)
            {
                material.SetMatrix(ShaderPropertyID.MatrixP, desc.MatrixP);
            }
            if (desc.MatrixVP != Matrix4x4.zero)
            {
                material.SetMatrix(ShaderPropertyID.MatrixVP, desc.MatrixVP);
            }
            if (desc.MatrixInvP != Matrix4x4.zero)
            {
                material.SetMatrix(ShaderPropertyID.MatrixInvP, desc.MatrixInvP);
            }
            if (desc.MatrixInvVP != Matrix4x4.zero)
            {
                material.SetMatrix(ShaderPropertyID.MatrixInvVP, desc.MatrixInvVP);
            }
            if (desc.OrthoParams != Vector4.zero)
            {
                material.SetVector(ShaderPropertyID.OrthoParams, desc.OrthoParams);
            }
            if (desc.WorldSpaceCameraPos.HasValue)
            {
                material.SetVector(ShaderPropertyID.WorldSpaceCameraPos, desc.WorldSpaceCameraPos.Value);
            }
        }

        /// <summary>
        /// SSRの3パス（カメラ色コピー / HiZトレース / 合成）を記録する。
        /// </summary>
        /// <param name="desc">SSRの入力一式。</param>
        /// <param name="blendTarget">反射の合成先。カメラ色コピーの採様源を兼ねるため、有効な内容を持つテクスチャ（通常はカメラカラー）を渡すこと。</param>
        public static void AddSSRPasses(this RenderGraph renderGraph, in Descriptor desc, TextureHandle blendTarget)
        {
            // カメラ色のコピーをとる(MipMapを自動生成させる)
            var colorDesc = renderGraph.GetTextureDesc(blendTarget);
            colorDesc.useMipMap = true;
            colorDesc.autoGenerateMips = true;
            colorDesc.clearBuffer = false;
            colorDesc.name = NameContents.SSRCameraColorCopyTexName;
            var copiedCameraColorTex = renderGraph.CreateTexture(colorDesc);
            renderGraph.AddCopyPassCustom(blendTarget, copiedCameraColorTex,
                passName: NameContents.CopyCameraColorPassName);

            var downSample = desc.DownSample;

            // トレース空間の層数: 全解像度時は仮想lod0(FullSize本体)が1段加わる
            var depthMapMipCount = downSample ? desc.MipMapLevelCount : desc.MipMapLevelCount + 1;

            // トレース空間lod0のサイズ(実際にトレースするテクスチャ自身のdescから取る)
            var fullDepthDesc = renderGraph.GetTextureDesc(desc.CameraDepthTexture);
            var halfDepthDesc = renderGraph.GetTextureDesc(desc.HalfStartDepthMipMap);
            var ssrWidth = downSample ? halfDepthDesc.width : fullDepthDesc.width;
            var ssrHeight = downSample ? halfDepthDesc.height : fullDepthDesc.height;
            var ssrSize = new Vector2(ssrWidth, ssrHeight);
            var crossEpsilon = Vector2.one / (512 * ssrSize);

            SetMaterialProperties(desc.SSRMaterial, desc, ssrSize, crossEpsilon, depthMapMipCount);

            // SSR反射テクスチャを作成
            var reflectionDesc = new TextureDesc(ssrWidth, ssrHeight)
            {
                colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                name = NameContents.SSRReflectionTexName,
                clearBuffer = false,
            };
            var reflectionTexHandle = renderGraph.CreateTexture(reflectionDesc);

            using (var builder = renderGraph.AddRasterRenderPass<SSRPassData>(NameContents.RenderSSRPassName,
                       out var passData))
            {
                passData.SSRMaterial = desc.SSRMaterial;
                passData.FullSizeDepthTexHandle = downSample ? TextureHandle.nullHandle : desc.CameraDepthTexture;
                passData.HalfStartDepthMipMapHandle = desc.HalfStartDepthMipMap;
                passData.CameraNormalsTexHandle = desc.CameraNormalsTexture;
                passData.SmoothTexHandle = desc.SmoothTexture;
                passData.SmoothTexChannel = desc.SmoothChannel;

                builder.SetRenderAttachment(reflectionTexHandle, 0, AccessFlags.Write);

                if (passData.FullSizeDepthTexHandle.IsValid())
                {
                    builder.UseTexture(passData.FullSizeDepthTexHandle, AccessFlags.Read);
                }
                builder.UseTexture(passData.HalfStartDepthMipMapHandle, AccessFlags.Read);

                builder.UseTexture(passData.CameraNormalsTexHandle, AccessFlags.Read);

                if (passData.SmoothTexHandle.IsValid())
                {
                    builder.UseTexture(passData.SmoothTexHandle, AccessFlags.Read);
                }

                builder.SetRenderFunc(static (SSRPassData data, RasterGraphContext context) =>
                {
                    if (data.FullSizeDepthTexHandle.IsValid())
                    {
                        DepthMipMapHelper.BindFullSizeDepthTexture(data.SSRMaterial, data.FullSizeDepthTexHandle);
                    }
                    DepthMipMapHelper.BindHalfStartDepthMipMap(data.SSRMaterial, data.HalfStartDepthMipMapHandle);
                    data.SSRMaterial.SetTexture(ShaderPropertyID._CameraNormalsTexture, data.CameraNormalsTexHandle);
                    data.SSRMaterial.SetTexture(ShaderPropertyID._CT_SSR_SmoothTexture, data.SmoothTexHandle.IsValid()
                                                                                            ? data.SmoothTexHandle
                                                                                            : Texture2D.blackTexture);
                    data.SSRMaterial.SetInteger(ShaderPropertyID._CT_SSR_SmoothChannel, data.SmoothTexChannel);

                    Blitter.BlitTexture(context.cmd, Vector2.one, data.SSRMaterial, SSRPassIndex);
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<SSRPassData>(NameContents.ComposeSSRPassName,
                       out var passData))
            {
                passData.SSRMaterial = desc.SSRMaterial;
                passData.CameraColorCopyTexHandle = copiedCameraColorTex;
                passData.ReflectionTexHandle = reflectionTexHandle;
                passData.CameraDepthTexHandle = desc.CameraDepthTexture;
                passData.CameraNormalsTexHandle = desc.CameraNormalsTexture;

                passData.DiffuseTexHandle = desc.DiffuseTexture;
                passData.SmoothTexHandle = desc.SmoothTexture;
                passData.MetallicTexHandle = desc.MetallicTexture;
                passData.AOTexHandle = desc.AOTexture;
                passData.SmoothTexChannel = desc.SmoothChannel;
                passData.MetallicTexChannel = desc.MetallicChannel;
                passData.AOTexChannel = desc.AOChannel;

                builder.SetRenderAttachment(blendTarget, 0, AccessFlags.Write);
                builder.UseTexture(passData.CameraColorCopyTexHandle, AccessFlags.Read);
                builder.UseTexture(passData.ReflectionTexHandle, AccessFlags.Read);
                builder.UseTexture(passData.CameraDepthTexHandle, AccessFlags.Read);
                builder.UseTexture(passData.CameraNormalsTexHandle, AccessFlags.Read);

                // 重複Useを避けるため、一旦HashSetに入れる
                var validTexHashSet = new HashSet<TextureHandle>();
                if (passData.DiffuseTexHandle.IsValid())
                {
                    validTexHashSet.Add(passData.DiffuseTexHandle);
                }
                if (passData.SmoothTexHandle.IsValid())
                {
                    validTexHashSet.Add(passData.SmoothTexHandle);
                }
                if (passData.MetallicTexHandle.IsValid())
                {
                    validTexHashSet.Add(passData.MetallicTexHandle);
                }
                if (passData.AOTexHandle.IsValid())
                {
                    validTexHashSet.Add(passData.AOTexHandle);
                }
                foreach (var texHandle in validTexHashSet)
                {
                    builder.UseTexture(texHandle, AccessFlags.Read);
                }

                builder.SetRenderFunc(static (SSRPassData data, RasterGraphContext context) =>
                {
                    var material = data.SSRMaterial;
                    material.SetTexture(ShaderPropertyID._CT_SSR_ReflectedUVMap, data.ReflectionTexHandle);
                    material.SetTexture(ShaderPropertyID._CameraDepthTexture, data.CameraDepthTexHandle);
                    material.SetTexture(ShaderPropertyID._CameraNormalsTexture, data.CameraNormalsTexHandle);
                    material.SetTexture(ShaderPropertyID._CT_SSR_DiffuseTexture, data.DiffuseTexHandle.IsValid() ? data.DiffuseTexHandle
                                                                                 : Texture2D.blackTexture);
                    material.SetTexture(ShaderPropertyID._CT_SSR_SmoothTexture, data.SmoothTexHandle.IsValid() ? data.SmoothTexHandle
                                                                                : Texture2D.blackTexture);
                    material.SetTexture(ShaderPropertyID._CT_SSR_MetalicTexture, data.MetallicTexHandle.IsValid() ? data.MetallicTexHandle
                                                                                 : Texture2D.blackTexture);
                    material.SetTexture(ShaderPropertyID._CT_SSR_AOTexture, data.AOTexHandle.IsValid() ? data.AOTexHandle
                                                                            : Texture2D.whiteTexture);

                    material.SetInteger(ShaderPropertyID._CT_SSR_SmoothChannel, data.SmoothTexChannel);
                    material.SetInteger(ShaderPropertyID._CT_SSR_MetalicChannel, data.MetallicTexChannel);
                    material.SetInteger(ShaderPropertyID._CT_SSR_AOChannel, data.AOTexChannel);

                    Blitter.BlitTexture(context.cmd, data.CameraColorCopyTexHandle, Vector2.one, data.SSRMaterial, ComposePassIndex);
                });
            }
        }
    }
}
