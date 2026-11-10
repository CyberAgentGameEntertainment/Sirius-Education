Shader "GodRay/GodRayRadialBlur"
{
    SubShader
    {
        HLSLINCLUDE
        #include_with_pragmas "Packages/jp.co.cyberagent.sirius.core/Runtime/Shaders/Common_URP_Free/HAS_URP_Definition.hlsl"
        ENDHLSL

        Pass
        {
            Name "Radial Blur"
            Blend Off
            ZTest Off
            ZWrite Off
            Cull Off
            // 放射ブラー
            HLSLPROGRAM

            // SRP Core 常用ライブラリ集（shim）
            #include "Packages/jp.co.cyberagent.sirius.core/Runtime/Shaders/Common_URP_Free/SiriusCore.hlsl"
            // フルスクリーン blit（Vert/Varyings/_BlitTexture）
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/jp.co.cyberagent.sirius.core/Runtime/Shaders/Common_URP_Free/DeclareDepthTexture.hlsl"

            // #include "GodRayCommon.hlsl"
            // #pragma enable_d3d11_debug_symbols
            #pragma vertex Vert
            #pragma fragment frag

            TEXTURE2D(_BinarizationedTex);
            SAMPLER(sampler_BinarizationedTex);
            float4 _BinarizationedTex_TexelSize;
            TEXTURE2D(_TemporalRadialBlurTex);
            SAMPLER(sampler_TemporalRadialBlurTex);
            float4x4 _MatrixPrevVP;
            uint _SampleCount;
            half _Strength;
            float4 _RadialParams;
            uint _FrameCount; // フレームカウント。ノイズの計算で使用する。
            uint _RadialBlurStartIndex; // ラジアルブラーの開始インデックス。
            half _TemporalBlendWeight; // 前フレームの結果を混ぜる割合。テンポラル合成しない場合は 0。

            struct FragmentOutput
            {
                half4 radialBlurColor;          // このフレームのラジアルブラーの結果
                half4 temporalRadialBlurColor;  // 次のフレームで使うためテンポーラルブラー

            };
            /// <summary>
            /// ボーダーカラーを指定してテクスチャからサンプリングします。
            /// </summary>
            /// <remarks>
            /// この関数はDirectX12でD3D12_TEXTURE_ADDRESS_MODE_BORDERを使用する場合と同等の動作をします。
            /// D3D12_TEXTURE_ADDRESS_MODE_BORDERについては、下記のURLを参照してください。
            /// https://learn.microsoft.com/en-us/windows/win32/api/d3d12/ne-d3d12-d3d12_texture_address_mode
            /// </remarks>
            half4 SamplingTexture2DWithBoarderColor(float2 uv, half4 borderColor)
            {
                float2 clampedUV = clamp(uv, 0.0, 1.0);
                half4 color = all( clampedUV - uv ) ?
                    borderColor : SAMPLE_TEXTURE2D(_BinarizationedTex, sampler_BinarizationedTex, clampedUV);
                return color;
            }
            /// <summary>
            /// UV座標とクリップ空間でのZ座標からワールド行列を計算する。
            /// </summary>
            float3 CalcWorldPosFromUVZ(float2 uv, float zHCS, float4x4 mViewProjInv)
            {
                float3 screenPos;
                screenPos.xy = (uv * float2(2.0, -2.0)) + float2(-1.0, 1.0);
                screenPos.z = zHCS;

                float4 worldPos = mul(mViewProjInv, float4(screenPos, 1.0));
                worldPos.xyz /= worldPos.w;
                return worldPos.xyz;
            }

            FragmentOutput frag(Varyings IN) : SV_Target
            {
                // 少ないサンプル数でも縞（マッハバンド）が出ないよう、サンプル位置を光線方向にずらす。
                // Interleaved-Gradient-NoiseはCall Of Dutyや天穂のサクナヒメで使われているノイズです。
                const float jitter = InterleavedGradientNoise(IN.texcoord * _BinarizationedTex_TexelSize.zw, _FrameCount % 8);

                const half2 symmetryUv =  IN.texcoord - _RadialParams.xy;
                const half distance = min(0.6, length(symmetryUv));
                const half factor = _Strength / _SampleCount * distance;
                half4 radialBlurColor = 0;

                // 1 フレームでは SampleCount の半分（奇数なら開始インデックスで切り上げ・切り捨て）をサンプルする。
                // 光線 [0, SampleCount) をその数で均等に区切り、各区間内をジッタでずらすことで、
                // 偶数・奇数どちらのフレームも光線全体を覆う。
                const uint frameSampleCount = (_SampleCount + 1 - _RadialBlurStartIndex) / 2;
                const float stratumWidth = (float)_SampleCount / frameSampleCount;

                UNITY_LOOP
                for (uint k = 0; k < frameSampleCount; k++)
                {
                    const float position = (k + jitter) * stratumWidth;
                    const half uvOffset = max(0, 1 - factor * position);
                    const float2 sampleUv = _RadialParams.xy + symmetryUv * uvOffset;
                    const half4 addColor = SamplingTexture2DWithBoarderColor(sampleUv, half4(0, 0, 0, 1));
                    // ジッタ後の実際の位置から重みを求め、SampleCount を変えても光芒の長さと明るさが変わらないようにする。
                    // 光線の長さに対する比 t で、σ = 0.35 のガウス分布。
                    const float t = position / _SampleCount;
                    radialBlurColor += addColor * exp(-t * t * (1.0 / (2.0 * 0.35 * 0.35)));
                }

                // 実際の重み和ではなく、その期待値（区間数 × ∫[0,1] exp(-t²/2σ²) dt）で割る。
                // 実際の重み和はジッタでピクセルごとに大きく変わり、それで割ると光芒の長さが SampleCount に依存してしまう。
                radialBlurColor *= rcp(frameSampleCount * 0.43678);

                // テンポーラルのゴースティングの抑制のために、１フレーム前のピクセル座標を計算する。
                // まずはこのピクセルのワールド座標を深度テクスチャから計算する。
                float depth = SampleSceneDepth(IN.texcoord);
                float3 positionWS = CalcWorldPosFromUVZ(IN.texcoord, depth, unity_MatrixInvVP);
                // ワールド座標を計算出来たら、１フレーム前のVP行列で座標変換。
                float4 positionHCSPrev = mul(_MatrixPrevVP, float4(positionWS, 1.0));
                // これで、ピクセルの１フレーム前のスクリーン座標が求まったので、UV座標に変換する。
                float2 uv = positionHCSPrev.xy / positionHCSPrev.w;
                uv = uv * float2(0.5, -0.5) + 0.5;

                // このフレームのラジアルブラーの結果と、テンポーラルなラジアルブラーの結果を合成します。
                half4 temporalBlurColor = SAMPLE_TEXTURE2D(_TemporalRadialBlurTex, sampler_TemporalRadialBlurTex, uv);
                FragmentOutput output;
                // 各フレームとも単体で加重平均の推定値になっているので、2 フレームは平均で合成する
                output.radialBlurColor = lerp(radialBlurColor, temporalBlurColor, _TemporalBlendWeight);

                // このフレームのラジアルブラーの結果をテンポーラルラジアルブラーの結果に反映する。
                output.temporalRadialBlurColor = radialBlurColor;
                return output;
            }
            ENDHLSL
        }
        Pass
        {
            Name "BoxFilter"
            Blend Off
            ZTest Off
            ZWrite Off
            Cull Off

            HLSLPROGRAM

            // SRP Core 常用ライブラリ集（shim）
            #include "Packages/jp.co.cyberagent.sirius.core/Runtime/Shaders/Common_URP_Free/SiriusCore.hlsl"
            // フルスクリーン blit（Vert/Varyings/_BlitTexture）
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #pragma vertex Vert
            #pragma fragment frag

            TEXTURE2D(_RadialBlurTex);
            SAMPLER(sampler_RadialBlurTex);
            float4 _RadialBlurTex_TexelSize;

            half4 frag(Varyings IN) : SV_Target
            {
                const float4 offset = _RadialBlurTex_TexelSize.xyxy * float4(-1.0, -1.0, 1.0, 1.0);
                half4 radialBlurColor = SAMPLE_TEXTURE2D(_RadialBlurTex, sampler_RadialBlurTex, IN.texcoord + offset.xy);
                radialBlurColor += SAMPLE_TEXTURE2D(_RadialBlurTex, sampler_RadialBlurTex, IN.texcoord + offset.zy);
                radialBlurColor += SAMPLE_TEXTURE2D(_RadialBlurTex, sampler_RadialBlurTex, IN.texcoord + offset.xw);
                radialBlurColor += SAMPLE_TEXTURE2D(_RadialBlurTex, sampler_RadialBlurTex, IN.texcoord + offset.zw);
                radialBlurColor *= 0.25;
                return radialBlurColor;
            }

            ENDHLSL
        }

        Pass
        {
            Name "CombineBlend"
            ZTest Off
            ZWrite Off
            Cull Off
            // Additive: (One, One) / Screen: G*(1-Dst) + Dst = (OneMinusDstColor, One)。アルファはシーンの値を保持する
            Blend [_SrcBlend] One, Zero One

            HLSLPROGRAM

            // SRP Core 常用ライブラリ集（shim）
            #include "Packages/jp.co.cyberagent.sirius.core/Runtime/Shaders/Common_URP_Free/SiriusCore.hlsl"
            // フルスクリーン blit（Vert/Varyings/_BlitTexture）
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #pragma vertex Vert
            #pragma fragment frag

            TEXTURE2D(_FinalRadialBlurTex);
            SAMPLER(sampler_FinalRadialBlurTex);
            half4 _GodRayColor;
            half _Intensity;

            half4 frag(Varyings IN) : SV_Target
            {
                return SAMPLE_TEXTURE2D(_FinalRadialBlurTex, sampler_FinalRadialBlurTex, IN.texcoord).r * _GodRayColor * _Intensity;
            }

            ENDHLSL
        }

        Pass
        {
            Name "CombineMultiply"
            ZTest Off
            ZWrite Off
            Cull Off

            HLSLPROGRAM

            // SRP Core 常用ライブラリ集（shim）
            #include "Packages/jp.co.cyberagent.sirius.core/Runtime/Shaders/Common_URP_Free/SiriusCore.hlsl"
            // フルスクリーン blit（Vert/Varyings/_BlitTexture）
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #pragma vertex Vert
            #pragma fragment frag

            TEXTURE2D(_SceneColorTex);
            SAMPLER(sampler_SceneColorTex);
            TEXTURE2D(_FinalRadialBlurTex);
            SAMPLER(sampler_FinalRadialBlurTex);
            half4 _GodRayColor;
            half _Intensity;

            half4 frag(Varyings IN) : SV_Target
            {
                half4 sceneColor = SAMPLE_TEXTURE2D(_SceneColorTex, sampler_SceneColorTex, IN.texcoord);
                half4 godray = SAMPLE_TEXTURE2D(_FinalRadialBlurTex, sampler_FinalRadialBlurTex, IN.texcoord).r * _GodRayColor * _Intensity;
                // Multiply: s*(1+g)。LDR ターゲットではブレンド前に [0,1] へクランプされるため、固定機能ブレンドにはできない
                return sceneColor * godray + sceneColor;
            }

            ENDHLSL
        }
    }
}
