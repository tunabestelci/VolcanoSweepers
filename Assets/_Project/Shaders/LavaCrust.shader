Shader "VolcanoSweepers/LavaCrust"
{
    Properties
    {
        _LavaMap("Lava", 2D) = "white" {}
        _ObsMap("Obsidian Base", 2D) = "black" {}
        _ObsNormal("Obsidian Normal", 2D) = "bump" {}
        _ObsMS("Obsidian Metal(R) Smooth(A)", 2D) = "white" {}
        _Mask("Mask (R crust, G dig)", 2D) = "black" {}
        _LavaTiling("Lava meters per tile", Float) = 3.5
        _ObsTiling("Obsidian meters per tile", Float) = 2.5
        _FieldSize("Field size (m)", Float) = 20
        _CrustHeight("Crust height", Float) = 0.14
        _DigDepth("Dig depth", Float) = 3
        _EmissionStrength("Emission", Float) = 1.3
        _FlowSpeed("Flow speed", Float) = 0.015
        _PoolRadius("Pool radius", Float) = 9
        _Bedrock("Bedrock color", Color) = (0.07, 0.06, 0.06, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_LavaMap);   SAMPLER(sampler_LavaMap);
            TEXTURE2D(_ObsMap);    SAMPLER(sampler_ObsMap);
            TEXTURE2D(_ObsNormal); SAMPLER(sampler_ObsNormal);
            TEXTURE2D(_ObsMS);     SAMPLER(sampler_ObsMS);
            TEXTURE2D(_Mask);      SAMPLER(sampler_Mask);

            CBUFFER_START(UnityPerMaterial)
                float4 _Mask_TexelSize;
                float _LavaTiling;
                float _ObsTiling;
                float _FieldSize;
                float _CrustHeight;
                float _DigDepth;
                float _EmissionStrength;
                float _FlowSpeed;
                float _PoolRadius;
                float4 _Bedrock;
            CBUFFER_END

            // Kasik/spatula izi (ScrapeMarks): global, 1.5 cm cozunurluklu pencere. xy: pencere kosesi (dunya xz), zw: 1/boyut
            TEXTURE2D(_ScrapeMarks); SAMPLER(sampler_ScrapeMarks);
            float4 _ScrapeRect;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
            };

            float CrustShape(float c) { return smoothstep(0.3, 0.6, c); }

            float3 LavaRamp(float t)
            {
                float fw = max(fwidth(t), 0.012);
                float3 c = float3(0.42, 0.04, 0.02);
                c = lerp(c, float3(0.85, 0.2, 0.04), smoothstep(0.22 - fw, 0.22 + fw, t));
                c = lerp(c, float3(1.0, 0.45, 0.07), smoothstep(0.42 - fw, 0.42 + fw, t));
                c = lerp(c, float3(1.0, 0.72, 0.16), smoothstep(0.62 - fw, 0.62 + fw, t));
                c = lerp(c, float3(1.0, 0.93, 0.5), smoothstep(0.84 - fw, 0.84 + fw, t));
                return c;
            }

            float HeightAtUV(float2 uv)
            {
                float4 m = SAMPLE_TEXTURE2D_LOD(_Mask, sampler_Mask, uv, 0);
                float cs = CrustShape(m.r);
                // Akan lav: genis, yavas dalgalar (kabuklu yerde ve kazilmis cukurda sakin)
                float2 mt = uv * _FieldSize;
                float w = sin(mt.x * 0.55 + _Time.y * 0.8) * sin(mt.y * 0.43 - _Time.y * 0.6) * 0.05
                        + sin(mt.x * 0.21 - mt.y * 0.33 + _Time.y * 0.5) * 0.07
                        + sin(mt.x * 1.3 + mt.y * 0.9 + _Time.y * 1.7) * 0.015;
                w *= (1.0 - cs) * smoothstep(0.01, 0.06, m.a - m.g);
                return -m.g * _DigDepth + cs * _CrustHeight + w;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 pos = IN.positionOS.xyz;
                pos.y += HeightAtUV(IN.uv);
                float3 posWS = TransformObjectToWorld(pos);
                OUT.positionWS = posWS;
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.uv = IN.uv;
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            half4 frag(Varyings IN) : SV_Target
            {
                // Duzensiz havuz kenari
                float2 meters = IN.uv * _FieldSize;
                float4 m = SAMPLE_TEXTURE2D(_Mask, sampler_Mask, IN.uv);
                clip(m.b - 0.5);                      // yalniz krater icindeki lav golu

                // Yuzey egimi (yukseklik farkindan)
                float e = _Mask_TexelSize.x;
                float hL = HeightAtUV(IN.uv - float2(e, 0));
                float hR = HeightAtUV(IN.uv + float2(e, 0));
                float hD = HeightAtUV(IN.uv - float2(0, e));
                float hU = HeightAtUV(IN.uv + float2(0, e));
                float dx = 2.0 * e * _FieldSize;
                float3 nGeo = normalize(float3((hL - hR) / dx, 1.0, (hD - hU) / dx));

                // Lav: iki katman, ters yonde akar
                float t = _Time.y * _FlowSpeed;
                float2 luv = meters / _LavaTiling;
                // Akiskan lav: dokular birbirine gore kayar ve dusuk frekansli bir gurultuyle buruslur
                float2 warp1 = (SAMPLE_TEXTURE2D(_LavaMap, sampler_LavaMap, luv * 0.31 + float2(t * 0.5, -t * 0.35)).rg - 0.5) * 0.55;
                float2 warp2 = (SAMPLE_TEXTURE2D(_LavaMap, sampler_LavaMap, luv * 0.47 + float2(-t * 0.4, t * 0.55)).gb - 0.5) * 0.4;
                float3 lava1 = SAMPLE_TEXTURE2D(_LavaMap, sampler_LavaMap, luv + warp1 + float2(t, t * 0.6)).rgb;
                float3 lava2 = SAMPLE_TEXTURE2D(_LavaMap, sampler_LavaMap, luv * 0.63 + warp2 + float2(-t * 0.7, t)).rgb;
                float3 lava3 = SAMPLE_TEXTURE2D(_LavaMap, sampler_LavaMap, luv * 1.9 + warp1 + warp2 + float2(t * 0.6, -t * 1.1)).rgb;
                float3 lava = lava1 * 0.38 + lava2 * 0.38 + lava3 * 0.24;
                // Genis olcekli parlaklik degisimi: tekrar eden izgara gorunmesin
                float big = SAMPLE_TEXTURE2D(_LavaMap, sampler_LavaMap, meters * 0.011 + float2(t * 0.15, t * 0.1)).r;
                lava = saturate(lava * lava * 1.4 * (0.75 + 0.55 * big));
                // Cel-shade: lav parlakligi birkac net renk bandina (koyu kirmizi -> turuncu -> sari) ayrilir
                lava = LavaRamp(saturate((dot(lava, float3(0.3, 0.59, 0.11)) - 0.32) * 2.3));

                // Kabuk: gurultuyle duzensiz kenar
                float noise = SAMPLE_TEXTURE2D(_LavaMap, sampler_LavaMap, meters / 1.7).r;
                float c = smoothstep(0.28, 0.5, m.r + (noise - 0.5) * 0.28);
                float lavaAmt = smoothstep(0.004, 0.035, m.a - m.g);

                float2 ouv = meters / _ObsTiling;
                float3 obs = SAMPLE_TEXTURE2D(_ObsMap, sampler_ObsMap, ouv).rgb;
                float4 obsMS = SAMPLE_TEXTURE2D(_ObsMS, sampler_ObsMS, ouv);
                float3 nTS = UnpackNormal(SAMPLE_TEXTURE2D(_ObsNormal, sampler_ObsNormal, ouv));

                float3 T = normalize(cross(float3(0, 0, 1), nGeo));
                float3 B = cross(nGeo, T);
                float3 nObs = normalize(T * nTS.x + B * nTS.y + nGeo * nTS.z);
                float3 normalWS = normalize(lerp(nGeo, nObs, c * lavaAmt));

                float3 albedo = lerp(lava * 0.35, obs, c);
                float smooth = lerp(0.25, obsMS.a, c);
                float metallic = lerp(0.0, obsMS.r, c);

                // Kasik izi: ust tabakasi soyulmus taze obsidyen (daha acik, cam gibi parlak), hafif cukur ve kenarinda murekkep cizgisi
                float2 suv = (IN.positionWS.xz - _ScrapeRect.xy) * _ScrapeRect.zw;
                if (_ScrapeRect.z > 0.0 && suv.x > 0.0 && suv.y > 0.0 && suv.x < 1.0 && suv.y < 1.0)
                {
                    float tu = 0.015 * _ScrapeRect.z;
                    float s0  = SAMPLE_TEXTURE2D_LOD(_ScrapeMarks, sampler_ScrapeMarks, suv, 0).r;
                    float sxp = SAMPLE_TEXTURE2D_LOD(_ScrapeMarks, sampler_ScrapeMarks, suv + float2(tu, 0), 0).r;
                    float sxn = SAMPLE_TEXTURE2D_LOD(_ScrapeMarks, sampler_ScrapeMarks, suv - float2(tu, 0), 0).r;
                    float szp = SAMPLE_TEXTURE2D_LOD(_ScrapeMarks, sampler_ScrapeMarks, suv + float2(0, tu), 0).r;
                    float szn = SAMPLE_TEXTURE2D_LOD(_ScrapeMarks, sampler_ScrapeMarks, suv - float2(0, tu), 0).r;
                    float crustK = c * lavaAmt;
                    float scr = smoothstep(0.1, 0.55, s0) * crustK;
                    float2 g = float2(sxp - sxn, szp - szn);
                    // soyulan yer cevresinden alcak: kenar basamagi isigi yakalar
                    normalWS = normalize(lerp(normalWS, nGeo, scr * 0.8) + float3(g.x, 0.0, g.y) * 0.3 * crustK);
                    // ince kazima cizikleri (yuksek frekansli doku)
                    float streak = SAMPLE_TEXTURE2D(_ObsMap, sampler_ObsMap, meters * 3.1).r;
                    // taze yuzey: ayni obsidyen dokusu ama tozsuz, biraz acik ve serin tonlu, cila gibi parlak
                    float3 fresh = obs * 0.7 + float3(0.075, 0.07, 0.095) * (0.8 + 0.4 * streak);
                    albedo = lerp(albedo, fresh, scr);
                    smooth = lerp(smooth, 0.96, scr);
                    metallic = lerp(metallic, 0.0, scr);
                    float ink = smoothstep(0.05, 0.2, s0) * (1.0 - smoothstep(0.28, 0.55, s0)) * crustK;
                    albedo = lerp(albedo, float3(0.006, 0.005, 0.008), ink * 0.95);
                    smooth = lerp(smooth, 0.2, ink);
                }

                float pulse = 0.85 + 0.2 * sin(_Time.y * 1.7 + noise * 6.0);
                float edge = smoothstep(0.0, 0.3, c) * (1.0 - smoothstep(0.55, 0.95, c));
                float3 emission = lava * _EmissionStrength * 0.8 * pulse * (1.0 - c);
                emission += float3(1.0, 0.35, 0.05) * edge * 0.9 * (1.0 - c * 0.3);

                // Ana kaya (dibe ulasildi)
                albedo = lerp(_Bedrock.rgb, albedo, lavaAmt);
                emission *= lavaAmt;

                // Kiyi: lavin kayayla bulustugu yerde koyu murekkep seridi (cel-shade cizgisi)
                float shore = 1.0 - smoothstep(0.5, 0.68, m.b);
                albedo = lerp(albedo, float3(0.03, 0.014, 0.01), shore);
                emission *= 1.0 - shore;
                smooth = lerp(0.12, smooth, lavaAmt);
                metallic *= lavaAmt;

                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord = InitializeInputDataFog(float4(IN.positionWS, 1.0), IN.fogFactor);
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.metallic = metallic;
                s.specular = half3(0, 0, 0);
                s.smoothness = smooth;
                s.occlusion = 1;
                s.emission = emission;
                s.alpha = 1;
                s.normalTS = half3(0, 0, 1);

                half4 col = UniversalFragmentPBR(inputData, s);
                col.rgb = MixFog(col.rgb, inputData.fogCoord);
                col.a = 1;
                return col;
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
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment fragDepth

            half fragDepth(Varyings IN) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_Mask, sampler_Mask, IN.uv).b - 0.5);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment fragDN
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            half4 fragDN(Varyings IN) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_Mask, sampler_Mask, IN.uv).b - 0.5);
                float3 n = float3(0, 1, 0);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = PackNormalOctQuadEncode(n);
                float2 r = saturate(oct * 0.5 + 0.5);
                return half4(PackFloat2To888(r), 0.0);
            #else
                return half4(n, 0.0);
            #endif
            }
            ENDHLSL
        }
    }
    Fallback Off
}
