using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using StructureHandler;

internal static class EditorPerformanceChecks
{
    private static void Check(bool value) { if (!value) throw new Exception("Editor performance regression."); }

    internal static StructureFile FilledFile()
    {
        int seed = 0;
        object Fill(Type type)
        {
            seed++;
            if (type == typeof(string)) return "state-" + seed + "\nquoted: \"yes\" 🌲";
            if (type == typeof(int)) return -seed;
            if (type == typeof(float)) return seed + .125f;
            if (type == typeof(bool)) return true;
            if (typeof(IList).IsAssignableFrom(type))
            {
                var list = (IList)Activator.CreateInstance(type)!;
                for (int i = 0; i < 2; i++) list.Add(Fill(type.GetGenericArguments()[0]));
                return list;
            }
            var record = Activator.CreateInstance(type)!;
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)) field.SetValue(record, Fill(field.FieldType));
            return record;
        }
        return (StructureFile)Fill(typeof(StructureFile));
    }

    internal static void Run(Action<string, Action> test)
    {
        test("Deferred previews deduplicate and respect time and job budgets", () =>
        {
            var queue = new EditorWorkQueue<int>();
            for (int i = 0; i < 20; i++) { queue.Enqueue(i); queue.Enqueue(i); }
            Check(queue.Count == 20);
            double time = 0; var completed = new List<int>();
            int processed = queue.Process(i => { completed.Add(i); time += 1.2; }, () => time, 2, 4);
            Check(processed == 2 && queue.Count == 18 && completed.SequenceEqual(new[] { 0, 1 }));
            Check(queue.Process(i => { time += 10; }, () => time, 2, 4) == 1);
            Check(queue.Process(i => { }, () => time, 2, 4) == 4);
            queue.Retain(new HashSet<int> { 8, 10, 12 });
            Check(queue.Count == 3);
            completed.Clear(); queue.Process(completed.Add, () => time, 2, 4);
            Check(completed.SequenceEqual(new[] { 8, 10, 12 }));
            queue.Enqueue(8); Check(queue.Count == 1); queue.Clear(); Check(queue.Count == 0);
        });
        test("Coordinate drafts preserve partial typing and reset after commit, nudge or undo", () =>
        {
            var draft = new EditorCoordinateDraft(); draft.Observe("107.5");
            Check(draft.Text == "107.5");
            draft.Text = "-"; draft.Observe("107.5"); Check(draft.Text == "-");
            draft.Text = "-12.25"; draft.Observe("107.5"); Check(draft.Text == "-12.25");
            draft.Reset(); draft.Observe("107.5"); Check(draft.Text == "107.5");
            draft.Text = "9"; draft.Observe("108"); Check(draft.Text == "108");
        });
        test("Undo copier preserves every model field and isolates all mutable lists/records", () =>
        {
            var file = FilledFile();
            var copy = EditorDocumentCopy.Clone(file);
            Check(EditorDocumentCopy.Equal(file, copy));
            void Separate(object a, object b)
            {
                if (a is string || a.GetType().IsValueType) { Check(a.Equals(b)); return; }
                Check(!ReferenceEquals(a, b));
                if (a is IList list && b is IList other)
                { for (int i = 0; i < list.Count; i++) Separate(list[i]!, other[i]!); return; }
                foreach (var field in a.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)) Separate(field.GetValue(a)!, field.GetValue(b)!);
            }
            Separate(file, copy);
            copy.objects[0].components[0].dataBase64 = "edited";
            Check(!EditorDocumentCopy.Equal(file, copy) && file.objects[0].components[0].dataBase64 != "edited");
            copy.planningTiles[0].color = "#000000";
            Check(file.planningTiles[0].color != "#000000");
        });
        test("Undo copier handles legacy nulls and immutable payload sharing", () =>
        {
            var file = new StructureFile { objects = null!, name = null!, planningTiles = null! };
            var copy = EditorDocumentCopy.Clone(file);
            Check(copy.objects == null && copy.name == null && EditorDocumentCopy.Equal(file, copy));
            file = FilledFile(); copy = EditorDocumentCopy.Clone(file);
            Check(ReferenceEquals(file.objects[0].components[0].dataBase64, copy.objects[0].components[0].dataBase64));
        });
        test("In-memory history deduplicates, branches, and does not mutate previous snapshots", () =>
        {
            var history = new EditorDocumentHistory(); var file = FilledFile();
            history.Reset(file); var saved = history.Current;
            history.Record(file); Check(!history.CanUndo);
            file.objects[0].x = 8; history.Record(file);
            Check(history.CanUndo && !history.CanRedo && history.Current!.objects[0].x == 8);
            var restored = EditorDocumentCopy.Clone(history.Undo()!);
            Check(EditorDocumentCopy.Equal(restored, saved) && history.CanRedo);
            restored.objects[0].x = 9; history.Record(restored);
            Check(!history.CanRedo && saved!.objects[0].x != 9);
            Check(EditorDocumentCopy.Equal(history.Undo(), saved));
            Check(history.Redo()!.objects[0].x == 9);
            history.Reset(null); Check(history.Current == null && !history.CanRedo && !history.CanUndo);
        });
        test("History caps record count and memory while retaining a large current document", () =>
        {
            var history = new EditorDocumentHistory(); var file = new StructureFile();
            for (int i = 0; i < 40; i++) { file.name = i.ToString(); history.Record(file); }
            int undo = 0; while (history.Undo() != null) undo++;
            Check(undo == 31);
            file.name = new string('x', 9 * 1024 * 1024); history.Reset(file);
            file.exportedUtc = "next"; history.Record(file); Check(!history.CanUndo);
            file.name = new string('y', 17 * 1024 * 1024); history.Record(file);
            Check(history.Current!.name.Length == file.name.Length && !history.CanUndo);
        });
        test("Sprite visibility matches brute-force extents, retaining global order and overhangs", () =>
        {
            var random = new Random(7216); var index = new PreviewSpatialIndex();
            var boxes = new List<PreviewBounds>();
            for (int i = 0; i < 2000; i++)
            {
                float x = random.Next(-500, 501) + .25f, y = random.Next(-500, 501) - .5f;
                var box = new PreviewBounds(x, y, x + random.Next(1, 40), y + random.Next(1, 50));
                boxes.Add(box); index.Add(i, box);
            }
            boxes.Add(new PreviewBounds(-1000, -1000, 1000, 1000)); index.Add(boxes.Count - 1, boxes[boxes.Count - 1]);
            var found = new List<int>();
            for (int i = 0; i < 200; i++)
            {
                float x = random.Next(-600, 601), y = random.Next(-600, 601);
                var view = new PreviewBounds(x, y, x + 80, y + 65);
                index.Query(view, found);
                Check(found.SequenceEqual(Enumerable.Range(0, boxes.Count).Where(id => boxes[id].Overlaps(view))));
            }
            index.Query(new PreviewBounds(-100000, -100000, 100000, 100000), found);
            Check(found.SequenceEqual(Enumerable.Range(0, boxes.Count)));
            index.Clear(); index.Query(new PreviewBounds(-8, -8, 8, 8), found); Check(found.Count == 0);
        });
        test("Sprite index retains exact edge contacts and negative chunk boundaries", () =>
        {
            var index = new PreviewSpatialIndex(); var found = new List<int>();
            index.Add(0, new PreviewBounds(-16, -8, -8, 0));
            index.Add(1, new PreviewBounds(-8, 0, 0, 8));
            index.Query(new PreviewBounds(-8, 0, -8, 0), found); Check(found.SequenceEqual(new[] { 0, 1 }));
            index.Query(new PreviewBounds(-7.9f, .1f, -7.8f, .2f), found); Check(found.SequenceEqual(new[] { 1 }));
        });
        test("Placement index preserves child-collider cells, updates moves, and removes deleted records", () =>
        {
            var index = new EditorCellIndex<object>(); var found = new HashSet<object>();
            object first = new(), second = new();
            index.Set(first, new[] { (0, 0), (0, 1), (5, -4), (0, 0) });
            index.Set(second, new[] { (5, -4) });
            index.Collect(new[] { (5, -4) }, found); Check(found.SetEquals(new[] { first, second }));
            index.Set(first, new[] { (8, 8) });
            index.Collect(new[] { (0, 0), (5, -4) }, found); Check(found.SetEquals(new[] { second }));
            index.Remove(second); index.Collect(new[] { (5, -4) }, found); Check(found.Count == 0);
            index.Collect(new[] { (8, 8), (8, 8) }, found); Check(found.SetEquals(new[] { first }));
            index.Clear(); index.Collect(new[] { (8, 8) }, found); Check(found.Count == 0);
        });
        test("Incremental placement candidates match exhaustive lookup across edits", () =>
        {
            var index = new EditorCellIndex<object>(); var found = new HashSet<object>();
            var expected = new Dictionary<object, HashSet<(int, int)>>(); var random = new Random(50);
            var objects = Enumerable.Range(0, 300).Select(_ => new object()).ToArray();
            for (int i = 0; i < 1500; i++)
            {
                object record = objects[random.Next(objects.Length)];
                if (i % 7 == 0) { index.Remove(record); expected.Remove(record); }
                else
                {
                    int x = random.Next(-10, 11), y = random.Next(-10, 11);
                    var cells = new HashSet<(int, int)> { (x, y), (x + 1, y), (x - 3, y + 2) };
                    expected[record] = cells; index.Set(record, cells);
                }
                var query = new[] { (random.Next(-10, 11), random.Next(-10, 11)), (0, 0) };
                index.Collect(query, found);
                Check(found.SetEquals(expected.Where(pair => pair.Value.Overlaps(query)).Select(pair => pair.Key)));
            }
        });
    }
}
