#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace StructureHandler;

internal sealed partial class StructureEditorUI
{
    private bool planningBrush;
    private bool planningEraser;
    private bool showPlanning = true;
    private string planningColor = "#FFD54F";
    private string customPlanningColor = "#FFD54F";
    private Vector2Int? lastPlanningCell;
    private readonly Dictionary<Vector2Int, PlanningTile> planningIndex = new();
    private readonly Dictionary<Vector2Int, Color> planningColors = new();
    private int transitionOverlapCount;
    private GUIStyle? planningTextStyle;
    private static readonly string[] PlanningPalette =
        { "#FFD54F", "#FF5252", "#FF9F43", "#66DD88", "#55CCEE", "#AA88FF", "#FF88BB", "#FFFFFF" };
    private static readonly string[] PlanningPaletteNames =
        { "Yellow", "Red", "Orange", "Green", "Cyan", "Purple", "Pink", "White" };

    private void StopPlanningBrush()
    {
        planningBrush = false;
        lastPlanningCell = null;
    }

    private void StartPlanningBrush(bool erase)
    {
        planningBrush = true;
        planningEraser = erase;
        showPlanning = true;
        deleteBrush = false;
        brushPrefabName = "";
        lastBrushCell = null;
        lastPlanningCell = null;
    }

    private void DrawPlanningTab()
    {
        GUILayout.Label("Planning colors (editor only)");
        PlanningLabel("Left-click and drag to mark cells. Right-click cancels. Middle-drag pans.");
        showPlanning = GUILayout.Toggle(showPlanning, " Show planning colors");
        for (int row = 0; row < 4; row++)
        {
            GUILayout.BeginHorizontal();
            for (int column = 0; column < 2; column++)
            {
                int index = row * 2 + column;
                ColorUtility.TryParseHtmlString(PlanningPalette[index], out Color color);
                Color old = GUI.backgroundColor;
                GUI.backgroundColor = color;
                if (GUILayout.Toggle(planningBrush && !planningEraser && planningColor == PlanningPalette[index],
                        PlanningPaletteNames[index], GUI.skin.button))
                {
                    if (!planningBrush || planningEraser || planningColor != PlanningPalette[index])
                    {
                        planningColor = customPlanningColor = PlanningPalette[index];
                        StartPlanningBrush(false);
                    }
                }
                GUI.backgroundColor = old;
            }
            GUILayout.EndHorizontal();
        }
        GUILayout.Label("Custom color (#RRGGBB)");
        customPlanningColor = GUILayout.TextField(customPlanningColor, 7);
        if (GUILayout.Button("Paint custom color"))
        {
            if (PlanningTiles.TryColor(customPlanningColor, out string color))
            {
                planningColor = color;
                StartPlanningBrush(false);
            }
            else status = "Enter six hexadecimal digits, for example #FFD54F.";
        }
        if (GUILayout.Button(planningBrush && planningEraser ? "Eraser active" : "Erase planning colors"))
            StartPlanningBrush(true);
        if (GUILayout.Button("Stop planning brush")) StopPlanningBrush();
        if (GUILayout.Button("Clear all planning colors"))
        {
            if (structure!.planningTiles.Count > 0)
            {
                structure.planningTiles.Clear();
                MarkStructureDirty();
                status = "Planning colors cleared. Undo restores them.";
            }
        }
        PlanningLabel("Colors are saved in planningTiles in the JSON. They never change game terrain or objects.");
        PlanningLabel("Blue outlined cells mark normal world-transition positions; these warnings cannot be erased.");
        DrawMoveEntireStructure();
    }

    private void PlanningLabel(string text)
    {
        planningTextStyle ??= new GUIStyle(GUI.skin.label) { wordWrap = true };
        GUILayout.Label(text, planningTextStyle, GUILayout.MaxWidth(282));
    }

