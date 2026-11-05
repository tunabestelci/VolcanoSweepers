// Cel-shade: tam ekran gecis. Isigi birkac kesin banda boler (renk tonu korunur) ve derinlik
// siniri / kivrimlarina murekkep cizgisi ceker. Tum sahne (arazi, lav, esyalar) ayni stile girer.
Shader "VolcanoSweepers/CelPost"
{
    Properties
    {
        _Bands("Isik bandi sayisi", Float) = 4
        _Shadow("En koyu band parlakligi", Range(0, 0.3)) = 0.07
        _Saturation("Doygunluk", Range(0.5, 2)) = 1.25
        _Soften("Doku yumusatma (px)", Range(0, 3)) = 1.6
        _OutlineWidth("Cizgi kalinligi (1080p pikseli)", Range(0.5, 4)) = 1.6
        _OutlineThreshold("Cizgi esigi", Range(0.005, 0.3)) = 0.05
        _OutlineColor("Cizgi rengi", Color) = (0.035, 0.015, 0.012, 1)
        _OutlineFade("Cizgi solma mesafesi", Float) = 260
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "CelPost"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float _Bands, _Shadow, _Saturation, _Soften, _OutlineWidth, _OutlineThreshold, _OutlineFade;
            float4 _OutlineColor;

            float3 Src(float2 uv) { return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb; }

            // 1/z: perspektifte duzlemlerde ekran uzayinda dogrusal -> duz yuzeyde sahte cizgi cikmaz
            float InvDepth(float2 uv)
            {
                float z = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                return rcp(max(z, 0.05));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float2 px = _BlitTexture_TexelSize.xy;

                // --- Isik bantlari ---
                float2 so = px * _Soften;
                float3 c = Src(uv) * 0.36
                         + (Src(uv + float2(so.x, 0)) + Src(uv - float2(so.x, 0)) + Src(uv + float2(0, so.y)) + Src(uv - float2(0, so.y))) * 0.16;
                float L = dot(c, float3(0.299, 0.587, 0.114));
                const float g = 0.55;
                float x = pow(saturate(L), g) * _Bands;
                float fw = clamp(fwidth(x) * 0.8, 0.02, 0.45);
                float q = (floor(x) + smoothstep(0.5 - fw, 0.5 + fw, frac(x))) / _Bands;
                float Lq = lerp(_Shadow, 1.0, pow(q, 1.0 / g));
                float3 col = c * clamp((Lq + 0.01) / (L + 0.01), 0.0, 3.0);
                float gray = dot(col, float3(0.299, 0.587, 0.114));
                col = max(0.0, lerp(gray.xxx, col, _Saturation));

                // --- Murekkep cizgisi ---
                float2 o = px * max(1.0, _ScreenParams.y / 1080.0 * _OutlineWidth);
                float i0 = InvDepth(uv);
                float iR = InvDepth(uv + float2(o.x, 0));
                float iL = InvDepth(uv - float2(o.x, 0));
                float iU = InvDepth(uv + float2(0, o.y));
                float iD = InvDepth(uv - float2(0, o.y));
                float lap = abs(iR + iL - 2.0 * i0) + abs(iU + iD - 2.0 * i0);
                float edge = smoothstep(_OutlineThreshold, _OutlineThreshold * 2.2, lap / max(i0, 1e-4));
                float dist = rcp(max(i0, 1e-4));
                edge *= 1.0 - smoothstep(_OutlineFade * 0.6, _OutlineFade, dist);
                col = lerp(col, _OutlineColor.rgb, edge);

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
