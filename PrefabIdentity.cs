#nullable enable
using System;
using System.Collections.Generic;

namespace StructureHandler;

internal static class PrefabIdentity
{
    internal static string FromInstance(string name, Func<string,bool> isAssetName)
    {
        const string clone = "(Clone)";
        string exact = name.EndsWith(clone, StringComparison.Ordinal)
            ? name.Substring(0, name.Length - clone.Length) : name;
        int space = exact.IndexOf(' '), paren = exact.IndexOf('(');
        int cut = space < 0 ? paren : paren < 0 ? space : Math.Min(space, paren);
        // Native names use the first token, but an independently loadable
        // decoration can really have spaces or a numbered suffix in its ID.
        return cut < 0 || isAssetName(exact) ? exact : exact.Substring(0, cut);
    }
}

// Rebuild from the currently loaded root assets. Never resolve an ambiguous
// bare name using Unity's unspecified enumeration order.
internal sealed class PrefabAssetIndex<T> where T : class
{
    private readonly Dictionary<string,T?> roots = new(StringComparer.OrdinalIgnoreCase);
    internal IEnumerable<string> Names { get { foreach (var p in roots) if (p.Value != null) yield return p.Key; } }
    internal bool ContainsName(string key) => roots.ContainsKey(key);
    internal T? Get(string key) => roots.TryGetValue(key, out var value) ? value : null;
    internal void Clear() => roots.Clear();
    internal void Add(string name, T asset)
    {
        if (!roots.TryGetValue(name,out var old)) roots.Add(name,asset);
        else if (!ReferenceEquals(old, asset)) roots[name] = null;
    }
}
