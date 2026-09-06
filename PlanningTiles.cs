#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace StructureHandler;

internal static class PlanningTiles
{
    // WorldTileGenerator places exits at local 0/99 on each axis. World-tile
    // origins are (tile * 100 - 50), including negative world coordinates.
    internal static bool IsTransitionAxis(int coordinate)
    {
        long local = ((long)coordinate + 50) % 100;
        if (local < 0) local += 100;
        return local == 0 || local == 99;
    }

    internal static bool IsTransitionCell(int x, int y) =>
        IsTransitionAxis(x) || IsTransitionAxis(y);

    internal static bool TryColor(string? input, out string color)
    {
        string value = (input ?? "").Trim().TrimStart('#');
        color = "";
        if (value.Length != 6 || value.Any(c => !Uri.IsHexDigit(c))) return false;
        color = "#" + value.ToUpperInvariant();
        return true;
    }

    internal static List<PlanningTile> Normalize(IEnumerable<PlanningTile>? tiles)
    {
        var result = new Dictionary<(int, int), PlanningTile>();
        foreach (var tile in tiles ?? Enumerable.Empty<PlanningTile>())
        {
            if (tile == null || Math.Abs((long)tile.x) > 10000000 ||
                Math.Abs((long)tile.y) > 10000000 || !TryColor(tile.color, out string color)) continue;
            // Last file wins for annotation conflicts when combining JSONs.
            result[(tile.x, tile.y)] = new PlanningTile { x = tile.x, y = tile.y, color = color };
        }
        return result.Values.ToList();
    }

    // Fill skipped cells between mouse events, including fast/diagonal drags.
    internal static IEnumerable<(int X, int Y)> Stroke(int x, int y, int endX, int endY)
    {
        int dx = Math.Abs(endX - x), dy = -Math.Abs(endY - y);
        int sx = x < endX ? 1 : -1, sy = y < endY ? 1 : -1;
        int error = dx + dy;
        while (true)
        {
            yield return (x, y);
            if (x == endX && y == endY) break;
            int twice = 2 * error;
            if (twice >= dy) { error += dy; x += sx; }
            if (twice <= dx) { error += dx; y += sy; }
        }
    }
}
