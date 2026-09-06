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
        new HashSet<string> { "tree", "grass" }, name => name == "floor");
    Check(candidates.SetEquals(new[] { "floor" }));
});
Test("Unrelated pooled scenery and deliberately inactive objects remain untouched", () =>
{
    var candidates = new HashSet<string> { "unrelated-pool-tree", "inactive-furniture", "discarded-tree" };
    NativeSavePolicy.RemoveReleased(candidates, new[] { new[] { "unrelated-pool-tree", "discarded-tree" } },
        new HashSet<string> { "discarded-tree", "inactive-furniture" }, _ => false);
    Check(candidates.SetEquals(new[] { "unrelated-pool-tree", "inactive-furniture" }));
});
Test("A reclaimed object is saved again and stale active stack entries are not removed", () =>
{
    var candidates = new HashSet<string> { "reused-tree", "active-tree" };
    var discarded = new HashSet<string> { "reused-tree", "active-tree" };
    discarded.Remove("reused-tree"); // ObjectPool.Claim postfix
    NativeSavePolicy.RemoveReleased(candidates, new[] { new[] { "reused-tree", "active-tree" } },
        discarded, name => name == "active-tree");
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
Test("Already unused grass at imported coordinates is filtered without an instance marker", () =>
{
    var candidates = new HashSet<string> { "rock", "old-grass", "neighbor-grass" };
    NativeSavePolicy.RemoveReleased(candidates, new[] { new[] { "old-grass", "neighbor-grass" } },
        new HashSet<string>(), name => name == "rock", name => name == "old-grass" || name == "rock");
    Check(candidates.SetEquals(new[] { "rock", "neighbor-grass" }));
});
Test("Active deconstructed ground and inactive non-pool scenery survive footprint filtering", () =>
{
    var candidates = new HashSet<string> { "restored-dirt", "reused-grass", "dormant-object", "unused-grass" };
    NativeSavePolicy.RemoveReleased(candidates, new[] { new[] { "reused-grass", "unused-grass" } },
        new HashSet<string>(), name => name == "restored-dirt" || name == "reused-grass", _ => true);
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
        var cells = DestinationSupportPolicy.GetUnreplacedCells(file, new[] { (4, -3) }, _ => true, CellOf);
        Check(cells.SetEquals(new[] { (4, -3) }));
        Check(file.supportingTerrain.Count == 0); // No destination history added to the portable JSON.
    });
}
Test("An explicit floor replaces destination ground without keeping a hidden second floor", () =>
{
    var file = new StructureFile { supportingTerrain = new() { new() { prefabName = "prefab_tile_wood", x = 4, y = -3 } } };
    Check(DestinationSupportPolicy.GetUnreplacedCells(file, new[] { (4, -3) }, _ => true, CellOf).Count == 0);
});
Test("Multicell objects retain destination support only at cells missing imported terrain", () =>
{
    var file = new StructureFile { supportingTerrain = new() { new() { prefabName = "prefab_tile_rock", x = 4, y = -3 } } };
    var cells = DestinationSupportPolicy.GetUnreplacedCells(file, new[] { (4, -3), (5, -3), (5, -3) }, _ => true, CellOf);
    Check(cells.SetEquals(new[] { (5, -3) }));
    Check(!cells.Contains((6, -3)));
});
Test("Filtered water does not remove the destination support while bath water replaces it", () =>
{
    var file = new StructureFile { supportingTerrain = new() {
        new() { prefabName = "prefab_tile_water", x = 4, y = -3 },
        new() { prefabName = "prefab_tile_water_bathhouse", x = 5, y = -3 }
    }};
    var cells = DestinationSupportPolicy.GetUnreplacedCells(file, new[] { (4, -3), (5, -3) },
        name => name != "prefab_tile_water", CellOf);
    Check(cells.SetEquals(new[] { (4, -3) }));
});
Test("Raw tile objects also replace destination support using the importer's rounding", () =>
{
    var file = new StructureFile { objects = new() {
        new() { prefabName = "PREFAB_TILE_ROCK", x = -2.5f, y = 3.5f }
    }};
    Check(DestinationSupportPolicy.GetUnreplacedCells(file, new[] { (-2, 4) }, _ => true, CellOf).Count == 0);
});
Test("Nonterrain supporting records cannot pretend to supply ground", () =>
{
    var file = new StructureFile { supportingTerrain = new() { new() { prefabName = "prefab_furniture_well", x = 4, y = -3 } } };
    Check(DestinationSupportPolicy.GetUnreplacedCells(file, new[] { (4, -3) }, _ => true, CellOf).Contains((4, -3)));
});
Test("Queued import parts decide ground support from their own destination cells", () =>
{
    var file = new StructureFile { objects = new() {
        new() { prefabName = "prefab_furniture_well", x = 0 },
        new() { prefabName = "prefab_furniture_well", x = 100 }
    }, supportingTerrain = new() { new() { prefabName = "prefab_tile_rock", x = 0 } } };
    var parts = StructurePlans.Partition(file, TileOf, _ => true);
    Check(DestinationSupportPolicy.GetUnreplacedCells(parts[(0, 0)], new[] { (0, 0) }, _ => true, CellOf).Count == 0);
    Check(DestinationSupportPolicy.GetUnreplacedCells(parts[(1, 0)], new[] { (100, 0) }, _ => true, CellOf).Contains((100, 0)));
});
Test("Planning-only and empty imports never capture unrelated destination support", () =>
{
    var file = new StructureFile { planningTiles = new() { new() { x = 4, y = -3 } } };
    Check(DestinationSupportPolicy.GetUnreplacedCells(file, Array.Empty<(int, int)>(), _ => true, CellOf).Count == 0);
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
Console.WriteLine($"{passed} regression tests passed.");
