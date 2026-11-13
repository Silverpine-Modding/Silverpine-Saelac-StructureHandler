#nullable enable
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace StructureHandler;

internal sealed partial class StructureEditorUI
{
    private sealed class PreparedSprite
    {
        internal Texture2D texture = null!;
        internal Vector2 offset, size, drawSize;
        internal Rect uv;
        internal float angle;
        internal Color color;
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
        drawIndex.Clear(); markerIndex.Clear(); visibleDraws.Clear(); visibleMarkers.Clear();
        placementIndex.Clear(); placementCandidates.Clear();
        visiblePlanning.Clear(); planningViewportValid = false;
        measuredSignText = null; measuredSignSkin = null; planningTextStyle = null;
        InvalidateRenderOrder();
    }

    private PreparedSprite PrepareSprite(SpritePreview part, Sprite sprite)
    {
        if (preparedSprites.TryGetValue((part, sprite), out var cached)) return cached;
        Bounds bounds = sprite.bounds;
        Texture2D texture = sprite.texture;
        Rect source = sprite.textureRect;
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
            size = new Vector2(Mathf.Abs(part.localAxisX.x * bounds.size.x) + Mathf.Abs(part.localAxisY.x * bounds.size.y),
                Mathf.Abs(part.localAxisX.y * bounds.size.x) + Mathf.Abs(part.localAxisY.y * bounds.size.y)),
            drawSize = new Vector2(part.localAxisX.magnitude * bounds.size.x, part.localAxisY.magnitude * bounds.size.y),
            angle = -Mathf.Atan2(part.localAxisX.y, part.localAxisX.x) * Mathf.Rad2Deg
        };
        preparedSprites[(part, sprite)] = result;
        return result;
    }

    private void RebuildPreviewIndex()
    {
        drawIndex.Clear(); markerIndex.Clear(); visiblePreviewValid = false;
        for (int i = 0; i < previewDrawCommands.Count; i++)
        {
            var command = previewDrawCommands[i];
            command.visual = PrepareSprite(command.part, command.sprite);
            var visual = command.visual;
            float x = command.x + visual.offset.x, y = command.y + visual.offset.y;
            drawIndex.Add(i, new PreviewBounds(x - visual.size.x / 2, y - visual.size.y / 2,
                x + visual.size.x / 2, y + visual.size.y / 2));
        }
        for (int i = 0; i < structure!.objects.Count; i++)
        {
            var item = structure.objects[i];
            Rect bounds = GetPrefabLocalBounds(item.prefabName);
            markerIndex.Add(i, new PreviewBounds(item.x + bounds.xMin, item.y + bounds.yMin,
                item.x + bounds.xMax, item.y + bounds.yMax));
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
