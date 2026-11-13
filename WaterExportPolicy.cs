using System;

namespace StructureHandler;

internal static class WaterExportPolicy
{
    // Applied only while collecting terrain for a new world export. Do not use
    // this for imports, editor saves, queued plans or persistence snapshots.
    internal static bool ShouldIncludeTerrain(string prefabName, bool includeOtherWater) =>
        includeOtherWater ||
        !prefabName.Contains("water", StringComparison.OrdinalIgnoreCase) ||
        prefabName.Contains("water_bathhouse", StringComparison.OrdinalIgnoreCase);
}
