#nullable enable

using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace StructureHandler;

internal static class StructureTransfer
{
    private static readonly HashSet<string> BaseShedObjects = new();
    private static Vector2Int? ExportTopLeft;
    private static Vector2Int? ExportBottomRight;
    private static bool HasCompleteExportBounds =>
        ExportTopLeft.HasValue && ExportBottomRight.HasValue;

    internal static string TopLeftButtonLabel =>
        ExportTopLeft.HasValue
            ? $"Top Left: {ExportTopLeft.Value.x}, {ExportTopLeft.Value.y}"
            : "Select Export Top Left";

    internal static string BottomRightButtonLabel =>
        ExportBottomRight.HasValue
            ? $"Bottom Right: {ExportBottomRight.Value.x}, {ExportBottomRight.Value.y}"
            : "Select Export Bottom Right";

    internal static void ToggleTopLeft(Action<string> updateLabel)
    {
        ConfigureBound(topLeft: true, updateLabel);
    }

    internal static void ToggleBottomRight(Action<string> updateLabel)
    {
        ConfigureBound(topLeft: false, updateLabel);
    }

    private static void ConfigureBound(
        bool topLeft,
        Action<string> updateLabel)
    {
        Vector2Int playerPosition =
            Player.Instance.transform.GetVector2IntPosition();
        PauseMenuManager.Instance.Close();

        List<DialogOption> options = new()
        {
            new DialogOption("Use Player Position", () =>
                SetBound(topLeft, playerPosition, updateLabel)),
            new DialogOption("Click World Tile", () =>
                Plugin.Instance.SelectWorldTile(
                    topLeft ? "top-left" : "bottom-right",
                    position => SetBound(topLeft, position, updateLabel))),
            new DialogOption("Type Coordinates", () =>
                TextInputUI.Instance.Open(
                    topLeft
                        ? "Top Left Coordinates (x, y)"
                        : "Bottom Right Coordinates (x, y)",
                    IsValidCoordinates,
                    input =>
                    {
                        SetBound(
                            topLeft, ParseCoordinates(input), updateLabel);
                    }))
        };

        if ((topLeft && ExportTopLeft.HasValue) ||
            (!topLeft && ExportBottomRight.HasValue))
        {
            options.Add(new DialogOption("Clear", () =>
            {
                if (topLeft)
                    ExportTopLeft = null;
                else
                    ExportBottomRight = null;

                updateLabel(topLeft
                    ? TopLeftButtonLabel
                    : BottomRightButtonLabel);
                UpperNotificationUI.Instance.OneOff(
                    topLeft
                        ? "Export top-left bound cleared."
                        : "Export bottom-right bound cleared.");
            }));
        }

        options.Add(new DialogOption("Cancel", null));
        DialogBox.Instance.DisplayTextNoDialog(
            topLeft
                ? "Choose the export top-left bound."
                : "Choose the export bottom-right bound.",
            options.ToArray());
    }

    private static IEnumerator ReturnToStructureControls()
    {
        // Let the text-input dialog finish releasing exclusive pause-menu mode.
        for (int frame = 0; frame < 300; frame++)
        {
            yield return null;
            if (Silverpine.ModdingTools.InventoryModTools.TryOpen(
                    Plugin.PluginGuid + ".game-controls"))
                yield break;
        }
        Plugin.Log.LogWarning(
            "Could not return to Structure Handler after coordinate entry.");
    }

    private static void SetBound(
        bool topLeft,
        Vector2Int position,
        Action<string> updateLabel)
    {
        if (topLeft)
            ExportTopLeft = position;
        else
            ExportBottomRight = position;

        updateLabel(topLeft
            ? TopLeftButtonLabel
            : BottomRightButtonLabel);
        UpperNotificationUI.Instance.OneOff(
            $"Export {(topLeft ? "top-left" : "bottom-right")} set to " +
            $"{position.x}, {position.y}.");
        Player.Instance.StartCoroutine(ReturnToStructureControls());
    }

    private static bool IsValidCoordinates(string input)
    {
        return TryParseCoordinates(input, out _);
    }

    private static Vector2Int ParseCoordinates(string input)
    {
        TryParseCoordinates(input, out Vector2Int position);
        return position;
    }

    private static bool TryParseCoordinates(
        string input,
        out Vector2Int position)
    {
        position = default;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        string[] parts = input.Split(
            new[] { ',', ' ' },
            StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out int x) ||
            !int.TryParse(parts[1], out int y))
            return false;

