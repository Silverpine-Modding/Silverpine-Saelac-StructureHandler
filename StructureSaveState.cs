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
    private static PendingTileImport? waitingForDialog;
    private static readonly BoundaryPromptVisit BoundaryVisit = new();
    private static Exception? migrationFailure;
    internal static int SaveEpoch => loadEpoch;
    internal static bool Unloading;
    internal static bool Mutating;
    internal static bool RecoveryRequired { get; private set; }
    internal static void MarkIncompleteRollback() => RecoveryRequired = true;
    internal static string? EditBlockReason => RecoveryRequired ? ImportRecoveryException.RecoveryMessage :
        migrationFailure != null ? "Structure Handler could not read this save's data. Restore a good save or companion before making structure changes." : null;
    internal static string LegacySavePath = "";
    internal static readonly List<PendingTileImport> Pending = new();
    internal static readonly ImportFootprint ImportedCells = new();
    private static Dictionary<string, StructureAppearance> Appearances =
        new(StringComparer.OrdinalIgnoreCase);

    internal static void Initialize()
    {
        if (registration != null) return;
        registration = ModSaveData.Register(Plugin.PluginGuid, new ModSaveDataDefinition
        {
            Id = Plugin.PluginGuid + ".world",
            CurrentVersion = 6,
            // Older versions have no imported-cell history (or supplemental
            // terrain). Empty additive defaults never invent import footprints.
            Migrate = (version, payload) => version == 5 ? GrassZoneRepair.Upgrade(payload) :
                version >= 1 && version <= 4 ? payload :
                throw new InvalidDataException("Unsupported structure save version."),
            Capture = Capture,
            Restore = RestoreSavedPayload,
            Reset = Reset
        });
        ModSaveData.Loaded += OnLoaded;
    }
    private static void Reset()
    {
        loadEpoch++;
        BoundaryVisit.Enter();
        waitingForDialog = null;
        RecoveryRequired = false;
        MarketCrates.Reset();
        TerrainRepair.Reset();
        GrassZoneRepair.Restore(null);
        StructureTerrainPersistence.Reset();
        foreach (var state in UnityEngine.Object.FindObjectsOfType<StructureInstanceState>(true))
            state.ReleasePooledState();
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
            throw new InvalidDataException("Saved structure data could not be read. It has not been replaced.", migrationFailure);
        // Include moved furniture at its current position, while retaining
        // overrides belonging to unloaded world tiles.
        RefreshAppearancePositions();
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
            importedCells = ImportedCells.Capture(),
            legacyGrassRepair = GrassZoneRepair.Capture(),
            sharedMarket = MarketCrates.Capture()
        });
    }
    private static void RestoreSavedPayload(string json)
    {
        try { Restore(json); }
        catch (Exception e) { migrationFailure = e; throw; } // Framework retains the original payload.
    }

    internal static void Restore(string json)
    {
        var state = StringSerializationAPI.Deserialize(typeof(StructureWorldState), json)
            as StructureWorldState ?? throw new InvalidDataException("Invalid structure save data.");
        var prepared = new PreparedWorldState(state, Plugin.RegisteredNameResolver());
        // SharedMarketBook validates the full board before changing it. This is
        // the final input-dependent operation; publish only validated data below.
        MarketCrates.Restore(state.sharedMarket);
        StructureTerrainPersistence.Ledger = prepared.Ledger;
        ImportedCells.Restore(prepared.Imported);
        GrassZoneRepair.Restore(state.legacyGrassRepair);
        Plugin.ProtectedWorldTiles.Clear();
        Plugin.BuildableWorldTiles.Clear();
        foreach (var tile in prepared.Protected) Plugin.ProtectedWorldTiles.Add(new Vector2Int(tile.X, tile.Y));
        foreach (var tile in prepared.Buildable) Plugin.BuildableWorldTiles.Add(new Vector2Int(tile.X, tile.Y));
        ResourceRegeneration.Restore(prepared.Resources);
        Pending.Clear();
        Pending.AddRange(prepared.Pending);
        Appearances = prepared.Appearances;
        restored = true;
    }

    internal static void RestoreAfterRollback(string json)
    {
        Restore(json);
        // Restore deserializes NEW records. Rebind existing components as well
        // as those just recreated, so later destruction/movement updates the
        // right record, and live crates use the restored price-board instance.
        foreach (var state in UnityEngine.Object.FindObjectsOfType<StructureInstanceState>())
        {
            if (state.Data == null) continue;
            var p = state.transform.position;
            if (Appearances.TryGetValue(AppearanceKey(state.Data.prefabName, p.x, p.y, p.z), out var data))
            { state.Data = data; state.Apply(); }
            else state.ReleasePooledState();
        }
        MarketCrates.Reconcile();
    }
    private static List<StructurePosition> Positions(IEnumerable<Vector2Int> positions) =>
        positions.OrderBy(p => p.x).ThenBy(p => p.y)
            .Select(p => new StructurePosition { x = p.x, y = p.y }).ToList();

    private static void OnLoaded()
    {
        MarketCrates.Reconcile();
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
                    ResourceRegeneration.Restore(PreparedWorldState.ReadResources(legacy.resources, Plugin.RegisteredNameResolver()));
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
    internal static void OnTileEntered()
    {
        BoundaryVisit.Enter();
        waitingForDialog = null;
        ScheduleLoadedTile();
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
        if (processing || EditBlockReason != null || SerializationManager.loadingSave || !TryCurrentTile(out var tile)) return;
        processing = true;
        try
        {
            StructureTerrainPersistence.RestoreLoaded(tile);
            GrassZoneRepair.OnTileReady(tile);
            foreach (var pending in Pending.Where(p => p.x == tile.x && p.y == tile.y).ToArray())
            {
                if (pending.boundaryTail?.Count > 0)
                {
                    PromptBoundaryChoice(pending, tile);
                    break;
                }
                try
                {
                    StructureTransfer.ValidateImportFiles(new[] { pending.structure }, enforcePlacement: true);
                }
                catch (Exception exception)
                {
                    // Preflight does not mutate the tile. Keep this entry and all
                    // later entries in overwrite order, but still service the
                    // objects and terrain that are already present on this tile.
                    Plugin.Log.LogError($"Queued structure '{pending.structure?.name}' on {tile} failed validation; queue retained: {exception}");
                    UpperNotificationUI.Instance?.OneOff("A queued structure could not be imported. Its place in the queue was kept; see the log.");
                    break;
                }
                try { StructureTransfer.ApplyLoadedImport(pending.structure); }
                catch (InvalidDataException exception)
                {
                    // Invalid destination data or a completed rollback. Keep
                    // the queue head and run maintenance on the unchanged tile.
                    Plugin.Log.LogError($"Queued structure '{pending.structure.name}' on {tile} was not applied: {exception}");
                    UpperNotificationUI.Instance?.OneOff("A queued structure was not applied. The existing tile and queue were kept; see the log.");
                    break;
                }
                Pending.Remove(pending);
            }
            RestoreAppearances();
            ResourceRegeneration.ProcessLoadedTile();
            TerrainRepair.CheckAfterLoad();
        }
        catch (ImportRecoveryException exception)
        {
            MarkIncompleteRollback();
            Plugin.Log.LogError(exception);
            UpperNotificationUI.Instance?.OneOff(ImportRecoveryException.RecoveryMessage);
        }
        catch (Exception exception)
        {
            Plugin.Log.LogError("Saved terrain restoration or deferred structure import failed; saved data was retained: " + exception);
            UpperNotificationUI.Instance?.OneOff(
                "Saved terrain or a queued structure could not be restored. Its data was kept; see the log.");
        }
        finally { processing = false; }
    }

    private static void PromptBoundaryChoice(PendingTileImport pending, Vector2Int tile)
    {
        if (BoundaryVisit.Deferred) return;
        var dialog = DialogBox.Instance;
        int epoch = loadEpoch, visit = BoundaryVisit.Id;
        if (dialog == null || dialog.isOpen)
        {
            if (ActionQueue.Instance == null || waitingForDialog == pending) return;
            waitingForDialog = pending;
            ActionQueue.Instance.DoAfterXFrames(30, () =>
            {
                if (waitingForDialog != pending || epoch != loadEpoch || visit != BoundaryVisit.Id) return;
                waitingForDialog = null;
                if (!SerializationManager.loadingSave && TryCurrentTile(out var current) && current == tile &&
                    Pending.FirstOrDefault(p => p.x == tile.x && p.y == tile.y) == pending)
                    PromptBoundaryChoice(pending, tile);
            });
            return;
        }
        // Suppress repeated callbacks while our own modal is open. Clear on
        // a decision, including "later"; never reopen behind the player's back.
        waitingForDialog = pending;
        void Choose(bool combine)
        {
            if (epoch != loadEpoch || visit != BoundaryVisit.Id || BoundaryVisit.Deferred) return;
            if (waitingForDialog == pending) waitingForDialog = null;
            if (SerializationManager.loadingSave || !TryCurrentTile(out var current) || current != tile) return;
            try
            {
                if (StructurePlans.ResolveBoundary(Pending, pending, combine,
                    file => StructureTransfer.ValidateImportFiles(new[] { file }, enforcePlacement: true))) ScheduleLoadedTile();
            }
            catch (Exception e) { Plugin.Log.LogError("Boundary queue choice was not applied: " + e);
                UpperNotificationUI.Instance?.OneOff("Boundary records could not be resolved. The queue was kept."); }
        }
        dialog.DisplayTextNoDialog($"Older queued sections named '{pending.structure.name}' now belong on world tile {tile}. " +
            "If they were one blueprint, combine them so its furniture and floor import together. If they were separate imports, keep their order; later sections overwrite earlier ones.",
            new DialogOption("One blueprint", () => Choose(true)),
            new DialogOption("Separate imports", () => Choose(false)),
            new DialogOption("Decide on a later visit", () =>
            {
                if (epoch != loadEpoch || visit != BoundaryVisit.Id || waitingForDialog != pending) return;
                waitingForDialog = null;
                BoundaryVisit.Defer(visit);
            }));
    }
    internal static void TileCleared(Vector2Int tile)
    {
        GrassZoneRepair.ForgetRegeneratedTile(tile);
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
        var old = instance.GetComponent<StructureInstanceState>();
        if (old != null) old.RestoreNativeSprite();
        var prefab = Plugin.ResolvePrefab(prefabName);
        // Imported JSON may name an alias. Use the identity we will see on re-entry.
        prefabName = Plugin.PrefabNameOf(instance);
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
            if (old != null)
            {
                old.Data = null;
                old.ReleasePooledState();
            }
            return;
        }
        var state = old != null ? old : instance.AddComponent<StructureInstanceState>();
        state.Data = data;
        Appearances[key] = data;
        state.Apply();
    }
    internal static void PrepareNativeSave()
    {
        if (SerializationManager.loadingSave || Appearances.Count == 0) return;
        RefreshAppearancePositions();
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
            string key = AppearanceKey(Plugin.PrefabNameOf(instance), position.x, position.y, position.z);
            if (!Appearances.TryGetValue(key, out var data)) continue;
            TrackAppearance(instance, data.prefabName, data.spriteVariantIndex, data.extender,
                data.lockSpriteVariant);
        }
    }
    internal static string AppearanceKey(StructureAppearance data) =>
        AppearanceKey(data.prefabName, data.x, data.y, data.z);
    private static string AppearanceKey(string prefab, float x, float y, float z) =>
        StructurePersistenceLedger.Key(prefab, x, y, z);
    internal static void RefreshAppearancePositions()
    {
        if (Appearances.Count == 0) return;
        var moves = new List<(StructureAppearance, float, float, float)>();
        foreach (var state in UnityEngine.Object.FindObjectsOfType<StructureInstanceState>())
        {
            var data = state.Data;
            if (data == null) continue;
            var p = state.transform.position;
            if (data.x != p.x || data.y != p.y || data.z != p.z) moves.Add((data, p.x, p.y, p.z));
        }
        StructurePersistenceLedger.MoveAppearanceRecords(Appearances, moves);
    }
    internal static void Forget(StructureAppearance data)
    {
        if (!Unloading && !Mutating && !SerializationManager.loadingSave)
            StructurePersistenceLedger.ForgetOwnedAppearance(Appearances, data);
    }
}

