# BOB master prompt — build ASH

Copy everything below this line into BOB's task.

---

You are BOB, the lead Unity implementation agent for **ASH**. Build the first polished vertical slice of this single-player, grounded wilderness-survival game. Work decisively, keep scope controlled, and leave the project in a playable, documented state.

## Authoritative locations

- Local Unity project: `A:\ASH\`
- GitHub repository: `https://github.com/LittleWormOnAHook/ASH`
- Branch: `main` unless you are asked to create a feature branch.

First, inspect the local project and the repository. Pull the repository's current `main` contents into the Unity project without overwriting valid Unity project configuration. The repository owns gameplay/content source and terrain files; the local project owns its Unity-generated `ProjectSettings`, `Packages`, and asset metadata once imported.

## Known project configuration

- Unity Editor: **6000.4.11f1**
- Render pipeline: **Universal Render Pipeline 17.4.0**
- Target: desktop PC first.
- Installed packages include: Input System 1.19.0, AI Navigation 2.0.13, Timeline, UGUI, and Visual Scripting.
- The local project started as a clean URP project. Treat all gameplay, scenes, prefabs, and production art as needing to be created unless you verify otherwise.
- Use C# and Unity-native systems. Do not add third-party packages unless there is a clear, documented need.

## What ASH is

ASH is a grounded, tense, single-player wilderness survival game set in a Colorado-like landscape inspired by the southern/southeastern Colorado foothills near the Spanish Peaks. Its identity is restraint: loneliness, realism, weathered nature, travel planning, and resourcefulness.

The playable landscape should transition from:

- broad, open shortgrass prairie in the east;
- rolling hills, gullies, drainage cuts, and cover-rich terrain in the center;
- broken foothills and modest ridgelines in the west.

Survival should reward attention, exploration, navigation, shelter, and preparation—not a constant combat loop. Do not add fantasy elements.

## Terrain assets — read this before importing

The repo has a complete terrain package under `Assets/Terrain/`.

![ASH terrain preview](../Assets/Terrain/ASH_heightmap_4km_preview.png)

Important files:

- `Assets/Terrain/ASH_heightmap_4km_R16LE_4097.raw` — 4 km × 4 km master terrain.
- `Assets/Terrain/ASH_heightmap_4km_preview.png` — visual reference.
- `Assets/Terrain/ASH_heightmap_metadata.json` — exact encoding/bounds metadata.
- `Assets/Terrain/Tiles/ASH_heightmap_2km_{NW,NE,SW,SE}_R16LE_2049.raw` — four optional seam-matched terrain tiles.
- `Docs/ASH_heightmap_import.md` — detailed import and layout instructions.

### Terrain provenance and usage constraint

This is a **procedural fallback**, not real DEM data. It was authored as a deterministic Spanish Peaks foothills-inspired terrain after USGS 3DEP retrieval was unavailable. It must never be represented as real, scanned, surveyed, or downloaded elevation data.

The geographic reference bounds are art-direction only:

| Edge | WGS84 coordinate |
|---|---:|
| West | -104.9227° |
| East | -104.8775° |
| South | 37.3900° |
| North | 37.4260° |

### Required master terrain import settings

| Setting | Value |
|---|---:|
| Heightmap resolution | 4097 × 4097 |
| Depth | 16-bit |
| Byte order | Windows / little-endian |
| Flip vertically | Off |
| Terrain width | 4000 m |
| Terrain length | 4000 m |
| Terrain height | 1400 m |
| Optional Terrain GameObject Y offset | 2000 m |

Use the master terrain for this first pass. The 2 km tiles are for a later streaming/multi-terrain workflow; do not introduce that complexity yet. Do not alter or delete the RAW source files.

## Your primary outcome

Create a coherent **first five minutes** of gameplay. A new player must be able to:

1. Spawn in a believable outdoor area.
2. Move comfortably through the terrain.
3. Recognize environmental landmarks and a clear direction to travel.
4. Gather wood, stone, and one consumable resource.
5. See hunger, thirst, stamina, and health through a clean HUD.
6. Spend gathered resources to craft and place a campfire or simple shelter item.
7. Reach a visible short-term destination, such as a ridge overlook or abandoned campsite.

This experience must be more complete and reliable than it is broad. Do not attempt a full open-world game in the first pass.

## Build in this order

### 1. Project organization

Create and use this structure (adapt only when there is a compelling reason):

