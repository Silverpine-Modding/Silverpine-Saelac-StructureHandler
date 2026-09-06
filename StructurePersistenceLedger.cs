#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace StructureHandler;

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

    internal void Move(string previousKey, StructureObject current)
    {
        Objects.Remove(previousKey);
        Objects[Key(current)] = current;
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
