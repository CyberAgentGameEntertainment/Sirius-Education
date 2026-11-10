#if HAS_URP
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Sirius.PostProcessing.Runtime.Scripts.Features.Passes
{
    /// <summary>
    /// URPのTaaHistory、RawDepthHistory、RawColorHistory、UniversalCameraHistoryあたりを参考にしてGodRayBlurHistoryを作成
    /// </summary>
    public class GodRayBlurHistory : CameraHistoryItem
    {
        private const string Name = "GodRayHistoryTex";
        private int _id;

        private RenderTextureDescriptor _descriptor;
        private Hash128 _descKey;

        public override void OnCreate(BufferedRTHandleSystem owner, uint typeId)
        {
            base.OnCreate(owner, typeId);
            _id = MakeId(0);
        }

        public override void Reset()
        {
            ReleaseHistoryFrameRT(_id);
            _descriptor.width = 0;
            _descriptor.height = 0;
            _descriptor.graphicsFormat = GraphicsFormat.None;
            _descKey = Hash128.Compute(0);
        }

        /// <summary>
        /// 現在のヒストリーテクスチャを取得する。
        /// 書き込むレンダーパスの実行後にのみ有効で、それまでは無効な場合がある。
        /// </summary>
        /// <param name="eyeIndex">目のインデックス。通常は XRPass.multipassId。</param>
        /// <returns>テクスチャ。</returns>
        public RTHandle GetCurrentTexture()
        {
            return GetCurrentFrameRT(_id);
        }

        /// <summary>
        /// 前フレームのヒストリーテクスチャを取得する。
        /// 書き込むレンダーパスの実行後にのみ有効で、それまでは無効な場合がある。
        /// </summary>
        /// <param name="eyeIndex">目のインデックス。通常は XRPass.multipassId。</param>
        /// <returns>テクスチャ。</returns>
        public RTHandle GetPreviousTexture()
        {
            return GetPreviousFrameRT(_id);
        }

        private bool IsAllocated()
        {
            return GetCurrentTexture() != null;
        }

        // desc（graphicsFormat など）が変わった場合に true
        private bool IsDirty(ref RenderTextureDescriptor desc)
        {
            return _descKey != Hash128.Compute(ref desc);
        }

        private void Alloc(ref RenderTextureDescriptor desc)
        {
            // 一般には現在のテクスチャがまだ書き込まれていない可能性があるため、ダブルバッファリングが必要
            // フレームごとに _0/_1 サフィックス付きの名前を付けるため、AllocHistoryFrameRT ではなく allocator 版 AllocBuffer を使う
            var d = desc;
            storage.AllocBuffer(_id, (rtHandleSystem, frameIndex) =>
                rtHandleSystem.Alloc(
                    d.width, d.height,
                    format: d.graphicsFormat,
                    filterMode: FilterMode.Bilinear,
                    wrapMode: TextureWrapMode.Clamp,
                    name: $"{Name}_{frameIndex}"),
                2);
            _descriptor = desc;
            _descKey = Hash128.Compute(ref desc);
        }

        internal RenderTextureDescriptor GetHistoryDescriptor(ref RenderTextureDescriptor cameraDesc)
        {
            var colorDesc = cameraDesc;
            colorDesc.depthBufferBits = (int)DepthBits.None;
            colorDesc.msaaSamples = (int)MSAASamples.None;
            colorDesc.useMipMap = false;
            colorDesc.autoGenerateMips = false;

            return colorDesc;
        }

        // RTHandle を再確保した場合に true を返す
        internal bool Update(ref RenderTextureDescriptor desc)
        {
            if (desc is { width: > 0, height: > 0 } && desc.graphicsFormat != GraphicsFormat.None)
            {
                var historyDesc = GetHistoryDescriptor(ref desc);

                if (IsDirty(ref historyDesc))
                    Reset();

                if (!IsAllocated())
                {
                    Alloc(ref historyDesc);
                    return true;
                }
            }

            return false;
        }
    }
}
#endif
