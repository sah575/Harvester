// Lethal Company tarzı tam ekran efekt: derinlikten siluet konturu + renk kademeleme (posterize) + sıralı dithering.
// URP Full Screen Pass renderer özelliğiyle post-processing'den sonra çalışır (LookBuilder kurar).
// Kontur sadece derinlikten hesaplanır: normal ön geçişinde mısır olmadığı için normal kullanmak
// mısırın arkasındaki robotların konturunu gösterirdi.
Shader "Harvester/LethalLook"
{
    Properties
    {
        _OutlineColor ("Kontur rengi", Color) = (0.02, 0.02, 0.03, 1)
        _OutlineStrength ("Kontur gücü", Range(0, 1)) = 0.85
        _OutlineThreshold ("Kontur eşiği (bağıl derinlik farkı)", Range(0.01, 1)) = 0.2
        _OutlineFadeStart ("Kontur sönme başlangıcı (m)", Float) = 12
        _OutlineFadeEnd ("Kontur sönme sonu (m)", Float) = 40
        _ColorLevels ("Renk kademesi (kanal başına)", Range(2, 64)) = 14
        _DitherStrength ("Dither gücü", Range(0, 1)) = 0.6
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Blend Off Cull Off

        Pass
        {
            Name "LethalLook"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            half4 _OutlineColor;
            half _OutlineStrength;
            float _OutlineThreshold;
            float _OutlineFadeStart;
            float _OutlineFadeEnd;
            float _ColorLevels;
            half _DitherStrength;

            float EyeDepth(float2 uv)
            {
                return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            }

            // 4x4 Bayer matrisi, 0..1 aralığında
            float Bayer4(uint2 p)
            {
                const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
                return (m[(p.y & 3) * 4 + (p.x & 3)] + 0.5) / 16.0;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).rgb;

                // Siluet: komşulardan biri merkezden belirgin şekilde uzaksa burası yakındaki objenin kenarı
                float2 texel = _CameraDepthTexture_TexelSize.xy;
                float center = EyeDepth(uv);
                float farthest = max(max(EyeDepth(uv + float2(texel.x, 0)), EyeDepth(uv - float2(texel.x, 0))),
                                     max(EyeDepth(uv + float2(0, texel.y)), EyeDepth(uv - float2(0, texel.y))));
                float edge = step(center * _OutlineThreshold, farthest - center);
                float fade = 1.0 - smoothstep(_OutlineFadeStart, _OutlineFadeEnd, center);
                color = lerp(color, _OutlineColor.rgb, edge * fade * _OutlineStrength);

                // Posterize gama uzayında yapılır ki karanlık tonlar da kademelensin
                half3 gamma = LinearToSRGB(saturate(color));
                uint2 pixel = uint2(uv * _CameraDepthTexture_TexelSize.zw);
                gamma += (Bayer4(pixel) - 0.5) * _DitherStrength / _ColorLevels;
                gamma = round(saturate(gamma) * _ColorLevels) / _ColorLevels;
                return half4(SRGBToLinear(gamma), 1);
            }
            ENDHLSL
        }
    }
}
