#nullable enable
using System;
using UnityEngine;

namespace StructureHandler;

internal sealed partial class StructureEditorUI
{
    private static readonly Direction[] DoorDirections = { Direction.Up, Direction.Down, Direction.Left, Direction.Right };
    private static readonly string[] DoorLabels = { "North / Up (+Y)", "South / Down (-Y)", "West / Left (-X)", "East / Right (+X)" };

    private void DrawDoorAccess(StructureObject item)
    {
        var info = EditorPrefabInfo(item.prefabName);
        if (info.Door == null) return;
        var saved = DoorAccessData.Saved(item);
        GUILayout.Label("Door: public approach side");
        try
        {
            if (saved == null && info.DoorDefault == null)
            {
                info.DoorDefault = ""; // do not retry a broken template every repaint
                info.DoorDefault = DoorAccessData.FromTemplate(info.Door);
            }
            string source = (saved == null ? info.DoorDefault : saved.dataBase64) ?? "";
            if (!ReferenceEquals(source, info.LastDoorPayload))
            {
                info.LastDoorPayload = source;
                try { info.DoorSide = DoorAccessData.Read(source); info.DoorReadError = null; }
                catch (Exception ex) { info.DoorReadError = ex.Message; }
            }
            if (info.DoorReadError != null)
            {
                GUILayout.Label("Cannot read this door's saved access side: " + info.DoorReadError);
                return;
            }
            GUILayout.Label("Choose the tile the character stands on, relative to the door. A locked door can be opened from this side unless it requires a specific key.");
            int current = Array.IndexOf(DoorDirections, (Direction)info.DoorSide);
            if (current < 0) GUILayout.Label($"Unrecognized saved direction ({info.DoorSide}). Choose a side to replace it.");
            int next = GUILayout.SelectionGrid(current, DoorLabels, 2);
            if (next < 0 || next == current) return;
            DoorAccessData.Set(item, source, DoorDirections[next]);
            MarkStructureDirty(geometryChanged: false, zonesChanged: false);
            status = "Door public approach: " + DoorLabels[next] + ". Save the structure JSON to keep the change.";
        }
        catch (Exception ex) { GUILayout.Label("Door access was not changed: " + ex.Message); }
    }
}
