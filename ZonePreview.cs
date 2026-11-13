using System;
using System.Collections.Generic;

namespace StructureHandler;

internal static class ZonePreview
{
    internal static Dictionary<(int X, int Y), string> Build(StructureFile file)
    {
        var result = new Dictionary<(int, int), string>();
        var floorCells = new HashSet<(int, int)>();
        void Add(string prefab, int x, int y, bool hasZone, string zone)
        {
            if (!prefab.StartsWith("prefab_tile_", StringComparison.OrdinalIgnoreCase) ||
                !hasZone || string.IsNullOrWhiteSpace(zone)) return;
            var cell = (x, y);
            bool ground = StructurePlacementPolicy.Slot(prefab) == PlacementSlot.Ground;
            // Prefer the floor's zone to a water overlay's zone at the same cell.
            if (floorCells.Contains(cell) && !ground) return;
            result[cell] = zone;
            if (ground) floorCells.Add(cell);
        }
        foreach (var item in file.objects)
            Add(item.prefabName, checked((int)Math.Round(item.x, MidpointRounding.ToEven)),
                checked((int)Math.Round(item.y, MidpointRounding.ToEven)), item.hasMapZoneName, item.mapZoneName);
        foreach (var item in file.supportingTerrain)
            Add(item.prefabName, item.x, item.y, item.hasMapZoneName, item.mapZoneName);
        return result;
    }

    internal static float Hue(string zone)
    {
        // Stable across launches/files, unlike the runtime string hash code.
        uint hash = 2166136261;
        foreach (char c in zone) hash = unchecked((hash ^ c) * 16777619);
        return (hash % 3600) / 3600f;
    }
}
