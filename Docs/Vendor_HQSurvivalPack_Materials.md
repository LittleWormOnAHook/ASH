# HQ Survival Pack: URP material conversion

Vendor pack stays out of git (`/Assets/HQ Survival Pack/` in `.gitignore`); this log is ours. No bundled URP/HDRP template ships with the pack (only `Readme.pdf`), so nothing was imported. ASH accumulation was not applied (and was stripped from 8 materials that already had it). Pickups and props stay clean.

Backup of the original `.mat` files: `C:\Users\Teabagger\ash_xfer\backup_hqsurvival_mats` (166 materials + metas).

## Before (166 materials)

| Count | Old shader | Status |
|------:|------------|--------|
| 141 | Standard (metallic workflow) | BUILTIN (pink in URP) |
| 8 | ASH/Accumulation Lit | OK, but not wanted on this pack |
| 4 | Nature/Tree Creator Leaves Fast | BUILTIN |
| 2 | Nature/Tree Creator Bark | BUILTIN |
| 2 | CTI/LOD Bark Array 301 | BUILTIN |
| 2 | HQSurvival/PBR-Metallic (With texture mask) | BUILTIN |
| 1 | CTI/LOD Bark 301 | BUILTIN |
| 1 | CTI/LOD Billboard 301 | BUILTIN |
| 1 | CTI/LOD Leaves 301 | ERROR (shader `LOD_FADE_CROSSFADE` keyword diagnostic) |
| 1 | Hidden/InternalErrorShader | MISSING (`CTI_default_billboard_material` referenced a shader GUID that is not in the pack) |
| 1 | Legacy Shaders/Particles/Additive | BUILTIN |
| 1 | Skybox/Cubemap | OK (works in URP as-is) |
| 1 | Universal Render Pipeline/Lit | OK (`Campfire_Ash`) |

Custom shaders in the pack, none of them URP: `HQSurvival/PBR-Metallic (With texture mask)`, `Hidden/TerrainEngine/Details/WavingDoublePass` (`WavingGrass.shader`, unused by any `.mat`; Built-in terrain-detail shader — URP terrain uses its own), plus the CTI Built-in LOD bark/leaves/billboard shaders and Built-in deferred helpers.

## After

**166 / 166 OK. Zero pink or unsupported shaders in the pack.**

| Count | New shader | Method |
|------:|------------|--------|
| 141 | Universal Render Pipeline/Lit | Unity `StandardUpgrader` (official Built-in → URP). Albedo/color, normal, metallic-smoothness maps (R metal, A smoothness), occlusion, emission, alpha clip / fade / premul, and tiling preserved. |
| 8 | Universal Render Pipeline/Lit | Remap: stripped `ASH/Accumulation Lit` (building logs/planks, stone wall, campfire branches/rocks). Same URP Lit property set kept. `BuildingPlanks03` and `Branches_Dead` had their original cutout mode restored. |
| 4 | Universal Render Pipeline/Lit | Remap: Nature/Tree Creator Leaves Fast → URP/Lit, alpha clip + two-sided, grass tints kept. Tree Creator leaf shading dropped. |
| 2 | Universal Render Pipeline/Lit | Remap: Nature/Tree Creator Bark → URP/Lit, normals kept. Soft tree-bark shading dropped. |
| 6 | Universal Render Pipeline/Lit | Remap: CTI bark / leaves / billboards (including the missing default billboard shader). 2D `_MainTex` used as albedo. Wind, translucency, billboard camera-facing, and Texture2DArray bark layers dropped. Leaves/billboards are alpha-clip + two-sided. |
| 2 | Universal Render Pipeline/Lit | Remap: custom `HQSurvival/PBR-Metallic (With texture mask)` (`Log`, `LogPile_Cloth`). |
| 1 | Universal Render Pipeline/Particles/Unlit | Remap: `Particle2D` (additive). |
| 1 | Skybox/Cubemap | Unchanged. |
| 1 | Universal Render Pipeline/Lit | Unchanged (`Campfire_Ash`). |

