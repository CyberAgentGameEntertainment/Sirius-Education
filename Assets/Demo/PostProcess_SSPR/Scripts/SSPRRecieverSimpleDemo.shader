Shader "Sirius/SSPRRecieverSimpleDemo"
{
    Properties
    {
        _ReflectionIntensity ("ReflectionIntensity", Range(0, 1)) = 0.2
        _DistortionIntensity ("DistortionIntensity", Range(0, 2)) = 0.2
        _BaseMap ("Base Map", 2D) = "black" {}
        _DistortionMap("Distortion Map", 2D) = "glay" {}
        [MaterialToggle]_UseNormalMap("Use Normal Map As Distortion Map", Float) = 0
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"
        }
        LOD 100

        Pass
        {
            Tags {"LightMode" = "UniversalForward"}
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ _CT_ENABLE_SSPR

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/jp.co.cyberagent.sirius.core/Runtime/Shaders/ReflectionUtils.hlsl"

            struct vert_in
            {
                float3 posisionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct frag_in
            {
                float4 positionCS : SV_POSITION;
                float2 base_uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            float _ReflectionIntensity;
            float _DistortionIntensity;
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_DistortionMap);
            SAMPLER(sampler_DistortionMap);
            float4 _BaseMap_ST;
            float _UseNormalMap;

            frag_in vert(vert_in input)
            {
                frag_in o;
                o.positionWS = TransformObjectToWorld(input.posisionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.base_uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }


            half4 frag(const frag_in input) : SV_Target
            {
                // ベースカラーをサンプリング
                const half4 base_color = SAMPLE_TEXTURE2D_X(_BaseMap, sampler_BaseMap, input.base_uv);
            #ifdef _CT_ENABLE_SSPR
                // スクリーンスペースUVを計算
                // 歪みテクスチャから歪みをサンプリング
                half4 distortion = SAMPLE_TEXTURE2D_X(_DistortionMap, sampler_DistortionMap, input.base_uv);
                float2 distortion_uv;
                if(_UseNormalMap > 0.5f)
                {
                    distortion_uv = UnpackNormal(distortion).rg;
                }else
                {
                    distortion_uv = distortion.rg * 2.0 - 1.0;
                }
                // 反射テクスチャから色をサンプリング
                const half4 reflection_color = LoadScreenSpacePlanarReflectionColor(
                    input.positionWS, distortion_uv * _DistortionIntensity);
                // 反射カラーとベースカラーをブレンド。
                half3 color = lerp(base_color.xyz, reflection_color.xyz, reflection_color.a * _ReflectionIntensity);
            #else
                // ベースカラーのみを出力
                half3 color = base_color.xyz;
            #endif
                return half4(color, base_color.a);
            }
            ENDHLSL
        }
    }
}
