# ASH Items and Crafting

_2026-11-14 (ET). All data are ScriptableObjects built by `ASH/Build/6. Item Catalog` (`Assets/_ASH/Editor/Build/ASHItemCatalog.cs`, table-driven and safe to re-run; GUIDs kept). Items: `Assets/_ASH/Data/Items`. Recipes: `Assets/_ASH/Data/Recipes`. Pickup prefabs (placeholder primitives): `Assets/_ASH/Prefabs/Pickups`. Numbers are first-pass placeholders, tunable on each asset._

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
| Rubbing Alcohol | Pickups (camp, ranger roof, caches) | Antiseptic Bandage, Campfire (alcohol start) |

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

## Medical
| Item | Restores health | Recipe |
|---|---|---|
| Bandage | 15 | 1 Cloth (anywhere) |
| Fiber Bandage | 8 | 3 Plant Fiber |
| Antiseptic Bandage | 30 | Bandage + Rubbing Alcohol, or Bandage + Whiskey |
| Painkillers | 10 | Found (radio hut roof) |

## Tools (no combat use)
| Item | Tool role | Effect |
|---|---|---|
| Hatchet | Axe | Carried in the pack = equipped. Fells dead trees, chops logs and stumps; required for crates/furniture. Deadwood 2× yield at half the hold. Opens cans |
| Hunting Knife | Knife | Required to harvest a rabbit and for Cut Cloth Strips. Better shirt/trout yields. Opens cans |
| Improvised Knife | Knife | Craft: 2 Scrap Metal + 1 Cloth + 1 Stone |
| Can Opener | CanOpener | Opens cans cleanly |
| Damaged GPS | GPS | Starting item. Opens the map (M or hotbar). Needed to log survey points |

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

## Recipes (16)
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
