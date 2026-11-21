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
TEXTURE2D(_ASH_RippleNrm); // optional directional ripple normal (Look > Ripple Strength, 0 = off)
float4 _ASH_Ripple;        // x ripple normal strength, y ripple tiling (m)
TEXTURE2D(_ASH_DriftMask);  SAMPLER(sampler_ASH_DriftMask);   // R depth01 (0.35 ankle, 1 knee), G wind exposure, B slope keep, A main-path factor (1 on road/trail)
TEXTURE2D(_ASH_DeformTex);  SAMPLER(sampler_ASH_DeformTex);   // phase 2: trail depth (R) around the player, not yet written

float4 _ASH_Layers;       // x ash amount 0..1, y snow amount 0..1, z total 0..1, w enabled (0/1)
float4 _ASH_Surface;      // x coverage 0..2, y normal threshold, z blend width, w wind redistribution 0..1
float4 _ASH_Weather;      // x wetness 0..1, y melt 0..1 (snow only), z windDir.x, w windDir.z
float4 _ASH_Noise;        // x scale, y strength, z ash tiling (m), w snow tiling (m)
float  _ASH_TrailKeep;     // share of the colour kept in footprints / trails (deform only dents the layer)
float4 _ASH_AccumColor;   // ash tint (rgb), a = ash darkness when wet
float4 _ASH_SnowColor;    // snow tint (rgb)
float4 _ASH_TexNorm;      // x = 1 / mean linear luminance of the ash texture; y = path depth reduction; z = path coverage reduction (drift mask A = path factor)
float4 _ASH_SurfTex;      // x layer normal strength, y detail tiling (m), z detail albedo strength, w detail normal strength
float4 _ASH_SurfTex2;     // x mip bias, y layer normal maps assigned, z detail assigned
float4 _ASH_Depth;        // x knee (drift) depth m, y surface enabled, z ankle (typical) depth m, w 0
float4 _ASH_TerrainRect;  // x minX, y minZ, z 1/sizeX, w 1/sizeZ
float4 _ASH_DeformRect;   // phase 2: x minX, y minZ, z 1/size, w enabled
float4 _ASH_Edge;         // x coverage edge softness, y patch noise scale (1/m), z patch noise strength, w smooth (bicubic) drift sampling 0/1
float4 _ASH_DriftTexel;   // x 1/width, y 1/height, z width, w height (drift mask)
float4 _ASH_PropMul;      // objects (ASH/Accumulation Lit, ASH_ACCUM_PROP): x ash, y snow multiplier of the terrain amount (0..4; default 0.5)
// Deformable surface + banking (set by ASHAccumulationSurface while the visible-depth surface runs)
TEXTURE2D(_ASH_Heightmap);
float4 _ASH_HeightParams;   // x heightmap res, y height scale (size.y / kMaxHeight), z terrain y, w unused
float4 _ASH_SurfaceRect;    // x centre x, y centre z, z half size, w edge fade width
TEXTURE2D(_ASH_ColliderMask); // R collider fade (1 = cleared; small / non-banking colliders), G distance m to the nearest banking collider footprint
float4 _ASH_ColliderMaskRect; // x minX, y minZ, z 1/size (square), w enabled
float4 _ASH_Bank;           // x bank height m, y bank reach m, z curve exponent, w enabled
float4 _ASH_Bank2;          // x noise scale 1/m, y noise strength 0..1, z layer depth (m) for a full-height bank, w mask texel m
float4 _ASH_Contact;        // x contact curve height m (extra concave fillet against the object), y falloff m, z noise 0..1, w enabled
float4 _ASH_Look;         // t471: x macro 1/m, y macro albedo variation, z snow sparkle, w sparkle cells/m
float4 _ASH_Look2;        // t471: x ash grain, y ash grain cells/m, z ash texture colour 0..1, w normal depth fade 0..1
float4 _ASH_Look3;        // t471: x sparkle density, y macro uv warp (repeats), z normal full depth (relative), w 0
float4 _ASH_AshColor2;    // t471: grey-brown ash variation colour (rgb), a amount
float4 _ASH_Dust;           // x dusting height above the bank/layer top m, y fade m, z strength 0..1, w enabled

