#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StructureHandler;

internal enum PlacementSlot { None, Ground, Wall, WaterOverlay }

internal static class StructurePlacementPolicy
{
    private const string Tile = "prefab_tile_";
    private const string Snowy = "terrainconstruction_snowy_";
    private const string Interior = Tile + "terrainconstruction_interior_";
    private const string Overlay = Tile + "terrainconstruction_overlay_";

    internal static bool IsTerrainRecordName(string name) =>
        name.StartsWith(Tile, StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith(Snowy + Tile, StringComparison.OrdinalIgnoreCase);

    // Normalize only for classification. Keep the actual prefab ID for
    // instantiation/serialization, including Unity's numbered asset copies.
    private static string NativeSurfaceName(string name)
    {
        if (name.EndsWith(")", StringComparison.Ordinal))
        {
            int suffix = name.LastIndexOf(" (", StringComparison.Ordinal);
            if (suffix > 0 && int.TryParse(name.Substring(suffix + 2, name.Length - suffix - 3),
                System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _))
                name = name.Substring(0, suffix);
        }
        if (name.StartsWith(Snowy + Tile, StringComparison.OrdinalIgnoreCase)) return name.Substring(Snowy.Length);
        if (name.StartsWith(Overlay, StringComparison.OrdinalIgnoreCase)) return Tile + name.Substring(Overlay.Length);
        if (name.StartsWith(Interior, StringComparison.OrdinalIgnoreCase))
        {
            string suffix = name.Substring(Interior.Length);
            return Tile + (suffix.StartsWith("snowy_", StringComparison.OrdinalIgnoreCase) ? suffix.Substring(6) : suffix);
        }
        return name;
    }

    internal static PlacementSlot Slot(string name)
    {
        name = NativeSurfaceName(name);
        // These are directional sprite decorations spawned by OvergrowthTile
        // on a neighbor. Several edges can meet there; none supplies a floor.
        if (name.Equals(Tile + "grass_overgrowth", StringComparison.OrdinalIgnoreCase) ||
            name.Equals(Tile + "grass_overgrowth_swamp", StringComparison.OrdinalIgnoreCase) ||
            name.Equals(Tile + "resin_overgrowth", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith(Tile + "map_", StringComparison.OrdinalIgnoreCase)) return PlacementSlot.None;
        // These native translucent surfaces reveal the floor/terrain beneath
        // them. Share one overlay slot so water cannot stack on other water
        // overlays, without erasing the underlying ground or its zone.
        if (name.Equals("prefab_tile_water_bathhouse", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("prefab_tile_water_shallow", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("prefab_tile_water_shallow_swamp", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("prefab_tile_icy_slip", StringComparison.OrdinalIgnoreCase))
            return PlacementSlot.WaterOverlay;
        if (name.StartsWith("prefab_wall_", StringComparison.OrdinalIgnoreCase)) return PlacementSlot.Wall;
        // Transition/trigger tiles are utilities, not the cell's ground surface.
        if (name.StartsWith("prefab_tile_", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("teleporter", StringComparison.OrdinalIgnoreCase)) return PlacementSlot.Ground;
        return PlacementSlot.None;
    }

    internal static bool ValidateFootprint(StructureObject item, bool repairMissingAnchor = false)
    {
        var offsets = item.occupiedOffsets;
        if (offsets == null || offsets.Count == 0) return false; // Implicit single anchor.
        if (offsets.Count > 10000 || offsets.Any(o => o == null ||
            Math.Abs((long)o.x) > 10000 || Math.Abs((long)o.y) > 10000))
            throw new InvalidDataException($"Invalid occupied footprint for {item.prefabName}.");
        if (offsets.Any(o => o.x == 0 && o.y == 0)) return false;
        if (!repairMissingAnchor || offsets.Count >= 10000)
            throw new InvalidDataException($"{item.prefabName} at ({item.x}, {item.y}) has a footprint without its (0, 0) anchor. Correct its occupiedOffsets before importing.");
        offsets.Insert(0, new StructurePosition());
        return true;
    }

    internal static bool Supported(double value) => Math.Abs(value) <= 10000000d;

    internal static bool FitsAt(StructureObject item, float x, float y)
    {
        if (!Supported(x) || !Supported(y) || !Supported(item.z)) return false;
        double ix = Math.Round(x, MidpointRounding.ToEven), iy = Math.Round(y, MidpointRounding.ToEven);
        return item.occupiedOffsets == null || item.occupiedOffsets.All(o =>
            o != null && Supported(ix + o.x) && Supported(iy + o.y));
    }

    // Validate before normalization, rounding or indexing. Otherwise a rejected
    // document can already be installed in the editor, or its peripheral cells
    // can poison ImportFootprint.Read on the next save load.
    internal static int ValidateDocumentGeometry(StructureFile file, bool repairMissingAnchor = false)
    {
        int repaired = 0;
        if (file.objects != null)
        foreach (var item in file.objects)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.prefabName))
                throw new InvalidDataException("An object record or its prefab name is missing.");
            if (ValidateFootprint(item, repairMissingAnchor)) repaired++;
            if (!FitsAt(item, item.x, item.y))
                throw new InvalidDataException($"Invalid coordinates or footprint for {item.prefabName}. Coordinates must be within ±10,000,000.");
        }
        if (file.supportingTerrain != null)
        foreach (var item in file.supportingTerrain)
            if (item == null || string.IsNullOrWhiteSpace(item.prefabName) ||
                !Supported(item.x) || !Supported(item.y) || !Supported(item.z))
                throw new InvalidDataException("A terrain record has a missing prefab name or coordinates outside ±10,000,000.");
        return repaired;
    }

    internal static IEnumerable<(int X, int Y)> Cells(StructureObject item)
        => CellsAt(item, item.x, item.y);

    internal static IEnumerable<(int X, int Y)> CellsAt(StructureObject item, float worldX, float worldY)
    {
        int x = checked((int)Math.Round(worldX, MidpointRounding.ToEven));
        int y = checked((int)Math.Round(worldY, MidpointRounding.ToEven));
        yield return (x, y);
        if (item.occupiedOffsets != null)
            foreach (var offset in item.occupiedOffsets)
                if (offset.x != 0 || offset.y != 0) yield return (checked(x + offset.x), checked(y + offset.y));
    }

    internal static void RequireInsideExportBounds(IEnumerable<StructureObject> objects,
        int left, int top, int right, int bottom)
    {
        foreach (var item in objects)
        foreach (var c in Cells(item))
            if (c.X < left || c.X > right || c.Y > top || c.Y < bottom)
                throw new InvalidDataException($"{item.prefabName} at ({item.x}, {item.y}) extends outside the export bounds at ({c.X}, {c.Y}). Expand the bounds to include it, or exclude its anchor. No file was written.");
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
