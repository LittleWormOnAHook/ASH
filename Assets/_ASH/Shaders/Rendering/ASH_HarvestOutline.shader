// t478: cheap inverted-hull outline for the targeted harvestable resource (ASHHarvestOutline draws it with Graphics.RenderMesh,
// only while the resource is the interactor's target, i.e. inside the harvest distance). Width in screen pixels.
Shader "ASH/Harvest Outline"
{
    Properties
    {
        _OutlineColor ("Color", Color) = (1, 0.85, 0.45, 1)
        _OutlineWidth ("Width (pixels)", Float) = 2.5
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry+10" }
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            ZTest LEqual
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes i)
            {
                Varyings o;
                float4 cs = TransformObjectToHClip(i.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(i.normalOS);
                float2 nCS = mul((float3x3)UNITY_MATRIX_VP, nWS).xy;
                float len = length(nCS);
                float2 dir = len > 1e-5 ? nCS / len : float2(0, 0);
                cs.xy += dir * (_OutlineWidth * 2.0 / _ScreenParams.xy) * cs.w;
                o.positionCS = cs;
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return _OutlineColor; }
            ENDHLSL
        }
    }
}