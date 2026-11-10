// --------------------------------------------------------------
// Copyright 2026 CyberAgent, Inc.
// --------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

namespace Sirius.Core
{
    public static class DualBlurHelper
    {
        // mip数計算の基準にする辺
        public enum MipCountBasis
        {
            MaxDimension = 0, // 幅・高さの大きい方
            Width,
            Height
        }

        private static class ShaderPropertyIDs
        {
            public static readonly int SourceTex = Shader.PropertyToID("_SourceTex");
            public static readonly int SourceTexLowMip = Shader.PropertyToID("_SourceTexLowMip");
        }

        private const int MaxPyramidSize = 14;

        private class PassData
        {
            public Material Material;
            public int MipCount;
            public int DownSamplePassIndex;
            public int UpSamplePassIndex;
            public TextureHandle[] BlurMipDowns;
            public TextureHandle[] BlurMipUps;
            public Action<CommandBuffer, Vector4> BeforeBlurCallback;
            public Vector4 BeforeBlurCallbackArg;
        }

        // prefix ごとのテクスチャ名・パス名キャッシュ（録画時の毎フレーム文字列生成を避ける）
        private static readonly Dictionary<string, (string[] Downs, string[] Ups, string BlurPass, string CopyPass)> NameCache = new();

        private static (string[] Downs, string[] Ups, string BlurPass, string CopyPass) GetNames(string prefix)
        {
            prefix ??= "";
            if (!NameCache.TryGetValue(prefix, out var names))
            {
                var downs = new string[MaxPyramidSize];
                var ups = new string[MaxPyramidSize];
                for (var i = 0; i < MaxPyramidSize; i++)
                {
                    downs[i] = prefix + "MipDown_" + i.ToString();
                    ups[i] = prefix + "MipUp_" + i.ToString();
                }
                names = (downs, ups, prefix + "BlurPass", prefix + "CopyPass");
                NameCache.Add(prefix, names);
            }
            return names;
        }

        public struct Descriptor
        {
            public Material Material;
            public TextureHandle SourceTexture;
            public int MaxMipCount;
            public MipCountBasis MipCountBasis;
            public int DownSamplePassIndex;
            public int UpSamplePassIndex;
            public string PassNamePrefix;
            // 実行時、ブラー開始前に呼ばれる（グローバルデータセット等用）。
            public Action<CommandBuffer, Vector4> BeforeBlurCallback;
            public Vector4 BeforeBlurCallbackArg;
        }

        public static void AddDualBlurPass(this RenderGraph renderGraph, in Descriptor desc, TextureHandle outputTarget)
        {
            var sourceDesc = desc.SourceTexture.GetDescriptor(renderGraph);
            sourceDesc.useMipMap = false;
            sourceDesc.autoGenerateMips = false;
            sourceDesc.msaaSamples = MSAASamples.None;
            var baseSize = desc.MipCountBasis switch
            {
                MipCountBasis.Width => sourceDesc.width,
                MipCountBasis.Height => sourceDesc.height,
                _ => Mathf.Max(sourceDesc.width, sourceDesc.height)
            };
            var mipCount = Mathf.FloorToInt(Mathf.Log(baseSize, 2f) - 1);
            mipCount = Mathf.Clamp(mipCount, 1, desc.MaxMipCount);
            var names = GetNames(desc.PassNamePrefix);
            if (mipCount == 1)
            {
                // mip Countが1の場合はブラー処理が行われないので、結果テクスチャにコピーのみを行う。
                renderGraph.AddBlitPass(desc.SourceTexture, outputTarget,
                    Vector2.one, Vector2.zero, passName: names.CopyPass);
                return;
            }

            using var builder = renderGraph.AddUnsafePass<PassData>(names.BlurPass, out var passData);

            // PassData は RenderGraph の pool で再利用されるため、配列の確保は各インスタンス初回のみ
            passData.BlurMipDowns ??= new TextureHandle[MaxPyramidSize];
            passData.BlurMipUps ??= new TextureHandle[MaxPyramidSize];
            passData.Material = desc.Material;
            passData.MipCount = mipCount;
            passData.DownSamplePassIndex = desc.DownSamplePassIndex;
            passData.UpSamplePassIndex = desc.UpSamplePassIndex;
            passData.BlurMipDowns[0] = desc.SourceTexture;
            passData.BlurMipUps[0] = outputTarget;
            passData.BeforeBlurCallback = desc.BeforeBlurCallback;
            passData.BeforeBlurCallbackArg = desc.BeforeBlurCallbackArg;

            for (var i = 1; i < mipCount; i++)
            {
                sourceDesc.width = Mathf.Max(1, sourceDesc.width >> 1);
                sourceDesc.height = Mathf.Max(1, sourceDesc.height >> 1);
                sourceDesc.name = names.Ups[i];
                passData.BlurMipUps[i] = renderGraph.CreateTexture(sourceDesc);
                sourceDesc.name = names.Downs[i];
                passData.BlurMipDowns[i] = renderGraph.CreateTexture(sourceDesc);
            }

            for (var i = 0; i < mipCount; i++)
            {
                builder.UseTexture(passData.BlurMipDowns[i], AccessFlags.ReadWrite);
                builder.UseTexture(passData.BlurMipUps[i], AccessFlags.ReadWrite);
            }

            builder.AllowGlobalStateModification(true);
            builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
            {
                var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                var material = data.Material;
                var mipCount = data.MipCount;

                data.BeforeBlurCallback?.Invoke(cmd, data.BeforeBlurCallbackArg);

                // ダウンサンプリング
                for (var i = 1; i < mipCount; i++)
                {
                    CustomBlit(cmd, data.BlurMipDowns[i - 1], data.BlurMipDowns[i],
                        material, data.DownSamplePassIndex);
                }

                // アップサンプリング
                for (var i = mipCount - 2; i >= 0; i--)
                {
                    var lowMip = i == mipCount - 2 ? data.BlurMipDowns[i + 1] : data.BlurMipUps[i + 1];
                    var highMip = data.BlurMipDowns[i];
                    var dst = data.BlurMipUps[i];
                    cmd.SetGlobalTexture(ShaderPropertyIDs.SourceTexLowMip, lowMip);
                    CustomBlit(cmd, highMip, dst, material, data.UpSamplePassIndex);
                }
            });
        }

        private static void CustomBlit(CommandBuffer cmd, RTHandle source, RTHandle destination, Material material, int passIndex)
        {
            cmd.SetGlobalTexture(ShaderPropertyIDs.SourceTex, source.nameID);
            Blitter.BlitCameraTexture(cmd, source, destination, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                material, passIndex);
        }
    }
}
