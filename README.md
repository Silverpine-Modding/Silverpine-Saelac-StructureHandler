# Structure Handler

Adds Structure Handler controls to Silverpine's shared in-game **Mods** menu
and a structure editor to the main-menu mod tools.

Version **1.2.26** requires **ModdingTools 1.12.0 or newer**.

## Text-selection drag capture (1.2.26)

- Release the editor's text-drag mouse capture before drawing other controls on
  mouse-up, even if the original field disappeared or the event was consumed.
  Recover a missed release on the next click without swallowing that click.
- Keep selection, keyboard focus and mouse capture together if a virtualized
  layout changes the field's control ID; use layout-independent text-control IDs.
- Clicking outside a text field lets the intended button or preview act right
  away. Releasing a text drag does not paint or pan the preview. Closing releases
  the editor's own field state without clearing another control's capture.
- Preserve Enter-to-apply coordinates, clipboard shortcuts and full selection.

## Coordinate submission (1.2.25)

- Capture Enter/keypad Enter on the coordinate field's actual keyboard-focus ID
  before the native text widget handles the key. Avoid the pre-draw name lookup
  that could miss submission. Escape still discards the active draft.
- Keep placement checks and native snapping. Report when snapping keeps an object
  at its old position rather than silently appearing to ignore a coordinate edit.
- Retain full-field text selection and the cached preview. No JSON/save changes
  or updates to ModdingTools or Vertical Layers are required.

## Text selection (1.2.24)

- Give single-line editor fields namespaced control IDs independent of the
  virtualized preview/catalog controls. Ctrl+A and double-click select the full
  value, including signed decimals and underscore-separated prefab names.
- Preserve native drag selection, clipboard and keyboard editing. The preview
  no longer takes over a text-selection drag that crosses its boundary.
- Long prefab names scroll inside a fixed-width field; coordinate edits still
  apply only on Enter. No Vertical Layers or ModdingTools update is needed.

## Cached preview and coordinate entry (1.2.23)

- Cache the composed preview in a resolution-scaled render texture. Unchanged
  frames draw one image; edits, pan/zoom, selection, overlays and floor switches
  invalidate it. Preserve sorted sprites, alpha, flips, rotation and clipping.
  Release GPU resources on close, and fall back to direct drawing if unavailable.
- Generate loose-item previews over multiple frames (2 ms target, at most four
  jobs per frame). A single native model render cannot be interrupted. Close,
  reload and deletion cancel obsolete work without changing serialized items.
- Selected object, terrain and item coordinates apply only on Enter (including
  keypad Enter). Escape discards the active draft. Nudge buttons still apply
  immediately; Quick Place and Move Entire Structure retain their action buttons.
- Optional **Vertical Layers 0.4.10** caches its floor view instead of rebuilding
  it on every GUI event, and retains all floors in save/undo operations.
- No game-save, JSON format or ModdingTools changes are required.

## Editor performance (1.2.22)

- Cache sprite geometry/UVs and query only visible sprite extents, preserving
  native layer order, rotated sprites, multi-tile furniture and overhangs.
- Use a separate placement index including native child-collider cells. Painting
  and deletion update that index without scanning every object's footprint.
- Keep undo snapshots in memory instead of serializing/parsing JSON after each
  edit. Group continuous text entry, retain stroke-level undo, and keep the
  existing 32-entry/32 MiB history limits. Saved JSON format is unchanged.
- Zone/sign/planning edits no longer rebuild the sprite list. Cache visible
  planning/zone overlays, item models, prefab metadata and text layout; render
  only visible file-list rows. Drop item-record caches after deletion and all
  preview caches on both normal and framework-forced editor close.
- No changes to import rules, saves or ModdingTools are required. Very large
  fully zoomed-out previews still cost more to draw than small visible areas.

## Shared market crates and mod-terrain imports (1.2.21)

