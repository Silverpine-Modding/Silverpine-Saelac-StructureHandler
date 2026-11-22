using System.Text;
using StructureHandler;

internal static class EditorFileChecks
{
    private static void InTempFolder(Action<string> action)
    {
        var directory = Directory.CreateTempSubdirectory("StructureHandler-editor-");
        string path = Path.Combine(directory.FullName, "Blueprint.json");
        try { action(path); }
        finally { File.Delete(path); File.Delete(path + ".bak"); directory.Delete(); }
    }

    private static string Refused(string path, string expected)
    {
        try { StructureJsonFile.Write(path, "unsaved edits", expectedRevision: expected); }
        catch (StructureFileChangedException e) { return e.Revision; }
        throw new Exception("An unconfirmed version was overwritten.");
    }

    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("Editor compares file contents even when the timestamp and length did not change", () => InTempFolder(path =>
        {
            var original = StructureJsonFile.Write(path, "AAA", expectedRevision: StructureJsonFile.Missing);
            var timestamp = File.GetLastWriteTimeUtc(path);
            File.WriteAllText(path, "BBB"); File.SetLastWriteTimeUtc(path, timestamp);
            var observed = Refused(path, original);
            check(StructureJsonFile.Read(path, out var actual) == "BBB" && actual == observed && observed != original &&
                !File.Exists(path + ".bak"));
            var saved = StructureJsonFile.Write(path, "my changes", expectedRevision: observed);
            check(StructureJsonFile.Read(path, out actual) == "my changes" && saved == actual && File.ReadAllText(path + ".bak") == "BBB");
        }));
        test("An overwrite confirmation applies only to the version the user was asked about", () => InTempFolder(path =>
        {
            File.WriteAllText(path, "other file");
            var confirmed = Refused(path, StructureJsonFile.Missing);
            File.WriteAllText(path, "edition 2");
            var second = Refused(path, confirmed);
            check(second != confirmed && File.ReadAllText(path) == "edition 2" && !File.Exists(path + ".bak"));
            StructureJsonFile.Write(path, "new blueprint", expectedRevision: second);
            check(File.ReadAllText(path + ".bak") == "edition 2");
        }));
        test("A removed file is not recreated without consent or overwritten if it reappears", () => InTempFolder(path =>
        {
            var original = StructureJsonFile.Write(path, "", expectedRevision: StructureJsonFile.Missing);
            check(original != StructureJsonFile.Missing); // A zero-byte file is not an absent file.
            File.Delete(path);
            var missing = Refused(path, original);
            check(!File.Exists(path) && missing == StructureJsonFile.Missing);
            File.WriteAllText(path, "returned");
            check(Refused(path, missing) != missing && File.ReadAllText(path) == "returned");
            File.Delete(path);
            StructureJsonFile.Write(path, "recreated", expectedRevision: missing);
            check(File.ReadAllText(path) == "recreated" && !File.Exists(path + ".bak"));
        }));
        test("Editor fingerprints the bytes it decoded, including UTF-16 BOM, and saves as UTF-8", () => InTempFolder(path =>
        {
            const string json = "{\"name\":\"Café 庭\"}";
            File.WriteAllText(path, json, Encoding.Unicode);
            check(StructureJsonFile.Read(path, out var first) == json);
            var saved = StructureJsonFile.Write(path, json, expectedRevision: first);
            check(saved != first && StructureJsonFile.Read(path, out var actual) == json && actual == saved);
            check(File.ReadAllBytes(path).SequenceEqual(new UTF8Encoding(false).GetBytes(json)));
        }));
        test("Save As at an undo point writes a new header without consuming redo", () =>
        {
            var file = new StructureFile { name = "Old", objects = new() { new() { x = 1 } } };
            var history = new EditorDocumentHistory(); history.Reset(file);
            file.objects[0].x = 2; history.Record(file);
            var editing = EditorDocumentCopy.Clone(history.Undo()!);
            var written = editing.WithSaveHeader("New", new() { new() { x = 1 } });
            history.Record(editing);
            check(written.name == "New" && written.occupiedPositions.Count == 1 &&
                !history.CanUndo && history.CanRedo && history.Redo()!.objects[0].x == 2);
        });
        test("Preview drags require a matching press, survive unrelated releases and reset between files", () =>
        {
            var drag = new EditorPreviewGesture();
            check(!drag.Owns(0) && !drag.Owns(2));
            drag.Set(0, true); drag.Set(2, true); drag.Set(2, false);
            check(drag.Owns(0) && !drag.Owns(2));
            drag.Set(0, false); check(!drag.Owns(0));
            drag.Set(2, true); drag.Set(0, true); drag.Reset();
            check(!drag.Owns(0) && !drag.Owns(2));
        });
    }
}
