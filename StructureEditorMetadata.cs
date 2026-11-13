#nullable enable
using System;
using System.Collections.Generic;

namespace StructureHandler;

internal sealed partial class StructureEditorUI
{
    private sealed class EditorPrefabMetadata
    {
        internal bool HasSign, HasZone, Furniture, CustomCollider, Passable;
        internal int Rotations, RootColliders;
        internal string SignMessage = "";
    }
    private static readonly Dictionary<string, EditorPrefabMetadata> editorPrefabMetadata = new(StringComparer.OrdinalIgnoreCase);

    private static EditorPrefabMetadata EditorPrefabInfo(string name)
    {
        if (editorPrefabMetadata.TryGetValue(name, out var info)) return info;
        info = new EditorPrefabMetadata();
        var prefab = Plugin.ResolvePrefab(name);
        if (prefab != null)
        {
            info.HasSign = prefab.GetComponentInChildren<Sign>(true) != null || prefab.GetComponentInChildren<SimpleSign>(true) != null;
            if (info.HasSign) info.SignMessage = GetPrefabStringField(name, "message", typeof(Sign), typeof(SimpleSign));
            info.HasZone = prefab.GetComponentInChildren<MapZone>(true) != null;
            info.Rotations = prefab.GetComponentInChildren<Turnable>(true)?.sprites?.Length ?? 0;
            info.Furniture = name.StartsWith("prefab_furniture_", StringComparison.OrdinalIgnoreCase) || prefab.GetComponent<Pickupable>() != null;
            var colliders = prefab.GetComponents<TurfCollider>();
            info.RootColliders = colliders.Length;
            if (colliders.Length == 1)
            {
                info.CustomCollider = colliders[0].GetType() != typeof(TurfCollider);
                info.Passable = colliders[0].passable;
            }
        }
        editorPrefabMetadata[name] = info;
        return info;
    }
}
