#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace StructureHandler;

internal sealed partial class StructureEditorUI
{
    private bool zoneBrush;
    private readonly ZonePaintStroke zoneStroke = new();
    private int zoneStrokeChanges;
    private bool showZoneColors;
    private int zoneColorRevision = -1;
    private int zoneRevision;
    private StructureFile? zoneColorStructure;
    private readonly Dictionary<(int X, int Y), Color> zoneColors = new();
    private readonly List<(int X, int Y, Color Color)> visibleZones = new();
    private (int Left, int Right, int Bottom, int Top) zoneViewport;
    private bool zoneViewportValid;

    private void ClearZoneColors()
    {
        zoneColors.Clear();
        visibleZones.Clear(); zoneViewportValid = false;
        zoneColorStructure = null;
        zoneColorRevision = -1;
    }

    private void DrawZoneColors(Rect preview, Vector2 center)
    {
        if (!showZoneColors || structure == null) return;
        if (!ReferenceEquals(zoneColorStructure, structure) || zoneColorRevision != zoneRevision)
        {
            zoneColors.Clear();
            zoneViewportValid = false;
            foreach (var pair in ZonePreview.Build(structure))
            {
                Color color = Color.HSVToRGB(ZonePreview.Hue(pair.Value), 0.65f, 1f);
                color.a = 0.25f;
                zoneColors[pair.Key] = color;
            }
            zoneColorStructure = structure;
            zoneColorRevision = zoneRevision;
        }
        // Visit only visible cells. Panning/zooming never rebuilds zone data.
        Vector2 origin = GetStructureCenter();
        int left = Mathf.CeilToInt(origin.x + (preview.xMin - center.x) / zoom - 0.5f);
        int right = Mathf.FloorToInt(origin.x + (preview.xMax - center.x) / zoom + 0.5f);
        int bottom = Mathf.CeilToInt(origin.y - (preview.yMax - center.y) / zoom - 0.5f);
        int top = Mathf.FloorToInt(origin.y - (preview.yMin - center.y) / zoom + 0.5f);
        var viewport = (left, right, bottom, top);
        if (!zoneViewportValid || !zoneViewport.Equals(viewport))
        {
            visibleZones.Clear();
            for (int x = left; x <= right; x++)
            for (int y = bottom; y <= top; y++)
                if (zoneColors.TryGetValue((x, y), out Color color)) visibleZones.Add((x, y, color));
            zoneViewport = viewport; zoneViewportValid = true;
        }
        foreach (var entry in visibleZones) DrawTile(preview, center, entry.X, entry.Y, entry.Color);
    }

    private void StopZoneBrush()
    {
        if (zoneBrush) brushStroke = false;
        zoneBrush = false;
        zoneStroke.End();
    }

    private void DrawZoneBrushControls()
    {
        showZoneColors = GUILayout.Toggle(showZoneColors, "Show Zone Colors");
        terrainBrushZone = LabeledTextField("Zone", terrainBrushZone);
        bool next = GUILayout.Toggle(zoneBrush, "Paint Zones", GUI.skin.button);
        if (next != zoneBrush)
        {
            if (!next) StopZoneBrush();
            else
            {
                itemBrush = false;
                StopPlanningBrush();
                deleteBrush = false;
                brushPrefabName = "";
                lastBrushCell = null;
                zoneStroke.End();
                zoneBrush = true;
                status = "Zone brush enabled: left-click/drag existing terrain or floors; right-click cancels.";
            }
        }
        PlanningLabel("Paint Zones renames existing terrain/floors without replacing them. Empty cells are skipped. A blank name clears the zone name.");
    }

    private bool HandleZoneInput(Vector2 center)
    {
        if (!zoneBrush) return false;
        Event current = Event.current;
        if (current.type == EventType.MouseDown && current.button == 1)
        {
            StopZoneBrush();
            status = "Zone brush cancelled.";
            current.Use();
        }
        else if ((current.type == EventType.MouseDown || current.type == EventType.MouseDrag) && current.button == 0)
        {
            bool begin = current.type == EventType.MouseDown;
            if (begin) zoneStrokeChanges = 0;
            Vector2Int cell = PreviewToWorldCell(center, current.mousePosition);
            EnsureEditorGeometry();
            int changed = 0;
            foreach (var point in zoneStroke.Trace(cell.x, cell.y, begin))
                if (cellIndex.TryGetValue(new Vector2Int(point.X, point.Y), out var entries))
                    changed += ZonePainting.Apply(structure!, entries, terrainBrushZone);
            if (changed > 0)
            {
                brushStroke = true;
                // Zone names don't change geometry or sprite draw order.
                MarkStructureDirty(geometryChanged: false);
                zoneStrokeChanges += changed;
            }
            status = zoneStrokeChanges == 0
                ? "No terrain/floor zone changes in this stroke."
                : $"Updated {zoneStrokeChanges} terrain/floor record(s) to zone " +
                  (terrainBrushZone.Length == 0 ? "(blank)." : $"\"{terrainBrushZone}\".") +
                  " Undo restores this stroke.";
            current.Use();
        }
        return true;
    }
}
