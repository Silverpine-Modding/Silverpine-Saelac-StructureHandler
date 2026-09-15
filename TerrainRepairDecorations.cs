#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace StructureHandler;

internal static class TerrainRepairDecorations
{
    private static readonly FieldInfo? Prefab = AccessTools.Field(typeof(OvergrowthTile), "overgrowthPrefab");
    private static readonly FieldInfo? Instances = AccessTools.Field(typeof(OvergrowthTile), "instantiatedPrefabs");
    private const string GrassEdging = "prefab_tile_grass_overgrowth";
    private static readonly HashSet<Type> Components = new()
    {
        typeof(Transform), typeof(SpriteRenderer), typeof(SeasonSprite)
    };

    internal static bool IsNativeOvergrowth(GameObject item)
    {
        // Do not trust the name alone: an editor-placed prefab or a similarly
        // named mod object is not disposable decoration owned by this grass.
        if (SerializationManager.GetPrefabName(item) != GrassEdging ||
            item.transform.parent == null || item.transform.childCount != 0 ||
            item.transform.localScale != Vector3.one || item.hideFlags != HideFlags.None) return false;
        GameObject parent = item.transform.parent.gameObject;
        if (SerializationManager.GetPrefabName(parent) != TerrainRepairPolicy.Grass) return false;
        var owner = parent.GetComponent<OvergrowthTile>();
        if (owner == null || !owner.enabled || Prefab?.GetValue(owner) is not GameObject prefab ||
            SerializationManager.GetPrefabName(prefab) != GrassEdging ||
            Instances?.GetValue(owner) is not List<GameObject> owned || !owned.Contains(item)) return false;
        var components = item.GetComponents<Component>();
        if (components.Length != Components.Count || components.Any(c => c == null) ||
            !Components.SetEquals(components.Select(c => c.GetType())) ||
            components.OfType<Behaviour>().Any(c => !c.enabled)) return false;
        Vector3 p = item.transform.position;
        Vector3 root = parent.transform.position;
        // Reparenting under RandomRotation can introduce tiny float errors.
        const float epsilon = 0.001f;
        return Mathf.Abs(p.z - 1f) < epsilon && Math.Abs(p.x - Math.Round(p.x)) < epsilon &&
               Math.Abs(p.y - Math.Round(p.y)) < epsilon &&
               Mathf.Abs(Mathf.Abs(p.x - root.x) + Mathf.Abs(p.y - root.y) - 1f) < epsilon;
    }
}
