#nullable enable
using System;
using System.Collections.Generic;

namespace StructureHandler;

internal static class DestinationSupportPolicy
{
    internal static HashSet<(int X, int Y)> GetUnreplacedCells(StructureFile file,
        IEnumerable<(int X, int Y)> occupied,
        Func<float, float, (int X, int Y)> cellOf, Func<string, PlacementSlot>? classify = null)
    {
        classify ??= StructurePlacementPolicy.Slot;
        var cells = new HashSet<(int X, int Y)>(occupied);
        // An editor object does not implicitly supply a floor. Only incoming
        // ground that will actually be instantiated replaces destination ground.
        // Bath/shallow water are overlays: retain destination ground beneath them.
        // Check both lists for old/raw JSONs, without changing the portable file.
        foreach (var item in file.supportingTerrain)
            if (classify(item.prefabName) == PlacementSlot.Ground)
                cells.Remove((item.x, item.y));
        foreach (var item in file.objects)
            if (classify(item.prefabName) == PlacementSlot.Ground)
                cells.Remove(cellOf(item.x, item.y));
        return cells;
    }

}
