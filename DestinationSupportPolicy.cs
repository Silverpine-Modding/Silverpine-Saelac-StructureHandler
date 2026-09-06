#nullable enable
using System;
using System.Collections.Generic;

namespace StructureHandler;

internal static class DestinationSupportPolicy
{
    internal static HashSet<(int X, int Y)> GetUnreplacedCells(StructureFile file,
        IEnumerable<(int X, int Y)> occupied, Func<string, bool> includeTerrain,
        Func<float, float, (int X, int Y)> cellOf)
    {
        var cells = new HashSet<(int X, int Y)>(occupied);
        // An editor object does not implicitly supply a floor. Only incoming
        // terrain that will actually be instantiated replaces destination ground.
        // Check both lists for old/raw JSONs, without changing the portable file.
        foreach (var item in file.supportingTerrain)
            if (IsTerrain(item.prefabName) && includeTerrain(item.prefabName))
                cells.Remove((item.x, item.y));
        foreach (var item in file.objects)
            if (IsTerrain(item.prefabName))
                cells.Remove(cellOf(item.x, item.y));
        return cells;
    }

    private static bool IsTerrain(string name) =>
        name.StartsWith("prefab_tile_", StringComparison.OrdinalIgnoreCase);
}
