# ASH Accumulation System (ash over snow)

**Single source of truth** for surface accumulation and (from phase 2) deformation. Weather systems
(e.g. a future `ASHWeatherDirector`) only **feed inputs**; they must not add a second accumulation shader,
duplicate deformation logic, or write the global shader properties below themselves.

Status: **Phase 1 (buildup shader) built.** Phase 2 (deformable trails), phase 3 (speed/footsteps) not built.

## Layer model
One layered system: **ash on top**, **snow underneath**, base surface (terrain/material) below.
Layer state lives on `ASHAccumulationController`: `ashAmount`, `snowAmount` (relative depth 0..1).
Local depth = layer amount x drift mask x coverage. Drift mask R: 0.35 ≈ ankle-deep, 1.0 = knee-deep
drift (`maxDepthMetres`, default 0.5 m). Future trails remove ash first, then snow, then expose ground.

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

## Missing integration points (phase 2+)
1. **Deformation**: a top-down render-texture "stamp" pass around the player writing `_ASH_DeformTex`
   (R = fraction pushed aside), with `_ASH_DeformRect` set; the shader core already reads it (`ASH_DeformAt`).
   Trails must persist through weather changes and **refill** at
   `deformationRefill × accumulationRate × (ashfall + snowfall)` per minute (hook marked in `Simulate`).
   `Deform()` / `GetDeformation()` become real; `GetDepthMetres` already subtracts deformation.
2. **Real displacement** (tessellation/near-player mesh) for ankle/knee depth: not built; shading only today.
3. **Speed and footsteps** read `GetDepthMetres` + terrain slope (phase 3).
4. **Snow on top of ash** (fresh snowfall after ash) is simplified: ash always draws on top.
5. Weather director: should call `SetWeatherInputs` (or implement `IASHWeatherInputs`) and nothing else.

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
