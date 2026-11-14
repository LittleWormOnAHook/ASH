# Genesis → ASH reuse audit (step 1 of the reuse-first workflow)

Source project: `A:\Dark Matter Genesis` (Unity 6000.4.11f1, same as ASH). Read-only; nothing in Genesis was modified.
Genesis gameplay code lives in `Assets/_Project/Scripts` (950 .cs) and `Assets/_Project/Features` (102 .cs), almost all in the
default `Assembly-CSharp` (no asmdefs). That's why the systems are tightly cross-referenced.

## Player controller + camera (priority 1)

| Item | Finding |
|---|---|
| Live player prefab | `Assets/_Project/Prefabs/Players/Player_Kade_v2.prefab` (used by `Dark Matter Genesis v1.7.unity`, `Dark Matter Mars-The Beginning.unity`) |
| Motor / camera | **Invector Third Person Controller v2.5.2** (`Assets/Invector-3rdPersonController`): `vThirdPersonController`, `vThirdPersonCamera`, `vFootStep`, `vHeadTrack`, `vRagdoll`, plus Melee/Shooter managers on the same prefab |
| Genesis glue | `Assets/_Project/Scripts/Player/Invector/*` (27 files): `PioneerInvectorBootstrap`, `PioneerInvectorInputBridge`, `PioneerShooterMeleeInput` (3,324 lines, the actual input driver, built on Invector's *Shooter* input), `PioneerPlayerInputBinder`, `PlayerController` (1,115 lines; also references ECM2, vehicles, optics, building) |
| Other layers on the prefab | Climb, Dash, Jetpack, Landing director, body-weight, grenade, weapons, progression, exposure HUD |
| Other third-party | `Assets/ECM2` (Easy Character Movement 2, legacy path), Gaia `SimpleCameraLayerCulling`, QFX VFX |
| Input | Invector reads the **legacy Input Manager** (`vInput.cs` polls `Input.GetAxis/GetKey` every frame, 72 calls in Basic Locomotion). Genesis runs **Active Input Handling = Both** (`activeInputHandler: 2`) and has Invector's axes in `ProjectSettings/InputManager.asset`. **ASH is currently Input System only (`activeInputHandler: 1`)**: Invector would throw `InvalidOperationException` every frame there. |
| Layers/tags | Invector expects a `Player` layer and others (Genesis TagManager has ~40 custom layers). |
| Character art | Kade v2 is the Genesis sci-fi lead. A grounded alternative already in Genesis: `Assets/TripoModels/rugged_male_character_3d_model` (+ `Player_rugged_male_character_3d_model_HumanoidAvatar.asset`). Invector's own demo character is the V-Bot robot (not grounded). |

**Copyable without Package Manager:** Invector is plain files under `Assets/`. Only *Basic Locomotion* is needed (Scripts 2 MB, Editor 23 MB, Animator, Animations ≈31 MB, HUD/Prefabs as needed). Melee Combat, Shooter, ItemManager and Add-ons are not needed. Basic Locomotion has one stray reference to Melee/Shooter (`vChangeInputTypeTrigger.cs`), which can be excluded.

### Options (needs Anthony's decision)
- **A, recommended: run it the way Genesis does.** Copy Invector Basic Locomotion into ASH. Set Active Input Handling to **Both** (one ProjectSettings change, needs an Editor restart). Add Invector's InputManager axes and the `Player` layer as additive ProjectSettings edits, without overwriting with Genesis files. Add a thin `ASHInvectorInput` (a subclass of `vThirdPersonInput`) that reads ASH's Input System actions and handles the UI and interaction locks. Port the non-combat parts of Genesis's camera tuning (`DMCameraProfile`, `DMCameraCollisionOverlay`), stamina sync and body-weight feel. Vendor code stays unmodified and upgradable.
- **B: Input-System-only fork.** Same copy, but patch the copied `vInput`/`GenericInput` to read the Input System. No ProjectSettings input change, but it forks vendor code.
- **C: Rebuild.** A CharacterController-based ASH controller modelled on Genesis's tuning (walk 4.4 / sprint 7.2, camera profile, collision pull/release). No vendor dependency, but it goes against reuse-first.

## Other candidate systems (coupling scan of `using` dependencies)

| System | Genesis source | Coupling | Plan |
|---|---|---|---|
| Interaction | `Scripts/Interaction/IWorldUsable.cs`, `WorldUseContext.cs`, `WorldUseController.cs` (1,658 lines) | Controller references 17 Genesis namespaces (AI, Building, Combat, Companions, Pet, Vendor, Vehicles…) plus ECM2/Invector | Reuse the `IWorldUsable`/`WorldUseContext` contract and priority/aim-assist/hold-harvest logic. Extract a slim controller |
| Resource nodes / pickups | `Scripts/Interaction/ResourceNode.cs` (hold-harvest mode), `ItemPickup.cs` | Combat, Map, UI, Quests, ECM2 | Extract hold-harvest + pickup, drop laser-mining/loot-attract VFX |
| Items | `Scripts/Data/ItemData.cs`, `ItemRegistry.cs` | Combat, Progression | Extract the ScriptableObject shape (name, icon, worldPrefab, maxStack, type, stat restores) |
| Inventory | `Scripts/Inventory/InventorySystem.cs` (1,326 lines) | Combat (ammo/equipment), Progression, Rendering | Extract slot/stack add/remove/count/consume logic, drop equipment/ammo/hotbar |
| Survival stats | `Scripts/Survival/SurvivalStats.cs` | Climb, Exposure, Progression, UI. Has health/energy/stamina/oxygen, **no hunger/thirst** | Reuse the stamina-activity, regen, low-stat health drain and notify-throttle structure. Configure hunger/thirst in place of energy/oxygen |
| Crafting | `Scripts/Crafting/RecipeDefinition.cs`, `RecipeIngredient.cs`, `CraftingManager.cs` | Progression, Quests, UI, stations | Reuse the recipe SO shape + CanCraft/TryCraft. Drop stations/levels/scroll discovery |
| HUD | `Scripts/UI/CharacterStatBarRow.cs` (TMP only), `WorldInteractionDotUI.cs` (heavily coupled), `CompassHudUI.cs` | Low / high / Map | Reuse the stat bar row widget. Rebuild a small prompt |
| Day/night | `Scripts/World/Time/DMIoClock*.cs`, `World/Sky/DMIoSkyController.cs` (1,172 lines, Io/HDRP-oriented) | Core | Reuse the clock (re-tuned to a 24 h Earth day). The sky controller is Io/HDRP-specific, so use a small URP sun/fog driver |
| Package needs | None of the above need packages ASH lacks. TextMeshPro ships with UGUI 2.0 in Unity 6. Genesis's HDRP, Cinemachine and Addressables are not needed |

## Environment
Gaia/Procedural Worlds, NatureManufacture and similar packs are installed in Genesis. They're candidates for the environment pass once the controller decision is made.

## Invector compile fix (ASH copy only, 2026-11-14)

ASH's Invector Basic Locomotion was copied from Genesis, whose copy references Genesis-only `Project.*` types (DMFootstepManager, DMFootstepProfile, GameAudioManager, DMSpawnedAudio, DustTrackLifetime, Project.Effects), which caused CS0246 errors. Minimal fix (Genesis untouched, no edits to Invector code):

- Replaced with the unmodified stock versions from `A:\Survival Pioneer - Copy\Assets\Invector-3rdPersonController` (read-only source): `FootStep/Scripts/vFootStep.cs`, `FootStep/Scripts/vAudioSurface.cs`, `Generic/Animator/vTriggerSoundByState.cs` (.meta GUIDs kept from the Genesis copy so references hold).
- Removed (not needed for the slice; Genesis-coupled in every local copy): `Generic/Triggers/vFindSpawnPoint.cs`, `Generic/Utils/vTeleport.cs`.
- Remaining `Project.` mentions (vThirdPersonCamera.cs, vInvectorIcon.cs) are comments only.
- Vendor roots (Invector, Procedural Worlds/Gaia) are excluded from git via .gitignore and tracked in Unity Version Control.
