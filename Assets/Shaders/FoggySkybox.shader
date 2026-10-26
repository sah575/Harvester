// Skybox/Cubemap'in aynısı, ufka doğru sahnenin sis rengine (RenderSettings.fogColor) karışır.
// Normal sis gökyüzüne uygulanmadığı için ufuk çizgisi keskin kalıyordu; bu onu yumuşatır.
Shader "Harvester/FoggySkybox"
{
    Properties
    {
        _Tint ("Renk tonu", Color) = (0.5, 0.5, 0.5, 0.5)
        [Gamma] _Exposure ("Pozlama", Range(0, 8)) = 1.0
        _Rotation ("Dönüş", Range(0, 360)) = 0
        [NoScaleOffset] _Tex ("Cubemap (HDR)", Cube) = "grey" {}
        _HorizonFogStart ("Ufuk sisi bitişi (yön y, 0 = ufuk)", Range(-0.5, 0.5)) = 0.0
        _HorizonFogEnd ("Ufuk sisi üst sınırı (yön y)", Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"

            // Skybox/Cubemap'teki unity_ColorSpaceDouble'ın linear uzaydaki değeri (proje linear)
            #define COLOR_SPACE_DOUBLE 4.59479380

            TEXTURECUBE(_Tex);
            SAMPLER(sampler_Tex);

            CBUFFER_START(UnityPerMaterial)
            half4 _Tex_HDR;
            half4 _Tint;
            half _Exposure;
            float _Rotation;
            float _HorizonFogStart;
            float _HorizonFogEnd;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float3 RotateAroundY(float3 v, float degrees)
            {
                float s, c;
                sincos(radians(degrees), s, c);
                return float3(c * v.x - s * v.z, v.y, s * v.x + c * v.z);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(RotateAroundY(input.positionOS.xyz, _Rotation));
                output.direction = input.positionOS.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 encoded = SAMPLE_TEXTURECUBE(_Tex, sampler_Tex, input.direction);
                half3 color = DecodeHDREnvironment(encoded, _Tex_HDR) * _Tint.rgb * COLOR_SPACE_DOUBLE * _Exposure;

                // Ufukta tamamen sis rengi, yukarı çıktıkça gökyüzü
                float height = normalize(input.direction).y;
                half fog = 1.0 - smoothstep(_HorizonFogStart, _HorizonFogEnd, height);
                color = lerp(color, unity_FogColor.rgb, fog);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