## Issues / not a clean 1:1

- **CTI conifers are display-only now.** The six CTI materials render in URP, but they no longer wave, billboard, or use the packed bump/spec/AO and bark texture arrays. A real CTI URP shader pack would be needed to restore that. `_MainTex` 2D fallbacks were present and are used.
- **`LogPile_Cloth` texture-mask tint dropped.** The custom shader blended a `MaskTex` with `_MaskedTint`. URP/Lit has no equivalent. Base color was restored from the original `_Color` (dark cloth). Its packed MRO map (`_Metallic` RGB = metal / smoothness / AO) was not assigned: that packing does not match URP's MetallicGlossMap (R metal, A smoothness). Metallic/smoothness are scalars.
- **`Log.mat` is in better shape.** It already had a standard `MetallicGlossMap` + occlusion + normal in the YAML; those maps and keywords are on URP/Lit. The custom MRO texture is unused (same packing mismatch). `MaskTex` was empty, so no mask feature was lost.
- **CTI Leaves shader still errors in the pack** (`LOD_FADE_CROSSFADE` duplicated). The material no longer uses that shader.
- **FBX remaps:** 180 of 181 FBXs already use `materialLocation: InPrefab` with `externalObjects` pointing at the pack `.mat` files, so converting the `.mat` files updates the meshes/prefabs. 3 collider-only FBXs have no materials (`BuildingParts_Log_Colls`, `PremadeBuildings_Col`, `LogCollider`). **11 `externalObjects` remaps point at GUIDs that do not exist in the project** (broken references, may pink if those slots are used). No matching `.mat` of the same name was found, so they were left as-is (no mass FBX reimport):
  - `BuildingParts_Planks.fbx`: Pillar (Plank), Platform (Plank), Roof (Metal), Stairs (Plank), Wall (Plank), Window (Wood), Wooden Door
  - `Rock06.fbx` / `Rock07.fbx`: "No Name"
  - `BuildingPlan_World.fbx`: Building Plan_World
  - `RifleScope.fbx`: Scope Glass (a `Scope_Lense` mat exists but was not assumed to be the same)
- **`WavingGrass.shader`** was not converted (no material uses it). If the pack's demo terrain still references it as a detail shader, URP will not run that Built-in grass shader.

## Useful asset types in the pack

195 prefabs under `Prefabs/` plus 181 FBX models. Rough split:

- **Buildings:** log/plank/stone/metal building parts, premade buildings (15 FBX, prefabs in `Prefabs/Building`)
- **Decoration:** barrels, boat, crates, logs, ores, wooden WC
- **Environment:** two rock sets (13 FBX), trees, vegetation (grass/leaves/bark), HDR cubemap skybox
- **CTI_ConiferBOTD:** Conifer tree system (sources + runtime; URP Lit stand-in only, as above)
- **Equipment:** bone / hide / leather / metal / rugged cloth / wooden armor, hats, rucksack
- **Food / resources:** cans, meat, eggs, pills, soda, atlases, rope, scrap
- **Objects / stations:** campfire, furnace, workbenches, bed, tent, sleeping bag, lanterns, torches, crates, medkit, anvil, tables, drawers, lights
- **Tools:** axes, pickaxes, hatchets, hoes, sickle, hammer, fishing rod, flashlight, compass, flint & steel, torch, building plan/hammer, phone, leather bottle, etc. (38 FBX)
- **Weapons:** firearms and ranged (AKM, SMG, shotguns, rifles, pistol, revolver, bow, crossbow, launchers, flamethrower, flare gun), melee (clubs, knives, spears, mace, machete), throwables (grenades, C4, molotov)
- **Particles:** `Dust` prefab + `Particle2D` material
- **Terrain:** terrain data + layers/textures in the pack (no terrain `.mat` to convert)
- **Animations, Icons, Scripts** also included

These are mostly pickup, tool, weapon, clothing and placeable-station assets, which is why they were kept off the ASH accumulation shader.
