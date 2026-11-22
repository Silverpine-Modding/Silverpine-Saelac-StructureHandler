#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace StructureHandler;

internal static class SavedComponentBinding
{
    private static readonly ConcurrentDictionary<Type,(string Full, string Short, string? Assembly)> Names = new();
    internal static bool IsNative(string? stored, string type) =>
        Matches(type, type, "Assembly-CSharp", (stored ?? "").Trim());

    private static bool Matches(string full, string shortName, string? owner, string name)
    {
        if (name == full || name == shortName) return true; // Legacy unqualified key.
        if (!name.StartsWith(full + ",", StringComparison.Ordinal)) return false;
        // Versions can change between releases. The explicitly named assembly cannot.
        var assembly = name.Substring(full.Length + 1).Trim();
        int end = assembly.IndexOf(',');
        if (end >= 0) assembly = assembly.Substring(0, end).TrimEnd();
        return string.Equals(owner, assembly, StringComparison.OrdinalIgnoreCase);
    }

    internal static T Select<T>(IEnumerable<T> candidates, StructureComponent record, string prefab)
        where T : class
    {
        if (record.componentIndex < -1 || string.IsNullOrWhiteSpace(record.type))
            throw new InvalidDataException($"Invalid component name or index on {prefab}.");
        string name = record.type.Trim();
        int index = Math.Max(0, record.componentIndex), count = 0;
        Type? matched = null;
        T? selected = null;
        foreach (var component in candidates)
        {
            var type = component.GetType();
            var id = Names.GetOrAdd(type, static t => (t.FullName ?? t.Name, t.Name, t.Assembly.GetName().Name));
            if (!Matches(id.Full, id.Short, id.Assembly, name)) continue;
            if (matched != null && matched != type)
                throw new InvalidDataException($"Ambiguous component '{name}' on {prefab}. Specify the full type and assembly.");
            matched = type;
            if (count++ == index) selected = component;
        }
        return selected ?? throw new InvalidDataException($"Missing saved component '{name}' [{index}] at '{record.hierarchyPath}' on {prefab}.");
    }

    // Older files can use two spellings of the same component. Keep the
    // documented last-record-wins order without doing an inventory load or
    // other stateful Deserialize twice on the same live instance.
    internal static List<(T Target, StructureComponent Record)> FinalWrites<T>(
        IEnumerable<StructureComponent> records, Func<StructureComponent,T> bind) where T : class
    {
        var entries = new List<(T Target, StructureComponent Record)>();
        var last = new Dictionary<T,int>();
        foreach (var record in records)
        {
            var target = bind(record);
            last[target] = entries.Count;
            entries.Add((target, record));
        }
        if (last.Count != entries.Count)
        {
            int index = 0;
            entries.RemoveAll(e => last[e.Target] != index++);
        }
        return entries;
    }
}
