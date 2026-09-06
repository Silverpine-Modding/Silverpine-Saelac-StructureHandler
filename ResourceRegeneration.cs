#nullable enable

using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace StructureHandler;

internal static class ResourceRegeneration
{
    private static readonly Dictionary<string, RegeneratingResourceMarker> Markers =
        new(StringComparer.OrdinalIgnoreCase);
    internal static IEnumerable<RegeneratingResourceMarker> Records => Markers.Values;
    internal static void Reset() => Markers.Clear();
    internal static void Restore(IEnumerable<RegeneratingResourceMarker> markers)
    {
        Markers.Clear();
        foreach (var marker in markers)
            if (marker != null && !string.IsNullOrWhiteSpace(marker.prefabName) &&
                StructureAlgorithms.IsFinite(marker.x) && StructureAlgorithms.IsFinite(marker.y) &&
                StructureAlgorithms.IsFinite(marker.z))
                Markers[Key(marker)] = marker;
    }
    internal static void ReplaceImportedResources(HashSet<Vector2Int> occupied,
        IEnumerable<GameObject> importedObjects)
    {
        foreach (string key in Markers.Where(pair => occupied.Contains(Cell(pair.Value)))
                     .Select(pair => pair.Key).ToArray())
            Markers.Remove(key);
        foreach (GameObject gameObject in importedObjects)
        {
            if (!TryCreateMarker(gameObject, out var marker)) continue;
            marker.lastCheckedDay = WorldInfoManager.Instance.GetCurrentDay();
            Markers[Key(marker)] = marker;
        }
    }
    internal static void RemoveMarkersForWorldTile(Vector2Int tile)
    {
        foreach (string key in Markers.Where(pair => Plugin.GetWorldTile(
                     new Vector2(pair.Value.x, pair.Value.y)) == tile)
                     .Select(pair => pair.Key).ToArray())
            Markers.Remove(key);
    }
    internal static void ProcessLoadedTile()
    {
        if (Markers.Count == 0 || !StructureSaveState.TryCurrentTile(out var tile)) return;
        int day = WorldInfoManager.Instance.GetCurrentDay();
        var due = Markers.Values.Where(marker => marker.lastCheckedDay < day &&
            Plugin.GetWorldTile(new Vector2(marker.x, marker.y)) == tile).ToArray();
        if (due.Length == 0) return;
        var scene = StructureTransfer.GetActiveSerializableObjects();
        var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var occupied = new HashSet<Vector2Int>();
        foreach (GameObject item in scene)
        {
            if (TryCreateMarker(item, out var marker))
            {
                live.Add(Key(marker));
                occupied.Add(Cell(marker));
            }
            else if (!StructureTransfer.IsTerrainObject(item) &&
                    (item.GetComponentInChildren<Deconstructable>(true) != null ||
                     item.GetComponentInChildren<ConstructionSite>(true) != null ||
                     item.GetComponentInChildren<Door>(true) != null ||
                     item.GetComponentInChildren<Window>(true) != null ||
                     item.GetComponentInChildren<Pickupable>(true) != null ||
                     item.GetComponentInChildren<Sign>(true) != null))
            {
                var position = item.transform.GetVector2IntPosition();
                foreach (var offset in StructureEditorUI.GetPrefabOccupiedOffsets(
                             SerializationManager.GetPrefabName(item),
                             item.GetComponentInChildren<Turnable>(true)?.GetIndex() ?? -1))
                    occupied.Add(position + new Vector2Int(offset.x, offset.y));
            }
        }
        foreach (var marker in due)
        {
            if (live.Contains(Key(marker)) || occupied.Contains(Cell(marker)))
            {
                marker.lastCheckedDay = day;
                continue;
            }
            var prefab = Plugin.ResolvePrefab(marker.prefabName);
            if (prefab == null) continue;
            GameObject? spawned = null;
            try
            {
                spawned = ObjectPool.IsObjectPoolTarget(prefab)
                    ? ObjectPool.Claim(prefab, new Vector3(marker.x, marker.y, marker.z))
                    : UnityEngine.Object.Instantiate(prefab,
                        new Vector3(marker.x, marker.y, marker.z), prefab.transform.rotation);
                foreach (var registrar in spawned.GetComponentsInChildren<TurfRegistrar>(true))
                    registrar.Register();
                occupied.Add(Cell(marker));
                marker.lastCheckedDay = day;
            }
            catch (Exception exception)
            {
                if (spawned != null) StructureTransfer.RemoveGameObjects(new[] { spawned });
                Plugin.Log.LogError("Could not restore imported resource: " + exception);
            }
        }
        ObjectPool.CallStarts();
    }
    private static bool TryCreateMarker(GameObject gameObject, out RegeneratingResourceMarker marker)
    {
        marker = null!;
        if (gameObject == null || !gameObject.activeInHierarchy) return false;
        var node = gameObject.GetComponentInChildren<ResourceNode>(true);
        if (node == null || (node.resourceNodeType != ResourceNodeType.Herb &&
                             node.resourceNodeType != ResourceNodeType.Ore)) return false;
        var position = gameObject.transform.position;
        marker = new RegeneratingResourceMarker
        {
            prefabName = SerializationManager.GetPrefabName(gameObject),
            x = position.x, y = position.y, z = position.z
        };
        return !string.IsNullOrWhiteSpace(marker.prefabName);
    }
    private static Vector2Int Cell(RegeneratingResourceMarker marker) =>
        new(Mathf.RoundToInt(marker.x), Mathf.RoundToInt(marker.y));
    private static string Key(RegeneratingResourceMarker marker) =>
        marker.prefabName + "|" + Cell(marker).x + "|" + Cell(marker).y;
}
