# Structure Handler 1.2.2

Created by **Saelac and ChatGPT**.

## Requirements and installation

- Silverpine 1.7.3 and BepInEx 5.
- **[ModdingTools 1.10.0 or newer](https://github.com/Silverpine-Modding/Silverpine-Saelac-Modding-Tools/releases)** must be installed separately. This requirement is enforced by the plugin.
- Close the game. Extract `StructureHandler-1.2.2.zip` into
  `BepInEx/plugins/StructureHandler/`, replacing the previous DLL, or replace
  that DLL with the standalone download. Do not keep a second copy elsewhere
  under `BepInEx/plugins`.
- Back up your saves before updating. Keep each save's `.moddingtools`
  companion file alongside it when copying or restoring saves.

## New in 1.2.2

- Fixed wooden bridge railing discovery by loading the actual asset from
  `MiscPrefabs` and registering a separate persistent, serializable template.
  Find **wooden bridge railing** under **Quick Place > Structural** by searching
  `bridge` or `railing`. Imported copies can be saved and exported again; native
  bridge scenery is not modified.
- Blue filled/outlined cells show normal world-transition positions on every
  100x100 world-tile boundary, even without transition objects in the JSON.
  An overlap count warns when the structure covers those coordinates.
- Added a **Plan** tab with eight palette colors, custom `#RRGGBB` colors,
  drag painting, an eraser, visibility toggle, clear-all, and undo/redo.
  Planning marks remain readable `planningTiles` JSON entries, follow whole-
  structure moves, and combine across documents. They are never imported into
  the world or counted as occupied terrain.

## Also included since the previous GitHub release (1.1.10)

- **Structures Import** now opens a checkbox file picker instead of importing
  every JSON. Only selected files participate in validation and conflict prompts.
- Import preflight validation, transactional recovery, and queued imports for
  unloaded world tiles improve reliability and prevent off-tile stacking.
- Tile protection, player-buildable settings, herb/ore regeneration markers,
  pending imports, and appearance overrides are stored per save. Legacy data
  migrates without deleting the original files.
- Improved sprite/extender restoration, terrain normalization, and JSON writes
  with recoverable `.json.bak` backups.
- Editor list virtualization and cached rendering/selection data reduce repeated
  work. Mouse-wheel scrolling/zoom, bounded undo history, and unsaved-change
  prompts improve editing.
- Split the plugin into focused source files and added a regression-test project.

## Verification and contents

- Release build: zero warnings and errors.
- All **47 Unity-independent regression tests** pass.
- Unity rendering and real gameplay/save-load behavior still need in-game smoke
  testing; the test suite does not substitute for that check.
- Downloads contain this plugin only, without proprietary game assemblies,
  configuration, save data, credentials, or debug symbols.

See the [README](https://github.com/Silverpine-Modding/Silverpine-Saelac-StructureHandler/blob/v1.2.2/README.md) for detailed behavior and build instructions.
