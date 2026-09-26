#nullable enable
using System;
using System.Collections.Generic;

namespace StructureHandler;

internal static class ZonePainting
{
    // Entries come from the editor's cell index. Change metadata only: never
    // replace prefabs, decode/overwrite component state, or create empty tiles.
    internal static int Apply(StructureFile file, IEnumerable<Tuple<bool, int>> entries, string zone)
    {
        int changed = 0;
        foreach (var entry in entries)
        {
            if (entry.Item1)
            {
                var tile = file.supportingTerrain[entry.Item2];
                if (tile.hasMapZoneName && tile.mapZoneName == zone) continue;
                tile.hasMapZoneName = true;
                tile.mapZoneName = zone;
            }
            else
            {
                var tile = file.objects[entry.Item2];
                // Legacy tile records are accepted, but walls/furniture must
                // not gain a zone merely because they overlap painted ground.
                if (!tile.prefabName.StartsWith("prefab_tile_", StringComparison.OrdinalIgnoreCase) ||
                    (tile.hasMapZoneName && tile.mapZoneName == zone)) continue;
                tile.hasMapZoneName = true;
                tile.mapZoneName = zone;
            }
            changed++;
        }
        return changed;
    }
}

internal sealed class ZonePaintStroke
{
    private bool dragging;
    private (int X, int Y)? previous;

    internal IEnumerable<(int X, int Y)> Trace(int x, int y, bool begin)
    {
        if (begin) { dragging = true; previous = null; }
        if (!dragging) return Array.Empty<(int, int)>();
        var from = previous ?? (x, y);
        previous = (x, y);
        return PlanningTiles.Stroke(from.Item1, from.Item2, x, y);
    }

    // Leaving the preview, zooming, or panning must not paint a connecting line.
    internal void BreakPath() => previous = null;
    internal void End() { dragging = false; previous = null; }
}
