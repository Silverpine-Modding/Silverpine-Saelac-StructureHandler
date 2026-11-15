#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace StructureHandler;

internal sealed partial class StructureEditorUI
{
    private readonly Dictionary<string, string> previewProblems = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> previewAssetErrors = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> loggedPreviewProblems = new(StringComparer.Ordinal);
    private readonly PreviewRetryQueue previewRetries = new();
    private float nextPreviewRetry;
    private bool showPreviewProblems;
    private Vector2 previewProblemsScroll;
    private GUIStyle? previewProblemStyle;
    private GUISkin? previewProblemSkin;
    private readonly List<Vector2> missingPreviewMarkers = new();
    private readonly PreviewSpatialIndex missingPreviewIndex = new();
    private readonly List<int> visibleMissingPreviews = new();

    private void ReportPreviewProblem(string name, string reason, bool retry = false)
    {
        if (previewProblems.TryGetValue(name, out string previous) && previous == reason &&
            (!retry || previewAssetErrors.ContainsKey(name))) return;
        previewProblems[name] = reason;
        if (retry)
        {
            previewAssetErrors[name] = reason;
            previewRetries.Fail(name, Time.realtimeSinceStartup);
        }
        if (loggedPreviewProblems.Add(name + "\n" + reason))
            Plugin.Log.LogWarning($"Editor preview for '{name}': {reason} The JSON record is unchanged.");
    }

    private static List<SpriteRenderer> GetPreviewRenderers(GameObject prefab) =>
        PreviewRendererDiscovery.Collect(prefab, renderer => IsPreviewLightingEffect(renderer, prefab.transform));

    private void Update()
    {
        if (!open || previewRetries.Count == 0 || Time.realtimeSinceStartup < nextPreviewRetry) return;
        nextPreviewRetry = Time.realtimeSinceStartup + 1;
        previewRetries.Retain(previewProblems.Keys);
        foreach (string name in previewRetries.TakeDue(Time.realtimeSinceStartup, 4))
        {
            spritePreviews.TryGetValue(name, out var old);
            ForgetPrefabPreview(name);
            List<SpritePreview> recovered = GetSpritePreviews(name);
            if (!previewAssetErrors.ContainsKey(name))
            {
                previewRetries.Remove(name);
                previewProblems.Remove(name);
                InvalidateVisuals();
            }
            else if (!SamePreviewParts(old, recovered)) InvalidateVisuals();
        }
    }

    private static bool SamePreviewParts(List<SpritePreview>? old, List<SpritePreview> current)
    {
        if (old == null) return current.Count == 0;
        if (old.Count != current.Count) return false;
        for (int i = 0; i < old.Count; i++)
        {
            var a = old[i]; var b = current[i];
            if (a.sprite != b.sprite || a.localOrigin != b.localOrigin || a.localAxisX != b.localAxisX ||
                a.localAxisY != b.localAxisY || a.color != b.color || a.flipX != b.flipX || a.flipY != b.flipY ||
                a.sortingLayer != b.sortingLayer || a.sortingOrder != b.sortingOrder || a.renderQueue != b.renderQueue ||
                a.localZ != b.localZ || a.ySorted != b.ySorted || a.sorterY != b.sorterY || a.sorterOffset != b.sorterOffset ||
                a.turnableName != b.turnableName || a.variantTarget != b.variantTarget ||
                !a.turnableSprites.SequenceEqual(b.turnableSprites)) return false;
        }
        return true;
    }

    private void ForgetPrefabPreview(string name)
    {
        if (spritePreviews.TryGetValue(name, out var parts))
        {
            var oldParts = new HashSet<SpritePreview>(parts);
            foreach (var key in preparedSprites.Keys.Where(key => oldParts.Contains(key.Item1)).ToArray())
                preparedSprites.Remove(key);
        }
        spritePreviews.Remove(name);
        spriteVariants.Remove(name);
        sharedVariants.Remove(name);
        previewAssetErrors.Remove(name);
    }

