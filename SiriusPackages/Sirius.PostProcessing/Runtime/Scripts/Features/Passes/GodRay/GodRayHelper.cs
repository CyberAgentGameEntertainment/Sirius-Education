// --------------------------------------------------------------
// Copyright 2026 CyberAgent, Inc.
// --------------------------------------------------------------

using Sirius.PostProcessing.Runtime.Scripts.Volumes;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Sirius.PostProcessing
{
    /// <summary>
    ///     PostProcessをかけるタイミング
    /// </summary>
    public enum PostprocessTiming
    {
        BeforePostprocess,
        AfterPostprocess
    }

    /// <summary>
    /// GodRay（光芒）の核心レンダリング。skybox 実描画 → 二値化 → radial blur（テンポラル本体）→ box filter → composite の
    /// 5 pass を行う。URP 非依存・無状態で、入力は <see cref="Descriptor"/> から受け取る。
    /// `RenderGraph.CreateSkyboxRendererList`/`RasterCommandBuffer.DrawRendererList` は SRP Core API（URP 拡張ではない、検証済）
    /// のため skybox 実描画も剥離可能。テンポラル history ストレージ（per-camera history manager／RTHandle 再確保ユーティリティ）は
    /// URP 固有の機構のため URP グルー（<c>GodRayRenderPass</c>）が担い、結果ハンドルを descriptor 経由で受け取る（部分デカップリング）。
    /// </summary>
    public static class GodRayHelper
    {
        // GodRay シェーダー名（Pass がマテリアル生成・shader 同梱契約で参照）
        public const string BinarizationShaderName = "GodRay/Binarization";
        public const string RadialBlurShaderName = "GodRay/GodRayRadialBlur";

        /// <summary>ラジアルブラーの最大サンプリング数。</summary>
        public static readonly int RadialBlurMaxSample = 64;

        /// <summary>画面外に出た太陽の光芒を消し切るまでの、画面端からの UV 距離</summary>
        private const float OffscreenFadeRange = 0.5f;

        // shader との property 契約（文字列不変）
        internal static class ShaderPropertyIDs
        {
            public static readonly int SceneColorTex = Shader.PropertyToID("_SceneColorTex");
            public static readonly int SampleCount = Shader.PropertyToID("_SampleCount");
            public static readonly int Strength = Shader.PropertyToID("_Strength");
            public static readonly int RadialBlurStartIndex = Shader.PropertyToID("_RadialBlurStartIndex");
            public static readonly int TemporalBlendWeight = Shader.PropertyToID("_TemporalBlendWeight");
            public static readonly int Intensity = Shader.PropertyToID("_Intensity");
            public static readonly int Threshold = Shader.PropertyToID("_Threshold");
            public static readonly int RadialBlurCenter = Shader.PropertyToID("_RadialBlurCenter");
            public static readonly int ViewRayParams = Shader.PropertyToID("_ViewRayParams");
            public static readonly int PhaseParams = Shader.PropertyToID("_PhaseParams");
            public static readonly int RadialParams = Shader.PropertyToID("_RadialParams");
            public static readonly int SkyBoxTex = Shader.PropertyToID("_SkyboxTex");
            public static readonly int BinarizationedTex = Shader.PropertyToID("_BinarizationedTex");
            public static readonly int TemporalRadialBlurTex = Shader.PropertyToID("_TemporalRadialBlurTex");
            public static readonly int RadialBlurTex = Shader.PropertyToID("_RadialBlurTex");
            public static readonly int FinalRadialBlurTex = Shader.PropertyToID("_FinalRadialBlurTex");
            public static readonly int FrameCount = Shader.PropertyToID("_FrameCount");
            public static readonly int GodRayColor = Shader.PropertyToID("_GodRayColor");
            public static readonly int MatrixPrevVp = Shader.PropertyToID("_MatrixPrevVP");
            public static readonly int SrcBlend = Shader.PropertyToID("_SrcBlend");

            // 非 URP 経路の注入口（URP ではグローバル設定されるため未使用）
            public static readonly int CameraDepthTexture = Shader.PropertyToID("_CameraDepthTexture");
            public static readonly int ZBufferParams = Shader.PropertyToID("_ZBufferParams");
            public static readonly int MatrixInvVp = Shader.PropertyToID("unity_MatrixInvVP");
        }

        private enum RadialBlurPass
        {
            RadialBlur,
            BoxFilter,
            CompositeBlend,
            CompositeMultiply,
        }

        /// <summary>AddGodRayPasses の入力一式（URP 型を含まない）。</summary>
        public struct Descriptor
        {
            // カメラのターゲット記述子（等倍）。GodRayDestTex/RadialBlurTex 用の 1/4 解像度 desc の算出元に使う
            public RenderTextureDescriptor cameraTargetDescriptor;

            // 入力カラー
            public TextureHandle cameraColor;

            // skybox 実描画対象のカメラ（CreateSkyboxRendererList に渡す。SRP Core API のため URP 型ではない）
            public Camera camera;

            // テンポラル history ハンドル（previous=読込 / current=書込。glue が ImportTexture 済み）
            public TextureHandle historyPrevious;
            public TextureHandle historyCurrent;

            // 入力深度（cameraDepth）。有効なら _CameraDepthTexture を material へ手動設定する（非 URP 経路向け）。
            // URP では _CameraDepthTexture がグローバル設定されるため未設定（nullHandle）でよい
            public TextureHandle cameraDepth;

            // カメラの _ZBufferParams。非ゼロなら material へ手動設定する（非 URP 経路向け）。
            // URP では _ZBufferParams がグローバル設定されるため未設定（zero）でよい
            public Vector4 zBufferParams;

            // カメラの unity_MatrixInvVP（RadialBlur pass のテンポラル再投影が参照）。非ゼロなら material へ手動設定する（非 URP 経路向け）。
            // URP では unity_MatrixInvVP がグローバル設定されるため未設定（zero）でよい
            public Matrix4x4 matrixInvVp;

            // 前フレームの VP 行列（glue 保持・算出）
            public Matrix4x4 prevFrameVp;

            // ラジアルブラー中心の解決に使う入力（判定・算出は Helper 側で行う）。
            // MainLightの方向を参照する場合は、MainLightの方向から計算する
            public bool referMainLightDirection;
            public Light mainLight; // 主光源（無ければ null。glue が光源データから解決）
            public Vector2 radialBlurCenterFallback; // referMainLightDirection=false or mainLight=null 時に使う値

            public int sampleCount;
            public float strength;
            public float intensity;
            public float threshold;
            public float sunFalloff; // 太陽との角度による光芒の元の減衰（Henyey-Greenstein の g）
            public Vector4 godRayColor; // 描画の色空間で表した色。SetVector で渡すため色空間の変換は glue で済ませておく
            public GodRayBlendMode blendMode;

            // 直前のフレームで太陽が見えず描画をスキップしていた場合 true。履歴が古いので今回のテンポラル合成には混ぜない
            public bool skippedLastFrame;

            // Material は Pass インスタンスが所有（Helper は無状態、desc 経由で受け取る）
            public Material binarizationMaterial;
            public Material radialBlurMaterial;
        }

        /// <summary>
        /// GodRay の描画に使う 1/4 解像度 RenderTextureDescriptor を算出する（URP 非依存の純関数）。
        /// glue（skybox/history 用 RT）と Helper（GodRayDestTex 用 RT）の双方が同じ結果を得るために公開する。
        /// </summary>
        public static RenderTextureDescriptor GetGodRayRenderTextureDescriptor(in RenderTextureDescriptor cameraTargetDescriptor)
        {
            var desc = cameraTargetDescriptor;
            desc.depthBufferBits = (int)DepthBits.None;
            desc.msaaSamples = (int)MSAASamples.None;
            desc.autoGenerateMips = false;
            desc.useMipMap = false;
            // 解像度を1/4にする
            desc.width /= 4;
            desc.height /= 4;
            return desc;
        }

        /// <summary>Additive / Screen は固定機能ブレンドでカメラカラーへ直接合成する（シーンカラーのコピーが要らない）。</summary>
        public static bool IsInPlaceBlend(GodRayBlendMode blendMode)
        {
            return blendMode != GodRayBlendMode.Multiply;
        }

        /// <param name="visibility">光芒の強さに掛ける係数。太陽が画面外に離れるほど 0 に近づき、カメラの背後では 0。</param>
        private static Vector2 CalculateRadialBlurCenter(Light light, Camera camera, out float visibility)
        {
            var lightDirVS = camera.worldToCameraMatrix.MultiplyVector(-light.transform.forward);
            // 方向ベクトルなので w=0 で射影して透視除算する。除算しないと画面端ほど中心がずれ、背後の太陽も画面内に投影される
            var clip = camera.projectionMatrix * new Vector4(lightDirVS.x, lightDirVS.y, lightDirVS.z, 0f);
            if (clip.w <= 0f)
            {
                visibility = 0f;
                return Vector2.zero;
            }

            var center = new Vector2(clip.x / clip.w, clip.y / clip.w) * 0.5f + new Vector2(0.5f, 0.5f);
            // 太陽が背後へ回り込んで中心が無限遠へ飛ぶ前に消し切り、光芒のポップを防ぐ
            var outside = Mathf.Max(Mathf.Max(-center.x, center.x - 1f), Mathf.Max(-center.y, center.y - 1f));
            visibility = Mathf.Clamp01(1f - outside / OffscreenFadeRange);
            return center;
        }

        /// <summary>
        /// ReferMainLightDirection の有無に応じてラジアルブラー中心と可視係数を解決する（URP 非依存）。
        /// glue が光源データから <paramref name="mainLight"/>（主光源が無ければ null）を解決して渡す。
        /// </summary>
        private static Vector2 ResolveRadialBlurCenter(bool referMainLightDirection, Light mainLight, Camera camera, Vector2 fallbackCenter, out float visibility)
        {
            visibility = 1f;
            return referMainLightDirection && mainLight != null
                ? CalculateRadialBlurCenter(mainLight, camera, out visibility)
                : fallbackCenter;
        }

        /// <summary>skybox 実描画 → 二値化 → radial blur(MRT) → box filter → composite を記録する。</summary>
        /// <returns>太陽が見えず何も記録しなかった場合 false。</returns>
        public static bool AddGodRayPasses(this RenderGraph renderGraph, in Descriptor desc, TextureHandle outputTex)
        {
            var radialBlurCenter = ResolveRadialBlurCenter(desc.referMainLightDirection, desc.mainLight, desc.camera, desc.radialBlurCenterFallback, out var visibility);
            if (visibility <= 0f)
            {
                return false;
            }

            var textureDesc = GetGodRayRenderTextureDescriptor(desc.cameraTargetDescriptor);
            // 光芒の結果は輝度の 1 チャンネルしか使わないので、1 チャンネルの half にして帯域を減らす
            var glowDesc = textureDesc;
            if (SystemInfo.IsFormatSupported(GraphicsFormat.R16_SFloat, GraphicsFormatUsage.Render))
            {
                glowDesc.graphicsFormat = GraphicsFormat.R16_SFloat;
            }
            var godRayDestTexHandle = CreateTexture(renderGraph, glowDesc, "GodRayDestTex");
            var radialBlurTexHandle = CreateTexture(renderGraph, textureDesc, "GodRayRadialBlurTex");

            DrawSkyBox(renderGraph, desc.camera, radialBlurTexHandle);
            DrawBinarizationTex(renderGraph, desc, radialBlurCenter, radialBlurTexHandle, godRayDestTexHandle);
            DrawRadialBlurTex(renderGraph, desc, radialBlurCenter, visibility, radialBlurTexHandle, godRayDestTexHandle);
            DrawBoxFilter(renderGraph, desc, radialBlurTexHandle, godRayDestTexHandle);
            DrawFinalComposite(renderGraph, desc, godRayDestTexHandle, outputTex);
            return true;
        }

        private class SkyBoxPassData
        {
            internal RendererListHandle SkyBoxRendererList;
        }

        // skybox の実ジオメトリ描画。RenderGraph.CreateSkyboxRendererList / RasterCommandBuffer.DrawRendererList は
        // SRP Core API（namespace UnityEngine.Rendering.RenderGraphModule、URP 拡張ではない、検証済）のため URP 非依存で書ける
        private static void DrawSkyBox(RenderGraph renderGraph, Camera camera, TextureHandle radialBlurTexHandle)
        {
            using (var builder = renderGraph.AddRasterRenderPass<SkyBoxPassData>("GodRaySkyBoxPass", out var passData))
            {
                passData.SkyBoxRendererList = renderGraph.CreateSkyboxRendererList(camera);

                builder.UseRendererList(passData.SkyBoxRendererList);
                builder.SetRenderAttachment(radialBlurTexHandle, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (SkyBoxPassData data, RasterGraphContext context) =>
                {
                    context.cmd.DrawRendererList(data.SkyBoxRendererList);
                });
            }
        }

        // RenderTextureDescriptor から TextureDesc への変換 + CreateTexture（URP 非依存の RenderGraph テクスチャ生成）
        private static TextureHandle CreateTexture(RenderGraph renderGraph, RenderTextureDescriptor desc, string name)
        {
            var rgDesc = new TextureDesc(desc)
            {
                name = name,
                clearBuffer =  false,
            };
            return renderGraph.CreateTexture(rgDesc);
        }

        private class BinarizationPassData
        {
            internal Material BinarizationMaterial;
            internal TextureHandle RadialBlurTex;
            internal TextureHandle CameraDepth;
        }

        private static void DrawBinarizationTex(RenderGraph renderGraph, in Descriptor desc, in Vector2 radialBlurCenter,
            TextureHandle radialBlurTexHandle, TextureHandle binarizationTex)
        {
            var material = desc.binarizationMaterial;
            material.SetFloat(ShaderPropertyIDs.Threshold, desc.threshold);
            material.SetVector(ShaderPropertyIDs.RadialBlurCenter, radialBlurCenter);
            // ラジアルブラー中心と同じ射影行列から視線を復元し、中心と各ピクセルで UV の取り方を揃える
            var proj = desc.camera.projectionMatrix;
            material.SetVector(ShaderPropertyIDs.ViewRayParams, new Vector4(1f / proj.m00, 1f / proj.m11, proj.m02, proj.m12));
            var g = desc.sunFalloff;
            material.SetVector(ShaderPropertyIDs.PhaseParams, new Vector4((1f - g) * (1f - g), 1f + g * g, 2f * g, 0f));
            if (desc.zBufferParams != Vector4.zero)
            {
                material.SetVector(ShaderPropertyIDs.ZBufferParams, desc.zBufferParams);
            }

            using (var builder = renderGraph.AddRasterRenderPass<BinarizationPassData>("GodRayBinarizationPass",
                       out var passData))
            {
                passData.BinarizationMaterial = desc.binarizationMaterial;
                passData.RadialBlurTex = radialBlurTexHandle;
                passData.CameraDepth = desc.cameraDepth;
                builder.SetRenderAttachment(binarizationTex, 0, AccessFlags.Write);
                builder.UseTexture(radialBlurTexHandle, AccessFlags.Read);
                if (passData.CameraDepth.IsValid())
                {
                    builder.UseTexture(passData.CameraDepth, AccessFlags.Read);
                }
                else
                {
                    builder.UseGlobalTexture(ShaderPropertyIDs.CameraDepthTexture, AccessFlags.Read);
                }
                builder.SetRenderFunc(static (BinarizationPassData data, RasterGraphContext context) =>
                {
                    var radialBlurTex = data.RadialBlurTex;
                    var material = data.BinarizationMaterial;

                    material.SetTexture(ShaderPropertyIDs.SkyBoxTex, radialBlurTex);
                    // 非 URP 経路：注入時のみ手動設定（URP はグローバルの _CameraDepthTexture を使う）
                    if (data.CameraDepth.IsValid())
                    {
                        material.SetTexture(ShaderPropertyIDs.CameraDepthTexture, data.CameraDepth);
                    }
                    Blitter.BlitTexture(context.cmd, radialBlurTex, Vector2.one, material, 0);
                });
            }
        }

        private class RadialBlurPassData
        {
            internal Material RadialBlurMaterial;
            internal TextureHandle TemporalRadialBlurTex;
            internal TextureHandle BinarizedTex;
            internal TextureHandle CameraDepth;
            internal bool SkipTemporalBlend;
        }

        private static void DrawRadialBlurTex(RenderGraph renderGraph, in Descriptor desc, in Vector2 radialBlurCenter, float visibility,
            TextureHandle radialBlurTexHandle, TextureHandle destTexHandle)
        {
            var material = desc.radialBlurMaterial;
            var isSceneViewCamera = desc.camera.cameraType == CameraType.SceneView;
            // テンポラル合成しない場合は 1 フレームの結果がそのまま出るので、ジッタと偶奇を固定してちらつきを防ぐ
            var frameCount = isSceneViewCamera ? 0 : Time.frameCount;
            var skipTemporalBlend = isSceneViewCamera || desc.skippedLastFrame;
            // 黒の履歴を半分混ぜると暗くなるので、テンポラル合成しないときは今回の結果だけを出す
            material.SetFloat(ShaderPropertyIDs.TemporalBlendWeight, skipTemporalBlend ? 0f : 0.5f);
            material.SetInteger(ShaderPropertyIDs.FrameCount, frameCount);
            material.SetInteger(ShaderPropertyIDs.RadialBlurStartIndex, frameCount % 2);
            material.SetInteger(ShaderPropertyIDs.SampleCount, desc.sampleCount);
            material.SetFloat(ShaderPropertyIDs.Strength, desc.strength);
            material.SetFloat(ShaderPropertyIDs.Intensity, desc.intensity * visibility);
            material.SetVector(ShaderPropertyIDs.RadialParams, radialBlurCenter);
            material.SetVector(ShaderPropertyIDs.GodRayColor, desc.godRayColor);
            material.SetMatrix(ShaderPropertyIDs.MatrixPrevVp, desc.prevFrameVp);
            // 非 URP 経路：注入時のみ手動設定（URP はグローバルの _ZBufferParams/unity_MatrixInvVP を使う）
            if (desc.zBufferParams != Vector4.zero)
            {
                material.SetVector(ShaderPropertyIDs.ZBufferParams, desc.zBufferParams);
            }
            if (desc.matrixInvVp != Matrix4x4.zero)
            {
                material.SetMatrix(ShaderPropertyIDs.MatrixInvVp, desc.matrixInvVp);
            }

            using (var builder = renderGraph.AddRasterRenderPass<RadialBlurPassData>("GodRayRadialBlurPass",
                       out var passData))
            {
                passData.RadialBlurMaterial = desc.radialBlurMaterial;
                passData.TemporalRadialBlurTex = desc.historyPrevious;
                passData.BinarizedTex = destTexHandle;
                passData.CameraDepth = desc.cameraDepth;
                // SceneView ガード（テンポラル不変条件5）。camera から取得できるため記録フェーズで直接判定する
                passData.SkipTemporalBlend = skipTemporalBlend;

                builder.SetRenderAttachment(radialBlurTexHandle, 0, AccessFlags.Write);
                builder.SetRenderAttachment(desc.historyCurrent, 1, AccessFlags.Write);
                builder.UseTexture(passData.TemporalRadialBlurTex, AccessFlags.Read);
                builder.UseTexture(passData.BinarizedTex, AccessFlags.Read);
                if (passData.CameraDepth.IsValid())
                {
                    builder.UseTexture(passData.CameraDepth, AccessFlags.Read);
                }
                else
                {
                    builder.UseGlobalTexture(ShaderPropertyIDs.CameraDepthTexture, AccessFlags.Read);
                }
                builder.SetRenderFunc(static (RadialBlurPassData data, RasterGraphContext context) =>
                {
                    var material = data.RadialBlurMaterial;
                    // SceneViewカメラではテンポラルテクスチャがカメラ間で共有されるため、
                    // 別カメラの結果が混入してGodRayがずれる問題を回避する。
                    material.SetTexture(ShaderPropertyIDs.TemporalRadialBlurTex,
                        data.SkipTemporalBlend ? Texture2D.blackTexture : data.TemporalRadialBlurTex);
                    material.SetTexture(ShaderPropertyIDs.BinarizationedTex, data.BinarizedTex);
                    // 非 URP 経路：注入時のみ手動設定（URP はグローバルの _CameraDepthTexture を使う）
                    if (data.CameraDepth.IsValid())
                    {
                        material.SetTexture(ShaderPropertyIDs.CameraDepthTexture, data.CameraDepth);
                    }
                    Blitter.BlitTexture(context.cmd, Vector2.one, material, (int)RadialBlurPass.RadialBlur);
                });
            }
        }

        private class BoxFilterPassData
        {
            internal Material RadialBlurMaterial;
            internal TextureHandle RadialBlurTex;
        }

        private static void DrawBoxFilter(RenderGraph renderGraph, in Descriptor desc, TextureHandle radialBlurTexHandle,
            TextureHandle finalRadialBlurTex)
        {
            using (var builder = renderGraph.AddRasterRenderPass<BoxFilterPassData>("GodRayBoxFilterPass",
                       out var passData))
            {
                passData.RadialBlurMaterial = desc.radialBlurMaterial;
                passData.RadialBlurTex = radialBlurTexHandle;

                builder.SetRenderAttachment(finalRadialBlurTex, 0, AccessFlags.Write);
                builder.UseTexture(radialBlurTexHandle, AccessFlags.Read);
                builder.SetRenderFunc(static (BoxFilterPassData data, RasterGraphContext context) =>
                {
                    var radialBlurTex = data.RadialBlurTex;
                    var material = data.RadialBlurMaterial;
                    material.SetTexture(ShaderPropertyIDs.RadialBlurTex, radialBlurTex);
                    Blitter.BlitTexture(context.cmd, Vector2.one, material, (int)RadialBlurPass.BoxFilter);
                });
            }
        }

        private class CompositePassData
        {
            internal Material CompositeMaterial;
            internal TextureHandle FinalRadialBlurTex;
            internal TextureHandle SceneColorTex;
            internal RadialBlurPass Pass;
            internal BlendMode SrcBlend;
        }

        // IsInPlaceBlend の場合は outputTex（カメラカラー）へ固定機能ブレンドで直接合成し、シーンカラーは読まない
        private static void DrawFinalComposite(RenderGraph renderGraph, in Descriptor desc, TextureHandle finalRadialBlurTex,
            TextureHandle outputTex)
        {
            var inPlace = IsInPlaceBlend(desc.blendMode);

            using (var builder = renderGraph.AddRasterRenderPass<CompositePassData>("GodRayFinalCompositePass",
                       out var passData))
            {
                passData.CompositeMaterial = desc.radialBlurMaterial;
                passData.FinalRadialBlurTex = finalRadialBlurTex;
                passData.SceneColorTex = inPlace ? TextureHandle.nullHandle : desc.cameraColor;
                passData.Pass = inPlace ? RadialBlurPass.CompositeBlend : RadialBlurPass.CompositeMultiply;
                // Screen: G*(1-Dst) + Dst。Additive: G + Dst
                passData.SrcBlend = desc.blendMode == GodRayBlendMode.Screen ? BlendMode.OneMinusDstColor : BlendMode.One;

                if (inPlace)
                {
                    // 既存のカメラカラーにブレンドするためロードさせる（Write だと TBR で DontCare になりうる）
                    builder.SetRenderAttachment(outputTex, 0, AccessFlags.ReadWrite);
                }
                else
                {
                    builder.SetRenderAttachment(outputTex, 0, AccessFlags.Write);
                    builder.UseTexture(passData.SceneColorTex, AccessFlags.Read);
                }
                builder.UseTexture(passData.FinalRadialBlurTex, AccessFlags.Read);
                builder.SetRenderFunc(static (CompositePassData data, RasterGraphContext context) =>
                {
                    var material = data.CompositeMaterial;
                    material.SetTexture(ShaderPropertyIDs.FinalRadialBlurTex, data.FinalRadialBlurTex);
                    if (data.SceneColorTex.IsValid())
                    {
                        material.SetTexture(ShaderPropertyIDs.SceneColorTex, data.SceneColorTex);
                    }
                    material.SetFloat(ShaderPropertyIDs.SrcBlend, (float)data.SrcBlend);
                    Blitter.BlitTexture(context.cmd, Vector2.one, material, (int)data.Pass);
                });
            }
        }
    }
}
