// --------------------------------------------------------------
// Copyright 2026 CyberAgent, Inc.
// --------------------------------------------------------------

#if HAS_URP
using Sirius.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Sirius.PostProcessing.Runtime.Scripts.Features.Passes
{
    public class SSRPass : ScriptableRenderPass, IAllowExecute
    {
        public static string[] UsingShaderNameList { get; } =
        {
            "Hidden/Sirius/SSR",
            "Hidden/Sirius/DepthMipMapUtil",
        };

        public bool AllowExecute { get; set; }

        private TextureHandle _diffuseTexHandle;
        private TextureHandle _smoothTexHandle;
        private TextureHandle _metallicTexHandle;
        private TextureHandle _aoTexHandle;
        private int _smoothTexChannel;
        private int _metallicTexChannel;
        private int _aoTexChannel;

        private SSRVolume _ssrVolume;

        private Shader _ssrShader;
        private Shader SSRShader
        {
            get
            {
                if (_ssrShader == null)
                {
                    _ssrShader = Shader.Find(UsingShaderNameList[0]);
                }
                return _ssrShader;
            }
        }

        private Material _ssrMaterial;
        private Material SSRMaterial
        {
            get
            {
                if (_ssrMaterial == null)
                {
                    _ssrMaterial = CoreUtils.CreateEngineMaterial(SSRShader);
                }
                return _ssrMaterial;
            }
        }

        // デバッグ
        private Texture2D _debugDiffuseTex;
        private Texture2D _debugGBuffer;
        private RTHandle _debugDiffuseRTHandle;
        private RTHandle _debugGBufferRTHandle;
        private const int DebugSmoothnessChannel = 0;
        private const int DebugMetallicChannel = 1;
        private const int DebugAOChannel = 2;
        private const int DebugTexSize = 4;
        private Color[] _debugDiffusePixels;
        private Color[] _debugGBufferPixels;

        public SSRPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
        }

        public void Cleanup()
        {
            CoreUtils.Destroy(_ssrMaterial);
            _ssrMaterial = null;
            _debugDiffuseRTHandle?.Release();
            _debugDiffuseRTHandle = null;
            _debugGBufferRTHandle?.Release();
            _debugGBufferRTHandle = null;
            CoreUtils.Destroy(_debugDiffuseTex);
            _debugDiffuseTex = null;
            CoreUtils.Destroy(_debugGBuffer);
            _debugGBuffer = null;
        }

        // デバッグ用
        private void UpdateDebugTextures()
        {
            if (_debugDiffuseTex == null)
            {
                _debugDiffuseTex = new Texture2D(DebugTexSize, DebugTexSize, TextureFormat.RGB24, false);
                _debugDiffuseRTHandle?.Release();
                _debugDiffuseRTHandle = RTHandles.Alloc(_debugDiffuseTex);
            }
            _debugDiffusePixels ??= new Color[DebugTexSize * DebugTexSize];
            for (var i = 0; i < _debugDiffusePixels.Length; i++)
            {
                _debugDiffusePixels[i] = _ssrVolume.debugDiffuseColor.value;
            }
            _debugDiffuseTex.SetPixels(_debugDiffusePixels);
            _debugDiffuseTex.Apply();

            if (_debugGBuffer == null)
            {
                _debugGBuffer = new Texture2D(DebugTexSize, DebugTexSize, TextureFormat.RGB24, false);
                _debugGBufferRTHandle?.Release();
                _debugGBufferRTHandle = RTHandles.Alloc(_debugGBuffer);
            }
            _debugGBufferPixels ??= new Color[DebugTexSize * DebugTexSize];
            for (var i = 0; i < _debugGBufferPixels.Length; i++)
            {
                _debugGBufferPixels[i] = new Color(_ssrVolume.debugSmoothness.value, _ssrVolume.debugMetallic.value,
                    _ssrVolume.debugAO.value, 0.0f);
            }
            _debugGBuffer.SetPixels(_debugGBufferPixels);
            _debugGBuffer.Apply();
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            _ssrVolume = VolumeManager.instance.stack.GetComponent<SSRVolume>();
            if (_ssrVolume == null || !_ssrVolume.enabled.value)
            {
                return;
            }

            var resourceData = frameData.Get<UniversalResourceData>();
            var depthMipMapData = frameData.GetOrCreateDepthMipMapData(renderGraph);

            TextureHandle diffuseTexHandle;
            TextureHandle smoothTexHandle;
            TextureHandle metallicTexHandle;
            TextureHandle aoTexHandle;

            // デバッグモード時はDebugテクスチャを外部設定より優先して正式入力として流す
            if (_ssrVolume.debugMode.value)
            {
                UpdateDebugTextures();
                diffuseTexHandle = renderGraph.ImportTexture(_debugDiffuseRTHandle);
                var debugGBufferHandle = renderGraph.ImportTexture(_debugGBufferRTHandle);
                smoothTexHandle = debugGBufferHandle;
                metallicTexHandle = debugGBufferHandle;
                aoTexHandle = debugGBufferHandle;
                _smoothTexChannel = DebugSmoothnessChannel;
                _metallicTexChannel = DebugMetallicChannel;
                _aoTexChannel = DebugAOChannel;
            }
            else
            {
                // 外部から設定されてなければ、URPのGBufferを使用
                if (!_diffuseTexHandle.IsValid())
                {
                    SetDiffuseTexture(resourceData.gBuffer[0]);
                }
                if (!_smoothTexHandle.IsValid())
                {
                    SetSmoothTexture(resourceData.gBuffer[2], 3);
                }
                if (!_metallicTexHandle.IsValid())
                {
                    SetMetallicTexture(resourceData.gBuffer[1], 0);
                }
                if (!_aoTexHandle.IsValid())
                {
                    SetAOTexture(resourceData.gBuffer[1], 3);
                }

                diffuseTexHandle = _diffuseTexHandle;
                smoothTexHandle = _smoothTexHandle;
                metallicTexHandle = _metallicTexHandle;
                aoTexHandle = _aoTexHandle;
            }

            var desc = new SSRHelper.Descriptor
            {
                SSRMaterial = SSRMaterial,
                HalfStartDepthMipMap = depthMipMapData.HalfStartDepthMipMap,
                MipMapLevelCount = depthMipMapData.MipMapLevelCount,
                CameraDepthTexture = resourceData.cameraDepthTexture,
                CameraNormalsTexture = resourceData.cameraNormalsTexture,
                DiffuseTexture = diffuseTexHandle,
                SmoothTexture = smoothTexHandle,
                MetallicTexture = metallicTexHandle,
                AOTexture = aoTexHandle,
                SmoothChannel = _smoothTexChannel,
                MetallicChannel = _metallicTexChannel,
                AOChannel = _aoTexChannel,
                DownSample = _ssrVolume.downSample.value,
                MaxSteps = _ssrVolume.maxSteps.value,
                HiZTraceStartLevel = _ssrVolume.hiZTraceStartLevel.value,
                HiZTraceMaxLevel = _ssrVolume.hiZTraceMaxLevel.value,
                MinSmoothness = _ssrVolume.minSmoothness.value,
                ReflectSky = _ssrVolume.reflectSky.value,
                DitherMode = (int)_ssrVolume.ditherMode.value,
                DitherTarget = (int)_ssrVolume.ditherTarget.value,
            };
            renderGraph.AddSSRPasses(desc, resourceData.activeColorTexture);
        }

        public void SetDiffuseTexture(TextureHandle texHandle)
        {
            _diffuseTexHandle = texHandle;
        }

        public void SetSmoothTexture(TextureHandle texHandle, int channel)
        {
            _smoothTexHandle = texHandle;
            _smoothTexChannel = channel;
        }

        public void SetMetallicTexture(TextureHandle texHandle, int channel)
        {
            _metallicTexHandle = texHandle;
            _metallicTexChannel = channel;
        }

        public void SetAOTexture(TextureHandle texHandle, int channel)
        {
            _aoTexHandle = texHandle;
            _aoTexChannel = channel;
        }
    }
}
#endif
