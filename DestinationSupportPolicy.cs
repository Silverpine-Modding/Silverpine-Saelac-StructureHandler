#nullable enable
using System;
using System.Collections.Generic;

namespace StructureHandler;

internal static class DestinationSupportPolicy
{
    internal static HashSet<(int X, int Y)> GetUnreplacedCells(StructureFile file,
        IEnumerable<(int X, int Y)> occupied,
        Func<float, float, (int X, int Y)> cellOf)
    {
        var cells = new HashSet<(int X, int Y)>(occupied);
        // An editor object does not implicitly supply a floor. Only incoming
        // ground that will actually be instantiated replaces destination ground.
        // Bath/shallow water are overlays: retain destination ground beneath them.
        // Check both lists for old/raw JSONs, without changing the portable file.
        foreach (var item in file.supportingTerrain)
            if (IsTerrain(item.prefabName))
                cells.Remove((item.x, item.y));
        foreach (var item in file.objects)
            if (IsTerrain(item.prefabName))
                cells.Remove(cellOf(item.x, item.y));
        return cells;
    }

    private static bool IsTerrain(string name) =>
        StructurePlacementPolicy.Slot(name) == PlacementSlot.Ground;
}
