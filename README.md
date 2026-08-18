# Structure Handler

Adds Structure Handler controls to Silverpine's shared in-game **Mods** menu
and a structure editor to the main-menu mod tools.

- Export asks for a name and writes
  `<Silverpine persistent data>/custom structures/<name>.json`.
- Import loads every `.json` file in that folder in alphabetical order.
- Imports are preflighted before the world is changed. Each file keeps the
  existing objects in place until all replacements have been created and
  deserialized successfully; a failed file discards its replacements.
- Overlapping JSON files are handled as pairwise conflict cases. Each prompt
  names exactly two structures, reports their shared position count, and offers
  exactly two winner choices before proceeding to the next case.
- Settings buttons can capture the player's current tile as the top-left and
  bottom-right export bounds. With neither selected, export remains unrestricted;
  with both selected, only objects inside the inclusive rectangle are exported.
  Each bound button offers **Use Player Position**, **Click World Tile**,
  **Type Coordinates**, and **Clear** when set. Typed coordinates accept
  `x, y` or `x y`; Escape cancels world-click selection.
- A complete custom rectangle replaces the normal player-owned-plot restriction,
  allowing bounded exports of player construction in locations such as the
  bathhouse area.
- Bounded exports include every reconstructible, non-character serializable
  prefab inside the rectangle, including vegetation, crops, herbs, resources,
  ores, boulders, natural cliff walls, furnishings, persistent scenery, and
  untouched shed pieces. Live characters, enemies, and transient/non-prefab
  systems remain excluded because duplicating them is unsafe.
- Player-placed pickupable furniture, including candles and lanterns, is
  included while original NPC-owned furniture is excluded.
- Constructed signs and their written messages are included explicitly.
- Original base-game shed objects are identified at startup by prefab and
  position, then excluded. Custom additions and renovations inside the shed
  footprint remain exportable.
- Doors bring along their underlying flat-ground terrain prefab when no
  constructed floor exists. Terrain is recorded explicitly because Silverpine's
  generated ground tiles do not implement its object-serialization interface.
- All terrain layers inside custom bounds are exported explicitly, including
  natural ground, constructed floors, water, and shallow water.
- Bathhouse water is always restored during import. A persistent Settings toggle
  named **Import Other Water Tiles** controls whether ordinary water terrain is
  restored; it defaults to off.
- Wilderness world tiles containing imports are recorded and protected from the
  game's midnight blob-clearing pass. The in-game controls show the player's
  current world-tile coordinates and whether regeneration is **On** or **Off**.
  That button changes only the world tile under the player, so individual tiles
  can be protected or returned to normal regeneration independently. When an
  enabled tile is regenerated, all imported herb/ore respawn markers within
  that world tile are removed from the current save's marker sidecar.
- A neighboring in-game toggle shows whether the world tile under the player is
  entirely **Player Buildable**. Enabling it makes all coordinates in that
  100×100 world tile pass the game's player-owned-property construction check.
- Bounded exports include placed `WorldItem` objects as well as pickupable
  furniture.
- Format 3 JSON lists each object by editable prefab name and world coordinates,
  with hierarchy-aware component records. Child components and repeated
  components of the same type are preserved. Component state remains Base64
  because the game itself serializes component state as binary. Formats 1 and 2
  remain importable.
- Delayed `WorldItem` sprite refreshes are ignored when a later overlapping
  structure has already replaced and destroyed the earlier imported item.
- Existing grass, trees, bushes, plants, herbs, and crops are cleared only from
  positions actually occupied by the import. Imported natural objects are not
  mistaken for clearance targets.
- Supporting terrain preserves its sprite choice, zone, and serializable
  component state instead of losing those fields when a floor is normalized.
- Quick Place is divided into Terrain, Structural, Furniture, Nature, and
  Utility. Constructed stone and wooden floors are listed under Structural even
  though they remain terrain records in the JSON.
- Quick Place is rebuilt whenever the editor opens and combines the game's
  serialization registry with every loaded scene-less `prefab_*` asset. This
  includes functional base-game and mod-provided prefab pieces that are not
  exposed by the normal construction lists.
- Imported `ResourceNodeType.Herb` and `ResourceNodeType.Ore` objects create
  per-save regeneration markers. If harvested, their original prefab respawns
  at the recorded coordinate when the clock reaches midnight, provided another
  ore/herb or a constructed object does not occupy the tile.

Build with `dotnet build -c Release`, then copy `StructureHandler.dll` from the
release output folder into `BepInEx/plugins/StructureHandler/`.

## Credits

Created by **Saelac and ChatGPT**.
