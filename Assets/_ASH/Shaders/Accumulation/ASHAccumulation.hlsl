#ifndef ASH_ACCUMULATION_INCLUDED
#define ASH_ACCUMULATION_INCLUDED
// ASH ash-over-snow accumulation core. ONE layered system: snow underneath, ash on top, base surface below.
// All inputs are GLOBAL shader properties driven by ASHAccumulationController (single source of truth).
// Used by: ASH/Accumulation Lit (objects, trees, structures) and ASH/Terrain Lit Accum (terrain).
// See Docs/AccumulationSystem.md. Do not duplicate this logic elsewhere.

TEXTURE2D(_ASH_AshTex);     SAMPLER(sampler_ASH_AshTex);
TEXTURE2D(_ASH_SnowTex);    SAMPLER(sampler_ASH_SnowTex);
// Surface Texture (top of the layer): share the ash sampler (terrain pass is near the 16-sampler limit)
TEXTURE2D(_ASH_AshNrm); TEXTURE2D(_ASH_SnowNrm); TEXTURE2D(_ASH_DetailTex); TEXTURE2D(_ASH_DetailNrm);
TEXTURE2D(_ASH_DriftMask);  SAMPLER(sampler_ASH_DriftMask);   // R depth01 (0.35 ankle, 1 knee), G wind exposure, B slope keep, A main-path factor (1 on road/trail)
TEXTURE2D(_ASH_DeformTex);  SAMPLER(sampler_ASH_DeformTex);   // phase 2: trail depth (R) around the player, not yet written

float4 _ASH_Layers;       // x ash amount 0..1, y snow amount 0..1, z total 0..1, w enabled (0/1)
float4 _ASH_Surface;      // x coverage 0..2, y normal threshold, z blend width, w wind redistribution 0..1
float4 _ASH_Weather;      // x wetness 0..1, y melt 0..1 (snow only), z windDir.x, w windDir.z
float4 _ASH_Noise;        // x scale, y strength, z ash tiling (m), w snow tiling (m)
float4 _ASH_AccumColor;   // ash tint (rgb), a = ash darkness when wet
float4 _ASH_SnowColor;    // snow tint (rgb)
float4 _ASH_TexNorm;      // x = 1 / mean linear luminance of the ash texture; y = path depth reduction; z = path coverage reduction (drift mask A = path factor)
float4 _ASH_SurfTex;      // x layer normal strength, y detail tiling (m), z detail albedo strength, w detail normal strength
float4 _ASH_SurfTex2;     // x mip bias, y layer normal maps assigned, z detail assigned
float4 _ASH_Depth;        // x knee (drift) depth m, y surface enabled, z ankle (typical) depth m, w 0
float4 _ASH_TerrainRect;  // x minX, y minZ, z 1/sizeX, w 1/sizeZ
float4 _ASH_DeformRect;   // phase 2: x minX, y minZ, z 1/size, w enabled

float ASH_Hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
float ASH_Noise(float2 p)
{
    float2 i = floor(p), f = frac(p); float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(ASH_Hash(i), ASH_Hash(i + float2(1, 0)), u.x), lerp(ASH_Hash(i + float2(0, 1)), ASH_Hash(i + float2(1, 1)), u.x), u.y);
}
float ASH_Fbm(float2 p) { return ASH_Noise(p) * 0.6 + ASH_Noise(p * 2.37 + 17.1) * 0.3 + ASH_Noise(p * 5.11 + 3.7) * 0.1; }

// Drift mask lookup by world position (objects) or terrain uv.
half4 ASH_DriftAt(float3 positionWS)
{
    float2 uv = (positionWS.xz - _ASH_TerrainRect.xy) * _ASH_TerrainRect.zw;
    if (any(uv < 0) || any(uv > 1)) return half4(0.35, 0.5, 1, 0);
    return SAMPLE_TEXTURE2D_LOD(_ASH_DriftMask, sampler_ASH_DriftMask, uv, 0);
}

// Phase-2 hook: how much of the layer is pushed aside here (0 none .. 1 to the ground). Not written yet.
half ASH_DeformAt(float3 positionWS)
{
    if (_ASH_DeformRect.w < 0.5) return 0;
    float2 uv = (positionWS.xz - _ASH_DeformRect.xy) * _ASH_DeformRect.z;
    if (any(uv < 0) || any(uv > 1)) return 0;
    return SAMPLE_TEXTURE2D_LOD(_ASH_DeformTex, sampler_ASH_DeformTex, uv, 0).r;
}

// Physical layer depth in metres (ash + snow) after wind, coverage, main-path reduction and deformation.
// MUST match ASHAccumulationController.GetDepthMetres (CPU side used by speed/footsteps/deformers).
float ASH_DepthMetres(half4 drift, half deform)
{
    if (_ASH_Layers.w < 0.5) return 0;
    // drift R: 0.35 = typical (ankle), 1 = deepest drift (knee); calm wind -> uniform ankle depth
    float r = lerp(0.35, drift.r, saturate(_ASH_Surface.w * 2.0)); // drifts are terrain-shaped; only very low redistribution flattens them
    float d = r <= 0.35 ? _ASH_Depth.z * r / 0.35 : lerp(_ASH_Depth.z, _ASH_Depth.x, smoothstep(0.35, 1.0, r));
    float t = saturate(_ASH_Layers.x + _ASH_Layers.y * (1.0 - _ASH_Weather.y)) * (1.0 - _ASH_TexNorm.y * saturate(drift.a)) * _ASH_Surface.x;
    return t * d * (1.0 - saturate(deform));
}

