#ifndef SIRIUS_URP_FREE_TEXTURE_X_INCLUDED
#define SIRIUS_URP_FREE_TEXTURE_X_INCLUDED

// TEXTURE2D_X 系マクロを非 XR 固定で提供する（Sirius は XR 非対応）。
// 定義本体は URP Core.hlsl の非ステレオ分岐と字句一致させ、HAS_URP 時に URP 側と重複定義しても衝突しないようにする。
// core TextureXR.hlsl は非ステレオ時に unity_StereoEyeIndex をマクロ化し URP Input.hlsl の宣言と衝突するため使わない

#define SLICE_ARRAY_INDEX       0

#define TEXTURE2D_X(textureName)                                        TEXTURE2D(textureName)
#define TYPED_TEXTURE2D_X(type, textureName)                            TYPED_TEXTURE2D(type, textureName)
#define TEXTURE2D_X_PARAM(textureName, samplerName)                     TEXTURE2D_PARAM(textureName, samplerName)
#define TEXTURE2D_X_ARGS(textureName, samplerName)                      TEXTURE2D_ARGS(textureName, samplerName)
#define TEXTURE2D_X_HALF(textureName)                                   TEXTURE2D_HALF(textureName)
#define TEXTURE2D_X_FLOAT(textureName)                                  TEXTURE2D_FLOAT(textureName)

#define FRAMEBUFFER_INPUT_X_HALF(idx)                                   FRAMEBUFFER_INPUT_HALF(idx)
#define FRAMEBUFFER_INPUT_X_FLOAT(idx)                                  FRAMEBUFFER_INPUT_FLOAT(idx)
#define FRAMEBUFFER_INPUT_X_INT(idx)                                    FRAMEBUFFER_INPUT_INT(idx)
#define FRAMEBUFFER_INPUT_X_UINT(idx)                                   FRAMEBUFFER_INPUT_UINT(idx)
#define LOAD_FRAMEBUFFER_X_INPUT(idx, v2fname)                          LOAD_FRAMEBUFFER_INPUT(idx, v2fname)
#define LOAD_FRAMEBUFFER_INPUT_X(idx, v2fname)                          LOAD_FRAMEBUFFER_X_INPUT(idx, v2fname)

#define FRAMEBUFFER_INPUT_X_HALF_MS(idx)                                FRAMEBUFFER_INPUT_HALF_MS(idx)
#define FRAMEBUFFER_INPUT_X_FLOAT_MS(idx)                               FRAMEBUFFER_INPUT_FLOAT_MS(idx)
#define FRAMEBUFFER_INPUT_X_INT_MS(idx)                                 FRAMEBUFFER_INPUT_INT_MS(idx)
#define FRAMEBUFFER_INPUT_X_UINT_MS(idx)                                FRAMEBUFFER_INPUT_UINT_MS(idx)
#define LOAD_FRAMEBUFFER_INPUT_X_MS(idx, sampleIdx, v2fname)            LOAD_FRAMEBUFFER_INPUT_MS(idx, sampleIdx, v2fname)

#define LOAD_TEXTURE2D_X(textureName, unCoord2)                         LOAD_TEXTURE2D(textureName, unCoord2)
#define LOAD_TEXTURE2D_X_MSAA(textureName, unCoord2, sampleIndex)       LOAD_TEXTURE2D_MSAA(textureName, unCoord2, sampleIndex)
#define LOAD_TEXTURE2D_X_LOD(textureName, unCoord2, lod)                LOAD_TEXTURE2D_LOD(textureName, unCoord2, lod)
#define SAMPLE_TEXTURE2D_X(textureName, samplerName, coord2)            SAMPLE_TEXTURE2D(textureName, samplerName, coord2)
#define SAMPLE_TEXTURE2D_X_LOD(textureName, samplerName, coord2, lod)   SAMPLE_TEXTURE2D_LOD(textureName, samplerName, coord2, lod)
#define GATHER_TEXTURE2D_X(textureName, samplerName, coord2)            GATHER_TEXTURE2D(textureName, samplerName, coord2)
#define GATHER_RED_TEXTURE2D_X(textureName, samplerName, coord2)        GATHER_RED_TEXTURE2D(textureName, samplerName, coord2)
#define GATHER_GREEN_TEXTURE2D_X(textureName, samplerName, coord2)      GATHER_GREEN_TEXTURE2D(textureName, samplerName, coord2)
#define GATHER_BLUE_TEXTURE2D_X(textureName, samplerName, coord2)       GATHER_BLUE_TEXTURE2D(textureName, samplerName, coord2)
#define GATHER_ALPHA_TEXTURE2D_X(textureName, samplerName, coord2)      GATHER_ALPHA_TEXTURE2D(textureName, samplerName, coord2)

#endif  // SIRIUS_URP_FREE_TEXTURE_X_INCLUDED
