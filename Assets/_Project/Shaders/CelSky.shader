// Cel-shade gokyuzu: ufukta lav isigiyla parlayan, yukari dogru koyulasan keskin bantlar + katmanli kul/duman bulutlari.
Shader "VolcanoSweepers/CelSky"
{
    Properties
    {
        _Top("Zenit", Color) = (0.07, 0.015, 0.03, 1)
        _Mid("Orta", Color) = (0.24, 0.055, 0.05, 1)
        _Horizon("Ufuk", Color) = (0.62, 0.20, 0.07, 1)
        _Smoke("Duman", Color) = (0.05, 0.012, 0.02, 1)
        _Bands("Bant sayisi", Float) = 6
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off

        Pass
        {
            Name "CelSky"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _Top, _Mid, _Horizon, _Smoke;
            float _Bands;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.dir = IN.positionOS.xyz;
                return OUT;
            }

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
            }
            float fbm(float2 p)
            {
                float s = 0, a = 0.5;
                for (int k = 0; k < 4; k++) { s += vnoise(p) * a; p = p * 2.03 + 11.7; a *= 0.5; }
                return s;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 d = normalize(IN.dir);
                float h = d.y;
                float az = atan2(d.x, d.z);
                float2 sp = float2(az * 2.2, h * 6.0);

                // yukseklige gore keskin bantlar (dalgali sinirlar)
                float wob = (fbm(sp * 1.4 + float2(0, 3)) - 0.5) * 0.18;
                float t = saturate(pow(saturate(h * 1.25), 0.55) + wob);
                float tq = floor(t * _Bands) / _Bands;
                float3 col = lerp(_Horizon.rgb, _Mid.rgb, saturate(tq * 2.0));
                col = lerp(col, _Top.rgb, saturate(tq * 2.0 - 1.0));

                // katmanli duman/kul bulutlari: net kenarli koyu seritler
                float c1 = fbm(float2(az * 1.6, h * 9.0) + float2(4.1, 0.0));
                float c2 = fbm(float2(az * 2.4 + 9.0, h * 14.0));
                float cloud1 = step(0.56, c1) * smoothstep(0.02, 0.22, h) * (1.0 - smoothstep(0.5, 0.85, h));
                float cloud2 = step(0.60, c2) * smoothstep(0.0, 0.12, h) * (1.0 - smoothstep(0.25, 0.55, h));
                col = lerp(col, _Smoke.rgb * 1.8, cloud1 * 0.7);
                col = lerp(col, _Smoke.rgb * 3.0 + _Horizon.rgb * 0.15, cloud2 * 0.55);

                // yer altinda ufuk rengi
                col = lerp(col, _Horizon.rgb * 0.7, saturate(-h * 6.0));
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
