// --------------------------------------------------------------
// Copyright 2026 CyberAgent, Inc.
// --------------------------------------------------------------

#if HAS_URP
using System;
using Sirius.Core.Runtime.Scripts;
using Sirius.PostProcessing.Runtime.Scripts.Volumes;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Sirius.PostProcessing.Runtime.Scripts.Features.Passes
{
    /// <summary>
    ///     GodRay の URP 胶水。核心レンダリング（skybox 実描画 → 二値化 → radial blur → box filter → composite）は
    ///     URP 非依存の <see cref="GodRayHelper.AddGodRayPasses"/> に委譲し、本クラスは URP 固有の処理
    ///     （テンポラル history ストレージ・前フレーム VP 算出・Volume/LightData 解決）を担う。
    /// </summary>
    public class GodRayRenderPass : ScriptableRenderPass, IAllowExecute
    {
        public static string[] UsingShaderNameList { get; } =
        {
            GodRayHelper.BinarizationShaderName,
            GodRayHelper.RadialBlurShaderName
        };

        public bool AllowExecute { get; set; }

        // マテリアル
        private Material _binarizationMaterial;
        private Material _radialBlurMaterial;

        private Material BinarizationMaterial
        {
            get
            {
                if (_binarizationMaterial == null)
                {
                    _binarizationMaterial = CoreUtils.CreateEngineMaterial(UsingShaderNameList[0]);
                }
                return _binarizationMaterial;
            }
        }

        private Material RadialBlurMaterial
        {
            get
            {
                if (_radialBlurMaterial == null)
                {
                    _radialBlurMaterial = CoreUtils.CreateEngineMaterial(UsingShaderNameList[1]);
                }
                return _radialBlurMaterial;
            }
        }

        /// <remarks>
        ///     同一の描画パスでテクスチャとレンダリングターゲットとして利用しているため、ダブルバッファにする。
        ///     テクスチャ枚数削減のため、バックバッファ側はスカイボックスの描画結果も格納している。
        /// </remarks>
        private readonly RTHandle[] _tempRadialBlurRTHandles = new RTHandle[2];

        /// <summary>直前のフレームのビュープロジェクション行列</summary>
        private Matrix4x4 _prevFrameVpMatrix;

        /// <summary>直前のフレームで太陽が見えず描画をスキップしたか</summary>
        private bool _skippedLastFrame;

        // Volume
        private GodRayVolume _radialBlurVolume;

        /// <summary>
        ///     セットアップ処理
        /// </summary>
        public void Setup(PostprocessTiming timing)
        {
            renderPassEvent = GetRenderPassEvent(timing);
        }

        public void Cleanup()
        {
            Array.ForEach(_tempRadialBlurRTHandles, x => x?.Release());
            CoreUtils.Destroy(_binarizationMaterial);
            CoreUtils.Destroy(_radialBlurMaterial);
            _binarizationMaterial = null;
            _radialBlurMaterial = null;
        }

        /// <summary>
        ///     RenderPassの描画タイミングの取得
        /// </summary>
        private static RenderPassEvent GetRenderPassEvent(PostprocessTiming postprocessTiming)
        {
            switch (postprocessTiming)
            {
                case PostprocessTiming.BeforePostprocess:
                    return RenderPassEvent.BeforeRenderingPostProcessing;
                case PostprocessTiming.AfterPostprocess:
                    return RenderPassEvent.AfterRendering;
                default:
                    throw new ArgumentOutOfRangeException(nameof(postprocessTiming), postprocessTiming, null);
            }
        }

        private readonly ProfilingSampler _profilingSampler = new("Sirius.GodRay");
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            _radialBlurVolume = VolumeManager.instance.stack.GetComponent<GodRayVolume>();
            using (new CTRenderGraphProfilingScope(renderGraph, _profilingSampler))
            {
                RenderGodRay(renderGraph, frameData);
            }
        }

        /// <summary>
        ///     テンポラル history のハンドルを解決する（historyManager 経由 or SceneView 等の手動 fallback）。
        ///     glue が per-camera 永続ストレージのライフサイクルを管理する（剥離困難、部分デカップリング）。
        /// </summary>
        private void ResolveHistoryHandles(RenderGraph renderGraph, ContextContainer frameData,
            ref RenderTextureDescriptor desc, bool isSceneViewCamera,
            out TextureHandle historyPrevious, out TextureHandle historyCurrent)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var historyManager = cameraData.historyManager;

            RTHandle previousBlurRTHandle;
            RTHandle currentBlurRTHandle;

            // SceneViewカメラではテンポラル蓄積をスキップするため、historyManagerを使わず手動でテクスチャを確保する。
            // historyManagerがない場合（SceneView等）も同様。
            if (historyManager != null && !isSceneViewCamera)
            {
                historyManager.RequestAccess<GodRayBlurHistory>();
                var godRayBlurHistory = historyManager.GetHistoryForWrite<GodRayBlurHistory>();
                godRayBlurHistory.Update(ref desc);
                previousBlurRTHandle = godRayBlurHistory.GetPreviousTexture();
                currentBlurRTHandle = godRayBlurHistory.GetCurrentTexture();
            }
            else
            {
                for (var i = 0; i < _tempRadialBlurRTHandles.Length; i++)
                {
                    RenderingUtils.ReAllocateHandleIfNeeded(ref _tempRadialBlurRTHandles[i], desc, FilterMode.Bilinear,
                        TextureWrapMode.Clamp, name: $"GodRayHistoryTex_{i}");
                }

                // テンポーラルテクスチャのフロントバッファとバックバッファの番号
                var temporalTextureFrontNo = Time.frameCount % 2;
                var temporalTextureBackNo = temporalTextureFrontNo == 0 ? 1 : 0;
                previousBlurRTHandle = _tempRadialBlurRTHandles[temporalTextureFrontNo];
                currentBlurRTHandle = _tempRadialBlurRTHandles[temporalTextureBackNo];
            }

            var importParams = new ImportResourceParams
            {
                clearOnFirstUse = false,
                discardOnLastUse = false
            };
            historyPrevious = renderGraph.ImportTexture(previousBlurRTHandle, importParams);
            historyCurrent = renderGraph.ImportTexture(currentBlurRTHandle, importParams);
        }

        private void RenderGodRay(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var lightData = frameData.Get<UniversalLightData>();
            var resourceData = frameData.Get<UniversalResourceData>();
            var isSceneViewCamera = cameraData.cameraType == CameraType.SceneView;

            var godRayHistoryDesc = GodRayHelper.GetGodRayRenderTextureDescriptor(cameraData.cameraTargetDescriptor);

            // テンポラル history ストレージ（剥離困難、glue 維持）
            ResolveHistoryHandles(renderGraph, frameData, ref godRayHistoryDesc, isSceneViewCamera,
                out var historyPrevious, out var historyCurrent);

            // ラジアルブラー中心の解決に使う主光源（判定・算出は Helper 側で行う）
            var mainLight = lightData.mainLightIndex != -1 ? lightData.visibleLights[lightData.mainLightIndex].light : null;

            var desc = new GodRayHelper.Descriptor
            {
                cameraTargetDescriptor =  cameraData.cameraTargetDescriptor,
                cameraColor = resourceData.activeColorTexture,
                camera = cameraData.camera,
                historyPrevious = historyPrevious,
                historyCurrent = historyCurrent,
                prevFrameVp = _prevFrameVpMatrix,
                referMainLightDirection = _radialBlurVolume.ReferMainLightDirection.value,
                mainLight = mainLight,
                radialBlurCenterFallback = _radialBlurVolume.RadialBlurCenter.value,
                sampleCount = _radialBlurVolume.SampleCount.value,
                strength = _radialBlurVolume.Strength.value,
                intensity = _radialBlurVolume.Intensity.value,
                threshold = _radialBlurVolume.Threshold.value,
                sunFalloff = _radialBlurVolume.SunFalloff.value,
                godRayColor = GetGodRayColor(lightData),
                blendMode = _radialBlurVolume.BlendMode.value,
                skippedLastFrame = _skippedLastFrame,
                binarizationMaterial = BinarizationMaterial,
                radialBlurMaterial = RadialBlurMaterial,
            };

            // 加算・スクリーンはカメラカラーへ直接合成するので、結果用のテクスチャは要らない
            var godRayResultTex = resourceData.activeColorTexture;
            if (!GodRayHelper.IsInPlaceBlend(desc.blendMode))
            {
                var godRayDesc = resourceData.activeColorTexture.GetDescriptor(renderGraph);
                godRayDesc.depthBufferBits = DepthBits.None;
                godRayDesc.msaaSamples = MSAASamples.None;
                godRayDesc.clearBuffer = false;
                godRayDesc.name = "GodRayResultTex";
                godRayResultTex = renderGraph.CreateTexture(godRayDesc);
            }

            var rendered = renderGraph.AddGodRayPasses(desc, godRayResultTex);
            _skippedLastFrame = !rendered;
            if (rendered)
            {
                resourceData.cameraColor = godRayResultTex;
            }

            // SceneViewカメラのVP行列でGameViewのテンポラル再投影が壊れるのを防ぐ
            if (!isSceneViewCamera)
            {
                _prevFrameVpMatrix = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(), true) * cameraData.GetViewMatrix();
            }
        }

        private Vector4 GetGodRayColor(UniversalLightData lightData)
        {
            var color = _radialBlurVolume.GodRayColor.value;
            // MainLight の finalColor は描画の色空間で表されているため、ゴッドレイの色もそちらに揃えてから掛ける
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
                color = color.linear;
            }

            if (_radialBlurVolume.UseMainLightColor.value && lightData.mainLightIndex != -1)
            {
                // 明るさは Intensity で調整するので、ライトの強度は除いて色味だけを使う
                var lightColor = lightData.visibleLights[lightData.mainLightIndex].finalColor;
                var maxComponent = lightColor.maxColorComponent;
                if (maxComponent > 0f)
                {
                    color *= lightColor / maxComponent;
                }
            }

            return color;
        }
    }
}
#endif
