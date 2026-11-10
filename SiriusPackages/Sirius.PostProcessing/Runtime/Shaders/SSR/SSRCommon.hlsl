#ifndef SIRIUS_SSR_COMMON_INCLUDED
#define SIRIUS_SSR_COMMON_INCLUDED

#include "Packages/jp.co.cyberagent.sirius.core/Runtime/Shaders/Common_URP_Free/SiriusCore.hlsl"
#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

// 反射結果合成時に使われるDiffuseMap
// 使用Channelはrgbに固定
TEXTURE2D_X_HALF(_CT_SSR_DiffuseTexture);

// 反射計算時および反射結果合成時に使われるSmoothnessMap
// 反射計算時: _MinSmoothness以上のPixelのみ反射計算を行う
// 反射結果合成時: Smoothnessが高いほど反射結果がはっきりする
TEXTURE2D_X_HALF(_CT_SSR_SmoothTexture);
uint _CT_SSR_SmoothChannel;

// 反射結果合成時に使われるMetalicMap
// Metalicが高いほど反射結果がはっきりする
TEXTURE2D_X_HALF(_CT_SSR_MetalicTexture);
uint _CT_SSR_MetalicChannel;

// 反射結果合成時に使われるAOMap
// AOが低いほど反射結果がはっきりする
TEXTURE2D_X_HALF(_CT_SSR_AOTexture);
uint _CT_SSR_AOChannel;

// 反射後のUV座標を保存するMap
TEXTURE2D_X_FLOAT(_CT_SSR_ReflectedUVMap);

uint _Frame;
uint _DitherMode;
uint _DitherTarget;
uint _MaxSteps;
uint _IsReflectSky;
uint _HiZTraceStartLevel;
uint _HiZTraceMaxLevel;
float _MinSmoothness;
float2 _ScreenResolution;
float2 _CrossEpsilon;

#if UNITY_REVERSED_Z
#define DEPTH_FAR 0.0
#else
#define DEPTH_FAR 1.0
#endif

#define HIZ_START_LEVEL _HiZTraceStartLevel
#define HIZ_MAX_LEVEL _HiZTraceMaxLevel
#define HIZ_STOP_LEVEL _HiZTraceStartLevel

float3 SampleReflectedUV(float2 uv)
{
    float4 reflectedUV = SAMPLE_TEXTURE2D_X(_CT_SSR_ReflectedUVMap, sampler_PointClamp, uv);
    return reflectedUV.xyz;
}

half3 SampleDiffuseColor(float2 uv)
{
    float4 diffuseMap = SAMPLE_TEXTURE2D_X(_CT_SSR_DiffuseTexture, sampler_LinearClamp, uv);
    float3 diffuse = diffuseMap.rgb;
    return diffuse;
}

half SampleSmoothness(float2 uv)
{
    float4 smoothnessMap = SAMPLE_TEXTURE2D_X(_CT_SSR_SmoothTexture, sampler_LinearClamp, uv);
    float smoothness = smoothnessMap[_CT_SSR_SmoothChannel];
    return smoothness;
}

half SampleMetalic(float2 uv)
{
    float4 metalicMap = SAMPLE_TEXTURE2D_X(_CT_SSR_MetalicTexture, sampler_LinearClamp, uv);
    float metalic = metalicMap[_CT_SSR_MetalicChannel];
    return metalic;
}

half SampleAO(float2 uv)
{
    float4 aoMap = SAMPLE_TEXTURE2D_X(_CT_SSR_AOTexture, sampler_LinearClamp, uv);
    float ao = aoMap[_CT_SSR_AOChannel];
    return ao;
}

float2 GetScreenResolution()
{
    return _ScreenResolution;
}

float2 GetCrossEpsilon()
{
    return _CrossEpsilon;
}

float2 GetLevelResolution(uint level)
{
    uint2 res = uint2(GetScreenResolution());
    return max(1, res >> level);
}

uint2 GetCellIndex(float2 ray, uint2 cellCount)
{
    return floor(ray.xy * cellCount);
}

uint2 GetCellCount(uint level)
{
    uint2 res = GetLevelResolution(level);
    return res;
}

float GetScreenEdgeMask(float2 positionCS)
{
    float yDiff = 1 - abs(positionCS.y);
    float xDiff = 1 - abs(positionCS.x);

    bool isEdge = yDiff < 0 || xDiff < 0;

    float t1 = smoothstep(0, 0.2, yDiff);
    float t2 = smoothstep(0, 0.1, xDiff);

    float mask = isEdge ? 0 : saturate(t1 * t2);
    return mask;
}

bool floatEqApprox(float a, float b)
{
    const float eps = 0.00001;
    return abs(a - b) < eps;
}

bool CheckCrossedCellBoundary(float2 cellId_1, float2 cellId_2)
{
    return !floatEqApprox(cellId_1.x, cellId_2.x) || !floatEqApprox(cellId_1.y, cellId_2.y);
}

float3 IntersectDepthPlane(float3 o, float3 d, float t)
{
    return o + d * t;
}

half RGB2Lum(half3 rgb)
{
    return (0.299 * rgb.r + 0.587 * rgb.g + 0.114 * rgb.b);
}

// ディザノイズ
float Dither8x8(uint2 pixCoord, float c0)
{
    const float dither[64] =
    {
        0, 32, 8, 40, 2, 34, 10, 42,
        48, 16, 56, 24, 50, 18, 58, 26,
        12, 44, 4, 36, 14, 46, 6, 38,
        60, 28, 52, 20, 62, 30, 54, 22,
        3, 35, 11, 43, 1, 33, 9, 41,
        51, 19, 59, 27, 49, 17, 57, 25,
        15, 47, 7, 39, 13, 45, 5, 37,
        63, 31, 55, 23, 61, 29, 53, 21
    };

    c0 *= 2;

    uint index = pixCoord.x % 8 * 8 + pixCoord.y % 8;

    float limit = float(dither[index] + 1) / 64.0;
    return saturate(c0 - limit);
}

// ビュー方向系ヘルパー（URP ShaderVariablesFunctions.hlsl 由来。同名関数のURP原版と衝突するため SSR_ 接頭辞を付与）
bool SSR_IsPerspectiveProjection()
{
    return (unity_OrthoParams.w == 0);
}

// 現在のビューの前方（中心）方向をワールド空間で返す
float3 SSR_GetViewForwardDir()
{
    float4x4 viewMat = GetWorldToViewMatrix();
    return -viewMat[2].xyz;
}

half3 SSR_GetWorldSpaceNormalizeViewDir(float3 positionWS)
{
    if (SSR_IsPerspectiveProjection())
    {
        // 透視投影
        float3 V = _WorldSpaceCameraPos - positionWS;
        return half3(normalize(V));
    }
    else
    {
        // 平行投影
        return half3(-SSR_GetViewForwardDir());
    }
}

#endif
