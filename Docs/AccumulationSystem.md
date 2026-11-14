# ASH Accumulation System (ash over snow)

**Single source of truth** for surface accumulation and (from phase 2) deformation. Weather systems
(e.g. a future `ASHWeatherDirector`) only **feed inputs**; they must not add a second accumulation shader,
duplicate deformation logic, or write the global shader properties below themselves.

Status: **Phase 1 (buildup shader) built.** Phase 2 (deformable trails), phase 3 (speed/footsteps) not built.

## Layer model
One layered system: **ash on top**, **snow underneath**, base surface (terrain/material) below.
Layer state lives on `ASHAccumulationController`: `ashAmount`, `snowAmount` (relative depth 0..1).
Local depth (metres) = layer amount x coverage x path factor x depth curve(drift mask R):
R ≤ 0.35 scales up to `ankleDepthMetres` (0.12 m); R 0.35 → 1 rises smoothly to `maxDepthMetres` (knee, 0.5 m).
Very low wind redistribution flattens drifts toward ankle depth. Map-wide ≈ 80% is 0.05–0.15 m, ≈ 10% is knee-deep.
Same curve in C# (`GetBaseDepthMetres`) and HLSL (`ASH_DepthMetres`). Trails remove ash first, then snow, then expose ground.

## Files
| What | Path |
|---|---|
| Shader core (only place the layer maths lives) | `Assets/_ASH/Shaders/Accumulation/ASHAccumulation.hlsl` |
| Object shader (URP 17.4 Lit copy + accumulation; same properties as URP/Lit) | `ASH_AccumulationLit.shader` ("ASH/Accumulation Lit"), `ASHLitForwardPass.hlsl`, `ASHLitGBufferPass.hlsl` |
| Terrain shader (URP 17.4 Terrain/Lit copy + accumulation; base/add/basemap passes) | `ASH_TerrainLitAccum*.shader` ("ASH/Terrain Lit Accum"), `ASHTerrainLitPasses.hlsl` |
| Controller | `Assets/_ASH/Scripts/World/Accumulation/ASHAccumulationController.cs` |
| Weather input types | `ASHWeatherInputs.cs` (`ASHWeatherInputs` struct, `IASHWeatherInputs`) |
| Drop-on component | `ASHAccumulation.cs` |
| Editor tools | `Assets/_ASH/Editor/World/ASHAccumulationTools.cs` (menus `ASH/World/Accumulation/*`, `ASH/World/Apply Accumulation To Selected`) |
| Drift/depth mask | `Assets/_ASH/Art/Terrain/ASH_DriftMask.png` (R depth01, G wind exposure, B slope keep; linear, readable) |
| Generated materials / tree variants | `Assets/_ASH/Art/Accumulation/{Materials,Trees}/`, terrain material `ASH_TerrainAccum.mat` |

The copied URP shader files must be re-synced if URP is upgraded (they are marked at the top).

## Public API (`ASHAccumulationController.Instance`)
```csharp
// Weather → accumulation (push each frame or on change; overrides ambientWeather for 5 s)
void SetWeatherInputs(in ASHWeatherInputs w);
void SetWeatherInputs(float ashfall, float snowfall, Vector2 windDir, float windStrength, float temperatureC, float rain);
// or pull: assign a component implementing IASHWeatherInputs to controller.weatherSource
ASHWeatherInputs CurrentInputs { get; }

// Queries (speed / footsteps / AI)
float GetDepthMetres(Vector3 worldPos);                 // current material depth (minus deformation)
void  GetLayers(Vector3 worldPos, out float ash, out float snow);

// Phase 2 stubs (do not reimplement elsewhere)
void  Deform(Vector3 worldPos, float radius, float strength);   // logs once, no-op
float GetDeformation(Vector3 worldPos);                          // returns 0

void ApplyPreset(Preset p);   // Light, Default, Heavy, Extreme
void PushGlobals();
```
`ASHWeatherInputs`: `ashfallIntensity`, `snowfallIntensity` (0..1), `windDirection` (XZ, blowing towards),
`windStrength` (0..1), `temperatureC`, `rainIntensity` (0..1).

