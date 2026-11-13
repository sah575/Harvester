// Dikiz aynası görüntüsünü ekrana basan arayüz shader'ı. Ayna kamerasında LethalLook tam ekran geçişi
// çalışmadığı için (pahalı: derinlik ve renk kopyası ister) aynı posterize + Bayer dither burada, aynanın
// kendi pikselleri üzerinde yapılır. Kontur yok: aynada küçük ve sisli, fark edilmiyor.
// RearViewMirror çalışma zamanında Resources'tan yükler; Resources'ta olduğu için build'e de girer.
Shader "Harvester/RearViewMirrorUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Ayna dokusu", 2D) = "black" {}
        _Color ("Renk", Color) = (1, 1, 1, 1)
        _ColorLevels ("Renk kademesi (kanal başına)", Range(2, 64)) = 14
        _DitherStrength ("Dither gücü", Range(0, 1)) = 0.6
        _PixelGrid ("Aynanın iç çözünürlüğü (piksel)", Vector) = (320, 80, 0, 0)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "True" }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _ColorLevels;
            float _DitherStrength;
            float4 _PixelGrid;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            // 4x4 Bayer matrisi, 0..1 aralığında (LethalLook ile aynı)
            float Bayer4(uint2 p)
            {
                const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
                return (m[(p.y & 3) * 4 + (p.x & 3)] + 0.5) / 16.0;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half3 color = tex2D(_MainTex, i.uv).rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                half3 gamma = LinearToGammaSpace(saturate(color));
                #else
                half3 gamma = saturate(color);
                #endif
                // Dither aynanın iç pikselleri üzerinde: uvRect ile aynalansa da desen kaymaz
                uint2 pixel = uint2(frac(i.uv) * _PixelGrid.xy);
                gamma += (Bayer4(pixel) - 0.5) * _DitherStrength / _ColorLevels;
                gamma = round(saturate(gamma) * _ColorLevels) / _ColorLevels;
                #ifndef UNITY_COLORSPACE_GAMMA
                color = GammaToLinearSpace(gamma);
                #else
                color = gamma;
                #endif
                return fixed4(color, 1) * i.color;
            }
            ENDCG
        }
    }
}
