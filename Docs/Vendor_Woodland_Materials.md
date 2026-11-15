# Woodland: URP + ASH accumulation material conversion

Vendor pack stays out of git (`/Assets/Woodland/` in `.gitignore`); this log is ours. No bundled URP template. Backup of the original `.mat` files: `C:\Users\Teabagger\ash_xfer\backup_woodland_mats` (40 materials + metas).

There are no rock/cliff/stone materials in this pack (plants, trees, a demo terrain plane).

## Before (40 materials in `Assets/Woodland/Materials`)

| Count | Old shader | Status |
|------:|------------|--------|
| 27 | Custom/TwoSide (Built-in, two-sided alpha cutout, no wind) | BUILTIN (pink in URP) |
| 12 | Universal Render Pipeline/Lit | OK (tree trunks, bush bark, demo terrain plane; already URP) |
| 1 | ASH/Accumulation Lit | OK (`bush.mat`, already on the ASH shader) |

`Custom/TwoSide` is the pack's only shader (`Assets/Woodland/shader/TwoSide.shader`): two-pass Built-in BlinnPhong cutout, no vertex wind.

## After

**40 / 40 OK. Zero pink or unsupported shaders.**

| Count | New shader | Method |
|------:|------------|--------|
| 26 | ASH/Accumulation Lit | Foliage mode (snow-on-top): `_ClearCoatMask` 1, `_ClearCoatSmoothness` = estimated plant height in metres, alpha clip preserved from the source cutoff, `_Cull` 0 (two-sided). Branches, bush, flowers, grass, palms, plants, sunflower, group 1. |
| 11 | ASH/Accumulation Lit | Slope-based accumulation, no foliage mode: 10 tree trunk materials (`tree1`–`tree10 diffuse`) and `bushtrunk bark`. Maps kept (albedo, normal, occlusion, detail). |
| 2 | ASH/Accumulation Lit | Slope-based accumulation (no foliage mode) with alpha clip + two-sided: `mush diffuse`, `trametes` (3D fungi, snow on caps). |
| 1 | Universal Render Pipeline/Lit | Left as URP/Lit: `Plane Terrain` (demo ground plane). Cleared a leftover `_EMISSION` keyword; enabled `_NORMALMAP` (it has a bump map). |

Foliage heights used for the snow-on-top ramp (adjust `_ClearCoatSmoothness` on the material if a plant looks under- or over-covered): grass 0.6 m, flowers/sunflower 0.5 m, plants 0.8 m, group 1 1 m, bush 1.2 m, palms 3 m, live branches 6 m (tree canopy).

Smoothness for former TwoSide mats is a low remapping of the old BlinnPhong `_Shininess` (0.08–0.35) so leaves stay matte. Colors, cutoffs, albedo and normals were preserved. 94 of 95 pack prefabs already referenced these `.mat` files, so they pick up the new shaders without an FBX reimport.

## Issues / needs a look

- **No source wind to keep.** TwoSide has no wind, and `ASH/Accumulation Lit` has none either. Vertex wind is planned with the weather director, same as the ANGRY MESH plants.
- **Foliage heights are estimates.** If snow sits too low or too high on a given prefab, tweak `_ClearCoatSmoothness` (height in metres above the object pivot).
- **`sunflower.prefab`** does not reference `Materials/sunflower.mat`. It points at a missing material GUID (`8f95b713…`), so that prefab may still be pink until the slot is assigned to `sunflower.mat`.
- **`bush.mat`** was already on ASH/Accumulation Lit (likely a partial earlier pass). Color was restored from the backup, a leftover `_SURFACE_TYPE_TRANSPARENT` keyword was removed, and foliage mode was applied.
- Most leaf/grass cards never had a normal map (bump slot was empty). The five `branches live diffuse` materials did, and now have `_NORMALMAP` enabled.
- Tree trunk smoothness was already 0 in the source URP materials; left as-is (very matte bark).

## Useful asset types

95 prefabs under `Prefab/Plants` and `Prefab/Tree`, plus 165 FBX meshes: bushes, flowers, grasses, palms, leafy plants, sunflowers, mushrooms/trametes, and 10 tree types with live-branch leaf cards. A mesh-paint utility (`PaintObject` under the Window menu) and a demo scene are also in the pack.
