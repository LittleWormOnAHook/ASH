# ASH — Icon Art Style Guide and Preservation Record

Status: **Approved for now** (2026-11-20). Do not redesign or replace the installed icons without an explicit request.

## Visual reference / source of truth
The **existing inventory icons installed in the Unity project**, especially the user's Windows Explorer screenshot of the icon collection, are the primary style reference. Preserve their established appearance rather than inventing a new art direction. The GPS and Grandpa's Journal item icons are important visual anchors.

### Inventory, resource, equipment, and crafting icons
- Realistic, detailed, colored game-item renders, subtly weathered and worn; legible at small UI sizes.
- One recognizable object (or a meaningful small grouping) per icon, centered, fully visible, with comfortable padding.
- Transparent alpha PNG with **no parchment, solid backdrop, grid/checkerboard, frame, title, or labels**.
- Target 256 x 256 pixels, RGBA, consistent scale and lighting.
- **Fuel cans and gas cans are red** (weathered red containers).
- Keep good existing artwork. Correct wrong mappings by reusing existing suitable images first; draw only missing items in the same style.
- Every sprite must depict the object named by its filename. Do not substitute unrelated images, even if they look good.
- Recipes should depict the **finished craftable item**, not an unrelated ingredient. Recipe variants yielding the same product may reuse that product image intentionally.
- Maintain current Unity sprite filenames and preserve .meta files / GUIDs when replacing PNG contents; do not blindly rename assigned assets.

### Distinct page-art style (not inventory icon style)
- **12 Field Manual illustrations**: full-page 512 x 512 PNGs, aged parchment, warm sepia pencil/ink cross-hatching, wintry survival scenes, no text.
- **8 Grandpa's Journal illustrations**: separate full-page 512 x 512 PNGs, same parchment/ink treatment but more personal, route clues, arrows, circles, place sketches; no legible text.
- Keep these separate from transparent inventory/recipe icons.

## Known Unity locations
- Inventory icons: `Assets/_ASH/Art/Icons/`
- Additional previously used inventory/recipe collections: `Assets/_ASH/Art/Icons Inventory/Inventory/`, `Assets/_ASH/Art/Icons Inventory/Recipes/`
- Field Manual: `Assets/_ASH/Art/FieldManual/` (user also referenced `Assets/_ASH/Art/Icons Misc` for existing manual artwork)
- Grandpa's Journal: `Assets/_ASH/Art/GrandpasJournal/`

**Important:** Multiple icon directories exist. Before replacing sprites, check which GUIDs are actually referenced by item and recipe definitions. Avoid duplicated conflicting copies.

## Previous art deliveries (conversation history)
- Inventory/recipe reconciliation: ASH_Icons_Corrected_Pass_07.zip; earlier ASH_Icons_Reconciled_Pass_06.zip.
- Field Manual: ASH_FieldManual_12_Pages.zip (12 numbered pages).
- Grandpa's Journal: ASH_GrandpasJournal_8_Pages.zip (8 numbered pages).
These are *historical delivery names*, not repository paths or proof that ZIPs are committed. The user has imported artwork into Unity. Installed Unity assets are the authoritative working copies.

## Reuse / future additions checklist
1. Inspect current Unity sprites and their filenames, assignments and .meta GUIDs.
2. Reuse existing correct art first.
3. For a new icon, match the installed realistic weathered 3D-item style and transparent-alpha format.
4. Verify object identity against filename and actual item/recipe definition.
5. Inspect at 64 px and 128 px, check alpha edges, cropping, and background artifacts.
6. Preserve the red fuel-can convention.
7. Do not modify Field Manual or Grandpa's Journal parchment art to look like inventory icons.
8. Keep this guide as the stable reference for future ASH artwork additions.

This document records style and integration decisions. It does not claim all historical icon mismatches were verified or that any PNG artwork was pushed by this commit.
