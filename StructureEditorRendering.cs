#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace StructureHandler;

internal sealed partial class StructureEditorUI
{
    private sealed class PreparedSprite
    {
        internal Texture2D texture = null!;
        internal Vector2 offset, drawSize;
        internal Rect uv;
        internal float angle;
        internal Color color;
        internal PreviewSelectionShape selection;
    }
    private readonly Dictionary<(SpritePreview, Sprite), PreparedSprite> preparedSprites = new();
    private readonly PreviewSpatialIndex drawIndex = new(), markerIndex = new();
    private readonly List<int> visibleDraws = new(), visibleMarkers = new();
    private PreviewBounds visiblePreview;
    private bool visiblePreviewValid;

    private void ClearRenderCaches()
    {
        ClearEditorTextInput();
        ReleasePreviewSurface();
        previewSurfaceFailed = false;
        coordinateDraftOwner = null;
        coordinateDrafts.Clear();
        previewDrawCommands.Clear(); preparedSprites.Clear();
        ClearPreviewSelection();
        ClearPreviewDiagnostics();
        drawIndex.Clear(); markerIndex.Clear(); visibleDraws.Clear(); visibleMarkers.Clear();
        placementIndex.Clear(); placementCandidates.Clear();
        visiblePlanning.Clear(); planningViewportValid = false;
        measuredSignText = null; measuredSignSkin = null; planningTextStyle = null;
        InvalidateRenderOrder();
    }

    private PreparedSprite PrepareSprite(SpritePreview part, Sprite sprite)
    {
        if (sprite == null) throw new InvalidOperationException("Sprite asset is missing or was unloaded.");
        if (preparedSprites.TryGetValue((part, sprite), out var cached) && cached.texture != null) return cached;
        Bounds bounds = sprite.bounds;
        Texture2D texture = sprite.texture;
        if (texture == null || texture.width <= 0 || texture.height <= 0)
            throw new InvalidOperationException("Sprite texture is missing or empty.");
        Rect source = sprite.textureRect;
        if (source.width <= 0 || source.height <= 0)
            throw new InvalidOperationException("Sprite has an empty texture rectangle.");
        if (!Finite(part.color.r) || !Finite(part.color.g) || !Finite(part.color.b) || !Finite(part.color.a) || part.color.a <= 0)
            throw new InvalidOperationException("Sprite renderer has a fully transparent or invalid tint.");
        Rect uv = new(source.x / texture.width, source.y / texture.height,
            source.width / texture.width, source.height / texture.height);
        bool mirrored = part.localAxisX.x * part.localAxisY.y - part.localAxisX.y * part.localAxisY.x < 0;
        if (part.flipX) { uv.x += uv.width; uv.width = -uv.width; }
        if (part.flipY != mirrored) { uv.y += uv.height; uv.height = -uv.height; }
        var result = new PreparedSprite
        {
            texture = texture, uv = uv, color = part.color,
            offset = part.localOrigin + part.localAxisX * (part.flipX ? -bounds.center.x : bounds.center.x) +
                part.localAxisY * (part.flipY ? -bounds.center.y : bounds.center.y),
            drawSize = new Vector2(part.localAxisX.magnitude * bounds.size.x, part.localAxisY.magnitude * bounds.size.y),
            angle = -Mathf.Atan2(part.localAxisX.y, part.localAxisX.x) * Mathf.Rad2Deg
        };
        if (!Finite(result.offset.x) || !Finite(result.offset.y) || !Finite(result.angle) ||
            !Finite(result.drawSize.x) || !Finite(result.drawSize.y) || result.drawSize.x <= 0 || result.drawSize.y <= 0)
            throw new InvalidOperationException("Sprite has invalid bounds or a zero-scale transform.");
        result.selection = new PreviewSelectionShape(result.offset.x, result.offset.y,
            result.drawSize.x, result.drawSize.y, -result.angle * Mathf.Deg2Rad);
        preparedSprites[(part, sprite)] = result;
        return result;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private void RebuildPreviewIndex()
    {
        drawIndex.Clear(); markerIndex.Clear(); visiblePreviewValid = false;
        missingPreviewMarkers.Clear(); missingPreviewIndex.Clear();
        ClearPreviewSelection();
        int validCount = 0;
        for (int i = 0; i < previewDrawCommands.Count; i++)
        {
            var command = previewDrawCommands[i];
            try { command.visual = PrepareSprite(command.part, command.sprite); }
            catch (Exception exception)
            {
                string name = command.objectIndex >= 0 ? structure!.objects[command.objectIndex].prefabName
                    : command.terrainIndex >= 0 ? structure!.supportingTerrain[command.terrainIndex].prefabName : command.part.prefabName;
                ReportPreviewProblem(name, "Sprite preparation failed: " + exception.Message, retry: true);
                continue;
            }
            previewDrawCommands[validCount] = command;
            var visual = command.visual;
            var bounds = visual.selection.At(command.x, command.y);
            drawIndex.Add(validCount++, bounds);
            if (command.objectIndex >= 0)
                objectSelectionBounds[command.objectIndex] = objectSelectionBounds.TryGetValue(command.objectIndex, out var previous)
                    ? PreviewSelectionShape.Union(previous, bounds) : bounds;
            if (command.terrainIndex >= 0) terrainWithVisuals.Add(command.terrainIndex);
        }
        if (validCount < previewDrawCommands.Count)
            previewDrawCommands.RemoveRange(validCount, previewDrawCommands.Count - validCount);
        for (int i = 0; i < structure!.supportingTerrain.Count; i++)
        {
            var terrain = structure.supportingTerrain[i];
            if (!terrainWithVisuals.Contains(i)) AddMissingPreviewMarker(terrain.x, terrain.y);
        }
        for (int i = 0; i < structure!.objects.Count; i++)
        {
            var item = structure.objects[i];
            var bounds = objectSelectionBounds.TryGetValue(i, out var visualBounds)
                ? visualBounds : new PreviewBounds(item.x - .5f, item.y - .5f, item.x + .5f, item.y + .5f);
            foreach (var cell in CachedCells(item))
                bounds = PreviewSelectionShape.Union(bounds, new PreviewBounds(cell.x - .5f, cell.y - .5f, cell.x + .5f, cell.y + .5f));
            markerIndex.Add(i, bounds);
            if (!objectSelectionBounds.ContainsKey(i) && previewProblems.ContainsKey(item.prefabName))
                AddMissingPreviewMarker(item.x, item.y);
        }
        PruneItemPreviews();
    }

    private void EnsureVisiblePreview(Rect preview, Vector2 center)
    {
        Vector2 origin = GetStructureCenter();
        var bounds = new PreviewBounds(origin.x + (preview.xMin - center.x) / zoom,
            origin.y - (preview.yMax - center.y) / zoom,
            origin.x + (preview.xMax - center.x) / zoom, origin.y - (preview.yMin - center.y) / zoom);
        if (visiblePreviewValid && visiblePreview.Equals(bounds)) return;
        drawIndex.Query(bounds, visibleDraws); markerIndex.Query(bounds, visibleMarkers);
        visiblePreview = bounds; visiblePreviewValid = true;
    }
}
