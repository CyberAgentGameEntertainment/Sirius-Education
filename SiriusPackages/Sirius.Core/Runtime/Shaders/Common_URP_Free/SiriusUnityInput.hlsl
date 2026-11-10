#ifndef SIRIUS_URP_FREE_UNITY_INPUT_INCLUDED
#define SIRIUS_URP_FREE_UNITY_INPUT_INCLUDED

#if HAS_URP
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityInput.hlsl"
// 互換性のため：キーワード未宣言のプロジェクト側 shader が URP 原版を先に include 済みなら鏡写し宣言を出さない
#elif !defined(UNIVERSAL_SHADER_VARIABLES_INCLUDED)

// Unity エンジン組み込みのシェーダー入力変数。
// URP パッケージ固有の入力変数は .universal/ShaderLibrary/Input.hlsl で定義されている

#if defined(STEREO_INSTANCING_ON) && (defined(SHADER_API_D3D11) || defined(SHADER_API_GLES3) || defined(SHADER_API_GLCORE) || defined(SHADER_API_PSSL) || defined(SHADER_API_VULKAN) || (defined(SHADER_API_METAL) && !defined(UNITY_COMPILER_DXC)))
#define UNITY_STEREO_INSTANCING_ENABLED
#endif

#if defined(STEREO_MULTIVIEW_ON) && (defined(SHADER_API_GLES3) || defined(SHADER_API_GLCORE) || defined(SHADER_API_VULKAN)) && !(defined(SHADER_API_SWITCH))  && !(defined(SHADER_API_SWITCH2))
    #define UNITY_STEREO_MULTIVIEW_ENABLED
#endif

#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
#define USING_STEREO_MATRICES
#endif

#if defined(USING_STEREO_MATRICES)
// 現在のパスの変換行列
#define glstate_matrix_projection     unity_StereoMatrixP[unity_StereoEyeIndex] // GL.GetGPUProjectionMatrix() を通した行列
#define unity_MatrixV                 unity_StereoMatrixV[unity_StereoEyeIndex]
#define unity_MatrixInvV              unity_StereoMatrixInvV[unity_StereoEyeIndex]
#define unity_MatrixInvP              unity_StereoMatrixInvP[unity_StereoEyeIndex]
#define unity_MatrixVP                unity_StereoMatrixVP[unity_StereoEyeIndex]
#define unity_MatrixInvVP             unity_StereoMatrixInvVP[unity_StereoEyeIndex]

// カメラの変換行列（XR ではパスの変換行列と同じ）
#define unity_CameraProjection        unity_StereoCameraProjection[unity_StereoEyeIndex] // GL.GetGPUProjectionMatrix() を通していない行列
#define unity_CameraInvProjection     unity_StereoCameraInvProjection[unity_StereoEyeIndex]
#define unity_WorldToCamera           unity_StereoMatrixV[unity_StereoEyeIndex] // 本来 unity_StereoWorldToCamera だが XR パスでの用途がない
#define unity_CameraToWorld           unity_StereoMatrixInvV[unity_StereoEyeIndex] // 本来 unity_StereoCameraToWorld だが XR パスでの用途がない
#define _WorldSpaceCameraPos          unity_StereoWorldSpaceCameraPos[unity_StereoEyeIndex]
#endif

#define UNITY_LIGHTMODEL_AMBIENT (glstate_lightmodel_ambient * 2)

// ----------------------------------------------------------------------------

// Unity から渡される時間
float4 _Time; // (t/20, t, t*2, t*3)
float4 _SinTime; // sin(t/8), sin(t/4), sin(t/2), sin(t)
float4 _CosTime; // cos(t/8), cos(t/4), cos(t/2), cos(t)
float4 unity_DeltaTime; // dt, 1/dt, smoothdt, 1/smoothdt
float4 _TimeParameters; // t, sin(t), cos(t)
float4 _LastTimeParameters; // t, sin(t), cos(t)

#if !defined(USING_STEREO_MATRICES)
float3 _WorldSpaceCameraPos;
#endif

// x = 1 または -1（投影が反転している場合は -1）
// y = ニアクリップ面
// z = ファークリップ面
// w = 1/ファークリップ面
float4 _ProjectionParams;

// x = 幅
// y = 高さ
// z = 1 + 1.0/幅
// w = 1 + 1.0/高さ
float4 _ScreenParams;

// Z バッファの線形化に使う値 (http://www.humus.name/temp/Linearize%20depth.txt)
// x = 1-far/near
// y = far/near
// z = x/far
// w = y/far
// 深度バッファが反転している場合（UNITY_REVERSED_Z が 1）は
// x = -1+far/near
// y = 1
// z = x/far
// w = 1/far
float4 _ZBufferParams;

// x = 平行投影カメラの幅
// y = 平行投影カメラの高さ
// z = 未使用
// w = 平行投影なら 1.0、透視投影なら 0.0
float4 unity_OrthoParams;

