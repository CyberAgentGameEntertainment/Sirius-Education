/*!
 * @file CoreUtil.hlsl
 * @brief SIRIUSのシェーダー全般のユーティリティ.
 */
#ifndef _SIRIUS_CORE_UTIL_HLSL_
#define _SIRIUS_CORE_UTIL_HLSL_

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "DeclareDepthTexture.hlsl"

float4x4 _CtPrevFrameVpMatrix; // 1フレーム前のビュープロジェクション行列

// グレースケール
#define GRAY_SCALE_BT601(rgbColor) (0.299 * (rgbColor).r + 0.587 * (rgbColor).g + 0.114 * (rgbColor).b)

/**
 * \brief DrawProcedural用の頂点属性
 */
struct AttributesProcedural
{
    uint vertexID : SV_VertexID;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

/**
 * \brief DrawProcedural用の頂点シェーダーからの出力構造体
 */
struct VaryingsProcedural
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_OUTPUT_STEREO
};


#define powFast(a, b) ((a) / ((1.0 - (b)) * (a) + (b)))
float pow2(float a)
{
    return a * a;
}
float2 pow2(float2 a)
{
    return a * a;
}
float3 pow2(float3 a)
{
    return a * a;
}
float4 pow2(float4 a)
{
    return a * a;
}
float pow3(float a)
{
    return a * a * a;
}
float2 pow3(float2 a)
{
    return a * a * a;
}
float3 pow3(float3 a)
{
    return a * a * a;
}
float4 pow3(float4 a)
{
    return a * a * a;
}
float pow4(float a)
{
    a = a * a;
    return a * a;
}
float2 pow4(float2 a)
{
    a = a * a;
    return a * a;
}
float3 pow4(float3 a)
{
    a = a * a;
    return a * a;
}
float4 pow4(float4 a)
{
    a = a * a;
    return a * a;
}
float pow8(float a)
{
    a = a * a;
    a = a * a;
    return a * a;
}
float2 pow8(float2 a)
{
    a = a * a;
    a = a * a;
    return a * a;
}
float3 pow8(float3 a)
{
    a = a * a;
    a = a * a;
    return a * a;
}
float4 pow8(float4 a)
{
    a = a * a;
    a = a * a;
    return a * a;
}
float PositiveClampedPow(float Base, float Exponent)
{
    return (Base <= 2.980233e-8) ? 0.0 : pow(Base, Exponent);
}
float2 PositiveClampedPow(float2 Base, float2 Exponent)
{
    return float2(PositiveClampedPow(Base.x, Exponent.x), PositiveClampedPow(Base.y, Exponent.y));
}
float3 PositiveClampedPow(float3 Base, float3 Exponent)
{
    return float3(PositiveClampedPow(Base.xy, Exponent.xy), PositiveClampedPow(Base.z, Exponent.z));
}
float4 PositiveClampedPow(float4 Base, float4 Exponent)
{
    return float4(PositiveClampedPow(Base.xy, Exponent.xy), PositiveClampedPow(Base.zw, Exponent.zw));
}

float acosFast(float inX)
{
    float x = abs(inX);
    float res = HALF_PI - 0.156583 * x;
    res *= sqrt(1.0 - x);
    return inX >= 0 ? res : PI - res;
}
float asinFast(float x)
{
    return HALF_PI - acosFast(x);
}
float atan2Fast(float y, float x)
{
    float ax = abs(x);
    float ay = abs(y);
    float t0 = max(ax, ay);
    float t1 = min(ax, ay);
    float t3 = t1 / t0;
    float t4 = t3 * t3;
    t0 = 0.0872929;
    t0 = t0 * t4 - 0.301895;
    t0 = t0 * t4 + 1.0;
    t3 = t0 * t3;
    t3 = ay > ax ? HALF_PI - t3 : t3;
    t3 = x < 0 ? PI - t3 : t3;
    t3 = y < 0 ? -t3 : t3;
    return t3;
}

float2 Panner(float2 Coordinate, float2 Speed, float Time, bool FractionalPart = false)
{
    float2 v = Coordinate + Speed * Time;
    v = FractionalPart ? frac(v) : v;
    return v;
}

float3 RotateAboutAxis(float4 NormalizedRotationAxisAndAngle, float3 PositionOnAxis, float3 Position)
{
    float3 ClosestPointOnAxis = PositionOnAxis + NormalizedRotationAxisAndAngle.xyz * dot(NormalizedRotationAxisAndAngle.xyz, Position - PositionOnAxis);
    float3 UAxis = Position - ClosestPointOnAxis;
    float3 VAxis = cross(NormalizedRotationAxisAndAngle.xyz, UAxis);
    float CosAngle;
    float SinAngle;
    sincos(NormalizedRotationAxisAndAngle.w, SinAngle, CosAngle);
    float3 R = UAxis * CosAngle + VAxis * SinAngle;
    float3 RotatedPosition = ClosestPointOnAxis + R;
    return RotatedPosition - Position;
}

bool IsOrthoProjection(float4x4 ViewToClip)
{
    return ViewToClip._44 >= 1.0;
}

