#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace StructureHandler;

// Ephemeral save-switch undo, deliberately separate from the persisted ledger.
// Only scenery in a native persistence zone survives a world-tile transition.
internal sealed class SuppressedSceneOriginals<TScene>
{
    private readonly Dictionary<string, (StructureObject Record, TScene Scene)> entries = new(StringComparer.OrdinalIgnoreCase);
    internal sealed class Batch
    {
        private readonly SuppressedSceneOriginals<TScene> owner;
        private readonly Dictionary<string, (StructureObject Record, TScene Scene)> pending = new(StringComparer.OrdinalIgnoreCase);
        internal Batch(SuppressedSceneOriginals<TScene> owner) { this.owner = owner; }
        internal void Remember(string key, bool resident, TScene scene, Func<StructureObject> snapshot)
        {
            if (resident && !owner.entries.ContainsKey(key) && !pending.ContainsKey(key))
                pending.Add(key, (snapshot(), scene));
        }
        internal void Commit()
        {
            foreach (var pair in pending) owner.entries.TryAdd(pair.Key, pair.Value);
            pending.Clear();
        }
    }
    internal Batch Begin() => new(this);
    internal void ClearTile(Func<StructureObject, bool> belongs)
    {
        var remove = new List<string>();
        foreach (var pair in entries) if (belongs(pair.Value.Record)) remove.Add(pair.Key);
        foreach (string key in remove) entries.Remove(key);
    }
    internal IEnumerable<(StructureObject Record, TScene Scene)> Restorable(
        Func<TScene, bool> loaded, Func<StructureObject, bool> resident)
    {
        foreach (var original in entries.Values)
            if (loaded(original.Scene) && resident(original.Record)) yield return original;
    }
    internal void Clear() => entries.Clear();
}

// No Unity state: the companion stores only scenery the native serializer
// misses, never historical copies of chests, furniture, crops, or NPCs.
internal sealed class StructurePersistenceLedger
{
    internal readonly Dictionary<string, StructureObject> Objects = new(StringComparer.OrdinalIgnoreCase);
    internal readonly Dictionary<string, StructureObject> Cleared = new(StringComparer.OrdinalIgnoreCase);

    internal static bool NeedsSupplemental(bool rootSerializable, bool turfRegistered, bool persistent) =>
        !rootSerializable || (!turfRegistered && !persistent);

    internal static string Key(StructureObject item) => Key(item.prefabName, item.x, item.y, item.z);
    internal static string Key(string prefab, float x, float y, float z) =>
        prefab + "|" + x.ToString("R", CultureInfo.InvariantCulture) + "|" +
        y.ToString("R", CultureInfo.InvariantCulture) + "|" + z.ToString("R", CultureInfo.InvariantCulture);

    internal void Reset() { Objects.Clear(); Cleared.Clear(); }

    internal void Restore(IEnumerable<StructureObject>? objects, IEnumerable<StructureObject>? cleared)
    {
        // Validate both collections before replacing any recoverable state.
        var nextObjects = Read(objects);
        var nextCleared = Read(cleared);
        Reset();
        foreach (var pair in nextObjects) Objects.Add(pair.Key, pair.Value);
        foreach (var pair in nextCleared) Cleared.Add(pair.Key, pair.Value);
    }

    private static Dictionary<string, StructureObject> Read(IEnumerable<StructureObject>? records)
    {
        var result = new Dictionary<string, StructureObject>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in records ?? Array.Empty<StructureObject>())
        {
            if (item == null || string.IsNullOrWhiteSpace(item.prefabName) ||
                !StructureAlgorithms.IsFinite(item.x) || !StructureAlgorithms.IsFinite(item.y) ||
                !StructureAlgorithms.IsFinite(item.z) || Math.Abs(item.x) > 10000000 || Math.Abs(item.y) > 10000000)
                throw new InvalidDataException("Invalid supplemental scenery record.");
            result[Key(item)] = item;
        }
        return result;
    }

    internal void RecordReplacement(IEnumerable<StructureObject> removed, IEnumerable<StructureObject> created)
    {
        foreach (var item in removed)
        {
            string key = Key(item);
            // Replacing an earlier imported tile must not turn it into original
            // scenery that would come back when switching saves.
            if (!Objects.Remove(key)) Cleared.TryAdd(key, item);
        }
        foreach (var item in created) Objects[Key(item)] = item;
    }

    internal static void MoveAppearanceRecords(Dictionary<string, StructureAppearance> records,
        IReadOnlyList<(StructureAppearance Data, float X, float Y, float Z)> moves)
    {
        if (moves.Count == 0) return;
        var ownedMoves = new List<(StructureAppearance Data, float X, float Y, float Z)>(moves.Count);
        foreach (var move in moves)
            if (ForgetOwnedAppearance(records, move.Data)) ownedMoves.Add(move);
        foreach (var move in ownedMoves)
        {
            var data = move.Data;
            data.x = move.X; data.y = move.Y; data.z = move.Z;
            records[Key(data.prefabName, data.x, data.y, data.z)] = data;
        }
    }

    internal static bool ForgetOwnedAppearance(Dictionary<string,StructureAppearance> records, StructureAppearance data)
    {
        string key = Key(data.prefabName, data.x, data.y, data.z);
        return records.TryGetValue(key, out var owner) && ReferenceEquals(owner, data) && records.Remove(key);
    }

    internal void ReplaceTracked(IEnumerable<string> oldKeys, IEnumerable<StructureObject> records)
    {
        foreach (string key in oldKeys) Objects.Remove(key);
        foreach (var record in records) Objects[Key(record)] = record;
    }

    internal void ClearWhere(Func<StructureObject, bool> belongsToTile)
    {
        RemoveWhere(Objects, belongsToTile);
        RemoveWhere(Cleared, belongsToTile);
    }

    private static void RemoveWhere(Dictionary<string, StructureObject> records, Func<StructureObject, bool> predicate)
    {
        var removed = new List<string>();
        foreach (var pair in records) if (predicate(pair.Value)) removed.Add(pair.Key);
        foreach (string key in removed) records.Remove(key);
    }
}
