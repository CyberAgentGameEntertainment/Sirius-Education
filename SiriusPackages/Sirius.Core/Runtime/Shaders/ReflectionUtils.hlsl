#ifndef CT_REFLECTION_UTILS_HLSL
#define CT_REFLECTION_UTILS_HLSL

#include "Packages/jp.co.cyberagent.sirius.core/Runtime/Shaders/CoreUtil.hlsl"

#ifdef _CT_ENABLE_SSPR

TEXTURE2D(_CtScreenSpacePlanarReflectionTex); // 反射テクスチャ


/**
 * \brief スクリーンスペース平面反射のカラーを取得する
 * \param worldPosition ワールド座標
 * \param distortionUV ディストーションUV
 */
half4 LoadScreenSpacePlanarReflectionColor(float3 worldPosition, float2 distortionUV)
{
    float4 clipPos = mul(_CtPrevFrameVpMatrix, float4(worldPosition, 1.0));
    clipPos.xy /= clipPos.w;
    clipPos.xy = clipPos.xy * 0.5 + 0.5;
    clipPos.xy += distortionUV;

    return SAMPLE_TEXTURE2D_X(_CtScreenSpacePlanarReflectionTex, sampler_LinearClamp, clipPos.xy);

}

/**
 * \brief スクリーンスペース平面反射のカラーを取得する
 * \param worldPosition ワールド座標
 */
half4 LoadScreenSpacePlanarReflectionColor(float3 worldPosition)
{
    return LoadScreenSpacePlanarReflectionColor(worldPosition, float2(0,0));
}

#endif

#endif
