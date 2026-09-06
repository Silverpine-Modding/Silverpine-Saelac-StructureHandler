# Structure Handler 1.2.6

Created by **Saelac and ChatGPT**.

## Requirements and installation

- Silverpine 1.7.3 and BepInEx 5.
- **[ModdingTools 1.10.0 or newer](https://github.com/Silverpine-Modding/Silverpine-Saelac-Modding-Tools/releases)** must be installed separately. This requirement is enforced by the plugin.
- Close the game. Extract `StructureHandler-1.2.6.zip` into
  `BepInEx/plugins/StructureHandler/`, replacing the previous DLL, or replace
  that DLL with the standalone download. Do not keep a second copy elsewhere
  under `BepInEx/plugins`.
- Back up your saves and structure JSONs before updating. Keep each save's
  `.sav.moddingtools` companion alongside its `.sav` file when copying or
  restoring saves. This download does not contain or update ModdingTools.

## New in 1.2.6

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

## Also included since the previous GitHub release (1.2.2)

- Save registrations and gameplay hooks survive destruction of Silverpine's
  initial plugin host during bootstrap.
- Terrain and scenery not covered by native serialization receive per-save
  supplemental records. Cleared nonserialized originals are tracked so they
  do not return over imported structures. Live edits and removals update these
  records instead of replaying historical exports.
- Unused pooled trees/grass discarded by imports, including older inactive
  pool entries at imported coordinates, are filtered out of native saves.
  Active objects are not blacklisted. Exact imported-cell footprints persist
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
  trees are unwanted. Older saves lack reliable import footprints. To correct
  affected areas, back up first, reimport only the affected structures once,
  save to a **new slot**, then reload and check the result. Reimport replaces
  objects and their contents/state inside its footprint.
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
- All **97 Unity-independent regression tests** pass.
- All **7 native serialization checks** and **6 compiled hook/lifecycle checks**
  pass. Real gameplay, rendering, and complete save/load behavior still need
  in-game verification; automated checks do not substitute for that test.
- Downloads contain only the plugin and documentation, without proprietary
  game assemblies, dependencies, configuration, save data, or debug symbols.

See the [README](https://github.com/Silverpine-Modding/Silverpine-Saelac-StructureHandler/blob/v1.2.6/README.md) for detailed behavior and build instructions.
