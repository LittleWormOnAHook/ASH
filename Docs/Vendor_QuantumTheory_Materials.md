# Quantum Theory (Better Rocks and Cliffs): URP material fix-up

Vendor pack stays out of git (`/Assets/Quantum Theory/` in .gitignore); this log is ours. Tool: `Assets/_ASH/Editor/Vendor/ASHVendorMaterialTools.cs`.
Methods: `urp-template` = vendor's own URP template package; `ash-accumulation` = rock material moved to `ASH/Accumulation Lit` (URP Lit property set + slope-based ash over snow driven by `ASH_Accumulation`), maps preserved; `remap` = ASH remap to URP/Lit / URP Terrain / URP Particles; `unchanged` = already URP-compatible. Rocks get ASH accumulation; non-rock assets are plain URP for now.

```csv
path,old shader,old status,new shader,new status,method,notes
"Better Rocks and Cliffs/Meshes/Materials/RockConcaveA.mat","Legacy Shaders/Bumped Specular",BUILTIN,"ASH/Accumulation Lit",OK,ash-accumulation,"albedo + normal kept; smoothness from legacy shininess (matte); _SpecTex legacy spec map unused"
"Better Rocks and Cliffs/Meshes/Materials/RockConcaveB.mat","Legacy Shaders/Bumped Specular",BUILTIN,"ASH/Accumulation Lit",OK,ash-accumulation,"albedo + normal kept; smoothness from legacy shininess (matte); _SpecTex legacy spec map unused"
"Better Rocks and Cliffs/Meshes/Materials/RockCubicA.mat","Legacy Shaders/Bumped Specular",BUILTIN,"ASH/Accumulation Lit",OK,ash-accumulation,"albedo + normal kept; smoothness from legacy shininess (matte); _SpecTex legacy spec map unused"
"Better Rocks and Cliffs/Meshes/Materials/RockLarge.mat","Legacy Shaders/Bumped Specular",BUILTIN,"ASH/Accumulation Lit",OK,ash-accumulation,"albedo + normal kept; smoothness from legacy shininess (matte); _SpecTex legacy spec map unused"
"Better Rocks and Cliffs/Meshes/Materials/RockLargeB.mat","Legacy Shaders/Bumped Specular",BUILTIN,"ASH/Accumulation Lit",OK,ash-accumulation,"albedo + normal kept; smoothness from legacy shininess (matte); _SpecTex legacy spec map unused"
"Better Rocks and Cliffs/Meshes/Materials/RockWide.mat","Legacy Shaders/Bumped Specular",BUILTIN,"ASH/Accumulation Lit",OK,ash-accumulation,"albedo + normal kept; smoothness from legacy shininess (matte); _SpecTex legacy spec map unused"
"Better Rocks and Cliffs/Textures/Extras/TerrainMat.mat","Nature/Terrain/Specular",BUILTIN,"Universal Render Pipeline/Terrain/Lit",OK,remap,"terrain material template -> URP Terrain/Lit (splats come from TerrainLayers)"
```

Result: 7 OK, 0 not URP-compatible.

## Notes
- Unity had already given the six legacy rock materials an in-memory URP/Lit upgrade (smoothness 0); ASH moved them to `ASH/Accumulation Lit` with matte smoothness from the old shininess.
- The FBX meshes had read-only embedded materials (`materialLocation: InPrefab`), so the `.mat` files were unused. Each FBX .meta now remaps its embedded material to the `Meshes/Materials` .mat of the same name.
- Pack has no prefabs: 6 FBX meshes (RockConcaveA/B, RockCubicA, RockLarge, RockLargeB, RockWide) to use for large boulders, cliffs and combos in the rock tool.
- Backup of the original .mat files: `C:\Users\Teabagger\ash_xfer\backup_qt_mats`.
