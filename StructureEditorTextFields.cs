#nullable enable
using System;
using UnityEngine;

namespace StructureHandler;

internal sealed partial class StructureEditorUI
{
    private enum EditorFieldAction { None, Submit, Cancel }
    private delegate void NativeTextField(Rect rect, int id, GUIContent content, bool multiline, int limit, GUIStyle style);
    private static readonly NativeTextField? nativeTextField = ResolveNativeTextField();
    private readonly GUIContent editorFieldContent = new();
    private static readonly GUIContent fieldSizeContent = new("M");
    private int editorFieldSequence;

    private static NativeTextField? ResolveNativeTextField()
    {
        // GUI.TextField/GUILayout.TextField do not expose an ID overload. Reuse
        // Unity's actual editing implementation with our own namespaced hints,
        // so virtualized lists and other mod panels cannot share field state.
        var method = typeof(GUI).GetMethod("DoTextField",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic,
            null, new[] { typeof(Rect), typeof(int), typeof(GUIContent), typeof(bool), typeof(int), typeof(GUIStyle) }, null);
        if (method == null) return null;
        try { return (NativeTextField)Delegate.CreateDelegate(typeof(NativeTextField), method); }
        catch { return null; } // Future Unity versions retain the public control as a fallback.
    }

    private string EditorTextField(string value, string? name = null, int maxLength = -1)
        => EditTextField(value, name, maxLength, false).Text;

    private (string Text, EditorFieldAction Action) EditTextField(
        string value, string? name = null, int maxLength = -1, bool submitOnEnter = false)
    {
        name ??= "StructureHandler.text." + editorFieldSequence++;
        if (nativeTextField != null) GUI.SetNextControlName(name);
        // Match GUILayout's stable keyboard-control allocation: the text value
        // and changing layout rectangle must not identify the editing state.
        int id = nativeTextField != null
            ? GUIUtility.GetControlID(name.GetHashCode(), FocusType.Keyboard) : 0;
        // Long prefab names scroll inside the field instead of resizing its hit area.
        Rect rect = GUILayoutUtility.GetRect(fieldSizeContent, GUI.skin.textField,
            GUILayout.MinWidth(30), GUILayout.ExpandWidth(true));
        Event input = Event.current;
        if (nativeTextField == null)
        {
            GUI.SetNextControlName(name);
            // The public control owns its ID. Snapshot the key before it can
            // consume it, but look up the name only AFTER registering the field.
            EventType type = input.type;
            KeyCode key = input.keyCode;
            string text = GUI.TextField(rect, value, maxLength);
            if (GUI.GetNameOfFocusedControl() == name)
                TrackEditorTextInput(name, GUIUtility.keyboardControl, rect);
            EditorFieldAction fallbackAction = TextFieldAction(submitOnEnter,
                GUI.GetNameOfFocusedControl() == name, type, key);
            if (fallbackAction != EditorFieldAction.None) input.Use();
            return (text, fallbackAction);
        }
        RebindEditorTextFocus(name, id, value);
        // A pre-draw name lookup can be empty/stale during a keyboard event.
        // Use the exact ID used by the native editor and intercept Enter/Escape
        // before DoTextField consumes them or restores its own text backup.
        EditorFieldAction action = TextFieldAction(submitOnEnter,
            GUIUtility.keyboardControl == id, input.type, input.keyCode);
        bool selectAll = GUIUtility.keyboardControl == id && input.type == EventType.KeyDown &&
            input.keyCode == KeyCode.A && (input.control || input.command) && !input.alt;
        bool doubleClick = input.type == EventType.MouseDown && input.button == 0 && input.clickCount >= 2 && rect.Contains(input.mousePosition);
        if (selectAll || action != EditorFieldAction.None) input.Use();
        editorFieldContent.text = value ?? "";
        nativeTextField(rect, id, editorFieldContent, false, maxLength, GUI.skin.textField);
        TrackEditorTextInput(name, id, rect);
        if ((selectAll || doubleClick) && GUIUtility.keyboardControl == id)
        {
            var editor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), id);
            SelectWholeField(editor);
            input.Use();
        }
        return (editorFieldContent.text, action);
    }

    private static EditorFieldAction TextFieldAction(bool submitOnEnter, bool focused, EventType type, KeyCode key)
    {
        if (!submitOnEnter || !focused || type != EventType.KeyDown) return EditorFieldAction.None;
        if (key == KeyCode.Return || key == KeyCode.KeypadEnter) return EditorFieldAction.Submit;
        return key == KeyCode.Escape ? EditorFieldAction.Cancel : EditorFieldAction.None;
    }

    internal static void SelectWholeField(TextEditor editor)
    {
        editor.MouseDragSelectsWholeWords(false);
        editor.SelectAll(); // Includes minus signs, decimal points and every underscore-separated segment.
    }
}
