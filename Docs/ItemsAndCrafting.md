# ASH Items and Crafting

_2026-11-14 (ET). All data are ScriptableObjects built by `ASH/Build/6. Item Catalog` (`Assets/_ASH/Editor/Build/ASHItemCatalog.cs`, table-driven and safe to re-run; GUIDs kept). Items: `Assets/_ASH/Data/Items`. Recipes: `Assets/_ASH/Data/Recipes`. Pickup prefabs: `Assets/_ASH/Prefabs/Pickups` (HQ Survival Pack visuals, see Survival Pack below; ammo stays primitive, the pack has no ammo models). Numbers are first-pass placeholders, tunable on each asset._

**Default play is on foot only.** Nothing depends on a vehicle.

## Resources
| Item | Source | Used for |
|---|---|---|
| Deadwood | Branch piles (HQ Branches01–03 wrappers; hatchet: 2× yield, half the time) | Campfire |
| Firewood | Felled dead trees → logs, downed trees, stumps (**hatchet required**); stack 20 | Campfire (firewood), Split Kindling |
| Kindling | Chips while chopping, logs/stumps/downed trees, Split Kindling; stack 30 | Campfire (firewood) |
| Stone | Stone piles | Campfire, Improvised Knife |
| Cloth | Break down Old Shirt (2, or 1 without a knife) or Rags (1); Cut Cloth Strips (3, knife); caches | Bandage, Improvised Knife |
| Plant Fiber | Wild plants: ANGRY MESH fern and grass clumps (knife bonus) | Fiber Bandage |
| Scrap Metal | Break down Empty Tin; Flatten Tins; crate bonus (50%) | Improvised Knife |
| Empty Tin Can | Left over after opening a can cleanly; drinking a tin of water | Fill at the stock tank (→ Tin of Creek Water); scrap |
| Reclaimed Wood | Crates (2–3) and broken chairs (1–2); **hatchet required** | Campfire (planks) |
| Rubbing Alcohol | Pickups (camp, ranger roof, caches) | Antiseptic Bandage, Campfire (alcohol start), Torch, lantern fuel |
| Rope | Craft (4 Plant Fiber); pickups | Wooden Bow |
| Battery | Pickups (knife lookout, caches) | Flashlight power (20 min each) |

## Food and water (hunger / thirst / health restored)
| Item | Restores | Notes |
|---|---|---|
| Chokecherries | 18 / 4 / 2 | Bushes |
| Dried Chokecherries | 14 / 0 / 1 | 3 berries → 2, at a campfire; stack 20 |
| Raw Meat | 8 / 0 / −4 | From Dead Rabbit (knife required) |
| Cooked Meat | 35 / 0 / 4 | Campfire |
| Raw Fish | 6 / 1 / −3 | From Whole Trout (knife optional) |
| Cooked Fish | 28 / 2 / 3 | Campfire |
| Canned Beans | 30 / 2 / 0 | **Needs an opener** (can opener, knife or hatchet). Smashed: 65% and no tin. Clean: returns an Empty Tin |
| Canned Peaches | 18 / 12 / 0 | Same as beans |
| Tin of Creek Water | 0 / 15 / −2 | Fill an Empty Tin at the stock tank (when not thirsty) |
| Tin of Boiled Water | 0 / 28 / 0 | Boil Water at a campfire; returns the tin |
| Whiskey | 0 / 3 / 0, +15 stamina | Also antiseptic |
| Energy Bar | 22 / 0 / 0, +10 stamina | Pickups |
| Soda Can | 6 / 14 / 0 | Leaves an Empty Tin |
| Bottled Water | 0 / 30 / 0 | Pickups |

## Medical
| Item | Restores health | Recipe |
|---|---|---|
| Bandage | 15 | 1 Cloth (anywhere) |
| Fiber Bandage | 8 | 3 Plant Fiber |
| Antiseptic Bandage | 30 | Bandage + Rubbing Alcohol, or Bandage + Whiskey |
| Painkillers | 10 | Found (radio hut roof) |
| First Aid Kit | 45 | Found (supply caches) |

