#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StructureHandler;

internal static class StructureTerrainPersistence
{
    internal static StructurePersistenceLedger Ledger { get; set; } = new();
    private static readonly Dictionary<string, GameObject> Live = new(StringComparer.OrdinalIgnoreCase);
    // Runtime-only undo of suppressed native scenery when loading another save
    // in the same scene. Never inject one save's edits into another save.
    private static readonly SuppressedSceneOriginals<Scene> Suppressed = new();
    private static bool changingSceneState;

    internal static bool NeedsSupplemental(GameObject item) =>
        StructurePersistenceLedger.NeedsSupplemental(
            item.GetComponent<ISerializableMonoBehavior>() != null,
            item.GetComponent<TurfRegistrar>() != null,
            item.GetComponent<PersistentEntity>() != null ||
            PersistenceZone.IsInsideAnyPersistenceZone(item.transform.GetVector2IntPosition()));

    private static bool SafeScenery(GameObject item) =>
        item != null && item.activeInHierarchy &&
        NeedsSupplemental(item) && !WorldPrefabPolicy.ProtectedFromRemoval(item);

    private static string Key(GameObject item)
    {
        Vector3 p = item.transform.position;
        return StructurePersistenceLedger.Key(Plugin.PrefabNameOf(item), p.x, p.y, p.z);
    }

    internal static SuppressedSceneOriginals<Scene>.Batch StageOriginals(IEnumerable<GameObject> originals)
    {
        var batch = Suppressed.Begin();
        foreach (var item in originals.Where(SafeScenery))
        {
            // Only always-resident scene scenery can be undone across saves.
            // A generated tile's original belongs to that world's generation,
            // not to the next save that happens to use this Unity scene.
            string key = Key(item);
            if (Live.TryGetValue(key, out var tracked) && ReferenceEquals(tracked, item)) continue;
            batch.Remember(key, PersistenceZone.IsInsideAnyPersistenceZone(item.transform.GetVector2IntPosition()),
                item.scene, () => StructureTransfer.CreateObjectRecords(new[] { item }).Single());
        }
        return batch;
    }

    internal static void RecordImport(IEnumerable<StructureObject> removedNonNative, IEnumerable<GameObject> created)
    {
        var extras = created.Where(SafeScenery).ToArray();
        Ledger.RecordReplacement(removedNonNative, StructureTransfer.CreateObjectRecords(extras));
        AttachExisting(extras);
    }

    internal static void RecordRestoredGround(GameObject ground)
    {
        // Intentional deconstruction supersedes this original's clearance
        // record; it must not be suppressed again on loading the save.
        Ledger.Cleared.Remove(Key(ground));
        if (!SafeScenery(ground)) return;
        Ledger.RecordReplacement(Array.Empty<StructureObject>(), StructureTransfer.CreateObjectRecords(new[] { ground }));
        AttachExisting(new[] { ground });
    }

    internal static void AttachExisting(IEnumerable<GameObject> instances)
    {
        foreach (var item in instances.Where(item => item != null && item.activeInHierarchy))
        {
            string key = Key(item);
            if (!Ledger.Objects.ContainsKey(key)) continue;
            // A prefab update may add native serialization support. Hand it back
            // to the native save instead of replaying a second copy every visit.
            if (!NeedsSupplemental(item)) { Ledger.Objects.Remove(key); continue; }
            if (!SafeScenery(item)) continue;
            var marker = item.GetComponent<StructureSupplementalInstance>() ?? item.AddComponent<StructureSupplementalInstance>();
            marker.Key = key;
            Live[key] = item;
        }
    }

    internal static void Forget(GameObject item, string key)
    {
        if (Live.TryGetValue(key, out var current) && ReferenceEquals(current, item))
        {
            Live.Remove(key);
            if (!changingSceneState && !StructureSaveState.Unloading &&
                !StructureSaveState.Mutating && !SerializationManager.loadingSave)
                Ledger.Objects.Remove(key);
        }
    }

