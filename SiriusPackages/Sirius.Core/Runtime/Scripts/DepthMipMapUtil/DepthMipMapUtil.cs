// --------------------------------------------------------------
// Copyright 2026 CyberAgent, Inc.
// --------------------------------------------------------------

using Sirius.Core.Runtime.Scripts;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Sirius.Core
{
    public static class DepthMipMapUtil
    {
        // 後方互換のための転送（実体は DepthMipMapHelper）
        public const int MaxMipCount = DepthMipMapHelper.MaxMipCount;
        public const string DepthPyramidFullSizeKeyword = DepthMipMapHelper.DepthPyramidFullSizeKeyword;

        public static readonly int FullSizeDepthTextureID = DepthMipMapHelper.FullSizeDepthTextureID;
        public static readonly int HalfStartDepthMipMapID = DepthMipMapHelper.HalfStartDepthMipMapID;

        public static void BindFullSizeDepthTexture(Material material, TextureHandle fullSizeDepthTex)
        {
            DepthMipMapHelper.BindFullSizeDepthTexture(material, fullSizeDepthTex);
        }

        public static void BindHalfStartDepthMipMap(Material material, TextureHandle halfStartDepthMips)
        {
            DepthMipMapHelper.BindHalfStartDepthMipMap(material, halfStartDepthMips);
        }

        public static void SetDepthPyramidFullSizeKeyword(Material material, bool state)
        {
            DepthMipMapHelper.SetDepthPyramidFullSizeKeyword(material, state);
        }

        // DepthMipMapが必要な時に実行する。初回実行時のみ生成Passを挿入する
        public static DepthMipMapData GetOrCreateDepthMipMapData(this ContextContainer frameData, RenderGraph renderGraph)
        {
            var depthMipMapData = frameData.GetOrCreate<DepthMipMapData>();
            if (!depthMipMapData.IsValid)
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                var srcDepth = resourceData.cameraDepthTexture;
                Debug.Assert(srcDepth.IsValid(), "cameraDepthTextureが無効。消費パスでConfigureInput(Depth)を指定すること");

                var halfStartDepthMipMap = DepthMipMapHelper.CreateHalfStartDepthMipMap(renderGraph, srcDepth);

                var desc = new DepthMipMapHelper.Descriptor
                {
                    Material = DepthMipMapHelper.DepthMipMapMaterial,
                    PassIndex = 0,
                    SrcDepth = srcDepth,
                    MaxMipCount = MaxMipCount,
                };
                renderGraph.AddGenerateDepthMipMapPass(desc, halfStartDepthMipMap, out var mipCount);

                depthMipMapData.FullSizeDepthTexture = srcDepth;
                depthMipMapData.HalfStartDepthMipMap = halfStartDepthMipMap;
                depthMipMapData.MipMapLevelCount = mipCount;
            }
            return depthMipMapData;
        }

        // renderGraphからでも呼べられるように
        public static DepthMipMapData GetOrCreateDepthMipMapData(this RenderGraph renderGraph, ContextContainer frameData)
        {
            return frameData.GetOrCreateDepthMipMapData(renderGraph);
        }
    }
}
