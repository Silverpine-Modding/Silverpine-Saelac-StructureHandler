using System;
using System.Collections.Generic;
namespace StructureHandler;
internal static class StructurePlans
{
    internal static Dictionary<(int X, int Y), StructureFile> Partition(StructureFile file,
        Func<float, float, (int, int)> tileOf, Func<string, bool> includeTerrain)
    {
        var parts = new Dictionary<(int, int), StructureFile>();
        StructureFile Part((int, int) tile)
        {
            if (!parts.TryGetValue(tile, out var part))
                parts[tile] = part = new StructureFile { name = file.name, formatVersion = 3 };
            return part;
        }
        foreach (var item in file.objects)
            Part(tileOf(item.x, item.y)).objects.Add(item);
        foreach (var item in file.supportingTerrain)
            if (includeTerrain(item.prefabName))
                Part(tileOf(item.x, item.y)).supportingTerrain.Add(item);
        return parts;
    }
}
