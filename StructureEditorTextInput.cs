#nullable enable
using UnityEngine;

namespace StructureHandler;

internal sealed partial class StructureEditorUI
{
    private int editorTextMouseControl;
    private int editorTextFocusControl;
    private string? editorTextFocusName;
    private Rect editorTextFocusScreenRect;

    // Run before buttons/preview, not inside the field: the field may have been
    // hidden, scrolled away, or assigned another ID by the time MouseUp arrives.
    private void BeginEditorTextInput()
    {
        Event input = Event.current;
        if (input.rawType == EventType.MouseUp && input.button == 0)
        {
            bool ownedRelease = editorTextMouseControl != 0 &&
                (GUIUtility.hotControl == editorTextMouseControl || GUIUtility.hotControl == 0);
            ReleaseEditorTextMouse();
            if (ownedRelease) input.Use(); // This release belongs to text selection, not the preview.
        }
        else if (input.rawType == EventType.MouseDown && input.button == 0)
        {
            // Recover a missed mouse-up (for example a release outside the
            // window). Only release OUR old text capture; never a scrollbar's.
            ReleaseEditorTextMouse();
            if (editorTextFocusControl != 0 && GUIUtility.keyboardControl == editorTextFocusControl &&
                !editorTextFocusScreenRect.Contains(GUIUtility.GUIToScreenPoint(input.mousePosition)))
            {
                GUIUtility.keyboardControl = 0;
                editorTextFocusControl = 0;
                editorTextFocusName = null;
            }
            // Do not consume the click: the intended button/preview must get it.
        }
    }

    private void ReleaseEditorTextMouse()
    {
        if (editorTextMouseControl == 0) return;
        if (GUIUtility.hotControl == editorTextMouseControl)
            GUIUtility.hotControl = 0;
        var editor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), editorTextMouseControl);
        editor.MouseDragSelectsWholeWords(false);
        editorTextMouseControl = 0;
    }

    private void RebindEditorTextFocus(string name, int id, string value)
    {
        // Virtualized controls before a field can change the generated ID.
        // Keep the active editor's selection and mouse ownership together.
        int previous = editorTextFocusControl;
        if (previous == 0 || previous == id || editorTextFocusName != name ||
            GUIUtility.keyboardControl != previous) return;
        var oldEditor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), previous);
        var newEditor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), id);
        TransferEditorTextState(oldEditor, newEditor, value);
        GUIUtility.keyboardControl = id;
        if (editorTextMouseControl == previous)
        {
            if (GUIUtility.hotControl == previous) GUIUtility.hotControl = id;
            editorTextMouseControl = id;
        }
        editorTextFocusControl = id;
    }

    private static void TransferEditorTextState(TextEditor oldEditor, TextEditor newEditor, string value)
    {
        newEditor.text = value;
        newEditor.OnFocus();
        newEditor.cursorIndex = oldEditor.cursorIndex;
        newEditor.selectIndex = oldEditor.selectIndex;
        newEditor.scrollOffset = oldEditor.scrollOffset;
        oldEditor.OnLostFocus();
    }

    private void TrackEditorTextInput(string name, int id, Rect rect)
    {
        if (GUIUtility.keyboardControl == id)
        {
            editorTextFocusControl = id;
            editorTextFocusName = name;
            // Layout/Used passes may return dummy rectangles. Never replace a
            // real hit area with one of those (especially during a drag).
            if (Event.current.rawType != EventType.Layout && rect.width > 0 && rect.height > 0)
                editorTextFocusScreenRect = GUIUtility.GUIToScreenRect(rect);
        }
        if (GUIUtility.hotControl == id && Event.current.rawType == EventType.MouseDown && Event.current.button == 0)
            editorTextMouseControl = id;
    }

    private void ClearEditorTextInput()
    {
        ReleaseEditorTextMouse();
        if (editorTextFocusControl != 0 && GUIUtility.keyboardControl == editorTextFocusControl)
            GUIUtility.keyboardControl = 0;
        editorTextFocusControl = 0;
        editorTextFocusName = null;
    }
}