// _ScaledScreenParams は URP10 には存在しない（URP13 以降のみ）ため、
// 古い URP では _CameraDepthTexture_TexelSize をフォールバックとして使う
// 最善のフォールバックではないが、NiloToonURP では現状これで動作している
float2 GetScaledScreenWidthHeight()
{
    #if SHADER_LIBRARY_VERSION_MAJOR >= 13
    return _ScaledScreenParams.xy;
    #else
    return _CameraDepthTexture_TexelSize.zw;
    #endif
}

float2 GetScaledScreenTexelSize()
{
    #if SHADER_LIBRARY_VERSION_MAJOR >= 13
    return _ScaledScreenParams.zw-1;
    #else
    return _CameraDepthTexture_TexelSize.xy;
    #endif
}

/**
 * \brief DrawProcedural用の頂点属性から頂点座標とテクスチャ座標を取得する
 */
void GetProceduralQuad(in uint vertexID, out float4 positionCS, out float2 uv)
{
    positionCS = GetQuadVertexPosition(vertexID);
    positionCS.xy = positionCS.xy * float2(2.0f, -2.0f) + float2(-1.0f, 1.0f);
    uv = GetQuadTexCoord(vertexID);
}

/**
 * \brief DrawProcedural用の頂点シェーダー
 */
VaryingsProcedural VertFullscreenMeshProcedural(AttributesProcedural input)
{
    VaryingsProcedural output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    GetProceduralQuad(input.vertexID, output.positionCS, output.uv);

    return output;
}

float Convert_SV_PositionZ_ToLinearViewSpaceDepthPerspectiveCamera(float rawDepthValueFromDepthTextureSample)
{
    // 透視投影カメラなら URP の LinearEyeDepth がすべて処理してくれる
    // https://docs.unity3d.com/Manual/SL-PlatformDifferences.html
    // 平行投影カメラには LinearEyeDepth() を使えないので注意
    return LinearEyeDepth(rawDepthValueFromDepthTextureSample, _ZBufferParams);
}

float Convert_SV_PositionZ_ToLinearViewSpaceDepthOrthographicCamera(float rawDepthValueFromDepthTextureSample)
{
    // 平行投影カメラの場合、_CameraDepthTexture はプラットフォームに関わらず（OpenGL でも）シーン深度を 0~1 で線形に格納する
    // 深度が反転しているプラットフォームでは 1-depth すること
    // https://docs.unity3d.com/Manual/SL-PlatformDifferences.html
    #if defined(UNITY_REVERSED_Z)
    // 一部の Android エミュレーターに対応するため UNITY_NEAR_CLIP_VALUE もチェックする
    // TODO: このチェックがまだ必要か確認する
    rawDepthValueFromDepthTextureSample = UNITY_NEAR_CLIP_VALUE == 1
                                              ? 1 - rawDepthValueFromDepthTextureSample
                                              : rawDepthValueFromDepthTextureSample;
    #endif

    // lerp(near, far, [0,1]の深度) でビュー空間の深度を求める
    return lerp(_ProjectionParams.y, _ProjectionParams.z, rawDepthValueFromDepthTextureSample);
}

// 想定する入力:
// - フラグメントシェーダーの SV_POSITION.z
// - tex2D(_CameraDepthTexture)（ShadowCaster パスの SV_POSITION.z）
// ※処理は重めだが、平行投影と透視投影の両方に対応する
float Convert_SV_PositionZ_ToLinearViewSpaceDepth(float SV_POSITIONz)
{
    // if() は数回の余分な演算より高コストになり得るため、a ? b : c（条件付き移動 / movc）を使う
    return unity_OrthoParams.w
               ? Convert_SV_PositionZ_ToLinearViewSpaceDepthOrthographicCamera(SV_POSITIONz)
               : Convert_SV_PositionZ_ToLinearViewSpaceDepthPerspectiveCamera(SV_POSITIONz);
}


float2 CalculateUVCoordFromClipSpace(float4 coordInClipSpace)
{
    float2 uv = coordInClipSpace.xy / coordInClipSpace.w;
    uv *= float2(0.5f, 0.5f * _ProjectionParams.x);
    uv += 0.5f;
    return uv;
}

// smoothstep() の線形版（クランプなし）
half InvLerp(half from, half to, half value)
{
    return (value - from) / (to - from);
}

half4 InvLerp(half from, half to, half4 value)
{
    return (value - from) / (to - from);
}

// smoothstep() の線形版
half InvLerpClamp(half from, half to, half value)
{
    return saturate(InvLerp(from, to, value));
}

half4 InvLerpClamp(half from, half to, half4 value)
{
    return saturate(InvLerp(from, to, value));
}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// 高レベルのヘルパー関数
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float LoadDepthTextureLinearDepthVS(int2 loadTexPos)
{
    // _CameraDepthTexture の有効範囲外を読まないよう loadTexPos をクランプする
    loadTexPos.x = max(loadTexPos.x, 0);
    loadTexPos.y = max(loadTexPos.y, 0);
    loadTexPos = min(loadTexPos, GetScaledScreenWidthHeight() - 1);

    float depthTextureRawSampleValue = LoadSceneDepth(loadTexPos);
    // URP の LoadSceneDepth(pos) を使うことで VR でも正しく描画される
    float depthTextureLinearDepthVS = Convert_SV_PositionZ_ToLinearViewSpaceDepth(depthTextureRawSampleValue);
    return depthTextureLinearDepthVS;
}

#endif