    internal static void RefreshLive()
    {
        var previous = Live.ToArray();
        var current = new List<(string Key, StructureObject Record, GameObject Item, StructureSupplementalInstance Marker)>();
        foreach (var pair in previous)
        {
            var item = pair.Value;
            if (item == null || !item.activeInHierarchy) continue;
            var record = StructureTransfer.CreateObjectRecords(new[] { item }).Single();
            var marker = item.GetComponent<StructureSupplementalInstance>();
            if (marker == null) throw new InvalidOperationException("Missing supplemental tracking marker for " + record.prefabName);
            current.Add((StructurePersistenceLedger.Key(record), record, item, marker));
        }
        // Snapshot first: neither a serialization failure nor two objects
        // trading positions should remove another tracked object's record.
        Ledger.ReplaceTracked(previous.Select(p => p.Key), current.Select(c => c.Record));
        Live.Clear();
        foreach (var c in current)
        {
            Live[c.Key] = c.Item;
            c.Marker.Key = c.Key;
        }
    }

    private static bool IsLoaded(StructureObject item, Vector2Int currentTile) =>
        Plugin.GetWorldTile(new Vector2(item.x, item.y)) == currentTile ||
        PersistenceZone.IsInsideAnyPersistenceZone(new Vector2Int(Mathf.RoundToInt(item.x), Mathf.RoundToInt(item.y)));

    private static HashSet<GameObject> FindSceneMatches(IEnumerable<StructureObject> records)
    {
        var matches = new HashSet<GameObject>();
        foreach (var record in records)
        {
            string key = StructurePersistenceLedger.Key(record);
            var cell = new Vector2Int(Mathf.RoundToInt(record.x), Mathf.RoundToInt(record.y));
            foreach (var item in Turfs.GetTurfGameObjects(cell))
                if (SafeScenery(item) && string.Equals(Key(item), key, StringComparison.OrdinalIgnoreCase)) matches.Add(item);
            if (Live.TryGetValue(key, out var tracked) && SafeScenery(tracked)) matches.Add(tracked);
        }
        return matches;
    }

    internal static void RestoreLoaded(Vector2Int tile)
    {
        // Never overwrite an already native-saved object, including prefabs
        // whose mod gained native serialization since this companion was written.
        foreach (var pair in Ledger.Objects.Where(pair => IsLoaded(pair.Value, tile)).ToArray())
        {
            var cell = new Vector2Int(Mathf.RoundToInt(pair.Value.x), Mathf.RoundToInt(pair.Value.y));
            if (Turfs.GetTurfGameObjects(cell).Any(item => item != null && item.activeInHierarchy &&
                !NeedsSupplemental(item) && string.Equals(Key(item), pair.Key, StringComparison.OrdinalIgnoreCase)))
                Ledger.Objects.Remove(pair.Key);
        }
        var wanted = Ledger.Objects.Where(pair => IsLoaded(pair.Value, tile) &&
            (!Live.TryGetValue(pair.Key, out var item) || item == null || !item.activeInHierarchy))
            .Select(pair => pair.Value).ToArray();
        var cleared = Ledger.Cleared.Values.Where(item => IsLoaded(item, tile)).ToArray();
        if (wanted.Length == 0 && cleared.Length == 0) return;

        // A missing mod/prefab must fail before we remove any existing scenery.
        StructureTransfer.ValidateImportFiles(new[] { new StructureFile { objects = wanted.ToList() } });
        var originals = FindSceneMatches(cleared.Concat(wanted));
        // Repeated midnight/load notifications must not reset live imported objects.
        originals.RemoveWhere(item => Live.TryGetValue(Key(item), out var tracked) && ReferenceEquals(item, tracked));
        var backup = StructureTransfer.CreateObjectRecords(originals);
        StructureTransfer.ValidateImportFiles(new[] { new StructureFile { objects = backup } });
        var suppressed = StageOriginals(originals);
        var created = new List<GameObject>();
        changingSceneState = true;
        try
        {
            ImportTransaction.Execute(() =>
            {
                StructureTransfer.RemoveGameObjects(originals);
                StructureTransfer.InstantiateObjectRecords(wanted, created);
                StructureTransfer.RegisterRestoredObjects(created);
                ObjectPool.CallStarts();
                AttachExisting(created);
                suppressed.Commit();
            }, () =>
            {
                StructureTransfer.DiscardCreatedObjects(created);
                StructureTransfer.RemoveGameObjects(originals.Where(item => item != null && item.activeInHierarchy));
                var restored = new List<GameObject>();
                StructureTransfer.InstantiateObjectRecords(backup, restored);
                StructureTransfer.RegisterRestoredObjects(restored);
                ObjectPool.CallStarts();
            }, "saved supplemental terrain");
        }
        catch (ImportRecoveryException) { suppressed.Commit(); throw; }
        finally
        {
            changingSceneState = false;
            // Supplemental ground can replace tiles after native Start has
            // already built the neighboring edges. Cover restoration/rollback.
            StructureTransfer.RequestTileVisualRefresh(wanted.Concat(cleared).Concat(backup)
                .SelectMany(StructureEditorUI.GetObjectCells));
        }
    }

