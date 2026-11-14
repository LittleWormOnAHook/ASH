// ASH deformable accumulation SURFACE: a player-following grid displaced to terrain height + ash/snow depth.
// Uses the shared core (ASHAccumulation.hlsl) for depth, layering and colour; it is not a second accumulation model.
// Driven by ASHAccumulationSurface + ASHAccumulationController globals. See Docs/AccumulationSystem.md.
Shader "Hidden/ASH/Accumulation Surface"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "ASHAccumulation.hlsl"

        TEXTURE2D(_ASH_Heightmap); SAMPLER(sampler_ASH_Heightmap);
        float4 _ASH_HeightParams;   // x heightmap res, y height scale (size.y / kMaxHeight), z terrain y, w unused
        float4 _ASH_SurfaceRect;    // x centre x, y centre z, z half size, w edge fade width
        float4 _ASH_Blockers[16];   // centre xz, half extents xz (oriented boxes: structures, pickups)
        float4 _ASH_BlockerRot[16]; // x cos, y sin
        float  _ASH_BlockerCount;
        float  _ASH_GridStep;
        float4 _ASH_SurfQuality;    // x normal step multiplier (vertex normals), y per-pixel footprint normal detail 0..1, z deform texel size m

        float ASH_TerrainHeight(float2 xz)
        {
            float2 uv01 = saturate((xz - _ASH_TerrainRect.xy) * _ASH_TerrainRect.zw);
            float res = _ASH_HeightParams.x;
            float2 uv = (uv01 * (res - 1.0) + 0.5) / res;
            float h = SAMPLE_TEXTURE2D_LOD(_ASH_Heightmap, sampler_ASH_Heightmap, uv, 0).r;
            return h * _ASH_HeightParams.y + _ASH_HeightParams.z;
        }

        float ASH_BlockAt(float2 xz)
        {
            float b = 0;
            int n = (int)_ASH_BlockerCount;
            for (int i = 0; i < 16; i++)
            {
                if (i >= n) break;
                float2 d = xz - _ASH_Blockers[i].xy; float c = _ASH_BlockerRot[i].x, s = _ASH_BlockerRot[i].y;
                float2 l = abs(float2(d.x * c + d.y * s, -d.x * s + d.y * c)) - _ASH_Blockers[i].zw;
                float outside = max(l.x, l.y);
                b = max(b, 1.0 - saturate(outside / 0.35));
            }
            return b;
        }

        // world height of the ash/snow surface (sinks just under the terrain where there is no material)
        float ASH_SurfaceHeight(float2 xz, out float depth, out half4 drift, out half deform)
        {
            float3 p = float3(xz.x, 0, xz.y);
            drift = ASH_DriftAt(p); deform = ASH_DeformAt(p);
            float2 dd = abs(xz - _ASH_SurfaceRect.xy);
            float edge = saturate((_ASH_SurfaceRect.z - max(dd.x, dd.y)) / max(_ASH_SurfaceRect.w, 0.01));
            depth = ASH_DepthMetres(drift, deform) * edge * (1.0 - ASH_BlockAt(xz));
            return ASH_TerrainHeight(xz) + depth - 0.08 * (1.0 - saturate(depth / 0.04));
        }

        struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS   : TEXCOORD1;
            float4 extra      : TEXCOORD2; // x depth, y deform, z fog
            half4  drift      : TEXCOORD3;
        };

        Varyings SurfaceVert(Attributes v)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(v);
            float3 w = TransformObjectToWorld(v.positionOS.xyz);
            float depth; half4 drift; half deform;
            w.y = ASH_SurfaceHeight(w.xz, depth, drift, deform);
            float e = max(_ASH_GridStep * max(_ASH_SurfQuality.x, 1.0), 0.05);
            float d0; half4 dr0; half df0;
            float hx1 = ASH_SurfaceHeight(w.xz + float2(e, 0), d0, dr0, df0);
            float hx0 = ASH_SurfaceHeight(w.xz - float2(e, 0), d0, dr0, df0);
            float hz1 = ASH_SurfaceHeight(w.xz + float2(0, e), d0, dr0, df0);
            float hz0 = ASH_SurfaceHeight(w.xz - float2(0, e), d0, dr0, df0);
            o.normalWS = normalize(float3(hx0 - hx1, 2.0 * e, hz0 - hz1));
            o.positionWS = w;
            o.positionCS = TransformWorldToHClip(w);
            o.extra = float4(depth, deform, ComputeFogFactor(o.positionCS.z), 0);
            o.drift = drift;
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SurfaceVert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            half4 Frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                if (_ASH_SurfQuality.y > 0.001 && i.extra.x > 0.002)
                {   // per-pixel footprint detail: slope of the deformed layer from the deform texture gradient
                    float tx = max(_ASH_SurfQuality.z, 0.02);
                    float3 pw = i.positionWS;
                    float gx = ASH_DeformAt(pw + float3(tx, 0, 0)) - ASH_DeformAt(pw - float3(tx, 0, 0));
                    float gz = ASH_DeformAt(pw + float3(0, 0, tx)) - ASH_DeformAt(pw - float3(0, 0, tx));
                    float base = ASH_DepthMetres(i.drift, 0) / (2.0 * tx);
                    n = normalize(n + float3(gx * base, 0, gz * base) * _ASH_SurfQuality.y);
                }
                half3 albedo = _ASH_SnowColor.rgb; half3 nn = n; half smooth = 0.3, metal = 0, occ = 1;
                ASH_ApplyAccumulation(i.positionWS, n, i.drift, 0, albedo, nn, smooth, metal, occ);
                // trench walls / footprint floors read slightly darker (compressed, shadowed)
                occ *= lerp(1.0, 0.75, saturate(i.extra.y * 1.5));

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalWS = nn;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord = i.extra.z;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.bakedGI = SampleSH(nn);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo; s.metallic = metal; s.smoothness = smooth; s.occlusion = occ; s.alpha = 1;
                s.normalTS = half3(0, 0, 1); s.specular = 0; s.emission = 0;
                half4 c = UniversalFragmentPBR(inputData, s);
                c.rgb = MixFog(c.rgb, inputData.fogCoord);
                return half4(c.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SurfaceVert
            #pragma fragment FragDepth
            half FragDepth(Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SurfaceVert
            #pragma fragment FragDN
            half4 FragDN(Varyings i) : SV_Target { return half4(NormalizeNormalPerPixel(normalize(i.normalWS)), 0.0); }
            ENDHLSL
        }
    }
}