## Tools (no combat use)
| Item | Tool role | Effect |
|---|---|---|
| Hatchet | Axe | **Works only from the hotbar, the cross bar weapon slot, or while held** (not from the lower pack rows; `ASHInventory.axeNeedsQuickSlot`). Use it to hold it in the right hand. Fells dead trees, chops logs and stumps; required for crates/furniture. Deadwood 2× yield at half the hold. Opens cans |
| Hunting Knife | Knife | Required to harvest a rabbit and for Cut Cloth Strips. Better shirt/trout yields. Opens cans |
| Improvised Knife | Knife | Craft: 2 Scrap Metal + 1 Cloth + 1 Stone |
| Can Opener | CanOpener | Opens cans cleanly |
| Damaged GPS | GPS | Starting item. Opens the map (M or hotbar). Needed to log survey points |

## Weapons, ammo and lights (equip from the hotbar: 1–9 toggles hold)
`ASHEquipment` on the player (`Assets/_ASH/Scripts/Gameplay`): three slots. **Main** = right hand (weapons, hatchet). **Off** = left hand (torch, flashlight). **Hip** = left hip (lantern). Using ammo or fuel from the hotbar loads/refuels instead.
| Item | Slot | Use |
|---|---|---|
| Hunting Rifle | Main | LMB: fires one .30-06 round (stub: muzzle flash, no damage) |
| Pistol | Main | LMB: one 9mm round (stub) |
| Wooden Bow | Main | LMB: looses one Wooden Arrow (stub) |
| Hatchet | Main | Held only; chopping works from the pack |
| .30-06 Rounds / 9mm Rounds / Wooden Arrows | — | Ammo (stacks 20/30/20) |
| Torch | Off | Crafted. Lit when equipped; burns 300 s then is used up (stack 3). Held raised by left-arm IK |
| Flashlight | Off | Battery, 1200 s per battery; auto-swaps from the pack |
| Oil Lantern | Hip | Pickup. Hangs from the left hip, scaled 0.6, spring swing. Burns Rubbing Alcohol: 480 s per unit, holds 2; tops itself up from the pack when dry; use alcohol to refill |

- **L** toggles the light: the hand light first, else the hip lantern, else readies the first light in the pack. Off = no fuel burn.
- **Hold RMB** with a torch or flashlight: aim. The body turns to face the camera (Invector strafe, settings restored on release); the left arm points the beam / raises the torch toward the aim (`ASHLightAim` on the player Animator).
- Tunables: `ASHEquipment` (hip offset/scale, swing stiffness/damping/response/max angle) and `ASHLightAim` (aim rotation speed, IK weights, raised-torch offset, aim reach/raise, elbow hint, torch tilt, blend speed) on `ASH_Player`. Per-item grip: `holdPosition` / `holdEuler` / `gripStyle` on each item asset.

## Hotbar and cross bar (`ASHHud`, `ASHInventory`)
- **Hotbar** (bottom centre, keys 1–6): the pack's top row; holds anything (pickups, tools, weapons).
- **Cross bar** (bottom right, adapted from the Genesis Hot Cross): four quadrant slots split by a gold cross, centre ring = rounds for the held weapon. Stored as 4 extra inventory slots after the pack, so stacks, counts and crafting see them.

| Quadrant | Holds | Key / gamepad | Press |
|---|---|---|---|
| Top-left: Weapon | Rifle, pistol, bow, hatchet | Z / d-pad up | Hold or holster |
| Top-right: Ammo | Rounds, arrows | X / d-pad right | Shows the count (reload stub) |
| Bottom-right: Light | Lantern, torch, flashlight | G / d-pad left | Equip; if already worn, toggle on/off |
| Bottom-left: Food | Food, water, meds | V / d-pad down | Eat / drink / apply |

- **Drag and drop** in every direction between the pack grid, the hotbar and the cross bar (open the pack with Tab). Cross quadrants refuse the wrong kind of item (a toast says what fits); a swap is refused if the other item wouldn't fit where it lands.
- Pickups never land in the cross bar on their own, but matching stacks top up there (ammo).
- Gamepad: Craft moved from d-pad up to **LB** and Map from d-pad right to **RB** (runtime override; the saved input asset is untouched). Aim is also on **LT**.

