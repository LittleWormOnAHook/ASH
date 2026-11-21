// t484: alpha-clipped foliage highlight (bushes, tree crosses, grass, plant fiber).
// Samples the source material's base map and clips at the same cutoff, then adds a soft gold rim/fresnel + slight tint.
// Used instead of the inverted-hull outline, which filled leaf and grass cards as solid gold quads.
Shader "ASH/Harvest Foliage Glow"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        _OutlineColor ("Color", Color) = (1, 0.85, 0.45, 1)
        _TintStrength ("Tint Strength", Range(0, 2)) = 0.7
        _RimPower ("Rim Power", Range(0.25, 12)) = 2.5
        _Pulse ("Pulse", Range(0, 1)) = 0.12
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        Pass
        {
            Name "FoliageGlow"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha One
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _OutlineColor;
                float _Cutoff;
                float _TintStrength;
                float _RimPower;
                float _Pulse;
            CBUFFER_END
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 viewWS : TEXCOORD2; };
            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(posWS);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.viewWS = GetWorldSpaceViewDir(posWS);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a;
                clip(a - _Cutoff);
                float3 n = normalize(i.normalWS);
                float3 v = normalize(i.viewWS);
                half ndv = saturate(abs(dot(n, v)));
                half rim = pow(saturate(1.0 - ndv), _RimPower);
                half pulse = 1.0 + _Pulse * sin(_Time.y * 2.8);
                half intensity = (_TintStrength * 0.4 + rim * 1.1) * pulse;
                half3 col = _OutlineColor.rgb * intensity;
                return half4(col, saturate(intensity) * _OutlineColor.a * a);
            }
            ENDHLSL
        }
    }
}
