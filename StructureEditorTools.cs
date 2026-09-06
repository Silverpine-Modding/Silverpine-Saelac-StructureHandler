#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace StructureHandler;

internal sealed partial class StructureEditorUI
{
    private readonly SnapshotHistory history = new();
    private string savedSnapshot = "";
    private bool historyDirty;
    private bool brushStroke;
    private bool discardApproved;
    private Action? pendingDiscardAction;
    private string? pendingOverwritePath;
    private Vector2 detailsScroll;
    internal int editRevision;
    private bool geometryDirty = true;
    private Vector2 cachedTopLeft;
    private readonly Dictionary<StructureObject, HashSet<Vector2Int>> cachedCells = new();
    private readonly Dictionary<Vector2Int, List<Tuple<bool, int>>> cellIndex = new();
    private readonly Dictionary<string, (string Label, int Category)> catalogMetadata =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> filteredCatalog = new();
    private string lastCatalogSearch = "";
    private int lastCatalogTab = -1;
    private static readonly Dictionary<string, Sprite[]> sharedVariants = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<(string, int), List<StructurePosition>> sharedFootprints = new();

    internal static void ClearSharedMetadata()
    {
        sharedVariants.Clear();
        sharedFootprints.Clear();
    }
    private string CaptureEditorSnapshot() =>
        structure == null ? "" : StringSerializationAPI.Serialize(typeof(StructureFile), structure);

