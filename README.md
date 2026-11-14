# ASH

## A survival game terrain prototype

This repository contains the initial Unity terrain source asset for **ASH**: a 4 km × 4 km Spanish Peaks foothills-inspired heightmap, plus seamless 2 km streaming tiles.

> **Terrain status: procedural fallback, not real DEM data.**
>
> The planned USGS 3DEP retrieval was unavailable from the asset-generation environment, so the included terrain is a deterministic, authored approximation. It must not be represented as real surveyed elevation data.

## Terrain layout

```
Assets/
  Terrain/
    ASH_heightmap_4km_R16LE_4097.raw
    Tiles/
      ASH_heightmap_2km_NW_R16LE_2049.raw
      ASH_heightmap_2km_NE_R16LE_2049.raw
      ASH_heightmap_2km_SW_R16LE_2049.raw
      ASH_heightmap_2km_SE_R16LE_2049.raw
    ASH_heightmap_4km_preview.png
    ASH_heightmap_metadata.json
Docs/
  ASH_heightmap_import.md
```

## Unity quick start

Import the master RAW file into a Unity Terrain at **4097 × 4097**, **16-bit**, **Windows/little-endian**, no vertical flip. Set Width and Length to **4000 m** and Height to **1400 m**. Full bounds, coordinate orientation, tiling positions, attribution, and import instructions are in `Docs/ASH_heightmap_import.md`.

Reference area bounds (WGS84): west **-104.9227°**, east **-104.8775°**, south **37.3900°**, north **37.4260°**. These coordinates are art-direction reference only.

## Implementation handoff

The complete BOB project brief, including the terrain preview, Unity configuration, implementation priorities, and first playable milestone, is in [`Docs/BOB_HANDOFF.md`](Docs/BOB_HANDOFF.md). A copy-ready build task is also available in [`Docs/BOB_MASTER_PROMPT.md`](Docs/BOB_MASTER_PROMPT.md).
