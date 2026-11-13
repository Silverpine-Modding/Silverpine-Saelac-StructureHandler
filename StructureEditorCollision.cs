#nullable enable
using System;
using UnityEngine;

namespace StructureHandler;

internal sealed partial class StructureEditorUI
{
    private void DrawFurnitureCollision(StructureObject item)
    {
        var info = EditorPrefabInfo(item.prefabName);
        if (!info.Furniture) return;

        // Native saves serialize root components; do not add/remove colliders
        // or pretend a child-only/custom collider will persist the same way.
        if (info.RootColliders != 1)
        {
            GUILayout.Label(info.RootColliders == 0
                ? "Turf collision: unavailable (no native root collider)."
                : "Turf collision: unavailable (multiple root colliders).");
            return;
        }
        if (info.CustomCollider)
        {
            GUILayout.Label("Turf collision: unavailable (custom collider type).");
            return;
        }
        if (!FurnitureCollision.TryReadEnabled(
                item.components, info.Passable, out bool enabled))
        {
            GUILayout.Label("Turf collision: unavailable (unrecognized saved data).");
            return;
        }

        bool next = GUILayout.Toggle(enabled, new GUIContent(
            enabled ? " Turf collision: On (blocks movement)" : " Turf collision: Off (allows passage)",
            "Changes this furniture's native TurfCollider.passable flag on import. " +
            "Other objects or terrain at the same cell may still block movement."));
        if (next == enabled) return;
        FurnitureCollision.SetEnabled(item, next);
        MarkStructureDirty(geometryChanged: false, zonesChanged: false);
        status = "Furniture turf collision " + (next ? "enabled" : "disabled") +
                 ". Save the JSON and import it to apply this change in-game.";
    }
}
