#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace StructureHandler;

internal static class PrefabFootprint
{
    private const int Limit = 10000;
    private static InvalidDataException Invalid(string name) =>
        new($"The footprint of '{name}' exceeds the supported size or has invalid bounds.");

    // Rectangular sprite coverage retains the existing edge rule. Validate
    // using doubles before narrowing/looping over dimensions from other mods.
    internal static List<StructurePosition> Sprite(string name, double left, double bottom, double right, double top)
    {
        var x0 = Math.Floor(left + .001); var y0 = Math.Floor(bottom + .001);
        var x1 = Math.Ceiling(right - .001) - 1; var y1 = Math.Ceiling(top - .001) - 1;
        CheckBounds(name, x0, y0, x1, y1, Limit);
        var cells = new List<StructurePosition>();
        for (int x = (int)x0; x <= x1; x++)
        for (int y = (int)y0; y <= y1; y++) cells.Add(new() { x = x, y = y });
        AddAnchor(name, cells);
        return cells;
    }

    // u and v are the transformed full-width and full-height vectors. Use the
    // bounding rectangle only to find candidates; do not claim its empty corners.
    internal static List<StructurePosition> Box(string name, double cx, double cy,
        double ux, double uy, double vx, double vy)
    {
        // A negative scale reverses the local axes without changing a centered
        // box's geometry. Keep the half-open edge choice independent of the
        // sign of either axis, including near-vertical floating-point vectors.
        if ((Math.Abs(ux) >= Math.Abs(uy) ? ux : uy) < 0) { ux = -ux; uy = -uy; }
        if ((Math.Abs(vx) >= Math.Abs(vy) ? vx : vy) < 0) { vx = -vx; vy = -vy; }
        double hx = (Math.Abs(ux) + Math.Abs(vx)) / 2, hy = (Math.Abs(uy) + Math.Abs(vy)) / 2;
        double x0 = Math.Floor(cx - hx), x1 = Math.Ceiling(cx + hx);
        double y0 = Math.Floor(cy - hy), y1 = Math.Ceiling(cy + hy);
        CheckBounds(name, x0, y0, x1, y1, Limit * 4, 1);
        var cells = new List<StructurePosition>();
        double det = ux * vy - uy * vx;
        if (det == 0) return cells;
        for (int x = (int)x0; x <= x1; x++)
        for (int y = (int)y0; y <= y1; y++)
        {
            double dx = x - cx, dy = y - cy;
            double a = (dx * vy - dy * vx) / det, b = (ux * dy - uy * dx) / det;
            if (a < -.500001 || a >= .499999 || b < -.500001 || b >= .499999) continue;
            if (cells.Count >= Limit || Math.Abs(x) > Limit || Math.Abs(y) > Limit) throw Invalid(name);
            cells.Add(new() { x = x, y = y });
        }
        if (cells.Count > 0) AddAnchor(name, cells);
        return cells;
    }

    private static void CheckBounds(string name, double x0, double y0, double x1, double y1, int budget, int margin = 0)
    {
        if (!(Math.Abs(x0) <= Limit + margin && Math.Abs(x1) <= Limit + margin &&
            Math.Abs(y0) <= Limit + margin && Math.Abs(y1) <= Limit + margin &&
            Math.Max(0, x1 - x0 + 1) * Math.Max(0, y1 - y0 + 1) <= budget)) throw Invalid(name);
    }

    private static void AddAnchor(string name, List<StructurePosition> cells)
    {
        if (cells.Exists(p => p.x == 0 && p.y == 0)) return;
        if (cells.Count >= Limit) throw Invalid(name);
        cells.Add(new());
    }
}