### How inputs affect surfaces (decided by the controller only)
| Control (inspector header) | Behaviour |
|---|---|
| 1. Accumulation rate | `ash += rate × ashfall`, `snow += rate × snowfall` per minute |
| 2. Surface coverage | coverage multiplier, upward-normal threshold/blend, noise breakup |
| 3. Deformation | **placeholder** (`deformationEnabled`, `footprintDepth`, `deformationRefill`) |
| 4. Wind redistribution | scales drift-mask contrast (lee/hollows keep, exposed lose) and strips windward object faces; `prevailingWind` bakes the mask |
| 5. Rain and melting | above 0 °C snow melts (`meltPerDegree`, ×`rainMeltBoost` in rain); rain/melt raise `wetness` (wet ash darkens, glossier); dries at `dryRate`; ash does not melt |
| 6. Persistence | `ashDecayPerMinute`, `snowDecayPerMinute` (0 = permanent) |

## Global shader properties (written ONLY by the controller)
| Property | Meaning |
|---|---|
| `_ASH_Layers` | x ash, y snow, z total, w enabled (0 = shaders inert, e.g. no controller) |
| `_ASH_Surface` | x coverage, y normal threshold, z blend, w effective wind redistribution |
| `_ASH_Weather` | x wetness, y extra snow melt, zw wind direction (XZ) |
| `_ASH_Noise` | x noise scale, y noise strength, z ash tiling (m), w snow tiling (m) |
| `_ASH_AccumColor` | ash tint rgb, a wet darkness |
| `_ASH_SnowColor` | snow tint |
| `_ASH_TexNorm` | x = 1 / mean linear luminance of the ash texture (`ashTextureMean`) |
| `_ASH_AshTex`, `_ASH_SnowTex` | layer albedo (ASH ash texture; Gaia/NatureManufacture snow sample by reference) |
| `_ASH_DriftMask`, `_ASH_TerrainRect` | drift mask and terrain XZ rect (min, 1/size) |
| `_ASH_DeformTex`, `_ASH_DeformRect` | **phase 2**: trail texture and its rect (w=0 disabled today) |

## Materials and objects
* **Trees**: `ASH/World/Accumulation/3. Apply To ASH Tree Prototypes` makes prefab variants
  (`Art/Accumulation/Trees/*_Accum.prefab`) with ASH material copies and swaps only the ASH-scattered prototypes
  (also updates the vegetation settings/record so re-scatters keep them). Anthony's own prototypes are untouched.
* **Structures**: add `ASHAccumulation` (or select + `ASH/World/Apply Accumulation To Selected`). Only URP/Lit and Gaia
  PW General (cabin) materials are converted (PW: albedo/normal/metal-gloss/cutout remapped; its vertex AO, wind and PW cover layers are dropped) (variants cached in `Art/Accumulation/Materials`); others are skipped. `Revert()` restores.
* **Pickups are never accumulated**: any renderer under `ASHItemPickup` or an `IASHUsable` is skipped, and
  pickup prefab folders are refused.
* **Terrain**: `materialTemplate` = `ASH_TerrainAccum.mat`; the original is kept on the controller
  (`Restore Original Terrain Material` context menu).

## Phase 2: visible depth, deformation, foot IK (Play Mode)
All runtime parts are auto-added to the controller GameObject in Play (no scene changes needed).
- **ASHAccumulationSurface**: dense grid following the camera, displaced in the vertex shader
  (`Hidden/ASH/Accumulation Surface`) to terrain height + `ASH_DepthMetres`; fades to the terrain shader's coverage over
  `displacementFade` at its edge. Visual only: colliders stay on the terrain, characters sink in. Oriented-box blockers
  (nearest 16 within 40 m, refreshed every 1 s) keep structures with `ASHAccumulation` and pickups/interactables clear.
