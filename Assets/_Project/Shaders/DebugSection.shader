Shader "VolcanoSweepers/DebugSection"
{
    // Yalnizca test goruntuleri icin: nesneyi z duzleminden keser, ic yuzleri koyu boyar.
    Properties
    {
        _Color("Color", Color) = (0.8, 0.75, 0.7, 1)
        _SectionZ("Section Z", Float) = 0
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
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _SectionZ;
            CBUFFER_END
            struct A { float4 p : POSITION; float3 n : NORMAL; };
            struct V { float4 cs : SV_POSITION; float3 ws : TEXCOORD0; float3 nws : TEXCOORD1; };
            V vert(A i) { V o; VertexPositionInputs vp = GetVertexPositionInputs(i.p.xyz); o.cs = vp.positionCS; o.ws = vp.positionWS; o.nws = TransformObjectToWorldNormal(i.n); return o; }
            half4 frag(V i, FRONT_FACE_TYPE f : FRONT_FACE_SEMANTIC) : SV_Target
            {
                clip(_SectionZ - i.ws.z);
                bool front = IS_FRONT_VFACE(f, true, false);
                float l = saturate(dot(normalize(i.nws), normalize(float3(0.4, 0.8, -0.5)))) * 0.5 + 0.5;
                return half4(front ? _Color.rgb * l : float3(0.25, 0.2, 0.18) * l, 1);
            }
            ENDHLSL
        }
    }
}