- Additional native market crates now work without duplicate `market` identity
  errors. Every loaded crate and bulletin board uses the same live price list.
  Each crate retains its own goods, unsold stock and native sales/mail behavior.
  All loaded crates sell against the current board before it changes once at the
  normal market time (midnight, or 7 AM with Mirel's native key condition).
- The shared board and last processed cycle are saved per save in the existing
  ModdingTools companion. Crate inventories remain in native game data. Loading
  an off-tile crate cannot replace the current board with its old prices; unloaded
  crates do not gain remote sales beyond native behavior.
- Direct imports now honor registered mod-prefab activation/hide-flag settings
  and initialization callbacks. This fixes invisible Terrain Construction roofed
  grass and other inactive-template terrain, while retaining wooden-rail support.
  Reimport affected structures after updating; missing grass is not automatically
  reconstructed from older saves. No source JSON or ModdingTools update is needed.
- Align the BepInEx-reported version with the DLL/project version.

## Item nudge controls (1.2.20)

- Selected items now show **Nudge** distance plus **Left / Right / Up / Down**
  below their X/Y coordinates in the Items tab. The distance is shared with
  Objects and defaults to 0.25 tiles. Loose items allow fractional movement;
  native snapping and **Ignore Placement Rules** work as they do for objects.
- Nudges update the preview immediately, support Undo/Redo, and are saved with
  the JSON. They do not apply pending name/quality edits or rewrite item data.

## Free placement and item-reader diagnostics (1.2.19)

- **Ignore Placement Rules**, above the 2D preview, permits fractional nudges
  (such as 0.25-tile potted-plant adjustments), exact mouse-point object placement,
  and placement through collider, wall-placement and submerged-terrain checks.
  It applies to Quick Place, manual coordinates, duplicate/move/rotate and Items
  furniture placement. It starts Off and resets when closing the editor.
- Turning it Off does not reposition existing objects. Native snapping/checks
  apply to future edits. Terrain stays on whole cells; exclusive ground/floor,
  wall/door/window and water-overlay slots still prevent duplicate tile layers.
  Invalid item data and item customization limits are never bypassed.
- This is an editor override, not a change to live gameplay rules or colliders.
  Free placement can obstruct paths or interactions. Save and import as usual;
  fractional object positions are stored directly in the JSON.
- Fix false `stored but never retrieved` console errors during item preview and
  editing. The editor reads native field envelopes without scheduling the game's
  getter-based audit, preserving the native format and untouched fields. Unknown
  field types, malformed blobs and incompatible item components still fail safely;
  no global error suppression or game serializer patch is installed.

## Water export preference and zone-color display (1.2.18)

- **Export Other Water Tiles** controls whether non-bath water is collected by
  **Structures Export**, including bounded terrain and ground under objects.
  It defaults to Off. Bathhouse water is always eligible for export.
- **Imports always include all water present in the JSON.** Immediate imports,
  queued imports, validation and ground replacement no longer read a water
  preference. The previous On/Off value migrates to the new export setting;
  its old import key is removed. Editor JSON saves keep deliberately placed water.
- Reimport an affected source JSON to recover water skipped by older versions.
  Missing records in an already-filtered queued import cannot be reconstructed
  automatically. No saved world or source JSON is rewritten by this update.
- **Show Zone Colors**, beside the zone-paint controls in Quick Place/Terrain,
  toggles tinting of explicitly named terrain/floor zones (initially Off).
  Colors are stable by zone name and separate from planning colors, green object
  markers and world-transition warnings. This only changes the preview, not zone
  data or in-game lighting. Zone data is cached and drawing visits visible cells.

## Shallow-water overlays (1.2.17)

- `prefab_tile_water_shallow` and `prefab_tile_water_shallow_swamp` now share
  the water-overlay slot with bathhouse water. All three may sit above one
  ground/floor tile; only one water overlay is allowed per cell.
- Painting an overlay preserves the underlying floor and its zone. Painting
  a floor preserves the overlay. Moving/duplicating overlays still checks for
  collisions with other water overlays; painting replaces the existing overlay.
- An overlay imported without ground in its JSON keeps the destination ground.
  An explicit JSON floor replaces that ground without carrying a second floor.
  This applies to immediate and queued imports and old/raw tile records.
- All water present in a JSON imports, and bathhouse water retains its bathing
  effects. Ordinary/deep river, lake and swamp
  water remain in the ground slot. Native transparency and preview sorting are
  unchanged; no save migration or automatic re-import is performed.

## Items tab (1.2.16)

- Search the game's item library (including compatible registered mod items),
  configure an item and enable **Place copies**. Each left-click places one copy;
  right-click stops. Ordinary items use fractional, unsnapped coordinates at Z=1.
  Native 3D items offer eight 45-degree orientations. Furniture-items instead
  decode their stored prefab and use its native snapping, collision and rotation.
  Like native placement, furniture does not transfer inventory-only custom text
  or quality onto its placed prefab; edit the resulting object in **Objects**.
- Select a loose item in the preview to move it, edit it, delete it or place
  copies. **Apply item changes** commits the fields; save the JSON afterward.
  Preview uses native item icons/model previews, item sprite sizing and the
  `prefab_item` renderer's transform and sorting metadata, without live lighting.
- Custom text follows the installed game's `NPCFunction_GiveItem` LLM prompts:
  descriptions are limited to **30 words**, letters to **150 words**, with the
  corresponding plain-text grammars. These are word limits, not character caps.
  Counters and validation explain errors; existing text is preserved if unchanged.
  Material/furniture/gold identities retain their native restrictions. Gold
  amounts are 0–999; enchantments are limited to one on eligible equipment.
- Quality offers the complete native **Normal–Legendary** range, intentionally
  broader than the NPC gift-generation picker. Items without native quality
  support get a small quality-only item component, not weapon/food mechanics.
  Keep Structure Handler installed to load those custom-quality items.
- Item data uses native `WorldInventory`/`WorldItem` serialization, including
  export, duplicate, undo/redo, combined JSONs, queued imports and game saves.
  No new ModdingTools companion item data is needed. The original providing mods
  are still required for modded items. Unsupported/missing item components stop
  editor changes instead of silently discarding their data.
- As with other offline placement, player reach, ownership and inventory cost
  aren't enforced. No live inventory callbacks run while configuring items.

## Game-like preview and placement (1.2.15)

- The preview globally sorts individual sprite parts by the native sorting-layer
  priority, sorting order (including YSorter and chair/bench rotation offsets),
  material queue and Z depth. Prefab pivots, child offsets, rotation, scale and
  flipping are retained. FakeAO/live lighting remain excluded; animations and
  arbitrary modded shaders/scripts are not simulated by this offline preview.
- Ground and floors share one slot per cell. Walls, doors and windows share a
  separate wall slot. Painting replaces only the corresponding slot and supports
  Undo. Moving/duplicating into an occupied slot is rejected, not destructive.
- **Bathhouse water has its own overlay slot above the floor.** Painting floors
  keeps bath water, and painting bath water keeps floors and their zone data.
  Multiple bath-water overlays in one cell are not permitted. Bath water still
  imports with the other-water toggle off and keeps its native bathing effects.
  Importing bath water without a floor in the JSON carries the destination floor
  beneath it; importing both uses the JSON floor without adding an extra one.
- Placement consults native collider/passability, Submerge, Pickupable snapping
  and wall-placement flags. Floor-layer editing remains possible beneath existing
  objects. Player distance, ownership, inventory and construction time do not
  apply to offline editing. World-generation/custom-script placement is not
  emulated; serialized native furniture overlays remain importable.
- Existing/merged JSON slot conflicts remain visible and editable, with a cell
  warning. Saving or importing conflicted slots is blocked until corrected,
  including deferred imports. No source JSON, game save, or rollback snapshot is
  silently cleaned up. Multi-file import conflict selection is unchanged.

## Furniture turf collision (1.2.14)

- Select furniture in the editor's **Objects** tab and toggle **Turf collision**:
  **On** blocks movement; **Off** makes that furniture passable. Other objects
  or terrain at the same cell can still block passage.
- Save the JSON and import it to apply the change. The native `TurfCollider`
  component payload carries the setting through export, duplicate, undo/redo,
  queued imports, native game saves and furniture pickup/replacement. No new
  ModdingTools companion data is needed.
- The toggle uses existing native root colliders only. Missing, multiple, or
  unrecognized colliders are marked unavailable instead of injecting components.
  It does not disable interaction colliders, alter terrain, or edit prefab assets.

## Editor FakeAO shadow filtering (1.2.13)

- The editor excludes the game's `FakeAO` / `sprite_fakeao` shadow layers,
  including water-edge variants and renderers nested under FakeAO containers.
  These overlays previously escaped the preview's lighting-effect filter.
- Actual door frames, windows, furniture and terrain sprites remain visible.
  Shading painted into the artwork and the optional footprint overlay remain.
- This changes only the editor preview. Prefabs, in-game lighting, imports,
  saved appearances and sprite-variant indices are unchanged.

## Tile shading refresh after imports (1.2.12)

- Imports call ModdingTools' `ModTileVisuals.RequestRefresh` with the exact
  imported turf cells. Grass edging and water-edge shading are rebuilt on those
  cells and their eight immediate neighbors so borders blend with the destination.
- The framework coalesces overlapping requests and waits two frames for native
  initialization and deferred destruction. Distant queued imports request the
  refresh when their destination is loaded and the import actually runs.
- Rollback requests the same refresh for restored terrain. Visual-service failures
  appear in Framework Status without retrying an already committed import.
- Supplemental terrain restoration and duplicate-terrain repair also refresh
  affected cells after their transaction, including rollback.
- This is a visual refresh only: neighboring terrain, collision, ownership and
  save data are not replaced. No automatic re-import or save migration is added.
  The earlier feature notes below describe their original dependency requirements;
  this version needs ModdingTools 1.12.0 for the new API.

## Furniture pickup on player-buildable tiles (1.2.11)

- The **Build / Sleep / Pickup** toggle also allows the normal **Pick Up Furniture**
  action on pickup-capable furniture in that world tile, even if its native
  `owned` flag is set. Existing marked tiles work without re-importing.
- Pickup highlighting uses the same permission, showing eligible furniture green.
  The check uses the furniture's coordinates, including across world-tile borders.
- Pickup mode, the normal interaction distance, inventory capacity, furniture
  contents, and the game's pickup/placement code are unchanged. Objects without
  native pickup support do not become pickupable.
- The tile toggle does not erase furniture ownership. Turning it off restores
  normal restrictions for furniture still in place. No new save data or
  ModdingTools update is needed.

## Sleeping on player-buildable tiles (1.2.10)

- The per-save player-buildable toggle also grants sleeping permission. A marked
  world tile grants player bed-sleeping permission, including in the wilderness
  and for imported beds that retain an NPC owner. Existing marked tiles work
  automatically; turning the flag off restores normal ownership rules.
- This checks the bed's location, not the adjacent cell the player stands on.
  It does not transfer bed ownership or change NPC routines, rental hours,
  relationship values, or world-generation settings.
- Normal non-tent beds still need a roof, and the cold restriction still applies.
  Ground sleeping keeps its normal submerged/cold checks and no-sanity-recovery
  rule. No save migration or ModdingTools update is needed.

## Zone paint brush (1.2.9)

- In the editor's **Terrain** tab, enter a **Zone** name and enable **Paint Zones**.
  The same controls are available in **Quick Place → Terrain / Structural**.
- Left-click and drag across existing terrain/floor cells to rename their zones.
  All terrain/floor layers in each painted cell are updated; furniture, walls,
  appearance, coordinates, and component data are left unchanged. Empty cells
  are skipped, and a blank zone name explicitly clears the name.
- Fast drags fill intervening cells. Right-click cancels; middle-drag pans.
  Each stroke is one Undo action. Save the JSON to retain the changes and import
  it normally to apply them in-game. No live save or world terrain is edited by
  this brush, and no ModdingTools update is required.

## Native grass edging repair correction (1.2.8)

- Normal seasonal grass edging no longer makes duplicate grass ground
  unrepairable. The repair verifies that the native `OvergrowthTile` owns each
  edging child and that it has only its normal visual components. An edging
  prefab placed independently or an unknown/custom child is not exempted.
- Verified edging is not counted as an additional ground layer. Releasing an
  excess grass tile also invokes the game's normal cleanup for its own edging;
  the retained grass and its edging remain intact.
- Skipped cells now show specific safety reasons in the repair dialog and
  coordinate report. Confirmation, backups, and protection of real mixed terrain,
  named zones, custom components, plants, and furniture remain unchanged.

## Duplicate terrain prevention and repair (1.2.7)

- Native saves now exclude **all unused, inactive objects actually in the game's
  reuse pools**, regardless of their old coordinates. Ordinary loading can leave
  spare grass/trees outside an import's footprint. This replaces the narrower
  filtering in 1.2.4/1.2.5; active objects and inactive non-pool content remain
  saveable. It does not clear surrounding terrain or expand import boundaries.
- A read-only check after loading/visiting a world tile notifies you when stacked
  grass ground is detected. No terrain is removed automatically.
- Open **Mods > Structure Handler > Repair Duplicate Terrain**. The confirmation
  shows removable copies, affected loaded cells, and ambiguous cells to skip.
  Confirm **Back Up and Repair** to keep one existing plain grass ground tile
  per safe cell. Its current appearance is retained; an older duplicate's original
  appearance cannot always be identified. Coordinates outside imports are included.
- Repair is intentionally limited to native, unmodified, unnamed grass ground at
  grid coordinates and Z=1. Mixed floor/water/dirt layers, different depths, named
  zones, unregistered/modified grass, and unknown component state are skipped.
  Furniture, plants, trees, and other terrain types are never removed by repair.
- A normal save must exist and mod-save warnings must be resolved first. Before
  removal, the plugin preserves the last disk save, its metadata/companion, and
  creates a **fresh pre-repair checkpoint including unsaved progress**. If backup
  creation fails, no repair runs. It rechecks the proposed objects before removal
  and attempts rollback if removal fails.
- Backups and a per-coordinate repair/skip report are stored under
  `%USERPROFILE%/AppData/LocalLow/Three Eyes Software/Silverpine/Saves/StructureHandler Repair Backups/`.
  Each uniquely named directory contains `RESTORE.txt`, the `original.sav` pair,
  and the `before-repair.sav` checkpoint pair. These are not normal save slots.
  Restore matching native/companion pairs with the game closed; never mix pairs.
- **Save normally after repair**, preferably to a new slot. The plugin does not
  overwrite the source save when repairing. Re-running repair is harmless when
  no redundant grass remains. Unloaded wilderness tiles are checked when visited.

The historical notes below describe older fixes; the pool and repair behavior
above supersedes their coordinate-only filtering and reimport-only advice.

## Destination support for editor objects (1.2.6)

- Imports now carry over the actual destination terrain at occupied cells where
  the JSON supplies no replacement terrain. This fixes standalone editor wells
  and the same gap for furniture, walls/doors, crops, herbs, and ores. Trees and
  other obstructing objects are still cleared; only terrain is carried over.
- This covers every occupied cell, including multi-cell objects and queued
  imports when their destination is loaded. Explicit imported ground wins;
  bath/shallow-water overlays can retain ground beneath them.
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
- Terrain inside custom bounds is exported explicitly, including natural ground
  and constructed floors. **Export Other Water Tiles** in the in-game Mods menu
  controls non-bath water and shallow-water inclusion; it defaults to Off.
- Bathhouse water is always eligible for export. All water already present in
  a JSON is imported regardless of the export preference.
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
  The **Build / Sleep / Pickup** label reflects that the same flag also grants
  bed-sleeping and furniture-pickup permission there; other native restrictions remain.
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
`Tests/EditorInputSmoke` exercises the production text-field helpers against a
headless event surface, including Enter submission, selection drags, mouse-up
recovery and control-ID changes. `Tests/ItemsMono` runs native serializer and
TextEditor compatibility checks in a disposable copy of the game's Mono runtime;
see its README for invocation. Neither suite substitutes for live UI testing.

Build with `dotnet build -c Release`, then copy `StructureHandler.dll` from the
release output folder into `BepInEx/plugins/StructureHandler/`.

## Credits

Created by **Saelac and ChatGPT**.