// scaleBias.x = flipSign
// scaleBias.y = scale
// scaleBias.z = bias
// scaleBias.w = 未使用
uniform float4 _ScaleBias;
uniform float4 _ScaleBiasRt;

// { w / RTHandle.maxWidth, h / RTHandle.maxHeight } : xy = currFrame, zw = prevFrame
uniform float4 _RTHandleScale;

float4 unity_CameraWorldClipPlanes[6];

#if !defined(USING_STEREO_MATRICES)
// カメラの投影行列。現在設定されている投影行列とは異なる場合がある点に注意
// （例: 影の描画中でも以下の行列は元のカメラの投影行列のまま）
float4x4 unity_CameraProjection;
float4x4 unity_CameraInvProjection;
float4x4 unity_WorldToCamera;
float4x4 unity_CameraToWorld;
#endif

// ----------------------------------------------------------------------------

#ifndef DOTS_INSTANCING_ON // Hybrid Renderer では UnityPerDraw の cbuffer が存在しない

// SRP Batcher のためブロックのレイアウトを守ること
CBUFFER_START(UnityPerDraw)
// Space ブロック
float4x4 unity_ObjectToWorld;
float4x4 unity_WorldToObject;
float4 unity_LODFade; // x は [0,1] のフェード値。y は x を 16 段階に量子化した値
real4 unity_WorldTransformParams; // w は通常 1.0、負のスケールが奇数個ある場合は -1.0

// Render Layer ブロック
// 有効なのは x のみ。元の 32bit 値を取り出すには asuint() で再解釈する必要がある
float4 unity_RenderingLayer;

// Light Indices ブロック
// RendererConfiguration の要求に応じてエンジン内部で設定される
half4 unity_LightData;
half4 unity_LightIndices[2];

float4 unity_ProbesOcclusion;

// Reflection Probe 0 ブロック
// HDR 環境マップのデコード情報
real4 unity_SpecCube0_HDR;
real4 unity_SpecCube1_HDR;

float4 unity_SpecCube0_BoxMax;          // w はブレンド距離
float4 unity_SpecCube0_BoxMin;          // w は補間値
float4 unity_SpecCube0_ProbePosition;   // ボックス投影の場合 w は 1
float4 unity_SpecCube0_Rotation;
float4 unity_SpecCube1_BoxMax;          // w はブレンド距離
float4 unity_SpecCube1_BoxMin;          // w は (SpecCube0.importance - SpecCube1.importance) の符号
float4 unity_SpecCube1_ProbePosition;   // ボックス投影の場合 w は 1
float4 unity_SpecCube1_Rotation;

// Lightmap ブロック
float4 unity_LightmapST;
float4 unity_DynamicLightmapST;

// SH ブロック
real4 unity_SHAr;
real4 unity_SHAg;
real4 unity_SHAb;
real4 unity_SHBr;
real4 unity_SHBg;
real4 unity_SHBb;
real4 unity_SHC;

// レンダラーのバウンディングボックス
float4 unity_RendererBounds_Min;
float4 unity_RendererBounds_Max;

// 速度
float4x4 unity_MatrixPreviousM;
float4x4 unity_MatrixPreviousMI;
//X : 前フレームの位置を使う（現状これを使うのはスキンメッシュのみ
//Y : モーションを強制的に無効化
//Z : Z バイアス値
//W : カメラのみ
float4 unity_MotionVectorsParams;

// スプライト
float4 unity_SpriteColor;
//X : FlipX
//Y : FlipY
//Z : 将来用に予約
//W : 将来用に予約
float4 unity_SpriteProps;
CBUFFER_END

static const uint unity_RendererUserValue = asuint(unity_RenderingLayer.y);

#endif // UNITY_DOTS_INSTANCING_ENABLED

// レンダラーのユーザー値は unity_RenderingLayer にパックされているため、Shader.PropertyToID を使うためのダミープロパティが必要
uint unity_RendererUserValuesPropertyEntry;

#if defined(USING_STEREO_MATRICES)
CBUFFER_START(UnityStereoViewBuffer)
float4x4 unity_StereoMatrixP[2];
float4x4 unity_StereoMatrixInvP[2];
float4x4 unity_StereoMatrixV[2];
float4x4 unity_StereoMatrixInvV[2];
float4x4 unity_StereoMatrixVP[2];
float4x4 unity_StereoMatrixInvVP[2];

float4x4 unity_StereoCameraProjection[2];
float4x4 unity_StereoCameraInvProjection[2];

float3   unity_StereoWorldSpaceCameraPos[2];
CBUFFER_END
#endif

#if defined(UNITY_STEREO_MULTIVIEW_ENABLED) && defined(SHADER_STAGE_VERTEX)
// OVR_multiview
// DX コンパイラにこの情報を伝えるため cbuffer で包む
#if !defined(UNITY_DECLARE_MULTIVIEW)
#define UNITY_DECLARE_MULTIVIEW(number_of_views) CBUFFER_START(OVR_multiview) uint gl_ViewID; uint numViews_##number_of_views; CBUFFER_END
#define UNITY_VIEWID gl_ViewID
#endif
#endif

