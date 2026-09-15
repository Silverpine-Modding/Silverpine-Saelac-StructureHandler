# Structure Handler 1.2.8

Created by **Saelac and ChatGPT**.

## Requirements and installation

- Silverpine 1.7.3 and BepInEx 5.
- **[ModdingTools 1.10.0 or newer](https://github.com/Silverpine-Modding/Silverpine-Saelac-Modding-Tools/releases)** must be installed separately. This requirement is enforced by the plugin.
- Close the game. Extract `StructureHandler-1.2.8.zip` into
  `BepInEx/plugins/StructureHandler/`, replacing the previous DLL, or replace
  that DLL with the standalone download. Do not keep a second copy elsewhere
  under `BepInEx/plugins`.
- Back up your saves and structure JSONs before updating. Keep each save's
  `.sav.moddingtools` companion alongside its `.sav` file when copying or
  restoring saves. This download does not contain or update ModdingTools.

## New in 1.2.8

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
- All **115 Unity-independent regression tests** pass.
- All **7 native serialization checks** and **9 compiled hook/lifecycle checks**
  pass. Real gameplay, rendering, and complete save/load behavior still need
  in-game verification; automated checks do not substitute for that test.
- Downloads contain only the plugin and documentation, without proprietary
  game assemblies, dependencies, configuration, save data, or debug symbols.

See the included README for detailed behavior, backup restoration, and build instructions.
