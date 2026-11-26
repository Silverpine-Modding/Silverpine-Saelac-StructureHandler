#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using Silverpine.ModdingTools;
using UnityEngine;

namespace StructureHandler;

// Temporary, no menu or per-frame world scan. Retire this migration in
// 1.2.41; keep GrassZoneLifecycle and accept v5 companions after retirement.
internal static class GrassZoneRepair
{
    private static readonly bool Enabled = LegacyGrassIndex.Enabled(Plugin.PluginVersion);
    private static LegacyGrassRepairState? state;
    private static Task<List<LegacyGrassCell>>? job;
    private static CancellationTokenSource? cancellation;
    private static bool deferred;
    private static readonly HashSet<(int,int)> regeneratedDuringIndex = new();
    internal static string Upgrade(string payload)
    {
        if (!Enabled) return payload;
        var old = (StructureWorldState)StringSerializationAPI.Deserialize(typeof(StructureWorldState), payload);
        if (old.importedCells == null || old.importedCells.Count == 0) return payload;
        old.legacyGrassRepair = new LegacyGrassRepairState();
        return StringSerializationAPI.Serialize(typeof(StructureWorldState), old);
    }
    internal static void Restore(LegacyGrassRepairState? saved)
    {
        cancellation?.Cancel();
        cancellation?.Dispose();
        cancellation = null;
        job = null;
        deferred = false;
        regeneratedDuringIndex.Clear();
        state = Enabled ? saved : null;
    }
    internal static LegacyGrassRepairState? Capture()
    {
        // If the player saves immediately, finish the read-only index so a
        // later reload cannot accidentally classify newly edited grass as old.
        if (job != null) FinishIndex();
        return state;
    }
    private static void FinishIndex()
    {
        try
        {
            var cells = job!.GetAwaiter().GetResult();
            if (regeneratedDuringIndex.Count != 0)
                cells.RemoveAll(c => regeneratedDuringIndex.Contains(StructurePlans.TileOf(c.x,c.y)));
            if (cells.Count == 0) state = null;
            else state!.pending = cells;
        }
        catch (Exception e) { Defer(e); }
        finally { job = null; cancellation?.Dispose(); cancellation = null; }
    }
    private static void Defer(Exception e)
    {
        deferred = true;
        Plugin.Log.LogWarning("Old grass-zone repair deferred until next load: " + e.Message);
    }
    internal static void ForgetRegeneratedTile(Vector2Int tile)
    {
        if (state == null) return;
        if (job != null) regeneratedDuringIndex.Add((tile.x,tile.y));
        if (state.pending == null) return;
        state.pending.RemoveAll(c => c != null && StructurePlans.TileOf(c.x,c.y) == (tile.x,tile.y));
        if (state.pending.Count == 0) state = null;
    }
    private static void StartIndex(Vector2Int current)
    {
        var index = new LegacyGrassIndex(ImportFootprint.Read(StructureSaveState.ImportedCells.Capture()));
        // The loaded scene is authoritative over the current tile's old blob.
        // Persistent areas outside that tile are also live, never in WorldTile.owned.
        foreach (var zone in UnityEngine.Object.FindObjectsOfType<MapZone>())
        {
            if (!zone.gameObject.activeInHierarchy || !zone.gameObject.scene.isLoaded ||
                zone.transform.parent != null || string.IsNullOrWhiteSpace(zone.zoneName) ||
                PooledGrass.Id(zone.gameObject.name) == null) continue;
            var p = zone.transform.GetVector2IntPosition();
            if (zone.transform.position.x != p.x || zone.transform.position.y != p.y) continue;
            if (Plugin.GetWorldTile(p) == current || PersistenceZone.IsInsideAnyPersistenceZone(p))
                index.Observe(p.x,p.y,zone.gameObject.name,zone.zoneName);
        }
        var field = AccessTools.Field(typeof(WorldTile), "brotliBlob") ?? throw new MissingFieldException("WorldTile.brotliBlob");
        var blobs = new List<(int x,int y,byte[] data)>();
        foreach (var pair in DungeonGenerationManager.Instance.worldTiles)
            if (pair.Key != current && !pair.Value.IsDungeonInstance() && field.GetValue(pair.Value) is byte[] data)
                blobs.Add((pair.Key.x,pair.Key.y,data)); // Native code replaces, never mutates, these arrays.
        cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        job = Task.Run(() => {
            foreach (var b in blobs) index.ReadTile(b.data,(b.x,b.y),token);
            return index.Candidates();
        }, token);
        ScheduleCompletion(state!);
    }
    private static void ScheduleCompletion(LegacyGrassRepairState snapshot)
    {
        ActionQueue.Instance.DoAfterXFrames(12, () => {
            if (!ReferenceEquals(state,snapshot) || deferred) return;
            if (job != null && !job.IsCompleted) { ScheduleCompletion(snapshot); return; }
            if (StructureSaveState.TryCurrentTile(out var current)) OnTileReady(current);
        });
    }
    internal static void OnTileReady(Vector2Int tile)
    {
        if (state == null || deferred || StructureSaveState.EditBlockReason != null || StructureSaveState.Unloading ||
            StructureSaveState.Mutating || SerializationManager.loadingSave || ModSaveData.Warnings.Count != 0) return;
        try
        {
            if (state.pending == null)
            {
                if (job == null)
                {
                    if (!StructureSaveState.ImportedCells.HasHistory) { state = null; return; }
                    StartIndex(tile);
                    return;
                }
                if (!job.IsCompleted) return;
                FinishIndex();
                if (state?.pending == null) return;
            }
            var old = state.pending;
            var here = old.Where(c => c != null && StructurePlans.TileOf(c.x,c.y) == (tile.x,tile.y)).ToArray();
            if (here.Length == 0) return;
            int cleared = 0;
            foreach (var c in here)
            {
                var zone = Match(c);
                if (zone == null) continue;
                if (!state.backedUp)
                {
                    string backup = TerrainRepair.BackupLegacyZones(); // disk copy only; never calls Save
                    state.backedUp = true;
                    Plugin.Log.LogInfo("Pre-migration grass-zone save backup: " + backup);
                }
                zone.SetZoneName(""); // native registry and native serialized name change together
                ++cleared;
            }
            var finished = new HashSet<LegacyGrassCell>(here);
            old.RemoveAll(finished.Contains);
            if (old.Count == 0) state = null;
            if (cleared != 0) Plugin.Log.LogInfo($"Cleared {cleared} old grass zone label(s) in world tile {tile}. Save normally to keep the repair.");
        }
        catch (Exception e) { Defer(e); }
    }
    private static MapZone? Match(LegacyGrassCell c)
    {
        if (string.IsNullOrWhiteSpace(c.name) || c.name == "Outside" || PooledGrass.Id(c.prefab) != c.prefab ||
            StructureSaveState.ImportedCells.Contains(c.x,c.y)) return null;
        var cell = new Vector2Int(c.x,c.y);
        if (PersistenceZone.IsInsideAnyPersistenceZone(cell) ||
            !GrassZoneLifecycle.Registry.TryGetValue(cell, out var zone) || zone == null ||
            !zone.gameObject.activeInHierarchy || !zone.gameObject.scene.isLoaded || zone.transform.parent != null ||
            zone.transform.position.x != c.x || zone.transform.position.y != c.y || zone.zoneName != c.name ||
            PooledGrass.Id(zone.gameObject.name) != c.prefab) return null;
        var prefab = SerializationManager.GetPrefabFromName(c.prefab);
        if (prefab == null || !prefab.TryGetComponent<MapZone>(out var template) || !string.IsNullOrEmpty(template.zoneName)) return null;
        var turf = Turfs.GetTurfGameObjects(cell);
        return turf.Contains(zone.gameObject) && !turf.Any(g => g != null && g != zone.gameObject &&
            g.TryGetComponent<MapZone>(out _)) ? zone : null;
    }
}
