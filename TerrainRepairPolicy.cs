#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StructureHandler;

// Unity-independent planning: no removal is authorized by name/coordinate alone.
internal static class TerrainRepairPolicy
{
    internal const string Grass = "prefab_tile_grass";
    internal sealed class Item
    {
        internal int Id;
        internal string Prefab = "";
        internal float X, Y, Z;
        internal bool Safe;
        internal string UnsafeReason = "";
        internal int Order;
    }
    internal sealed class Group
    {
        internal Item Keep = null!;
        internal List<Item> Remove = new();
    }
    internal sealed class Plan
    {
        internal List<Group> Groups = new();
        internal List<SkippedCell> Skipped = new();
        internal string SkipSummary => string.Join("\n", Skipped.SelectMany(p => p.Reasons)
            .GroupBy(reason => reason).OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g => $"{g.Key}: {g.Count()} cell(s)."));
        internal int RemoveCount => Groups.Sum(g => g.Remove.Count);
        internal string Signature => string.Join(";", Groups.Select(g =>
            g.Keep.X.ToString("R", CultureInfo.InvariantCulture) + "," +
            g.Keep.Y.ToString("R", CultureInfo.InvariantCulture) + "," +
            g.Keep.Z.ToString("R", CultureInfo.InvariantCulture) + "@" +
            g.Keep.Id + ":" + string.Join(",", g.Remove.Select(i => i.Id))));
    }
    internal sealed class SkippedCell
    {
        internal float X, Y;
        internal List<string> Reasons = new();
    }
    internal static Plan Build(IEnumerable<Item> terrain)
    {
        var plan = new Plan();
        foreach (var cell in terrain.GroupBy(i => (i.X, i.Y)).OrderBy(g => g.Key.X).ThenBy(g => g.Key.Y))
        {
            var grass = cell.Where(i => i.Prefab == Grass).GroupBy(i => i.Id).Select(g => g.First()).ToList();
            if (grass.Count < 2) continue;
            // Mixed layers, non-grid/modified depth, named zones or unknown
            // component state are ambiguous. Never assume grass under a floor
            // or water is redundant; skip the entire cell.
            var reasons = new List<string>();
            if (cell.Any(i => i.Prefab != Grass)) reasons.Add("Other terrain layers share the cell");
            reasons.AddRange(grass.Where(i => !i.Safe).Select(i => string.IsNullOrEmpty(i.UnsafeReason)
                ? "Modified or unsupported grass state" : i.UnsafeReason));
            if (grass.Any(i =>
                float.IsNaN(i.X) || float.IsInfinity(i.X) || float.IsNaN(i.Y) || float.IsInfinity(i.Y) ||
                i.X != Math.Round(i.X) || i.Y != Math.Round(i.Y) || i.Z != 1f))
                reasons.Add("Nonstandard terrain coordinates or depth");
            if (reasons.Count != 0)
            {
                plan.Skipped.Add(new SkippedCell { X = cell.Key.X, Y = cell.Key.Y,
                    Reasons = reasons.Distinct().OrderBy(reason => reason).ToList() });
                continue;
            }
            var ordered = grass.OrderBy(i => i.Order).ThenBy(i => i.Id).ToList();
            plan.Groups.Add(new Group { Keep = ordered[0], Remove = ordered.Skip(1).ToList() });
        }
        return plan;
    }
}
