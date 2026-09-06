# Structure Handler

Adds Structure Handler controls to Silverpine's shared in-game **Mods** menu
and a structure editor to the main-menu mod tools.

Version **1.2.6** requires **ModdingTools 1.10.0 or newer**.

## Destination support for editor objects (1.2.6)

- Imports now carry over the actual destination terrain at occupied cells where
  the JSON supplies no replacement terrain. This fixes standalone editor wells
  and the same gap for furniture, walls/doors, crops, herbs, and ores. Trees and
  other obstructing objects are still cleared; only terrain is carried over.
- This covers every occupied cell, including multi-cell objects and queued
  imports when their destination is loaded. Explicit imported terrain wins;
  disabled water imports do not count as replacement terrain.
- Carried terrain retains its component state, room name, appearance, and any
  existing floor-deconstruction history. It goes through the same rollback and
  native/supplemental saving paths as the rest of the import. Portable JSONs are
  not rewritten with destination-specific terrain.
- Reimport replacement also recognizes previously imported supplemental objects
  and matching incoming prefabs. Wells have no native serializable component;
  they must not be skipped and stacked on each reimport. Character and persistent
  object exclusions still apply.
- Already missing ground cannot be inferred from an empty destination. Test
  from a save before the damaged import, or explicitly add the desired supporting
  terrain in the editor before reimporting that cell.

## Imported-cell cleanup and destination ground (1.2.5)

- The native-save pool filter now also excludes unused pool entries already
  parked at coordinates replaced by successful imports. This closes the gap
  where older inactive copies were not among the active objects cleared by the
  import. Active objects and non-pool objects are never filtered by footprint.
- Exact `importedCells` are stored per save (companion version 3), restored on
  load, rolled back with failed imports, and cleared for a world tile when native
  regeneration actually runs. This is not an active-object blacklist or an
  automatic reimport. Deconstructed ground and intentionally imported nature
  remain saveable. Unrelated coordinates are untouched.
- Imported floors with the native ground-restoration handler now record the
  destination terrain before replacement. When replacing an existing floor,
  they inherit its underlying ground instead of recreating that old floor on
  deconstruction. Queued imports capture this when their destination is loaded.
  Editor-created floors use the same destination capture, not default grass.
- New world exports omit destination-specific `GrassTileHandler` history; old
  JSONs remain readable but their source ground is overridden on import. Native
  saves and rollback snapshots still preserve the current ground history.
- Restored plain terrain such as dry dirt receives supplemental saving when an
  imported floor is deconstructed, and matching old clearance data is retired.
  Native-serializable ground continues to use normal saving. If the importer
  cannot identify a native-spawnable destination ground type (for example, a
  bare world-exit utility tile), it stops before replacing that import part
  instead of inventing grass or a missing prefab.

Older saves do not contain reliable import footprints. Reimport the affected
structure once with 1.2.5, save to a new slot, and reload to verify. As always,
reimport replaces current objects/contents in its footprint. Existing floors
are not retroactively assigned a different ground type merely by loading.

## Native save corrections (1.2.4)

- Trees/grass released by Structure Handler are excluded from native saves
  while they are unused in the game's reuse pool. The filter tracks exact
  removed instances, not coordinates or arbitrary disabled objects. Reclaiming
  an object clears its exclusion. Other pool entries are untouched.
- Legacy global protection/buildability config values are no longer migrated
  into saves. Existing per-save choices remain intact; saves without companion
  data start with empty tile flags. Save-specific legacy resource files can
  still migrate safely.
- Ordinary sprite choices are written into the native `RandomSprite` state.
  Native rotation and seasonal variation already serialize themselves. Extenders
  supplied by the original prefab no longer need an extra appearance record.
  Existing redundant records migrate on visiting/loading their objects, before
  the next native save is written; unloaded or missing objects keep their data.
- Deliberately fixed seasonal sprites and other unsupported sprite/component
  overrides still use companion data. Explicit editor sprite selections carry
  `lockSpriteVariant`; ordinary exported seasonal snapshots follow native
  seasons. Old seasonal choices inconsistent with their saved variation seed
  are conservatively retained as overrides.

**Previously affected saves:** discarded scenery may already be active in the
native save. This update does not guess which active trees should be deleted.
Back up the save/JSONs, reimport the affected structure once, then save to a new
slot and reload. Reimport replaces objects and contents in its footprint.

## Save/load persistence fix (1.2.3)

- Save-data registration now survives Silverpine destroying the main-menu
  BepInEx host when gameplay starts. Per-save protection, regeneration markers,
  appearance overrides, and queued imports continue to save and restore.
- Imported scenery not covered by native serialization (such as plain dry dirt)
  is now stored explicitly in the `.sav.moddingtools` companion, along with the
  specific non-serialized originals cleared by imports. Restoration is limited
  to loaded world tiles and persistence zones, without deleting neighboring
  terrain or replaying native-saved furniture/container state.
- Supplemental objects are refreshed from their live state when saving/leaving
  a tile. Removing or moving one does not revive its original imported copy.
  Intentional tile regeneration clears that tile's supplemental records too.
- Switching saves resets imported supplemental objects and restores suppressed
  originals in still-loaded scenes. Older companion payloads migrate with empty
  supplemental lists; missing prefab/restoration failures preserve saved data.

**Existing affected saves:** 1.2.2 could unregister before writing any companion
data, and never recorded non-serialized terrain. The update cannot recover
information absent from those saves. Back up the save and source JSONs, restart
with 1.2.5, reimport only the affected structures once, then save to a new slot.
Reimporting replaces contents/state in its footprint as usual; subsequent loads
do not replay the original JSON. Keep the new `.sav` and `.sav.moddingtools`
files together. No existing saves or structure JSONs are rewritten by installing
the update.

## Editor and import features

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
- Saves without this data can migrate the save-specific
  `<save>.structurehandler-resources.json` once, but never global tile config values. Old files are retained, and a
  malformed legacy resource file is not overwritten. New games start with clean
  tile settings instead of inheriting another save's choices.
- Standard sprite choices use native component saving. Exceptional fixed-sprite
  overrides are reapplied after native initialization; manually added NPC
  interaction extenders survive saving/loading through companion data.
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
in-game smoke test. The separate `Tests/NativeSerializerSmoke` project checks
the new companion fields and old-payload compatibility using Silverpine's
actual `StringSerializationAPI` (local game assemblies are required).
`Tests/CheckPluginLifetime.ps1` also inspects the compiled plugin to ensure
bootstrap cleanup cannot remove its process-lifetime save registration.

Build with `dotnet build -c Release`, then copy `StructureHandler.dll` from the
release output folder into `BepInEx/plugins/StructureHandler/`.

## Credits

Created by **Saelac and ChatGPT**.
