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
T Throws<T>(Action body) where T : Exception
{
    try { body(); } catch (T error) { return error; }
    throw new Exception("Expected " + typeof(T).Name);
}

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
    var parts = StructurePlans.Partition(file, TileOf, _ => true);
    Check(parts.Count == 2);
    Check(parts.Values.All(p => p.objects.Count == 1 && p.supportingTerrain.Count == 1 && p.name == "Bath"));
    Check(parts.Values.Sum(p => p.objects.Count) == file.objects.Count);
});
Test("World boundary anchors are assigned to one destination", () =>
{
    var file = new StructureFile();
    foreach (float x in new[] { -50.001f, -50f, 49.999f, 50f }) file.objects.Add(new StructureObject { x = x });
    var parts = StructurePlans.Partition(file, TileOf, _ => true);
    Check(parts[(-1, 0)].objects.Count == 1 && parts[(0, 0)].objects.Count == 2 && parts[(1, 0)].objects.Count == 1);
});
Test("Water policy is honored without dropping bath water", () =>
{
    var file = new StructureFile();
    file.supportingTerrain.Add(new SupportingTerrain { prefabName = "prefab_tile_water", x = 100 });
    file.supportingTerrain.Add(new SupportingTerrain { prefabName = "prefab_tile_water_bathhouse" });
    var parts = StructurePlans.Partition(file, TileOf, name => name != "prefab_tile_water");
    Check(parts.Count == 1 && parts[(0, 0)].supportingTerrain.Count == 1);
    Check(file.supportingTerrain.Count == 2);
});
Test("Terrain-less empty imports have no destination mutations", () =>
    Check(StructurePlans.Partition(new StructureFile(), TileOf, _ => true).Count == 0));
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
    Check(StructurePlans.Partition(file, TileOf, _ => true).Count == 0);
    Check(file.objects.Count == 0 && file.supportingTerrain.Count == 0 && file.occupiedPositions.Count == 0);
});
Test("Planning outside a real structure never expands its import footprint", () =>
{
    var file = new StructureFile();
    file.objects.Add(new StructureObject { x = 0, y = 0 });
    file.planningTiles.Add(new PlanningTile { x = 500, y = -100 });
    var parts = StructurePlans.Partition(file, TileOf, _ => true);
    Check(parts.Count == 1 && parts[(0, 0)].objects.Count == 1);
    Check(parts[(0, 0)].planningTiles.Count == 0);
});
Console.WriteLine($"{passed} regression tests passed.");