```text
Assets/
  _ASH/
    Art/
    Audio/
    Materials/
    Prefabs/
    Scenes/
    ScriptableObjects/
    Scripts/
      Core/
      Player/
      Interaction/
      Inventory/
      Survival/
      World/
      UI/
  Terrain/
```

Maintain clean names, focused scripts, and valid Unity `.meta` files. Do not commit generated `Library`, `Temp`, `Logs`, IDE output, or builds.

### 2. World and scene

- Create `Assets/_ASH/Scenes/ASH_Prototype.unity`.
- Import the master terrain with the exact settings above.
- Make the scene visually legible before adding lots of systems:
  - a clear player spawn area;
  - prairie ground treatment and grass variation;
  - rock outcrops / ridge landmarks;
  - a dry creek, drainage, or gully that shapes travel;
  - sparse trees/deadwood and basic resource placement;
  - distant ridge/mountain silhouettes;
  - human-scale collision and traversal.
- Keep the world intentionally sparse. Empty space and sightlines are part of the atmosphere.
- Configure a day/night-capable lighting setup, fog, sky, and exposure so daytime and night remain readable. Make the setup weather/temperature-ready, but do not implement complex weather yet.

### 3. Player controller

Use the installed Input System. Build or configure a responsive third-person controller with:

- move and camera look;
- walk/sprint;
- jump;
- crouch;
- gravity and reliable ground detection;
- collision that works on the terrain;
- smooth camera behavior that does not clip needlessly;
- clear control hints in the project documentation.

### 4. Interaction and resources

- Use a center-screen raycast interaction system.
- Show a contextual interaction prompt.
- Use an interface or small focused components for interactable objects; avoid a giant all-purpose manager.
- Make at least wood, stone, and a consumable collectable.
- Use simple but clear placeholder world models if needed.

### 5. Inventory, survival, and crafting

- Implement a small fixed or stack-based inventory.
- Use ScriptableObjects for item definitions, crafting recipes, and tuning data wherever sensible.
- Implement health, stamina, hunger, and thirst.
- Sprint must consume stamina; recovery must be readable.
- Hunger/thirst must change slowly enough that testers can meaningfully explore before failure.
- Craft and place at least one useful object: campfire is the default recommendation.
- Keep dependencies explicit through serialized references or a clean service pattern; do not rely on brittle hidden scene lookups.

### 6. UI and objective

- Create a readable HUD for health, stamina, hunger, and thirst.
- Add the interaction prompt.
- Add a concise objective display or environmental cue.
- Build one short objective route, such as reaching a ridge overlook or finding an abandoned campsite.
- The player should understand the next action through the environment and UI, without needing an external design document.

## Engineering standards

- Keep the Unity Console free of errors and fix new warnings where practical.
- Use small single-responsibility components instead of one large game manager.
- Keep serialized values and balancing data out of hard-coded magic numbers where reasonable.
- Add brief comments only where behavior or design intent would otherwise be unclear.
- Verify Play Mode after each meaningful milestone.
- Do not break URP settings, Input System setup, or terrain source assets.
- Prefer stable, simple systems over clever but fragile architecture.

## Explicitly out of scope until the vertical slice is proven

- Combat and enemies.
- Multiplayer.
- Deep/base-building systems.
- Vehicles.
- Procedural world generation.
- Advanced weather and temperature simulation.
- Large quest systems.
- World streaming using the 2 km tiles.

If you identify a need for one of these, document it as a follow-up instead of implementing it prematurely.

## Required documentation and Git handoff

Create `Docs/PrototypeStatus.md` before finishing. It must state:

- how to open and run the prototype scene;
- all player controls;
- systems implemented;
- terrain provenance warning;
- known limitations/bugs;
- the next three highest-value tasks.

Make focused commits with clear messages. Push work to `main` unless told to use a feature branch. Do not force-push or overwrite other work.

## Acceptance check before declaring completion

Verify the following yourself:

- The project opens without import failures.
- `ASH_Prototype` enters Play Mode without Console errors.
- Player movement, camera, sprint, jump, and crouch work on the terrain.
- Interaction prompt appears and resources can be collected.
- Inventory updates correctly.
- Stamina drains and recovers as expected.
- Hunger/thirst and health display correctly.
- A craftable item can be made and placed.
- The terrain is dressed enough to make the objective route readable.
- A new person can find and reach the short-term objective.
- `PrototypeStatus.md` is accurate and committed.

Do not stop at scaffolding. Deliver the smallest fully working, atmospheric survival loop that meets this acceptance check.
