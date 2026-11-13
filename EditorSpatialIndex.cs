#nullable enable
using System;
using System.Collections.Generic;

namespace StructureHandler;

// Sprite extents, not anchor cells: overhanging/multi-tile sprites remain visible.
internal readonly struct PreviewBounds : IEquatable<PreviewBounds>
{
    internal readonly float Left, Bottom, Right, Top;
    internal PreviewBounds(float left, float bottom, float right, float top)
    { Left = left; Bottom = bottom; Right = right; Top = top; }
    internal bool Overlaps(PreviewBounds other) => Left <= other.Right && Right >= other.Left && Bottom <= other.Top && Top >= other.Bottom;
    public bool Equals(PreviewBounds other) => Left == other.Left && Bottom == other.Bottom && Right == other.Right && Top == other.Top;
}

internal sealed class PreviewSpatialIndex
{
    private const int ChunkSize = 8;
    private readonly Dictionary<(int, int), List<int>> chunks = new();
    private readonly Dictionary<int, PreviewBounds> bounds = new();
    private readonly List<int> oversized = new();
    private readonly HashSet<int> seen = new();
    internal void Clear() { chunks.Clear(); bounds.Clear(); oversized.Clear(); seen.Clear(); }
    private static int Chunk(float value) => (int)Math.Floor(value / ChunkSize);
    internal void Add(int id, PreviewBounds box)
    {
        bounds.Add(id, box);
        int left = Chunk(box.Left), right = Chunk(box.Right), bottom = Chunk(box.Bottom), top = Chunk(box.Top);
        if (((long)right - left + 1) * ((long)top - bottom + 1) > 256)
        { oversized.Add(id); return; }
        for (int x = left; x <= right; x++)
        for (int y = bottom; y <= top; y++)
        {
            if (!chunks.TryGetValue((x, y), out var list)) chunks[(x, y)] = list = new();
            list.Add(id);
        }
    }
    internal void Query(PreviewBounds viewport, List<int> result)
    {
        result.Clear(); seen.Clear();
        void Include(int id) { if (seen.Add(id) && bounds[id].Overlaps(viewport)) result.Add(id); }
        int left = Chunk(viewport.Left), right = Chunk(viewport.Right), bottom = Chunk(viewport.Bottom), top = Chunk(viewport.Top);
        // Avoid pathological query loops for oversized test/custom viewports.
        if (((long)right - left + 1) * ((long)top - bottom + 1) > 65536)
            foreach (int id in bounds.Keys) Include(id);
        else
        {
            for (int x = left; x <= right; x++)
            for (int y = bottom; y <= top; y++)
                if (chunks.TryGetValue((x, y), out var list)) foreach (int id in list) Include(id);
            foreach (int id in oversized) Include(id);
        }
        result.Sort(); // IDs are ranks in the globally sorted native draw order.
    }
}

internal sealed class EditorCellIndex<T> where T : class
{
    private readonly Dictionary<(int, int), HashSet<T>> cells = new();
    private readonly Dictionary<T, HashSet<(int, int)>> reverse = new();
    internal void Clear() { cells.Clear(); reverse.Clear(); }
    internal void Remove(T record)
    {
        if (!reverse.TryGetValue(record, out var previous)) return;
        foreach (var cell in previous)
            if (cells.TryGetValue(cell, out var entries))
            { entries.Remove(record); if (entries.Count == 0) cells.Remove(cell); }
        reverse.Remove(record);
    }
    internal void Set(T record, IEnumerable<(int, int)> occupied)
    {
        Remove(record);
        var footprint = new HashSet<(int, int)>(occupied);
        reverse[record] = footprint;
        foreach (var cell in footprint)
        {
            if (!cells.TryGetValue(cell, out var entries)) cells[cell] = entries = new();
            entries.Add(record);
        }
    }
    internal void Collect(IEnumerable<(int, int)> occupied, HashSet<T> result)
    {
        result.Clear();
        foreach (var cell in occupied)
            if (cells.TryGetValue(cell, out var entries)) result.UnionWith(entries);
    }
}
