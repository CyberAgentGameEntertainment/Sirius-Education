// --------------------------------------------------------------
// Copyright 2025 CyberAgent, Inc.
// --------------------------------------------------------------

namespace UnityEngine.Rendering
{
    /// <summary>
    /// URPのBlitterクラスの拡張
    /// </summary>
    public static class BlitterExtension
    {
        public static readonly MaterialPropertyBlock s_PropertyBlock = new();

        // URPのBlitterシェーダーで使われているプロパティID
        public static class BlitShaderIDs
        {
            public static readonly int _BlitTexture = Shader.PropertyToID("_BlitTexture");
            public static readonly int _BlitCubeTexture = Shader.PropertyToID("_BlitCubeTexture");
            public static readonly int _BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");
            public static readonly int _BlitScaleBiasRt = Shader.PropertyToID("_BlitScaleBiasRt");
            public static readonly int _BlitMipLevel = Shader.PropertyToID("_BlitMipLevel");
            public static readonly int _BlitTexArraySlice = Shader.PropertyToID("_BlitTexArraySlice");
            public static readonly int _BlitTextureSize = Shader.PropertyToID("_BlitTextureSize");
            public static readonly int _BlitPaddingSize = Shader.PropertyToID("_BlitPaddingSize");
            public static readonly int _BlitDecodeInstructions = Shader.PropertyToID("_BlitDecodeInstructions");
        }
        public static readonly Vector4 s_FullViewportScaleBias = new(1, 1, 0, 0);

        // SourceのPropertyIDを指定できるBlitTexture関数
        public static void BlitTexture(CommandBuffer cmd, RTHandle source, int sourcePropertyID, Vector4 scaleBias,
            Material material, int pass, bool clearPropertyBlock = true)
        {
            if (clearPropertyBlock)
                s_PropertyBlock.Clear();
            s_PropertyBlock.SetTexture(sourcePropertyID, source);
            s_PropertyBlock.SetVector(BlitShaderIDs._BlitScaleBias, scaleBias);
            Blitter.DrawTriangle(cmd, material, pass, s_PropertyBlock);
        }

        public static void BlitTexture(RasterCommandBuffer cmd, RTHandle source, int sourcePropertyID, Vector4 scaleBias,
            Material material, int pass, bool clearPropertyBlock = true)
        {
            BlitTexture(cmd.m_WrappedCommandBuffer, source, sourcePropertyID, scaleBias, material, pass, clearPropertyBlock);
        }

        /// <summary>
        /// SourceMipLevel, SourceDepthSliceが指定でき、MaterialとPassも指定できるBlitTexture関数
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="source"></param>
        /// <param name="scaleBias"></param>
        /// <param name="material"></param>
        /// <param name="pass"></param>
        /// <param name="sourceMipLevel"></param>
        /// <param name="sourceDepthSlice"></param>
        public static void BlitTexture(CommandBuffer cmd, RTHandle source, Vector4 scaleBias, Material material, int pass,
            float sourceMipLevel = 0, int sourceDepthSlice = 0)
        {
            Blitter.BlitTexture(cmd, source, scaleBias, material, pass, sourceMipLevel, sourceDepthSlice);
        }

        /// <summary>
        /// 上記関数のRasterCommandBuffer版
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="source"></param>
        /// <param name="scaleBias"></param>
        /// <param name="material"></param>
        /// <param name="pass"></param>
        /// <param name="sourceMipLevel"></param>
        /// <param name="sourceDepthSlice"></param>
        public static void BlitTexture(RasterCommandBuffer cmd, RTHandle source, Vector4 scaleBias, Material material, int pass,
            float sourceMipLevel = 0, int sourceDepthSlice = 0)
        {
            Blitter.BlitTexture(cmd.m_WrappedCommandBuffer, source, scaleBias, material, pass, sourceMipLevel, sourceDepthSlice);
        }
    }
}
