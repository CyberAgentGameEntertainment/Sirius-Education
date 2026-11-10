// --------------------------------------------------------------
// Copyright 2025 CyberAgent, Inc.
// --------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Unity.Mathematics;

namespace UnityEngine.Rendering.RenderGraphModule.Util
{
    /// <summary>
    /// URPのRenderGraphUtilsの拡張クラス
    /// </summary>
    public static partial class RenderGraphUtils
    {
        private static readonly Match UnityVersionMatch = Regex.Match(Application.unityVersion, @"^\d+\.\d+\.\d+");
        private static readonly Version UnityVersion = new(UnityVersionMatch.Value);
        // 6000.0.50からAddCopyPassの不具合が修正された。
        // https://issuetracker.unity3d.com/issues/scene-renders-incorrectly-when-using-rendergraphutils-dot-addcopypass
        private static readonly Version IssueFixedVersion = new(6000, 0, 50);

        /// <summary>
        /// URPのAddCopyPassと機能は同じで、不具合を修正したカスタムメソッド
        ///     SourceSliceとDestinationSliceを-1にすることができるように
        /// 注：2DTexturesをCopyする際、SourceSliceとDestinationSliceは0ではなく、-1にすること！
        ///     0にすると、ほとんどの場合正常にCopyできない
        ///----------------------------------------------------------------------------------
        /// コピー元テクスチャからコピー先テクスチャへデータをコピーするパスを追加する。データはピクセル単位でコピーされる。1:1 のコピーのみ可能で、スケーリングや
        /// テクスチャフィルタリングはできない。また、コピー元とコピー先はピクセルサイズと MSAA サンプル数が一致している必要がある。マルチサンプルテクスチャの場合は
        /// サンプルごとにコピーされる。
        ///
        /// タイルベース GPU で最適な性能を出せるよう、フレームバッファフェッチで実装できる範囲に機能を意図的に絞っている。より汎用的な
        /// 処理が必要な場合は AddBlitPass を使うこと。
        ///
        /// XR テクスチャは目ごとに個別にコピーする必要がある。
        ///
        /// MSAA テクスチャの場合は、先に CanAddCopyPassMSAA() で現在のプラットフォームが CopyPass に対応しているか確認すること。
        ///
        /// </summary>
        /// <param name="graph">パスを追加する RenderGraph。</param>
        /// <param name="source">コピー元テクスチャ。</param>
        /// <param name="destination">コピー先テクスチャ。source とは別のテクスチャである必要がある。</param>
        /// <param name="sourceSlice">使用するコピー元の配列 / 3D テクスチャのスライス。通常の 2D テクスチャでは -1 にすること。</param>
        /// <param name="destinationSlice">使用するコピー先の配列 / 3D テクスチャのスライス。通常の 2D テクスチャでは -1 にすること。</param>
        /// <param name="sourceMip">コピー元の先頭ミップレベル。ミップマップなしのテクスチャでは 0、ありの場合は有効なインデックスにすること。</param>
        /// <param name="destinationMip">コピー先の先頭ミップレベル。ミップマップなしのテクスチャでは 0、ありの場合は有効なインデックスにすること。</param>
        /// <param name="passName">デバッグとエラーログ用の名前。RenderGraph デバッガーに表示される。</param>
        /// <param name="file">この関数の呼び出し元ソースファイルの情報。デバッグ用。コンパイラが自動生成するため指定不要。</param>
        /// <param name="line">この関数の呼び出し元ソースファイルの情報。デバッグ用。コンパイラが自動生成するため指定不要。</param>
        public static void AddCopyPassCustom(
            this RenderGraph graph,
            TextureHandle source,
            TextureHandle destination,
            int sourceSlice = -1,
            int destinationSlice = -1,
            int sourceMip = 0,
            int destinationMip = 0,
            string passName = "Copy Pass Utility Custom"
#if !CORE_PACKAGE_DOCTOOLS
            , [CallerFilePath] string file = "",
            [CallerLineNumber] int line = 0
#endif
        )
        {
            // Unity6000.0.50以降は公式のAddCopyPassを使用する
            if (UnityVersion >= IssueFixedVersion)
            {
                graph.AddCopyPass(source, destination, sourceSlice, destinationSlice, sourceMip, destinationMip, passName);
                return;
            }

            if (!graph.nativeRenderPassesEnabled)
                throw new ArgumentException("CopyPass only supported for native render pass. Please use the blit functions instead for non native render pass platforms.");

            var sourceDesc = graph.GetTextureDesc(source);
            var destinationDesc = graph.GetTextureDesc(destination);

            if (sourceSlice < -1 || sourceSlice >= sourceDesc.slices)
                throw new ArgumentException("Invalid sourceSlice.");

            if (destinationSlice < -1 || destinationSlice >= destinationDesc.slices)
                throw new ArgumentException("Invalid destinationSlice.");

            int sourceMaxWidth = math.max(math.max(sourceDesc.width, sourceDesc.height), sourceDesc.slices);
            int sourceTotalMipChainLevels = (int)math.log2(sourceMaxWidth) + 1;
            if (sourceMip < 0 || sourceMip >= sourceTotalMipChainLevels)
                throw new ArgumentException("Invalid sourceMip.");

            int destinationMaxWidth = math.max(math.max(destinationDesc.width, destinationDesc.height), destinationDesc.slices);
            int destinationTotalMipChainLevels = (int)math.log2(destinationMaxWidth) + 1;
            if (destinationMip < 0 || destinationMip >= destinationTotalMipChainLevels)
                throw new ArgumentException("Invalid destinationMip.");

            if (sourceDesc.msaaSamples != destinationDesc.msaaSamples)
                throw new ArgumentException("MSAA samples from source and destination texture doesn't match.");

            var isMSAA = (int)sourceDesc.msaaSamples > 1;

            // 注意: SV_SampleIndex には ps_4.1 が必要なため、一部プラットフォームでは MSAA のコピーパスに非対応。
            //       コピーシェーダーのパス数で判定できる。
            //       MSAA パスが使えない場合は 1、使える場合は 2 になる。
            //       https://docs.unity3d.com/2017.4/Documentation/Manual/SL-ShaderCompileTargets.html
            //       https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/dx-graphics-hlsl-to-get-sample-position
            if (isMSAA && !Blitter.CanCopyMSAA())
                throw new ArgumentException("Target does not support MSAA for AddCopyPass. Please use the blit alternative or use non MSAA textures.");

            using (var builder = graph.AddRasterRenderPass<CopyPassData>(passName, out var passData, file, line))
            {
                passData.isMSAA = isMSAA;
                builder.SetInputAttachment(source, 0, AccessFlags.Read, sourceMip, sourceSlice);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write, destinationMip, destinationSlice);
                builder.SetRenderFunc((CopyPassData data, RasterGraphContext context) => CopyRenderFunc(data, context));
            }
        }

        /// <summary>
        /// テクスチャをグローバルテクスチャとして設定するパスを記録する。
        /// RenderGraphはフレーム終了時にグローバルテクスチャをリセットするため、呼び出し側で毎フレーム記録する必要がある。
        /// </summary>
        public static void AddSetGlobalTexturePass(this RenderGraph graph, TextureHandle texture, int propertyId)
        {
            using var builder = graph.AddUnsafePass<SetGlobalTexturePassData>("Set Global Texture", out _);
            builder.UseTexture(texture);
            builder.SetGlobalTextureAfterPass(texture, propertyId);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc((SetGlobalTexturePassData _, UnsafeGraphContext __) => { });
        }

        private class SetGlobalTexturePassData { }
    }
}
