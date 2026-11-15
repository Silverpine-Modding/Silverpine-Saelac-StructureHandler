using System;
using System.Collections.Generic;
using System.Linq;
using StructureHandler;

internal static class PreviewSelectionChecks
{
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        bool Near(float a, float b) => Math.Abs(a - b) < .00001f;
        // Native sprite dimensions/pivots and prefab scales inspected from the
        // installed 1.7.3 assets. No hardcoded prefab handling in production.
        foreach (var rug in new[] { (1, 3f, 2f), (2, 2f, 1f), (3, 1.8f, 1.8f) })
            test($"Rug {rug.Item1} selection follows scaled art without a half-cell shift", () =>
            {
                var shape = new PreviewSelectionShape(0, 0, rug.Item2, rug.Item3, 0);
                var at = shape.At(32.204f, -23.973f);
                check(Near(at.Right - at.Left, rug.Item2) && Near(at.Top - at.Bottom, rug.Item3));
                check(Near((at.Left + at.Right) / 2, 32.204f) && Near((at.Top + at.Bottom) / 2, -23.973f));
                check(shape.Contains(rug.Item2 / 2 - .01f, 0) && shape.Contains(-rug.Item2 / 2 + .01f, 0));
                check(!shape.Contains(rug.Item2 / 2 + .01f, 0) && !shape.Contains(0, -rug.Item3 / 2 - .01f));
            });
        foreach (var sprite in new[] { (0, 45f, 39f, 22.5f, 11f), (1, 29f, 41f, 12.5f, 12f),
            (2, 29f, 43f, 16.5f, 11f), (3, 45f, 45f, 22.5f, 11f) })
            test($"Kitsune orientation {sprite.Item1} selection respects its own pivot and size", () =>
            {
                float width = sprite.Item2 / 32, height = sprite.Item3 / 32;
                float x = (sprite.Item2 / 2 - sprite.Item4) / 32;
                float y = (sprite.Item3 / 2 - sprite.Item5) / 32;
                var shape = new PreviewSelectionShape(x, y, width, height, 0);
                check(Near(shape.Bounds.Left, -sprite.Item4 / 32) && Near(shape.Bounds.Bottom, -sprite.Item5 / 32));
                check(Near(shape.Bounds.Right - shape.Bounds.Left, width) && Near(shape.Bounds.Top - shape.Bounds.Bottom, height));
                check(shape.Contains(x, shape.Bounds.Top - .01f) && !shape.Contains(x, shape.Bounds.Top + .01f));
                check(!shape.Contains(-1, -1)); // Previously reachable via oversized sprite-derived grid offsets.
            });
        test("Rotated preview quad rejects empty corners inside its axis-aligned bounds", () =>
        {
            var shape = new PreviewSelectionShape(.25f, -.125f, 3, 1, (float)Math.PI / 4);
            check(shape.Contains(.25f, -.125f));
            check(!shape.Contains(shape.Bounds.Left + .001f, shape.Bounds.Top - .001f));
            var quarter = new PreviewSelectionShape(0, 0, 3, 1, (float)Math.PI / 2);
            check(Near(quarter.Bounds.Right, .5f) && Near(quarter.Bounds.Top, 1.5f));
            check(quarter.Contains(0, 1.4f) && !quarter.Contains(1, 0));
        });
        test("Sprite hit index includes overhangs and changes extents with the active orientation", () =>
        {
            var index = new PreviewSpatialIndex(); var hits = new List<int>();
            var wide = new PreviewSelectionShape(0, .359375f, 45f / 32, 45f / 32, 0);
            index.Add(0, wide.At(10.25f, -8.125f));
            index.Query(new PreviewBounds(10.9f, -7.2f, 10.9f, -7.2f), hits);
            check(hits.SequenceEqual(new[] { 0 }));
            index.Clear();
            var narrow = new PreviewSelectionShape(.0625f, .265625f, 29f / 32, 41f / 32, 0);
            index.Add(0, narrow.At(10.25f, -8.125f));
            index.Query(new PreviewBounds(10.9f, -7.2f, 10.9f, -7.2f), hits);
            check(hits.Count == 0);
        });
        test("Multipart selection union contains only the supplied active parts", () =>
        {
            var a = new PreviewSelectionShape(.25f, .5f, 1, 2, 0).At(-10, -20);
            var b = new PreviewSelectionShape(1.25f, -.5f, 1, 1, 0).At(-10, -20);
            var union = PreviewSelectionShape.Union(a, b);
            check(Near(union.Left, -10.25f) && Near(union.Right, -8.25f));
            check(Near(union.Bottom, -21) && Near(union.Top, -18.5f));
        });
    }
}
