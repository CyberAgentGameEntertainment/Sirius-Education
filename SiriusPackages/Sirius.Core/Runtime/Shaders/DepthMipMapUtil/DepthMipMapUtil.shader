Shader "Hidden/Sirius/DepthMipMapUtil"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque"}

        ZTest Always
        ZWrite Off
        ColorMask R
        Cull Off

        HLSLINCLUDE
        #include_with_pragmas "Packages/jp.co.cyberagent.sirius.core/Runtime/Shaders/Common_URP_Free/HAS_URP_Definition.hlsl"
        ENDHLSL

        Pass    // 近隣4ピクセルの最小Depthを取ってDownSampleする
        {
            Name "Down Sample Depth"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            //#pragma enable_d3d11_debug_symbols

            #include "Packages/jp.co.cyberagent.sirius.core/Runtime/Shaders/Common_URP_Free/SiriusCore.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #if UNITY_REVERSED_Z
            #define MIN_DEPTH(l, r) max(l, r)
            #define MIN_DEPTH3(a, b, c) Max3(a, b, c)
            #else
            #define MIN_DEPTH(l, r) min(l, r)
            #define MIN_DEPTH3(a, b, c) Min3(a, b, c)
            #endif

            int4 _SrcMipSize;   // xy: ダウンサンプル元mipレベルの実サイズ

            float Frag(Varyings i) : SV_TARGET
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                uint2 pixelCoord = i.positionCS.xy;
                uint2 s = pixelCoord * 2;
                float d00 = LOAD_TEXTURE2D_X_LOD(_BlitTexture, s, _BlitMipLevel).r;
                float d01 = LOAD_TEXTURE2D_X_LOD(_BlitTexture, s + uint2(0, 1), _BlitMipLevel).r;
                float d10 = LOAD_TEXTURE2D_X_LOD(_BlitTexture, s + uint2(1, 0), _BlitMipLevel).r;
                float d11 = LOAD_TEXTURE2D_X_LOD(_BlitTexture, s + uint2(1, 1), _BlitMipLevel).r;
                float minDepth = MIN_DEPTH(MIN_DEPTH3(d00, d01, d10), d11);

                // srcが奇数サイズの軸では、本texelのfootprintが3texelに跨るため
                // 追加サンプルでminに含める（HiZの保守性維持、偶数軸では実行されない）
                bool oddX = (_SrcMipSize.x & 1) != 0;
                bool oddY = (_SrcMipSize.y & 1) != 0;
                UNITY_BRANCH
                if (oddX)
                {
                    minDepth = MIN_DEPTH(minDepth, MIN_DEPTH(
                        LOAD_TEXTURE2D_X_LOD(_BlitTexture, s + uint2(2, 0), _BlitMipLevel).r,
                        LOAD_TEXTURE2D_X_LOD(_BlitTexture, s + uint2(2, 1), _BlitMipLevel).r));
                }
                UNITY_BRANCH
                if (oddY)
                {
                    minDepth = MIN_DEPTH(minDepth, MIN_DEPTH(
                        LOAD_TEXTURE2D_X_LOD(_BlitTexture, s + uint2(0, 2), _BlitMipLevel).r,
                        LOAD_TEXTURE2D_X_LOD(_BlitTexture, s + uint2(1, 2), _BlitMipLevel).r));
                }
                UNITY_BRANCH
                if (oddX && oddY)
                {
                    minDepth = MIN_DEPTH(minDepth, LOAD_TEXTURE2D_X_LOD(_BlitTexture, s + uint2(2, 2), _BlitMipLevel).r);
                }

                return minDepth;
            }

            #undef MIN_DEPTH
            #undef MIN_DEPTH3
            ENDHLSL
        }
    }
}
