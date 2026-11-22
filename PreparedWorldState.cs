#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace StructureHandler;

// Parse into detached collections. ModdingTools retains a rejected payload,
// but doesn't undo changes made by a Restore callback that throws.
internal sealed class PreparedWorldState
{
    internal readonly HashSet<(int X, int Y)> Imported, Protected, Buildable;
    internal readonly List<PendingTileImport> Pending;
    internal readonly StructurePersistenceLedger Ledger = new();
    internal readonly Dictionary<string, StructureAppearance> Appearances = new(StringComparer.OrdinalIgnoreCase);
    internal readonly Dictionary<string, RegeneratingResourceMarker> Resources;

    internal PreparedWorldState(StructureWorldState state, Func<string,string>? canonicalName = null)
    {
        Imported = ImportFootprint.Read(state.importedCells);
        Protected = ImportFootprint.Read(state.protectedTiles);
        Buildable = ImportFootprint.Read(state.buildableTiles);
        Pending = StructurePlans.RehomePending(state.pending ?? new());
        // Rekey detached records before publishing. An old name must not cause
        // us to respawn an object already loaded under its current ID.
        if (canonicalName != null)
        {
            void Scenery(List<StructureObject>? items) => NormalizeKeys(items, o=>o.prefabName, (o,n)=>o.prefabName=n,
                StructurePersistenceLedger.Key, canonicalName);
            Scenery(state.supplementalObjects); Scenery(state.clearedScenery);
            NormalizeKeys(state.appearances, a=>a.prefabName, (a,n)=>a.prefabName=n,
                a=>StructurePersistenceLedger.Key(a.prefabName,a.x,a.y,a.z), canonicalName);
        }
        Ledger.Restore(state.supplementalObjects, state.clearedScenery);
        Resources = ReadResources(state.resources, canonicalName);
        foreach (var a in state.appearances ?? new())
        {
            if (a == null || !Valid(a.prefabName, a.x, a.y, a.z))
                throw new InvalidDataException("Invalid furniture appearance record.");
            Appearances[StructurePersistenceLedger.Key(a.prefabName, a.x, a.y, a.z)] = a;
        }
    }

    private static void NormalizeKeys<T>(List<T>? records, Func<T,string> get, Action<T,string> set,
        Func<T,string> key, Func<string,string> canonical) where T:class
    {
        if (records == null || records.Count == 0) return;
        var origins = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in records)
        {
            if (r == null || string.IsNullOrWhiteSpace(get(r))) throw new InvalidDataException("Invalid saved prefab record.");
            string original = get(r);
            set(r, canonical(original));
            string k = key(r);
            if (origins.TryGetValue(k,out var prior) && !string.Equals(original,prior,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Saved prefab IDs '{prior}' and '{original}' now identify the same record at {k}; the original data was retained.");
            origins[k] = original;
        }
    }

    internal static Dictionary<string, RegeneratingResourceMarker> ReadResources(IEnumerable<RegeneratingResourceMarker>? markers,
        Func<string,string>? canonical = null)
    {
        var result = new Dictionary<string, RegeneratingResourceMarker>(StringComparer.OrdinalIgnoreCase);
        if (markers != null && canonical != null)
        {
            var list = markers as List<RegeneratingResourceMarker> ?? new List<RegeneratingResourceMarker>(markers);
            NormalizeKeys(list, m=>m.prefabName, (m,n)=>m.prefabName=n, ResourceKey, canonical);
            markers = list;
        }
        foreach (var m in markers ?? Array.Empty<RegeneratingResourceMarker>())
        {
            if (m == null || !Valid(m.prefabName, m.x, m.y, m.z))
                throw new InvalidDataException("Invalid regenerating resource marker.");
            result[ResourceKey(m)] = m;
        }
        return result;
    }

    internal static string ResourceKey(RegeneratingResourceMarker m) =>
        FormattableString.Invariant($"{m.prefabName}|{(int)Math.Round(m.x, MidpointRounding.ToEven)}|{(int)Math.Round(m.y, MidpointRounding.ToEven)}");
    private static bool Valid(string name, float x, float y, float z) =>
        !string.IsNullOrWhiteSpace(name) && StructurePlacementPolicy.Supported(x) &&
        StructurePlacementPolicy.Supported(y) && StructurePlacementPolicy.Supported(z);
}
