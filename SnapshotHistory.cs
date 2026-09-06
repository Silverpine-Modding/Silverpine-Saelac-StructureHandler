#nullable enable
using System;
using System.Collections.Generic;
namespace StructureHandler;
internal sealed class SnapshotHistory
{
    private readonly List<string> entries = new();
    private int cursor = -1;
    internal string? Current => cursor < 0 ? null : entries[cursor];
    internal bool CanUndo => cursor > 0;
    internal bool CanRedo => cursor >= 0 && cursor + 1 < entries.Count;
    internal void Reset(string snapshot)
    {
        entries.Clear(); entries.Add(snapshot); cursor = 0;
    }
    internal void Record(string snapshot)
    {
        if (snapshot == Current) return;
        if (cursor + 1 < entries.Count) entries.RemoveRange(cursor + 1, entries.Count - cursor - 1);
        entries.Add(snapshot); cursor = entries.Count - 1;
        long bytes = 0;
        foreach (string entry in entries) bytes += entry.Length * 2L;
        while (entries.Count > 1 && (entries.Count > 32 || bytes > 32 * 1024 * 1024))
        {
            bytes -= entries[0].Length * 2L;
            entries.RemoveAt(0); cursor--;
        }
    }
    internal string? Undo() => CanUndo ? entries[--cursor] : null;
    internal string? Redo() => CanRedo ? entries[++cursor] : null;
}
