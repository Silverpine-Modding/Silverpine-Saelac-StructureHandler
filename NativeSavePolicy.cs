#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace StructureHandler;

internal static class NativeSavePolicy
{
    // A disabled object can be intentional content. Only unused pool entries
    // are disposable; never filter arbitrary inactive scene objects by name.
    internal static void RemoveReleased<T>(ISet<T> candidates,
        IEnumerable<IEnumerable<T>> freePools, Func<T, bool> isActive)
    {
        foreach (var pool in freePools)
            foreach (var item in pool)
                if (!isActive(item))
                    candidates.Remove(item);
    }

    internal static bool IsCapturedSeasonalVariant<T>(int randomIndex, T selected,
        bool explicitlyLocked, IEnumerable<IReadOnlyList<T>> seasons) =>
        !explicitlyLocked && randomIndex >= 0 && seasons.Any(sprites =>
            sprites.Count > 0 && EqualityComparer<T>.Default.Equals(
                sprites[randomIndex % sprites.Count], selected));

    internal static bool NeedsExtenderOverride(bool requested, bool prefabProvides) =>
        requested && !prefabProvides;
}
