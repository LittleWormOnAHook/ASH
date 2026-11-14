# ASH Prototype Status

_Last updated 2026-11-14 (ET) by the ASH implementation agent. Unity 6000.4.11f1, URP._

## Open and run
1. Open `A:\ASH` in Unity 6000.4.11f1.
2. Open `Assets/_ASH/Scenes/ASH_Prototype.unity` (Anthony's scene, moved here with its GUID kept).
3. Press Play. The player starts at **Grandpa Cabin**.
4. Controller test scene: `Assets/_ASH/Scenes/Tests/ASH_ControllerTest.unity` (flat ground, 20° ramp, steps, crouch bar, resource nodes, water tank).

Automated check: in Play Mode, run `ASH.DebugTools.ASHPlayTest.Run` (through the Editor bridge `exec`). It logs `[ASH] TEST PASS/FAIL ...` and a `TEST SUMMARY`. Last result: **19 pass / 0 fail** in ASH_Prototype (2026-11-14, 1:55 AM ET).

### Controls (keyboard/mouse; gamepad in brackets)
| Action | Binding |
|---|---|
| Move / Look | WASD or arrows, mouse [left stick / right stick] |
| Sprint (hold) / Jump | Left Shift [L3] / Space [A] |
| Crouch toggle / Walk toggle | Left Ctrl or C [R3] / Caps Lock |
| Interact (hold to gather) | E [X] |
| Pack (inventory) / Craft | Tab or I [Y] / Q [D-pad up] |
| Hotbar use | 1–6 |
| Place: confirm / cancel / rotate | LMB or E / RMB or Esc / mouse wheel |
| Pause | Esc [Start] |

In the pack: LMB uses a slot (eat food, place the campfire), RMB splits a stack, drag moves or merges stacks. The top row is the always-visible hotbar.

## Vertical slice loop
1. Spawn at Grandpa Cabin.
2. Gather 4 deadwood, 3 stones and 2 chokecherries. Resource nodes are 8–35 m from the cabin, outside a 6 m cabin clearance.
3. Craft a campfire (Q).
4. Climb to the ridge overlook. It was chosen automatically from the cabin: **701 m north, 127 m climb**, maximum straight-line slope 34°. The objective shows direction and distance, with a smoke column at the campsite as a visual cue.
5. Place the campfire within 30 m of the old campsite (12 m from the overlook) to finish: "Camp made before dark."

Needs (hunger, thirst, stamina) decay over time. Eat chokecherries, and drink at the stock tank (on flat ground, 35 m or more from the start). If health reaches 0, the player respawns at `ASH_PlayerSpawn`, which sits at the cabin start.

## Systems and Genesis reuse (REUSE-FIRST)
The Genesis source (`A:\Dark Matter Genesis`) was **read only and never modified**. Each port is a lite rewrite inside `Assets/_ASH`, tested by ASHPlayTest.

| ASH system | Genesis source | Dependencies | Changes |
|---|---|---|---|
| `ASHInventory` (12 slots, first 6 are the hotbar) | `Assets/_Project/Scripts/Inventory/InventorySystem.cs` | — | Kept two-pass add (stack, then empty slot) and remove across stacks. Added Move/merge, Split and lazy slot creation. Removed equipment, ammo, storage unlocks and level gates. |
| `ASHItemDefinition` (ScriptableObject) | `Assets/_Project/Scripts/Data/ItemData.cs` | — | Kept identity, icon, stack size, category, restores and placeable data. Removed combat, XP and progression. |
| `ASHRecipe` / `ASHCrafting` | `Assets/_Project/Scripts/Crafting/RecipeDefinition.cs`, `CraftingManager.cs` | ASHInventory | Kept the CanCraft/HasIngredients/TryCraft flow and timed crafts. Removed stations and unlocks. |
| `ASHSurvivalStats` + `ASHSurvivalTuning` | `Assets/_Project/Scripts/Survival/SurvivalStats.cs` | Invector (stamina via link) | Health, stamina, hunger and thirst only. Tuning moved into a ScriptableObject with presets. |
| `ASHInteractor` / `IASHUsable` | `Assets/_Project/Scripts/Interaction/WorldUseController.cs`, `IWorldUsable.cs` | ASHInput | Camera-centre ray with sphere assist, 3 m reach, debounce, hold progress. |
| `ASHResourceNode`, `ASHItemPickup`, `ASHWaterSource` | `Assets/_Project/Scripts/Interaction/ResourceNode.cs`, `ItemPickup.cs` | ASHInventory, ASHSurvivalStats | Hold-to-gather, finite yield. No loot tables or XP. |
| `ASHPlayerDeath` | `Assets/_Project/Scripts/Player/PlayerDeathHandler.cs` | ASHSurvivalStats | Fade and respawn only. No ragdoll, loot or autosave. |
| `ASHDayNightClock` | `Assets/_Project/Scripts/World/Time/DMIoClock.cs` | URP | One sun and moon, gradients, an "ash haze" tint into fog and ambient. Presets: Default (48 min day), Frozen golden hour, Extreme (1 min day). |
| `ASHInvectorInput` / `ASHInvectorStaminaLink` / player tuning | `Assets/_Project/Scripts/Player/Invector/PioneerInvectorInputBridge.cs`, `PlayerInvectorRuntimeSetup.cs` | Invector Basic Locomotion 2.5.2 (unmodified) | A thin subclass of vThirdPersonInput on the Input System. Genesis speeds: walk 3, run 4, sprint 6, crouch 2. |
| `ASHInput` + `ASH_InputActions.inputactions` | `Assets/_Project/Settings/Input/InputSystem_Actions.inputactions` (Player map) | Input System | Bindings reused where actions overlap. Added Interact, Pack, Craft, hotbar keys and test overrides. |
| `ASHUIState` | Genesis player input-blocking/cursor flags | — | Reduced to a panel token set. |
| `ASHHud` | Genesis HUD layout (bars bottom-left, objective top-left, prompt under the reticle) | uGUI (legacy Text) | Built in code. Stat bars, prompt and hold bar, objective, clock, toasts, **hotbar and pack slot grid with item icons, stack counts, tooltips, drag/merge/split**, craft panel, pause and death screens. |
| New (no Genesis equivalent): `ASHPlacer`, `ASHCampfire`, `ASHObjectiveTracker`, `ASHAshFall`, `ASHIconBaker`, scene setup and painter tools | — | URP | See below. |

## World
- **Terrain provenance: a procedural fallback heightmap. It is not real DEM data.** It is 4097² 16-bit little-endian RAW, imported with no flip, at 4000 × 1400 × 4000 m. Anthony built the terrain and imported the heights himself; the ASH tools never regenerate them.
  - Fixed: the terrain height scale was 1000 m and was set to 1400 m to match the brief. Only the scale changed; the heights were untouched.
  - Orientation: RAW row r maps to world Z = r × 0.9765625 (north is +Z).
  - The terrain data asset moved to `Assets/_ASH/Art/Terrain/ASH_Master_TerrainData.asset` (GUID kept).
- **Start:** Grandpa Cabin, Anthony's placement, at about (522, 306, 1560). The player and cabin are never moved by tools.
  - Note: an earlier ASH rebuild (1:35 AM ET) had reset the player to the old default spawn. The setup restored it to just outside the cabin, at (511.5, 304.6, 1565.4). **Anthony: nudge the player to the door if you prefer a different spot.**
- **Scene setup:** `ASH/Build/3. Set Up Prototype Scene` rebuilds only `ASH_World` and `ASH_Systems`.
  - It derives the spawn, resources, overlook, campsite, route breadcrumbs and grass from the cabin start.
  - It disables the root "Main Camera" in favour of the player camera.
- **Painting:** `ASH/World/Paint Active Terrain` uses six layers. These are tinted copies of Gaia sample textures (URP terrain ignores the TerrainLayer tint): prairie grass, dry grass, soil, rock, creek gravel and **ash** (desaturated, darkened Snow_1, in hollows and heavier to the west, plus a campsite scar). Anthony's `ASH/Terrain Auto Painter` is untouched and can use the same layers.
- **Sky and atmosphere:** procedural skybox (hazy), exponential-squared fog at 0.0016, scene time 17:12.
- **Falling ash:** `Assets/_ASH/Prefabs/FX/ASH_AshFall.prefab` (`ASHAshFall`), built on URP particles because Gaia ships no snow FX. It uses small grey and charcoal flakes, slow drift, flutter, rare embers, and follows the camera.
  - Presets: Light, Default, Heavy, Extreme (20k/s, 60 m radius). All settings are exposed with wide ranges.
- **Item icons:** `ASH/Build/5. Bake Item Icons` renders each item's prefab to a transparent 256 px sprite in `Assets/_ASH/Art/Icons` (difference matting, auto exposure).

## Project setup notes
- **Input:** Active Input Handling is set to **Both** (needed by Invector). 19 Invector axes were added to the InputManager.
- **Layers:** Gaia owns layers 8–11. ASH uses 6 Resource, 7 Item, 12 StopMove, **13 Player**, 14 HeadTrack, 15 BodyPart, 16 Enemy, 17 Triggers and 23 Climbable.
  - Invector shows a dismissable Scene-view banner because it expects Player on layer 8. **Do not run "Invector/Import ProjectSettings"**; it would overwrite Gaia's layers.
- **Tags:** Enemy, CompanionAI and Weapon were added (Invector looks up "Enemy").
- **Vendor folders stay out of git** (Procedural Worlds/Gaia, Invector and Gaia User Data). They are tracked in Unity Version Control.
- **Invector compile fix:** stock files restored from `A:\Survival Pioneer - Copy`. See `Docs/GenesisReuseAudit.md`.
- **Left uncommitted on purpose:** `Assets/Settings/PC_RPAsset.asset` and `Mobile_RPAsset.asset` (URP auto-upgrade), `ProjectSettings/URPProjectSettings.asset`, `ProjectSettings/SceneTemplateSettings.json`, and `Assets/Scenes/SampleScene.unity`.
- **Terrain data is not committed yet.** At 76 MB (heights, splats and grass detail) it is under GitHub's 100 MB limit, but it will grow. **Decision for Anthony:** track `*.asset` terrain data with Git LFS (recommended), or commit it to git directly. Until then, the asset lives in the working copy and Unity Version Control.

## Known issues
- The Gaia warning "PC_High_Renderer ... missing RendererFeatures" comes from a legacy vendor renderer asset. It is harmless and was left alone (vendor code is not edited).
- Gaia's own obsolete-API warnings are vendor code and were left alone.
- Placeholder art: the deadwood, berry bush, campfire and campsite props are primitives plus the PW_Stone_01 mesh, waiting for vendor art.
- The overlook route has short 30–34° stretches, which Invector climbs (slope limit 75°). A proper trail could be painted later.
- NavMesh is deferred until AI exists.

## Next tasks
1. **Reactive ash ground:** trails and footprints where the player walks through the ash layer (render-texture trail mask feeding a terrain/decal shader, or a displacement/voxel approach). Planned, not started.
2. Inventory polish: drop or discard items to the world, gamepad slot navigation, more items and recipes (water container, torch).
3. Vendor art pass: animal and human vendor packs (Anthony), real deadwood, berry and campfire meshes, and cabin interior dressing.
4. Terrain data in Git LFS (pending Anthony's decision), then commit it.
5. Audio: wind, fire crackle, footsteps on ash.
