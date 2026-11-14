# ASH terrain heightmap

**Status: PROCEDURAL FALLBACK — not real DEM data.** USGS 3DEP retrieval was unavailable in this environment on 2026-11-13, so the height values were generated deterministically to evoke a Spanish Peaks foothills transition: shortgrass prairie to the east, broken modest foothills and a few ridge lines to the west. Do not cite this heightmap as surveyed elevation.

## Files

- `ASH_heightmap_4km_R16LE_4097.raw` — 4 km × 4 km master, 4,097 × 4,097 samples, headerless R16 little-endian.
- `ASH_heightmap_4km_preview.png` — annotated, north-up visual preview.
- `ASH_heightmap_2km_{NW,NE,SW,SE}_R16LE_2049.raw` — optional 2 km tiles. They use shared boundary samples so their seams line up.
- `ASH_heightmap_metadata.json` — machine-readable bounds, encoding, and import details.

## Geographic reference bounds (WGS84)

| Edge | Coordinate |
|---|---:|
| West | -104.9227° |
| East | -104.8775° |
| South | 37.3900° |
| North | 37.4260° |

This approximately 4 km reference box lies in the Spanish Peaks foothills region of southern Colorado. It was selected for art direction only; no DEM samples were acquired.

## Unity import — master terrain

1. Create a Terrain and choose **Import Raw** in Terrain Settings.
2. Select `ASH_heightmap_4km_R16LE_4097.raw`. Set **Resolution: 4097 × 4097**, **Depth: 16-bit**, **Byte Order: Windows / little endian**, and **Flip Vertically: off**.
3. Set Terrain Width = **4000 m**, Length = **4000 m**, Height = **1400 m**.
4. The encoded elevation range represents 2000–3400 m. Unity Terrain has no native vertical datum, so position the Terrain GameObject at **Y = 2000** if you want its world values to follow that convention.

The stored rows run north → south and columns west → east. If your local Unity convention makes north point the wrong way, use either Import Raw’s vertical flip or rotate the terrain 180°—not both.

## Unity import — four seamless tiles

Import each tile at **2049 × 2049**, with Width/Length **2000 m**, Height **1400 m**, using the same byte order and flip choice. Place their southwest corners at:

| Tile | X (m) | Z (m) |
|---|---:|---:|
| SW | 0 | 0 |
| SE | 2000 | 0 |
| NW | 0 | 2000 |
| NE | 2000 | 2000 |

Their edge samples are numerically identical. Set neighboring terrain tiles in Unity so shared edges meet exactly; use Terrain Tools’ neighbor linking after placement.

## Intended visual character

- East: broad, playable prairie with drainage incisions.
- Center: rolling terrain, low spurs, and cover-friendly gullies.
- West: more broken ground, several diagonal ridges, and a single landmark high point.