float4 _ASH_Debug;        // t549 live debug (ASH_AccumulationLook > Debug): x drift mask off, y sparkle off, z slope mask off, w detail normal off
float4 _ASH_Debug2;       // x layer (snow/ash) normal off, y ash grain + clumps off, z breakup noise off, w macro variation off
float4 _ASH_Debug3;       // x old hash (shows the t549 stripes again), y ripple off
// t549 fix: the old hash multiplied the cell coordinate by ~456 before frac(). At grain scale (30+ cells/m) a few hundred metres from
// the origin that product exceeds float precision, frac() collapses and the "noise" turns into straight, evenly spaced diagonal stripes.
float ASH_HashLegacy(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
float ASH_Hash(float2 p)
{
    if (_ASH_Debug3.x > 0.5) return ASH_HashLegacy(p);
    p = p - 4096.0 * floor(p / 4096.0);                 // wrap the cell index: precision-safe anywhere on the map
    float3 p3 = frac(float3(p.x, p.y, p.x) * 0.1031);   // hash without sine (Hoskins), float-only (works on every shader target)
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}
float ASH_Noise(float2 p)
{
    float2 i = floor(p), f = frac(p); float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(ASH_Hash(i), ASH_Hash(i + float2(1, 0)), u.x), lerp(ASH_Hash(i + float2(0, 1)), ASH_Hash(i + float2(1, 1)), u.x), u.y);
}
float ASH_Fbm(float2 p) { return ASH_Noise(p) * 0.6 + ASH_Noise(p * 2.37 + 17.1) * 0.3 + ASH_Noise(p * 5.11 + 3.7) * 0.1; }

// Drift mask lookup. Smooth mode = cubic B-spline from 4 bilinear taps, so the coarse mask texels (1-2 m) never show
// as stair-stepped diagonal edges once the coverage ramps it up; falls back to plain bilinear.
half4 ASH_DriftBicubic(float2 uv)
{
    float2 st = uv * _ASH_DriftTexel.zw - 0.5;
    float2 i = floor(st), f = st - i, f2 = f * f, f3 = f2 * f;
    float2 w0 = (1.0 - 3.0 * f + 3.0 * f2 - f3) / 6.0, w1 = (4.0 - 6.0 * f2 + 3.0 * f3) / 6.0;
    float2 w2 = (1.0 + 3.0 * f + 3.0 * f2 - 3.0 * f3) / 6.0, w3 = f3 / 6.0;
    float2 g0 = w0 + w1, g1 = w2 + w3;
    float2 p0 = (i - 1.0 + w1 / g0 + 0.5) * _ASH_DriftTexel.xy, p1 = (i + 1.0 + w3 / g1 + 0.5) * _ASH_DriftTexel.xy;
    half4 a = SAMPLE_TEXTURE2D_LOD(_ASH_DriftMask, sampler_ASH_DriftMask, float2(p0.x, p0.y), 0);
    half4 b = SAMPLE_TEXTURE2D_LOD(_ASH_DriftMask, sampler_ASH_DriftMask, float2(p1.x, p0.y), 0);
    half4 c = SAMPLE_TEXTURE2D_LOD(_ASH_DriftMask, sampler_ASH_DriftMask, float2(p0.x, p1.y), 0);
    half4 d = SAMPLE_TEXTURE2D_LOD(_ASH_DriftMask, sampler_ASH_DriftMask, float2(p1.x, p1.y), 0);
    return lerp(lerp(a, b, g1.x), lerp(c, d, g1.x), g1.y);
}

half4 ASH_DriftUV(float2 uv)
{
    if (_ASH_Debug.x > 0.5) return half4(0.35, 0.5, 1, 0);
    half4 r = half4(0, 0, 0, 0);
    UNITY_BRANCH if (_ASH_Edge.w > 0.5 && _ASH_DriftTexel.z > 2.0) r = ASH_DriftBicubic(uv); // 4 taps
    else r = SAMPLE_TEXTURE2D_LOD(_ASH_DriftMask, sampler_ASH_DriftMask, uv, 0);
    return r;
}

