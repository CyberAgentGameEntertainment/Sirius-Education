// --------------------------------------------------------------
// Copyright 2025 CyberAgent, Inc.
// --------------------------------------------------------------

#if HAS_URP
using System;
using Sirius.Core.Runtime.Scripts;
using Sirius.PostProcessing.Runtime.Scripts.Features.Passes;
using Sirius.PostProcessing.Runtime.Scripts.Volumes;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Sirius.PostProcessing.Runtime.Scripts.Features
{
    // allowFlag属性でPassのAllowExecuteと紐つける
    [AttributeUsage(AttributeTargets.Field)]
    public class AllowFlagAttribute : Attribute
    {
        public string BoolFieldName { get; }
        public AllowFlagAttribute(string boolFieldName)
        {
            BoolFieldName = boolFieldName;
        }
    }

    [DisallowMultipleRendererFeature]
    public sealed partial class SiriusPostProcessingFeature : ScriptableRendererFeature
    {
        [SerializeField] private bool allowSSPRPostProcess;
        [SerializeField] private bool allowGodRayPostProcess;
        [SerializeField] private bool allowSSRPostProcess;

        [AllowFlag("allowGodRayPostProcess")]
        private GodRayRenderPass _godRayRenderPass;
        [AllowFlag("allowSSPRPostProcess")]
        private SSPRPostProcessPass _ssprPostProcessPass;
        [AllowFlag("allowSSRPostProcess")]
        private SSRPass _ssrPass;

        private SetupPass _setupPass;

        public override void Create()
        {
            _setupPass = new SetupPass();
            _godRayRenderPass = new GodRayRenderPass();
            _ssrPass = new SSRPass();

            if (SystemInfo.supportsComputeShaders)
                _ssprPostProcessPass = new SSPRPostProcessPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.isPreviewCamera)
                return;

            // カメラの除外チェック
            var ignorerExist = renderingData.cameraData.camera.TryGetComponent<SiriusIgnorer>(out var ignorer);
            if (ignorerExist && ignorer.IgnorePostProcessingRendererFeature)
                return;

            // カメラのポストプロセスが有効な場合のみパスを追加する
            if (!renderingData.cameraData.postProcessEnabled)
                return;

            var volumeStack = VolumeManager.instance.stack;

            // SSPR
            // SceneViewカメラではSSPRをスキップする。
            // SSPRの反射テクスチャがカメラ間で共有されており、SceneViewの結果でGameView用の反射テクスチャが上書きされチラつきが発生するのを防ぐ。
            var enableSSPR = false;
            if (allowSSPRPostProcess && SystemInfo.supportsComputeShaders && !renderingData.cameraData.isSceneViewCamera)
            {
                var ssprVolume = volumeStack.GetComponent<SSPRVolume>();
                if (ssprVolume.IsActive())
                {
                    enableSSPR = true;
                    _ssprPostProcessPass.renderPassEvent = ssprVolume.CreateReflectionTextureTiming.value;
                    renderer.EnqueuePass(_ssprPostProcessPass);
                }
            }

            // GodRay
            if (allowGodRayPostProcess)
            {
                var godRayVolume = volumeStack.GetComponent<GodRayVolume>();
                if (godRayVolume.IsActive())
                {
                    _godRayRenderPass.Setup(godRayVolume.ProcessTiming.value);
                    renderer.EnqueuePass(_godRayRenderPass);
                }
            }

            // SSR
            if (allowSSRPostProcess)
            {
                var ssrVolume = volumeStack.GetComponent<SSRVolume>();
                if (ssrVolume.IsActive())
                {
                    _ssrPass.renderPassEvent = ssrVolume.renderPassEvent.value;
                    renderer.EnqueuePass(_ssrPass);
                }
            }

            // SetupPass
            _setupPass.Setup(enableSSPR, _ssprPostProcessPass);
            renderer.EnqueuePass(_setupPass);
        }

        protected override void Dispose(bool disposing)
        {
            _godRayRenderPass.Cleanup();
            _ssprPostProcessPass.Cleanup();
            _ssrPass.Cleanup();
        }
    }
}
#endif
