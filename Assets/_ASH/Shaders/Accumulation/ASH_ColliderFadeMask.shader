// ASH collider fade mask for the deformable accumulation surface (ASHAccumulationSurface).
// Pass 0 draws every nearby collider's real shape (box / sphere / capsule / mesh) top-down into a texel-aligned RG mask
// (vertical projection, geometry above ground + footprint height ignored): R = clearing collider (small / non-banking),
// G = banking collider. Pass 1 + 2 are a separable exact Euclidean distance search (rows, then columns) per channel:
// R becomes the fade (1 inside the footprint and within Padding, falling to 0 over Softness), G the distance in metres to
// the nearest banking footprint edge (1e4 = none in reach), which the surface shader turns into a soft bank.
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
        float  _ASH_CFM_Taps;      // search half-width in steps (fine + coarse)
        float  _ASH_CFM_Fine;      // first N steps are 1 texel (exact clearing edge), the rest Params.w texels (bank reach)
        int CFM_Off(int k) { int a = abs(k), nf = (int)_ASH_CFM_Fine; int o = a <= nf ? a : nf + (a - nf) * (int)_ASH_CFM_Params.w; return k < 0 ? -o : o; }
        float  _ASH_CFM_Res;
        float  _ASH_CFM_Bank;      // pass 0: 1 = this collider banks (G), 0 = it clears (R)
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
            BlendOp Max
            Blend One One
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
            half4 Frag(V i) : SV_Target
            {
                clip(CFM_Ground(i.positionWS.xz) + _ASH_CFM_Rect.w - i.positionWS.y);
                return _ASH_CFM_Bank > 0.5 ? half4(0, 1, 0, 0) : half4(1, 0, 0, 0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DistanceRows"
            HLSLPROGRAM
            #pragma vertex FullscreenVert
            #pragma fragment Frag
            float2 Frag(FsOut i) : SV_Target
            {
                int2 p = int2(i.positionCS.xy); int res = (int)_ASH_CFM_Res; int n = min((int)_ASH_CFM_Taps, 64);
                float2 best = float2(1e4, 1e4);
                for (int k = -n; k <= n; k++)
                {
                    int o = CFM_Off(k); int x = p.x + o; if (x < 0 || x >= res) continue;
                    float2 m = LOAD_TEXTURE2D(_ASH_CFM_Src, int2(x, p.y)).rg; float d = abs(o) * _ASH_CFM_Params.x;
                    if (m.r > 0.5) best.x = min(best.x, d);
                    if (m.g > 0.5) best.y = min(best.y, d);
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
            float2 Frag(FsOut i) : SV_Target
            {
                int2 p = int2(i.positionCS.xy); int res = (int)_ASH_CFM_Res; int n = min((int)_ASH_CFM_Taps, 64);
                float2 best2 = float2(1e8, 1e8);
                for (int k = -n; k <= n; k++)
                {
                    int o = CFM_Off(k); int y = p.y + o; if (y < 0 || y >= res) continue;
                    float2 dx = LOAD_TEXTURE2D(_ASH_CFM_Src, int2(p.x, y)).rg; float dy = abs(o) * _ASH_CFM_Params.x;
                    best2 = min(best2, dx * dx + dy * dy);
                }
                // distance to the footprint EDGE (texel centres are half a texel inside the edge)
                float2 d;
                d.x = best2.x < 1e7 ? max(sqrt(best2.x) - 0.5 * _ASH_CFM_Params.x, 0.0) : 1e4; if (best2.x == 0) d.x = 0;
                d.y = best2.y < 1e7 ? max(sqrt(best2.y) - 0.5 * _ASH_CFM_Params.x, 0.0) : 1e4; if (best2.y == 0) d.y = 0;
                // R: padding + soft falloff (clearing colliders); G: raw distance for the bank profile
                return float2(1.0 - saturate((d.x - _ASH_CFM_Params.y) / max(_ASH_CFM_Params.z, 1e-4)), d.y);
            }
            ENDHLSL
        }
    }
}
