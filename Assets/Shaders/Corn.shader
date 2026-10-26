// Mısır tarlası: GPU instancing ile çizilir. Rüzgâr ve robotlar geçerken eğilme vertex shader'da,
// renk farkları instance pozisyonundan türetilir (per-instance veri gerekmez).
Shader "Harvester/Corn"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.2, 0.3, 0.1, 1)
        _TipColor ("Tip Color", Color) = (0.6, 0.55, 0.28, 1)
        _ColorVariation ("Color Variation", Range(0, 1)) = 0.3
        _Smoothness ("Smoothness", Range(0, 1)) = 0.2
        _WindStrength ("Wind Strength", Float) = 0.15
        _WindSpeed ("Wind Speed", Float) = 1.1
        _BendStrength ("Bend Strength", Float) = 1.4
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _TipColor;
            half _ColorVariation;
            half _Smoothness;
            float _WindStrength;
            float _WindSpeed;
            float _BendStrength;
        CBUFFER_END

        // xyz: iten objenin pozisyonu, w: etki yarıçapı (0 = boş slot). CropField her kare doldurur.
        float4 _CornBenders[4];

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0; // y: yükseklik oranı (0 dip, 1 tepe)
            half4 color : COLOR; // rgb: ton, a: tepe rengine kayma
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        float Hash21(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        float3 InstanceOrigin()
        {
            return TransformObjectToWorld(float3(0, 0, 0));
        }

        float3 DeformWS(float3 positionWS, float height01)
        {
            float3 origin = InstanceOrigin();
            float weight = height01 * height01;

            float phase = Hash21(origin.xz) * 6.2831853;
            float t = _Time.y * _WindSpeed + phase + origin.x * 0.03 + origin.z * 0.02;
            positionWS.x += sin(t) * _WindStrength * weight;
            positionWS.z += sin(t * 0.7 + 1.3) * _WindStrength * 0.6 * weight;

            [unroll]
            for (int i = 0; i < 4; i++)
            {
                float4 bender = _CornBenders[i];
                float2 delta = origin.xz - bender.xz;
                float dist = length(delta);
                float influence = bender.w > 0 ? saturate(1 - dist / bender.w) : 0;
                float2 dir = dist > 0.001 ? delta / dist : float2(1, 0);
                positionWS.xz += dir * influence * _BendStrength * weight;
                positionWS.y -= influence * _BendStrength * 0.35 * weight;
            }
            return positionWS;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _FORWARD_PLUS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2; // rgb: albedo, a: yükseklik oranı
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = DeformWS(TransformObjectToWorld(input.positionOS.xyz), input.uv.y);
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);

                float variation = Hash21(InstanceOrigin().xz * 0.37) * 2 - 1;
                half3 albedo = lerp(_BaseColor.rgb, _TipColor.rgb, saturate(input.uv.y * 0.6 + input.color.a));
                albedo *= input.color.rgb * (1 + variation * _ColorVariation);
                output.color = half4(albedo, input.uv.y);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float3 normalWS = normalize(input.normalWS);
                normalWS = IS_FRONT_VFACE(face, normalWS, -normalWS);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = input.color.rgb;
                surface.alpha = 1;
                surface.specular = half3(0.05, 0.05, 0.05);
                surface.smoothness = _Smoothness;
                surface.occlusion = lerp(0.35, 1.0, saturate(input.color.a * 1.5)); // dipler daha karanlık
                surface.normalTS = half3(0, 0, 1);

                half4 color = UniversalFragmentBlinnPhong(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return half4(color.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            ShadowVaryings ShadowVert(Attributes input)
            {
                ShadowVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);

                float3 positionWS = DeformWS(TransformObjectToWorld(input.positionOS.xyz), input.uv.y);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                output.positionCS = positionCS;
                return output;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            DepthVaryings DepthVert(Attributes input)
            {
                DepthVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionCS = TransformWorldToHClip(DeformWS(TransformObjectToWorld(input.positionOS.xyz), input.uv.y));
                return output;
            }

            half DepthFrag(DepthVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