        position = new Vector2Int(x, y);
        return true;
    }

    internal static void CaptureBaseShed()
    {
        try
        {
            Plot shedPlot = Plot.allPlots.FirstOrDefault(
                plot => plot.owners.Contains(NPCName.None));
            if (shedPlot == null)
            {
                Plugin.Log.LogWarning("Could not find the base-game shed plot.");
                return;
            }

            foreach (GameObject gameObject in
                     UtilityFunctions.GetGameObjectsWithComponent<ISerializableMonoBehavior>())
            {
                if (gameObject != null &&
                    shedPlot.bounds.Contains(gameObject.transform.GetVector3IntPosition()))
                {
                    BaseShedObjects.Add(GetObjectSignature(gameObject));
                }
            }

        }
        catch (Exception exception)
        {
            Plugin.Log.LogError("Could not capture the base shed: " + exception);
        }
    }

    internal static void PromptExport()
    {
        PauseMenuManager.Instance.Close();
        TextInputUI.Instance.Open(
            "Structure Export Name",
            IsValidFileName,
            Export);
    }

    private static bool IsValidFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 80)
            return false;

        return value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    private static void Export(string requestedName)
    {
        try
        {
            if (!TryValidateExportBounds(out string boundsError))
            {
                DialogBox.Instance.DisplayTextNoDialog(boundsError);
                return;
            }

            Directory.CreateDirectory(Plugin.StructuresDirectory);
            string cleanName = requestedName.Trim();
            string path = Path.Combine(Plugin.StructuresDirectory, cleanName + ".json");

            List<GameObject> exportCandidates = FindPlayerStructures();
            List<GameObject> objects = exportCandidates
                .Where(gameObject => !IsTerrainObject(gameObject))
                .ToList();
            List<StructureObject> readableObjects = CreateObjectRecords(objects);
            List<SupportingTerrain> supportingTerrain =
                FindSupportingTerrain(readableObjects,
                    exportCandidates.Where(IsTerrainObject));
            if (objects.Count == 0 && supportingTerrain.Count == 0)
            {
                DialogBox.Instance.DisplayTextNoDialog(
                    "No player-built structures or placed furniture were found on an owned plot.");
                return;
            }

            IEnumerable<Vector2Int> allOccupiedPositions = readableObjects
                .SelectMany(StructureEditorUI.GetObjectCells)
                .Concat(supportingTerrain.Select(
                    terrain => new Vector2Int(terrain.x, terrain.y)));
            StructureFile file = new()
            {
                name = cleanName,
                gameVersion = Application.version,
                exportedUtc = DateTime.UtcNow.ToString("O"),
                occupiedPositions = allOccupiedPositions
                    .Distinct()
                    .OrderBy(p => p.x)
                    .ThenBy(p => p.y)
                    .Select(p => new StructurePosition { x = p.x, y = p.y })
                    .ToList(),
                supportingTerrain = supportingTerrain,
                objects = readableObjects,
                // Format 3 is fully readable. Keeping a second binary copy can
                // resurrect obsolete records after users edit the JSON.
                serializedObjectsBase64 = ""
            };

            Action write = () =>
            {
                try
                {
                    StructureJsonFile.Write(path, StringSerializationAPI.Serialize(typeof(StructureFile), file));
                    DialogBox.Instance.DisplayTextNoDialog(
                        $"Exported {objects.Count} structure/furniture objects and " +
                        $"{supportingTerrain.Count} terrain tiles to <i>\"{path}\"</i>.");
                }
                catch (Exception exception)
                {
                    Plugin.Log.LogError(exception);
                    DialogBox.Instance.DisplayTextNoDialog("Export failed: " + exception.Message.Truncate(250));
                }
            };

            if (File.Exists(path))
            {
                DialogBox.Instance.DisplayTextNoDialog(
                    $"A structure named <i>\"{cleanName}\"</i> already exists. Overwrite it?",
                    new DialogOption("Overwrite", write),
                    new DialogOption("Cancel", null));
            }
            else
            {
                write();
            }
        }
        catch (Exception exception)
        {
            Plugin.Log.LogError(exception);
            DialogBox.Instance.DisplayTextNoDialog(
                "Failed to export structures: " + exception.Message.Truncate(200));
        }
    }

    internal static void ImportSelected(IEnumerable<string> selectedPaths)
    {
        // Freeze the explicit selection; never re-enumerate the folder here.
        string[] paths = selectedPaths.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length == 0) return;
        PauseMenuManager.Instance.Close();
        PrepareImport(paths);
    }

    private static void PrepareImport(IEnumerable<string> paths)
    {
        try
        {
            List<StructureFile> files = paths.Select(ReadFile).ToList();
            ValidateImportFiles(files);
            // Build each footprint once, then count only pairs sharing cells.
            var owners = new Dictionary<Vector2Int, List<int>>();
            var counts = new Dictionary<(int, int), int>();
            for (int i = 0; i < files.Count; i++)
            foreach (var cell in GetOccupiedPositions(files[i]))
            {
                if (!owners.TryGetValue(cell, out var previous))
                    owners[cell] = previous = new List<int>();
                foreach (int other in previous)
                {
                    var pair = (other, i);
                    counts.TryGetValue(pair, out int count);
                    counts[pair] = count + 1;
                }
                previous.Add(i);
            }
            var conflicts = counts.OrderBy(pair => pair.Key.Item1).ThenBy(pair => pair.Key.Item2)
                .Select(pair => Tuple.Create(files[pair.Key.Item1], files[pair.Key.Item2], pair.Value))
                .ToList();
            ResolveConflict(files, conflicts, new List<(int Winner, int Loser)>(), 0);
        }
        catch (Exception exception)
        {
            Plugin.Log.LogError(exception);
            DialogBox.Instance.DisplayTextNoDialog("Failed to import structures: " + exception.Message.Truncate(250));
        }
    }

    private static void ResolveConflict(List<StructureFile> files,
        List<Tuple<StructureFile, StructureFile, int>> conflicts,
        List<(int Winner, int Loser)> choices, int conflictIndex, string explanation = "")
    {
        if (conflictIndex >= conflicts.Count)
        {
            if (!StructureAlgorithms.TryOrder(files.Count, choices, out var order))
                throw new InvalidDataException("Conflicting overwrite choices.");
            ImportAll(order.Select(index => files[index]).ToList());
            return;
        }
        var conflict = conflicts[conflictIndex];
        var first = conflict.Item1;
        var second = conflict.Item2;
        void Choose(StructureFile winner)
        {
            int win = files.IndexOf(winner);
            int lose = files.IndexOf(winner == first ? second : first);
            choices.Add((win, lose));
            if (!StructureAlgorithms.TryOrder(files.Count, choices, out _))
            {
                choices.RemoveAt(choices.Count - 1);
                ResolveConflict(files, conflicts, choices, conflictIndex,
                    "That choice contradicts earlier winners. Choose the other winner or cancel and restart.\n\n");
                return;
            }
            ResolveConflict(files, conflicts, choices, conflictIndex + 1);
        }
        DialogBox.Instance.DisplayTextNoDialog(
            explanation + $"Conflict {conflictIndex + 1} of {conflicts.Count}: " +
            $"{GetDisplayName(first)} and {GetDisplayName(second)} overlap at {conflict.Item3} position(s).\n\n" +
            "Which structure should overwrite the other?",
            new DialogOption(GetDisplayName(first) + " Wins", () => Choose(first)),
            new DialogOption(GetDisplayName(second) + " Wins", () => Choose(second)),
            new DialogOption("Cancel Import", null));
    }

    private static void ImportAll(List<StructureFile> files)
    {
        try
        {
            if (!StructureSaveState.TryCurrentTile(out var currentTile))
                throw new InvalidOperationException("Load a game before importing.");
            int applied = 0;
            int queued = 0;
            var plans = files.Select(file => StructurePlans.Partition(file, (x, y) =>
            {
                var tile = Plugin.GetWorldTile(new Vector2(x, y));
                return (tile.x, tile.y);
            }, ShouldImportTerrain)).ToList();
            foreach (var tile in plans.SelectMany(plan => plan.Keys).Distinct())
                if (!DungeonGenerationManager.Instance.worldTiles.ContainsKey(new Vector2Int(tile.X, tile.Y)))
                    throw new InvalidDataException($"Destination world tile ({tile.X}, {tile.Y}) does not exist in this save.");
            foreach (var parts in plans)
            {
                // Partition by anchor. Distant objects never exist in the live
                // scene alongside the destination tile's unloaded saved blob.
                foreach (var part in parts)
                {
                    if (part.Key == (currentTile.x, currentTile.y))
                    {
                        ApplyLoadedImport(part.Value);
                        applied++;
                    }
                    else
                    {
                        StructureSaveState.Pending.Add(new PendingTileImport
                        { x = part.Key.X, y = part.Key.Y, structure = part.Value });
                        Plugin.ProtectImportedPositions(GetOccupiedPositions(part.Value));
                        queued++;
                    }
                }
            }
            DialogBox.Instance.DisplayTextNoDialog(
                $"Imported {applied} loaded tile section(s). Queued {queued} distant section(s); " +
                "they will apply when you enter those world tiles. Save the game to keep these changes.");
        }
        catch (Exception exception)
        {
            Plugin.Log.LogError(exception);
            DialogBox.Instance.DisplayTextNoDialog("Import stopped: " + exception.Message.Truncate(300));
        }
    }

    internal static void ApplyLoadedImport(StructureFile file)
    {
        var occupied = GetOccupiedPositions(file);
        var originals = CollectObjectsAt(occupied);
        originals.UnionWith(CollectVegetationAt(occupied));
        // Snapshot before destroying anything. This can fail harmlessly if an
        // original cannot be serialized or reconstructed.
        var backup = CreateObjectRecords(originals);
        ValidateImportFiles(new[] { new StructureFile { objects = backup } });
        string savedState = StructureSaveState.Capture();
        var created = new List<GameObject>();
        bool removingOriginals = false;
        StructureSaveState.Mutating = true;
        try
        {
            ImportTransaction.Execute(() =>
            {
                removingOriginals = true;
                RemoveGameObjects(originals);
                InstantiateSupportingTerrain(file.supportingTerrain, created);
                InstantiateObjectRecords(file.objects, created);
                RegisterImportedObjects(created);
                ObjectPool.CallStarts();
                StructureSaveState.RemoveAppearances(occupied);
                foreach (var item in created)
                {
                    var appearance = item.GetComponent<StructureInstanceState>()?.Data;
                    if (appearance != null)
                        StructureSaveState.TrackAppearance(item, appearance.prefabName,
                            appearance.spriteVariantIndex, appearance.extender);
                }
                ResourceRegeneration.ReplaceImportedResources(occupied, created);
                Plugin.ProtectImportedPositions(occupied);
            }, () =>
            {
                try
                {
                    RemoveGameObjects(created);
                    if (removingOriginals)
                    {
                        // Remove any originals left after an interrupted removal,
                        // then restore exactly one instance of each snapshot.
                        RemoveGameObjects(originals.Where(item => item != null && item.activeInHierarchy));
                        var restoredObjects = new List<GameObject>();
                        InstantiateObjectRecords(backup, restoredObjects);
                        RegisterRestoredObjects(restoredObjects);
                        ObjectPool.CallStarts();
                    }
                }
                finally { StructureSaveState.Restore(savedState); }
            }, GetDisplayName(file));
        }
        finally { StructureSaveState.Mutating = false; }
        ActionQueue.Instance.DoAfterXFrames(1, WorldInfoManager.Instance.CheckDarknessMode);
    }

    private static HashSet<Vector2Int> GetOccupiedPositions(StructureFile file)
    {
        if (file.formatVersion >= 3)
        {
            return (file.objects ?? new List<StructureObject>())
                .SelectMany(StructureEditorUI.GetObjectCells)
                .Concat((file.supportingTerrain ??
                         new List<SupportingTerrain>())
                    .Where(item => ShouldImportTerrain(item.prefabName))
                    .Select(item => new Vector2Int(item.x, item.y)))
                .ToHashSet();
        }

        return file.occupiedPositions
            .Select(position => new Vector2Int(position.x, position.y))
            .ToHashSet();
    }

    internal static void ValidateImportFiles(
        IEnumerable<StructureFile> files)
    {
        HashSet<string> missingPrefabs =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (StructureFile file in files)
        {
            if (file.formatVersion < 3)
            {
                Convert.FromBase64String(file.serializedObjectsBase64);
                continue;
            }

            foreach (StructureObject record in
                     file.objects ?? new List<StructureObject>())
            {
                ValidatePosition(record.x, record.y, record.z, record.prefabName);
                if ((record.occupiedOffsets?.Count ?? 0) > 10000 ||
                    (record.occupiedOffsets?.Any(offset => offset == null ||
                        Math.Abs((long)offset.x) > 10000 || Math.Abs((long)offset.y) > 10000) ?? false))
                    throw new InvalidDataException($"Invalid occupied footprint for {record.prefabName}.");
                if (Plugin.ResolvePrefab(record.prefabName) == null)
                    missingPrefabs.Add(record.prefabName);
                ValidateComponentData(record.components, record.prefabName);
            }
            foreach (SupportingTerrain terrain in
                     file.supportingTerrain ?? new List<SupportingTerrain>())
            {
                if (!ShouldImportTerrain(terrain.prefabName))
                    continue;
                ValidatePosition(
                    terrain.x, terrain.y, terrain.z, terrain.prefabName);
                if (Plugin.ResolvePrefab(terrain.prefabName) == null)
                    missingPrefabs.Add(terrain.prefabName);
                ValidateComponentData(
                    terrain.components, terrain.prefabName);
            }
        }

        if (missingPrefabs.Count > 0)
            throw new InvalidDataException(
                "Import cancelled before changing the world because these " +
                "prefabs are unavailable: " +
                string.Join(", ", missingPrefabs.OrderBy(
                    name => name, StringComparer.OrdinalIgnoreCase)));
    }

    private static void ValidatePosition(
        float x, float y, float z, string prefabName)
    {
        if (Math.Abs(x) > 10000000 || Math.Abs(y) > 10000000 ||
            float.IsNaN(x) || float.IsInfinity(x) ||
            float.IsNaN(y) || float.IsInfinity(y) ||
            float.IsNaN(z) || float.IsInfinity(z))
            throw new InvalidDataException(
                $"Invalid coordinates for {prefabName}.");
    }

    private static void ValidateComponentData(
        IEnumerable<StructureComponent>? components,
        string prefabName)
    {
        foreach (StructureComponent component in
                 components ?? Enumerable.Empty<StructureComponent>())
        {
            if (string.IsNullOrWhiteSpace(component.type))
                throw new InvalidDataException(
                    $"A component on {prefabName} has no type.");
            Convert.FromBase64String(component.dataBase64 ?? "");
            var prefab = Plugin.ResolvePrefab(prefabName);
            if (prefab == null) continue;
            var target = ResolveHierarchyPath(prefab.transform, component.hierarchyPath);
            int index = Math.Max(0, component.componentIndex);
            if (target == null || target.GetComponents<MonoBehaviour>()
                    .OfType<ISerializableMonoBehavior>()
                    .Count(candidate => ComponentTypeMatches(candidate.GetType(), component.type)) <= index)
                throw new InvalidDataException(
                    $"Missing saved component {component.type} on {prefabName}; import was not applied.");
        }
    }

    private static string GetDisplayName(StructureFile file)
    {
        return string.IsNullOrWhiteSpace(file.name) ? "Unnamed Structure" : file.name;
    }

    private static StructureFile ReadFile(string path)
    {
        object? deserialized = StringSerializationAPI.Deserialize(
            typeof(StructureFile), File.ReadAllText(path));
        if (deserialized is not StructureFile file)
            throw new InvalidDataException(
                $"Could not read {Path.GetFileName(path)} as a structure file.");

        if (file.formatVersion < 1 || file.formatVersion > 3)
            throw new InvalidDataException(
                $"Unsupported format version {file.formatVersion} in {Path.GetFileName(path)}.");
        file.objects ??= new List<StructureObject>();
        file.supportingTerrain ??= new List<SupportingTerrain>();
        file.occupiedPositions ??= new List<StructurePosition>();
        file.serializedObjectsBase64 ??= "";
        if (file.formatVersion < 3)
        {
            file.objects = LegacyStructureReader.Read(file.serializedObjectsBase64);
            file.formatVersion = 3;
            file.serializedObjectsBase64 = "";
        }
        if (file.formatVersion >= 3)
            StructureFileNormalizer.MoveTileObjectsToTerrain(file);
        bool hasReadableData =
            file.objects.Count > 0 || file.supportingTerrain.Count > 0;
        if (file.formatVersion >= 3 && !hasReadableData)
            throw new InvalidDataException(
                $"No readable object data in {Path.GetFileName(path)}.");
        if (file.formatVersion < 3 &&
            string.IsNullOrWhiteSpace(file.serializedObjectsBase64))
            throw new InvalidDataException(
                $"No legacy object data in {Path.GetFileName(path)}.");

        if (file.formatVersion >= 3)
        {
            file.occupiedPositions = (file.objects ?? new List<StructureObject>())
                .SelectMany(StructureEditorUI.GetObjectCells)
                .Concat((file.supportingTerrain ?? new List<SupportingTerrain>())
                    .Select(item => new Vector2Int(item.x, item.y)))
                .Distinct()
                .Select(position => new StructurePosition
                {
                    x = position.x,
                    y = position.y
                })
                .ToList();
        }

        return file;
    }

    private static List<GameObject> FindPlayerStructures()
    {
        return GetActiveSerializableObjects()
            .Where(gameObject =>
                gameObject != null &&
                gameObject.activeInHierarchy &&
                (HasCompleteExportBounds ||
                 (gameObject.GetComponent<IPersistentSerializableMonoBehavior>() == null &&
                  gameObject.GetComponent<PersistentEntity>() == null)) &&
                (HasCompleteExportBounds ||
                 (!IsNaturalCliffWall(gameObject) &&
                  !BaseShedObjects.Contains(GetObjectSignature(gameObject)))) &&
                IsInsideSelectedExportBounds(
                    gameObject.transform.GetVector2IntPosition()) &&
                (HasCompleteExportBounds ||
                 WorldInfoManager.Instance.IsOnPlayerOwnedPlot(
                     gameObject.transform.GetVector2IntPosition())) &&
                (HasCompleteExportBounds
                    ? IsBoundedExportFixture(gameObject)
                    : gameObject.GetComponent<Deconstructable>() != null ||
                      gameObject.GetComponent<ConstructionSite>() != null ||
                      gameObject.GetComponent<StructureRailingState>() != null ||
                      gameObject.GetComponent<Sign>() != null ||
                      IsPlayerPlacedFurniture(gameObject)))
            .Distinct()
            .ToList();
    }

    private static bool IsBoundedExportFixture(GameObject gameObject)
    {
        if (gameObject == Player.Instance.gameObject ||
            gameObject.GetComponent<EntityMover>() != null ||
            gameObject.GetComponent<NeuralNPC>() != null ||
            gameObject.GetComponent<Enemy>() != null)
            return false;

        return true;
    }

    private static bool IsNaturalCliffWall(GameObject gameObject)
    {
        string prefabName = SerializationManager.GetPrefabName(gameObject);
        return prefabName.Equals(
                   "prefab_wall_rock_cliff",
                   StringComparison.OrdinalIgnoreCase) ||
               prefabName.Contains(
                   "rock_cliff",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryValidateExportBounds(out string error)
    {
        error = "";
        if (!ExportTopLeft.HasValue && !ExportBottomRight.HasValue)
            return true;

        if (!ExportTopLeft.HasValue || !ExportBottomRight.HasValue)
        {
            error = "Export bounds are incomplete. Select both the top-left and " +
                    "bottom-right bounds, or clear both to export everything.";
            return false;
        }

        Vector2Int topLeft = ExportTopLeft.Value;
        Vector2Int bottomRight = ExportBottomRight.Value;
        if (topLeft.x > bottomRight.x || topLeft.y < bottomRight.y)
        {
            error = "The selected export bounds are reversed. Top-left must be " +
                    "left of and above the bottom-right tile.";
            return false;
        }

        long width = (long)bottomRight.x - topLeft.x + 1;
        long height = (long)topLeft.y - bottomRight.y + 1;
        if (width > 1000000 || height > 1000000 || width * height > 1000000 ||
            bottomRight.x == int.MaxValue || topLeft.y == int.MaxValue)
        {
            error = "Export bounds are too large. Use a rectangle of at most 1,000,000 cells.";
            return false;
        }

        return true;
    }

    private static bool IsInsideSelectedExportBounds(Vector2Int position)
    {
        if (!ExportTopLeft.HasValue && !ExportBottomRight.HasValue)
            return true;
        if (!ExportTopLeft.HasValue || !ExportBottomRight.HasValue)
            return false;

        Vector2Int topLeft = ExportTopLeft.Value;
        Vector2Int bottomRight = ExportBottomRight.Value;
        return position.x >= topLeft.x &&
               position.x <= bottomRight.x &&
               position.y <= topLeft.y &&
               position.y >= bottomRight.y;
    }

    private static List<SupportingTerrain> FindSupportingTerrain(
        IEnumerable<StructureObject> objects,
        IEnumerable<GameObject> terrainCandidates)
    {
        List<SupportingTerrain> result = new();
        HashSet<(int, int, string)> seen = new();
        foreach (var terrain in terrainCandidates)
            AddTerrainRecord(result, seen, terrain,
                terrain.transform.GetVector2IntPosition());
        Vector2Int[] objectPositions = objects
            .SelectMany(StructureEditorUI.GetObjectCells)
            .Distinct()
            .ToArray();

        foreach (Vector2Int position in objectPositions)
        {
            IEnumerable<GameObject> terrain = Turfs.GetTurfGameObjects(position)
                .Where(gameObject =>
                    gameObject != null &&
                    gameObject.GetComponent<Door>() == null &&
                    gameObject.GetComponent<IPersistentSerializableMonoBehavior>() == null &&
                    gameObject.GetComponent<PersistentEntity>() == null &&
                    IsTerrainObject(gameObject));

            foreach (GameObject gameObject in terrain)
            {
                AddTerrainRecord(result, seen, gameObject, position);
            }
        }

        if (HasCompleteExportBounds)
        {
            Vector2Int topLeft = ExportTopLeft.GetValueOrDefault();
            Vector2Int bottomRight = ExportBottomRight.GetValueOrDefault();
            for (int x = topLeft.x; x <= bottomRight.x; x++)
            {
                for (int y = bottomRight.y; y <= topLeft.y; y++)
                {
                    Vector2Int position = new(x, y);
                    foreach (GameObject gameObject in
                             Turfs.GetTurfGameObjects(position)
                                 .Where(IsTerrainObject))
                    {
                        AddTerrainRecord(result, seen, gameObject, position);
                    }
                }
            }
        }

        return result;
    }

    private static void AddTerrainRecord(
        List<SupportingTerrain> result,
        HashSet<(int, int, string)> seen,
        GameObject gameObject,
        Vector2Int position)
    {
        string prefabName = SerializationManager.GetPrefabName(gameObject);
        if (!seen.Add((position.x, position.y, prefabName.ToLowerInvariant())))
            return;

        result.Add(new SupportingTerrain
        {
            prefabName = prefabName,
            x = position.x,
            y = position.y,
            z = gameObject.transform.position.z,
            spriteVariantIndex = GetCurrentSpriteVariantIndex(gameObject),
            hasMapZoneName =
                TryGetMapZoneName(gameObject, out string zoneName),
            mapZoneName = zoneName,
            components = CreateComponentRecords(gameObject)
        });
    }

    private static void InstantiateSupportingTerrain(
        IEnumerable<SupportingTerrain> supportingTerrain,
        ICollection<GameObject> createdObjects)
    {
        if (supportingTerrain == null)
            return;

        foreach (SupportingTerrain terrain in supportingTerrain)
        {
            if (!ShouldImportTerrain(terrain.prefabName))
                continue;

            GameObject? prefab = Plugin.ResolvePrefab(terrain.prefabName);
            if (prefab == null)
                throw new InvalidDataException("Missing supporting terrain prefab: " + terrain.prefabName);

            GameObject gameObject = UnityEngine.Object.Instantiate(
                prefab,
                new Vector3(terrain.x, terrain.y, terrain.z),
                prefab.transform.rotation);
            createdObjects.Add(gameObject);
            ApplyComponentRecords(
                gameObject, terrain.components, terrain.prefabName);
            ApplySpriteVariant(
                gameObject,
                terrain.prefabName,
                terrain.spriteVariantIndex);
            if (terrain.hasMapZoneName)
                SetMapZoneName(gameObject, terrain.mapZoneName);
            StructureSaveState.TrackAppearance(gameObject, terrain.prefabName,
                terrain.spriteVariantIndex, false);
        }
    }

    private static bool ShouldImportTerrain(string prefabName)
    {
        bool isWater = prefabName.Contains(
            "water", StringComparison.OrdinalIgnoreCase);
        if (!isWater)
            return true;

        bool isBathhouseWater = prefabName.Contains(
            "water_bathhouse", StringComparison.OrdinalIgnoreCase);
        return isBathhouseWater || Plugin.ImportOtherWaterTiles.Value;
    }

    private static List<StructureObject> CreateObjectRecords(
        IEnumerable<GameObject> gameObjects)
    {
        List<StructureObject> records = new();
        foreach (GameObject gameObject in gameObjects)
        {
            Vector3 position = gameObject.transform.position;
            bool hasSignMessage = TryGetEditableSignMessage(
                gameObject, out string signMessage);
            bool hasZoneName = TryGetMapZoneName(
                gameObject, out string zoneName);
            Turnable turnable =
                gameObject.GetComponentInChildren<Turnable>(true);
            StructureObject record = new()
            {
                prefabName = SerializationManager.GetPrefabName(gameObject),
                x = position.x,
                y = position.y,
                z = position.z,
                npcInteractionRangeExtender =
                    gameObject.GetComponentInChildren<NPCInteractionRangeExtender>(
                        includeInactive: true) != null,
                turnableIndex = turnable != null ? turnable.GetIndex() : -1,
                spriteVariantIndex =
                    turnable == null
                        ? GetCurrentSpriteVariantIndex(gameObject)
                        : -1,
                hasEditableSignMessage = hasSignMessage,
                signMessage = signMessage,
                hasMapZoneName = hasZoneName,
                mapZoneName = zoneName,
                occupiedOffsets = StructureEditorUI.GetPrefabOccupiedOffsets(
                    SerializationManager.GetPrefabName(gameObject),
                    turnable != null ? turnable.GetIndex() : -1)
            };

            record.components = CreateComponentRecords(gameObject);

            records.Add(record);
        }

        return records;
    }

    private static List<StructureComponent> CreateComponentRecords(
        GameObject gameObject)
    {
        List<StructureComponent> result = new();
        IEnumerable<ISerializableMonoBehavior> components =
            gameObject.GetComponentsInChildren<MonoBehaviour>(true)
                .OfType<ISerializableMonoBehavior>();
        foreach (ISerializableMonoBehavior component in components)
        {
            if (component is not Component unityComponent)
                continue;
            Type type = component.GetType();
            ISerializableMonoBehavior[] sameType =
                unityComponent.gameObject.GetComponents<MonoBehaviour>()
                    .OfType<ISerializableMonoBehavior>()
                    .Where(candidate => candidate.GetType() == type)
                    .ToArray();
            int componentIndex = Array.IndexOf(sameType, component);
            using MemoryStream stream = new();
            using BinaryWriter writer = new(stream);
            component.Serialize(writer);
            writer.Flush();
            result.Add(new StructureComponent
            {
                type = (type.FullName ?? type.Name) + ", " +
                       type.Assembly.GetName().Name,
                hierarchyPath = GetHierarchyPath(
                    gameObject.transform, unityComponent.transform),
                componentIndex = componentIndex,
                dataBase64 = Convert.ToBase64String(stream.ToArray())
            });
        }

        return result;
    }

    private static string GetHierarchyPath(
        Transform root, Transform target)
    {
        if (target == root)
            return "";
        Stack<int> indices = new();
        Transform? current = target;
        while (current != null && current != root)
        {
            indices.Push(current.GetSiblingIndex());
            current = current.parent;
        }
        return current == root
            ? string.Join("/", indices)
            : "";
    }

    private static Transform? ResolveHierarchyPath(
        Transform root, string hierarchyPath)
    {
        if (string.IsNullOrWhiteSpace(hierarchyPath))
            return root;
        Transform current = root;
        foreach (string part in hierarchyPath.Split('/'))
        {
            if (!int.TryParse(
                    part,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int siblingIndex) ||
                siblingIndex < 0 ||
                siblingIndex >= current.childCount)
                return null;
            current = current.GetChild(siblingIndex);
        }
        return current;
    }

    private static bool ComponentTypeMatches(
        Type candidate, string savedType)
    {
        string savedFullName = (savedType ?? "")
            .Split(',')[0]
            .Trim();
        string compactName =
            (candidate.FullName ?? candidate.Name) + ", " +
            candidate.Assembly.GetName().Name;
        return candidate.AssemblyQualifiedName == savedType ||
               compactName == savedType ||
               candidate.FullName == savedType ||
               candidate.FullName == savedFullName ||
               candidate.Name == savedType;
    }

    private static void ApplyComponentRecords(
        GameObject gameObject,
        IEnumerable<StructureComponent>? savedComponents,
        string prefabName)
    {
        foreach (StructureComponent savedComponent in
                 savedComponents ?? Enumerable.Empty<StructureComponent>())
        {
            Transform? target = ResolveHierarchyPath(
                gameObject.transform, savedComponent.hierarchyPath);
            if (target == null)
            {
                throw new InvalidDataException(
                    $"Missing component path '{savedComponent.hierarchyPath}' " +
                    $"on {prefabName}.");
            }

            ISerializableMonoBehavior[] matches =
                target.GetComponents<MonoBehaviour>()
                    .OfType<ISerializableMonoBehavior>()
                    .Where(candidate => ComponentTypeMatches(
                        candidate.GetType(), savedComponent.type))
                    .ToArray();
            int index = savedComponent.componentIndex >= 0
                ? savedComponent.componentIndex
                : 0;
            if (index >= matches.Length)
            {
                throw new InvalidDataException(
                    $"Missing component {savedComponent.type} " +
                    $"at '{savedComponent.hierarchyPath}' on {prefabName}.");
            }

            byte[] data = Convert.FromBase64String(
                savedComponent.dataBase64 ?? "");
            using MemoryStream stream = new(data);
            using BinaryReader reader = new(stream);
            matches[index].Deserialize(reader);
        }
    }

    private static int InstantiateObjectRecords(
        IEnumerable<StructureObject> records,
        ICollection<GameObject> createdObjects)
    {
        int count = 0;
        foreach (StructureObject record in records)
        {
            GameObject? prefab = Plugin.ResolvePrefab(record.prefabName);
            if (prefab == null)
                throw new InvalidDataException("Missing prefab: " + record.prefabName);

            Vector3 position = new(record.x, record.y, record.z);
            GameObject gameObject;
            if (ObjectPool.IsObjectPoolTarget(prefab))
            {
                gameObject = ObjectPool.Claim(prefab, position);
            }
            else
            {
                gameObject = UnityEngine.Object.Instantiate(
                    prefab, position, prefab.transform.rotation);
            }
            createdObjects.Add(gameObject);
            ApplyComponentRecords(
                gameObject, record.components, record.prefabName);

            Turnable importedTurnable =
                gameObject.GetComponentInChildren<Turnable>(true);
            if (record.turnableIndex >= 0 &&
                importedTurnable != null)
            {
                importedTurnable.SetRotation(record.turnableIndex);
            }

            ApplySpriteVariant(gameObject, record);
            if (record.hasEditableSignMessage)
                SetEditableSignMessage(gameObject, record.signMessage);
            if (record.hasMapZoneName)
                SetMapZoneName(gameObject, record.mapZoneName);
            StructureSaveState.TrackAppearance(gameObject, record.prefabName,
                record.spriteVariantIndex, record.npcInteractionRangeExtender);
            RailingPrefab.ActivateImportedInstance(gameObject);
            count++;
        }

        return count;
    }

    private static int GetCurrentSpriteVariantIndex(GameObject gameObject)
    {
        string prefabName = SerializationManager.GetPrefabName(gameObject);
        Sprite[] variants =
            StructureEditorUI.GetSpriteVariantsForPrefab(prefabName);
        if (variants.Length < 2)
            return -1;

        SpriteRenderer renderer =
            gameObject.GetComponentsInChildren<SpriteRenderer>(true)
                .FirstOrDefault(candidate =>
                    candidate.enabled && candidate.sprite != null);
        return renderer == null
            ? -1
            : Array.IndexOf(variants, renderer.sprite);
    }

    private static void ApplySpriteVariant(
        GameObject gameObject, StructureObject record)
    {
        ApplySpriteVariant(
            gameObject, record.prefabName, record.spriteVariantIndex);
    }

    private static void ApplySpriteVariant(
        GameObject gameObject, string prefabName, int spriteVariantIndex)
    {
        ApplySpriteVariantNow(gameObject, prefabName, spriteVariantIndex);
    }

    internal static void ApplySpriteVariantNow(
        GameObject gameObject, string prefabName, int spriteVariantIndex)
    {
        if (spriteVariantIndex < 0 ||
            gameObject.GetComponentInChildren<Turnable>(true) != null)
            return;

        Sprite[] variants =
            StructureEditorUI.GetSpriteVariantsForPrefab(prefabName);
        if (variants.Length == 0)
            return;

        SpriteRenderer renderer =
            gameObject.GetComponentsInChildren<SpriteRenderer>(true)
                .FirstOrDefault(candidate =>
                    candidate.enabled && candidate.sprite != null);
        if (renderer == null)
            return;

        int index = (spriteVariantIndex % variants.Length +
                     variants.Length) % variants.Length;
        renderer.sprite = variants[index];
        // Store the selected native index as well as the renderer. Native
        // RandomSprite.Start and subsequent game saves otherwise restore the old choice.
        foreach (var random in gameObject.GetComponentsInChildren<RandomSprite>(true))
        {
            var fields = Traverse.Create(random);
            var nativeSprites = fields.Field("sprites").GetValue<Sprite[]>();
            int nativeIndex = Array.IndexOf(nativeSprites ?? Array.Empty<Sprite>(), variants[index]);
            if (nativeIndex < 0) continue;
            fields.Field("index").SetValue(nativeIndex);
            fields.Field("initialized").SetValue(true);
        }
    }

    private static bool TryGetEditableSignMessage(
        GameObject gameObject, out string message)
    {
        foreach (Component component in new Component[]
                 {
                     gameObject.GetComponentInChildren<Sign>(true),
                     gameObject.GetComponentInChildren<SimpleSign>(true)
                 })
        {
            if (component != null &&
                TryGetStringField(component, "message", out message))
                return true;
        }

        message = "";
        return false;
    }

    private static void SetEditableSignMessage(
        GameObject gameObject, string message)
    {
        foreach (Component component in new Component[]
                 {
                     gameObject.GetComponentInChildren<Sign>(true),
                     gameObject.GetComponentInChildren<SimpleSign>(true)
                 })
        {
            if (component != null)
                SetStringField(component, "message", message);
        }
    }

    private static bool TryGetMapZoneName(
        GameObject gameObject, out string zoneName)
    {
        MapZone mapZone = gameObject.GetComponentInChildren<MapZone>(true);
        if (mapZone != null &&
            TryGetStringField(mapZone, "zoneName", out zoneName))
            return true;

        zoneName = "";
        return false;
    }

    private static void SetMapZoneName(
        GameObject gameObject, string zoneName)
    {
        MapZone mapZone = gameObject.GetComponentInChildren<MapZone>(true);
        if (mapZone != null)
            SetStringField(mapZone, "zoneName", zoneName);
    }

    private static bool TryGetStringField(
        Component component, string fieldName, out string value)
    {
        FieldInfo field = component.GetType().GetField(
            fieldName,
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic);
        if (field?.GetValue(component) is string text)
        {
            value = text;
            return true;
        }

        value = "";
        return false;
    }

    private static void SetStringField(
        Component component, string fieldName, string value)
    {
        FieldInfo field = component.GetType().GetField(
            fieldName,
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic);
        field?.SetValue(component, value ?? "");
    }

    private static bool IsPlayerPlacedFurniture(GameObject gameObject)
    {
        Pickupable pickupable = gameObject.GetComponent<Pickupable>();

        // Pickupable.owned means the furnishing still belongs to the world/NPC
        // and cannot be picked up. The placement code explicitly sets it false
        // when the player places the furnishing.
        return pickupable != null && !pickupable.owned;
    }

    private static HashSet<GameObject> CollectObjectsAt(
        HashSet<Vector2Int> positions)
    {
        HashSet<GameObject> existing = new();
        foreach (Vector2Int position in positions)
        {
            foreach (GameObject gameObject in Turfs.GetTurfGameObjects(position))
            {
                if (gameObject == null || !gameObject.activeInHierarchy ||
                    gameObject == Player.Instance.gameObject ||
                    gameObject.GetComponent<NeuralNPC>() != null ||
                    gameObject.GetComponent<EntityMover>() != null ||
                    gameObject.GetComponent<IPersistentSerializableMonoBehavior>() != null ||
                    gameObject.GetComponent<PersistentEntity>() != null)
                    continue;

                if (gameObject.GetComponentsInChildren<MonoBehaviour>(true)
                        .OfType<ISerializableMonoBehavior>().Any() ||
                    IsTerrainObject(gameObject))
                    existing.Add(gameObject);
            }
        }

        // An object instantiated on a world tile other than the player's
        // current tile may not be present in Turfs yet. Scan serialized scene
        // objects as well so later imports can still replace it instead of
        // creating an invisible/off-tile stack.
        foreach (GameObject gameObject in
                 GetActiveSerializableObjects())
        {
            if (gameObject == null || !gameObject.activeInHierarchy ||
                !positions.Contains(
                    gameObject.transform.GetVector2IntPosition()) ||
                gameObject == Player.Instance.gameObject ||
                gameObject.GetComponent<NeuralNPC>() != null ||
                gameObject.GetComponent<EntityMover>() != null ||
                gameObject.GetComponent<IPersistentSerializableMonoBehavior>() != null ||
                gameObject.GetComponent<PersistentEntity>() != null)
                continue;

            existing.Add(gameObject);
        }

        return existing;
    }

    private static HashSet<GameObject> CollectVegetationAt(
        HashSet<Vector2Int> positions)
    {
        HashSet<GameObject> vegetation = new();
        foreach (Vector2Int position in positions)
        {
            foreach (GameObject gameObject in Turfs.GetTurfGameObjects(position))
            {
                if (gameObject != null && gameObject.activeInHierarchy &&
                    gameObject.GetComponent<EntityMover>() == null &&
                    !IsTerrainObject(gameObject) &&
                    (gameObject.GetComponent<Tree>() != null ||
                     gameObject.name.ContainsAnyIgnoreCase(
                         "grass", "tree", "bush", "plant", "herb", "crop")))
                {
                    vegetation.Add(gameObject);
                }
            }
        }

        return vegetation;
    }

    internal static void RemoveGameObjects(
        IEnumerable<GameObject> gameObjects)
    {
        foreach (GameObject gameObject in gameObjects
                     .Where(item => item != null)
                     .Distinct())
        {
            if (!gameObject.activeInHierarchy) continue;
            // Released pool entries must not receive a deferred pool Start.
            var pending = Traverse.Create(typeof(ObjectPool))
                .Field("toCallStartOn").GetValue<List<IObjectPoolCompliant>>();
            if (pending != null)
            {
                var callbacks = new HashSet<IObjectPoolCompliant>(
                    gameObject.GetComponentsInChildren<IObjectPoolCompliant>(true));
                pending.RemoveAll(callbacks.Contains);
            }
            if (ObjectPool.IsObjectPoolTarget(gameObject))
                ObjectPool.Release(gameObject);
            else
            {
                // Tile registries unregister in OnDestroy. Transaction backups
                // exist before this point; complete removal before replacements Awake.
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }
    }

    private static void RegisterImportedObjects(
        IEnumerable<GameObject> gameObjects)
    {
        RegisterRestoredObjects(gameObjects);
    }

    private static void RegisterRestoredObjects(IEnumerable<GameObject> gameObjects)
    {
        foreach (TurfRegistrar registrar in gameObjects
                     .Where(item => item != null)
                     .SelectMany(item =>
                         item.GetComponentsInChildren<TurfRegistrar>(true))
                     .Where(item => item != null)
                     .Distinct())
        {
            registrar.Register();
        }
    }

    private static HashSet<Vector2Int> GetVegetationClearancePositions(
        HashSet<Vector2Int> occupied)
    {
        // Correct occupied footprints now include every covered cell, so
        // neighboring tiles no longer need destructive vegetation clearing.
        return new HashSet<Vector2Int>(occupied);
    }

    private static string GetObjectSignature(GameObject gameObject)
    {
        Vector2Int position = gameObject.transform.GetVector2IntPosition();
        return SerializationManager.GetPrefabName(gameObject) + "|" +
               position.x + "|" + position.y;
    }

    internal static bool IsTerrainObject(GameObject gameObject)
    {
        if (gameObject == null) return false;
        string prefabName = SerializationManager.GetPrefabName(gameObject);
        return prefabName.StartsWith(
                   "prefab_tile_", StringComparison.OrdinalIgnoreCase) &&
               gameObject.GetComponent<Door>() == null;
    }

    internal static HashSet<GameObject> GetActiveSerializableObjects() =>
        UnityEngine.Object.FindObjectsOfType<MonoBehaviour>()
            .OfType<ISerializableMonoBehavior>()
            .OfType<Component>()
            .Where(component => component != null && component.gameObject.activeInHierarchy)
            .Select(component => component.gameObject).ToHashSet();

}
