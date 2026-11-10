Shader "Hidden/Sirius/UtilBlur"
{
    HLSLINCLUDE
    #pragma exclude_renderers gles

    #define DISABLE_TEXTURE2D_X_ARRAY
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureXR.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Packages/jp.co.cyberagent.sirius.core/Runtime/Shaders/FilterUtils.hlsl"

    TEXTURE2D_X(_SourceTex);
    float4 _SourceTex_TexelSize;
    TEXTURE2D_X(_SourceTexLowMip);
    float4 _SourceTexLowMip_TexelSize;

    half _TexelSizeScale = 1.0f;
    half _LowMipBlendRate;

    half4 frag_down_sampling(const Varyings input) : SV_Target
    {
        const float2 uv = input.texcoord;
        half4 color = BoxFilter4Tap(TEXTURE2D_X_ARGS(_SourceTex, sampler_LinearClamp), uv, _SourceTex_TexelSize.xy);
        return color;
    }

    half4 frag_up_sampling(const Varyings input) : SV_Target
    {
        const float2 uv = input.texcoord;
        // _TexelSizeScale = 1 で高解像度テクスチャで2テクセル分の幅。0.5fで1テクセル分の幅。
        // 値を大きくするとステップ数を増やさずにブラーを強められるがアーティファクトが増えるため注意。
        const half4 low_mip = BoxFilter4Tap(
            TEXTURE2D_X_ARGS(_SourceTexLowMip, sampler_LinearClamp), uv,
            _SourceTexLowMip_TexelSize.xy * _TexelSizeScale);
        const half4 high_mip = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv);
        half4 color = lerp(high_mip, low_mip, _LowMipBlendRate);
        return color;
    }
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
        }
        LOD 100
        ZTest Always ZWrite Off Cull Off

        Pass // 0
        {
            Name "DownSampling"
            HLSLPROGRAM
            //#pragma enable_d3d11_debug_symbols
            #pragma vertex Vert
            #pragma fragment frag_down_sampling
            ENDHLSL
        }

        Pass // 1
        {
            Name "UpSampling"
            HLSLPROGRAM
            //#pragma enable_d3d11_debug_symbols
            #pragma vertex Vert
            #pragma fragment frag_up_sampling
            ENDHLSL
        }
    }
}
