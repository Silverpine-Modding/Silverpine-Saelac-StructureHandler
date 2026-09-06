using System;
using System.Collections.Generic;
using System.Linq;

namespace StructureHandler;

// Selection is independent of deserialization: unchecked JSON files are never read.
internal sealed class ImportFileSelection
{
    private string[] paths = Array.Empty<string>();
    private readonly HashSet<string> available = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> selected = new(StringComparer.OrdinalIgnoreCase);

    internal IReadOnlyList<string> Paths => paths;
    internal int SelectedCount => selected.Count;
    internal bool IsSelected(string path) => selected.Contains(path);

    internal void Refresh(IEnumerable<string> candidates)
    {
        paths = candidates.Where(path => !string.IsNullOrWhiteSpace(path) &&
                                        path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        available.Clear();
        available.UnionWith(paths);
        selected.IntersectWith(available);
    }

    internal void SetSelected(string path, bool value)
    {
        if (!available.Contains(path)) return;
        if (value) selected.Add(path);
        else selected.Remove(path);
    }

    internal void SelectAll() => selected.UnionWith(available);
    internal void Clear() => selected.Clear();
    internal string[] Snapshot() => paths.Where(selected.Contains).ToArray();
}