## Survival Pack (`ASH/World/Survival Pack/…`, `Assets/_ASH/Editor/World/ASHSurvivalPackTools.cs`)
- **1. Build** (also runs at the end of the Item Catalog build): pickup prefabs get a nested HQ Survival Pack "Visual" (vendor files unedited; ASHAccumulation stripped, plain material copies, colliders off, one trigger box, no accumulation). Pickup prefabs are updated in place, so scene instances keep position, item and amount. Also builds the held prefabs in `Assets/_ASH/Prefabs/Held`, wires the items, adds `ASHEquipment` + `ASHLightAim` to the player and rebakes icons.
- World props: ASH_SalvageCrate → WoodenCrate01, ASH_StonePile → Stone, ASH_Campfire → vendor Campfire (ASH flames/light kept).
- **2. Place New Item Pickups** (done once, scene saved) under `ASH_SurvivalPackPickups`: Lantern, Wooden Bow, Arrows ×6 at the Hatchet; Flashlight, Battery ×2 at the Hunting Knife; Pistol, 9mm ×8 at the Can Opener; Rifle, .30-06 ×5 at the first Whiskey; Energy Bars ×2, Soda, Bottled Water, Rope, First Aid Kit, Battery at the Canned Peaches caches.
- The item models come from the pack's Prefabs subfolders (Food, Objects, Weapons, Medical, Resources). `Prefabs/Environment` is nature props.

## Salvage (Break Down: journal Items tab, or B over a pack slot)
| Item | Yields | Tool |
|---|---|---|
| Old Flannel Shirt | 2 Cloth | Knife optional (1 without; 1.75× slower) |
| Rags | 1 Cloth | — |
| Dead Rabbit | 2 Raw Meat | **Knife required** |
| Whole Trout | 1 Raw Fish | Knife optional |
| Empty Tin Can | 1 Scrap Metal | — |
| *World:* Salvage Crate / Broken Chair | Reclaimed Wood (+ scrap chance) | **Hatchet required** |
| *World:* Dead tree / downed tree / log / stump | Logs, then Firewood + Kindling (see Axe harvesting) | **Hatchet required** |

## Recipes (21)
| Recipe | Category | Inputs → Output | Station / tool | Time |
|---|---|---|---|---|
| Campfire | Fire | 4 Deadwood, 3 Stone → Campfire | Anywhere | 1.5 s |
| Campfire (planks) | Fire | 3 Reclaimed Wood, 3 Stone → Campfire | Anywhere | 2 s |
| Campfire (firewood) | Fire | 2 Firewood, 2 Kindling, 3 Stone → Campfire | Anywhere | 2 s |
| Campfire (alcohol start) | Fire | 2 Deadwood, 3 Stone, 1 Rubbing Alcohol → Campfire | Anywhere | 1.5 s |
| Bandage | Medical | 1 Cloth → 1 | Anywhere | 2 s |
| Fiber Bandage | Medical | 3 Plant Fiber → 1 | Anywhere | 3 s |
| Antiseptic Bandage | Medical | Bandage + Rubbing Alcohol → 1 | Anywhere | 2 s |
| Antiseptic Bandage (whiskey) | Medical | Bandage + Whiskey → 1 | Anywhere | 2 s |
| Cook Meat | Food | Raw Meat → Cooked Meat | Lit campfire ≤ 4 m | 6 s |
| Cook Fish | Food | Raw Fish → Cooked Fish | Lit campfire | 5 s |
| Dry Chokecherries | Food | 3 Chokecherries → 2 Dried | Lit campfire | 5 s |
| Boil Water | Food | Tin of Creek Water → Tin of Boiled Water | Lit campfire | 4 s |
| Improvised Knife | Tools | 2 Scrap, 1 Cloth, 1 Stone → 1 | Anywhere | 6 s |
| Cut Cloth Strips | Materials | Old Shirt → 3 Cloth | Knife | 3 s |
| Flatten Tins | Materials | 2 Empty Tin → 1 Scrap Metal | Anywhere | 2 s |
| Split Kindling | Materials | 1 Firewood → 3 Kindling | Hatchet | 2 s |
| Torch | Tools | 1 Deadwood, 1 Cloth, 1 Rubbing Alcohol → 1 | Anywhere | 2 s |
| Torch (kindling) | Tools | 2 Kindling, 1 Cloth, 1 Rubbing Alcohol → 1 | Anywhere | 2 s |
| Rope | Materials | 4 Plant Fiber → 1 | Anywhere | 4 s |
| Wooden Arrows | Tools | 2 Kindling, 1 Scrap Metal, 1 Plant Fiber → 3 | Knife | 6 s |
| Wooden Bow | Tools | 2 Firewood, 1 Rope → 1 | Knife | 10 s |

