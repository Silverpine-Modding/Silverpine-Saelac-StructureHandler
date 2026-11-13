#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace StructureHandler;

// TurfCollider's native saved payload is one Boolean: passable (the inverse
// of collision enabled). Keep it in the existing component record so native
// saves and pickup/replacement preserve it without companion state or patches.
internal static class FurnitureCollision
{
    private static bool IsRootCollider(StructureComponent component) =>
        string.IsNullOrWhiteSpace(component.hierarchyPath) &&
        (component.type ?? "").Split(',')[0].Trim() == "TurfCollider";

    internal static bool TryReadEnabled(
        IEnumerable<StructureComponent>? components, bool prefabPassable,
        out bool enabled)
    {
        enabled = !prefabPassable;
        foreach (var component in components ?? Array.Empty<StructureComponent>())
        {
            if (!IsRootCollider(component)) continue;
            // The UI supports one native root collider. Do not guess at a
            // changed/modded component layout or overwrite an unknown payload.
            if (component.componentIndex > 0) return false;
            byte[] payload;
            try { payload = Convert.FromBase64String(component.dataBase64 ?? ""); }
            catch (FormatException) { return false; }
            if (payload.Length != 1) return false;
            enabled = payload[0] == 0;
        }
        return true;
    }

    internal static void SetEnabled(StructureObject item, bool enabled)
    {
        if (!TryReadEnabled(item.components, false, out _))
            throw new InvalidDataException("Unrecognized saved TurfCollider data; no changes were made.");

        string payload = enabled ? "AA==" : "AQ==";
        item.components ??= new List<StructureComponent>();
        bool found = false;
        foreach (var component in item.components)
        {
            if (!IsRootCollider(component)) continue;
            // Multiple legacy aliases may target the same component; keep
            // their identities/order and update them all so the last wins too.
            component.dataBase64 = payload;
            found = true;
        }
        if (!found)
            item.components.Add(new StructureComponent
            {
                type = "TurfCollider, Assembly-CSharp",
                hierarchyPath = "",
                componentIndex = 0,
                dataBase64 = payload
            });
    }
}
