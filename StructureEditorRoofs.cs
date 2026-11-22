#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace StructureHandler;

internal sealed partial class StructureEditorUI
{
    private bool showRoofOverlay;
    private readonly RoofPreview roofMap = new();
    private readonly Dictionary<string, RoofPreview.Prefab?> roofPrefabs = new(StringComparer.OrdinalIgnoreCase);
    private static readonly FieldInfo? derooferActive = typeof(Deroofer).GetField("active", BindingFlags.NonPublic | BindingFlags.Instance);
    private bool roofMapDirty = true;

    private void ClearRoofPreview()
    {
        roofPrefabs.Clear();
        roofMap.Clear();
        roofMapDirty = true;
        previewSurfaceValid = false;
    }

    private RoofPreview.Prefab? GetRoofPrefab(string name)
    {
        if (roofPrefabs.TryGetValue(name, out var cached)) return cached;
        var prefab = Plugin.ResolvePrefab(name);
        if (prefab == null) return roofPrefabs[name] = null;
        var info = new RoofPreview.Prefab { Ground = GetPlacementSlot(name) == PlacementSlot.Ground };
        foreach (var tags in prefab.GetComponentsInChildren<TileTags>(true))
        {
            if (tags.tags == null || !tags.tags.Contains(TileTag.Roofed) ||
                !PreviewRendererDiscovery.IsBranchActive(tags.transform, prefab.transform)) continue;
            Vector3 offset = tags.transform.position - prefab.transform.position;
            // Deroofer.Start removes the tag on its own GameObject only.
            var remover = tags.GetComponent<Deroofer>();
            bool canRemove = remover != null && remover.enabled && tags == tags.GetComponent<TileTags>();
            info.Tags.Add((offset.x, offset.y,
                canRemove ? StructureTransfer.GetHierarchyPath(prefab.transform, tags.transform) : null,
                canRemove && derooferActive?.GetValue(remover) is true));
        }
        return roofPrefabs[name] = info;
    }

    private void EnsureRoofMap()
    {
        if (!showRoofOverlay || !roofMapDirty || structure == null) return;
        roofMap.Clear();
        foreach (var tile in structure.supportingTerrain)
        {
            var info = GetRoofPrefab(tile.prefabName);
            if (info == null) roofMap.Unresolved(tile.x, tile.y);
            else roofMap.Record(tile.x, tile.y, info, tile.components);
        }
        foreach (var item in structure.objects)
        {
            var info = GetRoofPrefab(item.prefabName);
            if (info == null)
            {
                foreach (var cell in CachedCells(item)) roofMap.Unresolved(cell.x, cell.y);
                roofMap.Unresolved(Mathf.RoundToInt(item.x), Mathf.RoundToInt(item.y));
            }
            else roofMap.Record(item.x, item.y, info, item.components);
        }
        roofMapDirty = false;
    }

    private RoofStatus RoofAt(int x, int y) => roofMap.At(x, y, cellIndex.ContainsKey(new Vector2Int(x, y)));

    private void DrawRoofOverlay(Rect viewport, Vector2 center)
    {
        if (!showRoofOverlay) return;
        Vector2Int topLeft = PreviewToWorldCell(center, viewport.min);
        Vector2Int bottomRight = PreviewToWorldCell(center, viewport.max);
        for (int y = bottomRight.y; y <= topLeft.y; y++)
        for (int x = topLeft.x; x <= bottomRight.x; x++)
        {
            var state = RoofAt(x, y);
            if (state == RoofStatus.Empty) continue;
            Color tint = state == RoofStatus.Roofed ? new Color(.71f, .36f, 1f, .55f)
                : state == RoofStatus.Outdoor ? new Color(1f, .76f, .18f, .44f)
                : new Color(.74f, .76f, .78f, .38f);
            DrawTile(viewport, center, x, y, tint);
        }
    }

    private void DrawRoofLegend(Rect preview)
    {
        if (!showRoofOverlay || structure == null) return;
        string label = "Violet: roofed   |   Gold: outdoor   |   Gray: unknown / destination ground";
        if (preview.Contains(Event.current.mousePosition))
        {
            EnsureEditorGeometry();
            EnsureRoofMap();
            var cell = PreviewToWorldCell(preview.center + previewPan, Event.current.mousePosition);
            label += $"   ({cell.x}, {cell.y}): {RoofAt(cell.x, cell.y)}";
        }
        GUILayout.Label(label);
    }
}
