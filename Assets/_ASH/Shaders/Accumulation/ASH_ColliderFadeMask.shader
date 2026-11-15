// ASH collider fade mask for the deformable accumulation surface (ASHAccumulationSurface).
// Pass 0 draws every nearby collider's real shape (box / sphere / capsule / mesh) top-down into a texel-aligned R8 mask
// (vertical projection, geometry above ground + footprint height ignored). Pass 1 + 2 are a separable exact Euclidean
// distance search (rows, then columns) that turns the mask into a fade: 1 inside the footprint and within Padding, falling
// to 0 over Softness. No padded radius or bounds box is used anywhere.
Shader "Hidden/ASH/Collider Fade Mask"
{
    Properties { }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_ASH_Heightmap);
        float4 _ASH_TerrainRect;   // x minX, y minZ, z 1/sizeX, w 1/sizeZ
        float4 _ASH_HeightParams;  // x heightmap res, y height scale, z terrain y
        float4 _ASH_CFM_Rect;      // x minX, y minZ, z 1/size (square), w footprint height above ground (m)
        float4 _ASH_CFM_Params;    // x texel m, y padding m, z softness m, w search step (texels)
        float  _ASH_CFM_Taps;      // search half-width in steps
        float  _ASH_CFM_Res;
        TEXTURE2D(_ASH_CFM_Src);

        float CFM_Ground(float2 xz)
        {
            float2 uv01 = saturate((xz - _ASH_TerrainRect.xy) * _ASH_TerrainRect.zw);
            float res = _ASH_HeightParams.x;
            float2 uv = (uv01 * (res - 1.0) + 0.5) / res;
            return SAMPLE_TEXTURE2D_LOD(_ASH_Heightmap, sampler_LinearClamp, uv, 0).r * _ASH_HeightParams.y + _ASH_HeightParams.z;
        }

        struct FsOut { float4 positionCS : SV_POSITION; };
        FsOut FullscreenVert(uint id : SV_VertexID)
        {
            FsOut o; float2 p = float2((id << 1) & 2, id & 2);
            o.positionCS = float4(p * 2.0 - 1.0, 0.5, 1.0);
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "Footprint"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            V Vert(A v)
            {
                V o; float3 w = mul(unity_ObjectToWorld, float4(v.positionOS.xyz, 1.0)).xyz;
                float2 uv = (w.xz - _ASH_CFM_Rect.xy) * _ASH_CFM_Rect.z;
                float2 ndc = uv * 2.0 - 1.0;
                #if UNITY_UV_STARTS_AT_TOP
                ndc.y = -ndc.y;   // row 0 = uv v 0, so sampling with uv = (xz - min) / size reads the texel drawn here
                #endif
                o.positionCS = float4(ndc, 0.5, 1.0); o.positionWS = w;
                return o;
            }
            half Frag(V i) : SV_Target
            {
                clip(CFM_Ground(i.positionWS.xz) + _ASH_CFM_Rect.w - i.positionWS.y);
                return 1;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DistanceRows"
            HLSLPROGRAM
            #pragma vertex FullscreenVert
            #pragma fragment Frag
            float Frag(FsOut i) : SV_Target
            {
                int2 p = int2(i.positionCS.xy); int res = (int)_ASH_CFM_Res; int st = (int)_ASH_CFM_Params.w; int n = (int)_ASH_CFM_Taps;
                float best = 1e4;
                for (int k = -64; k <= 64; k++)
                {
                    if (abs(k) > n) continue;
                    int x = p.x + k * st; if (x < 0 || x >= res) continue;
                    if (LOAD_TEXTURE2D(_ASH_CFM_Src, int2(x, p.y)).r > 0.5) best = min(best, abs(k * st) * _ASH_CFM_Params.x);
                }
                return best;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DistanceColumns"
            HLSLPROGRAM
            #pragma vertex FullscreenVert
            #pragma fragment Frag
            float Frag(FsOut i) : SV_Target
            {
                int2 p = int2(i.positionCS.xy); int res = (int)_ASH_CFM_Res; int st = (int)_ASH_CFM_Params.w; int n = (int)_ASH_CFM_Taps;
                float best2 = 1e8;
                for (int k = -64; k <= 64; k++)
                {
                    if (abs(k) > n) continue;
                    int y = p.y + k * st; if (y < 0 || y >= res) continue;
                    float dx = LOAD_TEXTURE2D(_ASH_CFM_Src, int2(p.x, y)).r; float dy = abs(k * st) * _ASH_CFM_Params.x;
                    best2 = min(best2, dx * dx + dy * dy);
                }
                // distance to the footprint EDGE (texel centres are half a texel inside the edge), then padding + soft falloff
                float d = best2 < 1e7 ? max(sqrt(best2) - 0.5 * _ASH_CFM_Params.x, 0.0) : 1e4;
                if (best2 == 0) d = 0;
                return 1.0 - saturate((d - _ASH_CFM_Params.y) / max(_ASH_CFM_Params.z, 1e-4));
            }
            ENDHLSL
        }
    }
}
