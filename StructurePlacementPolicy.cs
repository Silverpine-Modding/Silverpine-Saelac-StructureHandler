#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace StructureHandler;

internal enum PlacementSlot { None, Ground, Wall, WaterOverlay }

internal static class StructurePlacementPolicy
{
    internal static PlacementSlot Slot(string name)
    {
        // These native translucent surfaces reveal the floor/terrain beneath
        // them. Share one overlay slot so water cannot stack on other water
        // overlays, without erasing the underlying ground or its zone.
        if (name.Equals("prefab_tile_water_bathhouse", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("prefab_tile_water_shallow", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("prefab_tile_water_shallow_swamp", StringComparison.OrdinalIgnoreCase))
            return PlacementSlot.WaterOverlay;
        if (name.StartsWith("prefab_wall_", StringComparison.OrdinalIgnoreCase)) return PlacementSlot.Wall;
        // Transition/trigger tiles are utilities, not the cell's ground surface.
        if (name.StartsWith("prefab_tile_", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("teleporter", StringComparison.OrdinalIgnoreCase)) return PlacementSlot.Ground;
        return PlacementSlot.None;
    }

    internal static IEnumerable<(int X, int Y)> Cells(StructureObject item)
    {
        int x = checked((int)Math.Round(item.x, MidpointRounding.ToEven));
        int y = checked((int)Math.Round(item.y, MidpointRounding.ToEven));
        if (item.occupiedOffsets == null || item.occupiedOffsets.Count == 0)
            yield return (x, y);
        else
            foreach (var offset in item.occupiedOffsets)
                yield return (checked(x + offset.x), checked(y + offset.y));
    }

    internal static int ReplaceSlot(StructureFile file, string name,
        IEnumerable<(int X, int Y)> cells, object? keep = null,
        Func<string, PlacementSlot>? classify = null)
    {
        classify ??= Slot;
        PlacementSlot slot = classify(name);
        if (slot == PlacementSlot.None) return 0;
        var targets = new HashSet<(int, int)>(cells);
        int count = file.supportingTerrain.RemoveAll(item => !ReferenceEquals(item, keep) &&
            classify(item.prefabName) == slot && targets.Contains((item.x, item.y)));
        return count + file.objects.RemoveAll(item => !ReferenceEquals(item, keep) &&
            classify(item.prefabName) == slot && Cells(item).Any(targets.Contains));
    }

    // Read-only. Used at the user-file boundary, never for rollback snapshots.
    internal static string? FindConflict(StructureFile file,
        Func<string, PlacementSlot>? classify = null)
    {
        classify ??= Slot;
        var occupied = new Dictionary<(int, int, PlacementSlot), (object Record, string Name)>();
        string? Add(object record, string name, int x, int y)
        {
            PlacementSlot slot = classify(name);
            if (slot == PlacementSlot.None) return null;
            var key = (x, y, slot);
            if (occupied.TryGetValue(key, out var other) && !ReferenceEquals(record, other.Record))
                return $"{slot} overlap at ({x}, {y}): {other.Name} and {name}. " +
                    "Open the JSON in the editor and move/delete the extra record (or repaint that slot) before saving/importing.";
            occupied[key] = (record, name);
            return null;
        }
        foreach (var item in file.supportingTerrain ?? new List<SupportingTerrain>())
        {
            string? conflict = Add(item, item.prefabName, item.x, item.y);
            if (conflict != null) return conflict;
        }
        foreach (var item in file.objects ?? new List<StructureObject>())
        {
            if (classify(item.prefabName) == PlacementSlot.None) continue;
            foreach (var cell in Cells(item))
            {
                string? conflict = Add(item, item.prefabName, cell.X, cell.Y);
                if (conflict != null) return conflict;
            }
        }
        return null;
    }

    // Mirrors the static checks in PlayerAbility_Construct, PlacementModeManager
    // and ItemComponent_Pickupable. Player reach/ownership/materials have no
    // meaning in an offline blueprint editor.
    internal static string? Rejection(bool construction, bool pickupable,
        int snappingOffset, bool allowWallPlacement, bool water, bool blocked, bool wallBlocked,
        bool ignorePlacementRules = false)
    {
        if (ignorePlacementRules) return null;
        if (construction && water) return "The game does not allow construction on submerged terrain.";
        if ((construction || (pickupable && snappingOffset != -1)) && blocked)
            return "The game does not allow snapped placement on an impassable collider.";
        if (pickupable && !allowWallPlacement && wallBlocked)
            return "This furniture does not allow placement on walls.";
        return null;
    }

    internal static bool ShouldSnap(bool ignorePlacementRules, bool construction, bool pickupable, int snappingOffset) =>
        !ignorePlacementRules && (construction || pickupable) && snappingOffset != -1;

    internal static float NudgeStep(float requested, bool snap) =>
        snap ? Math.Max(1f, (float)Math.Round(requested)) : requested;
}
