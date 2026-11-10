using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Sirius.Core
{
    public static class MipMapGenerator
    {
        private static readonly Vector4 ScaleBias = new(1f, 1f, 0f, 0f);
        private static readonly int SrcMipSizeID = Shader.PropertyToID("_SrcMipSize");

        public static int CalculateMipCount(int width, int height, int maxMipCount = 0)
        {
            var mipCount = Mathf.FloorToInt(Mathf.Log(Mathf.Min(width, height), 2)) + 1;
            mipCount = maxMipCount > 0 ? Mathf.Min(mipCount, maxMipCount) : mipCount;
            return mipCount;
        }

        public struct Descriptor
        {
            public Material Material;
            public int PassIndex;
            public int MaxMipCount;
            public string PassName;
        }

        private class PassData
        {
            public TextureHandle TargetTexture;
            public TextureHandle[] TempTextures;
            public Material Material;
            public int PassIndex;
            public Vector2Int BaseSize;
        }

        public static void AddGenerateMipMapsPass(this RenderGraph renderGraph, in Descriptor desc, TextureHandle targetTexture, out int mipCount)
        {
            var passName = string.IsNullOrEmpty(desc.PassName) ? "Sirius.GenerateMipMap" : desc.PassName;
            using (var builder = renderGraph.AddUnsafePass<PassData>(passName, out var passData))
            {
                var texDesc = renderGraph.GetTextureDesc(targetTexture);
                mipCount = CalculateMipCount(texDesc.width, texDesc.height, desc.MaxMipCount);

                passData.TargetTexture = targetTexture;
                passData.Material = desc.Material;
                passData.PassIndex = desc.PassIndex;
                passData.BaseSize = new Vector2Int(texDesc.width, texDesc.height);

                builder.UseTexture(passData.TargetTexture, AccessFlags.ReadWrite);
                builder.AllowGlobalStateModification(true);

                // 中間Textureを利用してMipMap生成
                var tempTexCount = mipCount - 1;
                passData.TempTextures = new TextureHandle[tempTexCount];
                texDesc.useMipMap = false;
                texDesc.enableRandomWrite = false;
                texDesc.clearBuffer = false;
                for (var i = 0; i < tempTexCount; i++)
                {
                    texDesc.name = $"_TempMip{i.ToString()}";
                    texDesc.width = Mathf.Max(1, texDesc.width >> 1);
                    texDesc.height = Mathf.Max(1, texDesc.height >> 1);
                    passData.TempTextures[i] = renderGraph.CreateTexture(texDesc);
                    builder.UseTexture(passData.TempTextures[i], AccessFlags.ReadWrite);
                }
                builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    var target = data.TargetTexture;
                    var temps = data.TempTextures;
                    var material = data.Material;
                    var pass = data.PassIndex;
                    var baseSize = data.BaseSize;
                    for (var i = 0; i < temps.Length; i++)
                    {
                        var srcW = Mathf.Max(1, baseSize.x >> i);
                        var srcH = Mathf.Max(1, baseSize.y >> i);
                        cmd.SetGlobalVector(SrcMipSizeID, new Vector4(srcW, srcH, 0, 0));
                        var source = i == 0 ? target : temps[i - 1];
                        cmd.SetRenderTarget(temps[i]);
                        BlitterExtension.BlitTexture(cmd, source, ScaleBias, material, pass);
                    }
                    for (var i = 0; i < temps.Length; i++)
                    {
                        cmd.CopyTexture(temps[i], 0, 0, target, 0, i + 1);
                    }
                });
            }
        }
    }
}
