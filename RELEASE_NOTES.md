# Structure Handler 1.2.26

Created by **Saelac and ChatGPT**.

This is a cumulative release from the previously published **1.2.12**. It includes
the editor performance/input fixes, Items tab, placement controls, water and zone
changes, shared market crates, and mod-prefab terrain fixes described below.

## Requirements and installation

- Silverpine 1.7.3 and BepInEx 5.
- **[ModdingTools 1.12.0 or newer](https://github.com/Silverpine-Modding/Silverpine-Saelac-Modding-Tools/releases)** must be installed separately. This requirement is enforced by the plugin.
- Close the game. Extract `StructureHandler-1.2.26.zip` into
  `BepInEx/plugins/StructureHandler/`, replacing the previous DLL, or replace
  that DLL with the standalone download. Do not keep a second copy elsewhere
  under `BepInEx/plugins`.
- Back up your saves and structure JSONs before updating. Keep each save's
  `.sav.moddingtools` companion alongside its `.sav` file when copying or
  restoring saves. This download does not contain or update ModdingTools.

## New in 1.2.26

- Fix stranded mouse capture after text-selection drags. Release it on mouse-up
  even outside/missing fields, and recover missed releases before the next click.
- Preserve text selection, scroll and drag ownership when virtualized controls
  change a text field's ID; allocate its ID independently of the layout rectangle.
- Clicking away unfocuses the field without eating the target click. Dragging
  across the preview cannot paint/pan it, and cleanup leaves other controls alone.
- Enter-only coordinate edits and the full-field selection fixes are retained.
  No save, JSON, ModdingTools or Vertical Layers update is needed.

## Retained from 1.2.25

- Fix coordinate submission to use the field's actual keyboard-focus ID and
  intercept Enter/keypad Enter before Unity's text control handles the key.
  The previous pre-draw focus-name lookup could leave the draft unapplied.
- Preserve Enter-only edits, Escape cancellation, full-field text selection,
  placement validation and native snapping. Report unchanged/snapped moves.
- No JSON migration, save repair or companion-plugin update is needed.

## Retained from 1.2.24

- Use namespaced text-control IDs for coordinates, prefab names and other
  single-line fields. Ctrl+A and double-click select the entire value rather
  than a decimal/underscore-separated segment.
- Keep native drag/keyboard/clipboard editing and stop the preview from taking
  over text drags. Long prefab names scroll within the field. Enter-to-apply
  coordinate behavior is unchanged; no other plugin update is needed.

## Retained from 1.2.23

- Cache the composed editor preview as one resolution-scaled image on unchanged
  frames. Invalidate it for edits, selection, overlays, floor changes and pan/zoom;
  preserve native sprite sorting, transparency, rotation and viewport clipping.
  Release GPU resources on close and retain direct rendering as a fallback.
- Spread loose-item/model previews across frames with a 2 ms target and a maximum
  of four jobs per frame. Individual native renders cannot be interrupted.
- Apply selected object, terrain and item coordinate text edits only on Enter
  or keypad Enter; Escape cancels the active draft. Nudge buttons remain immediate.
- Pair with optional Vertical Layers 0.4.10 to reuse the floor-filtered document
  between GUI events while saving/undoing the complete physical document.
- No JSON migration, save repair or ModdingTools update is necessary.

## Retained from 1.2.22

- Optimize the editor's steady-state rendering with cached sprite geometry/UVs,
  visibility indexes that include sprite overhangs, and an axis-aligned draw
  fast path. Preserve native sprite-part sorting, water transparency and rotation.
- Index placement candidates by occupied and native child-collider cells;
  incrementally update the index for painting, movement and quick deletion.
- Replace JSON undo snapshots with isolated in-memory copies and coalesce text
  entry. Preserve every model field, component payload, stroke undo and the
  existing history limits; normal JSON saving is unchanged.
- Avoid visual rebuilds for zone, sign and planning changes. Cache visible
  overlays, item previews/model checks, prefab metadata and sign text layout.
  Virtualize the structure file list as well as the existing object/catalog lists.
- Prune deleted item records and release preview caches on framework-forced
  close as well as normal close. No ModdingTools update or save repair needed.

## Retained from 1.2.21

- Support multiple functioning market crates without duplicate `market` keys.
  Share exactly one board across all crates, sell their independent stock using
  that board, then change prices once per native market cycle. Keep native mail,
  sale chances and the midnight/7 AM schedule.
- Persist shared prices and the cycle stamp per save; unloaded crates cannot
  overwrite the current board with stale prices when revisited. Stock remains
  native-saved. No new remote/off-tile sales simulation is added.
- Preserve the board when another crate is destroyed or unloaded, and promote
  a remaining crate if the registered one disappears.
- Fix missing roofed greenhouse grass from Terrain Construction: direct imports
  now activate registered mod-prefab clones according to their registration
  settings before restoring component data. This covers terrain and objects,
  including rollback, without activating templates or disabled child objects.
- Reimport affected JSONs after updating to restore previously missing terrain.
  No JSON edits or ModdingTools update required. Correct the displayed plugin version.

## Retained from 1.2.20

- Add **Left / Right / Up / Down** nudge buttons beneath selected items' X/Y
  coordinates. Items and Objects share the editable distance (default 0.25),
  placement-rule toggle and undoable movement path.
- Nudging does not rewrite item payloads or commit pending customization edits.
  Save the JSON to keep the new position; no ModdingTools update is required.

## Retained from 1.2.19

- Add **Ignore Placement Rules** above the editor preview. When enabled, object
  placement, movement and fractional nudges bypass native snapping, collider,
  wall-placement and submerged-terrain restrictions. Quick Place and Items
  furniture use the exact clicked point. Default Off; resets on editor close.
- Keep one ground/floor, one wall/door/window and one water overlay per cell.
  Terrain remains grid-aligned. Existing positions are not moved when disabling
  the override, and item customization/data validation remain enabled.
- Fix false item/inventory `stored but never retrieved` messages with an
  editor-local native-envelope reader. Preserve all native field types and
  untouched fields; reject malformed/unknown data. Actual item-component reads
  and gameplay error reporting remain unchanged.
- No ModdingTools update, JSON migration or save-data rewrite is required.

## Retained from 1.2.18

- Correct the water toggle to **Export Other Water Tiles**. It filters non-bath
  water only while collecting a world export, including water beneath objects
  and inside custom bounds. Bathhouse water remains eligible regardless.
- Import every water record in the JSON, including queued imports, without
  skipping its validation, footprint or destination-ground replacement.
- Migrate the old toggle's On/Off value to the new export setting and remove its
  obsolete import key. Fresh installations default the export option to Off.
- Keep editor saves and existing JSONs intact. Previously skipped water requires
  reimporting its source JSON; no automatic world/save repair is attempted.
- Add **Show Zone Colors** beside the zone-paint controls in Quick Place/Terrain.
  Toggle named floor/terrain tints independently of other overlays, without
  modifying zone data. Colors are stable and preview work is revision-cached.

## Retained from 1.2.17

- Treat shallow water and shallow swamp water as translucent overlays alongside
  bathhouse water, allowing one floor/ground tile beneath them.
- Preserve floors and zone names when painting water; preserve water when
  painting floors. Different overlay types replace/conflict with one another
  instead of stacking into multiple water layers.
- Keep destination ground when an imported overlay has no explicit floor.
  Explicit floors replace the destination ground without adding a second floor,
  including queued imports and legacy tile records.
- Keep native transparency, bath effects and ordinary/deep-water ground-slot
  behavior. Version 1.2.18 moves the water toggle to export only.

## Retained from 1.2.16

- Add an **Items** tab with searchable native/registered item templates, unsnapped
  click-to-place copies, native 3D rotation, selection, movement and deletion.
- Furniture-items use their encoded furniture prefab and native spatial rules.
- Customize names/descriptions, full Normal–Legendary quality, eligible
  enchantments, letters and gold. Enforce the game's LLM text grammars and limits:
  30-word descriptions, 150-word letters and 0–999 gold, without truncating old data.
- Save item state in native inventory/world-item components. No new companion
  item data is needed; quality-only custom components require Structure Handler.
- Render actual item previews; preserve item data through native saves and
  pickup. Empty raw `prefab_item` placement is replaced by the populated Items tab.

## Retained from 1.2.15

- Match native sprite layering globally, including Y-sort/rotation offsets,
  Z depth, prefab pivots and transformed multipart sprites. FakeAO stays hidden.
- Enforce one ground/floor and one wall/door/window per cell. Painting replaces
  its slot with Undo; moving and duplicating cannot create occupied-slot stacks.
- Keep bathhouse water as a distinct overlay, allowing a floor beneath it and
  preserving the normal bath-water import behavior and sanity-regeneration data.
- Use native spatial placement metadata for blockers, water, snapping and
  wall-compatible furniture without requiring a live player in the editor.
- Report pre-existing or merged JSON slot conflicts instead of silently deleting
  records. Block conflicted saves and immediate/deferred imports until corrected.

## Retained from 1.2.14

- Add a **Turf collision** toggle to selected furniture in the editor's Objects
  tab. On blocks movement; Off makes that furniture passable. Save/import to apply.
- Store the setting in the existing native TurfCollider payload, so native saves
  and pickup/replacement retain it without new companion save data or runtime hooks.
- Preserve all other component data. Unsupported or malformed collider layouts
  are marked unavailable rather than adding/removing native components.

## Retained from 1.2.13

- Hide native FakeAO shadow overlays in the structure editor, including water
  edge variants, renamed renderers using FakeAO sprites and nested containers.
- Keep normal multipart door/window/furniture sprites. No prefab renderers or
  materials are modified, and in-game lighting and saved data are unchanged.
- Keep sprite-variant indices intact; a FakeAO variant is skipped only in the
  preview. No save migration, re-import or ModdingTools update is required.

## Retained from 1.2.12

- Refreshes supplemental terrain restoration and duplicate-terrain repair,
  including restored terrain after a rollback.
- Imports now use the ModdingTools tile-refresh API to rebuild native grass
  edging and water-edge shading on imported cells and their immediate neighbors.
  Neighboring terrain is not replaced or added to the import footprint.
- Refreshes are batched and deferred until native tile initialization settles.
  Queued imports refresh when applied at their destination, not when queued.
- Failed imports also refresh after rollback. A visual-service failure is
  reported in Framework Status without re-queuing a successful import.
- Requires ModdingTools 1.12.0. No save migration or automatic re-import is added.
  Earlier notes below retain the dependency requirements of their original releases.

## Retained from 1.2.11

- Player-buildable world tiles permit the normal **Pick Up Furniture** action on
  pickup-capable furniture, including objects retaining the native owned flag.
  The tile toggle is labeled **Build / Sleep / Pickup**.
- The pickup highlight and click permission both use the furniture's location.
  Pickup distance, inventory capacity, contents, and native item creation and
  placement are unchanged; no pickup components are added to other objects.
- Existing marked tiles work immediately. Disabling the flag restores normal
  restrictions for furniture still in place without erasing ownership data.
  No save migration, re-import or ModdingTools update is required.

## Retained from 1.2.10

- Marking a world tile player buildable also grants player bed-sleeping permission
  there, including wilderness and NPC-owned imported beds. This continues using
  the existing per-save flag, now labeled **Build / Sleep / Pickup**.
- Permission is evaluated at the bed's coordinates. Turning the flag off restores
  normal ownership checks without changing the bed's saved owner, NPC routines,
  relationship values, or rental hours.
- Roof and cold restrictions remain intact; ground sleeping is unchanged.
  No save migration or ModdingTools update is required.

## Retained from 1.2.9

- **Paint Zones** in the editor's Terrain tab and Quick Place's Terrain/Structural
  categories renames existing terrain/floor zones with left-click and drag.
- Uses the Zone text field without replacing tiles or changing their appearance.
  Empty cells and non-terrain objects are untouched. Blank names clear a zone name.
- Fast drags fill skipped cells; right-click cancels and middle-drag pans. A whole
  stroke is one Undo action. Changes are saved in the structure JSON for import.
- No save-format migration or ModdingTools update is needed.

## Retained from 1.2.8

- Fix false "ambiguous" results for duplicate grass beside terrain with native
  seasonal overgrowth. Only edging actually owned by the native grass component,
  with the expected visual-only component set and child hierarchy, is accepted.
  Independently placed edging and unknown/mod-added children remain protected.
- Verified edging no longer counts as a separate terrain layer. The game's
  normal release cleanup removes only an excess grass copy's owned edging.
- The repair dialog and coordinate report now explain why any cells are skipped.
  Backups and confirmation remain required. No save is automatically repaired,
  and no ModdingTools update is needed.

## Retained from 1.2.7

- **Prevent neighboring grass duplication on save/load:** exclude unused,
  inactive reuse-pool entries from native saves regardless of stale coordinates.
  The filter no longer relies on an import footprint or instance-discard markers.
  It does not delete live surroundings, intentionally inactive non-pool content,
  or change import replacement bounds.
- **Repair Duplicate Terrain:** new in-game Structure Handler action previews
  the number of redundant grass copies and affected loaded cells, then asks for
  confirmation. It keeps one existing plain grass tile per safe cell. Mixed
  terrain layers, named zones, nonstandard depth, and modified/unknown state are
  skipped; furniture, vegetation, trees, and other terrain types are not removed.
- Existing saves receive a read-only detection notice, not automatic deletion.
  Repair requires a normal save and a successful disk backup plus fresh checkpoint
  (including unsaved progress). Backups and coordinate reports are kept in
  `Saves/StructureHandler Repair Backups/`, with restoration instructions.
- Save normally after repair to retain the correction. No original save is
  overwritten by the repair action. No ModdingTools update is required.

## Retained from 1.2.6

- **Ground underneath editor objects:** imports carry over the destination's
  actual terrain wherever the JSON supplies no replacement terrain. This fixes
  standalone wells and the same issue for other furniture, walls/doors,
  crops, herbs, and ores, including multi-cell footprints. Trees and obstructing
  objects are still cleared; explicit imported terrain takes precedence.
- Carried terrain retains its room name, appearance, component state, and
  existing floor-deconstruction history. It follows the import's rollback and
  native/supplemental save paths. Queued imports capture it when the destination
  is loaded; portable JSONs are not rewritten with destination terrain.
- **Well reimport stacking:** replacement now recognizes matching incoming
  prefabs and previously imported supplemental objects even without native
  serialization. Character and persistent-object exclusions remain in place.

## Earlier fixes retained

- Save registrations and gameplay hooks survive destruction of Silverpine's
  initial plugin host during bootstrap.
- Terrain and scenery not covered by native serialization receive per-save
  supplemental records. Cleared nonserialized originals are tracked so they
  do not return over imported structures. Live edits and removals update these
  records instead of replaying historical exports.
- Unused pooled trees/grass are filtered out of native saves, now without a
  coordinate restriction. Active objects are not blacklisted. Exact imported-cell footprints persist
  per save and are cleared when their world tile is intentionally regenerated.
- Global tile-protection/buildability configuration is no longer copied into
  unrelated saves. Existing per-save choices remain intact.
- Ordinary sprite variation is stored in native component state where supported.
  Native seasonal variation and rotation retain their native saving behavior.
  Explicit fixed-sprite choices and unsupported component overrides retain
  companion support; prefab-default interaction extenders need no extra record.
- Imported floors capture their destination's underlying ground for later
  deconstruction. Replacing an existing floor inherits its original ground,
  not the previous floor. New exports omit source-specific ground history.
  Fresh ground restored by deconstruction remains saveable, including plain dirt.

## Important upgrade notes

- This update does not automatically reimport structures or guess which active
  trees are unwanted. Use **Repair Duplicate Terrain** for redundant plain grass
  ground. If a cell is skipped as ambiguous, inspect it manually; do not assume
  all stacked objects are redundant. Repair is limited to currently loaded areas.
- Already-missing ground cannot be recovered from an empty destination. Use a
  save from before the damaged import, or add the intended supporting terrain
  in the editor before reimporting.
- Existing floors are not retroactively assigned different underlying terrain
  merely by loading the save. If a newly imported floor's restorable destination
  ground cannot be identified, that import section stops before replacement
  instead of inventing default grass.
- No saves or structure JSONs are rewritten merely by installing the update.

## Verification and contents

- Release build: zero warnings and errors.
- **219 Unity-independent regression tests**, **57 headless editor-input checks**,
  **38 native serialization/permission checks**, **56 Unity-Mono checks**, and
  the compiled hook/lifecycle checks pass.
- The optional Vertical Layers 0.4.10 companion is published separately for
  floor-view caching and complete-document save/undo support.
- Real gameplay, rendering, mouse interaction and complete save/load behavior
  still need in-game verification; automated checks do not substitute for it.
- Downloads contain only the plugin and documentation, without proprietary
  game assemblies, dependencies, configuration, save data, or debug symbols.

See the included README for detailed behavior, backup restoration, and build instructions.
