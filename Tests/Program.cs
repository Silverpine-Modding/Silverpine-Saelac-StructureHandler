using System.IO.Compression;
using StructureHandler;

int passed = 0;
void Test(string name, Action body)
{
    body();
    passed++;
    Console.WriteLine("PASS " + name);
}
void Check(bool value) { if (!value) throw new Exception("Assertion failed."); }
EditorPerformanceChecks.Run(Test);
T Throws<T>(Action body) where T : Exception
{
    try { body(); } catch (T error) { return error; }
    throw new Exception("Expected " + typeof(T).Name);
}

Test("LLM description limit is 30 words, not an invented character limit", () =>
{
    Check(ItemCustomizationRules.ValidateText("A Sword", string.Join(" ", Enumerable.Repeat("word", 30)), "Sword", "") == null);
    Check(ItemCustomizationRules.ValidateText("A Sword", string.Join(" ", Enumerable.Repeat("word", 31)), "Sword", "") != null);
    Check(ItemCustomizationRules.ValidateText("A Sword", new string('a', 500), "Sword", "") == null);
});
Test("LLM text grammars reject markup and preserve punctuation", () =>
{
    Check(ItemCustomizationRules.ValidateText("Sword 2", "A smith's blade - sharp!", "Sword", "") == null);
    Check(ItemCustomizationRules.ValidateText("<b>Sword</b>", "", "Sword", "") != null);
    Check(ItemCustomizationRules.ValidateText("Sword", "Damage 999", "Sword", "") != null);
});
Test("Legacy text is preserved when only quality or rotation changes", () =>
{
    string old = "Existing description: 123 <color=red>magic</color>";
    Check(ItemCustomizationRules.ValidateText("Épée", old, "Épée", old) == null);
});
Test("LLM letter limit is 150 words with newline and numeric support", () =>
{
    Check(ItemCustomizationRules.ValidateLetter("Darian", string.Join("\n", Enumerable.Repeat("123", 150))) == null);
    Check(ItemCustomizationRules.ValidateLetter("Darian", string.Join(" ", Enumerable.Repeat("word", 151))) != null);
    Check(ItemCustomizationRules.ValidateLetter("Darian", "<size=80>bad</size>") != null);
});
Test("Native item rotation uses eight 45-degree orientations", () =>
{
    Check(ItemCustomizationRules.NormalizeRotation(-45) == 315);
    Check(ItemCustomizationRules.NormalizeRotation(405) == 45);
    Check(ItemCustomizationRules.NormalizeRotation(0) == 0);
});

Test("Sprite layer priority wins over order and camera depth", () =>
{
    var floor = new PreviewSortKey(-2, 100, 3000, -10, 0);
    var wall = new PreviewSortKey(1, -200, 3000, 20, 1);
    Check(floor.CompareTo(wall) < 0);
});
Test("Native order puts bath water above its floor", () =>
{
    Check(new PreviewSortKey(-2, 100, 3000, 1, 1)
        .CompareTo(new PreviewSortKey(-2, 101, 3000, 1, 0)) < 0);
});
Test("Equal layer and order uses queue then negative-Z camera depth", () =>
{
    Check(new PreviewSortKey(1, 0, 3000, 1, 2).CompareTo(new PreviewSortKey(1, 0, 3000, .5f, 1)) < 0);
    Check(new PreviewSortKey(1, 0, 2500, -.5f, 2).CompareTo(new PreviewSortKey(1, 0, 3000, 10, 1)) < 0);
    Check(new PreviewSortKey(1, 0, 3000, 1, 0).CompareTo(new PreviewSortKey(1, 0, 3000, 1, 1)) < 0);
});
Test("Sprite parts interleave globally, not in whole-object batches", () =>
{
    var commands = new[] { ("back", new PreviewSortKey(1, -2, 3000, 1, 0)),
        ("front", new PreviewSortKey(1, 2, 3000, 1, 1)),
        ("other object", new PreviewSortKey(1, 0, 3000, 1, 2)) };
    Check(commands.OrderBy(x => x.Item2).Select(x => x.Item1).SequenceEqual(new[] { "back", "other object", "front" }));
});
Test("YSorter uses native rounded Y and offsets including negative coordinates", () =>
{
    Check(PreviewSortKey.YOrder(1.25f, 2, 0) == -10);
    Check(PreviewSortKey.YOrder(-1.25f, -2, 4) == 14);
    Check(PreviewSortKey.YOrder(1.35f, 0, 0) == -14);
});
Test("Chair and bench rotation sorting offsets match Turnable", () =>
{
    Check(PreviewSortKey.RotationOffset("prefab_furniture_chair_wood", 1) == 4);
    Check(PreviewSortKey.RotationOffset("prefab_furniture_chair_fancy", 2) == 4);
    Check(PreviewSortKey.RotationOffset("prefab_furniture_bench", 2) == 4);
    Check(PreviewSortKey.RotationOffset("prefab_furniture_bench", 1) == 0);
    Check(PreviewSortKey.RotationOffset("prefab_furniture_bathtub", 1) == 0);
});
Test("Floors and ground share a slot; doors and windows share the wall slot", () =>
{
    foreach (string name in new[] { "prefab_tile_wood", "prefab_tile_rock", "prefab_tile_grass",
                 "prefab_tile_water", "prefab_tile_water_lake", "prefab_tile_water_swamp" })
        Check(StructurePlacementPolicy.Slot(name) == PlacementSlot.Ground);
    foreach (string name in new[] { "prefab_wall_wood", "prefab_wall_rock_door", "prefab_wall_wood_window" })
        Check(StructurePlacementPolicy.Slot(name) == PlacementSlot.Wall);
    Check(StructurePlacementPolicy.Slot("prefab_tile_teleporter_world") == PlacementSlot.None);
});
Test("Floor, wall, bath water and furniture may coexist without slot conflicts", () =>
{
    var file = new StructureFile
    {
        supportingTerrain = new() { new() { prefabName = "prefab_tile_rock" }, new() { prefabName = "prefab_tile_water_bathhouse" } },
        objects = new() { new() { prefabName = "prefab_wall_rock" }, new() { prefabName = "prefab_furniture_candle" }, new() { prefabName = "prefab_furniture_table_wood" } }
    };
    Check(StructurePlacementPolicy.FindConflict(file) == null);
});
Test("Terrain duplication and cross-list floor conflicts report their cell without mutation", () =>
{
    var file = new StructureFile { supportingTerrain = new() { new() { prefabName = "prefab_tile_rock", x = -3, y = 2 } },
        objects = new() { new() { prefabName = "prefab_tile_wood", x = -3, y = 2 } } };
    Check(StructurePlacementPolicy.FindConflict(file)!.Contains("(-3, 2)"));
    Check(file.objects.Count == 1 && file.supportingTerrain.Count == 1);
});
Test("Wall conflicts include doors, windows and occupied offsets", () =>
{
    var file = new StructureFile { objects = new() {
        new() { prefabName = "prefab_wall_rock", occupiedOffsets = new() { new() { x = 1 }, new() { x = 1 } } },
        new() { prefabName = "prefab_wall_wood_window", x = 1 } } };
    Check(StructurePlacementPolicy.FindConflict(file)!.StartsWith("Wall overlap at (1, 0)"));
    file.objects.RemoveAt(1);
    Check(StructurePlacementPolicy.FindConflict(file) == null);
});
Test("Bath water allows a floor beneath it but not duplicate bath water", () =>
{
    var file = new StructureFile { supportingTerrain = new() { new() { prefabName = "prefab_tile_rock" },
        new() { prefabName = "prefab_tile_water_bathhouse" }, new() { prefabName = "prefab_tile_water_bathhouse" } } };
    Check(StructurePlacementPolicy.FindConflict(file)!.StartsWith("WaterOverlay overlap"));
    Check(StructurePlacementPolicy.ReplaceSlot(file, "prefab_tile_water_bathhouse", new[] { (0, 0) }) == 2);
    Check(file.supportingTerrain.Single().prefabName == "prefab_tile_rock");
});
Test("Painting floor preserves bath water, wall, furniture and neighboring cells", () =>
{
    var file = new StructureFile { supportingTerrain = new() { new() { prefabName = "prefab_tile_grass" },
        new() { prefabName = "prefab_tile_wood" }, new() { prefabName = "prefab_tile_water_bathhouse" },
        new() { prefabName = "prefab_tile_rock", x = 1 } },
        objects = new() { new() { prefabName = "prefab_wall_wood" }, new() { prefabName = "prefab_furniture_candle" } } };
    Check(StructurePlacementPolicy.ReplaceSlot(file, "prefab_tile_rock", new[] { (0, 0) }) == 2);
    Check(file.supportingTerrain.Count == 2 && file.objects.Count == 2);
    Check(file.supportingTerrain.Any(x => x.prefabName == "prefab_tile_water_bathhouse"));
});
Test("Repainting one wall keeps its metadata while removing other wall-slot occupants", () =>
{
    var keep = new StructureObject { prefabName = "prefab_wall_wood_door", signMessage = "keep" };
    var file = new StructureFile { objects = new() { keep, new() { prefabName = "prefab_wall_rock" },
        new() { prefabName = "prefab_furniture_candle" } } };
    Check(StructurePlacementPolicy.ReplaceSlot(file, keep.prefabName, new[] { (0, 0) }, keep) == 1);
    Check(file.objects.Contains(keep) && keep.signMessage == "keep" && file.objects.Count == 2);
});
Test("Slot rules support component-classified custom floors and walls", () =>
{
    var file = new StructureFile { objects = new() { new() { prefabName = "mod_floor" }, new() { prefabName = "prefab_tile_wood" } } };
    Check(StructurePlacementPolicy.FindConflict(file, name => name == "mod_floor" ? PlacementSlot.Ground : StructurePlacementPolicy.Slot(name)) != null);
});
Test("Half-cell wall translations cannot silently introduce rounded-cell duplicates", () =>
{
    var file = new StructureFile { objects = new() {
        new() { prefabName = "prefab_wall_rock", x = .5f },
        new() { prefabName = "prefab_wall_wood", x = 1.5f } } };
    Check(StructurePlacementPolicy.FindConflict(file) == null);
    foreach (var item in file.objects) item.x += 1;
    Check(StructurePlacementPolicy.FindConflict(file) != null);
});
Test("Ordinary water and a floor cannot bypass import slot validation", () =>
{
    var file = new StructureFile { supportingTerrain = new() { new() { prefabName = "prefab_tile_water" }, new() { prefabName = "prefab_tile_rock" } } };
    Check(StructurePlacementPolicy.FindConflict(file) != null);
    Check(file.supportingTerrain.Count == 2);
});
Test("Snapped placement rejects blocking colliders but free furniture can overlap non-wall props", () =>
{
    Check(StructurePlacementPolicy.Rejection(false, true, 0, false, false, true, false) != null);
    Check(StructurePlacementPolicy.Rejection(false, true, -1, false, false, true, false) == null);
    Check(StructurePlacementPolicy.Rejection(false, true, 0, false, false, false, false) == null);
});
Test("Native wall-placement permission only exempts free placement, not snapped collision", () =>
{
    Check(StructurePlacementPolicy.Rejection(false, true, -1, false, false, true, true) != null);
    Check(StructurePlacementPolicy.Rejection(false, true, -1, true, false, true, true) == null);
    Check(StructurePlacementPolicy.Rejection(false, true, 0, true, false, true, true) != null);
});
Test("Submerge blocks construction but not the native free furniture placement path", () =>
{
    Check(StructurePlacementPolicy.Rejection(true, false, 0, false, true, false, false) != null);
    Check(StructurePlacementPolicy.Rejection(false, true, -1, false, true, false, false) == null);
});

