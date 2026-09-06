#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StructureHandler;

// Exact cells replaced by successful imports in this save, not world-tile
// protection and not a prohibition against active objects at those cells.
internal sealed class ImportFootprint
{
    private HashSet<(int X, int Y)> cells = new();

    internal bool Contains(int x, int y) => cells.Contains((x, y));
    internal void Record(IEnumerable<(int X, int Y)> imported) => cells.UnionWith(imported);
    internal void Reset() => cells.Clear();
    internal List<StructurePosition> Capture() => cells.OrderBy(p => p.X).ThenBy(p => p.Y)
        .Select(p => new StructurePosition { x = p.X, y = p.Y }).ToList();

    internal static HashSet<(int X, int Y)> Read(IEnumerable<StructurePosition>? records)
    {
        var result = new HashSet<(int X, int Y)>();
        foreach (var item in records ?? Array.Empty<StructurePosition>())
        {
            if (item == null || item.x < -10000000 || item.x > 10000000 ||
                item.y < -10000000 || item.y > 10000000)
                throw new InvalidDataException("Invalid imported-cell record.");
            result.Add((item.x, item.y));
        }
        return result;
    }

    internal void Restore(HashSet<(int X, int Y)> validated) => cells = new(validated);

    internal void ClearWorldTile(int x, int y) => cells.RemoveWhere(p =>
        Math.Floor((p.X + 50d) / 100d) == x && Math.Floor((p.Y + 50d) / 100d) == y);
}