#if defined(UNITY_STEREO_MULTIVIEW_ENABLED) && defined(SHADER_STAGE_VERTEX)
#define unity_StereoEyeIndex UNITY_VIEWID
UNITY_DECLARE_MULTIVIEW(2);
#elif defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
static uint unity_StereoEyeIndex;
#endif

float4x4 glstate_matrix_transpose_modelview0;

// ----------------------------------------------------------------------------

real4 glstate_lightmodel_ambient;
real4 unity_AmbientSky;
real4 unity_AmbientEquator;
real4 unity_AmbientGround;
real4 unity_IndirectSpecColor;
float4 unity_FogParams;
real4  unity_FogColor;

#if !defined(USING_STEREO_MATRICES)
float4x4 glstate_matrix_projection;
float4x4 unity_MatrixV;
float4x4 unity_MatrixInvV;
float4x4 unity_MatrixInvP;
float4x4 unity_MatrixVP;
float4x4 unity_MatrixInvVP;
float4 unity_StereoScaleOffset;
int unity_StereoEyeIndex;
#endif

real4 unity_ShadowColor;

// ----------------------------------------------------------------------------

// Unity 固有
TEXTURECUBE(unity_SpecCube0);
SAMPLER(samplerunity_SpecCube0);
TEXTURECUBE(unity_SpecCube1);
SAMPLER(samplerunity_SpecCube1);

// メインライトマップ
TEXTURE2D(unity_Lightmap);
SAMPLER(samplerunity_Lightmap);
TEXTURE2D_ARRAY(unity_Lightmaps);
SAMPLER(samplerunity_Lightmaps);

// 動的ライトマップ
TEXTURE2D(unity_DynamicLightmap);
SAMPLER(samplerunity_DynamicLightmap);
// TODO ENLIGHTEN: インスタンス化 GI

// デュアル / ディレクショナルライトマップ（常に unity_Lightmap と併用するためサンプラーを共有できる）
TEXTURE2D(unity_LightmapInd);
TEXTURE2D_ARRAY(unity_LightmapsInd);
TEXTURE2D(unity_DynamicDirectionality);
// TODO ENLIGHTEN: インスタンス化 GI
// TEXTURE2D_ARRAY(unity_DynamicDirectionality);

TEXTURE2D(unity_ShadowMask);
SAMPLER(samplerunity_ShadowMask);
TEXTURE2D_ARRAY(unity_ShadowMasks);
SAMPLER(samplerunity_ShadowMasks);

// ミップマップストリーミングのデバッグ
TEXTURE2D(unity_MipmapStreaming_DebugTex);

// ----------------------------------------------------------------------------

// TODO: アフィン行列はすべて 3x4 にすべき
// TODO: 使用頻度の高い順に並べ、よく使う変数をまとめる
// 注意: 行列変数を直接参照せず UNITY_MATRIX_X マクロを使うこと
#if defined(USING_STEREO_MATRICES)
float4x4 _PrevViewProjMatrixStereo[2];
float4x4 _NonJitteredViewProjMatrixStereo[2];
float4x4 _ViewProjMatrixStereo[2];
#define  _PrevViewProjMatrix  _PrevViewProjMatrixStereo[unity_StereoEyeIndex]
#define  _NonJitteredViewProjMatrix _NonJitteredViewProjMatrixStereo[unity_StereoEyeIndex]
#define  _ViewProjMatrix      _ViewProjMatrixStereo[unity_StereoEyeIndex]
#else
float4x4 _PrevViewProjMatrix;         // ジッターなし。モーションベクトル用
float4x4 _NonJitteredViewProjMatrix;  // ジッターなし
float4x4 _ViewProjMatrix; // TODO: 現状 URP は unity_MatrixVP を使っている（Input.hlsl 参照）
#endif
float4x4 _ViewMatrix;
float4x4 _ProjMatrix;
float4x4 _InvViewProjMatrix;
float4x4 _InvViewMatrix;
float4x4 _InvProjMatrix;
float4   _InvProjParam;
float4   _ScreenSize;       // {w, h, 1/w, 1/h}
float4   _FrustumPlanes[6]; // {(a, b, c) = N, d = -dot(N, P)} [L, R, T, B, N, F]

float4x4 OptimizeProjectionMatrix(float4x4 M)
{
    // 行列の形式（x = 定数でない値）
    // 平行投影     透視投影      合成(OR)
    // | x 0 0 x |  | x 0 x 0 |  | x 0 x x |
    // | 0 x 0 x |  | 0 x x 0 |  | 0 x x x |
    // | x x x x |  | x x x x |  | x x x x | <- 斜投影の行
    // | 0 0 0 1 |  | 0 0 x 0 |  | 0 0 x x |
    // 一部の値は常に 0 なので、
    // 定数のロードと演算を省略できる
    M._21_41 = 0;
    M._12_42 = 0;
    return M;
}

#endif  // HAS_URP
#endif  // SIRIUS_URP_FREE_UNITY_INPUT_INCLUDED
