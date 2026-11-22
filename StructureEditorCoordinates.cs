#nullable enable
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace StructureHandler;

internal sealed partial class StructureEditorUI
{
    private object? coordinateDraftOwner;
    private readonly Dictionary<string, EditorCoordinateDraft> coordinateDrafts = new();

    private EditorCoordinateDraft FieldDraft(object owner, string key)
    {
        if (!ReferenceEquals(coordinateDraftOwner, owner))
        {
            coordinateDraftOwner = owner;
            coordinateDrafts.Clear();
        }
        if (!coordinateDrafts.TryGetValue(key, out var draft))
            coordinateDrafts[key] = draft = new EditorCoordinateDraft();
        return draft;
    }

    private string PrefabTextField(object owner, string current)
    {
        var draft = FieldDraft(owner, "Prefab");
        draft.Observe(current);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Prefab (Enter)", GUILayout.Width(85));
        var edit = EditTextField(draft.Text, "StructureHandler.prefab", submitOnEnter: true);
        draft.Text = edit.Text;
        GUILayout.EndHorizontal();
        if (edit.Action == EditorFieldAction.None) return current;
        draft.Reset();
        if (edit.Action != EditorFieldAction.Submit) return current;
        GUIUtility.keyboardControl = 0;
        return edit.Text.Trim();
    }

    // Return the existing coordinate until Enter is pressed. Incomplete values
    // such as "-" or "107." must not move a record or rebuild the entire preview.
    private string CoordinateTextField(object owner, string axis, string current, bool wholeNumber = false)
    {
        var draft = FieldDraft(owner, axis);
        draft.Observe(current);
        string control = "StructureHandler.coordinate." + axis;
        GUILayout.BeginHorizontal();
        GUILayout.Label(axis, GUILayout.Width(85));
        var edit = EditTextField(draft.Text, control, submitOnEnter: true);
        draft.Text = edit.Text;
        GUILayout.EndHorizontal();
        if (edit.Action == EditorFieldAction.Cancel)
        {
            draft.Reset();
        }
        if (edit.Action != EditorFieldAction.Submit) return current;
        bool valid = wholeNumber
            ? int.TryParse(draft.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer) &&
                integer >= -10000000 && integer <= 10000000
            : TryFloat(draft.Text, out float value) && Mathf.Abs(value) <= 10000000;
        if (!valid)
        {
            status = wholeNumber ? "Enter a whole-number coordinate within ±10,000,000." : "Enter a valid coordinate within ±10,000,000.";
            return current;
        }
        string result = draft.Text;
        draft.Reset(); // Next paint shows the accepted position, including any native snapping/rejection.
        GUIUtility.keyboardControl = 0;
        return result;
    }
}