Test("Ignore Placement Rules bypasses construction, wall and collider restrictions only when enabled", () =>
{
    Check(StructurePlacementPolicy.Rejection(true, true, 0, false, true, true, true) != null);
    Check(StructurePlacementPolicy.Rejection(true, true, 0, false, true, true, true, true) == null);
    Check(StructurePlacementPolicy.Rejection(false, true, -1, false, false, true, true, true) == null);
    Check(StructurePlacementPolicy.Rejection(false, true, 0, false, false, true, false, true) == null);
});
Test("Free placement permits fractional nudges on normally snapped potted plants", () =>
{
    bool normal = StructurePlacementPolicy.ShouldSnap(false, false, true, 0);
    bool free = StructurePlacementPolicy.ShouldSnap(true, false, true, 0);
    Check(normal && !free);
    Check(StructurePlacementPolicy.NudgeStep(.25f, normal) == 1f);
    Check(StructurePlacementPolicy.NudgeStep(.25f, free) == .25f);
    Check(StructurePlacementPolicy.NudgeStep(.1f, free) == .1f);
    Check(!StructurePlacementPolicy.ShouldSnap(false, false, true, -1));
    Check(!StructurePlacementPolicy.ShouldSnap(false, false, false, 0));
    Check(!StructurePlacementPolicy.ShouldSnap(true, true, false, 5));
    Check(StructurePlacementPolicy.ShouldSnap(false, true, false, 5));
});
Test("Nudged furniture can share a cell but ground/wall/water slots remain exclusive", () =>
{
    var file = new StructureFile { objects = new() {
        new() { prefabName = "prefab_furniture_pottedplant", x = .1f, y = .25f },
        new() { prefabName = "prefab_furniture_pottedplant", x = -.1f, y = -.25f },
        new() { prefabName = "prefab_wall_rock" } },
        supportingTerrain = new() { new() { prefabName = "prefab_tile_rock" },
            new() { prefabName = "prefab_tile_water_bathhouse" } } };
    Check(StructurePlacementPolicy.FindConflict(file) == null);
    file.objects.Add(new() { prefabName = "prefab_wall_wood", x = .1f });
    Check(StructurePlacementPolicy.FindConflict(file) != null);
    Check(file.objects[0].x == .1f && file.objects[0].y == .25f);
});

Test("Preview excludes native FakeAO object names and water edge variants", () =>
{
    foreach (string name in new[] { "FakeAO", "FakeAO(Clone)", "fakeao (2)",
                 "prefab_effect_fakeao_water", "prefab_effect_fakeao_water_corner(Clone)" })
        Check(PreviewLightingPolicy.IsLightingEffect(name, "", "sprite-diffuse", "Sprites/Diffuse"));
});
Test("Preview recognizes FakeAO by sprite even when the renderer is renamed", () =>
{
    Check(PreviewLightingPolicy.IsLightingEffect("Sprite", "sprite_fakeao", "sprite-diffuse", "Sprites/Diffuse"));
    Check(PreviewLightingPolicy.IsLightingEffect("Sprite", "SPRITE_FAKEAO_WATER_CORNER", "", ""));
});
Test("Preview recognizes FakeAO materials, shaders, and parent container names", () =>
{
    Check(PreviewLightingPolicy.IsLightingEffect("Sprite", "", "FakeAO Material", ""));
    Check(PreviewLightingPolicy.IsLightingEffect("Sprite", "", "", "Custom/FakeAO"));
    Check(PreviewLightingPolicy.IsFakeAO("FakeAO Container"));
    Check(!PreviewLightingPolicy.IsFakeAO("Furniture"));
});
Test("Preview retains door frames, windows, walls, terrain, and furniture", () =>
{
    foreach (var (obj, sprite) in new[] {
                 ("frame", "sprite_wall_rock_doorgap"), ("sprite_window", "sprite_window"),
                 ("prefab_wall_rock", "sprite_wall_rock"), ("prefab_tile_rock", "sprite_floor_rock"),
                 ("prefab_furniture_table_wood", "sprite_table_wood"),
                 ("prefab_furniture_bath_stone", "sprite_bath_stone") })
        Check(!PreviewLightingPolicy.IsLightingEffect(obj, sprite, "sprite-diffuse", "Sprites/Diffuse"));
    // Do not apply the broad legacy effect-name filter to normal sprite names.
    Check(!PreviewLightingPolicy.IsLightingEffect("decoration", "sprite_light_fixture", "sprite-diffuse", "Sprites/Diffuse"));
});
Test("Preview retains existing light and shadow exclusions", () =>
{
    Check(PreviewLightingPolicy.IsLightingEffect("Shadow", "", "", ""));
    Check(PreviewLightingPolicy.IsLightingEffect("Glow", "", "", ""));
    Check(PreviewLightingPolicy.IsLightingEffect("Sprite", "", "Ambient Occlusion", ""));
    Check(PreviewLightingPolicy.IsLightingEffect("Sprite", "", "", "Custom/Light"));
});
Test("Preview lighting classification tolerates missing names", () =>
{
    Check(!PreviewLightingPolicy.IsLightingEffect(null, null, null, null));
    Check(!PreviewLightingPolicy.IsLightingEffect("", "", "", ""));
    Check(!PreviewLightingPolicy.IsFakeAO(null));
});

Test("Conflict winners are always applied after their losers", () =>
{
    Check(StructureAlgorithms.TryOrder(3, new[] { (0, 1), (1, 2) }, out var order));
    Check(order.SequenceEqual(new[] { 2, 1, 0 }));
});
Test("Three-way contradictory winner choices are rejected", () =>
    Check(!StructureAlgorithms.TryOrder(3, new[] { (0, 1), (1, 2), (2, 0) }, out _)));
Test("Repeated choices do not add duplicate indegrees", () =>
{
    Check(StructureAlgorithms.TryOrder(2, new[] { (0, 1), (0, 1) }, out var order));
    Check(order.SequenceEqual(new[] { 1, 0 }));
});
Test("Unrelated files retain deterministic order", () =>
{
    Check(StructureAlgorithms.TryOrder(4, Array.Empty<(int, int)>(), out var order));
    Check(order.SequenceEqual(new[] { 0, 1, 2, 3 }));
});
Test("Every accepted edge is honored across 500 seeded graphs", () =>
{
    var random = new Random(71);
    for (int trial = 0; trial < 500; trial++)
    {
        var rank = Enumerable.Range(0, 20).OrderBy(_ => random.Next()).ToArray();
        var choices = new List<(int, int)>();
        for (int i = 0; i < 20; i++)
            for (int j = i + 1; j < 20; j++)
                if (random.Next(5) == 0) choices.Add((rank[j], rank[i]));
        Check(StructureAlgorithms.TryOrder(20, choices, out var order));
        foreach (var (winner, loser) in choices)
            Check(Array.IndexOf(order, winner) > Array.IndexOf(order, loser));
    }
});
Test("Invalid conflict indices fail explicitly", () =>
    Throws<ArgumentOutOfRangeException>(() => StructureAlgorithms.TryOrder(1, new[] { (2, 0) }, out _)));

(int, int) TileOf(float x, float y) => ((int)Math.Floor((x + 50) / 100), (int)Math.Floor((y + 50) / 100));
Test("Off-tile objects and support are split without duplicating records", () =>
{
    var file = new StructureFile { name = "Bath" };
    file.objects.AddRange(new[] { new StructureObject { x = 0 }, new StructureObject { x = 100 } });
    file.supportingTerrain.AddRange(new[] { new SupportingTerrain { x = 0 }, new SupportingTerrain { x = 100 } });
    var parts = StructurePlans.Partition(file, TileOf);
    Check(parts.Count == 2);
    Check(parts.Values.All(p => p.objects.Count == 1 && p.supportingTerrain.Count == 1 && p.name == "Bath"));
    Check(parts.Values.Sum(p => p.objects.Count) == file.objects.Count);
});
Test("World boundary anchors are assigned to one destination", () =>
{
    var file = new StructureFile();
    foreach (float x in new[] { -50.001f, -50f, 49.999f, 50f }) file.objects.Add(new StructureObject { x = x });
    var parts = StructurePlans.Partition(file, TileOf);
    Check(parts[(-1, 0)].objects.Count == 1 && parts[(0, 0)].objects.Count == 2 && parts[(1, 0)].objects.Count == 1);
});
Test("Import partitioning includes ordinary and bath water on separate world tiles", () =>
{
    var file = new StructureFile();
    file.supportingTerrain.Add(new SupportingTerrain { prefabName = "prefab_tile_water", x = 100 });
    file.supportingTerrain.Add(new SupportingTerrain { prefabName = "prefab_tile_water_bathhouse" });
    var parts = StructurePlans.Partition(file, TileOf);
    Check(parts.Count == 2 && parts[(0, 0)].supportingTerrain.Single().prefabName == "prefab_tile_water_bathhouse");
    Check(parts[(1, 0)].supportingTerrain.Single().prefabName == "prefab_tile_water");
    Check(file.supportingTerrain.Count == 2);
});
Test("Import partitioning keeps shallow-water overlays and their explicit floors", () =>
{
    foreach (string water in new[] { "prefab_tile_water_shallow", "prefab_tile_water_shallow_swamp" })
    {
        var file = new StructureFile { supportingTerrain = new() {
            new() { prefabName = water, x = 100 }, new() { prefabName = "prefab_tile_rock", x = 100 } } };
        var parts = StructurePlans.Partition(file, TileOf);
        Check(parts.Count == 1 && parts[(1, 0)].supportingTerrain.Select(item => item.prefabName)
            .SequenceEqual(new[] { water, "prefab_tile_rock" }));
        Check(file.supportingTerrain.Count == 2);
    }
});
Test("Terrain-less empty imports have no destination mutations", () =>
    Check(StructurePlans.Partition(new StructureFile(), TileOf).Count == 0));
