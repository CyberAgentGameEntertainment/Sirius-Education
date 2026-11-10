#ifndef SIRIUS_URP_FREE_DEPTH_TEXTURE_INCLUDED
#define SIRIUS_URP_FREE_DEPTH_TEXTURE_INCLUDED

#if HAS_URP
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
// 互換性のため：キーワード未宣言のプロジェクト側 shader が URP 原版を先に include 済みなら鏡写し宣言を出さない
#elif !defined(UNITY_DECLARE_DEPTH_TEXTURE_INCLUDED)

// scene depth テクスチャの宣言とサンプリングを URP 非依存で提供する。
#include "Packages/jp.co.cyberagent.sirius.core/Runtime/Shaders/Common_URP_Free/SiriusCore.hlsl"

TEXTURE2D_X_FLOAT(_CameraDepthTexture);
float4 _CameraDepthTexture_TexelSize;

float SampleSceneDepth(float2 uv, SAMPLER(samplerParam))
{
    return SAMPLE_TEXTURE2D_X(_CameraDepthTexture, samplerParam, uv).r;
}

float SampleSceneDepth(float2 uv)
{
    return SampleSceneDepth(uv, sampler_PointClamp);
}

float LoadSceneDepth(uint2 pixelCoords)
{
    return LOAD_TEXTURE2D_X(_CameraDepthTexture, pixelCoords).r;
}

#endif  // HAS_URP
#endif  // SIRIUS_URP_FREE_DEPTH_TEXTURE_INCLUDED
