using StructureHandler;

internal static class RoofPreviewChecks
{
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        var floor = new RoofPreview.Prefab { Ground = true };
        var roof = new RoofPreview.Prefab { Ground = true };
        roof.Tags.Add((0, 0, null, false));
        var overlay = new RoofPreview.Prefab();
        test("Roof overlay distinguishes known outdoor ground, roofed ground, retained destination ground and empty cells", () =>
        {
            var map = new RoofPreview();
            map.Record(0, 0, floor, null); map.Record(1, 0, roof, null); map.Record(2, 0, overlay, null);
            check(map.At(0, 0, true) == RoofStatus.Outdoor && map.At(1, 0, true) == RoofStatus.Roofed &&
                map.At(2, 0, true) == RoofStatus.Unknown && map.At(3, 0, false) == RoofStatus.Empty);
        });
        test("Overlay and furniture do not negate roofed floors; stacked sources are order independent", () =>
        {
            foreach (var order in new[] { new[] { roof, floor, overlay }, new[] { overlay, floor, roof } })
            {
                var map = new RoofPreview();
                foreach (var record in order) map.Record(4, 2, record, null);
                check(map.At(4, 2, true) == RoofStatus.Roofed);
            }
        });
        test("Native child roof tag registers at rounded final transform, not entire multi-tile footprint", () =>
        {
            var map = new RoofPreview();
            var prop = new RoofPreview.Prefab(); prop.Tags.Add((.6f, -.6f, null, false));
            map.Record(2.6f, -2.6f, prop, null);
            check(map.At(3, -3, false) == RoofStatus.Roofed && map.At(4, -4, true) == RoofStatus.Unknown);
        });
        test("Missing mod prefab is unknown even over a known floor, unless a confirmed roof is also present", () =>
        {
            var map = new RoofPreview();
            map.Unresolved(1, 2); map.Record(1, 2, floor, null);
            check(map.At(1, 2, true) == RoofStatus.Unknown);
            map.Record(1, 2, roof, null); map.Unresolved(1, 2);
            check(map.At(1, 2, true) == RoofStatus.Roofed);
            map.Clear();
            check(map.At(1, 2, false) == RoofStatus.Empty);
        });
        test("Saved Deroofer Boolean overrides the prefab default only on the matching child", () =>
        {
            var map = new RoofPreview();
            var tile = new RoofPreview.Prefab { Ground = true };
            tile.Tags.Add((0, 0, "", true)); tile.Tags.Add((1, 0, "2/0", false));
            map.Record(0, 0, tile, null);
            check(map.At(0, 0, true) == RoofStatus.Outdoor && map.At(1, 0, false) == RoofStatus.Roofed);
            map.Clear();
            map.Record(0, 0, tile, new() {
                new() { type = "Deroofer, Assembly-CSharp", dataBase64 = "AA==" },
                new() { type = "Deroofer", hierarchyPath = "2/0", componentIndex = 0, dataBase64 = "AQ==" } });
            check(map.At(0, 0, true) == RoofStatus.Roofed && map.At(1, 0, true) == RoofStatus.Unknown);
        });
        test("Malformed de-roofing state does not erase tiles or label them outdoor", () =>
        {
            var map = new RoofPreview();
            var tile = new RoofPreview.Prefab { Ground = true }; tile.Tags.Add((0, 0, "", false));
            map.Record(-1, 1, tile, new() { new() { type = "Deroofer", dataBase64 = "?" } });
            check(map.At(-1, 1, true) == RoofStatus.Unknown);
        });
    }
}