Test("Terrain translations cannot accumulate fractional drift", () =>
{
    Check(StructureAlgorithms.IsGridTranslation(-15, 24));
    Check(!StructureAlgorithms.IsGridTranslation(0.25f, 0));
    Check(!StructureAlgorithms.IsGridTranslation(float.NaN, 0));
    Check(!StructureAlgorithms.IsGridTranslation(0, float.PositiveInfinity));
});
Test("Large lists render a bounded visible row range", () =>
{
    var rows = StructureAlgorithms.VisibleRows(14000, 330, 28, 100000);
    Check(rows.First == 500 && rows.Last - rows.First <= 14);
});
Test("Empty and past-end scroll ranges are safe", () =>
{
    Check(StructureAlgorithms.VisibleRows(0, 330, 28, 0) == (0, 0));
    Check(StructureAlgorithms.VisibleRows(10000, 330, 28, 3) == (3, 3));
});
Test("Undo and redo round-trip document snapshots", () =>
{
    var history = new SnapshotHistory(); history.Reset("A"); history.Record("B");
    Check(history.Undo() == "A" && history.Redo() == "B");
    Check(history.Redo() == null);
});
Test("Editing after undo discards only the redo branch", () =>
{
    var history = new SnapshotHistory(); history.Reset("A"); history.Record("B"); history.Record("C");
    history.Undo(); history.Record("D");
    Check(!history.CanRedo && history.Undo() == "B" && history.Undo() == "A");
});
Test("Unchanged snapshots do not create undo entries", () =>
{
    var history = new SnapshotHistory(); history.Reset("A"); history.Record("A");
    Check(!history.CanUndo);
});
Test("History is capped at 32 snapshots", () =>
{
    var history = new SnapshotHistory(); history.Reset("0");
    for (int i = 1; i < 80; i++) history.Record(i.ToString());
    int undos = 0; while (history.Undo() != null) undos++;
    Check(undos == 31);
});
Test("History caps memory but retains a single oversized current document", () =>
{
    var history = new SnapshotHistory(); history.Reset("A"); history.Record(new string('x', 17 * 1024 * 1024));
    Check(!history.CanUndo && history.Current!.Length == 17 * 1024 * 1024);
});

string Legacy(Action<BinaryWriter> write)
{
    using var output = new MemoryStream();
    using (var brotli = new BrotliStream(output, CompressionLevel.Fastest, true))
    using (var writer = new BinaryWriter(brotli)) write(writer);
    return Convert.ToBase64String(output.ToArray());
}
Test("Legacy binary records retain duplicate-component indices and skipped slots", () =>
{
    var encoded = Legacy(writer =>
    {
        writer.Write(1); writer.Write("prefab_test"); writer.Write(12.5f); writer.Write(-2f); writer.Write(1f);
        using var bytes = new MemoryStream();
        using (var components = new BinaryWriter(bytes, System.Text.Encoding.UTF8, true))
        {
            components.Write(3);
            components.Write("Sign"); components.Write(true); components.Write(1); components.Write((byte)42);
            components.Write("Sign"); components.Write(false);
            components.Write("Sign"); components.Write(true); components.Write(1); components.Write((byte)99);
        }
        writer.Write((int)bytes.Length); writer.Write(bytes.ToArray());
    });
    var items = LegacyStructureReader.Read(encoded);
    Check(items.Count == 1 && items[0].x == 12.5f && items[0].components.Count == 2);
    Check(items[0].components[1].componentIndex == 2);
    Check(Convert.FromBase64String(items[0].components[0].dataBase64)[0] == 42);
});
Test("Negative legacy counts fail safely", () =>
    Throws<InvalidDataException>(() => LegacyStructureReader.Read(Legacy(writer => writer.Write(-1)))));
Test("Excessive legacy counts fail before allocation", () =>
    Throws<InvalidDataException>(() => LegacyStructureReader.Read(Legacy(writer => writer.Write(250001)))));
Test("Truncated legacy payloads fail safely", () =>
    Throws<EndOfStreamException>(() => LegacyStructureReader.Read(Legacy(writer =>
    {
        writer.Write(1); writer.Write("prefab_test"); writer.Write(0f); writer.Write(0f); writer.Write(1f);
        writer.Write(100); writer.Write((byte)1);
    }))));
Test("Malformed base64 is not accepted", () => Throws<FormatException>(() => LegacyStructureReader.Read("?")));

Test("Successful imports do not invoke rollback", () =>
{
    bool committed = false;
    ImportTransaction.Execute(() => committed = true, () => throw new Exception("Unexpected rollback"), "Test");
    Check(committed);
});
for (int stage = 0; stage < 4; stage++)
{
    int failureStage = stage;
    Test("Transaction fault at stage " + stage + " restores the snapshot", () =>
    {
        var world = new List<string> { "floor", "wall" };
        string sidecar = "original";
        var error = Throws<InvalidDataException>(() => ImportTransaction.Execute(() =>
        {
            if (failureStage == 0) throw new Exception("removal");
            world.Clear();
            if (failureStage == 1) throw new Exception("instantiate");
            world.Add("new wall");
            if (failureStage == 2) throw new Exception("registration");
            sidecar = "modified";
            throw new Exception("protection");
        }, () => { world.Clear(); world.AddRange(new[] { "floor", "wall" }); sidecar = "original"; }, "Test"));
        Check(world.SequenceEqual(new[] { "floor", "wall" }) && sidecar == "original");
        Check(error.InnerException != null);
    });
}
Test("Rollback failures preserve both exceptions and never claim restoration", () =>
{
    var error = Throws<AggregateException>(() => ImportTransaction.Execute(
        () => throw new Exception("import"), () => throw new Exception("recovery"), "Test"));
    Check(error.InnerExceptions.Count == 2 && error.Message.Contains("Reload"));
});
Test("JSON replacement keeps the previous file and leaves no temporary files", () =>
{
    string directory = Path.Combine(Path.GetTempPath(), "StructureHandler-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    string path = Path.Combine(directory, "Test.json");
    try
    {
        StructureJsonFile.Write(path, "first"); StructureJsonFile.Write(path, "second");
        Check(File.ReadAllText(path) == "second" && File.ReadAllText(path + ".bak") == "first");
        Check(Directory.GetFiles(directory, "*.tmp").Length == 0);
    }
    finally
    {
        File.Delete(path); File.Delete(path + ".bak"); Directory.Delete(directory);
    }
});
Test("Import picker starts with no files selected", () =>
{
    var selection = new ImportFileSelection();
    selection.Refresh(new[] { "Bath.json", "House.json" });
    Check(selection.SelectedCount == 0 && selection.Snapshot().Length == 0);
});
Test("Import selection never reads unchecked broken JSON", () =>
{
    var selection = new ImportFileSelection();
    selection.Refresh(new[] { "Broken.json", "Bath.json", "House.json" });
    selection.SetSelected("House.json", true);
    selection.SetSelected("Bath.json", true);
    var loaded = selection.Snapshot().Select(path => path == "Broken.json"
        ? throw new Exception("Unchecked file was read") : path).ToArray();
    Check(loaded.SequenceEqual(new[] { "Bath.json", "House.json" }));
});
Test("Import selection ignores backups and deduplicates paths", () =>
{
    var selection = new ImportFileSelection();
    selection.Refresh(new[] { "Bath.json", "bath.JSON", "Bath.json.bak", "Bath.json.123.tmp" });
    selection.SelectAll();
    Check(selection.Paths.Count == 1 && selection.SelectedCount == 1);
});
Test("Refreshing the picker retains present choices and drops missing ones", () =>
{
    var selection = new ImportFileSelection();
    selection.Refresh(new[] { "Bath.json", "House.json" });
    selection.SelectAll();
    selection.Refresh(new[] { "House.json", "New.json" });
    Check(selection.Snapshot().SequenceEqual(new[] { "House.json" }));
    Check(!selection.IsSelected("New.json"));
});
Test("Select All and Clear affect only available import files", () =>
{
    var selection = new ImportFileSelection();
    selection.Refresh(new[] { "Bath.json", "House.json" });
    selection.SetSelected("NotListed.json", true);
    Check(selection.SelectedCount == 0);
    selection.SelectAll(); Check(selection.SelectedCount == 2);
    selection.SetSelected("BATH.JSON", false); Check(selection.SelectedCount == 1);
    selection.Clear(); Check(selection.Snapshot().Length == 0);
});
Test("An import snapshot cannot expand when new files appear", () =>
{
    var selection = new ImportFileSelection();
    selection.Refresh(new[] { "Bath.json" });
    selection.SelectAll();
    string[] frozen = selection.Snapshot();
    selection.Refresh(new[] { "Bath.json", "New.json" });
    selection.SelectAll();
    Check(frozen.SequenceEqual(new[] { "Bath.json" }));
});
Test("Imported railing marker round-trips native component state", () =>
{
    var railing = new StructureRailingState();
    using var bytes = new MemoryStream();
    using var writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8, true);
    railing.Serialize(writer); writer.Flush();
    Check(bytes.Length == 4);
    bytes.Position = 0;
    using var reader = new BinaryReader(bytes);
    new StructureRailingState().Deserialize(reader);
    Check(bytes.Position == bytes.Length);
});
Test("Newer railing state versions are not silently misread", () =>
{
    using var bytes = new MemoryStream(BitConverter.GetBytes(2));
    using var reader = new BinaryReader(bytes);
    Throws<InvalidDataException>(() => new StructureRailingState().Deserialize(reader));
});
Test("Truncated railing state fails safely", () =>
{
    using var bytes = new MemoryStream(new byte[] { 1, 0 });
    using var reader = new BinaryReader(bytes);
    Throws<EndOfStreamException>(() => new StructureRailingState().Deserialize(reader));
});
Test("Normal transition cells match all 396 border cells per world tile", () =>
{
    foreach (int tileX in new[] { -7, -1, 0, 1, 9 })
    foreach (int tileY in new[] { -3, 0, 4 })
    {
        int count = 0;
        for (int localX = 0; localX < 100; localX++)
        for (int localY = 0; localY < 100; localY++)
        {
            bool expected = localX == 0 || localX == 99 || localY == 0 || localY == 99;
            bool actual = PlanningTiles.IsTransitionCell(tileX * 100 - 50 + localX, tileY * 100 - 50 + localY);
            Check(actual == expected);
            if (actual) count++;
        }
        Check(count == 396);
    }
});
Test("Transition warnings cover both adjacent edges, not interior cells", () =>
{
    foreach (int x in new[] { -151, -150, -51, -50, 49, 50, 149, 150 })
        Check(PlanningTiles.IsTransitionCell(x, 0));
    foreach (int x in new[] { -149, -52, -49, 0, 48, 51, 148, 151 })
        Check(!PlanningTiles.IsTransitionCell(x, 0));
});
Test("Planning colors normalize readable hex and reject invalid values", () =>
{
    Check(PlanningTiles.TryColor(" aabbcc ", out var color) && color == "#AABBCC");
    Check(PlanningTiles.TryColor("#ffd54f", out color) && color == "#FFD54F");
    Check(!PlanningTiles.TryColor("red", out _) && !PlanningTiles.TryColor("#12GG00", out _));
    Check(!PlanningTiles.TryColor(null, out _) && !PlanningTiles.TryColor("#12345678", out _));
});
Test("Legacy files need no planning data; malformed marks are skipped", () =>
{
    Check(PlanningTiles.Normalize(null).Count == 0);
    var marks = PlanningTiles.Normalize(new[] {
        new PlanningTile { x = int.MinValue }, new PlanningTile { color = "invalid" },
        new PlanningTile { x = 4, y = -8, color = "66dd88" } });
    Check(marks.Count == 1 && marks[0].x == 4 && marks[0].color == "#66DD88");
});
Test("Combining planning marks uses last color without mutating originals", () =>
{
    var first = new PlanningTile { x = 4, y = 5, color = "#FF0000" };
    var second = new PlanningTile { x = 4, y = 5, color = "#0000FF" };
    var result = PlanningTiles.Normalize(new[] { first, second });
    Check(result.Count == 1 && result[0].color == "#0000FF");
    result[0].x = 8;
    Check(first.x == 4 && second.x == 4);
});
Test("Fast planning strokes have no gaps, including reversed diagonals", () =>
{
    foreach (var end in new[] { (12, 0), (0, -12), (12, 5), (-5, 12), (-8, -8) })
    {
        var cells = PlanningTiles.Stroke(0, 0, end.Item1, end.Item2).ToArray();
        Check(cells.First() == (0, 0) && cells.Last() == end);
        Check(cells.Length == Math.Max(Math.Abs(end.Item1), Math.Abs(end.Item2)) + 1);
        Check(cells.Distinct().Count() == cells.Length);
        for (int i = 1; i < cells.Length; i++)
            Check(Math.Abs(cells[i].X - cells[i - 1].X) <= 1 && Math.Abs(cells[i].Y - cells[i - 1].Y) <= 1);
    }
    Check(PlanningTiles.Stroke(2, 3, 2, 3).Single() == (2, 3));
});
Test("Planning-only files cause no import destination or terrain mutations", () =>
{
    var file = new StructureFile();
    file.planningTiles.Add(new PlanningTile { x = 50, y = 100 });
    Check(StructurePlans.Partition(file, TileOf).Count == 0);
    Check(file.objects.Count == 0 && file.supportingTerrain.Count == 0 && file.occupiedPositions.Count == 0);
});
Test("Planning outside a real structure never expands its import footprint", () =>
{
    var file = new StructureFile();
    file.objects.Add(new StructureObject { x = 0, y = 0 });
    file.planningTiles.Add(new PlanningTile { x = 500, y = -100 });
    var parts = StructurePlans.Partition(file, TileOf);
    Check(parts.Count == 1 && parts[(0, 0)].objects.Count == 1);
    Check(parts[(0, 0)].planningTiles.Count == 0);
});
Test("Only objects not covered by native saving get supplemental records", () =>
{
    Check(StructurePersistenceLedger.NeedsSupplemental(false, true, false)); // plain dirt
    Check(StructurePersistenceLedger.NeedsSupplemental(false, false, false)); // decorative sprite
    Check(!StructurePersistenceLedger.NeedsSupplemental(true, true, false)); // furniture/tree
    Check(!StructurePersistenceLedger.NeedsSupplemental(true, false, true)); // persistent native object
    Check(StructurePersistenceLedger.NeedsSupplemental(true, false, false)); // unregistered loose root
});
StructureObject Scenery(string name, float x = 0, float y = 0, float z = 1) =>
    new() { prefabName = name, x = x, y = y, z = z };