    private bool HandlePlanningInput(Vector2 center)
    {
        if (!planningBrush) return false;
        Event current = Event.current;
        if (current.type == EventType.MouseDown && current.button == 1)
        {
            StopPlanningBrush();
            status = "Planning brush cancelled.";
            current.Use();
        }
        else if ((current.type == EventType.MouseDown || current.type == EventType.MouseDrag) && current.button == 0)
        {
            Vector2Int cell = PreviewToWorldCell(center, current.mousePosition);
            // Never bridge a stroke across a mouse-up, a pan, or GUI controls.
            Vector2Int from = current.type == EventType.MouseDown ? cell : lastPlanningCell ?? cell;
            EnsureEditorGeometry();
            bool changed = false;
            ColorUtility.TryParseHtmlString(planningColor, out Color color);
            foreach (var point in PlanningTiles.Stroke(from.x, from.y, cell.x, cell.y))
            {
                if (System.Math.Abs((long)point.X) > 10000000 || System.Math.Abs((long)point.Y) > 10000000) continue;
                Vector2Int key = new(point.X, point.Y);
                bool exists = planningIndex.TryGetValue(key, out var mark);
                if (planningEraser)
                {
                    if (!exists) continue;
                    structure!.planningTiles.Remove(mark!);
                    planningIndex.Remove(key);
                    planningColors.Remove(key);
                }
                else
                {
                    if (exists && mark!.color == planningColor) continue;
                    if (!exists)
                    {
                        mark = new PlanningTile { x = key.x, y = key.y };
                        structure!.planningTiles.Add(mark);
                        planningIndex[key] = mark;
                    }
                    mark!.color = planningColor;
                    planningColors[key] = color;
                }
                changed = true;
            }
            if (changed) { brushStroke = true; MarkStructureDirty(); }
            lastPlanningCell = cell;
            current.Use();
        }
        return true;
    }

    private void DrawPlanningOverlay(Rect preview, Vector2 center)
    {
        Vector2 origin = GetStructureCenter();
        int left = Mathf.CeilToInt(origin.x + (preview.xMin - center.x) / zoom - 0.5f);
        int right = Mathf.FloorToInt(origin.x + (preview.xMax - center.x) / zoom + 0.5f);
        int bottom = Mathf.CeilToInt(origin.y - (preview.yMax - center.y) / zoom - 0.5f);
        int top = Mathf.FloorToInt(origin.y - (preview.yMin - center.y) / zoom + 0.5f);
        Color old = GUI.color;
        // Work is bounded by the visible viewport, not document size. Draw after
        // sprites so neither a wall nor a floor can hide a planning/warning cell.
        for (int x = left; x <= right; x++)
        for (int y = bottom; y <= top; y++)
        {
            Vector2Int cell = new(x, y);
            bool transition = PlanningTiles.IsTransitionCell(x, y);
            bool painted = showPlanning && planningColors.ContainsKey(cell);
            if (!transition && !painted) continue;
            Vector2 point = WorldToPreview(center, x, y);
            Rect square = new(point.x - zoom / 2, point.y - zoom / 2, zoom, zoom);
            if (painted)
            {
                Color tint = planningColors[cell]; tint.a = 0.38f;
                GUI.color = tint;
                GUI.DrawTexture(square, Texture2D.whiteTexture);
            }
            if (transition)
            {
                GUI.color = new Color(0.08f, 0.4f, 1f, cellIndex.ContainsKey(cell) ? 0.5f : 0.28f);
                GUI.DrawTexture(square, Texture2D.whiteTexture);
                GUI.color = new Color(0.2f, 0.65f, 1f, 0.95f);
                const float edge = 1.5f;
                GUI.DrawTexture(new Rect(square.x, square.y, square.width, edge), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(square.x, square.yMax - edge, square.width, edge), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(square.x, square.y, edge, square.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(square.xMax - edge, square.y, edge, square.height), Texture2D.whiteTexture);
            }
        }
        GUI.color = old;
    }
}
