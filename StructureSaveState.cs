#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Silverpine.ModdingTools;
using UnityEngine;

namespace StructureHandler;

internal static class StructureSaveState
{
    private static IDisposable? registration;
    private static bool restored;
    private static bool scheduled;
    private static bool processing;
    private static int loadEpoch;
    private static Exception? migrationFailure;
    internal static bool Unloading;
    internal static bool Mutating;
    internal static string LegacySavePath = "";
    internal static readonly List<PendingTileImport> Pending = new();
    internal static readonly ImportFootprint ImportedCells = new();
    private static readonly Dictionary<string, StructureAppearance> Appearances =
        new(StringComparer.OrdinalIgnoreCase);

    internal static void Initialize()
    {
        if (registration != null) return;
        registration = ModSaveData.Register(Plugin.PluginGuid, new ModSaveDataDefinition
        {
            Id = Plugin.PluginGuid + ".world",
            CurrentVersion = 3,
            // Older versions have no imported-cell history (or supplemental
            // terrain). Empty additive defaults never invent import footprints.
            Migrate = (version, payload) => version == 1 || version == 2 ? payload :
                throw new InvalidDataException("Unsupported structure save version."),
            Capture = Capture,
            Restore = Restore,
            Reset = Reset
        });
        ModSaveData.Loaded += OnLoaded;
    }
    private static void Reset()
    {
        loadEpoch++;
        StructureTerrainPersistence.Reset();
        Plugin.ProtectedWorldTiles.Clear();
        Plugin.BuildableWorldTiles.Clear();
        ResourceRegeneration.Reset();
        Pending.Clear();
        ImportedCells.Reset();
        Appearances.Clear();
        restored = false;
        migrationFailure = null;
        LegacySavePath = "";
        scheduled = false;
    }
    internal static string Capture()
    {
        if (migrationFailure != null)
            throw new InvalidDataException("Legacy structure markers could not be read. They have not been replaced.", migrationFailure);
        // Include moved furniture at its current position, while retaining
        // overrides belonging to unloaded world tiles.
        foreach (var state in UnityEngine.Object.FindObjectsOfType<StructureInstanceState>())
            state.RefreshPosition();
        StructureTerrainPersistence.RefreshLive();
        return StringSerializationAPI.Serialize(typeof(StructureWorldState), new StructureWorldState
        {
            protectedTiles = Positions(Plugin.ProtectedWorldTiles),
            buildableTiles = Positions(Plugin.BuildableWorldTiles),
            resources = ResourceRegeneration.Records.ToList(),
            pending = Pending.ToList(),
            appearances = Appearances.Values.ToList(),
            supplementalObjects = StructureTerrainPersistence.Ledger.Objects.Values.ToList(),
            clearedScenery = StructureTerrainPersistence.Ledger.Cleared.Values.ToList(),
            importedCells = ImportedCells.Capture()
        });
    }
    internal static void Restore(string json)
    {
        var state = StringSerializationAPI.Deserialize(typeof(StructureWorldState), json)
            as StructureWorldState ?? throw new InvalidDataException("Invalid structure save data.");
        var importedCells = ImportFootprint.Read(state.importedCells);
        StructureTerrainPersistence.Ledger.Restore(state.supplementalObjects, state.clearedScenery);
        ImportedCells.Restore(importedCells);
        Plugin.ProtectedWorldTiles.Clear();
        Plugin.BuildableWorldTiles.Clear();
        foreach (var tile in state.protectedTiles ?? new())
            Plugin.ProtectedWorldTiles.Add(new Vector2Int(tile.x, tile.y));
        foreach (var tile in state.buildableTiles ?? new())
            Plugin.BuildableWorldTiles.Add(new Vector2Int(tile.x, tile.y));
        ResourceRegeneration.Restore(state.resources ?? new());
        Pending.Clear();
        Pending.AddRange(state.pending ?? new());
        Appearances.Clear();
        foreach (var appearance in state.appearances ?? new())
            Appearances[AppearanceKey(appearance)] = appearance;
        restored = true;
    }
    private static List<StructurePosition> Positions(IEnumerable<Vector2Int> positions) =>
        positions.OrderBy(p => p.x).ThenBy(p => p.y)
            .Select(p => new StructurePosition { x = p.x, y = p.y }).ToList();

