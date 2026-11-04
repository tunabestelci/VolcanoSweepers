Shader "VolcanoSweepers/LiquidVessel"
{
    // Kap icindeki sivi: kabin icine yerlestirilen silindiri dunyadaki yatay bir duzlemde (_FillY) keser.
    // Kap egilse de sivi yuzeyi dunyaya gore duz kalir; kesilen yerde ic yuzler "su yuzeyi" gibi boyanir.
    Properties
    {
        _BaseMap("Water Texture", 2D) = "white" {}
        _Color("Color", Color) = (0.2, 0.5, 0.8, 1)
        _SurfaceColor("Surface Color", Color) = (0.45, 0.72, 0.95, 1)
        _Smoothness("Smoothness", Range(0, 1)) = 0.9
        _FillY("Fill Plane Y (world)", Float) = 0
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

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _Color;
                float4 _SurfaceColor;
                float _Smoothness;
                float _FillY;
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
                clip(_FillY - IN.positionWS.y);
                bool front = IS_FRONT_VFACE(isFront, true, false);

                float3 normalWS = front ? normalize(IN.normalWS) : float3(0, 1, 0);
                float2 suv = IN.positionWS.xz * 0.9 + float2(_Time.y * 0.03, _Time.y * 0.02);
                float3 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, front ? IN.uv : suv).rgb;
                float3 albedo = front ? tex * _Color.rgb : tex * _SurfaceColor.rgb;

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
                s.smoothness = _Smoothness;
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
