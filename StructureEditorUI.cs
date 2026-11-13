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

internal sealed partial class StructureEditorUI :
    Silverpine.ModdingTools.ModToolBehaviour
{
    private const float DesignWidth = 1920f;
    private const float DesignHeight = 1080f;
    private static StructureEditorUI? instance;
    private readonly List<string> files = new();
    private readonly List<string> fileLabels = new();
    private static readonly string[] QuickPlaceCategories = { "Terrain", "Structural", "Furniture", "Nature", "Utility" };
    private string? measuredSignText;
    private GUISkin? measuredSignSkin;
    private float measuredSignHeight;
    private readonly HashSet<string> checkedFiles =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<SpritePreview>> spritePreviews =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Sprite[]> spriteVariants =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Rect> spriteLocalBounds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> placementCatalog = new();
    private StructureFile? structure;
    private string currentPath = "";
    private string saveAsName = "";
    private string addPrefabName = "";
    private string brushPrefabName = "";
    private string catalogSearch = "";
    private string addX = "0";
    private string addY = "0";
    private string addZ = "0";
    private string offsetX = "0";
    private string offsetY = "0";
    private string nudgeStep = "0.25";
    private string terrainBrushZone = "";
    private bool addNpcInteractionRangeExtender;
    private bool deleteBrush;
    private bool showFootprintOverlay = true;
    private string status = "";
    private Vector2 fileScroll;
    private Vector2 objectScroll;
    private Vector2 terrainScroll;
    private Vector2 catalogScroll;
    private Vector2 signTextScroll;
    private Vector2 previewPan;
    private Vector2 previewOrigin;
    private int selectedObject = -1;
    private int selectedTerrain = -1;
    private int editorTab;
    private int quickPlaceTab;
    private int movementReferenceSelection = int.MinValue;
    private Vector2Int? lastBrushCell;
    private Vector2Int? lastSelectionCell;
    private bool open;
    private bool dirty;
    private float zoom = 22f;
    private bool renderOrderDirty = true;
    private StructureFile? renderOrderStructure;
    private readonly List<PreviewDrawCommand> previewDrawCommands = new();

    private sealed class SpritePreview
    {
        public Sprite sprite = null!;
        public Vector2 localOrigin;
        public Vector2 localAxisX;
        public Vector2 localAxisY;
        public Sprite[] turnableSprites = Array.Empty<Sprite>();
        public bool flipX;
        public bool flipY;
        public int sortingLayer;
        public int sortingOrder;
        public int renderQueue;
        public float localZ;
        public bool ySorted;
        public float sorterY;
        public int sorterOffset;
        public string turnableName = "";
        public bool variantTarget;
        public Color color = Color.white;
    }

    private sealed class PreviewDrawCommand
    {
        public PreparedSprite visual = null!;
        public SpritePreview part = null!;
        public Sprite sprite = null!;
        public float x, y;
        public PreviewSortKey key;
    }

    internal static void Open(
        Silverpine.ModdingTools.ModToolSession session)
    {
        if (instance == null)
        {
            GameObject root = new("Structure Handler Editor IMGUI");
            instance = root.AddComponent<StructureEditorUI>();
        }
        else
        {
            instance.gameObject.SetActive(true);
        }

        instance.open = true;
        instance.AttachSession(session);
        instance.BuildPlacementCatalog();
        instance.RefreshFiles();
    }

    private void Close()
    {
        if (!AllowDiscard(Close)) return;
        open = false;
        // These are references to the game's existing prefab sprites, not
        // instantiated objects. Drop every editor-held reference on close.
        spritePreviews.Clear();
        spriteVariants.Clear();
        spriteLocalBounds.Clear();
        ClearRenderCaches();
        ClearItemEditor();
        conflictStructure = null;
        cachedCells.Clear();
        cellIndex.Clear();
        planningIndex.Clear();
        planningColors.Clear();
        ClearZoneColors();
        StopPlanningBrush();
        StopZoneBrush();
        ClearSharedMetadata();
        structure = null;
        dirty = false;
        historyDirty = false;
        history.Reset(null);
        savedSnapshot = null;
        pendingOverwritePath = null;
        InvalidateRenderOrder();
        selectedObject = -1;
        selectedTerrain = -1;
        brushPrefabName = "";
        lastBrushCell = null;
        ignorePlacementRules = false;
        gameObject.SetActive(false);
        ReleaseSession();
    }

    protected override void OnFrameworkSessionClosed()
    {
        // An emergency Mod Tools close must not discard an unsaved document.
        // Keep it in memory for the next editor open, without retaining sprites.
        open = false;
        spritePreviews.Clear();
        spriteVariants.Clear();
        spriteLocalBounds.Clear();
        ClearRenderCaches();
        StopPlanningBrush();
        StopZoneBrush();
        ClearItemEditor();
        ClearZoneColors();
        ClearSharedMetadata();
        ignorePlacementRules = false;
        gameObject.SetActive(false);
    }

    private void OnGUI()
    {
        if (!open)
            return;
        editorFieldSequence = 0;
        // Finish a text-edit group before another control/selection is used.
        if (Event.current.type == EventType.MouseDown) FlushHistory();
        if (Event.current.type == EventType.MouseUp)
        {
            brushStroke = false;
            lastPlanningCell = null;
            zoneStroke.End();
        }
        if (pendingDiscardAction == null && pendingOverwritePath == null &&
            Event.current.type == EventType.KeyDown && Event.current.control && Event.current.keyCode == KeyCode.Z)
        {
            if (Event.current.shift) Redo(); else Undo();
            Event.current.Use();
        }

        GUI.enabled = true;
        GUI.color = Color.white;
        GUI.backgroundColor = Color.white;
        GUI.depth = -1000;
        using Silverpine.ModdingTools.ModGuiScope guiScope =
            Silverpine.ModdingTools.ModGui.BeginScaled(
                DesignWidth, DesignHeight);
        BeginEditorTextInput();

        Color oldBackdropColor = GUI.color;
        GUI.color = new Color(0.025f, 0.035f, 0.045f, 0.97f);
        GUI.DrawTexture(
            new Rect(0, 0, DesignWidth, DesignHeight),
            Texture2D.whiteTexture);
        GUI.color = oldBackdropColor;
        GUI.Box(new Rect(0, 0, DesignWidth, DesignHeight), "");
        if (pendingOverwritePath != null) { DrawOverwritePrompt(); return; }
        if (pendingDiscardAction != null) { DrawDiscardPrompt(); return; }
        GUILayout.BeginArea(new Rect(
            12, 10, DesignWidth - 24, DesignHeight - 20));
        GUILayout.BeginHorizontal();
        GUILayout.Label("Structure Editor", GUILayout.Width(180));
        GUILayout.Label(
            structure == null
                ? "Choose a format-3 JSON file."
                : $"{Path.GetFileName(currentPath)}{(dirty ? "  (unsaved)" : "")}");
        GUI.enabled = history.CanUndo || historyDirty;
        if (GUILayout.Button("Undo", GUILayout.Width(70))) Undo();
        GUI.enabled = history.CanRedo;
        if (GUILayout.Button("Redo", GUILayout.Width(70))) Redo();
        GUI.enabled = true;
        if (GUILayout.Button("Close", GUILayout.Width(90)))
            Close();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        DrawFilePanel();
        DrawPreviewPanel();
        DrawDetailsPanel();
        GUILayout.EndHorizontal();

        if (!string.IsNullOrWhiteSpace(status))
            GUILayout.Label(status);
        GUILayout.EndArea();
        FlushHistory(force: false);
    }

    private void DrawFilePanel()
    {
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(230));
        GUILayout.Label("Structure files");
        if (GUILayout.Button("New Blank Structure"))
            CreateBlankStructure();
        if (GUILayout.Button("Refresh"))
            RefreshFiles();
        GUI.enabled = checkedFiles.Count > 0;
        if (GUILayout.Button(
                $"Combine Checked ({checkedFiles.Count})"))
            CombineCheckedFiles();
        GUI.enabled = true;
        Rect viewport = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        const float rowHeight = 28f;
        Rect content = new(0, 0, Mathf.Max(1f, viewport.width - 20), files.Count * rowHeight);
        fileScroll = GUI.BeginScrollView(viewport, fileScroll, content, false, true);
        var rows = StructureAlgorithms.VisibleRows(fileScroll.y, viewport.height, rowHeight, files.Count);
        for (int i = rows.First; i < rows.Last; i++)
        {
            string path = files[i];
            bool wasChecked = checkedFiles.Contains(path);
            bool isChecked = GUI.Toggle(new Rect(0, i * rowHeight, 22, rowHeight - 2), wasChecked, "");
            if (isChecked != wasChecked)
            {
                if (isChecked)
                    checkedFiles.Add(path);
                else
                    checkedFiles.Remove(path);
            }
            if (GUI.Button(new Rect(24, i * rowHeight, Mathf.Max(1f, content.width - 24), rowHeight - 2), fileLabels[i]))
                Load(path);
        }
        GUI.EndScrollView(true);
        GUILayout.Space(8);
        GUILayout.Label("Save As name");
        saveAsName = EditorTextField(saveAsName, "StructureHandler.saveAs");
        GUI.enabled = structure != null &&
                      !string.IsNullOrWhiteSpace(currentPath);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Save"))
            Save(currentPath);
        GUI.enabled = structure != null;
        if (GUILayout.Button("Save As"))
            SaveAs();
        GUILayout.EndHorizontal();
        GUI.enabled = true;
        GUILayout.EndVertical();
    }

    private void DrawPreviewPanel()
    {
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.BeginHorizontal();
        GUILayout.Label("2D preview");
        if (GUILayout.Button("-", GUILayout.Width(28)))
            zoom = Mathf.Max(8f, zoom - 2f);
        GUILayout.Label($"{zoom:0}px", GUILayout.Width(48));
        if (GUILayout.Button("+", GUILayout.Width(28)))
            zoom = Mathf.Min(60f, zoom + 2f);
        if (GUILayout.Button("Center", GUILayout.Width(65)))
        {
            previewPan = Vector2.zero;
            previewOrigin = CalculateStructureCenter();
        }
        bool nextDeleteBrush = GUILayout.Toggle(
            deleteBrush, "Quick Delete", GUI.skin.button,
            GUILayout.Width(95));
        if (nextDeleteBrush != deleteBrush)
        {
            deleteBrush = nextDeleteBrush;
            lastBrushCell = null;
            if (deleteBrush)
            {
                itemBrush = false;
                StopPlanningBrush();
                StopZoneBrush();
                brushPrefabName = "";
                status =
                    "Quick Delete enabled: left-click and drag to erase; " +
                    "right-click to cancel.";
            }
        }
        showFootprintOverlay = GUILayout.Toggle(
            showFootprintOverlay, "Green Tiles", GUI.skin.button,
            GUILayout.Width(90));
        GUILayout.EndHorizontal();

        DrawPlacementRulesToggle();
        Rect preview = GUILayoutUtility.GetRect(
            300, 10000, 300, 10000, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        GUI.Box(preview, "");
        if (structure != null)
        {
            Vector2 center = preview.center + previewPan;
            // Keep oversized furniture and multi-part sprites inside the
            // preview instead of allowing IMGUI textures to cover controls.
            GUI.BeginGroup(preview);
            Rect clippedPreview =
                new(0f, 0f, preview.width, preview.height);
            Vector2 clippedCenter =
                center - new Vector2(preview.x, preview.y);
            if (Event.current.type == EventType.Repaint)
            {
                EnsureEditorGeometry();
                DrawCachedPreview(clippedPreview, clippedCenter);
            }
            GUI.EndGroup();
            HandlePreviewInput(preview, center);
        }
        GUILayout.Label(
            zoneBrush
                ? "Zone brush: left-click/drag to rename terrain/floors; right-click to cancel. Middle-drag pans."
                : planningBrush
                ? $"Planning {(planningEraser ? "eraser" : planningColor)}: left-drag to mark; right-click to cancel."
                : string.IsNullOrWhiteSpace(brushPrefabName)
                ? "Drag or middle-drag to pan. Mouse wheel zooms; click to select."
                : $"Brush: {CatalogDisplayName(brushPrefabName)} — left-click/drag to place, right-click to cancel.");
        if (structure != null)
        {
            EnsureEditorGeometry();
            GUILayout.Label("Blue outline/fill: normal world-transition cells. " +
                (transitionOverlapCount > 0 ? $"Warning: {transitionOverlapCount} overlapped by this structure." : "No structure overlap."));
            string? conflict = PlacementConflict();
            if (conflict != null) GUILayout.Label("Placement conflict: " + conflict);
        }
        GUILayout.EndVertical();
    }

    private void DrawGrid(Rect rect, Vector2 center)
    {
        Vector2 origin = GetStructureCenter();
        Vector2 worldZero = new(
            center.x - origin.x * zoom,
            center.y + origin.y * zoom);
        float firstX = Mathf.Repeat(
            worldZero.x + zoom * 0.5f - rect.x, zoom);
        float firstY = Mathf.Repeat(
            worldZero.y + zoom * 0.5f - rect.y, zoom);

        Color old = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.12f);
        for (float x = firstX; x < rect.width; x += zoom)
            DrawPreviewTexture(
                new Rect(rect.x + x, rect.y, 1f, rect.height),
                Texture2D.whiteTexture);
        for (float y = firstY; y < rect.height; y += zoom)
            DrawPreviewTexture(
                new Rect(rect.x, rect.y + y, rect.width, 1f),
                Texture2D.whiteTexture);
        GUI.color = old;
    }

    private void DrawWorldTileOutlines(Rect rect, Vector2 center)
    {
        Vector2 origin = GetStructureCenter();
        float worldLeft =
            origin.x + (rect.xMin - center.x) / zoom;
        float worldRight =
            origin.x + (rect.xMax - center.x) / zoom;
        float worldTop =
            origin.y - (rect.yMin - center.y) / zoom;
        float worldBottom =
            origin.y - (rect.yMax - center.y) / zoom;

        int firstTileX = Mathf.FloorToInt((worldLeft + 50.5f) / 100f);
        int lastTileX = Mathf.CeilToInt((worldRight + 50.5f) / 100f);
        int firstTileY = Mathf.FloorToInt((worldBottom + 50.5f) / 100f);
        int lastTileY = Mathf.CeilToInt((worldTop + 50.5f) / 100f);

        Color old = GUI.color;
        GUI.color = new Color(1f, 0.55f, 0.08f, 0.95f);
        for (int tileX = firstTileX; tileX <= lastTileX; tileX++)
        {
            float boundaryX = tileX * 100f - 50.5f;
            float screenX = WorldToPreview(
                center, boundaryX, origin.y).x;
            if (screenX >= rect.xMin && screenX <= rect.xMax)
            {
                DrawPreviewTexture(
                    new Rect(screenX - 2f, rect.yMin, 5f, rect.height),
                    Texture2D.whiteTexture);
            }
        }

        for (int tileY = firstTileY; tileY <= lastTileY; tileY++)
        {
            float boundaryY = tileY * 100f - 50.5f;
            float screenY = WorldToPreview(
                center, origin.x, boundaryY).y;
            if (screenY >= rect.yMin && screenY <= rect.yMax)
            {
                DrawPreviewTexture(
                    new Rect(rect.xMin, screenY - 2f, rect.width, 5f),
                    Texture2D.whiteTexture);
            }
        }
        GUI.color = old;
    }

    private void DrawRecords(Rect preview, Vector2 center)
    {
        EnsureRenderOrder();
        EnsureVisiblePreview(preview, center);
        foreach (int index in visibleDraws)
        {
            var command = previewDrawCommands[index];
            DrawPrefabSprite(preview, WorldToPreview(center, command.x, command.y),
                command.visual);
        }
        DrawZoneColors(preview, center);
        // Editor markers are overlays, not part of the game's sprite ordering.
        if (selectedTerrain >= 0 && selectedTerrain < structure!.supportingTerrain.Count)
        {
            var terrain = structure.supportingTerrain[selectedTerrain];
            DrawTile(preview, center, terrain.x, terrain.y, new Color(1f, 0.75f, 0.1f, 0.55f));
        }
        foreach (int i in visibleMarkers)
        {
            StructureObject item = structure!.objects[i];
            if (!showFootprintOverlay && i != selectedObject && GetSpritePreviews(item.prefabName).Count > 0) continue;
            DrawObject(preview, center, item,
                i == selectedObject
                    ? new Color(1f, 0.75f, 0.1f, 0.95f)
                    : new Color(
                        0.25f, 0.9f, 0.35f,
                        showFootprintOverlay ? 0.8f : 0f));
        }

    }

    private void DrawTopLeftMarker(Vector2 center)
    {
        Vector2 topLeft = GetTopLeftCoordinate();
        Vector2 marker = WorldToPreview(center, topLeft.x, topLeft.y);
        Color old = GUI.color;
        GUI.color = Color.red;
        DrawPreviewTexture(
            new Rect(marker.x - 5f, marker.y - 5f, 10f, 10f),
            Texture2D.whiteTexture);
        GUI.color = old;
    }

    private void EnsureRenderOrder()
    {
        if (!renderOrderDirty &&
            ReferenceEquals(renderOrderStructure, structure))
            return;

        previewSurfaceValid = false;
        previewDrawCommands.Clear();
        foreach (var terrain in structure!.supportingTerrain)
            AddPreviewCommands(terrain.prefabName, terrain.x, terrain.y, terrain.z,
                terrain.spriteVariantIndex, -1);
        foreach (var item in structure.objects)
            if (!AddItemPreviewCommands(item))
                AddPreviewCommands(item.prefabName, item.x, item.y, item.z,
                    item.spriteVariantIndex, item.turnableIndex);
        previewDrawCommands.Sort((left, right) => left.key.CompareTo(right.key));
        RebuildPreviewIndex();
        renderOrderStructure = structure;
        renderOrderDirty = false;
    }

    private void AddPreviewCommands(string prefabName, float x, float y, float z,
        int variant, int rotation)
    {
        Sprite[] variants = variant < 0 ? Array.Empty<Sprite>() : GetSpriteVariants(prefabName);
        foreach (var part in GetSpritePreviews(prefabName))
        {
            Sprite sprite = part.sprite;
            int normalizedRotation = rotation;
            if (rotation >= 0 && part.turnableSprites.Length > 0)
            {
                normalizedRotation = (rotation % part.turnableSprites.Length + part.turnableSprites.Length) % part.turnableSprites.Length;
                sprite = part.turnableSprites[normalizedRotation];
            }
            else if (rotation < 0 && variant >= 0 && variants.Length > 0 && part.variantTarget)
            {
                sprite = variants[(variant % variants.Length + variants.Length) % variants.Length];
            }
            if (sprite == null || PreviewLightingPolicy.IsFakeAO(sprite.name)) continue;
            int order = part.ySorted
                ? PreviewSortKey.YOrder(y + part.sorterY, part.sorterOffset,
                    PreviewSortKey.RotationOffset(part.turnableName, normalizedRotation))
                : part.sortingOrder;
            previewDrawCommands.Add(new PreviewDrawCommand
            {
                part = part, sprite = sprite, x = x, y = y,
                key = new PreviewSortKey(part.sortingLayer, order, part.renderQueue,
                    z + part.localZ, previewDrawCommands.Count)
            });
        }
    }

    private void DrawObject(
        Rect preview, Vector2 center, StructureObject item, Color markerColor)
    {
        Vector2 point = WorldToPreview(center, item.x, item.y);
        if (!IsPrefabVisible(preview, point, item.prefabName))
            return;
        List<SpritePreview> previews = GetSpritePreviews(item.prefabName);

        // A full one-world-unit square matches prefab_tile_rock and makes the
        // serialized coordinate footprint unambiguous.
        float markerAlpha = previews.Count == 0
            ? Mathf.Max(markerColor.a, 0.35f)
            : markerColor.a > 0f ? 0.28f : 0f;
        if (markerAlpha <= 0f)
            return;
        Color old = GUI.color;
        GUI.color = new Color(
            markerColor.r, markerColor.g, markerColor.b,
            markerAlpha);
        foreach (Vector2Int occupied in CachedCells(item))
        {
            Vector2 occupiedPoint =
                WorldToPreview(center, occupied.x, occupied.y);
            Rect marker = new(
                occupiedPoint.x - zoom * 0.5f,
                occupiedPoint.y - zoom * 0.5f,
                zoom,
                zoom);
            if (preview.Overlaps(marker))
                DrawPreviewTexture(marker, Texture2D.whiteTexture);
        }
        GUI.color = old;
    }

    private bool IsPrefabVisible(
        Rect preview, Vector2 point, string prefabName)
    {
        Rect local = GetPrefabLocalBounds(prefabName);
        Rect screen = new(
            point.x + local.xMin * zoom,
            point.y - local.yMax * zoom,
            local.width * zoom,
            local.height * zoom);
        return preview.Overlaps(screen);
    }

    private void DrawPrefabSprite(
        Rect preview,
        Vector2 point,
        PreparedSprite visual)
    {
        Vector2 spriteCenter = point + new Vector2(
            visual.offset.x * zoom, -visual.offset.y * zoom);

        // Rotate the actual quad, not just its bounding rectangle. Preserve
        // prefab pivots, scale and mirrored child transforms.
        float width = visual.drawSize.x * zoom;
        float height = visual.drawSize.y * zoom;
        Rect drawRect = new(spriteCenter.x - width * 0.5f,
            spriteCenter.y - height * 0.5f, width, height);
        if (capturingPreview)
        {
            DrawPreviewQuad(drawRect, visual.texture, visual.uv, visual.color, visual.angle);
            return;
        }
        Color old = GUI.color;
        // Most tiles are axis-aligned: don't cross the Unity GUI matrix API
        // twice per sprite just to restore an unchanged matrix.
        if (Mathf.Abs(visual.angle) <= 0.001f)
        {
            GUI.color = visual.color;
            GUI.DrawTextureWithTexCoords(drawRect, visual.texture, visual.uv, alphaBlend: true);
            GUI.color = old;
            return;
        }
        Matrix4x4 oldMatrix = GUI.matrix;
        try
        {
            GUI.color = visual.color;
            GUIUtility.RotateAroundPivot(visual.angle, spriteCenter);
            GUI.DrawTextureWithTexCoords(drawRect, visual.texture, visual.uv, alphaBlend: true);
        }
        finally { GUI.matrix = oldMatrix; GUI.color = old; }
    }

    private List<SpritePreview> GetSpritePreviews(string prefabName)
    {
        if (spritePreviews.TryGetValue(
                prefabName, out List<SpritePreview> cached))
            return cached;

        List<SpritePreview> previews = new();
        spritePreviews[prefabName] = previews;
        try
        {
            GameObject? prefab = Plugin.ResolvePrefab(prefabName);
            if (prefab == null)
                return previews;

            Turnable turnable =
                prefab.GetComponentInChildren<Turnable>(true);
            YSorter[] sorters = prefab.GetComponentsInChildren<YSorter>(true);
            SpriteRenderer variantTarget = prefab.GetComponentsInChildren<SpriteRenderer>(true)
                .FirstOrDefault(candidate => candidate.enabled && candidate.sprite != null);
            List<SpriteRenderer> renderers =
                prefab.GetComponentsInChildren<SpriteRenderer>(false)
                    .Where(candidate =>
                        candidate.sprite != null &&
                        candidate.enabled &&
                        !IsPreviewLightingEffect(candidate, prefab.transform))
                    .ToList();

            foreach (SpriteRenderer renderer in renderers)
            {
                // Imports retain the prefab root's rotation and scale.
                Vector3 relativeOrigin = renderer.transform.position - prefab.transform.position;
                Vector3 relativeX = renderer.transform.TransformVector(Vector3.right);
                Vector3 relativeY = renderer.transform.TransformVector(Vector3.up);
                bool usesTurnableSprites =
                    turnable != null &&
                    turnable.sprites != null &&
                    turnable.gameObject.GetSpriteRendererExtended() == renderer;
                YSorter sorter = sorters.FirstOrDefault(candidate =>
                    candidate.gameObject.GetSpriteRendererExtended() == renderer);
                previews.Add(new SpritePreview
                {
                    sprite = renderer.sprite,
                    localOrigin = new Vector2(
                        relativeOrigin.x, relativeOrigin.y),
                    localAxisX = new Vector2(relativeX.x, relativeX.y),
                    localAxisY = new Vector2(relativeY.x, relativeY.y),
                    turnableSprites = usesTurnableSprites
                        ? turnable!.sprites!
                        : Array.Empty<Sprite>(),
                    flipX = renderer.flipX,
                    flipY = renderer.flipY,
                    sortingLayer = SortingLayer.GetLayerValueFromID(renderer.sortingLayerID),
                    sortingOrder = renderer.sortingOrder,
                    renderQueue = renderer.sharedMaterial != null ? renderer.sharedMaterial.renderQueue : 3000,
                    localZ = relativeOrigin.z,
                    ySorted = sorter != null,
                    sorterY = sorter != null ? sorter.transform.position.y - prefab.transform.position.y : 0f,
                    sorterOffset = sorter != null ? sorter.offset : 0,
                    turnableName = usesTurnableSprites ? turnable!.name : "",
                    variantTarget = renderer == variantTarget,
                    color = renderer.color
                });
            }
            previews.Sort((left, right) =>
            {
                int layer = left.sortingLayer.CompareTo(right.sortingLayer);
                return layer != 0
                    ? layer
                    : left.sortingOrder.CompareTo(right.sortingOrder);
            });
            return previews;
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning(
                $"Could not preview sprite for {prefabName}: {exception.Message}");
            return previews;
        }
    }

    private Rect GetPrefabLocalBounds(string prefabName)
    {
        if (spriteLocalBounds.TryGetValue(prefabName, out Rect cached))
            return cached;

        bool initialized = false;
        float minX = -0.5f;
        float minY = -0.5f;
        float maxX = 0.5f;
        float maxY = 0.5f;
        foreach (SpritePreview preview in GetSpritePreviews(prefabName))
        {
            IEnumerable<Sprite> sprites =
                new[] { preview.sprite }
                    .Concat(preview.turnableSprites)
                    .Concat(GetSpriteVariants(prefabName))
                    .Where(sprite => sprite != null)
                    .Distinct();
            foreach (Sprite sprite in sprites)
            {
                Bounds bounds = sprite.bounds;
                Vector2 localOffset =
                    preview.localOrigin +
                    preview.localAxisX * (preview.flipX ? -bounds.center.x : bounds.center.x) +
                    preview.localAxisY * (preview.flipY ? -bounds.center.y : bounds.center.y);
                Vector2 size = new(
                    Mathf.Abs(preview.localAxisX.x * bounds.size.x) +
                    Mathf.Abs(preview.localAxisY.x * bounds.size.y),
                    Mathf.Abs(preview.localAxisX.y * bounds.size.x) +
                    Mathf.Abs(preview.localAxisY.y * bounds.size.y));
                float spriteMinX = localOffset.x - size.x * 0.5f;
                float spriteMaxX = localOffset.x + size.x * 0.5f;
                float spriteMinY = localOffset.y - size.y * 0.5f;
                float spriteMaxY = localOffset.y + size.y * 0.5f;
                if (!initialized)
                {
                    minX = spriteMinX;
                    maxX = spriteMaxX;
                    minY = spriteMinY;
                    maxY = spriteMaxY;
                    initialized = true;
                }
                else
                {
                    minX = Mathf.Min(minX, spriteMinX);
                    maxX = Mathf.Max(maxX, spriteMaxX);
                    minY = Mathf.Min(minY, spriteMinY);
                    maxY = Mathf.Max(maxY, spriteMaxY);
                }
            }
        }

        Rect result = Rect.MinMaxRect(minX, minY, maxX, maxY);
        spriteLocalBounds[prefabName] = result;
        return result;
    }

    private void MarkStructureDirty(bool geometryChanged = true, bool renderChanged = false,
        bool zonesChanged = true, object? changedRecord = null)
    {
        dirty = true;
        historyDirty = true;
        historyDeadline = Event.current != null && Event.current.rawType == EventType.KeyDown
            ? Time.realtimeSinceStartup + 0.35f : 0f;
        editRevision++;
        if (zonesChanged) zoneRevision++;
        if (geometryChanged)
        {
            geometryDirty = true;
            cachedCells.Clear();
            placementRevision++;
            if (changedRecord == null) placementIndexDirty = true;
            else if (!placementIndexDirty) IndexPlacementRecord(changedRecord);
        }
        if (geometryChanged || renderChanged) InvalidateVisuals();
    }

    private void InvalidateRenderOrder()
    {
        InvalidateVisuals();
        placementIndexDirty = true;
        placementRevision++;
        zoneRevision++;
        geometryDirty = true;
        cachedCells.Clear();
    }

    private void InvalidateVisuals()
    {
        previewVisualRevision++;
        previewSurfaceValid = false;
        renderOrderDirty = true;
        renderOrderStructure = null;
        visiblePreviewValid = false;
    }

    private static bool IsPreviewLightingEffect(
        SpriteRenderer renderer, Transform prefabRoot)
    {
        if (PreviewLightingPolicy.IsLightingEffect(
                renderer.gameObject.name, renderer.sprite?.name,
                renderer.sharedMaterial?.name,
                renderer.sharedMaterial?.shader?.name))
            return true;

        // Some prefabs put an otherwise generically named renderer underneath
        // a FakeAO container. Inspect that branch only, never the world scene.
        for (Transform current = renderer.transform; current != null;
             current = current.parent)
        {
            if (PreviewLightingPolicy.IsFakeAO(current.name)) return true;
            if (current == prefabRoot) break;
        }
        return false;
    }

    internal Sprite[] GetSpriteVariants(string prefabName)
    {
        if (spriteVariants.TryGetValue(prefabName, out Sprite[] cached))
            return cached;

        Sprite[] variants = GetSpriteVariantsForPrefab(prefabName);
        spriteVariants[prefabName] = variants;
        return variants;
    }

    internal static Sprite[] GetSpriteVariantsForPrefab(string prefabName)
    {
        if (sharedVariants.TryGetValue(prefabName, out var cached)) return cached;
        List<Sprite> result = new();
        try
        {
            GameObject? prefab =
                Plugin.ResolvePrefab(prefabName);
            if (prefab != null)
            {
                foreach (MonoBehaviour component in
                         prefab.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (component == null || component is Turnable)
                        continue;
                    foreach (FieldInfo field in component.GetType().GetFields(
                                 BindingFlags.Instance |
                                 BindingFlags.Public |
                                 BindingFlags.NonPublic))
                    {
                        if (field.FieldType == typeof(Sprite[]) &&
                            field.GetValue(component) is Sprite[] sprites)
                        {
                            result.AddRange(sprites.Where(sprite => sprite != null));
                        }
                        else if (typeof(IEnumerable<Sprite>).IsAssignableFrom(
                                     field.FieldType) &&
                                 field.GetValue(component) is
                                     IEnumerable<Sprite> spriteList)
                        {
                            result.AddRange(
                                spriteList.Where(sprite => sprite != null));
                        }
                    }
                }
            }
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning(
                $"Could not inspect sprite variants for {prefabName}: " +
                exception.Message);
        }

        var variants = result.Distinct().ToArray();
        if (variants.Length > 0) sharedVariants[prefabName] = variants;
        return variants;
    }

    private void DrawTile(
        Rect preview, Vector2 center, float worldX, float worldY, Color color)
    {
        Vector2 point = WorldToPreview(center, worldX, worldY);
        Rect tile = new(
            point.x - zoom * 0.42f, point.y - zoom * 0.42f,
            zoom * 0.84f, zoom * 0.84f);
        if (!preview.Overlaps(tile))
            return;
        Color old = GUI.color;
        GUI.color = color;
        DrawPreviewTexture(tile, Texture2D.whiteTexture);
        GUI.color = old;
    }

    private void HandlePreviewInput(Rect preview, Vector2 center)
    {
        Event current = Event.current;
        // A text field/scrollbar owns a drag even if the pointer leaves its
        // rectangle and crosses the preview. Never turn that into paint/pan.
        if (GUIUtility.hotControl != 0) return;
        if (pendingDiscardAction != null || !preview.Contains(current.mousePosition))
        {
            lastPlanningCell = null;
            zoneStroke.BreakPath();
            return;
        }
        if (current.type == EventType.ScrollWheel)
        {
            zoneStroke.BreakPath();
            float next = Mathf.Clamp(zoom * Mathf.Pow(1.12f, -current.delta.y), 8f, 60f);
            Vector2 pointer = current.mousePosition - preview.center;
            previewPan = pointer - (pointer - previewPan) * (next / zoom);
            zoom = next;
            current.Use();
            return;
        }

        if (current.type == EventType.MouseDrag && current.button == 2)
        {
            lastPlanningCell = null;
            zoneStroke.BreakPath();
            previewPan += current.delta;
            current.Use();
            return;
        }

        if (HandleItemInput(center)) return;
        if (HandleZoneInput(center)) return;
        if (HandlePlanningInput(center)) return;

        if (deleteBrush)
        {
            if (current.type == EventType.MouseDown && current.button == 1)
            {
                deleteBrush = false;
                lastBrushCell = null;
                status = "Quick Delete cancelled.";
                current.Use();
                return;
            }
            if ((current.type == EventType.MouseDown ||
                 current.type == EventType.MouseDrag) &&
                current.button == 0)
            {
                Vector2Int cell =
                    PreviewToWorldCell(center, current.mousePosition);
                if (!lastBrushCell.HasValue || lastBrushCell.Value != cell)
                {
                    brushStroke = true;
                    DeleteAtCell(cell);
                    lastBrushCell = cell;
                }
                current.Use();
            }
            else if (current.type == EventType.MouseUp &&
                     current.button == 0)
            {
                lastBrushCell = null;
                current.Use();
            }
            return;
        }

        if (current.type == EventType.MouseDown && current.button == 1 &&
            !string.IsNullOrWhiteSpace(brushPrefabName))
        {
            status = "Quick-place brush cancelled.";
            brushPrefabName = "";
            lastBrushCell = null;
            current.Use();
            return;
        }

        if (!string.IsNullOrWhiteSpace(brushPrefabName))
        {
            if ((current.type == EventType.MouseDown ||
                 current.type == EventType.MouseDrag) &&
                current.button == 0)
            {
                Vector2Int cell = PreviewToWorldCell(center, current.mousePosition);
                if (!lastBrushCell.HasValue || lastBrushCell.Value != cell)
                {
                    brushStroke = true;
                    PlaceBrushObject(cell, PreviewToWorldPoint(center, current.mousePosition));
                    lastBrushCell = cell;
                }
                current.Use();
            }
            else if (current.type == EventType.MouseUp && current.button == 0)
            {
                lastBrushCell = null;
                current.Use();
            }
            return;
        }

        if (current.type == EventType.MouseDrag && current.button == 0)
        {
            previewPan += current.delta;
            current.Use();
        }
        else if (current.type == EventType.MouseDown && current.button == 0)
        {
            Vector2Int clickedCell =
                PreviewToWorldCell(center, current.mousePosition);
            EnsureEditorGeometry();
            List<Tuple<bool, int>> candidates = cellIndex.TryGetValue(clickedCell, out var indexed)
                ? indexed : new List<Tuple<bool, int>>();

            if (candidates.Count == 0)
            {
                selectedObject = -1;
                selectedTerrain = -1;
            }
            else
            {
                int currentIndex = candidates.FindIndex(candidate =>
                    candidate.Item1
                        ? selectedTerrain == candidate.Item2
                        : selectedObject == candidate.Item2);
                int nextIndex =
                    lastSelectionCell == clickedCell && currentIndex >= 0
                        ? (currentIndex + 1) % candidates.Count
                        : 0;
                Tuple<bool, int> next = candidates[nextIndex];
                selectedObject = next.Item1 ? -1 : next.Item2;
                selectedTerrain = next.Item1 ? next.Item2 : -1;
                editorTab = next.Item1 ? 1 : StructureItemData.IsItem(structure!.objects[next.Item2]) ? 4 : 0;
            }

            lastSelectionCell = clickedCell;
            if (candidates.Count > 1)
            {
                status =
                    $"Overlapping records: click again for the next " +
                    $"({candidates.Count} total).";
            }
            current.Use();
        }
    }

    private Vector2Int PreviewToWorldCell(Vector2 center, Vector2 screenPoint)
        => Vector2Int.RoundToInt(PreviewToWorldPoint(center, screenPoint));

    private Vector2 PreviewToWorldPoint(Vector2 center, Vector2 screenPoint)
    {
        Vector2 origin = GetStructureCenter();
        return new Vector2(
            origin.x + (screenPoint.x - center.x) / zoom,
            origin.y - (screenPoint.y - center.y) / zoom);
    }

    private void PlaceBrushObject(Vector2Int cell, Vector2? freePosition = null)
    {
        if (structure == null)
            return;
        float z = TryFloat(addZ, out float parsedZ) ? parsedZ : 0f;
        if (IsQuickPlaceSupportingTerrain(brushPrefabName))
        {
            SupportingTerrain existingTerrain =
                structure.supportingTerrain.FirstOrDefault(item =>
                    item.x == cell.x &&
                    item.y == cell.y &&
                    item.prefabName.Equals(
                        brushPrefabName,
                        StringComparison.OrdinalIgnoreCase));
            var terrain = existingTerrain ?? new SupportingTerrain
                { prefabName = brushPrefabName, x = cell.x, y = cell.y };
            terrain.z = z;
            terrain.hasMapZoneName = !string.IsNullOrWhiteSpace(terrainBrushZone);
            terrain.mapZoneName = terrainBrushZone;
            PlaceEditorTerrain(terrain, true, existingTerrain != null);
            return;
        }

        var profile = Profile(brushPrefabName);
        float snapY = profile.Snap >= 0 ? profile.Snap / 32f : 0f;
        Vector2 position = ignorePlacementRules && freePosition.HasValue
            ? freePosition.Value : new Vector2(cell.x, cell.y + snapY);
        PlaceEditorObject(new StructureObject
        {
            prefabName = brushPrefabName,
            x = position.x,
            y = position.y,
            z = z,
            npcInteractionRangeExtender =
                addNpcInteractionRangeExtender,
            turnableIndex = GetDefaultTurnableIndex(brushPrefabName),
            occupiedOffsets = GetPrefabOccupiedOffsets(
                brushPrefabName, GetDefaultTurnableIndex(brushPrefabName))
        }, true);
        addX = Format(position.x);
        addY = Format(position.y);
    }

    private void DeleteAtCell(Vector2Int cell)
    {
        if (structure == null)
            return;

        var cells = new HashSet<Vector2Int> { cell };
        CollectPlacementCandidates(cells);
        var targets = new HashSet<object>(placementCandidates.Where(record => RecordOccupies(record, cells)));
        int removed = RemoveIndexedRecords(targets);
        if (removed == 0)
        {
            status = $"Nothing to delete at {cell.x}, {cell.y}.";
            return;
        }

        selectedObject = -1;
        selectedTerrain = -1;
        // Index removal is already complete; do not rebuild it on every drag.
        bool wasDirty = placementIndexDirty;
        MarkStructureDirty();
        placementIndexDirty = wasDirty;
        status =
            $"Deleted {removed} record(s) at " +
            $"{cell.x}, {cell.y}.";
    }

    private Vector2 WorldToPreview(Vector2 center, float x, float y)
    {
        Vector2 origin = GetStructureCenter();
        return center + new Vector2((x - origin.x) * zoom, -(y - origin.y) * zoom);
    }

    private Vector2 GetStructureCenter()
    {
        return previewOrigin;
    }

    private Vector2 CalculateStructureCenter()
    {
        if (structure == null)
            return Vector2.zero;
        IEnumerable<Vector2> points = structure.objects
            .Select(item => new Vector2(item.x, item.y))
            .Concat(structure.supportingTerrain.Select(
                item => new Vector2(item.x, item.y)));
        if (!points.Any())
            points = structure.planningTiles.Select(item => new Vector2(item.x, item.y));
        if (!points.Any())
            return Vector2.zero;
        return new Vector2(
            (points.Min(point => point.x) + points.Max(point => point.x)) / 2f,
            (points.Min(point => point.y) + points.Max(point => point.y)) / 2f);
    }

    private void DrawDetailsPanel()
    {
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(330));
        int previousTab = editorTab;
        GUILayout.BeginHorizontal();
        if (GUILayout.Toggle(
                editorTab == 0, "Objects", GUI.skin.button))
            editorTab = 0;
        if (GUILayout.Toggle(
                editorTab == 1, "Terrain", GUI.skin.button))
            editorTab = 1;
        if (GUILayout.Toggle(
                editorTab == 2, "Quick Place", GUI.skin.button))
            editorTab = 2;
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Toggle(editorTab == 4, "Items", GUI.skin.button)) editorTab = 4;
        if (GUILayout.Toggle(editorTab == 3, "Plan", GUI.skin.button)) editorTab = 3;
        GUILayout.EndHorizontal();
        if (previousTab != editorTab)
        {
            StopPlanningBrush();
            StopZoneBrush();
            itemBrush = false;
            if (editorTab == 3 || editorTab == 4)
            {
                deleteBrush = false;
                brushPrefabName = "";
            }
        }

        if (structure == null)
        {
            GUILayout.Label("No structure loaded.");
            GUILayout.EndVertical();
            return;
        }

        detailsScroll = GUILayout.BeginScrollView(detailsScroll);
        if (editorTab == 0)
            DrawObjectsTab();
        else if (editorTab == 1)
            DrawTerrainTab();
        else if (editorTab == 2)
            DrawQuickPlaceTab();
        else if (editorTab == 4)
            DrawItemsTab();
        else
            DrawPlanningTab();
        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    private void DrawObjectsTab()
    {
        GUILayout.Label("Objects");
        DrawVirtualList(ref objectScroll, structure!.objects.Count, 190,
            i => { var item = structure.objects[i]; return $"{i + 1}. {item.prefabName} ({item.x:0.##}, {item.y:0.##})"; },
            i => selectedObject == i,
            i => { selectedObject = i; selectedTerrain = -1; });

        if (selectedObject >= 0 && selectedObject < structure.objects.Count)
            DrawSelectedObject();

        DrawMoveEntireStructure();
    }

    private void DrawTerrainTab()
    {
        DrawZoneBrushControls();
        GUILayout.Space(8);
        GUILayout.Label("Supporting terrain");
        DrawVirtualList(ref terrainScroll, structure!.supportingTerrain.Count, 240,
            i => { var item = structure.supportingTerrain[i]; return $"{i + 1}. {item.prefabName} ({item.x}, {item.y})"; },
            i => selectedTerrain == i,
            i => { selectedTerrain = i; selectedObject = -1; });

        if (selectedTerrain >= 0 &&
            selectedTerrain < structure.supportingTerrain.Count)
            DrawSelectedTerrain();
    }

    private void DrawQuickPlaceTab()
    {
        string[] categories = QuickPlaceCategories;
        GUILayout.BeginHorizontal();
        for (int index = 0; index < 3; index++)
            if (GUILayout.Toggle(
                    quickPlaceTab == index,
                    categories[index],
                    GUI.skin.button))
                quickPlaceTab = index;
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        for (int index = 3; index < categories.Length; index++)
            if (GUILayout.Toggle(
                    quickPlaceTab == index,
                    categories[index],
                    GUI.skin.button))
                quickPlaceTab = index;
        GUILayout.EndHorizontal();

        string category = categories[
            Mathf.Clamp(quickPlaceTab, 0, categories.Length - 1)];
        GUILayout.Label($"Quick-place {category.ToLowerInvariant()}");
        if (quickPlaceTab == 0 || quickPlaceTab == 1 || zoneBrush ||
            IsQuickPlaceSupportingTerrain(brushPrefabName))
        {
            DrawZoneBrushControls();
            PlanningLabel("Quick-placed terrain also uses this zone name.");
        }
        catalogSearch = LabeledTextField("Search", catalogSearch);
        RefreshCatalogFilter();
        DrawVirtualList(ref catalogScroll, filteredCatalog.Count, 330,
            i => catalogMetadata[filteredCatalog[i]].Label, _ => false, i =>
            {
                addPrefabName = brushPrefabName = filteredCatalog[i];
                StopPlanningBrush();
                StopZoneBrush();
                addZ = GetQuickPlaceDefaultZ(brushPrefabName).ToString(CultureInfo.InvariantCulture);
                deleteBrush = false;
                lastBrushCell = null;
                status = $"Brush selected: {catalogMetadata[brushPrefabName].Label}.";
            });

        GUILayout.Space(8);
        GUILayout.Label("Add generic prefab (default state)");
        addPrefabName = LabeledTextField("Prefab", addPrefabName);
        addX = LabeledTextField("X", addX);
        addY = LabeledTextField("Y", addY);
        addZ = LabeledTextField("Z", addZ);
        addNpcInteractionRangeExtender = GUILayout.Toggle(
            addNpcInteractionRangeExtender,
            " Add NPC interaction range extender to new objects");
        if (GUILayout.Button("Add object"))
            AddObject();
    }

    private void DrawMoveEntireStructure()
    {
        GUILayout.Space(8);
        GUILayout.Label("Move entire structure");
        Vector2 movementReference = GetMovementReferenceCoordinate();
        int movementSelectionKey =
            selectedObject >= 0
                ? selectedObject
                : selectedTerrain >= 0 ? -selectedTerrain - 2 : -1;
        if (movementReferenceSelection != movementSelectionKey)
        {
            offsetX = Format(movementReference.x);
            offsetY = Format(movementReference.y);
            movementReferenceSelection = movementSelectionKey;
        }
        GUILayout.Label(
            selectedObject >= 0 && selectedObject < structure!.objects.Count
                ? $"Selected-object reference: {Format(movementReference.x)}, " +
                  $"{Format(movementReference.y)}"
                : selectedTerrain >= 0 &&
                  selectedTerrain < structure!.supportingTerrain.Count
                    ? $"Selected-terrain reference: " +
                      $"{Format(movementReference.x)}, " +
                      $"{Format(movementReference.y)}"
                : $"Top-left reference: {Format(movementReference.x)}, " +
                  $"{Format(movementReference.y)}  (red dot)");
        offsetX = LabeledTextField("Target X", offsetX);
        offsetY = LabeledTextField("Target Y", offsetY);
        if (GUILayout.Button(
                selectedObject >= 0 || selectedTerrain >= 0
                    ? "Move selected reference to coordinate"
                    : "Move top-left to coordinate"))
            MoveToCoordinate();
    }

    private void DrawSelectedTerrain()
    {
        SupportingTerrain item =
            structure!.supportingTerrain[selectedTerrain];
        GUILayout.Label("Selected supporting terrain");
        string prefab = LabeledTextField("Prefab", item.prefabName);
        GUILayout.Label("Coordinates: press Enter to apply");
        string x = CoordinateTextField(item, "X", item.x.ToString(CultureInfo.InvariantCulture), true);
        string y = CoordinateTextField(item, "Y", item.y.ToString(CultureInfo.InvariantCulture), true);
        string z = CoordinateTextField(item, "Z", Format(item.z));
        string zone = LabeledTextField(
            "Zone", item.hasMapZoneName ? item.mapZoneName : "");
        if (prefab != item.prefabName)
        {
            string previous = item.prefabName;
            item.prefabName = prefab;
            if (!PlaceEditorTerrain(item, false, true)) item.prefabName = previous;
        }
        if (int.TryParse(x, out int nextX) && nextX != item.x)
        {
            ChangeTerrainPlacement(item, nextX, item.y);
        }
        if (int.TryParse(y, out int nextY) && nextY != item.y)
        {
            ChangeTerrainPlacement(item, item.x, nextY);
        }
        if (z != Format(item.z)) SetFloat(z, value => item.z = value, item.z);
        string currentZone =
            item.hasMapZoneName ? item.mapZoneName : "";
        if (zone != currentZone)
        {
            item.mapZoneName = zone;
            item.hasMapZoneName = true;
            terrainBrushZone = zone;
            MarkStructureDirty(geometryChanged: false);
        }

        Sprite[] variants = GetSpriteVariants(item.prefabName);
        if (variants.Length > 1)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                $"Sprite: {item.spriteVariantIndex + 1}/{variants.Length}");
            if (GUILayout.Button("Previous", GUILayout.Width(75)))
            {
                item.spriteVariantIndex =
                    (item.spriteVariantIndex - 1 + variants.Length) %
                    variants.Length;
                item.lockSpriteVariant = true;
                MarkStructureDirty(geometryChanged: false, renderChanged: true, zonesChanged: false);
            }
            if (GUILayout.Button("Next", GUILayout.Width(55)))
            {
                item.spriteVariantIndex =
                    (item.spriteVariantIndex + 1) % variants.Length;
                item.lockSpriteVariant = true;
                MarkStructureDirty(geometryChanged: false, renderChanged: true, zonesChanged: false);
            }
            GUILayout.EndHorizontal();
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Left")) ChangeTerrainPlacement(item, item.x - 1, item.y);
        if (GUILayout.Button("Right")) ChangeTerrainPlacement(item, item.x + 1, item.y);
        if (GUILayout.Button("Up")) ChangeTerrainPlacement(item, item.x, item.y + 1);
        if (GUILayout.Button("Down")) ChangeTerrainPlacement(item, item.x, item.y - 1);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Duplicate"))
        {
            PlaceEditorTerrain(new SupportingTerrain
            {
                prefabName = item.prefabName,
                x = item.x + 1,
                y = item.y,
                z = item.z,
                spriteVariantIndex = item.spriteVariantIndex,
                lockSpriteVariant = item.lockSpriteVariant,
                hasMapZoneName = item.hasMapZoneName,
                mapZoneName = item.mapZoneName,
                components = (item.components ??
                              new List<StructureComponent>())
                    .Select(component => new StructureComponent
                    {
                        type = component.type,
                        hierarchyPath = component.hierarchyPath,
                        componentIndex = component.componentIndex,
                        dataBase64 = component.dataBase64
                    }).ToList()
            }, false);
        }
        if (GUILayout.Button("Delete"))
        {
            structure.supportingTerrain.RemoveAt(selectedTerrain);
            selectedTerrain = Mathf.Min(
                selectedTerrain,
                structure.supportingTerrain.Count - 1);
            MarkStructureDirty();
        }
        GUILayout.EndHorizontal();
    }

    private void DrawSelectedObject()
    {
        StructureObject item = structure!.objects[selectedObject];
        if (StructureItemData.IsItem(item))
        {
            DrawSelectedItem(item);
            return;
        }
        GUILayout.Label("Selected object");
        string prefab = LabeledTextField("Prefab", item.prefabName);
        string displayedX = Format(item.x), displayedY = Format(item.y);
        GUILayout.Label("Coordinates: press Enter to apply");
        string x = CoordinateTextField(item, "X", displayedX);
        string y = CoordinateTextField(item, "Y", displayedY);
        string z = CoordinateTextField(item, "Z", Format(item.z));
        if (prefab != item.prefabName)
        {
            string previous = item.prefabName;
            item.prefabName = prefab;
            if (!CheckPlacement(item, false, item)) item.prefabName = previous;
            else MarkStructureDirty();
        }
        // Display formatting is not an edit. In particular, turning native
        // rules back on must not snap a previously free-placed object.
        if (x != displayedX && TryFloat(x, out float nextX)) ChangeObjectPlacement(item, nextX, item.y);
        if (y != displayedY && TryFloat(y, out float nextY)) ChangeObjectPlacement(item, item.x, nextY);
        if (z != Format(item.z)) SetFloat(z, value => item.z = value, item.z);

        if (PrefabHasSign(item.prefabName) ||
            item.hasEditableSignMessage)
        {
            string currentMessage = item.hasEditableSignMessage
                ? item.signMessage
                : EditorPrefabInfo(item.prefabName).SignMessage;
            string nextMessage =
                ScrollableSignTextField(currentMessage);
            if (nextMessage != currentMessage)
            {
                item.signMessage = nextMessage;
                item.hasEditableSignMessage = true;
                MarkStructureDirty(geometryChanged: false, zonesChanged: false);
            }
        }

        if (item.prefabName.StartsWith(
                "prefab_tile_", StringComparison.OrdinalIgnoreCase) ||
            PrefabHasMapZone(item.prefabName) ||
            item.hasMapZoneName)
        {
            string currentZone =
                item.hasMapZoneName ? item.mapZoneName : "";
            string nextZone = LabeledTextField("Floor zone", currentZone);
            if (nextZone != currentZone)
            {
                item.mapZoneName = nextZone;
                item.hasMapZoneName = true;
                terrainBrushZone = nextZone;
                MarkStructureDirty(geometryChanged: false);
            }
        }

        DrawFurnitureCollision(item);

        bool nextRangeExtender = GUILayout.Toggle(
            item.npcInteractionRangeExtender,
            " Add NPC interaction range extender");
        if (nextRangeExtender != item.npcInteractionRangeExtender)
        {
            item.npcInteractionRangeExtender = nextRangeExtender;
            MarkStructureDirty(geometryChanged: false, zonesChanged: false);
        }

        int rotationCount = GetTurnableSpriteCount(item.prefabName);
        if (rotationCount > 0)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                $"Furniture rotation: {item.turnableIndex + 1}/{rotationCount}");
            if (GUILayout.Button("Previous", GUILayout.Width(75)))
            {
                ChangeObjectPlacement(item, item.x, item.y,
                    (item.turnableIndex - 1 + rotationCount) % rotationCount);
            }
            if (GUILayout.Button("Next", GUILayout.Width(55)))
            {
                ChangeObjectPlacement(item, item.x, item.y,
                    (item.turnableIndex + 1) % rotationCount);
            }
            GUILayout.EndHorizontal();
        }

        Sprite[] variants = item.turnableIndex >= 0
            ? Array.Empty<Sprite>()
            : GetSpriteVariants(item.prefabName);
        if (variants.Length > 1)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                $"Sprite: {item.spriteVariantIndex + 1}/{variants.Length}");
            if (GUILayout.Button("Previous", GUILayout.Width(75)))
            {
                item.spriteVariantIndex =
                    (item.spriteVariantIndex - 1 + variants.Length) %
                    variants.Length;
                item.lockSpriteVariant = true;
                MarkStructureDirty(geometryChanged: false, renderChanged: true, zonesChanged: false);
            }
            if (GUILayout.Button("Next", GUILayout.Width(55)))
            {
                item.spriteVariantIndex =
                    (item.spriteVariantIndex + 1) % variants.Length;
                item.lockSpriteVariant = true;
                MarkStructureDirty(geometryChanged: false, renderChanged: true, zonesChanged: false);
            }
            GUILayout.EndHorizontal();
        }

        DrawNudgeControls(item);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Duplicate"))
        {
            StructureObject copy = new()
            {
                prefabName = item.prefabName,
                x = item.x + 1,
                y = item.y,
                z = item.z,
                npcInteractionRangeExtender =
                    item.npcInteractionRangeExtender,
                turnableIndex = item.turnableIndex,
                spriteVariantIndex = item.spriteVariantIndex,
                lockSpriteVariant = item.lockSpriteVariant,
                hasEditableSignMessage =
                    item.hasEditableSignMessage,
                signMessage = item.signMessage,
                hasMapZoneName = item.hasMapZoneName,
                mapZoneName = item.mapZoneName,
                occupiedOffsets = (item.occupiedOffsets ??
                    new List<StructurePosition>())
                    .Select(offset => new StructurePosition
                        { x = offset.x, y = offset.y }).ToList(),
                components = (item.components ??
                              new List<StructureComponent>())
                    .Select(component => new StructureComponent
                    {
                        type = component.type,
                        hierarchyPath = component.hierarchyPath,
                        componentIndex = component.componentIndex,
                        dataBase64 = component.dataBase64
                    }).ToList()
            };
            PlaceEditorObject(copy, false);
        }
        if (GUILayout.Button("Delete"))
        {
            structure.objects.RemoveAt(selectedObject);
            selectedObject = Mathf.Min(selectedObject, structure.objects.Count - 1);
            MarkStructureDirty();
        }
        GUILayout.EndHorizontal();
    }

    private static bool PrefabHasSign(string prefabName)
        => EditorPrefabInfo(prefabName).HasSign;

    private static bool PrefabHasMapZone(string prefabName)
        => EditorPrefabInfo(prefabName).HasZone;

    private static string GetPrefabStringField(
        string prefabName, string fieldName, params Type[] componentTypes)
    {
        GameObject? prefab =
            Plugin.ResolvePrefab(prefabName);
        if (prefab == null)
            return "";
        foreach (Type type in componentTypes)
        {
            Component component = prefab.GetComponentInChildren(type, true);
            FieldInfo field = type.GetField(
                fieldName,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            if (component != null && field?.GetValue(component) is string value)
                return value;
        }

        return "";
    }

    private string LabeledTextField(string label, string value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(65));
        value = EditorTextField(value);
        GUILayout.EndHorizontal();
        return value;
    }

    private string ScrollableSignTextField(string value)
    {
        GUILayout.Label("Sign text");
        float contentWidth = 275f;
        if (measuredSignText != value || measuredSignSkin != GUI.skin)
        {
            measuredSignText = value; measuredSignSkin = GUI.skin;
            measuredSignHeight = Mathf.Max(52f, GUI.skin.textArea.CalcHeight(new GUIContent(value ?? ""), contentWidth));
        }
        float contentHeight = measuredSignHeight;
        signTextScroll = GUILayout.BeginScrollView(
            signTextScroll,
            alwaysShowHorizontal: false,
            alwaysShowVertical: contentHeight > 68f,
            GUILayout.Height(72f));
        value = GUILayout.TextArea(
            value ?? "",
            GUILayout.Width(contentWidth),
            GUILayout.Height(contentHeight));
        GUILayout.EndScrollView();
        return value;
    }

    private void SetFloat(string text, Action<float> set, float oldValue)
    {
        if (TryFloat(text, out float value) && !Mathf.Approximately(value, oldValue))
        {
            set(value);
            // This control edits Z only: order changes, XY occupancy does not.
            MarkStructureDirty(geometryChanged: false, renderChanged: true, zonesChanged: false);
        }
    }

    private void RefreshFiles()
    {
        Directory.CreateDirectory(Plugin.StructuresDirectory);
        files.Clear();
        files.AddRange(Directory.GetFiles(Plugin.StructuresDirectory, "*.json")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
        fileLabels.Clear();
        fileLabels.AddRange(files.Select(Path.GetFileNameWithoutExtension));
        checkedFiles.RemoveWhere(path => !files.Contains(
            path, StringComparer.OrdinalIgnoreCase));
        status = $"{files.Count} structure file(s) found.";
    }

    private void BuildPlacementCatalog()
    {
        placementCatalog.Clear();
        catalogMetadata.Clear();
        lastCatalogTab = -1;
        try
        {
            placementCatalog.AddRange(
                Plugin.GetKnownPrefabNames()
                    .Where(name =>
                    {
                        if (IsUnsafeQuickPlaceName(name))
                            return false;
                        GameObject? prefab =
                            Plugin.ResolvePrefab(name);
                        return prefab != null &&
                               prefab.GetComponent<EntityMover>() == null &&
                               prefab.GetComponent<NeuralNPC>() == null &&
                               prefab.GetComponent<Enemy>() == null &&
                               prefab.GetComponentsInChildren<SpriteRenderer>(
                                   includeInactive: true)
                                   .Any(renderer => renderer.sprite != null);
                    })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(CatalogDisplayName, StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception exception)
        {
            Plugin.Log.LogError(
                "Could not build the editor placement catalog: " + exception);
            status = "Could not build the quick-place catalog.";
        }
        foreach (var name in placementCatalog)
            catalogMetadata[name] = (CatalogDisplayName(name), GetQuickPlaceCategory(name));
    }

    private static string CatalogDisplayName(string prefabName)
    {
        if (prefabName.Equals(RailingPrefab.Name, StringComparison.OrdinalIgnoreCase))
            return "wooden bridge railing";
        string value = prefabName.StartsWith(
                "prefab_", StringComparison.OrdinalIgnoreCase)
            ? prefabName.Substring("prefab_".Length)
            : prefabName;
        return value.Replace('_', ' ');
    }

    private static int GetDefaultTurnableIndex(string prefabName)
    {
        Turnable? turnable =
            Plugin.ResolvePrefab(prefabName)
                ?.GetComponentInChildren<Turnable>(true);
        return turnable == null ? -1 : turnable.GetIndex();
    }

    private static int GetTurnableSpriteCount(string prefabName)
        => EditorPrefabInfo(prefabName).Rotations;

    internal static List<StructurePosition> GetPrefabOccupiedOffsets(
        string prefabName, int turnableIndex)
    {
        var key = (prefabName, turnableIndex);
        if (!sharedFootprints.TryGetValue(key, out var footprint))
            sharedFootprints[key] = footprint = BuildPrefabOccupiedOffsets(prefabName, turnableIndex);
        return footprint;
    }

    private static List<StructurePosition> BuildPrefabOccupiedOffsets(
        string prefabName, int turnableIndex)
    {
        GameObject? prefab = Plugin.ResolvePrefab(prefabName);
        Turnable? turnable =
            prefab?.GetComponentInChildren<Turnable>(true);
        BoxCollider2D? box =
            prefab?.GetComponentInChildren<BoxCollider2D>(true);
        if (box != null)
        {
            Vector2 min = box.offset - box.size * 0.5f;
            Vector2 max = box.offset + box.size * 0.5f;
            List<Vector2Int> cells = new();
            // Tile anchors are integer coordinates. Treat the collider's lower
            // edge as inclusive and upper edge as exclusive so centered
            // even-sized colliders cover the expected number of cells.
            for (int x = Mathf.CeilToInt(min.x - 0.001f);
                 x < Mathf.CeilToInt(max.x - 0.001f); x++)
            for (int y = Mathf.CeilToInt(min.y - 0.001f);
                 y < Mathf.CeilToInt(max.y - 0.001f); y++)
                cells.Add(new Vector2Int(x, y));
            if (cells.Count > 0)
            {
                int turns = turnable != null &&
                            turnable.sprites?.Length == 4
                    ? (turnableIndex - turnable.GetIndex() + 4) % 4
                    : 0;
                for (int turn = 0; turn < turns; turn++)
                    cells = cells.Select(cell =>
                        new Vector2Int(-cell.y, cell.x)).ToList();
                if (!cells.Contains(Vector2Int.zero))
                    cells.Add(Vector2Int.zero);
                return cells.Distinct().Select(cell =>
                    new StructurePosition { x = cell.x, y = cell.y }).ToList();
            }
        }
        if (turnable == null || turnable.sprites == null ||
            turnable.sprites.Length == 0)
            return new List<StructurePosition>
                { new() { x = 0, y = 0 } };

        int index = (turnableIndex % turnable.sprites.Length +
                     turnable.sprites.Length) % turnable.sprites.Length;
        Bounds bounds = turnable.sprites[index].bounds;
        int minX = Mathf.FloorToInt(bounds.min.x + 0.001f);
        int maxX = Mathf.CeilToInt(bounds.max.x - 0.001f) - 1;
        int minY = Mathf.FloorToInt(bounds.min.y + 0.001f);
        int maxY = Mathf.CeilToInt(bounds.max.y - 0.001f) - 1;
        List<StructurePosition> result = new();
        for (int x = minX; x <= maxX; x++)
        for (int y = minY; y <= maxY; y++)
            result.Add(new StructurePosition { x = x, y = y });
        if (!result.Any(offset => offset.x == 0 && offset.y == 0))
            result.Add(new StructurePosition { x = 0, y = 0 });
        return result;
    }

    internal static HashSet<Vector2Int> GetObjectCells(StructureObject item)
    {
        int anchorX = Mathf.RoundToInt(item.x);
        int anchorY = Mathf.RoundToInt(item.y);
        List<StructurePosition> offsets =
            item.occupiedOffsets != null && item.occupiedOffsets.Count > 0
                ? item.occupiedOffsets
                : new List<StructurePosition> { new() };
        return offsets.Select(offset =>
            new Vector2Int(anchorX + offset.x, anchorY + offset.y)).ToHashSet();
    }

    private static bool IsQuickPlaceSupportingTerrain(string prefabName)
    {
        return prefabName.StartsWith(
            "prefab_tile_", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetQuickPlaceCategory(string prefabName)
    {
        GameObject? prefab = Plugin.ResolvePrefab(prefabName);
        if (IsStructuralQuickPlace(prefabName, prefab))
            return 1;

        if (prefabName.ContainsAnyIgnoreCase(
                "tree", "grass", "bush", "plant", "crop", "flower",
                "herb", "mushroom", "rock", "boulder", "ore", "stump",
                "log", "reed", "vine", "cactus", "sapling"))
            return 3;

        if (prefab != null &&
            (prefab.GetComponent<Tree>() != null ||
             prefab.GetComponent<ResourceNode>() != null))
            return 3;

        if (IsQuickPlaceSupportingTerrain(prefabName))
            return 0;

        if (prefabName.ContainsAnyIgnoreCase(
                "furniture", "chair", "table", "bench", "bed", "bath",
                "sofa", "couch", "shelf", "cabinet", "counter", "stool",
                "desk", "wardrobe", "dresser", "rug", "carpet", "towel",
                "candle", "lantern", "lamp", "decoration", "decor"))
            return 2;

        return 4;
    }

    private static bool IsStructuralQuickPlace(
        string prefabName, GameObject? prefab)
    {
        if (prefabName.StartsWith(
                "prefab_wall_", StringComparison.OrdinalIgnoreCase) ||
            prefabName.ContainsAnyIgnoreCase(
                "door", "window", "railing", "fence", "gate", "roof",
                "stair", "bridge", "foundation", "pillar", "column"))
            return true;

        if (prefabName.StartsWith(
                "prefab_tile_", StringComparison.OrdinalIgnoreCase) &&
            prefabName.ContainsAnyIgnoreCase(
                "floor", "wood", "plank", "stone", "brick", "cobble",
                "concrete", "metal", "marble", "slate", "carpet"))
            return true;

        return prefab != null &&
               (prefab.GetComponentInChildren<Door>(true) != null ||
                prefab.GetComponentInChildren<Window>(true) != null);
    }

    private static float GetQuickPlaceDefaultZ(string prefabName)
    {
        GameObject? prefab =
            Plugin.ResolvePrefab(prefabName);
        bool isWallDoorOrWindow =
            prefabName.StartsWith(
                "prefab_wall_", StringComparison.OrdinalIgnoreCase) ||
            prefabName.ContainsAnyIgnoreCase("door", "window") ||
            (prefab != null &&
             (prefab.GetComponentInChildren<Door>(true) != null ||
              prefab.GetComponentInChildren<Window>(true) != null));
        return isWallDoorOrWindow ? 0.5f : 1f;
    }

    private static bool IsUnsafeQuickPlaceName(string prefabName)
    {
        return string.IsNullOrWhiteSpace(prefabName) ||
               prefabName.Equals("prefab_item", StringComparison.OrdinalIgnoreCase) ||
               prefabName.ContainsAnyIgnoreCase(
                   "player", "npc_", "enemy", "monster",
                   "projectile", "particle", "effect_", "vfx", "sfx",
                   "audio", "music", "camera", "cursor", "selector",
                   "marker", "spawner", "spawnpoint", "trigger",
                   "cutscene", "dialog", "quest", "ui_", "canvas");
    }

    private void Load(string path)
    {
        if (!AllowDiscard(() => Load(path))) return;
        try
        {
            StructureFile loaded =
                ReadEditableFile(path, out int normalizedTiles);
            ClearItemEditor();
            StopPlanningBrush();
            StopZoneBrush();
            structure = loaded;
            InvalidateRenderOrder();
            currentPath = path;
            saveAsName = Path.GetFileNameWithoutExtension(path);
            selectedObject = -1;
            selectedTerrain = -1;
            movementReferenceSelection = -1;
            previewPan = Vector2.zero;
            previewOrigin = CalculateStructureCenter();
            Vector2 topLeft = GetTopLeftCoordinate();
            offsetX = Format(topLeft.x);
            offsetY = Format(topLeft.y);
            dirty = normalizedTiles > 0;
            status = $"Loaded {loaded.objects.Count} objects and " +
                     $"{loaded.supportingTerrain.Count} terrain records." +
                     (normalizedTiles > 0
                         ? $" Normalized {normalizedTiles} tile object(s) to terrain."
                         : "");
            ResetHistory();
        }
        catch (Exception exception)
        {
            status = "Could not load file: " + exception.Message;
            Plugin.Log.LogError(exception);
        }
    }

    private void CombineCheckedFiles()
    {
        if (!AllowDiscard(CombineCheckedFiles)) return;
        ClearItemEditor();

        try
        {
            List<StructureFile> sources = checkedFiles
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => ReadEditableFile(path, out _))
                .ToList();

            List<StructureObject> objects = sources
                .SelectMany(source =>
                    source.objects ?? new List<StructureObject>())
                .GroupBy(GetObjectMergeKey, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();
            List<SupportingTerrain> terrain = sources
                .SelectMany(source =>
                    source.supportingTerrain ??
                    new List<SupportingTerrain>())
                .GroupBy(GetTerrainMergeKey, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();

            structure = new StructureFile
            {
                formatVersion = 3,
                name = "Combined Structure",
                gameVersion = Application.version,
                exportedUtc = DateTime.UtcNow.ToString("O"),
                objects = objects,
                supportingTerrain = terrain,
                planningTiles = PlanningTiles.Normalize(sources.SelectMany(source => source.planningTiles)),
                occupiedPositions = new List<StructurePosition>(),
                serializedObjectsBase64 = ""
            };
            StopPlanningBrush();
            StopZoneBrush();
            InvalidateRenderOrder();
            currentPath = "";
            saveAsName = "Combined Structure";
            selectedObject = -1;
            selectedTerrain = -1;
            movementReferenceSelection = -1;
            previewPan = Vector2.zero;
            previewOrigin = CalculateStructureCenter();
            Vector2 topLeft = GetTopLeftCoordinate();
            offsetX = Format(topLeft.x);
            offsetY = Format(topLeft.y);
            MarkStructureDirty();
            ResetHistory();
            status =
                $"Combined {sources.Count} JSON files into " +
                $"{objects.Count} objects and {terrain.Count} terrain records. " +
                "Choose Save As to create the shared JSON.";
        }
        catch (Exception exception)
        {
            status = "Could not combine files: " + exception.Message;
            Plugin.Log.LogError(exception);
        }
    }

    private static StructureFile ReadEditableFile(
        string path, out int normalizedTiles)
    {
        object? deserialized = StringSerializationAPI.Deserialize(
            typeof(StructureFile), File.ReadAllText(path));
        if (deserialized is not StructureFile file)
            throw new InvalidDataException(
                $"Could not read {Path.GetFileName(path)} as a structure file.");
        if (file.formatVersion != 3)
            throw new InvalidDataException(
                "Only readable format-3 files can be edited safely.");
        file.objects ??= new List<StructureObject>();
        file.supportingTerrain ??= new List<SupportingTerrain>();
        file.occupiedPositions ??= new List<StructurePosition>();
        file.planningTiles = PlanningTiles.Normalize(file.planningTiles);
        normalizedTiles =
            StructureFileNormalizer.MoveTileObjectsToTerrain(file);
        foreach (var item in file.objects)
            if (string.IsNullOrWhiteSpace(item.prefabName) ||
                !StructureAlgorithms.IsFinite(item.x) || !StructureAlgorithms.IsFinite(item.y) ||
                !StructureAlgorithms.IsFinite(item.z))
                throw new InvalidDataException("An object contains an invalid prefab name or coordinate.");
        foreach (var item in file.supportingTerrain)
            if (string.IsNullOrWhiteSpace(item.prefabName) || !StructureAlgorithms.IsFinite(item.z))
                throw new InvalidDataException("Terrain contains an invalid prefab name or coordinate.");
        return file;
    }

    private static string GetObjectMergeKey(StructureObject item)
    {
        string componentData = string.Join(
            ";",
            (item.components ?? new List<StructureComponent>())
                .Select(component =>
                    component.type + ":" +
                    component.hierarchyPath + ":" +
                    component.componentIndex + ":" +
                    component.dataBase64));
        return string.Join("|",
            item.prefabName,
            item.x.ToString("R", CultureInfo.InvariantCulture),
            item.y.ToString("R", CultureInfo.InvariantCulture),
            item.z.ToString("R", CultureInfo.InvariantCulture),
            item.npcInteractionRangeExtender,
            item.turnableIndex,
            item.spriteVariantIndex,
            item.hasEditableSignMessage,
            item.signMessage,
            item.hasMapZoneName,
            item.mapZoneName,
            componentData);
    }

    private static string GetTerrainMergeKey(SupportingTerrain item)
    {
        string componentData = string.Join(
            ";",
            (item.components ?? new List<StructureComponent>())
                .Select(component =>
                    component.type + ":" +
                    component.hierarchyPath + ":" +
                    component.componentIndex + ":" +
                    component.dataBase64));
        return string.Join("|",
            item.prefabName,
            item.x,
            item.y,
            item.z.ToString("R", CultureInfo.InvariantCulture),
            item.spriteVariantIndex,
            item.hasMapZoneName,
            item.mapZoneName,
            componentData);
    }

    private void CreateBlankStructure()
    {
        if (!AllowDiscard(CreateBlankStructure)) return;
        ClearItemEditor();

        string baseName = "New Structure";
        string path = Path.Combine(
            Plugin.StructuresDirectory, baseName + ".json");
        int suffix = 2;
        while (File.Exists(path))
        {
            path = Path.Combine(
                Plugin.StructuresDirectory,
                $"{baseName} {suffix++}.json");
        }

        structure = new StructureFile
        {
            formatVersion = 3,
            name = Path.GetFileNameWithoutExtension(path),
            gameVersion = Application.version,
            exportedUtc = DateTime.UtcNow.ToString("O"),
            occupiedPositions = new List<StructurePosition>(),
            supportingTerrain = new List<SupportingTerrain>(),
            objects = new List<StructureObject>(),
            serializedObjectsBase64 = ""
        };
        StopPlanningBrush();
        StopZoneBrush();
        InvalidateRenderOrder();
        currentPath = path;
        saveAsName = structure.name;
        selectedObject = -1;
        selectedTerrain = -1;
        movementReferenceSelection = -1;
        previewPan = Vector2.zero;
        previewOrigin = Vector2.zero;
        offsetX = "0";
        offsetY = "0";
        MarkStructureDirty();
        Save(path);
        ResetHistory();
        RefreshFiles();
        status =
            $"Created blank structure {Path.GetFileName(path)}. " +
            "Use Quick Place to begin building.";
    }

    private void AddObject()
    {
        if (structure == null || string.IsNullOrWhiteSpace(addPrefabName) ||
            !TryFloat(addX, out float x) ||
            !TryFloat(addY, out float y) ||
            !TryFloat(addZ, out float z))
        {
            status = "Enter a prefab name and valid X, Y, and Z numbers.";
            return;
        }

        if (IsQuickPlaceSupportingTerrain(addPrefabName.Trim()))
        {
            if (!StructureAlgorithms.IsGridTranslation(x, y))
            {
                status = "Terrain requires whole-number X and Y coordinates.";
                return;
            }
            string previousBrush = brushPrefabName;
            brushPrefabName = addPrefabName.Trim();
            PlaceBrushObject(new Vector2Int(Mathf.RoundToInt(x), Mathf.RoundToInt(y)));
            brushPrefabName = previousBrush;
            return;
        }

        PlaceEditorObject(new StructureObject
        {
            prefabName = addPrefabName.Trim(),
            x = x,
            y = y,
            z = z,
            npcInteractionRangeExtender =
                addNpcInteractionRangeExtender,
            turnableIndex = GetDefaultTurnableIndex(addPrefabName.Trim()),
            occupiedOffsets = GetPrefabOccupiedOffsets(
                addPrefabName.Trim(),
                GetDefaultTurnableIndex(addPrefabName.Trim()))
        }, true);
    }

    private void MoveToCoordinate()
    {
        if (structure == null ||
            !TryFloat(offsetX, out float targetX) ||
            !TryFloat(offsetY, out float targetY))
        {
            status = "Enter valid destination coordinates.";
            return;
        }

        Vector2 reference = GetMovementReferenceCoordinate();
        float x = targetX - reference.x;
        float y = targetY - reference.y;
        if (!StructureAlgorithms.IsFinite(x) || !StructureAlgorithms.IsFinite(y) ||
            structure.objects.Any(item => Math.Abs(item.x + x) > 10000000 || Math.Abs(item.y + y) > 10000000) ||
            structure.supportingTerrain.Any(item => Math.Abs(item.x + x) > 10000000 || Math.Abs(item.y + y) > 10000000) ||
            structure.planningTiles.Any(item => Math.Abs(item.x + x) > 10000000 || Math.Abs(item.y + y) > 10000000))
        {
            status = "The destination is outside the supported coordinate range.";
            return;
        }
        if ((structure.supportingTerrain.Count > 0 || structure.planningTiles.Count > 0 ||
             structure.objects.Any(item => GetPlacementSlot(item.prefabName) != PlacementSlot.None)) &&
            !StructureAlgorithms.IsGridTranslation(x, y))
        {
            status = "This structure contains ground, walls or planning cells. Use an integer tile displacement; " +
                $"nearest aligned target: {Format(reference.x + Mathf.Round(x))}, {Format(reference.y + Mathf.Round(y))}.";
            return;
        }
        // Even an integer translation can change RoundToInt at half-cell
        // anchors (banker's rounding). Validate the proposed document first.
        var translated = new StructureFile
        {
            objects = structure.objects.Where(item => GetPlacementSlot(item.prefabName) != PlacementSlot.None)
                .Select(item => new StructureObject { prefabName = item.prefabName,
                    x = item.x + x, y = item.y + y, occupiedOffsets = item.occupiedOffsets }).ToList(),
            supportingTerrain = structure.supportingTerrain.Select(item => new SupportingTerrain
                { prefabName = item.prefabName, x = item.x + Mathf.RoundToInt(x), y = item.y + Mathf.RoundToInt(y) }).ToList()
        };
        string? conflict = StructurePlacementPolicy.FindConflict(translated, GetPlacementSlot);
        if (conflict != null) { status = "Cannot move: " + conflict; return; }
        foreach (StructureObject item in structure.objects)
        {
            item.x += x;
            item.y += y;
        }
        foreach (SupportingTerrain item in structure.supportingTerrain)
        {
            item.x += Mathf.RoundToInt(x);
            item.y += Mathf.RoundToInt(y);
        }
        foreach (PlanningTile item in structure.planningTiles)
        {
            item.x += Mathf.RoundToInt(x);
            item.y += Mathf.RoundToInt(y);
        }
        MarkStructureDirty();
        status =
            $"Moved the structure reference to {Format(targetX)}, {Format(targetY)}.";
    }

    private void SaveAs()
    {
        if (structure == null || string.IsNullOrWhiteSpace(saveAsName) ||
            saveAsName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            status = "Enter a valid Save As name.";
            return;
        }
        string path = Path.Combine(Plugin.StructuresDirectory, saveAsName.Trim() + ".json");
        if (File.Exists(path) && !string.Equals(path, currentPath, StringComparison.OrdinalIgnoreCase))
        {
            pendingOverwritePath = path;
            return;
        }
        Save(path);
        RefreshFiles();
    }

    private void Save(string path)
    {
        if (structure == null || string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            string? conflict = PlacementConflict();
            if (conflict != null) { status = "Cannot save: " + conflict; return; }
            structure.name = Path.GetFileNameWithoutExtension(path);
            structure.occupiedPositions = structure.objects
                .SelectMany(GetObjectCells)
                .Concat(structure.supportingTerrain.Select(
                    item => new Vector2Int(item.x, item.y)))
                .Distinct()
                .OrderBy(item => item.x)
                .ThenBy(item => item.y)
                .Select(item => new StructurePosition { x = item.x, y = item.y })
                .ToList();
            structure.serializedObjectsBase64 = "";
            StructureJsonFile.Write(
                path,
                StringSerializationAPI.Serialize(typeof(StructureFile), structure));
            currentPath = path;
            dirty = false;
            history.Record(structure);
            savedSnapshot = history.Current;
            historyDirty = false;
            status = "Saved " + Path.GetFileName(path) + ".";
        }
        catch (Exception exception)
        {
            status = "Could not save: " + exception.Message;
            Plugin.Log.LogError(exception);
        }
    }

    private static string Format(float value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private Vector2 GetTopLeftCoordinate()
    {
        EnsureEditorGeometry();
        return cachedTopLeft;
    }

    private Vector2 GetMovementReferenceCoordinate()
    {
        if (structure != null &&
            selectedObject >= 0 &&
            selectedObject < structure.objects.Count)
        {
            StructureObject selected = structure.objects[selectedObject];
            return new Vector2(selected.x, selected.y);
        }
        if (structure != null &&
            selectedTerrain >= 0 &&
            selectedTerrain < structure.supportingTerrain.Count)
        {
            SupportingTerrain selected =
                structure.supportingTerrain[selectedTerrain];
            return new Vector2(selected.x, selected.y);
        }

        return GetTopLeftCoordinate();
    }

    private static bool TryFloat(string value, out float result) =>
        float.TryParse(
            value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) &&
        StructureAlgorithms.IsFinite(result);
}
