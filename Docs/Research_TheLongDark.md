# Research: The Long Dark (inspiration only)

_2026-11-14 (ET). Quick design research for ASH's journal, crafting and GPS map. Inspiration only: no Long Dark content, names, art, text or numbers are copied into ASH. Sources: The Long Dark fandom wiki pages on Charcoal, Map, Crafting, Cooking and Decay (thelongdark.fandom.com/wiki/...)._

## What The Long Dark does (summary)
- **Mapping:** the map starts blank. Players "survey" with charcoal, which reveals the area around them. More is revealed from higher, clear-weather vantage points. Each survey takes in-game time and uses up a charcoal.
- **Crafting:** a crafting menu is grouped by category (fire, first aid, clothing, tools) and gated by location: anywhere, a workbench, or an active fire. Simple recipes include bandages torn from cloth.
- **Food:** cans open cleanly with a can opener or a blade tool. Forcing them open spills some food and wastes the can. Raw meat and fish are cooked at a fire.
- **Tools:** a hatchet and a knife do the heavy lifting: firewood, harvesting, opening cans. Tools wear out with use.
- **Fire:** needs fuel plus a fire starter. Accelerants help. Burnt-out fires leave charcoal.

## ASH: adopt / adapt / skip
| Idea | ASH decision | How ASH does it |
|---|---|---|
| Blank map revealed by surveying from high ground | **Adapt** | A *damaged GPS* (Trailmate 40) replaces charcoal. The map art exists, but corrupt data hides it behind fog. Climbing a high POI (lookout tower, ranger roof, water tower, radio mast, fire lookout ruin) and logging the viewpoint (2.5 s hold at a topo panel, survey marker, trail register or relay cabinet) reveals authored regions, trails and 1–2 new POIs. Walking reveals a thin corridor. Grounded, not a magic minimap: rough fix only (±70 m circle with drift), no exact dot. |
| Higher = more revealed | **Adopt (authored)** | Each survey reveals data authored per point in `ASH_RegionLayout.json`, escalating 1→5. The last survey (the western fire lookout ruin) reveals the whole region. |
| Category + location-gated crafting | **Adopt** | Journal Crafting tab: categories All/Fire/Medical/Food/Tools/Materials. Stations: Anywhere, or a lit Campfire within 4 m. Recipes can also require a tool (knife). |
| Cloth → bandages | **Adopt (own numbers)** | 1 Cloth → 1 Bandage. 3 Plant Fiber → Fiber Bandage. Bandage + alcohol (or whiskey) → Antiseptic Bandage. |
| Cans need an opener/blade; forced open wastes food | **Adopt (own numbers)** | Can opener, knife or hatchet opens cleanly and returns an Empty Tin Can. Without one, the can is smashed: 65% of the restores, no tin. |
| Hatchet/knife as utility tools | **Adopt** | Hatchet: required for crates/furniture (→ Reclaimed Wood); doubles deadwood yield at half the hold time. Knife: required to harvest a rabbit; improves cloth and fish yields; needed for the Cut Cloth Strips recipe. No combat use. |
| Cooking at a fire | **Adopt** | Cook Meat/Fish, Dry Chokecherries and Boil Water all need a lit placed campfire. Raw meat and fish cost health if eaten raw; creek water costs a little health until boiled. |
| Alcohol / accelerants | **Adapt** | Rubbing alcohol or whiskey: antiseptic bandages, and an alternative campfire recipe that needs less deadwood. |
| Break things down for materials | **Adopt** | "Break Down" on salvage (journal Items tab button, or **B** over a pack slot): shirts/rags → cloth, rabbit → meat, trout → fish, tins → scrap. Crates and chairs are world nodes needing the hatchet. |
| Tool decay / condition | **Skip for now** | No item condition in the slice. Candidate for later (condition 0–100, wear per use). |
| Charcoal from burnt fires | **Skip** | The GPS replaces charcoal mapping. Charcoal may come back later as a crafting material. |
| Temperature, clothing layers, cabin fever, wildlife AI | **Skip (out of slice scope)** | ASH's threat is ash fall and exposure. Revisit after the slice. |
| Long survey time cost | **Adapt lightly** | A 2.5 s hold. A time-of-day cost could be added later if the day clock should matter. |
