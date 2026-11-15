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
        TEXTURE2D(_ASH_ColliderMask); // collider fade (1 = cleared): real collider footprints + padding + softness, texel-aligned
        float4 _ASH_ColliderMaskRect; // x minX, y minZ, z 1/size (square), w enabled
        float  _ASH_GridStep;
        float4 _ASH_Thin;           // x thin fade start depth m, y thin fade end depth m, z lift above terrain m, w enabled

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

        float ASH_TerrainHeight(float2 xz)
        {
            float2 uv01 = saturate((xz - _ASH_TerrainRect.xy) * _ASH_TerrainRect.zw);
            float res = _ASH_HeightParams.x;
            float2 uv = (uv01 * (res - 1.0) + 0.5) / res;
            float h = SAMPLE_TEXTURE2D_LOD(_ASH_Heightmap, sampler_LinearClamp, uv, 0).r; // always bilinear (the terrain heightmap RT may be point-filtered)
            return h * _ASH_HeightParams.y + _ASH_HeightParams.z;
        }

        // per-pixel collider fade at xz (bilinear on the texel-aligned mask; 0 outside it)
        float ASH_ColliderFade(float2 xz)
        {
            if (_ASH_ColliderMaskRect.w < 0.5) return 0;
            float2 uv = (xz - _ASH_ColliderMaskRect.xy) * _ASH_ColliderMaskRect.z;
            if (any(uv < 0.0) || any(uv > 1.0)) return 0;
            return SAMPLE_TEXTURE2D_LOD(_ASH_ColliderMask, sampler_LinearClamp, uv, 0).r;
        }

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

        // per-pixel edge: cut the surface where the collider fade wins (dithered over the soft band); terrain shows beneath
        void ASH_ColliderClip(float2 xz, float4 positionCS)
        {
            float b = ASH_ColliderFade(xz);
            if (b <= 0.0) return;
            float n = frac(52.9829189 * frac(dot(floor(positionCS.xy), float2(0.06711056, 0.00583715))));
            clip(n * 0.998 + 0.001 - b);
        }

        // world height of the ash/snow surface (sinks just under the terrain where there is no material)
        float ASH_SurfaceHeight(float2 xz, out float depth, out half4 drift, out half deform)
        {
            float3 p = float3(xz.x, 0, xz.y);
            drift = ASH_DriftAt(p); deform = ASH_DeformAt(p);
            float2 dd = abs(xz - _ASH_SurfaceRect.xy);
            float edge = saturate((_ASH_SurfaceRect.z - max(dd.x, dd.y)) / max(_ASH_SurfaceRect.w, 0.01));
            depth = ASH_DepthMetres(drift, deform) * edge * (1.0 - ASH_BlockAt(xz));
            return ASH_TerrainHeight(xz) + ASH_LiftDepth(depth);
        }

        // ash/snow depth above the terrain at xz (same terms as ASH_SurfaceHeight, without the terrain height)
        float ASH_SurfaceDepth(float2 xz)
        {
            float3 p = float3(xz.x, 0, xz.y);
            half4 drift = ASH_DriftAt(p); half deform = ASH_DeformAt(p);
            float2 dd = abs(xz - _ASH_SurfaceRect.xy);
            float edge = saturate((_ASH_SurfaceRect.z - max(dd.x, dd.y)) / max(_ASH_SurfaceRect.w, 0.01));
            float depth = ASH_DepthMetres(drift, deform) * edge * (1.0 - ASH_BlockAt(xz));
            return ASH_LiftDepth(depth);
        }

        // Per-pixel thin-layer fade: where the layer is thinner than Thin Fade End the real-depth surface dissolves with a
        // world-space noise threshold (patchy, stable, no screen dither) and the terrain's own soft coverage shows instead.
        void ASH_ThinClip(float3 positionWS)
        {
            if (_ASH_Thin.w < 0.5) return;
            float2 xz = positionWS.xz;
            float2 dd = abs(xz - _ASH_SurfaceRect.xy);
            float edge = saturate((_ASH_SurfaceRect.z - max(dd.x, dd.y)) / max(_ASH_SurfaceRect.w, 0.01));
            float depth = ASH_DepthMetres(ASH_DriftAt(positionWS), ASH_DeformAt(positionWS)) * edge;
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
                gx += k * (ASH_SurfaceDepth(xz + float2(ds, 0)) - ASH_SurfaceDepth(xz - float2(ds, 0))) / (2.0 * ds);
                gz += k * (ASH_SurfaceDepth(xz + float2(0, ds)) - ASH_SurfaceDepth(xz - float2(0, ds))) / (2.0 * ds);
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
