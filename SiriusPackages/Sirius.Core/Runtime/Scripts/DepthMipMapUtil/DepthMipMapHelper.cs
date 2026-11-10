// --------------------------------------------------------------
// Copyright 2026 CyberAgent, Inc.
// --------------------------------------------------------------

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Sirius.Core
{
    public class DepthMipMapData : ContextItem
    {
        public TextureHandle FullSizeDepthTexture;   // cameraDepthTexture本体(仮想lod0)
        public TextureHandle HalfStartDepthMipMap;   // 半解像度起点のMipMap(仮想lod1以降)
        public int MipMapLevelCount;                 // HalfStartDepthMipMapの実mip数

        public bool IsValid => HalfStartDepthMipMap.IsValid();

        public override void Reset()
        {
            FullSizeDepthTexture = TextureHandle.nullHandle;
            HalfStartDepthMipMap = TextureHandle.nullHandle;
            MipMapLevelCount = 0;
        }
    }

    /// <summary>
    /// 半解像度起点の深度MipMap生成の核心レンダリングとバインドユーティリティ。
    /// URP 非依存で、生成の入力は <see cref="Descriptor"/> から受け取る。
    /// </summary>
    public static class DepthMipMapHelper
    {
        // todo: Shaderをビルドに入れさせる仕組みを用意
        private const string ShaderPath = "Hidden/Sirius/DepthMipMapUtil";
        public const string DepthPyramidFullSizeKeyword = "_CT_DEPTH_PYRAMID_FULLSIZE";

        // 4K目標 → mip数11
        public const int MaxMipCount = 11;

        private static readonly int SrcMipSizeID = Shader.PropertyToID("_SrcMipSize");
        public static readonly int FullSizeDepthTextureID = Shader.PropertyToID("_CT_FullSizeDepthTexture");
        public static readonly int HalfStartDepthMipMapID = Shader.PropertyToID("_CT_HalfStartDepthMipMap");

        private static Material _material;
        public static Material DepthMipMapMaterial
        {
            get
            {
                if (_material == null)
                {
                    _material = CoreUtils.CreateEngineMaterial(ShaderPath);
                }
                return _material;
            }
        }

        public static void Dispose()
        {
            CoreUtils.Destroy(_material);
            _material = null;
        }

        public static TextureHandle CreateHalfStartDepthMipMap(RenderGraph renderGraph, TextureHandle depthTex,
            bool lowPrecision = true, string textureName = "_HalfStartDepthMipMap")
        {
            var desc = renderGraph.GetTextureDesc(depthTex);
            desc.format = lowPrecision ? GraphicsFormat.R16_SFloat : GraphicsFormat.R32_SFloat;
            desc.filterMode = FilterMode.Point;
            desc.wrapMode = TextureWrapMode.Clamp;
            desc.clearBuffer = true;
            // 遠平面のraw深度値でクリアする(reversed-Zでは0、それ以外では1)
            desc.clearColor = SystemInfo.usesReversedZBuffer ? Color.black : Color.white;
            // サイズを半分に
            desc.width = Mathf.Max(desc.width >> 1, 1);
            desc.height = Mathf.Max(desc.height >> 1, 1);

            desc.useMipMap = true;
            desc.autoGenerateMips = false;
            desc.name = textureName;

            return renderGraph.CreateTexture(desc);
        }

        // RenderFunc内でのバインド(handleはPassData経由で渡す)
        public static void BindFullSizeDepthTexture(Material material, TextureHandle fullSizeDepthTex)
        {
            material.SetTexture(FullSizeDepthTextureID, fullSizeDepthTex);
        }

        // HalfStartDepthMipMapのみを読む消費者用(FullSizeのバインドを要求しない)
        public static void BindHalfStartDepthMipMap(Material material, TextureHandle halfStartDepthMips)
        {
            material.SetTexture(HalfStartDepthMipMapID, halfStartDepthMips);
        }

        public static void SetDepthPyramidFullSizeKeyword(Material material, bool state)
        {
            CoreUtils.SetKeyword(material, DepthPyramidFullSizeKeyword, state);
        }

        /// <summary>AddGenerateDepthMipMapPass に渡す入力一式（URP 型を含まない）。</summary>
        public struct Descriptor
        {
            // 生成マテリアル（呼び出し側が所有、desc 経由で渡す）
            public Material Material;
            public int PassIndex;

            // 深度源（呼び出し側で解決して渡す）
            public TextureHandle SrcDepth;
            public int MaxMipCount;
        }

        private class PassData
        {
            public Material Material;
            public int PassIndex;
            public TextureHandle Source;
            public Vector4 SrcSize;
        }

        // desc.SrcDepthを深度源として半解像度起点の深度MipMapを生成する
        public static void AddGenerateDepthMipMapPass(this RenderGraph renderGraph, in Descriptor desc, TextureHandle targetTexture, out int mipCount)
        {
            var srcDesc = renderGraph.GetTextureDesc(desc.SrcDepth);
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Sirius.DownSampleDepth", out var passData))
            {
                passData.Material = desc.Material;
                passData.PassIndex = desc.PassIndex;
                passData.Source = desc.SrcDepth;
                passData.SrcSize = new Vector4(srcDesc.width, srcDesc.height, 0, 0);

                builder.SetRenderAttachment(targetTexture, 0, AccessFlags.WriteAll);
                builder.UseTexture(passData.Source, AccessFlags.Read);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                {
                    ctx.cmd.SetGlobalVector(SrcMipSizeID, data.SrcSize);
                    BlitterExtension.BlitTexture(ctx.cmd, data.Source, BlitterExtension.s_FullViewportScaleBias, data.Material, data.PassIndex);
                });
            }

            var makeMipsDesc = new MipMapGenerator.Descriptor()
            {
                Material = desc.Material,
                PassIndex = desc.PassIndex,
                MaxMipCount = desc.MaxMipCount,
                PassName = "Sirius.GenerateDepthMipMap",
            };
            renderGraph.AddGenerateMipMapsPass(makeMipsDesc, targetTexture, out mipCount);
        }
    }
}
