#ifndef _SIRIUS_FILTER_UTILS_HLSL_
#define _SIRIUS_FILTER_UTILS_HLSL_

half4 EncodeHDR(half3 color)
{
    #if _USE_RGBM
    half4 outColor = EncodeRGBM(color);
    #else
    half4 outColor = half4(color, 1.0);
    #endif

    #if UNITY_COLORSPACE_GAMMA
    return half4(sqrt(outColor.xyz), outColor.w); // リニア → ガンマ
    #else
    return outColor;
    #endif
}

half3 DecodeHDR(half4 color)
{
    #if UNITY_COLORSPACE_GAMMA
    color.xyz *= color.xyz; // ガンマ → リニア
    #endif

    #if _USE_RGBM
    return DecodeRGBM(color);
    #else
    return color.xyz;
    #endif
}

// HDR テクスチャをデコードする
// dLDR / RGBM 形式に対応
half3 DecodeHDR(half4 data, half4 decodeInstructions, int colorspaceIsGamma)
{
    // decodeInstructions.w が true ならテクスチャのアルファを考慮する（アルファが RGB に影響する）
    half alpha = decodeInstructions.w * (data.a - 1.0) + 1.0;
    alpha = max(0, alpha);

    // リニアモード非対応なら指数部分を省略できる
    if (colorspaceIsGamma)
        return (decodeInstructions.x * alpha) * data.rgb;

    return (decodeInstructions.x * pow(alpha, decodeInstructions.y)) * data.rgb;
}

// HDR テクスチャをデコードする
// dLDR / RGBM 形式に対応
half3 DecodeHDR(half4 data, half4 decodeInstructions)
{
    #if defined(UNITY_COLORSPACE_GAMMA)
    return DecodeHDR(data, decodeInstructions, 1);
    #else
    return DecodeHDR(data, decodeInstructions, 0);
    #endif
}

// 4 タップのボックスフィルタ
half4 BoxFilter4Tap(TEXTURE2D_X_PARAM(tex, samplerTex), const float2 uv, const float2 uvSampleDist)
{
    float4 offset = uvSampleDist.xyxy * float4(-1.0, -1.0, 1.0, 1.0);
    half4 color = SAMPLE_TEXTURE2D_X(tex, samplerTex, uv + offset.xy);
    color += SAMPLE_TEXTURE2D_X(tex, samplerTex, uv + offset.zy);
    color += SAMPLE_TEXTURE2D_X(tex, samplerTex, uv + offset.xw);
    color += SAMPLE_TEXTURE2D_X(tex, samplerTex, uv + offset.zw);
    color *= 0.25; // 1/4
    return color;
}

half3 BoxFilter4TapHDR(TEXTURE2D_X_PARAM(tex, samplerTex), const float2 uv, const float2 uvSampleDist)
{
    float4 offset = uvSampleDist.xyxy * float4(-1.0, -1.0, 1.0, 1.0);
    half3 color = DecodeHDR(SAMPLE_TEXTURE2D_X(tex, samplerTex, uv + offset.xy));
    color += DecodeHDR(SAMPLE_TEXTURE2D_X(tex, samplerTex, uv + offset.zy));
    color += DecodeHDR(SAMPLE_TEXTURE2D_X(tex, samplerTex, uv + offset.xw));
    color += DecodeHDR(SAMPLE_TEXTURE2D_X(tex, samplerTex, uv + offset.zw));
    color *= 0.25; // 1/4
    return color;
}

// 9 タップのテントフィルタ
half4 TentFilter9Tap(TEXTURE2D_X_PARAM(tex, samplerTex), float2 uv, float2 uvSampleDist)
{
    float4 offset = uvSampleDist.xyxy * float4(1.0, 1.0, -1.0, 0.0);
    half4 color = SAMPLE_TEXTURE2D_X(tex, samplerTex, uv - offset.xy);
    color += SAMPLE_TEXTURE2D_X(tex, samplerTex, uv - offset.wy) * 2.0;
    color += SAMPLE_TEXTURE2D_X(tex, samplerTex, uv - offset.zy);
    color += SAMPLE_TEXTURE2D_X(tex, samplerTex, uv + offset.zw) * 2.0;
    color += SAMPLE_TEXTURE2D_X(tex, samplerTex, uv) * 4.0;
    color += SAMPLE_TEXTURE2D_X(tex, samplerTex, uv + offset.xw) * 2.0;
    color += SAMPLE_TEXTURE2D_X(tex, samplerTex, uv + offset.zy);
    color += SAMPLE_TEXTURE2D_X(tex, samplerTex, uv + offset.wy) * 2.0;
    color += SAMPLE_TEXTURE2D_X(tex, samplerTex, uv + offset.xy);
    color *= 0.0625; // 1/16
    return color;
}

half3 TentFilter9TapHDR(TEXTURE2D_X_PARAM(tex, samplerTex), float2 uv, float2 uvSampleDist)
{
    float4 offset = uvSampleDist.xyxy * float4(1.0, 1.0, -1.0, 0.0);
    half3 color = DecodeHDR(SAMPLE_TEXTURE2D_X(tex, samplerTex, uv - offset.xy));
    color += DecodeHDR(SAMPLE_TEXTURE2D_X(tex, samplerTex, uv - offset.wy)) * 2.0;
    color += DecodeHDR(SAMPLE_TEXTURE2D_X(tex, samplerTex, uv - offset.zy));
    color += DecodeHDR(SAMPLE_TEXTURE2D_X(tex, samplerTex, uv + offset.zw)) * 2.0;
    color += DecodeHDR(SAMPLE_TEXTURE2D_X(tex, samplerTex, uv)) * 4.0;
    color += DecodeHDR(SAMPLE_TEXTURE2D_X(tex, samplerTex, uv + offset.xw)) * 2.0;
    color += DecodeHDR(SAMPLE_TEXTURE2D_X(tex, samplerTex, uv + offset.zy));
    color += DecodeHDR(SAMPLE_TEXTURE2D_X(tex, samplerTex, uv + offset.wy)) * 2.0;
    color += DecodeHDR(SAMPLE_TEXTURE2D_X(tex, samplerTex, uv + offset.xy));
    color *= 0.0625; // 1/16
    return color;
}
#endif