// Core. drift = mask sample (R depth01, G exposure). geomNormalWS = un-mapped surface normal.
// Outputs the visible ash/snow coverage and blends the surface towards the layers.
void ASH_ApplyAccumulation(float3 positionWS, half3 geomNormalWS, half4 drift, half objectWindward,
    inout half3 albedo, inout half3 normalWS, inout half smoothness, inout half metallic, inout half occlusion)
{
    if (_ASH_Layers.w < 0.5 || _ASH_Layers.z <= 0.0001) return;

    // upward-facing mask (coverage widens the accepted angle)
    half thr = _ASH_Surface.y - (_ASH_Surface.x - 1.0) * 0.35;
    half up = saturate((geomNormalWS.y - thr) / max(_ASH_Surface.z, 0.01));

    // wind redistribution: drift mask depth (terrain lee/hollows) and windward faces of objects lose loose material
    half depthMask = lerp(0.5, drift.r, _ASH_Surface.w);
    half windLoss = _ASH_Surface.w * saturate(objectWindward) * 0.6;
    half n = ASH_Fbm(positionWS.xz * _ASH_Noise.x);
    half breakup = (n - 0.5) * _ASH_Noise.y;

    // layer thicknesses (relative): ash on top, snow underneath (melt removes snow only); deformation removes from the top
    half deform = ASH_DeformAt(positionWS);
    half pathF = saturate(drift.a);
    half scale = depthMask * (1.0 - _ASH_TexNorm.y * pathF) * (1.0 - windLoss) * up * _ASH_Surface.x;
    half ashT = _ASH_Layers.x * scale;
    half snowT = _ASH_Layers.y * (1.0 - _ASH_Weather.y) * scale;
    half remove = deform * (ashT + snowT);
    half ashLeft = max(0, ashT - remove);
    half snowLeft = max(0, snowT - max(0, remove - ashT));

    half snowCov = saturate((snowLeft * 4.0 + breakup) * 2.0);
    // ash is a thinner, patchier top layer so the snow underneath shows through where it is thin (exposed spots, noise)
    half ashCov = saturate((ashLeft * 2.2 + breakup * 1.6 - 0.25) * 2.0);
    half pathCov = 1.0 - _ASH_TexNorm.z * pathF; snowCov *= pathCov; ashCov *= pathCov; // main paths: less buildup
    half cov = max(snowCov, ashCov);
    if (cov <= 0.001) return;

    float2 xz = positionWS.xz;
    float2 uvS = xz / max(_ASH_Noise.w, 0.01), uvA = xz / max(_ASH_Noise.z, 0.01);
    half bias = _ASH_SurfTex2.x;
    half4 snowS = SAMPLE_TEXTURE2D_BIAS(_ASH_SnowTex, sampler_ASH_SnowTex, uvS, bias);
    half4 ashS = SAMPLE_TEXTURE2D_BIAS(_ASH_AshTex, sampler_ASH_AshTex, uvA, bias);
    half lum = dot(ashS.rgb, half3(0.299, 0.587, 0.114));
    half detail = clamp(lum * max(_ASH_TexNorm.x, 0.01), 0.55, 1.6);
    half3 ashAlb = _ASH_AccumColor.rgb * detail * lerp(1.0, _ASH_AccumColor.a, _ASH_Weather.x);
    half3 snowAlb = snowS.rgb * _ASH_SnowColor.rgb * lerp(1.0, 0.85, _ASH_Weather.x);

    // layer normal (tangent-space maps projected on world XZ around the un-mapped surface normal) + fine detail
    half3 layerN = geomNormalWS; half3 tn = half3(0, 0, 0);
    if (_ASH_SurfTex2.y > 0.5)
    {
        half3 nS = UnpackNormalScale(SAMPLE_TEXTURE2D_BIAS(_ASH_SnowNrm, sampler_ASH_AshTex, uvS, bias), _ASH_SurfTex.x);
        half3 nA = UnpackNormalScale(SAMPLE_TEXTURE2D_BIAS(_ASH_AshNrm, sampler_ASH_AshTex, uvA, bias), _ASH_SurfTex.x);
        tn.xy = lerp(nS.xy * snowCov, nA.xy, ashCov);
    }
    if (_ASH_SurfTex2.z > 0.5)
    {
        float2 uvD = xz / _ASH_SurfTex.y;
        half dA = SAMPLE_TEXTURE2D_BIAS(_ASH_DetailTex, sampler_ASH_AshTex, uvD, bias).g;
        half m = lerp(1.0, dA * 2.0, _ASH_SurfTex.z); snowAlb *= m; ashAlb *= m;
        tn.xy += UnpackNormalScale(SAMPLE_TEXTURE2D_BIAS(_ASH_DetailNrm, sampler_ASH_AshTex, uvD, bias), _ASH_SurfTex.w).xy;
    }
    layerN = normalize(geomNormalWS + half3(tn.x, 0, tn.y));

    albedo = lerp(albedo, snowAlb, snowCov);
    albedo = lerp(albedo, ashAlb, ashCov);
    half layerSmooth = lerp(lerp(0.12, 0.55, _ASH_Weather.x), lerp(0.3, 0.7, _ASH_Weather.x), snowCov * (1 - ashCov));
    smoothness = lerp(smoothness, layerSmooth, cov);
    metallic = lerp(metallic, 0, cov);
    occlusion = lerp(occlusion, 1, cov * 0.7);
    normalWS = normalize(lerp(normalWS, layerN, cov * 0.75));
}

#endif
