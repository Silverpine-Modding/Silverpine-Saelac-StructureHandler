#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace StructureHandler;

internal sealed partial class StructureEditorUI
{
    private readonly Dictionary<int, PreviewBounds> objectSelectionBounds = new();
    private readonly HashSet<int> terrainWithVisuals = new();
    private readonly List<int> hitDraws = new();
    private readonly List<Tuple<bool, int>> selectionCandidates = new();
    private readonly HashSet<(bool, int)> seenSelectionRecords = new();

    private List<Tuple<bool, int>> FindPreviewSelections(Vector2 point, Vector2Int cell)
    {
        EnsureRenderOrder();
        EnsureEditorGeometry();
        selectionCandidates.Clear(); seenSelectionRecords.Clear();
        drawIndex.Query(new PreviewBounds(point.x, point.y, point.x, point.y), hitDraws);
        // Start with the visually topmost part. A multipart object appears only
        // once in the cycle; underlying floors/water remain selectable too.
        for (int i = hitDraws.Count - 1; i >= 0; i--)
        {
            var command = previewDrawCommands[hitDraws[i]];
            if (!command.visual.selection.Contains(point.x - command.x, point.y - command.y)) continue;
            bool terrain = command.terrainIndex >= 0;
            int index = terrain ? command.terrainIndex : command.objectIndex;
            if (index >= 0 && seenSelectionRecords.Add((terrain, index)))
                selectionCandidates.Add(Tuple.Create(terrain, index));
        }
        // Missing/utility prefabs and deferred item previews still need a way
        // to select them. Do not add oversized footprint hits for visible art.
        if (cellIndex.TryGetValue(cell, out var records))
            foreach (var record in records)
                if (!(record.Item1 ? terrainWithVisuals.Contains(record.Item2) : objectSelectionBounds.ContainsKey(record.Item2)) &&
                    seenSelectionRecords.Add((record.Item1, record.Item2)))
                    selectionCandidates.Add(record);
        return selectionCandidates;
    }

    private void DrawObjectSelection(Rect preview, Vector2 center, PreviewBounds bounds)
    {
        Vector2 topLeft = WorldToPreview(center, bounds.Left, bounds.Top);
        Rect rect = new(topLeft.x, topLeft.y,
            (bounds.Right - bounds.Left) * zoom, (bounds.Top - bounds.Bottom) * zoom);
        if (!preview.Overlaps(rect)) return;
        Color old = GUI.color;
        GUI.color = new Color(1f, 0.75f, 0.1f, 0.24f);
        DrawPreviewTexture(rect, Texture2D.whiteTexture);
        GUI.color = new Color(1f, 0.75f, 0.1f, 0.95f);
        float edge = Mathf.Min(1.5f, rect.width / 2f, rect.height / 2f);
        DrawPreviewTexture(new Rect(rect.x, rect.y, rect.width, edge), Texture2D.whiteTexture);
        DrawPreviewTexture(new Rect(rect.x, rect.yMax - edge, rect.width, edge), Texture2D.whiteTexture);
        DrawPreviewTexture(new Rect(rect.x, rect.y, edge, rect.height), Texture2D.whiteTexture);
        DrawPreviewTexture(new Rect(rect.xMax - edge, rect.y, edge, rect.height), Texture2D.whiteTexture);
        GUI.color = old;
    }

    private void ClearPreviewSelection()
    {
        objectSelectionBounds.Clear(); terrainWithVisuals.Clear(); hitDraws.Clear();
        selectionCandidates.Clear(); seenSelectionRecords.Clear();
    }
}
