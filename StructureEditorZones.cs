#nullable enable
using UnityEngine;

namespace StructureHandler;

internal sealed partial class StructureEditorUI
{
    private bool zoneBrush;
    private readonly ZonePaintStroke zoneStroke = new();
    private int zoneStrokeChanges;

    private void StopZoneBrush()
    {
        if (zoneBrush) brushStroke = false;
        zoneBrush = false;
        zoneStroke.End();
    }

    private void DrawZoneBrushControls()
    {
        terrainBrushZone = LabeledTextField("Zone", terrainBrushZone);
        bool next = GUILayout.Toggle(zoneBrush, "Paint Zones", GUI.skin.button);
        if (next != zoneBrush)
        {
            if (!next) StopZoneBrush();
            else
            {
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
