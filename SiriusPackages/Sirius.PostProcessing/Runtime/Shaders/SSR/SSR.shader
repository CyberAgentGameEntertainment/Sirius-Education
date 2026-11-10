// Sirius SSR Shader

Shader "Hidden/Sirius/SSR"
{
    SubShader
    {
        // カリング・深度なし
        Cull Off ZWrite Off ZTest Off

        HLSLINCLUDE
        #include_with_pragmas "Packages/jp.co.cyberagent.sirius.core/Runtime/Shaders/Common_URP_Free/HAS_URP_Definition.hlsl"
        ENDHLSL

        // Hi-Z トレースのみで反射色とマスクを求める
        Pass        // 0
        {
            Name "HiZ SSR"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            // #pragma enable_d3d11_debug_symbols

            // Defferd Shading用
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            // downSample無効時はFullSize本体を仮想lod0として読む(Volumeで実行時切替)
            #pragma multi_compile_fragment _ _CT_DEPTH_PYRAMID_FULLSIZE

            #include "SSRCommon.hlsl"
            #include "Packages/jp.co.cyberagent.sirius.core/Runtime/Shaders/Common_URP_Free/DeclareNormalsTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/jp.co.cyberagent.sirius.core/Runtime/Shaders/DepthMipMapUtil/DeclareDepthMipMap.hlsl"

            float GetMinDepthPlane(uint2 cellIdx, uint level)
            {
                float depth = CT_LoadDepthPyramid_Lod(cellIdx, level);
                // near = 0, far = 1 統一
                #if UNITY_REVERSED_Z
                depth = 1 - depth;
                #endif
                return depth;
            }

            float3 IntersectCellBoundary(float3 o, float3 d, float2 cellIndex, float2 cellCount, float2 crossStep, float2 crossOffset)
            {
                float2 cell_size = 1.0 / cellCount;
                float2 planes = cellIndex / cellCount + cell_size * crossStep;
                float2 solutions = (planes - o.xy) / d.xy;
                float3 intersection_pos = o + d * min(solutions.x, solutions.y);

                intersection_pos.xy += (solutions.x < solutions.y) ? float2(crossOffset.x, 0.0) : float2(0.0, crossOffset.y);
                return intersection_pos;
            }

            float3 HiZTrace(float thickness, in float3 p, in float3 v, uint maxSteps, out float hit)
            {
                const int rootLevel = HIZ_MAX_LEVEL;
                const int endLevel = HIZ_STOP_LEVEL;
                const int startLevel = HIZ_START_LEVEL;
                int level = startLevel;
                hit = 0.0;

                UNITY_BRANCH
                if (v.z <= 0)
                {
                    return float3(0, 0, 0);
                }

                // z が 1.0f（最大深度）になるようベクトルをスケールする
                float3 d = v / v.z;

                // セル横断の方向と、横断時に次のセルへ入るための小さなオフセットを求める
                float2 crossStep = float2(d.x >= 0.0f ? 1.0f : -1.0f, d.y >= 0.0f ? 1.0f : -1.0f);
                float2 crossOffset = crossStep * GetCrossEpsilon();
                crossStep = saturate(crossStep);

                // 現在のレイを元のスクリーン座標と深度に設定する
                float3 ray = p;

                // 直後の自己交差を避けるため次のセルへ進める
                uint2 rayCell = GetCellIndex(ray.xy, GetCellCount(level));
                ray = IntersectCellBoundary(ray, d, rayCell, GetCellCount(level), crossStep, crossOffset);

                uint iterations = 0;

                UNITY_LOOP
                while (level >= endLevel
                    && iterations < maxSteps
                    && ray.x >= 0 && ray.x < 1
                    && ray.y >= 0 && ray.y < 1
                    && ray.z > 0)
                {
                    // 現在のレイのセル番号を求める
                    const uint2 cellCount = GetCellCount(level);
                    const uint2 oldCellIdx = GetCellIndex(ray.xy, cellCount);

                    // 現在のレイがある最小深度面を求める
                    // (セル判定と同じ整数座標でテクセルを取る)
                    float minZ = GetMinDepthPlane(oldCellIdx, level);

                    // レイの深度が最小深度面より奥の場合のみ交差させる
                    float3 tmpRay = ray;

                    float minMinusRay = minZ - ray.z;

                    tmpRay = minMinusRay > 0 ? IntersectDepthPlane(tmpRay, d, minMinusRay) : tmpRay;
                    // 新しいセル番号も求める
                    const uint2 newCellIdx = GetCellIndex(tmpRay.xy, cellCount);
                    // 新旧のセル番号が異なればセルを横断している

                    UNITY_BRANCH
                    if (CheckCrossedCellBoundary(oldCellIdx, newCellIdx))
                    {
                        // 代わりにそのセルの境界と交差させ、次の反復で大きく進めるようレベルを 1 つ上げる
                        tmpRay = IntersectCellBoundary(ray, d, oldCellIdx, cellCount, crossStep, crossOffset);
                        level = min(rootLevel, level + 2);
                    }
                    else if (level == startLevel)
                    {
                        float minZOffset = (minZ + (1 - p.z) * thickness);
                        bool isSky = minZ == 1;
                        UNITY_BRANCH
                        if (_IsReflectSky == 0 && isSky)
                        {
                            break;
                        }
                        UNITY_FLATTEN
                        if (tmpRay.z > minZOffset)
                        {
                            tmpRay = IntersectCellBoundary(ray, d, oldCellIdx, cellCount, crossStep, crossOffset);
                            level = startLevel + 1;
                        }
                    }
                    // Hi-Z バッファのレベルを 1 つ下げる
                    --level;

                    ray = tmpRay;
                    ++iterations;
                }
                hit = level < endLevel ? 1 : 0;
                hit = iterations > 0 ? hit : 0;
                return ray;
            }

            float4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float2 screenUV = i.texcoord;

                float rawDepth = CT_LoadDepthPyramidUV_Lod(screenUV, HIZ_START_LEVEL);

                UNITY_BRANCH
                if (rawDepth == DEPTH_FAR)
                {
                    return float4(screenUV, 0, 0);
                }

                float smoothness = SampleSmoothness(screenUV);
                bool doRayMarch = smoothness > _MinSmoothness;

                UNITY_BRANCH
                if (!doRayMarch)
                {
                    return float4(screenUV, 0, 0);
                }

                float3 normal = SampleSceneNormals(screenUV);
                float4 positionCS = ComputeClipSpacePosition(screenUV, rawDepth);
                float4 positionVS = mul(UNITY_MATRIX_I_P, positionCS);
                positionVS /= positionVS.w;
                float3 positionWS = ComputeWorldSpacePosition(positionCS, UNITY_MATRIX_I_VP);
                float3 viewDirWS = -SSR_GetWorldSpaceNormalizeViewDir(positionWS);  // positionWSに向かわせるために反転
                float3 reflectedDirWS = reflect(viewDirWS, normal);
                float3 reflectedDirVS = TransformWorldToViewDir(reflectedDirWS);
                float3 reflectedEndPosVS = positionVS.xyz + reflectedDirVS * -positionVS.z;
                float4 reflectedEndPosCS = TransformWViewToHClip(reflectedEndPosVS);
                reflectedEndPosCS /= reflectedEndPosCS.w;

                #if UNITY_REVERSED_Z
                reflectedEndPosCS.z = 1 - reflectedEndPosCS.z;
                positionCS.z = 1 - positionCS.z;
                #endif

                float3 outReflDirInTS = normalize((reflectedEndPosCS - positionCS).xyz);
                outReflDirInTS.xy *= 0.5;

                #if UNITY_UV_STARTS_AT_TOP
                outReflDirInTS.y *= -1;
                #endif

                float3 outSamplePosInTS = float3(screenUV, positionCS.z);

                float3 viewForwardDir = SSR_GetViewForwardDir();
                float ddd = saturate(dot(viewForwardDir, reflectedDirWS));
                float thickness = 0.01f * (1 - ddd);
                float mask = smoothstep(0, 0.1f, ddd);

                UNITY_BRANCH
                if (mask == 0)
                {
                    return float4(screenUV, 0, 0);
                }

                float hit = 0;
                float3 intersectPoint = HiZTrace(thickness, outSamplePosInTS, outReflDirInTS, _MaxSteps, hit);

                float2 realIntersectUv = intersectPoint.xy;
                float edgeMask = GetScreenEdgeMask(realIntersectUv.xy * 2 - 1);

                mask *= hit * edgeMask;
                return float4(realIntersectUv, mask, 0);
            }
            ENDHLSL
        }

        // 合成
        Pass        // 1
        {
            Name "SSR Composite"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            // #pragma enable_d3d11_debug_symbols

            #include "SSRCommon.hlsl"
            #include "Packages/jp.co.cyberagent.sirius.core/Runtime/Shaders/Common_URP_Free/DeclareDepthTexture.hlsl"
            #include "Packages/jp.co.cyberagent.sirius.core/Runtime/Shaders/Common_URP_Free/DeclareNormalsTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float2 uv = i.texcoord;
                float2 pixelCoord = i.positionCS.xy;

                half4 sourceColor = LOAD_TEXTURE2D_X(_BlitTexture, pixelCoord);
                float rawDepth = LoadSceneDepth(pixelCoord);

                UNITY_BRANCH
                if (rawDepth == DEPTH_FAR) {
                    return sourceColor;
                }
                float3 positionWS = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                float3 viewDirWS = -SSR_GetWorldSpaceNormalizeViewDir(positionWS);  // positionWSに向かわせるために反転

                // スクリーン空間の法線とスムースネスを取得する
                float3 normalWS = LoadSceneNormals(pixelCoord);
                half smooth = SampleSmoothness(uv);
                half stepS = smoothstep(_MinSmoothness, 1, smooth);
                half fresnal = 1 - dot(viewDirWS, -normalWS);

                float3 normalCS = TransformWorldToHClipDir(normalWS);
                #if UNITY_UV_STARTS_AT_TOP
                normalCS.y *= -1;
                #endif

                // ディザの計算
                float dither;
                // type 0 = 元のマスクをそのまま使う
                // type 1 = 元のマスクをディザリングする
                uint type = _DitherTarget;

                UNITY_BRANCH
                if (_DitherMode == 0)
                {
                    dither = Dither8x8(pixelCoord, 0.5);
                }
                else
                {
                    dither = InterleavedGradientNoise(pixelCoord, _Frame);
                }
                dither *= 2;
                dither -= 1;
                //////////////////////

                // ディザリングした UV 座標を取得する
                float stepSSqrd = stepS * stepS;
                const float2 uvOffset = (normalCS * lerp(dither * 0.05, 0, stepSSqrd)).xy;
                float2 sampleUV = uv + uvOffset * type;
                float3 reflectedUV = SampleReflectedUV(sampleUV);
                float maskVal = saturate(reflectedUV.z) * stepS;
                reflectedUV.xy += uvOffset * (1 - type);

                // エミッシブマテリアル用の輝度マスクを取得する
                half lumin = saturate(RGB2Lum(sourceColor.rgb) - 1);
                half luminMask = 1 - lumin;
                luminMask = pow(luminMask, 5);

                // メタリック・AO・スペキュラーカラーを取得する
                half3 diffuseColor = SampleDiffuseColor(uv);
                half metallic = SampleMetalic(uv);
                half ao = SampleAO(uv);

                // フレネルを計算する
                half fresnalMask = 1 - saturate(RGB2Lum(diffuseColor));
                fresnalMask = lerp(1, fresnalMask, metallic);
                fresnal = lerp(1, fresnal * fresnal, fresnalMask);

                // メタリックのブレンド用の値
                const half lMet = 0.3;
                const half hMet = 1.0;
                const half lSpecCol = 0.0;
                const half hSpecCol = 0.6;

                // スムースネスのブレンド用の値
                const half blurL = 0.0;
                const half blurH = 5.0;
                const half blurPow = 4;

                // 色を合成する
                half4 specularColor = half4(1, 1, 1, 1);
                specularColor.rgb = lerp(half3(1, 1, 1), diffuseColor, lerp(lSpecCol, hSpecCol, metallic));

                half fm = clamp(metallic, lMet, hMet);
                half ff = 1 - fm;
                half roughnessBlurAmount = lerp(blurL, blurH, 1 - pow(stepS, blurPow));
                uint lod = round(roughnessBlurAmount);
                half4 reflectedColor = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, reflectedUV.xy, lod);

                half refw = maskVal * ao * fresnal * luminMask;

                half4 blendedColor = sourceColor * ff + (reflectedColor * specularColor) * fm;

                half4 result = lerp(sourceColor, blendedColor, refw);
                return result;
            }
            ENDHLSL
        }
    }
}