    internal static void BeforeUnload(Vector2Int tile)
    {
        RefreshLive();
        var unload = Live.Values.Where(item => item != null &&
            Plugin.GetWorldTile(item.transform.position) == tile &&
            !PersistenceZone.IsInsideAnyPersistenceZone(item.transform.GetVector2IntPosition())).ToArray();
        changingSceneState = true;
        try { StructureTransfer.RemoveGameObjects(unload); }
        finally { changingSceneState = false; }
    }

    internal static void ClearTile(Vector2Int tile)
    {
        bool Belongs(StructureObject item) => Plugin.GetWorldTile(new Vector2(item.x, item.y)) == tile;
        var discarded = Live.Where(pair => Ledger.Objects.TryGetValue(pair.Key, out var record) && Belongs(record))
            .Select(pair => pair.Value).ToArray();
        // Called only when the native regeneration-clear operation really ran.
        Suppressed.ClearTile(Belongs);
        Ledger.ClearWhere(Belongs);
        changingSceneState = true;
        try { StructureTransfer.RemoveGameObjects(discarded); }
        finally { changingSceneState = false; }
        // Drop tracking as well: a later save must not recreate wiped markers.
        foreach (var key in Live.Keys.Where(key => !Ledger.Objects.ContainsKey(key)).ToArray()) Live.Remove(key);
    }

    internal static void Reset()
    {
        changingSceneState = true;
        try
        {
            StructureTransfer.RemoveGameObjects(Live.Values.ToArray());
            Live.Clear();
            // Being in the same Unity scene does not mean a wilderness tile
            // is loaded. Never replay its previous generation into a new save.
            var originals = Suppressed.Restorable(s => s.IsValid() && s.isLoaded,
                r => PersistenceZone.IsInsideAnyPersistenceZone(new Vector2Int(Mathf.RoundToInt(r.x), Mathf.RoundToInt(r.y)))).ToArray();
            // An object can be present without a turf registrar, or its mod may
            // now serialize it natively. Check live scene identity, not whether
            // the old companion would be allowed to manage that instance.
            var cells = originals.Select(o => (o.Scene, new Vector2Int(Mathf.RoundToInt(o.Record.x), Mathf.RoundToInt(o.Record.y)))).ToHashSet();
            var missing = originals.GroupBy(o => o.Scene).ToDictionary(g => g.Key, g =>
                g.Select(o => StructurePersistenceLedger.Key(o.Record)).ToHashSet(StringComparer.OrdinalIgnoreCase));
            if (cells.Count != 0)
                foreach (var t in UnityEngine.Object.FindObjectsOfType<Transform>())
                    if (cells.Contains((t.gameObject.scene, t.GetVector2IntPosition())))
                        missing[t.gameObject.scene].Remove(Key(t.gameObject));
            foreach (var original in originals)
            {
                var scene = original.Scene;
                if (!missing[scene].Remove(StructurePersistenceLedger.Key(original.Record))) continue;
                var restored = new List<GameObject>();
                try
                {
                    StructureTransfer.InstantiateObjectRecords(new[] { original.Record }, restored);
                    foreach (var item in restored) SceneManager.MoveGameObjectToScene(item, scene);
                    StructureTransfer.RegisterRestoredObjects(restored);
                }
                catch (Exception error)
                {
                    StructureTransfer.DiscardCreatedObjects(restored);
                    Plugin.Log.LogError("Could not reset original scenery while switching saves: " + error);
                }
            }
            Suppressed.Clear();
            Ledger.Reset();
        }
        finally { changingSceneState = false; }
    }
}

// This is deliberately not ISerializableMonoBehavior: adding a component only
// to an instance would produce native component-count mismatches on save load.
internal sealed class StructureSupplementalInstance : MonoBehaviour
{
    internal string Key = "";
    private void OnDestroy() => StructureTerrainPersistence.Forget(gameObject, Key);
    internal void ReleasePooledState()
    {
        StructureTerrainPersistence.Forget(gameObject, Key);
        Key = "";
        DestroyImmediate(this);
    }
}
