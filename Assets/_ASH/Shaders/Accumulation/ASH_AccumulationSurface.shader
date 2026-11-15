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

        float  _ASH_GridStep;
        float4 _ASH_Thin;           // x thin fade start depth m, y thin fade end depth m, z lift above terrain m, w enabled
        // (_ASH_Heightmap, _ASH_HeightParams, _ASH_SurfaceRect, _ASH_ColliderMask(Rect), _ASH_Bank* live in ASHAccumulation.hlsl)

        // Height of the real-depth surface over the terrain. Where the layer is thinner than the fade start it sinks under the
        // terrain (those pixels are clipped anyway); elsewhere it stays at least Lift above it, so the 0.2 m grid never
        // intersects the differently-triangulated terrain at near-zero depth (that intersection drew sawtooth bands).
        float ASH_LiftDepth(float depth)
        {
            if (_ASH_Thin.w < 0.5) return depth - 0.08 * (1.0 - saturate(depth / 0.04)); // legacy
            return depth < _ASH_Thin.x * 0.5 ? -0.08 : max(depth, _ASH_Thin.z);
        }
        float4 _ASH_SurfNormal;     // x normal smoothing (heightmap texels), y deform normal strength, z deform texel size m
        float4 _ASH_SurfQuality;    // x normal step multiplier (vertex normals), y per-pixel footprint normal detail 0..1, z deform texel size m

        // per-pixel collider fade at xz (R: small / non-banking colliders; bilinear on the texel-aligned mask; 0 outside it)
        float ASH_ColliderFade(float2 xz) { return ASH_ColliderMaskAt(xz).r; }

        // geometry fade: the grid only sinks where its whole cell is cleared (min over the cell), so a 0.2 m grid never
        // drags the ash down outside a collider; the exact edge is cut per pixel by ASH_ColliderClip
        float ASH_BlockAt(float2 xz)
        {
            if (_ASH_ColliderMaskRect.w < 0.5) return 0;
            float g = _ASH_GridStep, b = ASH_ColliderFade(xz);
            [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                    if (x != 0 || y != 0) b = min(b, ASH_ColliderFade(xz + float2(x, y) * g));
            return b;
        }

        // per-pixel edge: cut the surface where the collider fade wins (dithered over the soft band), and inside a banking
        // collider's real footprint (the bank runs up to it and continues under the object, hidden by it)
        void ASH_ColliderClip(float2 xz, float4 positionCS)
        {
            float2 m = ASH_ColliderMaskAt(xz);
            if (_ASH_Bank.w > 0.5) clip(m.y - 0.25 * _ASH_Bank2.w);
            float b = m.r;
            if (b <= 0.0) return;
            float n = frac(52.9829189 * frac(dot(floor(positionCS.xy), float2(0.06711056, 0.00583715))));
            clip(n * 0.998 + 0.001 - b);
        }

        // layer depth at xz before the lift: drift/deform depth (cleared near small colliders) + banks against big ones
        // cheap version for the normal's slope differences (4 calls per pixel): one bilinear drift tap and one mask tap instead of
        // bicubic drift + the 3x3 cell-min block (those only matter for the visible silhouette, not for the lighting slope)
        float ASH_LayerDepthFast(float2 xz)
        {
            float3 p = float3(xz.x, 0, xz.y);
            half4 drift = ASH_DriftAtFast(p); half deform = ASH_DeformAt(p);
            float2 m = ASH_ColliderMaskAt(xz);
            float bank = ASH_BankAt(xz, drift, deform, m.y);
            return ASH_LiftDepth((ASH_DepthMetres(drift, deform) * (1.0 - m.x) + bank) * ASH_SurfaceEdge(xz));
        }

        float ASH_LayerDepth(float2 xz, out half4 drift, out half deform)
        {
            float3 p = float3(xz.x, 0, xz.y);
            drift = ASH_DriftAt(p); deform = ASH_DeformAt(p);
            float bank = ASH_BankAt(xz, drift, deform, ASH_ColliderMaskAt(xz).g);
            return (ASH_DepthMetres(drift, deform) * (1.0 - ASH_BlockAt(xz)) + bank) * ASH_SurfaceEdge(xz);
        }

        // world height of the ash/snow surface (sinks just under the terrain where there is no material)
        float ASH_SurfaceHeight(float2 xz, out float depth, out half4 drift, out half deform)
        {
            depth = ASH_LayerDepth(xz, drift, deform);
            return ASH_TerrainHeight(xz) + ASH_LiftDepth(depth);
        }

        // ash/snow depth above the terrain at xz (same terms as ASH_SurfaceHeight, without the terrain height)
        float ASH_SurfaceDepth(float2 xz)
        {
            half4 drift; half deform;
            return ASH_LiftDepth(ASH_LayerDepth(xz, drift, deform));
        }

        // Per-pixel thin-layer fade: where the layer is thinner than Thin Fade End the real-depth surface dissolves with a
        // world-space noise threshold (patchy, stable, no screen dither) and the terrain's own soft coverage shows instead.
        // Banks count as depth, so a bank in a thin morning dusting stays solid.
        void ASH_ThinClip(float3 positionWS)
        {
            if (_ASH_Thin.w < 0.5) return;
            float2 xz = positionWS.xz;
            half4 drift = ASH_DriftAt(positionWS); half deform = ASH_DeformAt(positionWS);
            float depth = (ASH_DepthMetres(drift, deform) + ASH_BankAt(xz, drift, deform, ASH_ColliderMaskAt(xz).g)) * ASH_SurfaceEdge(xz);
            float f = smoothstep(_ASH_Thin.x, max(_ASH_Thin.y, _ASH_Thin.x + 1e-4), depth);
            float n = ASH_Fbm(xz * max(_ASH_Edge.y, 1e-4) * 4.0 + 11.3) * 0.96 + 0.02;
            clip(f - n);
        }

        // Smooth surface normal: 3x3 Sobel on the terrain heightmap at (smoothing x) one heightmap texel - texel-correct, so the
        // bilinear facets of the heightmap no longer show as a regular zigzag in sunlight - plus the ash depth slope (drift +
        // footprints) by central differences at the deform texel size, scaled by Deform Normal Strength. Flat where depth is flat.
        float3 ASH_SurfaceNormal(float2 xz)
        {
            float sm = max(_ASH_SurfNormal.x, 0.5);
            float2 texel = 1.0 / (_ASH_TerrainRect.zw * max(_ASH_HeightParams.x - 1.0, 1.0)); // metres per heightmap texel (x, z)
            float2 t = texel * sm;
            float h00 = ASH_TerrainHeight(xz + float2(-t.x, -t.y)), h10 = ASH_TerrainHeight(xz + float2(0, -t.y)), h20 = ASH_TerrainHeight(xz + float2(t.x, -t.y));
            float h01 = ASH_TerrainHeight(xz + float2(-t.x, 0)),                                                    h21 = ASH_TerrainHeight(xz + float2(t.x, 0));
            float h02 = ASH_TerrainHeight(xz + float2(-t.x, t.y)),  h12 = ASH_TerrainHeight(xz + float2(0, t.y)),  h22 = ASH_TerrainHeight(xz + float2(t.x, t.y));
            float gx = ((h20 + 2.0 * h21 + h22) - (h00 + 2.0 * h01 + h02)) / (8.0 * t.x);
            float gz = ((h02 + 2.0 * h12 + h22) - (h00 + 2.0 * h10 + h20)) / (8.0 * t.y);
            float k = _ASH_SurfNormal.y;
            if (k > 0.001)
            {
                float ds = max(_ASH_SurfNormal.z * sm, 0.05);
                gx += k * (ASH_LayerDepthFast(xz + float2(ds, 0)) - ASH_LayerDepthFast(xz - float2(ds, 0))) / (2.0 * ds);
                gz += k * (ASH_LayerDepthFast(xz + float2(0, ds)) - ASH_LayerDepthFast(xz - float2(0, ds))) / (2.0 * ds);
            }
            return normalize(float3(-gx, 1.0, -gz));
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
            o.normalWS = ASH_SurfaceNormal(w.xz); // same smooth normal as the pixels (DepthNormals / SSAO stay consistent)
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
                ASH_ColliderClip(i.positionWS.xz, i.positionCS);
                ASH_ThinClip(i.positionWS);
                float3 n = ASH_SurfaceNormal(i.positionWS.xz); // per pixel, not interpolated from the coarse grid
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
            half FragDepth(Varyings i) : SV_Target { ASH_ColliderClip(i.positionWS.xz, i.positionCS); ASH_ThinClip(i.positionWS); return i.positionCS.z; }
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
            half4 FragDN(Varyings i) : SV_Target { ASH_ColliderClip(i.positionWS.xz, i.positionCS); ASH_ThinClip(i.positionWS); return half4(NormalizeNormalPerPixel(normalize(i.normalWS)), 0.0); }
            ENDHLSL
        }
    }
}