    private static void OnLoaded()
    {
        if (!restored && ModSaveData.Warnings.Count == 0 &&
            !string.IsNullOrWhiteSpace(LegacySavePath))
        {
            // Only a save-specific legacy file can identify this save's state.
            // The old global protection/buildability lists have no ownership
            // information and must never be copied into unrelated saves.
            string path = LegacySavePath + ".structurehandler-resources.json";
            if (File.Exists(path))
            {
                try
                {
                    var legacy = StringSerializationAPI.Deserialize(
                        typeof(RegeneratingResourceFile), File.ReadAllText(path)) as RegeneratingResourceFile;
                    if (legacy == null || legacy.formatVersion != 1)
                        throw new InvalidDataException("Unsupported legacy resource markers.");
                    ResourceRegeneration.Restore(legacy.resources);
                }
                catch (Exception exception)
                {
                    migrationFailure = exception;
                    Plugin.Log.LogError("Legacy resource marker migration failed; the original file was kept: " + exception);
                    UpperNotificationUI.Instance?.OneOff("Structure Handler could not load legacy resource markers. See the log; the file was kept.");
                }
            }
        }
        ScheduleLoadedTile();
    }
    internal static bool TryCurrentTile(out Vector2Int tile)
    {
        tile = default;
        if (DungeonGenerationManager.Instance == null || Player.Instance == null) return false;
        tile = DungeonGenerationManager.Instance.currentPlayerWorldTilePosition;
        return true;
    }
    internal static void ScheduleLoadedTile()
    {
        if (scheduled || ActionQueue.Instance == null) return;
        scheduled = true;
        int epoch = loadEpoch;
        ActionQueue.Instance.DoAfterXFrames(2, () =>
        {
            if (epoch != loadEpoch) return;
            scheduled = false;
            ProcessLoadedTile();
        });
    }
    internal static void ProcessLoadedTile()
    {
        if (processing || SerializationManager.loadingSave || !TryCurrentTile(out var tile)) return;
        processing = true;
        try
        {
            StructureTerrainPersistence.RestoreLoaded(tile);
            foreach (var pending in Pending.Where(p => p.x == tile.x && p.y == tile.y).ToArray())
            {
                StructureTransfer.ValidateImportFiles(new[] { pending.structure });
                StructureTransfer.ApplyLoadedImport(pending.structure);
                Pending.Remove(pending);
            }
            RestoreAppearances();
            ResourceRegeneration.ProcessLoadedTile();
        }
        catch (Exception exception)
        {
            Plugin.Log.LogError("Saved terrain restoration or deferred structure import failed; saved data was retained: " + exception);
            UpperNotificationUI.Instance?.OneOff(
                "Saved terrain or a queued structure could not be restored. Its data was kept; see the log.");
        }
        finally { processing = false; }
    }
    internal static void TileCleared(Vector2Int tile)
    {
        ImportedCells.ClearWorldTile(tile.x, tile.y);
        StructureTerrainPersistence.ClearTile(tile);
        ResourceRegeneration.RemoveMarkersForWorldTile(tile);
        Pending.RemoveAll(p => p.x == tile.x && p.y == tile.y);
        foreach (string key in Appearances.Where(pair => Plugin.GetWorldTile(
                     new Vector2(pair.Value.x, pair.Value.y)) == tile)
                     .Select(pair => pair.Key).ToArray())
            Appearances.Remove(key);
    }
    internal static void RemoveAppearances(HashSet<Vector2Int> cells)
    {
        foreach (string key in Appearances.Where(pair => cells.Contains(
                     new Vector2Int(Mathf.RoundToInt(pair.Value.x), Mathf.RoundToInt(pair.Value.y))))
                     .Select(pair => pair.Key).ToArray())
            Appearances.Remove(key);
    }
    internal static void TrackAppearance(GameObject instance, string prefabName, int sprite, bool extender,
        bool lockSpriteVariant = false)
    {
        var prefab = Plugin.ResolvePrefab(prefabName);
        bool nativeSprite = NativeAppearance.TryApply(instance, prefab, sprite, lockSpriteVariant);
        bool extraExtender = NativeSavePolicy.NeedsExtenderOverride(extender,
            prefab != null && prefab.GetComponentInChildren<NPCInteractionRangeExtender>(true) != null);
        var position = instance.transform.position;
        var data = new StructureAppearance
        {
            prefabName = prefabName, x = position.x, y = position.y, z = position.z,
            spriteVariantIndex = nativeSprite ? -1 : sprite, extender = extraExtender,
            lockSpriteVariant = !nativeSprite && sprite >= 0
        };
        string key = AppearanceKey(data);
        if (data.spriteVariantIndex < 0 && !data.extender)
        {
            Appearances.Remove(key);
            var old = instance.GetComponent<StructureInstanceState>();
            if (old != null)
            {
                old.Data = null;
                UnityEngine.Object.DestroyImmediate(old);
            }
            return;
        }
        var state = instance.GetComponent<StructureInstanceState>() ??
            instance.AddComponent<StructureInstanceState>();
        state.Data = data;
        Appearances[key] = data;
        state.Apply();
    }
    internal static void PrepareNativeSave()
    {
        if (SerializationManager.loadingSave || Appearances.Count == 0) return;
        foreach (var state in UnityEngine.Object.FindObjectsOfType<StructureInstanceState>())
            state.RefreshPosition();
        // Migrate old redundant overrides BEFORE the native binary is written.
        // Unloaded/missing objects keep their records until they can be visited.
        RestoreAppearances();
    }
    private static void RestoreAppearances()
    {
        if (Appearances.Count == 0) return;
        foreach (var instance in StructureTransfer.GetActiveSerializableObjects())
        {
            var position = instance.transform.position;
            string key = AppearanceKey(SerializationManager.GetPrefabName(instance), position.x, position.y, position.z);
            if (!Appearances.TryGetValue(key, out var data)) continue;
            TrackAppearance(instance, data.prefabName, data.spriteVariantIndex, data.extender,
                data.lockSpriteVariant);
        }
    }
    internal static string AppearanceKey(StructureAppearance data) =>
        AppearanceKey(data.prefabName, data.x, data.y, data.z);
    private static string AppearanceKey(string prefab, float x, float y, float z) =>
        prefab + "|" + x.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
        "|" + y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
        "|" + z.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    internal static void MoveAppearance(StructureAppearance data, Vector3 position)
    {
        Appearances.Remove(AppearanceKey(data));
        data.x = position.x; data.y = position.y; data.z = position.z;
        Appearances[AppearanceKey(data)] = data;
    }
    internal static void Forget(StructureAppearance data)
    {
        if (!Unloading && !Mutating && !SerializationManager.loadingSave)
            Appearances.Remove(AppearanceKey(data));
    }
}

internal sealed class StructureInstanceState : MonoBehaviour
{
    internal StructureAppearance? Data;
    private NPCInteractionRangeExtender? addedExtender;
    private void Start() => Apply();
    private void OnDestroy()
    {
        if (Data != null) StructureSaveState.Forget(Data);
    }
    internal void RefreshPosition()
    {
        if (Data != null && (Data.x != transform.position.x || Data.y != transform.position.y ||
                             Data.z != transform.position.z))
            StructureSaveState.MoveAppearance(Data, transform.position);
    }
    internal void Apply()
    {
        if (Data == null) return;
        if (Data.extender && GetComponentInChildren<NPCInteractionRangeExtender>(true) == null)
            addedExtender = gameObject.AddComponent<NPCInteractionRangeExtender>();
        StructureTransfer.ApplySpriteVariantNow(gameObject, Data.prefabName, Data.spriteVariantIndex);
    }
    internal void ReleasePooledState()
    {
        if (Data != null) StructureSaveState.Forget(Data);
        Data = null;
        if (addedExtender != null) DestroyImmediate(addedExtender);
        DestroyImmediate(this);
    }
}