- **ASHDeformationField**: ping-pong RHalf render texture (`Hidden/ASH/DeformUpdate`) that follows the player, R = fraction
  pushed aside. Sets `_ASH_DeformTex` / `_ASH_DeformRect`; the terrain shader and the surface both read it (one field).
  Refill per second = `(deformationRefill × accumulationRate × (ashfall + snowfall) + trailFadePerMinute) / 60`, so trails
  persist through weather changes and new fall fills them. CPU copy (async region readback around the player) feeds
  `GetDeformation` / `GetDepthMetres`. Trails outside the window are forgotten; changing area/resolution clears trails.
- **ASHDeformer**: stamper component (player feet by default via the Humanoid rig; add explicit points for animals,
  NPCs, tyres). Segments from the last contact position, wading trench when deep. Max 32 stamps per update.
- **API**: `Deform(pos, radius, strength)`, `DeformSegment(a, b, radius, strength)`, `GetDeformation(pos)`,
  `GetBaseDepthMetres(pos)`, `GetDepthMetres(pos)` (includes deformation).
- **Globals**: `_ASH_Depth` (x knee m, y surface on, z ankle m), `_ASH_Heightmap`, `_ASH_HeightParams`,
  `_ASH_SurfaceRect`, `_ASH_Blockers[16]`, `_ASH_BlockerRot[16]`, `_ASH_BlockerCount`, `_ASH_GridStep`, `_ASH_SurfQuality`.

### Deformation quality (graphics setting)
Controller → *Deformation quality*. Runtime-safe: `SetDeformationQuality(DeformQuality)` or
`SetDeformationQualityCustom(resolution, areaRadius, meshRadius, gridStep, stampHz, refillHz, fade, normals)`.
Components rebuild their render textures / mesh cleanly (old ones destroyed); Off frees both.

| Preset | RT | Trail area radius | Mesh radius | Grid | Stamp Hz | Refill Hz | Fade | Normals | Verts |
|---|---|---|---|---|---|---|---|---|---|
| Off | – | – | – | – | – | – | – | – | 0 (shader-only coverage) |
| Low | 512 | 32 m | 16 m | 0.33 m | 20 | 2 | 5 m | coarse | 9.6k |
| Medium (default) | 1024 | 64 m | 24 m | 0.20 m | 30 | 4 | 8 m | vertex | 58k |
| High | 2048 | 64 m | 36 m | 0.15 m | 60 | 10 | 12 m | + per-pixel prints | 231k |
| Ultra | 2048 | 96 m | 48 m | 0.10 m | every frame | 20 | 16 m | + per-pixel | 924k |
| Extreme | 4096 | 128 m | 64 m | 0.075 m | every frame | 30 | 24 m | + per-pixel | 2.9M |

### Surface Texture (top of the ash/snow; separate from deformation quality)
On `ASH_Accumulation` > `ASHAccumulationController` > **Surface Texture** header. The deformation quality presets (RT resolution, trail area, mesh density/radius, stamp/refill rates, fade, footprint normals) never change anything here, and this section never changes deformation.
- `ashTexture` / `ashNormal`, `snowTexture` / `snowNormal`: albedo + optional tangent-space normal, world-projected (XZ) on the layer.
- `ashTiling` / `snowTiling`: metres per repeat. `surfaceNormalStrength`: layer normal strength. `ashTextureMean`: ash luminance normaliser.
- `detailTexture` / `detailNormal`, `detailTiling` (m), `detailAlbedoStrength`, `detailNormalStrength`: fine detail over both layers (grey 0.5 = neutral).
- `surfaceMipBias`: texture quality (negative sharper, positive softer/cheaper), runtime.
- `surfaceMaxTextureSize` + context menu **Surface Texture/Apply Max Texture Size**: sets the import max size of the assigned textures (only ones under `Assets/_ASH`).
Globals: `_ASH_AshNrm`, `_ASH_SnowNrm`, `_ASH_DetailTex`, `_ASH_DetailNrm`, `_ASH_SurfTex` (normal strength, detail tiling, detail albedo, detail normal), `_ASH_SurfTex2` (mip bias, normals on, detail on). They share the ash sampler (the terrain pass is near the sampler limit). Runtime API: set the fields, then `PushSurfaceTexture()` (also pushed every frame by `PushGlobals`).
### Foot IK (`ASHFootIK`, on the player Animator)
Needs IK Pass on base layer: `ASH/Player/Setup Foot IK + Ash Deformer` copies the Invector controller to
`Assets/_ASH/Animation/ASH_BasicLocomotion.controller` (IK Pass on; vendor asset untouched) and adds ASHFootIK +
ASHDeformer to `ASH_Player.prefab`. Per-foot raycast to the ground (under the ash), pelvis drops to the lower foot, feet
rotate to the slope normal. Weights: animator floats `LeftFootIK` / `RightFootIK` if present, else a stance heuristic.
Tunables: ray lengths, max pelvis drop/raise, foot offset, smoothing, max foot angle; Subtle / Default / Extreme presets.

