// --------------------------------------------------------------
// Copyright 2026 CyberAgent, Inc.
// --------------------------------------------------------------

#if HAS_URP
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Sirius.PostProcessing.Runtime.Scripts.Features.Passes
{
    /// <summary>
    ///     ポストプロセス用のグローバルパラメータをセットアップするパス
    /// </summary>
    public class SetupPass : ScriptableRenderPass
    {
        private static readonly int CtPrevFrameVpMatrixPropID = Shader.PropertyToID("_CtPrevFrameVpMatrix");
        private bool _enableSSPR;
        private SSPRPostProcessPass _ssprPass;
        /// <summary>直前のフレームのビュープロジェクション行列</summary>
        private Matrix4x4 _prevFrameVpMatrix;
        public SetupPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRendering;
        }

        public void Setup(bool enableSSPR, SSPRPostProcessPass ssprPass)
        {
            _enableSSPR = enableSSPR;
            _ssprPass = ssprPass;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                       "Sirius.PostProcess.Setup Pass", out var passData))
            {
                passData.EnableSSPR = _enableSSPR;
                passData.PrevFrameVpMatrix = _prevFrameVpMatrix;
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    var cmd = context.cmd;
                    CoreUtils.SetKeyword(context.cmd, "_CT_ENABLE_SSPR", data.EnableSSPR);
                    cmd.SetGlobalMatrix(CtPrevFrameVpMatrixPropID, data.PrevFrameVpMatrix);
                });
            }
            if (_enableSSPR)
            {
                _ssprPass.OnRecordBeforeRendering(renderGraph, frameData);
            }
            // SceneViewカメラではVP行列を更新しない。
            // GameView用の前フレームVP行列がSceneViewで上書きされるのを防ぐ。
            if (cameraData.cameraType != CameraType.SceneView)
            {
                _prevFrameVpMatrix = cameraData.camera.projectionMatrix * cameraData.camera.worldToCameraMatrix;
            }
        }


        private class PassData
        {
            public bool EnableSSPR;
            public Matrix4x4 PrevFrameVpMatrix;
        }
    }
}
#endif