Test("Replacement records retain the cleared original and supplemental import", () =>
{
    var ledger = new StructurePersistenceLedger();
    var original = Scenery("prefab_original"); var placed = Scenery("prefab_dirt");
    ledger.RecordReplacement(new[] { original }, new[] { placed });
    Check(ledger.Cleared.Count == 1 && ledger.Objects.Count == 1);
    Check(ledger.Cleared.ContainsKey(StructurePersistenceLedger.Key(original)));
});
Test("Replacing an imported tile does not turn that import into baseline scenery", () =>
{
    var ledger = new StructurePersistenceLedger();
    var original = Scenery("prefab_original"); var first = Scenery("prefab_first"); var second = Scenery("prefab_second");
    ledger.RecordReplacement(new[] { original }, new[] { first });
    ledger.RecordReplacement(new[] { first }, new[] { second });
    Check(ledger.Cleared.Count == 1 && ledger.Objects.Count == 1);
    Check(!ledger.Cleared.ContainsKey(StructurePersistenceLedger.Key(first)));
    Check(ledger.Objects.ContainsKey(StructurePersistenceLedger.Key(second)));
});
Test("Multiple layers and fractional furniture anchors do not share replacement keys", () =>
{
    Check(StructurePersistenceLedger.Key(Scenery("p", z: 0)) != StructurePersistenceLedger.Key(Scenery("p", z: 1)));
    Check(StructurePersistenceLedger.Key(Scenery("p", x: 0.25f)) != StructurePersistenceLedger.Key(Scenery("p", x: 0.5f)));
});
Test("Refreshing moved supplemental scenery removes its previous spawn point", () =>
{
    var ledger = new StructurePersistenceLedger(); var first = Scenery("p", 0);
    ledger.RecordReplacement(Array.Empty<StructureObject>(), new[] { first });
    var moved = Scenery("p", 4); moved.signMessage = "Edited after import";
    ledger.Move(StructurePersistenceLedger.Key(first), moved);
    Check(ledger.Objects.Count == 1 && ledger.Objects.Values.Single().x == 4);
    Check(ledger.Objects.Values.Single().signMessage == "Edited after import");
});
Test("A removed supplemental object is not recreated from the historical export", () =>
{
    var ledger = new StructurePersistenceLedger(); var item = Scenery("p");
    ledger.RecordReplacement(Array.Empty<StructureObject>(), new[] { item });
    ledger.Objects.Remove(StructurePersistenceLedger.Key(item));
    var restored = new StructurePersistenceLedger(); restored.Restore(ledger.Objects.Values, ledger.Cleared.Values);
    Check(restored.Objects.Count == 0);
});
Test("Intentional regeneration clears only that world tile's terrain and clearance", () =>
{
    var ledger = new StructurePersistenceLedger();
    ledger.RecordReplacement(new[] { Scenery("original", 0), Scenery("original", -100) },
        new[] { Scenery("import", 0), Scenery("import", -100) });
    ledger.ClearWhere(item => TileOf(item.x, item.y) == (0, 0));
    Check(ledger.Objects.Count == 1 && ledger.Cleared.Count == 1);
    Check(ledger.Objects.Values.Single().x == -100 && ledger.Cleared.Values.Single().x == -100);
});
Test("Loading another save replaces rather than merges terrain history", () =>
{
    var ledger = new StructurePersistenceLedger();
    ledger.RecordReplacement(new[] { Scenery("old_base") }, new[] { Scenery("old_import") });
    ledger.Restore(new[] { Scenery("other_save", 100) }, null);
    Check(ledger.Cleared.Count == 0 && ledger.Objects.Count == 1);
    Check(ledger.Objects.Values.Single().prefabName == "other_save");
    ledger.Reset(); Check(ledger.Objects.Count == 0 && ledger.Cleared.Count == 0);
});
Test("A version-1 save has empty supplemental state instead of invented objects", () =>
{
    var ledger = new StructurePersistenceLedger(); ledger.Restore(null, null);
    Check(ledger.Objects.Count == 0 && ledger.Cleared.Count == 0);
});
Test("Malformed supplemental state does not partially replace recoverable records", () =>
{
    var ledger = new StructurePersistenceLedger();
    ledger.RecordReplacement(Array.Empty<StructureObject>(), new[] { Scenery("keep") });
    Throws<InvalidDataException>(() => ledger.Restore(new[] { Scenery("new") }, new[] { Scenery("bad", float.NaN) }));
    Check(ledger.Objects.Count == 1 && ledger.Objects.Values.Single().prefabName == "keep");
});
Test("Restoring an import rollback reinstates terrain and its original clearance", () =>
{
    var ledger = new StructurePersistenceLedger();
    ledger.RecordReplacement(new[] { Scenery("base") }, new[] { Scenery("old") });
    var objects = ledger.Objects.Values.ToArray(); var cleared = ledger.Cleared.Values.ToArray();
    ledger.RecordReplacement(new[] { Scenery("old") }, new[] { Scenery("failed_new") });
    ledger.Restore(objects, cleared);
    Check(ledger.Objects.Values.Single().prefabName == "old");
    Check(ledger.Cleared.Values.Single().prefabName == "base");
});
Test("Discarded imported scenery in free pools is excluded from native saving", () =>
{
    var candidates = new HashSet<string> { "tree", "floor", "grass" };
    NativeSavePolicy.RemoveReleased(candidates, new[] { new[] { "tree", "grass" } },
        name => name == "floor");
    Check(candidates.SetEquals(new[] { "floor" }));
});
Test("All unused pool entries are excluded but deliberately inactive non-pool objects remain", () =>
{
    var candidates = new HashSet<string> { "unrelated-pool-tree", "inactive-furniture", "discarded-tree" };
    NativeSavePolicy.RemoveReleased(candidates, new[] { new[] { "unrelated-pool-tree", "discarded-tree" } },
        _ => false);
    Check(candidates.SetEquals(new[] { "inactive-furniture" }));
});
Test("A reclaimed object is saved again and stale active stack entries are not removed", () =>
{
    var candidates = new HashSet<string> { "reused-tree", "active-tree" };
    NativeSavePolicy.RemoveReleased(candidates, new[] { new[] { "active-tree" } }, _ => true);
    Check(candidates.Count == 2);
});
Test("Native seasonal snapshots are recognized across export and import seasons", () =>
{
    IReadOnlyList<string>[] seasons = { new[] { "spring0", "spring1" }, new[] { "summer0", "summer1", "summer2" } };
    Check(NativeSavePolicy.IsCapturedSeasonalVariant(5, "spring1", false, seasons));
    Check(NativeSavePolicy.IsCapturedSeasonalVariant(5, "summer2", false, seasons));
    Check(!NativeSavePolicy.IsCapturedSeasonalVariant(5, "summer1", false, seasons));
});
Test("Explicit fixed seasonal sprites and missing native state retain fallback support", () =>
{
    IReadOnlyList<string>[] seasons = { Array.Empty<string>(), new[] { "spring" } };
    Check(!NativeSavePolicy.IsCapturedSeasonalVariant(0, "spring", true, seasons));
    Check(!NativeSavePolicy.IsCapturedSeasonalVariant(-1, "spring", false, seasons));
    Check(!NativeSavePolicy.IsCapturedSeasonalVariant(0, "missing", false, seasons));
});
Test("Only extenders absent from the native prefab need companion support", () =>
{
    Check(!NativeSavePolicy.NeedsExtenderOverride(true, true));
    Check(NativeSavePolicy.NeedsExtenderOverride(true, false));
    Check(!NativeSavePolicy.NeedsExtenderOverride(false, false));
    Check(!NativeSavePolicy.NeedsExtenderOverride(false, true));
});
Test("Unused grass at neighboring coordinates is filtered without import history", () =>
{
    var candidates = new HashSet<string> { "rock", "old-grass", "neighbor-grass" };
    NativeSavePolicy.RemoveReleased(candidates, new[] { new[] { "old-grass", "neighbor-grass" } },
        name => name == "rock");
    Check(candidates.SetEquals(new[] { "rock" }));
});
Test("Active deconstructed ground and inactive non-pool scenery survive pool filtering", () =>
{
    var candidates = new HashSet<string> { "restored-dirt", "reused-grass", "dormant-object", "unused-grass" };
    NativeSavePolicy.RemoveReleased(candidates, new[] { new[] { "reused-grass", "unused-grass" } },
        name => name == "restored-dirt" || name == "reused-grass");
    Check(candidates.SetEquals(new[] { "restored-dirt", "reused-grass", "dormant-object" }));
});
Test("Imported cells survive capture and restore without widening to a whole world region", () =>
{
    var footprint = new ImportFootprint(); footprint.Record(new[] { (47, 14), (47, 14), (-40, 35) });
    var other = new ImportFootprint(); other.Restore(ImportFootprint.Read(footprint.Capture()));
    Check(other.Capture().Count == 2 && other.Contains(47, 14) && other.Contains(-40, 35));
    Check(!other.Contains(48, 14));
});
Test("Loading another save resets the footprint instead of merging old imported cells", () =>
{
    var footprint = new ImportFootprint(); footprint.Record(new[] { (1, 1) });
    footprint.Restore(ImportFootprint.Read(new[] { new StructurePosition { x = 10, y = 10 } }));
    Check(!footprint.Contains(1, 1) && footprint.Contains(10, 10));
    footprint.Reset(); Check(footprint.Capture().Count == 0);
    footprint.Restore(ImportFootprint.Read(null)); Check(footprint.Capture().Count == 0);
});
Test("Native tile regeneration clears the correct footprint boundaries only", () =>
{
    var footprint = new ImportFootprint(); footprint.Record(new[] { (-51, 0), (-50, 0), (49, 0), (50, 0) });
    footprint.ClearWorldTile(0, 0);
    Check(footprint.Contains(-51, 0) && footprint.Contains(50, 0));
    Check(!footprint.Contains(-50, 0) && !footprint.Contains(49, 0));
});
Test("Failed imports restore their previous footprint snapshot", () =>
{
    var footprint = new ImportFootprint(); footprint.Record(new[] { (1, 1) }); var saved = footprint.Capture();
    Throws<InvalidDataException>(() => ImportTransaction.Execute(() =>
    {
        footprint.Record(new[] { (2, 2) }); throw new InvalidOperationException("failure");
    }, () => footprint.Restore(ImportFootprint.Read(saved)), "footprint"));
    Check(footprint.Contains(1, 1) && !footprint.Contains(2, 2));
});
Test("Malformed footprint data is rejected before old state is replaced", () =>
{
    var footprint = new ImportFootprint(); footprint.Record(new[] { (1, 1) });
    Throws<InvalidDataException>(() => footprint.Restore(ImportFootprint.Read(new[] { new StructurePosition { x = int.MinValue } })));
    Check(footprint.Contains(1, 1));
});
Test("Destination ground uses actual dirt or water rather than exported default grass", () =>
{
    Check(DestinationGroundPolicy.Choose(new[] { ("prefab_tile_dirt_dry", false, (string?)null, 1f) }) == "prefab_tile_dirt_dry");
    Check(DestinationGroundPolicy.Choose(new[] { ("prefab_tile_water", false, (string?)null, 1f) }) == "prefab_tile_water");
});
Test("Replacing or reimporting an existing floor inherits ground without resurrecting the floor", () =>
{
    Check(DestinationGroundPolicy.Choose(new[] {
        ("prefab_tile_wood", true, (string?)"prefab_tile_dirt_dry", 1f),
        ("prefab_tile_grass", false, (string?)null, 1f)
    }) == "prefab_tile_dirt_dry");
});
Test("Missing destination ground is not silently replaced with a default", () =>
{
    Check(DestinationGroundPolicy.Choose(Array.Empty<(string, bool, string?, float)>()) == null);
    Check(DestinationGroundPolicy.Choose(new[] { ("prefab_tile_wood", true, (string?)null, 1f) }) == null);
});
Test("Portable exports omit source ground history but retain zone and other component data", () =>
{
    var file = new StructureFile();
    file.supportingTerrain.Add(new SupportingTerrain { components = new() {
        new StructureComponent { type = "GrassTileHandler, Assembly-CSharp", dataBase64 = "old-ground" },
        new StructureComponent { type = "MapZone, Assembly-CSharp", dataBase64 = "zone" }
    }});
    file.objects.Add(new StructureObject { components = new() {
        new StructureComponent { type = "GrassTileHandler, Assembly-CSharp", dataBase64 = "old-ground" },
        new StructureComponent { type = "Mod.GrassTileHandler, Example", dataBase64 = "custom" }
    }});
    DestinationGroundPolicy.StripExportedHistory(file);
    Check(file.supportingTerrain.Single().components.Single().dataBase64 == "zone");
    Check(file.objects.Single().components.Single().dataBase64 == "custom");
});
(int X, int Y) CellOf(float x, float y) => ((int)Math.Round(x), (int)Math.Round(y));
foreach (string water in new[] { "prefab_tile_water", "prefab_tile_water_lake", "prefab_tile_water_swamp",
    "prefab_tile_water_shallow", "prefab_tile_water_shallow_swamp" })
{
    Test($"World export toggle controls {water} inclusion only", () =>
    {
        Check(!WaterExportPolicy.ShouldIncludeTerrain(water, false));
        Check(!WaterExportPolicy.ShouldIncludeTerrain(water.ToUpperInvariant(), false));
        Check(WaterExportPolicy.ShouldIncludeTerrain(water, true));
    });
    Test($"JSON water {water} always enters the import footprint and queued plan", () =>
    {
        var waterRecord = new SupportingTerrain { prefabName = water, x = 100, y = 12, z = 1 };
        var file = new StructureFile { supportingTerrain = new() { waterRecord } };
        // This remains importable even when the export policy excludes that prefab.
        Check(!WaterExportPolicy.ShouldIncludeTerrain(water, false));
        var parts = StructurePlans.Partition(file, TileOf);
        Check(parts.Count == 1 && ReferenceEquals(parts[(1, 0)].supportingTerrain.Single(), waterRecord));
        var cells = DestinationSupportPolicy.GetUnreplacedCells(parts[(1, 0)], new[] { (100, 12) }, CellOf);
        Check(cells.Contains((100, 12)) == (StructurePlacementPolicy.Slot(water) == PlacementSlot.WaterOverlay));
        Check(file.supportingTerrain.Single() == waterRecord);
    });
}
Test("Bath water and nonwater ground always remain exportable", () =>
{
    foreach (string prefab in new[] { "prefab_tile_water_bathhouse", "PREFAB_TILE_WATER_BATHHOUSE",
        "prefab_tile_grass", "prefab_tile_dirt_dry", "prefab_tile_rock", "prefab_tile_wood" })
    {
        Check(WaterExportPolicy.ShouldIncludeTerrain(prefab, false));
        Check(WaterExportPolicy.ShouldIncludeTerrain(prefab, true));
    }
});
Test("Export filtering a water overlay keeps its zoned floor without mutating source records", () =>
{
    var source = new List<SupportingTerrain> {
        new() { prefabName = "prefab_tile_water_shallow", x = 5, y = -3 },
        new() { prefabName = "prefab_tile_rock", x = 5, y = -3, hasMapZoneName = true, mapZoneName = "Courtyard" } };
    var exported = source.Where(item => WaterExportPolicy.ShouldIncludeTerrain(item.prefabName, false)).ToList();
    Check(exported.Single().prefabName == "prefab_tile_rock" && exported.Single().mapZoneName == "Courtyard");
    Check(source.Count == 2);
});
foreach (string water in new[] { "prefab_tile_water_bathhouse", "prefab_tile_water_shallow", "prefab_tile_water_shallow_swamp" })
{
    Test($"{water} uses the shared water overlay slot, including uppercase names", () =>
    {
        Check(StructurePlacementPolicy.Slot(water) == PlacementSlot.WaterOverlay);
        Check(StructurePlacementPolicy.Slot(water.ToUpperInvariant()) == PlacementSlot.WaterOverlay);
    });
    Test($"Painting {water} replaces only water overlays, not zoned floors or neighbors", () =>
    {
        var floor = new SupportingTerrain { prefabName = "prefab_tile_rock", x = 5, y = -3,
            hasMapZoneName = true, mapZoneName = "Courtyard" };
        var neighbor = new SupportingTerrain { prefabName = water, x = 6, y = -3 };
        var file = new StructureFile { supportingTerrain = new() { floor, neighbor,
                new() { prefabName = "prefab_tile_water_bathhouse", x = 5, y = -3 },
                new() { prefabName = "prefab_tile_water_shallow", x = 5, y = -3 } },
            objects = new() { new() { prefabName = "prefab_tile_water_shallow_swamp", x = 5, y = -3 },
                new() { prefabName = "prefab_furniture_candle", x = 5, y = -3 } } };
        Check(StructurePlacementPolicy.ReplaceSlot(file, water, new[] { (5, -3) }) == 3);
        Check(file.supportingTerrain.SequenceEqual(new[] { floor, neighbor }));
        Check(floor.hasMapZoneName && floor.mapZoneName == "Courtyard");
        Check(file.objects.Single().prefabName == "prefab_furniture_candle");
    });
    Test($"Painting floor under {water} keeps the overlay", () =>
    {
        var overlay = new SupportingTerrain { prefabName = water };
        var file = new StructureFile { supportingTerrain = new() { overlay, new() { prefabName = "prefab_tile_grass" } } };
        Check(StructurePlacementPolicy.ReplaceSlot(file, "prefab_tile_wood", new[] { (0, 0) }) == 1);
        Check(ReferenceEquals(file.supportingTerrain.Single(), overlay));
    });
    Test($"{water} and an explicit floor coexist and do not carry a hidden extra floor", () =>
    {
        var file = new StructureFile { supportingTerrain = new() {
            new() { prefabName = water, x = 5, y = -3 },
            new() { prefabName = "prefab_tile_wood", x = 5, y = -3, hasMapZoneName = true, mapZoneName = "Courtyard" } } };
        Check(StructurePlacementPolicy.FindConflict(file) == null);
        Check(DestinationSupportPolicy.GetUnreplacedCells(file, new[] { (5, -3) }, CellOf).Count == 0);
        Check(file.supportingTerrain[1].mapZoneName == "Courtyard");
        // Old/raw JSON may represent the water in the objects list instead.
        file.supportingTerrain.RemoveAt(0);
        file.objects.Add(new StructureObject { prefabName = water, x = 5, y = -3 });
        Check(StructurePlacementPolicy.FindConflict(file) == null);
        Check(DestinationSupportPolicy.GetUnreplacedCells(file, new[] { (5, -3) }, CellOf).Count == 0);
    });
    Test($"Standalone {water} carries destination ground through current and legacy records", () =>
    {
        var file = new StructureFile { supportingTerrain = new() { new() { prefabName = water, x = 5, y = -3 } } };
        Check(DestinationSupportPolicy.GetUnreplacedCells(file, new[] { (5, -3) }, CellOf).SetEquals(new[] { (5, -3) }));
        file.supportingTerrain.Clear();
        file.objects.Add(new StructureObject { prefabName = water, x = 5, y = -3 });
        Check(DestinationSupportPolicy.GetUnreplacedCells(file, new[] { (5, -3) }, CellOf).SetEquals(new[] { (5, -3) }));
    });
    Test($"{water} conflicts with every other water overlay across both record lists", () =>
    {
        foreach (string other in new[] { "prefab_tile_water_bathhouse", "prefab_tile_water_shallow", "prefab_tile_water_shallow_swamp" })
        {
            var file = new StructureFile { supportingTerrain = new() { new() { prefabName = water } },
                objects = new() { new() { prefabName = other } } };
            Check(StructurePlacementPolicy.FindConflict(file)!.StartsWith("WaterOverlay overlap"));
        }
    });
    Test($"Queued {water} retains support at the destination without changing its neighbor", () =>
    {
        var file = new StructureFile { supportingTerrain = new() {
            new() { prefabName = water, x = 100 }, new() { prefabName = "prefab_tile_rock", x = 0 } } };
        var parts = StructurePlans.Partition(file, TileOf);
        Check(DestinationSupportPolicy.GetUnreplacedCells(parts[(0, 0)], new[] { (0, 0) }, CellOf).Count == 0);
        Check(DestinationSupportPolicy.GetUnreplacedCells(parts[(1, 0)], new[] { (100, 0) }, CellOf).SetEquals(new[] { (100, 0) }));
    });
}
foreach (string prefab in new[] {
    "prefab_furniture_well", "prefab_furniture_bathtub", "prefab_furniture_bench_wooden",
    "prefab_furniture_table_wood", "prefab_furniture_candle", "prefab_furniture_sign",
    "prefab_furniture_towel_wall", "prefab_crop_turnip", "prefab_herb_lavender",
    "prefab_ore_iron", "prefab_wall_wood", "prefab_wall_wood_door", "prefab_wall_rock_window"
})
{
    Test($"Standalone {prefab} carries destination support instead of clearing a hole", () =>
    {
        var file = new StructureFile { objects = new() { new() { prefabName = prefab, x = 4, y = -3 } } };
        var cells = DestinationSupportPolicy.GetUnreplacedCells(file, new[] { (4, -3) }, CellOf);
        Check(cells.SetEquals(new[] { (4, -3) }));
        Check(file.supportingTerrain.Count == 0); // No destination history added to the portable JSON.
    });
}
Test("An explicit floor replaces destination ground without keeping a hidden second floor", () =>
{
    var file = new StructureFile { supportingTerrain = new() { new() { prefabName = "prefab_tile_wood", x = 4, y = -3 } } };
    Check(DestinationSupportPolicy.GetUnreplacedCells(file, new[] { (4, -3) }, CellOf).Count == 0);
});
Test("Multicell objects retain destination support only at cells missing imported terrain", () =>
{
    var file = new StructureFile { supportingTerrain = new() { new() { prefabName = "prefab_tile_rock", x = 4, y = -3 } } };
    var cells = DestinationSupportPolicy.GetUnreplacedCells(file, new[] { (4, -3), (5, -3), (5, -3) }, CellOf);
    Check(cells.SetEquals(new[] { (5, -3) }));
    Check(!cells.Contains((6, -3)));
});
Test("Imported deep water replaces ground while bath water preserves support", () =>
{
    var file = new StructureFile { supportingTerrain = new() {
        new() { prefabName = "prefab_tile_water", x = 4, y = -3 },
        new() { prefabName = "prefab_tile_water_bathhouse", x = 5, y = -3 }
    }};
    var withWater = DestinationSupportPolicy.GetUnreplacedCells(file, new[] { (4, -3), (5, -3) }, CellOf);
    Check(withWater.SetEquals(new[] { (5, -3) }));
});
Test("Explicit floor beneath bath water supplies its own support without carrying an extra floor", () =>
{
    var file = new StructureFile { supportingTerrain = new() {
        new() { prefabName = "prefab_tile_rock", x = 5, y = -3, hasMapZoneName = true, mapZoneName = "Bathhouse" },
        new() { prefabName = "prefab_tile_water_bathhouse", x = 5, y = -3 } } };
    Check(StructurePlacementPolicy.FindConflict(file) == null);
    Check(DestinationSupportPolicy.GetUnreplacedCells(file, new[] { (5, -3) }, CellOf).Count == 0);
    Check(file.supportingTerrain[0].mapZoneName == "Bathhouse");
});
Test("Legacy raw bath-water records preserve destination support too", () =>
{
    var file = new StructureFile { objects = new() { new() { prefabName = "prefab_tile_water_bathhouse", x = 5, y = -3 } } };
    Check(DestinationSupportPolicy.GetUnreplacedCells(file, new[] { (5, -3) }, CellOf).Contains((5, -3)));
});
Test("Raw tile objects also replace destination support using the importer's rounding", () =>
{
    var file = new StructureFile { objects = new() {
        new() { prefabName = "PREFAB_TILE_ROCK", x = -2.5f, y = 3.5f }
    }};
    Check(DestinationSupportPolicy.GetUnreplacedCells(file, new[] { (-2, 4) }, CellOf).Count == 0);
});
Test("Nonterrain supporting records cannot pretend to supply ground", () =>
{
    var file = new StructureFile { supportingTerrain = new() { new() { prefabName = "prefab_furniture_well", x = 4, y = -3 } } };
    Check(DestinationSupportPolicy.GetUnreplacedCells(file, new[] { (4, -3) }, CellOf).Contains((4, -3)));
});
Test("Queued import parts decide ground support from their own destination cells", () =>
{
    var file = new StructureFile { objects = new() {
        new() { prefabName = "prefab_furniture_well", x = 0 },
        new() { prefabName = "prefab_furniture_well", x = 100 }
    }, supportingTerrain = new() { new() { prefabName = "prefab_tile_rock", x = 0 } } };
    var parts = StructurePlans.Partition(file, TileOf);
    Check(DestinationSupportPolicy.GetUnreplacedCells(parts[(0, 0)], new[] { (0, 0) }, CellOf).Count == 0);
    Check(DestinationSupportPolicy.GetUnreplacedCells(parts[(1, 0)], new[] { (100, 0) }, CellOf).Contains((100, 0)));
});
Test("Planning-only and empty imports never capture unrelated destination support", () =>
{
    var file = new StructureFile { planningTiles = new() { new() { x = 4, y = -3 } } };
    Check(DestinationSupportPolicy.GetUnreplacedCells(file, Array.Empty<(int, int)>(), CellOf).Count == 0);
});
Test("Carried plain ground retains its exact state through supplemental save restore and reimport", () =>
{
    var terrain = Scenery("prefab_tile_dirt_dry", 4, -3, 1);
    terrain.hasMapZoneName = true; terrain.mapZoneName = "Courtyard";
    terrain.spriteVariantIndex = 2; terrain.lockSpriteVariant = true;
    terrain.components.Add(new StructureComponent { type = "ExampleGroundState", dataBase64 = "AQID" });
    var ledger = new StructurePersistenceLedger();
    ledger.RecordReplacement(new[] { terrain }, new[] { terrain });
    ledger.RecordReplacement(new[] { terrain }, new[] { terrain });
    var reloaded = new StructurePersistenceLedger(); reloaded.Restore(ledger.Objects.Values, ledger.Cleared.Values);
    var kept = reloaded.Objects.Values.Single();
    Check(kept.prefabName == "prefab_tile_dirt_dry" && kept.mapZoneName == "Courtyard");
    Check(kept.spriteVariantIndex == 2 && kept.lockSpriteVariant && kept.components.Single().dataBase64 == "AQID");
    Check(reloaded.Cleared.Count == 1 && reloaded.Objects.Count == 1);
});
Test("A failed import restores carried-ground persistence along with the original objects", () =>
{
    var ledger = new StructurePersistenceLedger();
    var terrain = Scenery("prefab_tile_dirt_dry", 4, -3, 1);
    var before = ledger.Objects.Values.ToArray(); var cleared = ledger.Cleared.Values.ToArray();
    Throws<InvalidDataException>(() => ImportTransaction.Execute(() =>
    {
        ledger.RecordReplacement(new[] { terrain }, new[] { terrain });
        throw new InvalidOperationException("Import failed after carrying ground");
    }, () => ledger.Restore(before, cleared), "well and ground"));
    Check(ledger.Objects.Count == 0 && ledger.Cleared.Count == 0);
});
Test("Loading fewer grass records never saves the spare at an untouched coordinate", () =>
{
    foreach (var release in new[] { new[] { 0, 1, 2 }, new[] { 1, 0, 2 } })
    {
        var positions = new[] { 0, 1, 2 }; var active = new bool[3]; var pool = new Stack<int>(release);
        foreach (int destination in new[] { 0, 1 }) { int id = pool.Pop(); positions[id] = destination; active[id] = true; }
        var candidates = new HashSet<int> { 0, 1, 2 };
        NativeSavePolicy.RemoveReleased(candidates, new[] { pool }, id => active[id]);
        Check(candidates.Count == 2 && candidates.Select(id => positions[id]).Distinct().Count() == 2);
        Check(pool.Count == 1); // Filtering does not mutate the pool or live world.
    }
});
TerrainRepairPolicy.Item Grass(int id, float x = 48, float y = 35, float z = 1, bool safe = true) =>
    new() { Id = id, X = x, Y = y, Z = z, Prefab = TerrainRepairPolicy.Grass, Safe = safe, Order = id };
