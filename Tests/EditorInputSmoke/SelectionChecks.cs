using UnityEngine;

namespace StructureHandler;

internal sealed partial class StructureEditorUI
{
    // Prepared draw commands stand in for Unity asset loading; hit testing,
    // fallback decisions, deduplication and highlight drawing are production code.
    private sealed class PreparedSprite { internal PreviewSelectionShape selection; }
    private sealed class PreviewDrawCommand
    {
        internal PreparedSprite visual = new();
        internal float x, y;
        internal int objectIndex = -1, terrainIndex = -1;
    }
    private readonly PreviewSpatialIndex drawIndex = new();
    private readonly List<PreviewDrawCommand> previewDrawCommands = new();
    private readonly Dictionary<Vector2Int, List<Tuple<bool, int>>> cellIndex = new();
    private readonly List<Rect> drawnMarkers = new();
    private float zoom = 20;
    private void EnsureRenderOrder() { }
    private void EnsureEditorGeometry() { }
    private Vector2 WorldToPreview(Vector2 center, float x, float y) => new(center.x + x * zoom, center.y - y * zoom);
    private void DrawPreviewTexture(Rect rect, Texture2D texture) => drawnMarkers.Add(rect);

    internal void CheckSelections(Action<bool, string> check)
    {
        void Reset() { ClearPreviewSelection(); drawIndex.Clear(); previewDrawCommands.Clear(); cellIndex.Clear(); }
        void Add(int index, bool terrain, PreviewSelectionShape shape, float x = 0, float y = 0)
        {
            var command = new PreviewDrawCommand { x = x, y = y, visual = new() { selection = shape },
                objectIndex = terrain ? -1 : index, terrainIndex = terrain ? index : -1 };
            drawIndex.Add(previewDrawCommands.Count, shape.At(x, y)); previewDrawCommands.Add(command);
            if (terrain) terrainWithVisuals.Add(index);
            else objectSelectionBounds[index] = objectSelectionBounds.TryGetValue(index, out var previous)
                ? PreviewSelectionShape.Union(previous, shape.At(x, y)) : shape.At(x, y);
        }
        var oneCell = new PreviewSelectionShape(0, 0, 1, 1, 0);
        Reset();
        Add(0, true, oneCell); // Floor.
        Add(1, true, oneCell); // Bath water over floor.
        Add(0, false, oneCell); Add(0, false, oneCell); // Multipart object, not two choices.
        var hits = FindPreviewSelections(new Vector2(0, 0), new Vector2Int(0, 0));
        check(hits.SequenceEqual(new[] { Tuple.Create(false, 0), Tuple.Create(true, 1), Tuple.Create(true, 0) }),
            "Visual selection cycle starts at topmost art, deduplicates multipart objects and retains water/floor choices");
        Reset();
        Add(0, false, new PreviewSelectionShape(0, 0, 2, 1, 0), .25f, -.125f);
        check(FindPreviewSelections(new Vector2(1.2f, -.125f), new Vector2Int(1, 0)).Count == 1,
            "Rug overhang is clickable outside its old anchor-cell footprint");
        cellIndex[new Vector2Int(-1, -1)] = new() { Tuple.Create(false, 0), Tuple.Create(false, 1), Tuple.Create(true, 2) };
        hits = FindPreviewSelections(new Vector2(-1, -1), new Vector2Int(-1, -1));
        check(hits.SequenceEqual(new[] { Tuple.Create(false, 1), Tuple.Create(true, 2) }),
            "Visible art rejects oversized footprint hits while missing objects and terrain remain selectable");
        Add(1, false, new PreviewSelectionShape(0, 0, 4, 4, 0));
        hits = FindPreviewSelections(new Vector2(-1, -1), new Vector2Int(-1, -1));
        check(hits.Count == 2 && hits[0].Equals(Tuple.Create(false, 1)),
            "A resolved item preview replaces its fallback hit without duplicating the selection");
        Reset();
        Add(0, false, new PreviewSelectionShape(0, 0, 3, 1, (float)Math.PI / 4));
        check(FindPreviewSelections(new Vector2(-1.3f, 1.3f), new Vector2Int(-1, 1)).Count == 0,
            "Picking rejects empty rotated-quad corners after broad-phase spatial lookup");
        Reset();
        var bounds = new PreviewSelectionShape(0, 0, 1.8f, 1.8f, 0).At(.25f, -.125f);
        drawnMarkers.Clear();
        DrawObjectSelection(new Rect(-100, -100, 200, 200), new Vector2(0, 0), bounds);
        check(drawnMarkers.Count == 5 && Math.Abs(drawnMarkers[0].x + 13) < .001f &&
            Math.Abs(drawnMarkers[0].width - 36) < .001f && Math.Abs(drawnMarkers[0].y + 15.5f) < .001f,
            "Orange fill and outline follow fractional scaled sprite bounds instead of snapped tile squares");
        Add(0, false, oneCell); FindPreviewSelections(new Vector2(0, 0), new Vector2Int(0, 0));
        ClearPreviewSelection();
        check(objectSelectionBounds.Count == 0 && terrainWithVisuals.Count == 0 && hitDraws.Count == 0 &&
            selectionCandidates.Count == 0 && seenSelectionRecords.Count == 0,
            "Closing/rebuilding clears all visual selection caches");
    }
}