internal sealed class StructureInstanceState : MonoBehaviour
{
    internal StructureAppearance? Data;
    private NPCInteractionRangeExtender? addedExtender;
    private readonly TemporarySpriteOverride<SpriteRenderer,Sprite> paint =
        new(r => r != null, r => r.sprite, (r,s) => r.sprite = s);
    internal void RestoreNativeSprite() => paint.Release();
    private void Start() => Apply();
    private void OnDestroy()
    {
        if (Data != null) StructureSaveState.Forget(Data);
    }
    internal void Apply()
    {
        if (Data == null) return;
        if (Data.extender && GetComponentInChildren<NPCInteractionRangeExtender>(true) == null)
            addedExtender = gameObject.AddComponent<NPCInteractionRangeExtender>();
        else if (!Data.extender)
            RemoveAddedExtender();
        StructureTransfer.ApplySpriteVariantNow(gameObject, Data.prefabName, Data.spriteVariantIndex, paint);
    }
    // Never remove a component provided by the prefab or another mod.
    private void RemoveAddedExtender()
    {
        if (addedExtender != null) DestroyImmediate(addedExtender);
        addedExtender = null;
    }
    internal void ReleasePooledState()
    {
        if (Data != null) StructureSaveState.Forget(Data);
        Data = null;
        RestoreNativeSprite();
        RemoveAddedExtender();
        DestroyImmediate(this);
    }
}