Test("Repair keeps exactly one existing grass at an untouched cell", () =>
{
    var items = new[] { Grass(1), Grass(2), Grass(3) };
    var plan = TerrainRepairPolicy.Build(items);
    Check(plan.Groups.Count == 1 && plan.RemoveCount == 2 && plan.Groups[0].Keep.Id == 1);
    Check(plan.Skipped.Count == 0 && items.Length == 3);
});
Test("Repair plan is independent of scene enumeration order", () =>
{
    var items = new[] { Grass(4, -4), Grass(1), Grass(2), Grass(3, -4) };
    Check(TerrainRepairPolicy.Build(items).Signature == TerrainRepairPolicy.Build(items.Reverse()).Signature);
});
Test("Repair is idempotent after excess grass is removed", () =>
{
    var plan = TerrainRepairPolicy.Build(new[] { Grass(1), Grass(2), Grass(3, 2) });
    Check(TerrainRepairPolicy.Build(new[] { plan.Groups[0].Keep, Grass(3, 2) }).RemoveCount == 0);
});
Test("Named-zone or mod-modified candidates block the whole cell", () =>
{
    var plan = TerrainRepairPolicy.Build(new[] { Grass(1), Grass(2, safe: false), Grass(3) });
    Check(plan.RemoveCount == 0 && plan.Skipped.Count == 1);
});
Test("Floor water dirt and other terrain layers prevent ambiguous grass removal", () =>
{
    foreach (string name in new[] { "prefab_tile_rock", "prefab_tile_wood", "prefab_tile_water", "prefab_tile_dirt_dry" })
    {
        var layer = Grass(3); layer.Prefab = name;
        var plan = TerrainRepairPolicy.Build(new[] { Grass(1), Grass(2), layer });
        Check(plan.RemoveCount == 0 && plan.Skipped.Count == 1);
    }
});
Test("Unverified overgrowth prefabs are not assumed to be disposable edging", () =>
{
    var decoration = Grass(3); decoration.Prefab = "prefab_tile_grass_overgrowth";
    var plan = TerrainRepairPolicy.Build(new[] { Grass(1), Grass(2), decoration });
    Check(plan.RemoveCount == 0 && plan.Skipped.Single().Reasons.Contains("Other terrain layers share the cell"));
});
Test("Unsafe runtime grass state explains why a cell was skipped", () =>
{
    var a = Grass(1, safe: false); var b = Grass(2, safe: false);
    a.UnsafeReason = b.UnsafeReason = "Unrecognized content attached to grass";
    var plan = TerrainRepairPolicy.Build(new[] { a, b });
    Check(plan.RemoveCount == 0 && plan.Skipped.Single().Reasons.Count == 1);
    Check(plan.SkipSummary == "Unrecognized content attached to grass: 1 cell(s).");
});
Test("Skipped-cell reasons count cells rather than duplicate objects", () =>
{
    var a = Grass(1, safe: false); var b = Grass(2, safe: false); var c = Grass(3, x: 40, safe: false);
    a.UnsafeReason = b.UnsafeReason = c.UnsafeReason = "Named or missing grass zone";
    var plan = TerrainRepairPolicy.Build(new[] { a, b, c, Grass(4, x: 40) });
    Check(plan.Skipped.Count == 2 && plan.SkipSummary == "Named or missing grass zone: 2 cell(s).");
});
Test("Multiple independent ambiguity reasons are preserved", () =>
{
    var a = Grass(1, safe: false); a.UnsafeReason = "Named or missing grass zone";
    var floor = Grass(3); floor.Prefab = "prefab_tile_rock";
    var plan = TerrainRepairPolicy.Build(new[] { a, Grass(2, z: 0.5f), floor });
    Check(plan.RemoveCount == 0 && plan.Skipped.Single().Reasons.Count == 3);
    Check(plan.SkipSummary.Contains("Nonstandard terrain coordinates or depth"));
});
Test("A new unsafe child invalidates a previously approved repair", () =>
{
    var first = TerrainRepairPolicy.Build(new[] { Grass(1), Grass(2) });
    var modified = Grass(2, safe: false); modified.UnsafeReason = "Unrecognized content attached to grass";
    var second = TerrainRepairPolicy.Build(new[] { Grass(1), modified });
    Check(first.Signature != second.Signature && second.RemoveCount == 0);
});
Test("Different Z or nonstandard depth and fractional coordinates are not flattened", () =>
{
    foreach (var unusual in new[] { Grass(2, z: 0.5f), Grass(2, z: float.NaN) })
        Check(TerrainRepairPolicy.Build(new[] { Grass(1), unusual }).RemoveCount == 0);
    Check(TerrainRepairPolicy.Build(new[] { Grass(1, x: 1.5f), Grass(2, x: 1.5f) }).RemoveCount == 0);
});
Test("Repeated references to the same terrain object are not deleted as duplicates", () =>
{
    var one = Grass(1); Check(TerrainRepairPolicy.Build(new[] { one, one }).RemoveCount == 0);
});
Test("Repair never removes other prefab families even if they stack", () =>
{
    foreach (string name in new[] { "prefab_tree_oak", "prefab_plant_grass", "prefab_furniture_bath", "prefab_tile_rock" })
    {
        var a = Grass(1); var b = Grass(2); a.Prefab = b.Prefab = name;
        Check(TerrainRepairPolicy.Build(new[] { a, b }).RemoveCount == 0);
    }
});
Test("Changing the retained object or removal set invalidates the repair signature", () =>
{
    var original = TerrainRepairPolicy.Build(new[] { Grass(1), Grass(2) });
    var changed = TerrainRepairPolicy.Build(new[] { Grass(1), Grass(3) });
    Check(original.Signature != changed.Signature);
});
Test("Moving the same objects to another cell invalidates an old confirmation", () =>
{
    var original = TerrainRepairPolicy.Build(new[] { Grass(1), Grass(2) });
    var moved = TerrainRepairPolicy.Build(new[] { Grass(1, x: 20), Grass(2, x: 20) });
    Check(original.Signature != moved.Signature);
});
Test("Backup preserves the existing save pair and captures unsaved state separately", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "StructureHandler-repair-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        string source = Path.Combine(root, "slot.sav"); File.WriteAllText(source, "original-save");
        File.WriteAllText(source + ".moddingtools", "original-companion");
        File.WriteAllText(Path.ChangeExtension(source, ".savmeta"), "metadata");
        string backup = TerrainRepairBackup.Create(source, Path.Combine(root, "backups"), path =>
        {
            Check(File.ReadAllText(Path.Combine(Path.GetDirectoryName(path)!, "original.sav")) == "original-save");
            File.WriteAllText(path, "current-unsaved-progress"); File.WriteAllText(path + ".moddingtools", "checkpoint-companion");
        });
        Check(File.ReadAllText(source) == "original-save");
        Check(File.ReadAllText(source + ".moddingtools") == "original-companion");
        Check(File.ReadAllText(Path.Combine(backup, "original.savmeta")) == "metadata");
        Check(File.ReadAllText(Path.Combine(backup, "before-repair.sav")) == "current-unsaved-progress");
        Check(File.Exists(Path.Combine(backup, "RESTORE.txt")));
    }
    finally { Directory.Delete(root, true); }
});
Test("Missing source or failed checkpoint cannot authorize a repair", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "StructureHandler-repair-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        string source = Path.Combine(root, "slot.sav"); bool called = false;
        Throws<IOException>(() => TerrainRepairBackup.Create(source, root, _ => called = true)); Check(!called);
        File.WriteAllText(source, "original-save");
        Throws<IOException>(() => TerrainRepairBackup.Create(source, root, _ => throw new IOException("disk full")));
        Throws<IOException>(() => TerrainRepairBackup.Create(source, root, _ => { }));
        Check(File.ReadAllText(source) == "original-save");
    }
    finally { Directory.Delete(root, true); }
});
Test("Zone preview colors only explicitly named terrain, not furniture or cleared names", () =>
{
    var file = new StructureFile { supportingTerrain = new() {
        new() { prefabName = "prefab_tile_rock", hasMapZoneName = true, mapZoneName = "Bathhouse" },
        new() { prefabName = "prefab_tile_grass", x = 1, hasMapZoneName = true, mapZoneName = "" },
        new() { prefabName = "prefab_tile_wood", x = 2, hasMapZoneName = false, mapZoneName = "unused" } },
        objects = new() { new() { prefabName = "prefab_furniture_table_wood", x = 3, hasMapZoneName = true, mapZoneName = "Shop" } } };
    var zones = ZonePreview.Build(file);
    Check(zones.Count == 1 && zones[(0, 0)] == "Bathhouse");
    Check(file.supportingTerrain.Count == 3 && file.objects.Count == 1);
    Check(!file.supportingTerrain[2].hasMapZoneName && file.supportingTerrain[2].mapZoneName == "unused");
});
Test("Zone preview reads legacy floors and gives ground priority over water overlays", () =>
{
    var file = new StructureFile { objects = new() {
        new() { prefabName = "prefab_tile_wood", x = -2.5f, y = 3.5f, hasMapZoneName = true, mapZoneName = "Floor" } },
        supportingTerrain = new() {
            new() { prefabName = "prefab_tile_water_bathhouse", x = -2, y = 4, hasMapZoneName = true, mapZoneName = "Water" } } };
    Check(ZonePreview.Build(file)[(-2, 4)] == "Floor");
});
Test("Zone colors are deterministic and preserve distinct case-sensitive zone names", () =>
{
    Check(ZonePreview.Hue("Bathhouse") == ZonePreview.Hue(new string("Bathhouse".ToCharArray())));
    Check(ZonePreview.Hue("Bathhouse") != ZonePreview.Hue("bathhouse"));
    foreach (string zone in new[] { "Bathhouse", "Bakery", "Tavern - Kitchen", "", "石" })
        Check(ZonePreview.Hue(zone) >= 0 && ZonePreview.Hue(zone) < 1);
});
Test("Rebuilding zone preview reflects renamed or removed zones without retaining old cells", () =>
{
    var file = new StructureFile { supportingTerrain = new() {
        new() { prefabName = "prefab_tile_rock", hasMapZoneName = true, mapZoneName = "Old" } } };
    var before = ZonePreview.Build(file);
    file.supportingTerrain[0].mapZoneName = "New";
    Check(ZonePreview.Build(file)[(0, 0)] == "New" && before[(0, 0)] == "Old");
    file.supportingTerrain[0].mapZoneName = "";
    Check(ZonePreview.Build(file).Count == 0);
});
Test("Zone painting changes only zone metadata on existing terrain", () =>
{
    var tile = new SupportingTerrain {
        prefabName = "prefab_tile_wood", x = 2, y = -3, z = 1,
        spriteVariantIndex = 4, lockSpriteVariant = true,
        components = new() { new() { type = "MapZone", dataBase64 = "original state" } }
    };
    var file = new StructureFile { supportingTerrain = new() { tile } };
    var components = tile.components;
    Check(ZonePainting.Apply(file, new[] { Tuple.Create(true, 0) }, "Bakery") == 1);
    Check(tile.hasMapZoneName && tile.mapZoneName == "Bakery");
    Check(tile.prefabName == "prefab_tile_wood" && tile.x == 2 && tile.y == -3 && tile.z == 1);
    Check(tile.spriteVariantIndex == 4 && tile.lockSpriteVariant);
    Check(ReferenceEquals(tile.components, components) && components.Single().dataBase64 == "original state");
    Check(file.supportingTerrain.Count == 1 && file.objects.Count == 0);
});
Test("Zone painting skips furniture and walls above a floor", () =>
{
    var file = new StructureFile {
        supportingTerrain = new() { new() { prefabName = "prefab_tile_rock" } },
        objects = new() {
            new() { prefabName = "prefab_furniture_table_wood", hasMapZoneName = true, mapZoneName = "Leave alone" },
            new() { prefabName = "prefab_wall_stone" },
            new() { prefabName = "prefab_door_wood" }
        }
    };
    var entries = new[] { Tuple.Create(true, 0), Tuple.Create(false, 0), Tuple.Create(false, 1), Tuple.Create(false, 2) };
    Check(ZonePainting.Apply(file, entries, "Tavern") == 1);
    Check(file.objects[0].mapZoneName == "Leave alone" && !file.objects[1].hasMapZoneName && !file.objects[2].hasMapZoneName);
});
Test("Zone painting covers stacked ground and legacy floor records in the indexed cell only", () =>
{
    var file = new StructureFile {
        supportingTerrain = new() {
            new() { prefabName = "prefab_tile_grass" }, new() { prefabName = "prefab_tile_rock" },
            new() { prefabName = "prefab_tile_wood", x = 1, mapZoneName = "Unpainted", hasMapZoneName = true }
        },
        objects = new() { new() { prefabName = "PREFAB_TILE_WOOD", turnableIndex = 2 } }
    };
    Check(ZonePainting.Apply(file, new[] { Tuple.Create(true, 0), Tuple.Create(true, 1), Tuple.Create(false, 0) }, "Hall") == 3);
    Check(file.objects[0].mapZoneName == "Hall" && file.objects[0].turnableIndex == 2);
    Check(file.supportingTerrain[2].mapZoneName == "Unpainted");
});
Test("Empty zone strokes create no objects or terrain", () =>
{
    var file = new StructureFile();
    Check(ZonePainting.Apply(file, Array.Empty<Tuple<bool, int>>(), "Empty") == 0);
    Check(file.objects.Count == 0 && file.supportingTerrain.Count == 0);
});
Test("Repeated zone painting is a no-op and blank names are explicit overrides", () =>
{
    var tile = new SupportingTerrain { mapZoneName = "Bakery" };
    var file = new StructureFile { supportingTerrain = new() { tile } };
    var entries = new[] { Tuple.Create(true, 0) };
    Check(ZonePainting.Apply(file, entries, "Bakery") == 1); // Flag must also be set.
    Check(ZonePainting.Apply(file, entries, "Bakery") == 0);
    Check(ZonePainting.Apply(file, entries, "") == 1 && tile.hasMapZoneName && tile.mapZoneName == "");
    Check(ZonePainting.Apply(file, entries, "") == 0);
});
Test("Zone drags fill skipped cells in both directions, including negative coordinates", () =>
{
    var stroke = new ZonePaintStroke();
    Check(stroke.Trace(-3, 2, true).SequenceEqual(new[] { (-3, 2) }));
    Check(stroke.Trace(1, 2, false).SequenceEqual(new[] { (-3, 2), (-2, 2), (-1, 2), (0, 2), (1, 2) }));
    Check(stroke.Trace(-1, 0, false).SequenceEqual(new[] { (1, 2), (0, 1), (-1, 0) }));
});
Test("Zone strokes never connect across preview exits, panning, or zooming", () =>
{
    var stroke = new ZonePaintStroke();
    _ = stroke.Trace(0, 0, true).ToArray();
    stroke.BreakPath();
    Check(stroke.Trace(10, 10, false).SequenceEqual(new[] { (10, 10) }));
});
Test("Zone painting requires a preview mouse-down and ends on release or cancellation", () =>
{
    var stroke = new ZonePaintStroke();
    Check(!stroke.Trace(4, 4, false).Any());
    _ = stroke.Trace(0, 0, true).ToArray();
    stroke.End();
    Check(!stroke.Trace(20, 20, false).Any());
    Check(stroke.Trace(30, 30, true).SequenceEqual(new[] { (30, 30) }));
});
Test("Painted zones survive JSON round-trip and an entire stroke can be undone/redone", () =>
{
    var options = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
    var file = new StructureFile { supportingTerrain = new() { new() { x = 0 }, new() { x = 1 } } };
    string Snapshot() => System.Text.Json.JsonSerializer.Serialize(file, options);
    var history = new SnapshotHistory();
    history.Reset(Snapshot());
    ZonePainting.Apply(file, new[] { Tuple.Create(true, 0) }, "Bath House");
    ZonePainting.Apply(file, new[] { Tuple.Create(true, 1) }, "Bath House");
    history.Record(Snapshot());
    var undone = System.Text.Json.JsonSerializer.Deserialize<StructureFile>(history.Undo()!, options)!;
    Check(undone.supportingTerrain.All(t => !t.hasMapZoneName));
    var redone = System.Text.Json.JsonSerializer.Deserialize<StructureFile>(history.Redo()!, options)!;
    Check(redone.supportingTerrain.All(t => t.hasMapZoneName && t.mapZoneName == "Bath House"));
});
Test("Furniture collision reads prefab defaults without changing legacy records", () =>
{
    var item = new StructureObject();
    Check(FurnitureCollision.TryReadEnabled(item.components, false, out var enabled) && enabled);
    Check(FurnitureCollision.TryReadEnabled(item.components, true, out enabled) && !enabled);
    Check(FurnitureCollision.TryReadEnabled(null, false, out enabled) && enabled);
    Check(item.components.Count == 0);
});
Test("Furniture collision writes native passable state and reuses the same record", () =>
{
    var item = new StructureObject { prefabName = "prefab_furniture_table_wood", x = 3, y = 7, z = 1,
        turnableIndex = 2, npcInteractionRangeExtender = true };
    FurnitureCollision.SetEnabled(item, false);
    var record = item.components.Single();
    Check(record.type == "TurfCollider, Assembly-CSharp" && record.hierarchyPath == "" && record.componentIndex == 0);
    Check(record.dataBase64 == "AQ==");
    Check(FurnitureCollision.TryReadEnabled(item.components, false, out var enabled) && !enabled);
    FurnitureCollision.SetEnabled(item, true);
    Check(ReferenceEquals(record, item.components.Single()) && record.dataBase64 == "AA==");
    Check(FurnitureCollision.TryReadEnabled(item.components, true, out enabled) && enabled);
    Check(item.x == 3 && item.y == 7 && item.z == 1 && item.turnableIndex == 2 && item.npcInteractionRangeExtender);
});
Test("Furniture collision preserves other components, child colliders, and legacy identity", () =>
{
    var item = new StructureObject { components = new() {
        new() { type = "Turnable", dataBase64 = "AAECAw==" },
        new() { type = "TurfCollider, Assembly-CSharp, Version=0.0.0.0, Culture=neutral", componentIndex = -1, dataBase64 = "AA==" },
        new() { type = "TurfCollider", hierarchyPath = "0/1", componentIndex = 0, dataBase64 = "AA==" },
        new() { type = "Mod.TurfCollider", dataBase64 = "AQID" }
    } };
    string type = item.components[1].type;
    FurnitureCollision.SetEnabled(item, false);
    Check(item.components.Count == 4 && item.components[0].dataBase64 == "AAECAw==");
    Check(item.components[1].dataBase64 == "AQ==" && item.components[1].type == type && item.components[1].componentIndex == -1);
    Check(item.components[2].dataBase64 == "AA==" && item.components[3].dataBase64 == "AQID");
});
Test("Furniture collision uses the importer last-record-wins order for aliases", () =>
{
    var item = new StructureObject { components = new() {
        new() { type = "TurfCollider", dataBase64 = "AA==" },
        new() { type = "TurfCollider, Assembly-CSharp", componentIndex = 0, dataBase64 = "AQ==" }
    } };
    Check(FurnitureCollision.TryReadEnabled(item.components, false, out var enabled) && !enabled);
    FurnitureCollision.SetEnabled(item, true);
    Check(item.components.Count == 2 && item.components.All(c => c.dataBase64 == "AA=="));
});
Test("Malformed furniture collision payloads cannot cause partial edits", () =>
{
    foreach (string invalid in new[] { "", "!invalid!", "AAE=" })
    {
        var item = new StructureObject { components = new() {
            new() { type = "TurfCollider", dataBase64 = "AA==" },
            new() { type = "TurfCollider", dataBase64 = invalid }
        } };
        Check(!FurnitureCollision.TryReadEnabled(item.components, false, out _));
        Throws<InvalidDataException>(() => FurnitureCollision.SetEnabled(item, false));
        Check(item.components.Count == 2 && item.components[0].dataBase64 == "AA==" && item.components[1].dataBase64 == invalid);
    }
});
Test("Nondefault collider indices are rejected instead of silently overwritten", () =>
{
    var item = new StructureObject { components = new() {
        new() { type = "TurfCollider", componentIndex = 1, dataBase64 = "AA==" }
    } };
    Check(!FurnitureCollision.TryReadEnabled(item.components, false, out _));
    Throws<InvalidDataException>(() => FurnitureCollision.SetEnabled(item, false));
    Check(item.components.Single().dataBase64 == "AA==");
});
Test("Furniture collision survives JSON, independent copies, and undo redo", () =>
{
    var options = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
    var item = new StructureObject { prefabName = "prefab_furniture_table_wood" };
    string Snapshot() => System.Text.Json.JsonSerializer.Serialize(item, options);
    var history = new SnapshotHistory(); history.Reset(Snapshot());
    FurnitureCollision.SetEnabled(item, false); history.Record(Snapshot());
    var undone = System.Text.Json.JsonSerializer.Deserialize<StructureObject>(history.Undo()!, options)!;
    Check(undone.components.Count == 0);
    var redone = System.Text.Json.JsonSerializer.Deserialize<StructureObject>(history.Redo()!, options)!;
    Check(FurnitureCollision.TryReadEnabled(redone.components, false, out var enabled) && !enabled);
    FurnitureCollision.SetEnabled(redone, true);
    Check(item.components.Single().dataBase64 == "AQ==" && redone.components.Single().dataBase64 == "AA==");
});
Test("Queued structure partitioning retains native furniture collision records", () =>
{
    var item = new StructureObject { prefabName = "prefab_furniture_table_wood", x = 100, y = 10 };
    FurnitureCollision.SetEnabled(item, false);
    var file = new StructureFile { objects = new() { item } };
    var parts = StructurePlans.Partition(file, TileOf);
    var queued = parts[(1, 0)].objects.Single();
    Check(FurnitureCollision.TryReadEnabled(queued.components, false, out var enabled) && !enabled);
    Check(queued.components.Single().dataBase64 == "AQ==");
});
PrefabLifecycleChecks.Run(Test, Check);
Console.WriteLine($"{passed} regression tests passed.");
