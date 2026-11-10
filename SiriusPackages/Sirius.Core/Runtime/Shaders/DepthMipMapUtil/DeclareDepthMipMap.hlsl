#ifndef SIRIUS_DECLARE_DEPTH_MIP_MAP_INCLUDED
#define SIRIUS_DECLARE_DEPTH_MIP_MAP_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"

// 4K目標 → mip数11
#define CT_DEPTH_MIPS_MAX_LOD 10   // HalfStartDepthMipMapの最大有効mipインデックス

// XR非対応のため両方TEXTURE2Dで宣言
TEXTURE2D_FLOAT(_CT_FullSizeDepthTexture);      // 仮想lod0(全解像度)
float4 _CT_FullSizeDepthTexture_TexelSize;
TEXTURE2D_FLOAT(_CT_HalfStartDepthMipMap);      // 仮想lod1以降
float4 _CT_HalfStartDepthMipMap_TexelSize;

// ---- FullSize直読API(全解像度本体を読む読み手用。分岐なし) ----

float CT_SampleFullSizeDepth(float2 uv)
{
    return SAMPLE_DEPTH_TEXTURE_LOD(_CT_FullSizeDepthTexture, sampler_PointClamp, uv, 0);
}

// コア: pixelPosはFullSizeのテクセル空間。範囲内保証は呼び出し側
float CT_LoadFullSizeDepth(uint2 pixelPos)
{
    return LOAD_TEXTURE2D_LOD(_CT_FullSizeDepthTexture, pixelPos, 0).r;
}

// ---- MipMap直読API(半解像度基準のlodを渡す読み手用。分岐なし) ----

float CT_SampleHalfStartDepthMipMap_Lod(float2 uv, uint lod)
{
    lod = clamp(lod, 0, CT_DEPTH_MIPS_MAX_LOD);
    return SAMPLE_DEPTH_TEXTURE_LOD(_CT_HalfStartDepthMipMap, sampler_PointClamp, uv, lod);
}

// コア: pixelPosは対象lodのテクセル空間。範囲内保証は呼び出し側
float CT_LoadHalfStartDepthMipMap_Lod(uint2 pixelPos, uint lod)
{
    lod = clamp(lod, 0, CT_DEPTH_MIPS_MAX_LOD);
    return LOAD_TEXTURE2D_LOD(_CT_HalfStartDepthMipMap, pixelPos, lod).r;
}

// ---- 仮想ピラミッドAPI ----
// lodの基準はvariantで切り替わる:
//   _CT_DEPTH_PYRAMID_FULLSIZE 有効時: lod0=FullSize本体、lod n≥1=HalfStartDepthMipMapのmip n-1
//   無効時: lod0=HalfStartDepthMipMapのmip0(半解像度)。FullSizeには一切触れない(バインド不要)
// FullSizeと半解像度を切り替える利用パスのみ #pragma multi_compile_fragment _ _CT_DEPTH_PYRAMID_FULLSIZE を宣言する(半解像度固定なら不要)

// コア: セルindex等の整数座標を既に持つ読み手はこちらでuv→pixelPos再計算を避ける
float CT_LoadDepthPyramid_Lod(uint2 pixelPos, uint lod)
{
#ifdef _CT_DEPTH_PYRAMID_FULLSIZE
    UNITY_BRANCH
    if (lod == 0)
    {
        return CT_LoadFullSizeDepth(pixelPos);
    }
    return CT_LoadHalfStartDepthMipMap_Lod(pixelPos, lod - 1);
#else
    return CT_LoadHalfStartDepthMipMap_Lod(pixelPos, lod);
#endif
}

// 各LODのテクスチャサイズ取得
uint2 CT_GetDepthPyramidLevelSize(uint lod)
{
#ifdef _CT_DEPTH_PYRAMID_FULLSIZE
    UNITY_BRANCH
    if (lod == 0)
    {
        return uint2(_CT_FullSizeDepthTexture_TexelSize.zw);
    }
    return max(1, uint2(_CT_HalfStartDepthMipMap_TexelSize.zw) >> (lod - 1));
#else
    return max(1, uint2(_CT_HalfStartDepthMipMap_TexelSize.zw) >> lod);
#endif
}

// uv読み手用のLoad版: uvを対象lodのpixelPosへ換算してLoadする
// (Sample版はサンプラーのuv→テクセル丸めが整数座標の読み手とズレることがある)
float CT_LoadDepthPyramidUV_Lod(float2 uv, uint lod)
{
    uint2 pixelPos = uint2(uv * CT_GetDepthPyramidLevelSize(lod));
    return CT_LoadDepthPyramid_Lod(pixelPos, lod);
}

// uv読み手用(uv→pixelPos換算はサンプラーの専用ユニットに任せる)
float CT_SampleDepthPyramid_Lod(float2 uv, uint lod)
{
#ifdef _CT_DEPTH_PYRAMID_FULLSIZE
    UNITY_BRANCH
    if (lod == 0)
    {
        return CT_SampleFullSizeDepth(uv);
    }
    return CT_SampleHalfStartDepthMipMap_Lod(uv, lod - 1);
#else
    return CT_SampleHalfStartDepthMipMap_Lod(uv, lod);
#endif
}

#endif
