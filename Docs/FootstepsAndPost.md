# Footsteps, puffs, track shadows and post stack

## Footsteps (`ASHFootsteps` on ASH_Player)
- Step detection: Humanoid foot bones (sole height over ground, lift/plant hysteresis, min speed, min interval). Works with root motion and foot IK, no animation events needed. The Invector `vFootStep` on the prefab had no foot triggers (it never played), so it is **disabled**; `ASHFootsteps` replaces it.
- Mapping: `Assets/_ASH/Settings/ASH_FootstepSurfaceMap.asset` (ScriptableObject, swappable; menu Create > ASH > Footstep Surface Map).
- Resolve order:
  - **Terrain:** on a main road/trail (drift mask A >= `roadPathMin`) with less than `roadSnowDepth` -> `roadSurface` (Gravel), no puffs. Ash/snow >= `snowDepthMin` (3 cm) -> `terrainDefault` (**Snow**). Otherwise the dominant terrain layer rule (Soil -> Dirt, Rock -> Stone, CreekGravel -> Gravel; grass/ash layers -> Snow).
  - **Meshes:** physic material name -> tag -> layer -> object/parent/material name keywords (cabin/plank/deck/log... -> Wood, rock/stone/cliff... -> Stone, metal/car... -> Metal) -> `meshDefault` (Stone).
- Deep ash/snow: louder (`deepVolumeBoost`), lower pitch (`deepPitchDrop`) and a gravel crunch layer (`deepLayerClips`) scaled by depth.
- Audio: Snow, Dirt, Stone (Rock), Wood copied from Genesis `Audio/Foot Steps` into `Assets/_ASH/Audio/Footsteps/`; Gravel and Metal use the Invector clips in place.
- Runtime API: `Step(pos, speed01)`, `Resolve(pos, out depth, out onTerrain, out onRoad)`.

## Footstep puffs (`ASHFootstepPuffs` on ASH_Player)
One pooled world-space particle system, `Emit()` per step. Soft fluffy puff (`Art/FX/ASH_SoftPuff.png`, `ASH_FootstepPuff.mat`, URP Particles Simple Lit, no shadows). Color is a light gray-white mix from the controller's local ash vs snow layers (`GetLayers`). Particles hang for 2.2-3.4 s, then settle (gravity `settle`) and sink below the surface while fading. Count and size scale with local depth (`GetDepthMetres`) and speed. None below `minDepth` (bare ground) or on roads. Quality presets: Off, Low, Medium (default), High, Extreme (`ApplyQuality`).

## Tracks: no shadow casting, AO instead
`ASHAccumulationSurface` (the raised, deformable ash mesh) renders with `ShadowCastingMode.Off`, receive on, and has DepthOnly + DepthNormals passes, so URP SSAO sees the tracks. The PC renderer SSAO feature (`Assets/Settings/PC_Renderer.asset`) is set to source Depth Normals, intensity 0.8 (was 0.4), radius 0.3, direct lighting strength 0.25.

## Post stack (`ASHPostProcessing` on ASH_Player)
At runtime it creates a global Volume (priority 10, above the scene's SampleSceneProfile volume) and turns post on for the main camera. Nothing is saved into the scene. Profiles: `Assets/_ASH/Settings/ASH_PostProfile.asset` (PC) and `ASH_PostProfile_Mobile.asset` (minimal). Translated from Genesis `BOTD Post Processing` (HDRP):

| Effect | ASH (URP) | Genesis (HDRP) |
|---|---|---|
| Tonemapping | ACES | External LUT mode |
| Color Adjustments | contrast +8, saturation -8 | (color curves, empty) |
| White Balance | temperature -15 | -39 (HDRP scale; toned down) |
| Bloom | threshold 1.0, intensity 0.35, scatter 0.66, warm tint, HQ filtering | intensity 0.42, scatter 0.66, warm tint, lens dirt |
| Vignette | 0.2, smoothness 0.4, black | 0.2, gold tint |
| Film Grain | Thin1 0.15 | - |
| Depth of Field | Off (Gaussian far 40-200 m ready) | inactive |
| Motion Blur | camera only, low, 0.15 | low |
| Lift Gamma Gain / Shadows Midtones Highlights | slight cool shadows | - |
| Chromatic Aberration | 0.03 | 0.01-ish |
| Ambient Occlusion | SSAO renderer feature | HDRP AO 1.17 / r 0.25 |

HDRP-only in Genesis, with no URP equivalent (not carried over): Exposure (auto/physical), Indirect Lighting Controller, Contact Shadows, Screen Space Reflection, Fog/Volumetric Fog, Physically Based Sky, SSGI (Global Illumination), Water Rendering. Bloom lens dirt was left off. Panini Projection was inactive in Genesis.

Menu: **ASH/Player/Setup Footsteps + Puffs + Post Stack** (idempotent). Test: `ASHFootstepPostTools.PlayBatchCheck`; the harness holds player input still only while the editor is unfocused.
