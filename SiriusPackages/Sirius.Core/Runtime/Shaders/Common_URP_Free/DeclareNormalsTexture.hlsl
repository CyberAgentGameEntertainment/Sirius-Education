#ifndef SIRIUS_URP_FREE_NORMALS_TEXTURE_INCLUDED
#define SIRIUS_URP_FREE_NORMALS_TEXTURE_INCLUDED

#if HAS_URP
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
// 互換性のため：キーワード未宣言のプロジェクト側 shader が URP 原版を先に include 済みなら鏡写し宣言を出さない
#elif !defined(UNITY_DECLARE_NORMALS_TEXTURE_INCLUDED)

// scene normals テクスチャの宣言とサンプリングを URP 非依存で提供する。
// _GBUFFER_NORMALS_OCT 時の oct 解包分岐は URP 版から逐字移植（deferred + accurate normals 構成で必須）。
#include "Packages/jp.co.cyberagent.sirius.core/Runtime/Shaders/Common_URP_Free/SiriusCore.hlsl"

TEXTURE2D_X_FLOAT(_CameraNormalsTexture);
float4 _CameraNormalsTexture_TexelSize;

float3 SampleSceneNormals(float2 uv, SAMPLER(samplerParam))
{
    float3 normal = SAMPLE_TEXTURE2D_X(_CameraNormalsTexture, samplerParam, uv).xyz;

    #if defined(_GBUFFER_NORMALS_OCT)
    float2 remappedOctNormalWS = Unpack888ToFloat2(normal); // 値の範囲は [ 0,  1]
    float2 octNormalWS = remappedOctNormalWS.xy * 2.0 - 1.0;    // 値の範囲は [-1, +1]
    normal = UnpackNormalOctQuadEncode(octNormalWS);
    #endif

    return normal;
}

float3 SampleSceneNormals(float2 uv)
{
    return SampleSceneNormals(uv, sampler_PointClamp);
}

float3 LoadSceneNormals(uint2 pixelCoords)
{
    float3 normal = LOAD_TEXTURE2D_X(_CameraNormalsTexture, pixelCoords).xyz;

    #if defined(_GBUFFER_NORMALS_OCT)
    float2 remappedOctNormalWS = Unpack888ToFloat2(normal); // 値の範囲は [ 0,  1]
    float2 octNormalWS = remappedOctNormalWS.xy * 2.0 - 1.0;    // 値の範囲は [-1, +1]
    normal = UnpackNormalOctQuadEncode(octNormalWS);
    #endif

    return normal;
}

#endif  // HAS_URP
#endif  // SIRIUS_URP_FREE_NORMALS_TEXTURE_INCLUDED
