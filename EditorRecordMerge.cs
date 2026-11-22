#nullable enable
using System;
using System.Collections.Generic;

namespace StructureHandler;

internal static class EditorRecordMerge
{
    // Position quickly partitions large blueprints. It is not the identity:
    // compare every persisted field, including nested component bytes, footprint
    // and sprite lock. A delimiter in sign/zone text cannot alias another field.
    private sealed class RecordComparer<T>(Func<T, int> hash) : IEqualityComparer<T> where T : class
    {
        public bool Equals(T? a, T? b) => EditorDocumentCopy.Equal(a, b);
        public int GetHashCode(T record) => hash(record);
    }

    internal static List<StructureObject> Objects(IEnumerable<StructureFile> files) =>
        Merge(files, f => f.objects, o => (o.prefabName, o.x, o.y, o.z).GetHashCode());
    internal static List<SupportingTerrain> Terrain(IEnumerable<StructureFile> files) =>
        Merge(files, f => f.supportingTerrain, o => (o.prefabName, o.x, o.y, o.z).GetHashCode());

    private static List<T> Merge<T>(IEnumerable<StructureFile> files, Func<StructureFile, List<T>> records,
        Func<T, int> hash) where T : class
    {
        var comparer = new RecordComparer<T>(hash);
        var maximum = new Dictionary<T, int>(comparer);
        var result = new List<T>();
        foreach (var file in files)
        {
            var counts = new Dictionary<T, int>(comparer);
            foreach (var record in records(file))
            {
                counts.TryGetValue(record, out int seen);
                counts[record] = ++seen;
                if (maximum.TryGetValue(record, out int previous) && previous >= seen) continue;
                // Keep intentional stacks within a source, but combining a
                // second export of the same stack must not double it.
                maximum[record] = seen;
                result.Add(record);
            }
        }
        return result;
    }
}
