using System;
using System.Collections.Generic;
using System.Linq;

namespace StructureHandler;

// Unity-independent rules shared by the editor, importer, and regression tests.
internal static class StructureAlgorithms
{
    internal static bool TryOrder(int count, IEnumerable<(int Winner, int Loser)> choices,
        out int[] order)
    {
        var outgoing = Enumerable.Range(0, count).Select(_ => new HashSet<int>()).ToArray();
        var incoming = new int[count];
        foreach (var choice in choices)
        {
            if (choice.Winner < 0 || choice.Winner >= count ||
                choice.Loser < 0 || choice.Loser >= count)
                throw new ArgumentOutOfRangeException(nameof(choices));
            if (outgoing[choice.Loser].Add(choice.Winner)) incoming[choice.Winner]++;
        }
        var available = new SortedSet<int>(Enumerable.Range(0, count).Where(i => incoming[i] == 0));
        var result = new List<int>(count);
        while (available.Count > 0)
        {
            int next = available.Min;
            available.Remove(next);
            result.Add(next);
            foreach (int winner in outgoing[next])
                if (--incoming[winner] == 0) available.Add(winner);
        }
        order = result.ToArray();
        return order.Length == count;
    }

    internal static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    internal static bool IsGridTranslation(float x, float y) =>
        IsFinite(x) && IsFinite(y) && Math.Abs(x - Math.Round(x)) < 0.0001 &&
        Math.Abs(y - Math.Round(y)) < 0.0001;

    internal static (int First, int Last) VisibleRows(float scroll, float height, float rowHeight, int count)
    {
        int first = Math.Max(0, Math.Min(count, (int)Math.Floor(scroll / rowHeight)));
        int last = Math.Max(first, Math.Min(count, (int)Math.Ceiling((scroll + height) / rowHeight) + 1));
        return (first, last);
    }
}
