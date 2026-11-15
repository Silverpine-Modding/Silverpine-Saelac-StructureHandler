#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace StructureHandler;

// Retry negative asset results, not every record or IMGUI event. Keep permanent
// failures inexpensive, and never retain Unity objects in this scheduler.
internal sealed class PreviewRetryQueue
{
    private sealed class Entry { internal double Due; internal int Attempts; }
    private readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    internal int Count => entries.Count;
    internal void Fail(string name, double now)
    {
        if (!entries.ContainsKey(name)) entries[name] = new Entry { Due = now + 5 };
    }
    internal void Remove(string name) => entries.Remove(name);
    internal void Clear() => entries.Clear();
    internal void Retain(IEnumerable<string> names)
    {
        var live = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        foreach (string name in entries.Keys.Where(name => !live.Contains(name)).ToArray()) entries.Remove(name);
    }
    internal string[] TakeDue(double now, int maximum)
    {
        string[] due = entries.Where(pair => pair.Value.Due <= now)
            .OrderBy(pair => pair.Value.Due).Take(Math.Max(0, maximum)).Select(pair => pair.Key).ToArray();
        foreach (string name in due)
        {
            var entry = entries[name];
            entry.Attempts = Math.Min(4, entry.Attempts + 1);
            entry.Due = now + Math.Min(60, 5 * (1 << entry.Attempts));
        }
        return due;
    }
}
