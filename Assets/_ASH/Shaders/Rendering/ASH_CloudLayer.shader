// ASH cloud layer: cheap procedural 2D cloud deck on a camera-following dome (one transparent draw, no textures).
// Coverage / darkness / wind come from ASHWeatherOutputs (weather director); lit by the main light (our sun / clock).
Shader "ASH/CloudLayer"
{
    Properties
    {
        _Coverage ("Coverage", Range(0, 1)) = 0.45
        _Sharpness ("Edge Sharpness", Range(0.5, 12)) = 3
        _Opacity ("Opacity", Range(0, 1)) = 0.95
        _Darkness ("Darkness (storm)", Range(0, 1)) = 0
        _Scale ("Scale (1/km)", Range(0.05, 10)) = 1.2
        _Height ("Deck Height (km, projection)", Range(0.2, 10)) = 1.5
        _HorizonFade ("Horizon Fade", Range(0.001, 0.5)) = 0.08
        _WindOffset ("Wind Offset (km)", Vector) = (0, 0, 0, 0)
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _ShadowColor ("Shadow Color", Color) = (0.42, 0.45, 0.52, 1)
        _SunScatter ("Sun Scatter (silver lining)", Range(0, 4)) = 0.8
        _Detail ("Detail Octaves", Range(1, 5)) = 4
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-400" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Clouds"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Front

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Coverage, _Sharpness, _Opacity, _Darkness, _Scale, _Height, _HorizonFade, _SunScatter, _Detail;
                float4 _WindOffset, _Tint, _ShadowColor;
            CBUFFER_END

            struct A { float4 pos : POSITION; };
            struct V { float4 cs : SV_POSITION; float3 ws : TEXCOORD0; };

            V vert(A i) { V o; o.ws = TransformObjectToWorld(i.pos.xyz); o.cs = TransformWorldToHClip(o.ws); return o; }

            float h2(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(h2(i), h2(i + float2(1, 0)), u.x), lerp(h2(i + float2(0, 1)), h2(i + float2(1, 1)), u.x), u.y);
            }
            float fbm(float2 p)
            {
                float a = 0.5, s = 0; int n = (int)_Detail;
                [unroll(5)] for (int k = 0; k < 5; k++) { if (k >= n) break; s += a * vnoise(p); p = p * 2.03 + float2(17.1, 9.7); a *= 0.5; }
                return s / (1 - pow(0.5, n));
            }

            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.ws - _WorldSpaceCameraPos);
                if (d.y <= 0) return 0;
                float2 uv = d.xz / (d.y + 0.05) * _Height * _Scale + _WindOffset.xy * _Scale;
                float n = fbm(uv);
                float cov = saturate(_Coverage);
                float dens = saturate((n - (1 - cov)) * _Sharpness);
                dens = lerp(dens, saturate(dens + 0.6), smoothstep(0.85, 1.0, cov)); // overcast closes the gaps
                float thick = saturate(fbm(uv * 1.7 + 3.1) * dens * 1.4);
                Light L = GetMainLight();
                float mu = saturate(dot(d, L.direction));
                float3 sun = L.color;
                float3 amb = max(SampleSH(float3(0, 1, 0)), 0.02);
                float3 lit = lerp(sun * 0.9 + amb, _ShadowColor.rgb * (amb + sun * 0.25), thick);
                lit += sun * pow(mu, 10) * _SunScatter * (1 - thick) * dens;
                lit *= _Tint.rgb * lerp(1, 0.28, _Darkness);
                float fade = saturate(d.y / _HorizonFade);
                return half4(lit, dens * _Opacity * fade);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
