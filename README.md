# Structure Handler

Adds Structure Handler controls to Silverpine's shared in-game **Mods** menu
and a structure editor to the main-menu mod tools.

Version **1.2.2** requires **ModdingTools 1.10.0 or newer**.

- Blue filled/outlined preview cells show the game's normal world-transition
  positions, whether or not any transition prefab exists in the JSON. They are
  the outermost rows/columns of each 100×100 world tile (`-50` and `49`, repeated
  every 100 coordinates on both axes). The preview reports how many of these
  cells the structure overlaps. This is a coordinate guide, not a live-world
  scan; pre-existing custom terrain may already have replaced an exit.
- The editor's **Plan** tab paints non-gameplay color annotations onto any grid
  cell, including empty cells. Choose a palette color or `#RRGGBB`, then
  left-click/drag; right-click cancels and middle-drag pans. An eraser, visibility
  toggle, clear-all button, and Undo/Redo are provided. Planning colors save as
  readable `planningTiles` entries with `x`, `y`, and `color`. They move with the
  whole structure and combine across JSONs (last alphabetically named file wins
  for conflicting annotation colors). They never become import objects, terrain,
  occupied cells, or regeneration markers. Blue transition outlines remain on top.

- Export asks for a name and writes
  `<Silverpine persistent data>/custom structures/<name>.json`.
- **Structures Import** opens a checkbox picker for the `.json` files in that
  folder. Nothing is selected initially. Choose individual files, **Select All**,
  **Clear Selection**, or **Refresh**, then press **Import Selected**. The list
  supports the mouse wheel and a draggable scrollbar; **Back** returns to the
  Structure Handler controls without importing. Only checked files are read and
  validated, so an unrelated broken JSON does not block the selected imports.
  Non-conflicting selected files use alphabetical order; conflict choices
  determine the order of overlapping files. Refresh retains checked files that
  still exist; new files start unchecked. The picker uses the existing scaled
  native-style overlay and retains its background/player-input lock.
- Imports are preflighted for missing prefabs, incompatible component paths,
  invalid coordinates, and nonexistent destination world tiles before changing
  the world. Original objects and mod state are snapshotted before replacement.
  If creation, deserialization, registration, or finalization fails, the importer
  removes partial replacements and reconstructs the originals. A rollback failure
  is reported explicitly and requires reloading the last game save.
- Sections belonging to unloaded world tiles are queued, not spawned into the
  current scene. They apply after entering the destination tile. Save the game
  to retain pending imports; this prevents off-tile stacking and save loss.
- Overlapping JSON files are handled as pairwise conflict cases. Each prompt
  names exactly two structures, reports their shared position count, and offers
  exactly two winner choices before proceeding to the next case.
  Contradictory three-way choices are rejected instead of silently ignoring an
  earlier winner. Cancel and restart to revise earlier choices.
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
  that world tile are removed only if the native regeneration call actually
  runs (including when another mod has vetoed it).
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
- **Wooden bridge railing** is under **Quick Place → Structural**; search
  `bridge` or `railing`. The asset is explicitly loaded from
  `MiscPrefabs/prefab_furniture_railing_wood`, which the native serialization
  registry does not load. Structure Handler registers its own inactive copy
  with turf registration and a serializable marker so imported railings can be
  replaced, exported again, and restored by native saves. The original resource
  asset and existing bridge scenery are not modified. The canonical JSON prefab
  name remains `prefab_furniture_railing_wood`.
- Quick Place is rebuilt whenever the editor opens and combines the game's
  serialization registry with every loaded scene-less `prefab_*` asset. This
  includes functional base-game and mod-provided prefab pieces that are not
  exposed by the normal construction lists.
- Imported `ResourceNodeType.Herb` and `ResourceNodeType.Ore` objects create
  per-save regeneration markers. If harvested, their original prefab respawns
  at the recorded coordinate when the clock reaches midnight, provided another
  ore/herb or a constructed object does not occupy the tile.
  Distant resources are checked when their world tile is loaded, at most once
  per game day. Supporting floor/terrain does not block regeneration.

## Save data and editor improvements

- Tile protection, player-buildable flags, resource markers, pending imports,
  and imported sprite/extender overrides now belong to each individual game
  save through its `<save>.moddingtools` companion. Keep that companion with the
  native save when copying/backing it up. Changes are committed on a normal game
  save, not immediately on import or midnight.
- Saves without this data migrate the old tile config values and
  `<save>.structurehandler-resources.json` once. Old files are retained, and a
  malformed legacy resource file is not overwritten. New games start with clean
  tile settings instead of inheriting another save's choices.
- Sprite choices are reapplied after native random-sprite initialization and
  loading; manually added NPC interaction extenders survive saving/loading.
  Pooled objects do not carry overrides into unrelated reused instances.
- Mouse wheel scrolls file lists, object/terrain lists, Quick Place, long detail
  panels, and sign text. Over the preview, it zooms around the cursor. Middle
  mouse dragging still pans the preview.
- Object, terrain, and catalog lists draw visible rows only. Catalog labels,
  sprite variants, footprints, render order, and selection geometry are cached
  until invalidated. Repaint-only preview work avoids repeating rendering work
  on every input/layout event.
- Undo/Redo buttons and Ctrl+Z/Ctrl+Shift+Z restore document edits. A drag-paint
  or drag-delete stroke is one undo action. History is limited to 32 snapshots
  and approximately 32 MiB (one oversized current snapshot may remain).
- Closing/replacing an edited document asks to save or discard changes.
  An emergency Mod Tools close keeps the document in memory for reopening in
  the same scene. Save As asks before replacing a different existing file.
- JSON writes replace files atomically; the previous version is retained as
  `<name>.json.bak`, which is not included in normal imports.
- Moving a document with terrain requires a whole-tile displacement, keeping
  fractional furniture anchors aligned with their supporting terrain.
- Floor-aware Vertical Layers integration accepts editor API 2 (the matching
  compatibility source is updated separately); the integration no longer forces
  an editor cache rebuild on every GUI event.

## Verification

Run `dotnet run --project Tests/RegressionTests.csproj -c Release` for the
Unity-independent regression suite. It covers conflict ordering, destination
partitioning, water filtering, undo history, list virtualization, legacy binary
decoding, transaction failure recovery, atomic JSON replacement, import file
selection, railing state, world-transition boundaries, and planning colors. Unity
rendering, pooled-object lifecycles, and real save/load behavior still need an
in-game smoke test.

Build with `dotnet build -c Release`, then copy `StructureHandler.dll` from the
release output folder into `BepInEx/plugins/StructureHandler/`.

## Credits

Created by **Saelac and ChatGPT**.