## Axe harvesting (`ASHChoppable`, `Assets/_ASH/Scripts/Interaction`)
Hold Interact; each hold is one chop (placeholder chop audio from Genesis, `Assets/_ASH/Audio/Harvest`). Without a hatchet the prompt says "Needs Hatchet". The final chop is refused when the pack is full. Respawn: `respawnSeconds` on each prefab (default 0 = never).
| Prefab (`Assets/_ASH/Prefabs/Harvest`) | Source mesh | Chops | Result |
|---|---|---|---|
| ASH_Harvest_DeadFir_A / _B | NatureManufacture Fir_08 Dead (forest / cheap) | 8 (0.75 s each) | Topples away from you (2.6 s), then up to 4 logs along the fall line + a stump |
| ASH_Harvest_DeadTreeDowned | HQ Branches/DeadTree | 5 | 2 Kindling, splits into 2 logs |
| ASH_Harvest_Log | HQ Log_Static | 3 | 3 Firewood + 1 Kindling |
| ASH_Harvest_Stump | HQ Branches/TreeStump | 4 | 2 Firewood + 2 Kindling |

## World resources (`ASH/World/Resources/…`, `Assets/_ASH/Editor/World/ASHHarvestTools.cs`)
- **1. Build content** makes the wrappers: root on the Resource layer → Visual → nested vendor prefab (vendor ASHAccumulation stripped, plain material copies in `Assets/_ASH/Materials/Plain`). Pickups get one trigger box; plant shadows are off.
- **2. Replace scene** swapped the scene once (scene saved): 22 ASH_Deadwood → branch piles (same node data), 5 fiber grass → plants, 28 placeholder dead trees → dead firs (every 4th a downed tree), the HQ DeadTree/TreeStump near (520, 303, 1577) → harvestable in place + 2 dead firs.
- **Scatter All / Wood / Plants** (additive) and **Clear Wood / Plants / All**: everything goes under `ASH_ResourceScatter/Wood|Plants`, with Undo. Biome-weighted (ridge/gully/prairie/creek), drawn toward trees, off roads and trails, kept off terrain trees, spaced from other usables. Counts, weights, seed and spacing are in `Assets/_ASH/Data/World/ASH_ResourceScatterSettings.asset`; presets Low (×0.4) / Default / Extreme (×3). Current scatter: 240 branch piles, 40 stumps, 20 downed trees, 360 plants.
- The plant materials (ANGRY MESH Fern, Grass_02, Grass_03) were converted in place to ASH/Accumulation Lit foliage mode (alpha clip 0.5, two-sided, snow on top). Originals backed up in `C:\Users\Teabagger\ash_xfer\backup_am_plant_mats`. The vendor grass wind sway is lost.

## Placement (pickups, 46 placed by `ASH/Build/7. Region + Survey Points`)
- **Redstone Camp (10–45 m from the cabin):** Hatchet, Old Shirt, Rags ×3, Canned Beans, Empty Tin, Dead Rabbit, Rubbing Alcohol, 3 plant clumps, a crate and a chair.
- **Survey rewards on top of each climb:** (1) Redstone lookout: Hunting Knife; (2) Ranger roof: Bandage + Rubbing Alcohol; (3) Ash Hollow tower: Canned Peaches ×2 + Whiskey; (4) Radio hut roof: Can Opener + Painkillers.
- **Along the Mule Trail, county road and descent route:** rags, cans, a shirt, a crate, fiber. Creek crossing: 2 trout. Burned homestead: shirt, chair, beans. Each supply cache: peaches, 2 cloth, alcohol or whiskey.

## Optional and undecided: truck parts
**Status: the truck is undecided and optional.** Default play is on foot only. If a truck is ever added, it would be slow, on Old County Road only, with gas and parts scarce and tied to goals.

The items live in one folder, `Assets/_ASH/Data/Items/Optional_TruckParts/`: Gas Can, Spark Plugs, Car Battery, Radiator Hose, Spare Tire, Fan Belt (category Key; each name ends "(optional)").
- Not placed in the world, used by no recipe, and referenced by no system. There are no vehicle hooks anywhere.
- To remove: delete that folder and its rows in `ASHItemCatalog.cs` (the entries using `dir: TruckDir`).
