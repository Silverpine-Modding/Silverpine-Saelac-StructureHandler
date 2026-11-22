#nullable enable
using System;
using System.Collections.Generic;

namespace StructureHandler;

// This is evidence from a blueprint, not a query against the currently loaded world.
internal enum RoofStatus { Empty, Unknown, Outdoor, Roofed }

internal sealed class RoofPreview
{
    internal sealed class Prefab
    {
        internal bool Ground;
        // Null path: this tag has no Deroofer on it.
        internal readonly List<(float X, float Y, string? Path, bool DefaultRemoved)> Tags = new();
    }

    [Flags] private enum Evidence : byte { Ground = 1, Unresolved = 2, Roof = 4 }
    private readonly Dictionary<(int, int), Evidence> cells = new();
    internal void Clear() => cells.Clear();
    private void Add(int x, int y, Evidence evidence)
    {
        cells.TryGetValue((x, y), out var previous);
        cells[(x, y)] = previous | evidence;
    }

    internal void Unresolved(int x, int y) => Add(x, y, Evidence.Unresolved);

    internal void Record(float x, float y, Prefab prefab, List<StructureComponent>? components)
    {
        // TileTags registers the final transform position, not the rounded
        // anchor plus a separately rounded offset (different for nudged objects).
        static int Cell(float value) => (int)Math.Round(value, MidpointRounding.ToEven);
        if (prefab.Ground) Add(Cell(x), Cell(y), Evidence.Ground);
        foreach (var tag in prefab.Tags)
        {
            int tx = Cell(x + tag.X), ty = Cell(y + tag.Y);
            bool? removed = tag.Path == null ? false : Removed(tag.Path, tag.DefaultRemoved, components);
            if (removed == false) Add(tx, ty, Evidence.Roof);
            else if (removed == null) Unresolved(tx, ty);
        }
    }

    private static bool? Removed(string path, bool fallback, List<StructureComponent>? components)
    {
        // Native Deroofer serializes a single Boolean; do not run Deserialize
        // or Start on a live instance just to inspect the editor document.
        if (components != null)
            for (int i = components.Count - 1; i >= 0; --i)
            {
                var item = components[i];
                if (item == null || (item.hierarchyPath ?? "") != path || item.componentIndex is not (-1 or 0) ||
                    !SavedComponentBinding.IsNative(item.type, "Deroofer")) continue;
                Span<byte> data = stackalloc byte[1];
                return Convert.TryFromBase64String(item.dataBase64 ?? "", data, out int n) && n == 1
                    ? data[0] != 0 : (bool?)null;
            }
        return fallback;
    }

    internal RoofStatus At(int x, int y, bool occupied)
    {
        if (!cells.TryGetValue((x, y), out var e)) return occupied ? RoofStatus.Unknown : RoofStatus.Empty;
        // Native HasTileTag is additive: one confirmed roof wins regardless
        // of what another prefab on that cell contributes.
        if ((e & Evidence.Roof) != 0) return RoofStatus.Roofed;
        if ((e & Evidence.Unresolved) != 0) return RoofStatus.Unknown;
        return RoofStatus.Outdoor;
    }
}
