#nullable enable
using System;
using UnityEngine;

namespace StructureHandler;

internal sealed partial class StructureEditorUI
{
    private RenderTexture? previewSurface;
    private Material? previewMaterial;
    private Texture? previewBatchTexture;
    private bool capturingPreview, previewBatchOpen, previewSurfaceValid, previewSurfaceFailed;
    private int previewVisualRevision;
    private (int Visual, int Edit, int Zones, int Object, int Terrain, bool Footprints,
        bool ZoneColors, bool Planning, Vector2 Size, Vector2 Center, Vector2 Origin, float Zoom, float Scale) previewSurfaceKey;

    // Cache the composed viewport, not the document. Pan/zoom/edits rebuild it;
    // an idle repaint is one texture draw regardless of the number of records.
    private void DrawCachedPreview(Rect viewport, Vector2 center)
    {
        EnsureRenderOrder(); // Also enqueue new loose-item previews without generating them here.
        ProcessItemPreviewQueue();
        float scale = GUI.matrix.MultiplyVector(Vector3.right).magnitude;
        var key = (previewVisualRevision, editRevision, zoneRevision, selectedObject, selectedTerrain,
            showFootprintOverlay, showZoneColors, showPlanning, viewport.size, center, previewOrigin, zoom, scale);
        if (!previewSurfaceFailed)
        {
            try
            {
                EnsurePreviewSurface(viewport, scale);
                if (!previewSurfaceValid || !previewSurfaceKey.Equals(key))
                {
                    RenderPreviewSurface(viewport, center);
                    previewSurfaceKey = key;
                    previewSurfaceValid = true;
                }
                Color old = GUI.color;
                try
                {
                    GUI.color = Color.white;
                    // The surface has an opaque background; don't blend its alpha a second time.
                    GUI.DrawTexture(viewport, previewSurface!, ScaleMode.StretchToFill, false);
                }
                finally { GUI.color = old; }
                return;
            }
            catch (Exception exception)
            {
                ReleasePreviewSurface();
                previewSurfaceFailed = true;
                Plugin.Log.LogWarning("Preview image caching unavailable; using direct drawing. " + exception.Message);
            }
        }
        DrawPreviewContents(viewport, center);
    }

    private void DrawPreviewContents(Rect viewport, Vector2 center)
    {
        DrawGrid(viewport, center);
        DrawRecords(viewport, center);
        DrawPlanningOverlay(viewport, center);
        DrawWorldTileOutlines(viewport, center);
        DrawTopLeftMarker(center);
    }

    private void EnsurePreviewSurface(Rect viewport, float scale)
    {
        // Match the actual GUI scale on high-DPI displays, with a bounded GPU allocation.
        float limit = Mathf.Min(4096, SystemInfo.maxTextureSize);
        scale = Mathf.Min(Mathf.Max(.1f, scale), limit / Mathf.Max(1, Mathf.Max(viewport.width, viewport.height)));
        int width = Mathf.Max(1, Mathf.CeilToInt(viewport.width * scale));
        int height = Mathf.Max(1, Mathf.CeilToInt(viewport.height * scale));
        if (previewSurface != null && (previewSurface.width != width || previewSurface.height != height))
            ReleasePreviewSurface();
        if (previewMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Unlit sprite shader missing.");
            previewMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }
        if (previewSurface == null)
            previewSurface = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = "Structure Handler editor preview", hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
                useMipMap = false, autoGenerateMips = false, antiAliasing = 1
            };
        if (!previewSurface.IsCreated())
        {
            previewSurfaceValid = false;
            if (!previewSurface.Create()) throw new InvalidOperationException("Could not allocate preview surface.");
        }
    }

    private void RenderPreviewSurface(Rect viewport, Vector2 center)
    {
        // Resolve Unity assets before entering GL.Begin: loading an item/model can itself render.
        EnsureRenderOrder();
        EnsureVisiblePreview(viewport, center);
        RenderTexture oldTarget = RenderTexture.active;
        bool oldSrgb = GL.sRGBWrite;
        Color oldColor = GUI.color;
        GL.PushMatrix();
        try
        {
            RenderTexture.active = previewSurface;
            GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
            GL.Clear(false, true, new Color(.015f, .018f, .02f, 1f));
            GL.LoadPixelMatrix(0, viewport.width, viewport.height, 0);
            capturingPreview = true;
            DrawPreviewContents(viewport, center);
        }
        finally
        {
            EndPreviewBatch();
            capturingPreview = false;
            GUI.color = oldColor;
            GL.PopMatrix();
            RenderTexture.active = oldTarget;
            GL.sRGBWrite = oldSrgb;
        }
    }

    private void DrawPreviewTexture(Rect rect, Texture texture)
    {
        if (capturingPreview) DrawPreviewQuad(rect, texture, new Rect(0, 0, 1, 1), GUI.color, 0);
        else GUI.DrawTexture(rect, texture);
    }

    private void DrawPreviewQuad(Rect rect, Texture texture, Rect uv, Color color, float angle)
    {
        // Batch only consecutive textures; regrouping would break the native sprite sort order.
        if (!previewBatchOpen || previewBatchTexture != texture)
        {
            EndPreviewBatch();
            previewMaterial!.mainTexture = texture;
            if (!previewMaterial.SetPass(0)) throw new InvalidOperationException("Preview shader pass unavailable.");
            GL.Begin(GL.QUADS);
            previewBatchOpen = true;
            previewBatchTexture = texture;
        }
        float cos = Mathf.Cos(angle * Mathf.Deg2Rad), sin = Mathf.Sin(angle * Mathf.Deg2Rad);
        Vector2 center = rect.center;
        float x = rect.width * .5f, y = rect.height * .5f;
        GL.Color(color);
        PreviewVertex(center, -x, -y, cos, sin, uv.x, uv.y + uv.height);
        PreviewVertex(center, x, -y, cos, sin, uv.x + uv.width, uv.y + uv.height);
        PreviewVertex(center, x, y, cos, sin, uv.x + uv.width, uv.y);
        PreviewVertex(center, -x, y, cos, sin, uv.x, uv.y);
    }

    private static void PreviewVertex(Vector2 center, float x, float y, float cos, float sin, float u, float v)
    {
        GL.TexCoord2(u, v);
        GL.Vertex3(center.x + cos * x - sin * y, center.y + sin * x + cos * y, 0);
    }

    private void EndPreviewBatch()
    {
        if (previewBatchOpen) GL.End();
        previewBatchOpen = false;
        previewBatchTexture = null;
    }

    private void ReleasePreviewSurface()
    {
        previewSurfaceValid = false;
        if (previewSurface != null) { previewSurface.Release(); Destroy(previewSurface); }
        if (previewMaterial != null) Destroy(previewMaterial);
        previewSurface = null;
        previewMaterial = null;
    }
}