### Build notes
- The hidden shaders are referenced by the controller's `surfaceShader` / `deformShader` fields (Shader.Find fallback);
  assign them or add both to *Always Included Shaders* before a player build.
- Terrain LOD: the surface samples the full-res heightmap; at distance the terrain's own LOD may differ slightly, hidden
  by the edge fade.

## Vendor rocks (ANGRY MESH + Quantum Theory)
All rock / cliff / boulder materials in both vendor packs use `ASH/Accumulation Lit` (the same object shader, URP Lit property set), so ash over snow settles on upward-facing surfaces by slope, driven by the global controller. No per-object component is needed. Maps preserved: albedo, normal, AO, detail albedo/normal (ANGRY MESH; the vendor's own moss/snow/sand top layer is replaced by ASH accumulation; Sand variants keep their red base); albedo + normal (Quantum Theory, matte smoothness from the legacy shininess). Quantum Theory FBX files are remapped to their `Meshes/Materials/*.mat` (they used read-only embedded materials before). Non-rock assets (grass, ferns, roots, particles, skyboxes, terrain templates) stay plain URP. ANGRY MESH terrain-detail meshes (`Detail_Rocks`, `Detail_Low_Rocks`) stay `Unlit/Texture` for the terrain detail system.
Tool: `ASH/Vendor/ANGRY MESH/Rocks -> ASH Accumulation`, `ASH/Vendor/Quantum Theory/*`. Logs: `Docs/Vendor_AngryMesh_Materials.md`, `Docs/Vendor_QuantumTheory_Materials.md`.

## Still missing
1. **Speed and footsteps** read `GetDepthMetres` + terrain slope (phase 3).
2. **Snow on top of ash** (fresh snowfall after ash) is simplified: ash always draws on top.
3. Walkable structure tops (porch, roofs) do not deform yet.
4. Weather director: should call `SetWeatherInputs` (or implement `IASHWeatherInputs`) and nothing else.

## Main paths
Drift mask **A** = main-path factor (1 on the painted road/trail surface, falling to 0 over `pathFalloffMetres` beyond
`roadHalfWidth`/`trailHalfWidth` + `pathCorridorExtra`, from the vegetation settings). At runtime the controller applies
`pathDepthReduction` (depth: shader *and* `GetDepthMetres`, so speed/deformation see shallower paths) and
`pathCoverageReduction` (visual coverage) via `_ASH_TexNorm.yz`. Width/falloff are baked: re-run *Generate Drift Mask*.

## Path markers
`ASH/World/Place Path Markers` / `Clear Path Markers` (`Editor/World/ASHPathMarkers.cs`) place swappable prefabs from
`Assets/_ASH/Data/World/ASH_PathMarkerSettings.asset` (prefab list with Random/Sequential/PerRoute pick, spacing + jitter,
road/trail edge offsets, sides, random yaw/tilt/scale, tree clearance, max slope, collider avoidance, per-route overrides)
under one `ASH_PathMarkers` parent. Placeholder: `Assets/_ASH/Prefabs/World/ASH_PathMarker_Post.prefab` (wood stake on
URP/Lit takes ash lightly; orange ribbon on Simple Lit stays visible). Markers get `ASHAccumulation` when `accumulate` is on.
