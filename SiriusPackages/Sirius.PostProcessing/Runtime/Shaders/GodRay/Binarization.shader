Shader "GodRay/Binarization"
{
    SubShader
    {
        HLSLINCLUDE
        #include_with_pragmas "Packages/jp.co.cyberagent.sirius.core/Runtime/Shaders/Common_URP_Free/HAS_URP_Definition.hlsl"
        ENDHLSL

        Pass
        {
            Name "Binarization"

            ZTest Off
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            // SRP Core 常用ライブラリ集（shim）
            #include "Packages/jp.co.cyberagent.sirius.core/Runtime/Shaders/Common_URP_Free/SiriusCore.hlsl"
            // フルスクリーン blit（Vert/Varyings/_BlitTexture）
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/jp.co.cyberagent.sirius.core/Runtime/Shaders/Common_URP_Free/DeclareDepthTexture.hlsl"

            TEXTURE2D(_SkyboxTex);
            half _Threshold;
            float2 _RadialBlurCenter;
            float4 _ViewRayParams; // (1/P00, 1/P11, P02, P12)
            float3 _PhaseParams; // ((1-g)^2, 1+g^2, 2g)

            // ビュー空間の視線方向。行列のグローバル設定に頼らないので非 URP 経路でもそのまま動く
            float3 ViewDirVS(float2 uv)
            {
                return normalize(float3((uv * 2.0 - 1.0 + _ViewRayParams.zw) * _ViewRayParams.xy, -1.0));
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float depth = SampleSceneDepth(IN.texcoord);
                depth = Linear01Depth(depth, _ZBufferParams);
                #define SKYBOX_DEPTH_THRESH 0.9
                const half skyMask = step(SKYBOX_DEPTH_THRESH, depth);

                half4 color = SAMPLE_TEXTURE2D(_SkyboxTex, sampler_LinearClamp, IN.texcoord);
                half brightness = step(_Threshold, max(max(color.r, color.g), color.b));

                // 太陽から離れた明るい雲からも同じ強さで光芒が伸びないよう、太陽との角度で減衰させる。
                // 重みは Henyey-Greenstein の位相関数を太陽方向で 1 に正規化したもので、g = 0 なら減衰しない。
                // 太陽方向も同じ関数で求めるので、UV の上下の向きがプラットフォームで違っても角度は変わらない。
                const float cosTheta = dot(ViewDirVS(IN.texcoord), ViewDirVS(_RadialBlurCenter));
                const float phase = _PhaseParams.x / (_PhaseParams.y - _PhaseParams.z * cosTheta);
                brightness *= phase * sqrt(phase);

                brightness *= skyMask;
                return half4(brightness, brightness, brightness, 1);
            }
            ENDHLSL
        }
    }
}