    private void RefreshPreviewAssets()
    {
        spritePreviews.Clear(); spriteVariants.Clear(); preparedSprites.Clear();
        previewAssetErrors.Clear(); previewProblems.Clear(); previewRetries.Clear();
        ClearSharedMetadata();
        // Keep the selected item/draft and brush intact. Only invalidate assets.
        itemModelAvailability.Clear(); itemPreviewCache.Clear(); itemPreviewErrors.Clear();
        itemVisuals.Clear(); itemSpriteLayout = null;
        itemPreviewQueue.Clear(); itemPreviewFrame = -1;
        foreach (var sprite in smallItemSprites.Values)
        {
            if (sprite == null) continue;
            Destroy(sprite.texture); Destroy(sprite);
        }
        smallItemSprites.Clear();
        previewSurfaceFailed = false;
        BuildPlacementCatalog();
        InvalidateRenderOrder();
        status = "Refreshed prefab catalog and preview assets. No structure data was changed.";
    }

    private void ClearPreviewDiagnostics()
    {
        previewProblems.Clear(); previewAssetErrors.Clear(); loggedPreviewProblems.Clear();
        previewRetries.Clear(); nextPreviewRetry = 0;
        previewProblemStyle = null; previewProblemSkin = null;
        missingPreviewMarkers.Clear(); missingPreviewIndex.Clear(); visibleMissingPreviews.Clear();
    }

    private void DrawPreviewDiagnostics()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(previewProblems.Count == 0 ? "" :
            $"Preview issues: {previewProblems.Count} (magenta outlines for missing art)");
        if (previewProblems.Count > 0)
            showPreviewProblems = GUILayout.Toggle(showPreviewProblems, "Details", GUI.skin.button, GUILayout.Width(65));
        if (GUILayout.Button("Refresh Previews", GUILayout.Width(135))) RefreshPreviewAssets();
        GUILayout.EndHorizontal();
        if (!showPreviewProblems || previewProblems.Count == 0) return;
        if (previewProblemStyle == null || previewProblemSkin != GUI.skin)
        {
            previewProblemSkin = GUI.skin;
            previewProblemStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
        }
        previewProblemsScroll = GUILayout.BeginScrollView(previewProblemsScroll, GUILayout.Height(110));
        foreach (var issue in previewProblems)
            GUILayout.Label(issue.Key + ": " + issue.Value, previewProblemStyle, GUILayout.MinWidth(0));
        GUILayout.EndScrollView();
    }

    private void AddMissingPreviewMarker(float x, float y)
    {
        missingPreviewIndex.Add(missingPreviewMarkers.Count, new PreviewBounds(x - .5f, y - .5f, x + .5f, y + .5f));
        missingPreviewMarkers.Add(new Vector2(x, y));
    }

    private void DrawMissingPreviewMarkers(Rect preview, Vector2 center)
    {
        missingPreviewIndex.Query(visiblePreview, visibleMissingPreviews);
        Color old = GUI.color;
        try
        {
            GUI.color = new Color(1f, .15f, .7f, .9f);
            foreach (int index in visibleMissingPreviews)
            {
                var point = missingPreviewMarkers[index];
                Vector2 p = WorldToPreview(center, point.x, point.y);
                float left = p.x - zoom * .5f, top = p.y - zoom * .5f;
                DrawPreviewTexture(new Rect(left, top, zoom, 2), Texture2D.whiteTexture);
                DrawPreviewTexture(new Rect(left, top + zoom - 2, zoom, 2), Texture2D.whiteTexture);
                DrawPreviewTexture(new Rect(left, top, 2, zoom), Texture2D.whiteTexture);
                DrawPreviewTexture(new Rect(left + zoom - 2, top, 2, zoom), Texture2D.whiteTexture);
            }
        }
        finally { GUI.color = old; }
    }
}
