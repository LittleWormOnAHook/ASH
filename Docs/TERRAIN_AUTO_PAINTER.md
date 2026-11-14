# Terrain Auto Painter

`Assets/_ASH/Editor/TerrainAutoPainterWindow.cs` adds **ASH → Terrain Auto Painter** to Unity's menu. It paints a Terrain's splatmap from slope, normalized height, and low-frequency noise. It does not modify the RAW heightmap.

## Setup

1. Let Gaia and all new assets finish importing.
2. Create/import the Terrain Layers you want to use.
3. Select the ASH Terrain, then open **ASH → Terrain Auto Painter**.
4. Assign layers in this recommended order:
   - **Prairie / Flat** — dry grass, shortgrass, or dry soil.
   - **Hills / Scrub** — dirt, scrub grass, or weathered soil.
   - **Rocky Hills** — blended rock/dirt.
   - **Cliffs** — your steepest, most vertical-looking rock material.
   - **High Ridges** — optional lighter/drier high-elevation material.
5. Start with the supplied thresholds and press **Auto Paint Terrain**.
6. Hand-paint paths, campsites, creek beds, and focal areas afterward.

## Starting values for ASH

| Zone | Slope |
|---|---:|
| Prairie | below 7° |
| Hills | 7°–20° |
| Rocky hills | 20°–37° |
| Cliffs | above 37° |

For the procedural 4 km ASH terrain, start the optional High Ridges layer at **0.70** normalized height and reach full strength at **0.88**. Set noise to **0.12** to prevent obviously contour-shaped texture borders.

## Notes

- The tool uses the Terrain's current **Control Texture Resolution**. 2048 is a strong starting value for this 4 km terrain; use 4096 only for finer paint detail where the extra memory is acceptable.
- Click Unity's Undo command if you dislike a pass; the TerrainData paint operation is registered as one undo step.
- The tool replaces the Terrain's active layer array with the layers assigned in its window. Include every layer you want retained before painting.
