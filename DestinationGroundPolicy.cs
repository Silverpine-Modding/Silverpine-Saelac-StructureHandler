#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace StructureHandler;

internal static class DestinationGroundPolicy
{
    internal static string? Choose(IEnumerable<(string Prefab, bool Constructed, string? Previous, float Z)> candidates)
    {
        var ordered = candidates.OrderBy(c => c.Z).ThenBy(c => c.Prefab, StringComparer.OrdinalIgnoreCase).ToArray();
        // Native construction inherits the old floor's underlying ground, not
        // the old floor itself. Reimports must not create a floor resurrection chain.
        foreach (var item in ordered)
            if (item.Constructed && !string.IsNullOrWhiteSpace(item.Previous)) return item.Previous;
        foreach (var item in ordered)
            if (!item.Constructed && !string.IsNullOrWhiteSpace(item.Prefab)) return item.Prefab;
        return null;
    }

    internal static void StripExportedHistory(StructureFile file)
    {
        foreach (var item in file.objects) Strip(item.components);
        foreach (var item in file.supportingTerrain) Strip(item.components);
    }

    private static void Strip(List<StructureComponent>? components) => components?.RemoveAll(item =>
        string.Equals(item.type?.Split(',')[0].Trim(), "GrassTileHandler", StringComparison.Ordinal));
}
