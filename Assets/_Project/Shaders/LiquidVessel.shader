Shader "VolcanoSweepers/LiquidVessel"
{
    // Kap icindeki sivi: kabin icine yerlestirilen silindiri dunyadaki yatay bir duzlemde (_FillY) keser.
    // Kap egilse de sivi yuzeyi dunyaya gore duz kalir; kesilen yerde ic yuzler "su yuzeyi" gibi boyanir.
    Properties
    {
        _BaseMap("Water Texture", 2D) = "white" {}
        _BumpMap("Water Normal", 2D) = "bump" {}
        _Color("Color", Color) = (0.2, 0.5, 0.8, 1)
        _SurfaceColor("Surface Color", Color) = (0.45, 0.72, 0.95, 1)
        _Smoothness("Smoothness", Range(0, 1)) = 0.9
        _FillY("Fill Plane Y (world)", Float) = 0
        _Ripple("Ripple", Range(0, 2)) = 0.5
        _RimRadius("Rim Radius (local)", Float) = 0.14
        _TiltX("Slosh Tilt X", Float) = 0
        _TiltZ("Slosh Tilt Z", Float) = 0
        _SectionZ("Section Z (test)", Float) = 1000000
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _Color;
                float4 _SurfaceColor;
                float _Smoothness;
                float _FillY;
                float _Ripple;
                float _RimRadius;
                float _TiltX;
                float _TiltZ;
                float _SectionZ;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float fogFactor : TEXCOORD3;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vp = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = vp.positionCS;
                OUT.positionWS = vp.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.fogFactor = ComputeFogFactor(vp.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN, FRONT_FACE_TYPE isFront : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float t = _Time.y;
                // Dalgali seviye: yuzey ince dalgalarla kabin duvarinda hafifce yukselip alcalir
                float wave = _Ripple * 0.0035 * (sin(IN.positionWS.x * 55.0 + t * 7.0) + sin(IN.positionWS.z * 47.0 - t * 6.0));
                float4x4 om0 = GetObjectToWorldMatrix();
                float2 c0 = float2(om0._m03, om0._m23);
                // Calkalanan su: yuzey eksen noktasindan gecen egik bir duzlem (hacim korunur)
                float tiltY = _TiltX * (IN.positionWS.x - c0.x) + _TiltZ * (IN.positionWS.z - c0.y);
                clip(_FillY + tiltY + wave - IN.positionWS.y);
                clip(_SectionZ - IN.positionWS.z);
                bool front = IS_FRONT_VFACE(isFront, true, false);

                float3 normalWS;
                float3 albedo;
                float smoothness = _Smoothness;
                if (front)
                {
                    normalWS = normalize(IN.normalWS);
                    albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb * _Color.rgb;
                }
                else
                {
                    // Su yuzeyi: bakis isinini _FillY duzlemiyle kesistir (duvardaki gorunen noktadan degil)
                    float3 camPos = GetCameraPositionWS();
                    float3 dir = normalize(IN.positionWS - camPos);
                    float3 P = IN.positionWS;
                    if (dir.y < -1e-3) P = camPos + dir * ((_FillY - camPos.y) / dir.y);

                    float2 w1 = P.xz * 5.5 + float2(t * 0.22, t * 0.17);
                    float2 w2 = P.xz * 9.5 + float2(-t * 0.19, t * 0.27);
                    float2 w3 = P.xz * 17.0 + float2(t * 0.3, -t * 0.22);
                    float3 n1 = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, w1));
                    float3 n2 = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, w2));
                    float3 n3 = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, w3));
                    float2 nxy = (n1.xy + n2.xy * 0.8 + n3.xy * 0.5) * (0.55 + 0.9 * _Ripple);
                    normalWS = normalize(float3(nxy.x - _TiltX, 1.0, nxy.y - _TiltZ));

                    // Kenarda kopuk ve kabarciklar (agza yakin)
                    float4x4 om = GetObjectToWorldMatrix();
                    float3 center = float3(om._m03, om._m13, om._m23);
                    float sc = length(float3(om._m01, om._m11, om._m21));
                    float rn = length(P.xz - center.xz) / max(_RimRadius * sc, 1e-4);
                    float noise = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, P.xz * 7.0 + float2(t * 0.04, -t * 0.03)).r;
                    float noise2 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, P.xz * 23.0 + float2(-t * 0.06, t * 0.05)).g;
                    float foam = smoothstep(0.78, 1.0, rn) * smoothstep(0.42, 0.78, noise);
                    float bubbles = smoothstep(0.9, 0.96, noise2) * smoothstep(0.35, 0.8, rn);

                    float depthTint = saturate(0.5 + 0.5 * n1.x);
                    albedo = lerp(_Color.rgb * 0.55, _SurfaceColor.rgb, depthTint);
                    albedo = lerp(albedo, float3(0.93, 0.97, 1.0), saturate(foam + bubbles * 0.8));
                    smoothness = lerp(_Smoothness, 0.55, saturate(foam + bubbles));
                }

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
                s.metallic = 0;
                s.specular = half3(0, 0, 0);
                s.smoothness = smoothness;
                s.occlusion = 1;
                s.emission = 0;
                s.alpha = 1;
                s.normalTS = half3(0, 0, 1);

                half4 col = UniversalFragmentPBR(inputData, s);
                col.rgb = MixFog(col.rgb, inputData.fogCoord);
                col.a = 1;
                return col;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
