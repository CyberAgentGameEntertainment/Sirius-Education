// --------------------------------------------------------------
// Copyright 2026 CyberAgent, Inc.
// --------------------------------------------------------------

#if HAS_URP
using Sirius.Core.Runtime.Scripts;
using Sirius.PostProcessing.Runtime.Scripts.Volumes;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Sirius.PostProcessing.Runtime.Scripts.Features.Passes
{
    /// <summary>
    ///     SSPR(Screen Space Planar Reflection)のRenderPass
    /// </summary>
    public class SSPRPostProcessPass : ScriptableRenderPass, IAllowExecute
    {
        public static string[] UsingShaderNameList { get; } =
        {
            "Hidden/Sirius/UtilBlur"
        };
        public bool AllowExecute { get; set; }

        private const string SsprReflectionRTName = "_SSPR_ReflectionRT";
        private readonly ProfilingSampler _profilingSampler;
        private ComputeShader _cs;
        private RTHandle _reflectionRTHandle;
        private GraphicsBuffer _posWSyBuffer;
        private int _posWSyBufferSize;
        private SSPRVolume _volume;

        private static Material _blurMaterial;
        private static Material BlurMaterial
        {
            get
            {
                if (_blurMaterial == null)
                {
                    _blurMaterial = CoreUtils.CreateEngineMaterial("Hidden/Sirius/UtilBlur");
                }
                return _blurMaterial;
            }
        }

        public SSPRPostProcessPass()
        {
            _profilingSampler = new ProfilingSampler("Sirius.ScreenSpacePlanarReflectionPass");
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        }

        private ComputeShader Cs
        {
            get
            {
                if (_cs == null)
                {
                    _cs = Resources.Load<ComputeShader>("SSPRCompute");
                    if (_cs == null)
                    {
                        Debug.LogError("SSPRCompute.compute not found");
                    }
                    else
                    {
                        KernelIDs.Clear = _cs.FindKernel("Clear");
                        KernelIDs.ColorRTDirectResolve = _cs.FindKernel("MobilePathSinglePassColorRTDirectResolve");
                        KernelIDs.FillHoles = _cs.FindKernel("FillHoles");
                    }
                }

                return _cs;
            }
        }

        public void Cleanup()
        {
            _posWSyBuffer?.Dispose();
            _reflectionRTHandle?.Release();
            CoreUtils.Destroy(_blurMaterial);
            _blurMaterial = null;
        }

        private int GetRTHeight()
        {
            return Mathf.CeilToInt(_volume.ReflectionTextureSize.value / (float)SSPRHelper.ShaderNumThreadY) * SSPRHelper.ShaderNumThreadY;
        }

        private int GetRTWidth()
        {
            return GetRTHeight();
        }

        private void SetupPosWSyBuffer()
        {
            var requireBufferSize = GetRTWidth() * GetRTHeight();
            if (_posWSyBuffer == null || _posWSyBufferSize != requireBufferSize)
            {
                _posWSyBuffer?.Dispose();
                _posWSyBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, GetRTWidth() * GetRTHeight(), sizeof(uint));
                _posWSyBufferSize = requireBufferSize;
            }
        }

        private static class KernelIDs
        {
            public static int Clear;
            public static int ColorRTDirectResolve;
            public static int FillHoles;
        }

        // RenderGraph描画の入り口
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (new CTRenderGraphProfilingScope(renderGraph, _profilingSampler))
            {
                InternalRecordRenderGraph(renderGraph, frameData);
            }
        }
        private void InternalRecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();
            _volume = VolumeManager.instance.stack.GetComponent<SSPRVolume>();

            if (!SystemInfo.supportsComputeShaders || !Cs || !resourceData.cameraDepthTexture.IsValid())
                return;

            SetupPosWSyBuffer();

            var rtWidth = GetRTWidth();
            var rtHeight = GetRTHeight();
            var cameraColorTex = resourceData.activeColorTexture;
            var cameraDepthTex = resourceData.cameraDepthTexture;

            // 永続反射RT（跨フレーム参照）の再割当。descはcompute書き込み先RTと同系譜から導出する
            var reflectionDesc = SSPRHelper.GetColorTextureDesc(renderGraph, cameraColorTex, rtWidth, rtHeight);
            reflectionDesc.useMipMap = _volume.GenerateMipMap.value;
            reflectionDesc.autoGenerateMips = _volume.GenerateMipMap.value;
            RenderingUtils.ReAllocateHandleIfNeeded(ref _reflectionRTHandle, reflectionDesc, name: SsprReflectionRTName);

            // Metal では UNITY_MATRIX_VP が正しくないため、独自に計算した VP 行列をコンピュートシェーダーに渡す
            var camera = cameraData.camera;
            var vpMatrix = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(), true) * camera.worldToCameraMatrix;

            var desc = new SSPRHelper.Descriptor
            {
                ComputeShader = Cs,
                ClearKernel = KernelIDs.Clear,
                ColorRTDirectResolveKernel = KernelIDs.ColorRTDirectResolve,
                FillHolesKernel = KernelIDs.FillHoles,
                BlurMaterial = BlurMaterial,
                CameraColorTexture = cameraColorTex,
                CameraDepthTexture = cameraDepthTex,
                PosWSyBuffer = renderGraph.ImportBuffer(_posWSyBuffer),
                RTWidth = rtWidth,
                RTHeight = rtHeight,
                HorizontalPlaneHeightWS = _volume.HorizontalReflectionPlaneHeightWS.value,
                HorizontalPlaneHeightWSBias = _volume.HorizontalReflectionPlaneHeightWSBias.value,
                FadeOutScreenBorderWidthVertical = _volume.FadeOutScreenBorderWidthVertical.value,
                FadeOutScreenBorderWidthHorizontal = _volume.FadeOutScreenBorderWidthHorizontal.value,
                ScreenLRStretchIntensity = _volume.ScreenLRStretchIntensity.value,
                ScreenLRStretchThreshold = _volume.ScreenLRStretchThreshold.value,
                TintColor = _volume.TintColor.value,
                FillHoles = _volume.QualityLevel.value == SSPRVolume.Quality.High,
                BlurIntensity = _volume.BlurIntensity.value,
                BlurWidth = _volume.BlurWidth.value,
                BlurTextureMipCount = _volume.BlurTextureMipCount.value,
                CameraForwardVector = camera.transform.forward,
                CameraPositionWS = camera.transform.position,
                CameraViewProjectionMatrix = vpMatrix,
            };
            renderGraph.AddSSPRPasses(desc, renderGraph.ImportTexture(_reflectionRTHandle));
        }
        /// <summary>
        /// Before Renderingパスので呼ばれる処理。
        /// </summary>
        /// <param name="renderGraph"></param>
        /// <param name="frameData"></param>
        public void OnRecordBeforeRendering(RenderGraph renderGraph, ContextContainer frameData)
        {
            // Render Graphは1フレームの処理の終了時にグローバルテクスチャがリセットされる仕様になっている。
            // そこで、BeforeRenderingのタイミングで1フレーム前の反射テクスチャをグローバルテクスチャとして設定しなおす。
            if (_reflectionRTHandle?.rt == null || !_reflectionRTHandle.rt.IsCreated())
                return;

            SSPRHelper.BindReflectionTexture(renderGraph, renderGraph.ImportTexture(_reflectionRTHandle));
        }
    }
}
#endif