// Drift mask lookup by world position (objects, surface) - terrain passes call ASH_DriftUV with their uv.
half4 ASH_DriftAt(float3 positionWS)
{
    float2 uv = (positionWS.xz - _ASH_TerrainRect.xy) * _ASH_TerrainRect.zw;
    if (any(uv < 0) || any(uv > 1)) return half4(0.35, 0.5, 1, 0);
    return ASH_DriftUV(uv);
}
// Single bilinear tap (used for lighting-normal slope differences, where bicubic smoothing is not visible)
half4 ASH_DriftAtFast(float3 positionWS)
{
    float2 uv = (positionWS.xz - _ASH_TerrainRect.xy) * _ASH_TerrainRect.zw;
    if (any(uv < 0) || any(uv > 1) || _ASH_Debug.x > 0.5) return half4(0.35, 0.5, 1, 0);
    return SAMPLE_TEXTURE2D_LOD(_ASH_DriftMask, sampler_ASH_DriftMask, uv, 0);
}

// Soft coverage ramp: smoothstep centred on c over the edge softness width (replaces the old linear x2 gain).
half ASH_Cov(half x, half c) { half w = max(_ASH_Edge.x, 0.01) * 0.5; return smoothstep(c - w, c + w, x); }
// world-space breakup (fine fbm + large patches) for natural, patchy thin-layer edges
half ASH_Breakup(float2 xz)
{
    half b = (ASH_Fbm(xz * _ASH_Noise.x) - 0.5) * _ASH_Noise.y;
    return b + (ASH_Fbm(xz * max(_ASH_Edge.y, 1e-4) + 31.7) - 0.5) * _ASH_Edge.z;
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

// ---- terrain height / collider mask / banks ---------------------------------------------------------------------------
float ASH_TerrainHeight(float2 xz)
{
    float2 uv01 = saturate((xz - _ASH_TerrainRect.xy) * _ASH_TerrainRect.zw);
    float res = _ASH_HeightParams.x;
    float2 uv = (uv01 * (res - 1.0) + 0.5) / res;
    float h = SAMPLE_TEXTURE2D_LOD(_ASH_Heightmap, sampler_LinearClamp, uv, 0).r; // always bilinear (the terrain heightmap RT may be point-filtered)
    return h * _ASH_HeightParams.y + _ASH_HeightParams.z;
}

// collider mask at xz: R fade (0 outside), G distance to a banking collider's footprint edge (large = none)
float2 ASH_ColliderMaskAt(float2 xz)
{
    if (_ASH_ColliderMaskRect.w < 0.5) return float2(0, 1e4);
    float2 uv = (xz - _ASH_ColliderMaskRect.xy) * _ASH_ColliderMaskRect.z;
    if (any(uv < 0.0) || any(uv > 1.0)) return float2(0, 1e4);
    return SAMPLE_TEXTURE2D_LOD(_ASH_ColliderMask, sampler_LinearClamp, uv, 0).rg;
}

// surface-area edge fade (1 inside the displaced area, 0 at its border)
float ASH_SurfaceEdge(float2 xz)
{
    float2 dd = abs(xz - _ASH_SurfaceRect.xy);
    return saturate((_ASH_SurfaceRect.z - max(dd.x, dd.y)) / max(_ASH_SurfaceRect.w, 0.01));
}

// Bank shape: rises from 0 at Reach to full Height at the collider's real footprint edge. Curve > 1 = steeper close to the
// object (piled against it), < 1 = broad gentle swell. World-space noise varies both height and reach (natural, uneven).
float ASH_BankProfile(float2 xz, float dist)
{
    float k = saturate(_ASH_Bank2.y);
    if (_ASH_Bank.w < 0.5 || dist >= _ASH_Bank.y * (1.0 + 0.4 * k)) return 0; // beyond the longest (noisy) reach: skip the noise
    float n = ASH_Fbm(xz * max(_ASH_Bank2.x, 1e-4) + 7.7);
    float reach = max(_ASH_Bank.y * lerp(1.0, 0.6 + 0.8 * n, k), 0.01);
    float t = saturate(1.0 - dist / reach);
    t = t * t * (3.0 - 2.0 * t);
    float b = _ASH_Bank.x * pow(t, max(_ASH_Bank.z, 0.05)) * lerp(1.0, 0.5 + n, k);
    // contact curve: a concave fillet in the last Contact.y metres, so the snow curves UP onto the object's base
    if (_ASH_Contact.w > 0.5 && _ASH_Contact.x > 0.0)
    {
        float c = saturate(1.0 - dist / max(_ASH_Contact.y * lerp(1.0, 0.7 + 0.6 * n, saturate(_ASH_Contact.z)), 0.01));
        b += _ASH_Contact.x * c * c * lerp(1.0, 0.45 + 1.1 * n, saturate(_ASH_Contact.z));
    }
    return b;
}

// Bank height (m) added on top of the layer: grows with the material lying there (full at Bank2.z depth) and is pressed
// down by footprints / trails exactly like the layer (deform), so the player still digs through banks.
float ASH_BankAt(float2 xz, half4 drift, half deform, float dist)
{
    float b = ASH_BankProfile(xz, dist);
    if (b <= 0.0) return 0;
    return b * saturate(ASH_DepthMetres(drift, 0) / max(_ASH_Bank2.z, 0.005)) * (1.0 - saturate(deform));
}

// Light dusting on the bottom few cm of objects standing in the layer (ASH/Accumulation Lit): from the bank / layer top up
// Dust.x metres (noisy line), fading over Dust.y. Only inside the visible-depth surface area, so it matches the banks.
void ASH_BaseDust(float3 positionWS, inout half3 albedo, inout half3 normalWS, inout half smoothness, inout half metallic)
{
    if (_ASH_Dust.w < 0.5 || _ASH_Layers.w < 0.5 || _ASH_HeightParams.x < 1.0) return;
    float2 xz = positionWS.xz;
    float edge = ASH_SurfaceEdge(xz); if (edge <= 0.0) return;
    float ground = ASH_TerrainHeight(xz);
    if (positionWS.y - ground > 2.0 * _ASH_Depth.x + (_ASH_Bank.x + _ASH_Contact.x) * 1.6 + (_ASH_Dust.x + 0.5 * _ASH_Contact.x) * 1.7 + _ASH_Dust.y + _ASH_Contact.x) return; // above any possible dust line (most object pixels): one tap
    half4 drift = ASH_DriftAt(positionWS); half deform = ASH_DeformAt(positionWS);
    float base = ASH_DepthMetres(drift, 0); if (base <= 0.001) return;
    float2 cm = ASH_ColliderMaskAt(xz);
    float top = ground + (ASH_DepthMetres(drift, deform) + ASH_BankAt(xz, drift, deform, cm.y)) * edge;
    float n = ASH_Fbm(xz * 4.3 + positionWS.y * 3.1);
    // band on the object from the snow top: dusting plus half the contact curve, a noisy upper edge, fading upward
    float cOn = _ASH_Contact.w > 0.5 ? 1.0 : 0.0;
    float h0 = (_ASH_Dust.x + 0.5 * _ASH_Contact.x * cOn) * lerp(0.4 + 1.2 * n, lerp(1.0, 0.3 + 1.4 * n, saturate(_ASH_Contact.z)), cOn);
    float fadeW = max(_ASH_Dust.y + 0.5 * _ASH_Contact.x * cOn, 0.005);
    half k = (half)((1.0 - smoothstep(h0, h0 + fadeW, positionWS.y - top)) * saturate(_ASH_Dust.z) * edge * saturate(base / max(_ASH_Bank2.z, 0.005)));
    if (k <= 0.001) return;
    float2 uvS = xz / max(_ASH_Noise.w, 0.01);
    half3 snowAlb = SAMPLE_TEXTURE2D_LOD(_ASH_SnowTex, sampler_ASH_SnowTex, uvS, 0).rgb * _ASH_SnowColor.rgb;
    half ashF = saturate(_ASH_Layers.x / max(_ASH_Layers.x + _ASH_Layers.y * (1.0 - _ASH_Weather.y), 1e-3));
    half3 col = lerp(snowAlb, _ASH_AccumColor.rgb * lerp(1.0, _ASH_AccumColor.a, _ASH_Weather.x), ashF);
    albedo = lerp(albedo, col, k);
    smoothness = lerp(smoothness, lerp(0.3, 0.12, ashF), k);
    metallic = lerp(metallic, 0, k);
    normalWS = normalize(lerp(normalWS, half3(0, 1, 0), k * 0.35));
}

// Core. drift = mask sample (R depth01, G exposure). geomNormalWS = un-mapped surface normal.
// Outputs the visible ash/snow coverage and blends the surface towards the layers.
void ASH_ApplyAccumulation(float3 positionWS, half3 geomNormalWS, half4 drift, half objectWindward,
    inout half3 albedo, inout half3 normalWS, inout half smoothness, inout half metallic, inout half occlusion)
{
    if (_ASH_Layers.w < 0.5 || _ASH_Layers.z <= 0.0001) return;

    // upward-facing mask (coverage widens the accepted angle)
    half thr = _ASH_Surface.y - (_ASH_Surface.x - 1.0) * 0.35;
    half up = _ASH_Debug.z > 0.5 ? 1.0 : saturate((geomNormalWS.y - thr) / max(_ASH_Surface.z, 0.01));

    // wind redistribution: drift mask depth (terrain lee/hollows) and windward faces of objects lose loose material
    half depthMask = lerp(0.5, drift.r, _ASH_Surface.w);
    half windLoss = _ASH_Surface.w * saturate(objectWindward) * 0.6;
    half breakup = _ASH_Debug2.z > 0.5 ? 0.0 : ASH_Breakup(positionWS.xz);

    // layer thicknesses (relative): ash on top, snow underneath (melt removes snow only); deformation removes from the top
    half deform = ASH_DeformAt(positionWS);
    half pathF = saturate(drift.a);
    half scale = depthMask * (1.0 - _ASH_TexNorm.y * pathF) * (1.0 - windLoss) * up * _ASH_Surface.x;
    half ashT = _ASH_Layers.x * scale;
    half snowT = _ASH_Layers.y * (1.0 - _ASH_Weather.y) * scale;
#ifdef ASH_ACCUM_PROP
    // objects build up with the terrain (same amounts, rate, drift, melt) scaled by the controller's prefab multipliers
    ashT *= _ASH_PropMul.x; snowT *= _ASH_PropMul.y;
#endif
    half remove = deform * (1.0 - saturate(_ASH_TrailKeep)) * (ashT + snowT);
    half ashLeft = max(0, ashT - remove);
    half snowLeft = max(0, snowT - max(0, remove - ashT));

    // soft edges + thin layers fade to nothing (no hard thresholds on the coarse mask)
    half snowCov = ASH_Cov(snowLeft * 4.0 + breakup, 0.25) * smoothstep(0.0, 0.03, snowLeft);
    // ash is a thinner, patchier top layer so the snow underneath shows through where it is thin (exposed spots, noise)
    half ashCov = ASH_Cov(ashLeft * 2.2 + breakup * 1.6 - 0.25, 0.25) * smoothstep(0.0, 0.03, ashLeft);
    half pathCov = 1.0 - _ASH_TexNorm.z * pathF; snowCov *= pathCov; ashCov *= pathCov; // main paths: less buildup
    half cov = max(snowCov, ashCov);
    if (cov <= 0.001) return;

    float2 xz = positionWS.xz;
    // t471: macro patches (break up tiling: brightness + uv warp). Cheap value-noise fbm, no extra textures or passes.
    half macro = (_ASH_Look.x > 0.0 && _ASH_Debug2.w < 0.5) ? ASH_Fbm(xz * _ASH_Look.x) : 0.5;
    float2 warp = (macro - 0.5) * _ASH_Look3.y;
    float2 uvS = xz / max(_ASH_Noise.w, 0.01) + warp, uvA = xz / max(_ASH_Noise.z, 0.01) - warp;
    half bias = _ASH_SurfTex2.x;
    half4 snowS = SAMPLE_TEXTURE2D_BIAS(_ASH_SnowTex, sampler_ASH_SnowTex, uvS, bias);
    half4 ashS = SAMPLE_TEXTURE2D_BIAS(_ASH_AshTex, sampler_ASH_AshTex, uvA, bias);
    half lum = dot(ashS.rgb, half3(0.299, 0.587, 0.114));
    half detail = clamp(lum * max(_ASH_TexNorm.x, 0.01), 0.55, 1.6);
    // t471: ash colour variation from the texture itself (normalised to Ash Color), grey-brown clumps and fine grain
    half3 ashVar = lerp(half3(detail, detail, detail), clamp(ashS.rgb * max(_ASH_TexNorm.x, 0.01), 0.4, 1.8), _ASH_Look2.z);
    half3 ashTint = _ASH_AccumColor.rgb;
    if (_ASH_AshColor2.a > 0.001 && _ASH_Debug2.y < 0.5)
    {
        half clump = ASH_Noise(xz * max(_ASH_Look2.y, 1.0) * 0.17 + 7.3) * 0.7 + macro * 0.3;
        ashTint = lerp(ashTint, _ASH_AshColor2.rgb, _ASH_AshColor2.a * saturate(clump * 1.6 - 0.3));
    }
    if (_ASH_Look2.x > 0.001 && _ASH_Debug2.y < 0.5)
    {
        float2 g = xz * _ASH_Look2.y;
        half grain = ASH_Noise(g) * 0.65 + ASH_Noise(g * 0.31 + 11.7) * 0.35;
        ashVar *= 1.0 + (grain - 0.5) * 2.0 * _ASH_Look2.x;
    }
    half3 ashAlb = ashTint * ashVar * lerp(1.0, _ASH_AccumColor.a, _ASH_Weather.x);
    half3 snowAlb = snowS.rgb * _ASH_SnowColor.rgb * lerp(1.0, 0.85, _ASH_Weather.x);
    half mv = 1.0 + (macro - 0.5) * 2.0 * _ASH_Look.y; snowAlb *= mv; ashAlb *= mv;

    // layer normal (tangent-space maps projected on world XZ around the un-mapped surface normal) + fine detail
    half3 layerN = geomNormalWS; half3 tn = half3(0, 0, 0);
    if (_ASH_SurfTex2.y > 0.5 && _ASH_Debug2.x < 0.5)
    {
        half3 nS = UnpackNormalScale(SAMPLE_TEXTURE2D_BIAS(_ASH_SnowNrm, sampler_ASH_AshTex, uvS, bias), _ASH_SurfTex.x);
        half3 nA = UnpackNormalScale(SAMPLE_TEXTURE2D_BIAS(_ASH_AshNrm, sampler_ASH_AshTex, uvA, bias), _ASH_SurfTex.x);
        tn.xy = lerp(nS.xy * snowCov, nA.xy, ashCov);
    }
    if (_ASH_SurfTex2.z > 0.5)
    {
        float2 uvD = xz / _ASH_SurfTex.y + warp;
        half dA = SAMPLE_TEXTURE2D_BIAS(_ASH_DetailTex, sampler_ASH_AshTex, uvD, bias).g;
        half m = lerp(1.0, dA * 2.0, _ASH_SurfTex.z); snowAlb *= m; ashAlb *= m;
        if (_ASH_Debug.w < 0.5) tn.xy += UnpackNormalScale(SAMPLE_TEXTURE2D_BIAS(_ASH_DetailNrm, sampler_ASH_AshTex, uvD, bias), _ASH_SurfTex.w).xy;
    }
    if (_ASH_Ripple.x > 0.001 && _ASH_Debug3.y < 0.5)
        tn.xy += UnpackNormalScale(SAMPLE_TEXTURE2D_BIAS(_ASH_RippleNrm, sampler_ASH_AshTex, xz / _ASH_Ripple.y + warp, bias), _ASH_Ripple.x).xy;
    // t471: thin dustings follow the ground; the bump grows to full strength with layer depth
    tn.xy *= lerp(1.0, saturate((ashLeft + snowLeft) / max(_ASH_Look3.z, 0.01)), _ASH_Look2.w);
    layerN = normalize(geomNormalWS + half3(tn.x, 0, tn.y));

    // t471: snow sparkle - sparse view-dependent glints (hash cells), faded with distance to avoid shimmer
    if (_ASH_Look.z > 0.001 && _ASH_Debug.y < 0.5)
    {
        float3 toCam = _WorldSpaceCameraPos.xyz - positionWS; half dist = length(toCam); half3 V = toCam / max(dist, 0.001);
        half h = ASH_Hash(floor(xz * _ASH_Look.w) + floor(V.xz * 6.0 + V.y * 3.0) * 13.7);
        half glint = step(1.0 - _ASH_Look3.x, h) * saturate(dot(layerN, V) * 2.0) * saturate(1.0 - dist / 30.0);
        snowAlb += glint * _ASH_Look.z * (1.0 - ashCov);
    }

    albedo = lerp(albedo, snowAlb, snowCov);
    albedo = lerp(albedo, ashAlb, ashCov);
    half layerSmooth = lerp(lerp(0.12, 0.55, _ASH_Weather.x), lerp(0.3, 0.7, _ASH_Weather.x), snowCov * (1 - ashCov));
    smoothness = lerp(smoothness, layerSmooth, cov);
    metallic = lerp(metallic, 0, cov);
    occlusion = lerp(occlusion, 1, cov * 0.7);
    normalWS = normalize(lerp(normalWS, layerN, cov * 0.75));
}

#endif
