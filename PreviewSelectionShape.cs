#nullable enable
using System;

namespace StructureHandler;

// The actual drawn quad, in world units relative to its record's anchor.
// Selection must not use rounded import footprints or unused sprite variants.
internal readonly struct PreviewSelectionShape
{
    private readonly float centerX, centerY, halfWidth, halfHeight, cos, sin;
    internal readonly PreviewBounds Bounds;

    internal PreviewSelectionShape(float x, float y, float width, float height, float radians)
    {
        centerX = x; centerY = y;
        halfWidth = Math.Abs(width) / 2; halfHeight = Math.Abs(height) / 2;
        cos = (float)Math.Cos(radians); sin = (float)Math.Sin(radians);
        float extentX = Math.Abs(cos) * halfWidth + Math.Abs(sin) * halfHeight;
        float extentY = Math.Abs(sin) * halfWidth + Math.Abs(cos) * halfHeight;
        Bounds = new PreviewBounds(x - extentX, y - extentY, x + extentX, y + extentY);
    }

    internal bool Contains(float x, float y)
    {
        x -= centerX; y -= centerY;
        return Math.Abs(x * cos + y * sin) <= halfWidth + 0.00001f &&
            Math.Abs(-x * sin + y * cos) <= halfHeight + 0.00001f;
    }

    internal PreviewBounds At(float x, float y) => new(
        Bounds.Left + x, Bounds.Bottom + y, Bounds.Right + x, Bounds.Top + y);

    internal static PreviewBounds Union(PreviewBounds a, PreviewBounds b) => new(
        Math.Min(a.Left, b.Left), Math.Min(a.Bottom, b.Bottom),
        Math.Max(a.Right, b.Right), Math.Max(a.Top, b.Top));
}