    private void ResetHistory()
    {
        string snapshot = CaptureEditorSnapshot();
        history.Reset(snapshot);
        savedSnapshot = dirty ? "" : snapshot;
        historyDirty = false;
        brushStroke = false;
    }
    private void FlushHistory()
    {
        if (!historyDirty || brushStroke || structure == null) return;
        history.Record(CaptureEditorSnapshot());
        historyDirty = false;
    }
    private void Undo()
    {
        brushStroke = false;
        FlushHistory();
        RestoreEditorSnapshot(history.Undo());
    }
    private void Redo()
    {
        FlushHistory();
        RestoreEditorSnapshot(history.Redo());
    }
    private void RestoreEditorSnapshot(string? snapshot)
    {
        if (snapshot == null) return;
        structure = (StructureFile)StringSerializationAPI.Deserialize(typeof(StructureFile), snapshot);
        structure.planningTiles = PlanningTiles.Normalize(structure.planningTiles);
        lastPlanningCell = null;
        selectedObject = selectedTerrain = -1;
        movementReferenceSelection = int.MinValue;
        dirty = snapshot != savedSnapshot;
        historyDirty = false;
        editRevision++;
        InvalidateRenderOrder();
    }
    private bool AllowDiscard(Action action)
    {
        if (!dirty || discardApproved) return true;
        pendingDiscardAction = action;
        return false;
    }
    private void DrawDiscardPrompt()
    {
        if (pendingDiscardAction == null) return;
        GUI.enabled = true;
        GUI.Box(new Rect(570, 360, 780, 180), "");
        GUILayout.BeginArea(new Rect(595, 380, 730, 140));
        GUILayout.Label("This structure has unsaved changes.");
        GUILayout.Label("Save them, discard them, or return to editing.");
        GUILayout.Space(18);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Save and continue"))
        {
            if (string.IsNullOrWhiteSpace(currentPath)) SaveAs(); else Save(currentPath);
            if (!dirty) RunPendingDiscard();
        }
        if (GUILayout.Button("Discard changes")) RunPendingDiscard();
        if (GUILayout.Button("Keep editing")) pendingDiscardAction = null;
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }
    private void RunPendingDiscard()
    {
        var action = pendingDiscardAction;
        pendingDiscardAction = null;
        discardApproved = true;
        try { action?.Invoke(); }
        finally { discardApproved = false; }
    }
    private void DrawOverwritePrompt()
    {
        GUI.enabled = true;
        GUI.Box(new Rect(570, 360, 780, 180), "");
        GUILayout.BeginArea(new Rect(595, 380, 730, 140));
        GUILayout.Label("Replace " + System.IO.Path.GetFileName(pendingOverwritePath) + "?");
        GUILayout.Label("The previous file will be kept as a .json.bak backup.");
        GUILayout.Space(18);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Overwrite"))
        {
            string path = pendingOverwritePath!;
            pendingOverwritePath = null;
            Save(path);
            RefreshFiles();
            if (!dirty && pendingDiscardAction != null) RunPendingDiscard();
        }
        if (GUILayout.Button("Cancel")) pendingOverwritePath = null;
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }
    private void DrawVirtualList(ref Vector2 scroll, int count, float height,
        Func<int, string> label, Func<int, bool> selected, Action<int> click)
    {
        Rect viewport = GUILayoutUtility.GetRect(1f, height, GUILayout.ExpandWidth(true));
        const float rowHeight = 28f;
        Rect content = new(0, 0, Mathf.Max(1, viewport.width - 20), count * rowHeight);
        scroll = GUI.BeginScrollView(viewport, scroll, content, false, true);
        var rows = StructureAlgorithms.VisibleRows(scroll.y, height, rowHeight, count);
        for (int i = rows.First; i < rows.Last; i++)
        {
            Rect row = new(0, i * rowHeight, content.width, rowHeight - 2);
            if (GUI.Toggle(row, selected(i), label(i), GUI.skin.button) && !selected(i))
                click(i);
        }
        GUI.EndScrollView(true);
    }
    private HashSet<Vector2Int> CachedCells(StructureObject item)
    {
        if (!cachedCells.TryGetValue(item, out var cells))
            cachedCells[item] = cells = GetObjectCells(item);
        return cells;
    }
    private void EnsureEditorGeometry()
    {
        if (!geometryDirty) return;
        geometryDirty = false;
        cellIndex.Clear();
        cachedCells.Clear();
        planningIndex.Clear();
        planningColors.Clear();
        transitionOverlapCount = 0;
        cachedTopLeft = Vector2.zero;
        if (structure == null) return;
        bool first = true;
        void Point(float x, float y)
        {
            if (first) { cachedTopLeft = new Vector2(x, y); first = false; }
            else { cachedTopLeft.x = Mathf.Min(cachedTopLeft.x, x); cachedTopLeft.y = Mathf.Max(cachedTopLeft.y, y); }
        }
        void Add(Vector2Int cell, bool terrain, int index)
        {
            if (!cellIndex.TryGetValue(cell, out var entries))
                cellIndex[cell] = entries = new();
            entries.Add(Tuple.Create(terrain, index));
        }
        for (int i = 0; i < structure.objects.Count; i++)
        {
            var item = structure.objects[i];
            Point(item.x, item.y);
            foreach (var cell in CachedCells(item)) Add(cell, false, i);
        }
        for (int i = 0; i < structure.supportingTerrain.Count; i++)
        {
            var item = structure.supportingTerrain[i];
            Point(item.x, item.y);
            Add(new Vector2Int(item.x, item.y), true, i);
        }
        transitionOverlapCount = cellIndex.Keys.Count(cell => PlanningTiles.IsTransitionCell(cell.x, cell.y));
        structure.planningTiles ??= new List<PlanningTile>();
        bool planningOnly = first;
        foreach (var mark in structure.planningTiles)
        {
            var key = new Vector2Int(mark.x, mark.y);
            planningIndex[key] = mark;
            if (ColorUtility.TryParseHtmlString(mark.color, out Color color)) planningColors[key] = color;
            // Annotations do not affect import occupancy or existing references.
            // A planning-only document still needs a center and movement anchor.
            if (planningOnly) Point(mark.x, mark.y);
        }
    }
    private void RefreshCatalogFilter()
    {
        if (lastCatalogTab == quickPlaceTab && lastCatalogSearch == catalogSearch) return;
        lastCatalogTab = quickPlaceTab;
        lastCatalogSearch = catalogSearch;
        filteredCatalog.Clear();
        filteredCatalog.AddRange(placementCatalog.Where(name =>
            catalogMetadata[name].Category == quickPlaceTab &&
            (string.IsNullOrWhiteSpace(catalogSearch) ||
             name.Contains(catalogSearch, StringComparison.OrdinalIgnoreCase) ||
             catalogMetadata[name].Label.Contains(catalogSearch, StringComparison.OrdinalIgnoreCase))));
    }
}
