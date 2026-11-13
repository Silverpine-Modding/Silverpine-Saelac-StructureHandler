using System.Globalization;
using StructureHandler;
using UnityEngine;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS " + message);
    checks++;
}
void Focus(string axis) => GUIUtility.keyboardControl = ("StructureHandler.coordinate." + axis).GetHashCode();

// Invoke the production coordinate and text-field helpers, not a copy of their
// submission logic. Only Unity's external layout/event APIs are stubbed here.
var ui = new StructureEditorUI();
object owner = new();
string x = "32.204", y = "115", z = "1";
void DrawAll()
{
    x = ui.Coordinate(owner, "X", x);
    y = ui.Coordinate(owner, "Y", y);
    z = ui.Coordinate(owner, "Z", z);
}
foreach (KeyCode key in new[] { KeyCode.Return, KeyCode.KeypadEnter })
{
    y = "115";
    Focus("Y");
    GUI.BeginEvent(EventType.KeyDown, typedText: "118"); DrawAll();
    Check(y == "115", key + ": typing Y does not change the location");
    GUI.BeginEvent(EventType.KeyDown, key);
    Check(GUI.GetNameOfFocusedControl() == "", "Reproduce a missing pre-draw focus name");
    DrawAll();
    Check(y == "118" && x == "32.204" && z == "1", key + ": Y 115 -> 118 commits after drawing X, without changing other axes");
    Check(Event.current.type == EventType.Used && GUIUtility.keyboardControl == 0, "Submission consumes the key once and releases keyboard focus");
    GUI.BeginEvent(EventType.Repaint); DrawAll();
    Check(y == "118", "Committed coordinate survives the next preview repaint");
}
foreach (string axis in new[] { "X", "Y", "Z" })
{
    string current = "1";
    Focus(axis);
    GUI.BeginEvent(EventType.KeyDown, typedText: "-23.973");
    current = ui.Coordinate(owner, axis, current);
    GUI.BeginEvent(EventType.KeyUp, KeyCode.Return);
    current = ui.Coordinate(owner, axis, current);
    Check(current == "1", axis + ": key-up does not commit a pending draft");
    GUI.BeginEvent(EventType.KeyDown, KeyCode.Return);
    current = ui.Coordinate(owner, axis, current);
    Check(current == "-23.973", axis + ": signed decimal submits without rounding in the input helper");
}
foreach (string invalid in new[] { "-", "not a coordinate", "NaN", "Infinity", "10000001" })
{
    Focus("Y");
    GUI.BeginEvent(EventType.KeyDown, typedText: invalid); ui.Coordinate(owner, "Y", "118");
    GUI.BeginEvent(EventType.KeyDown, KeyCode.Return);
    Check(ui.Coordinate(owner, "Y", "118") == "118" && GUIUtility.keyboardControl != 0 && ui.Status.Contains("valid coordinate"),
        "Invalid input stays editable without moving the object: " + invalid);
    GUI.BeginEvent(EventType.KeyDown, typedText: "119"); ui.Coordinate(owner, "Y", "118");
    GUI.BeginEvent(EventType.KeyDown, KeyCode.Return);
    Check(ui.Coordinate(owner, "Y", "118") == "119", "Correcting the invalid draft allows the next Enter to apply");
}
Focus("X");
GUI.BeginEvent(EventType.KeyDown, typedText: "2.5"); ui.Coordinate(owner, "X", "2", true);
GUI.BeginEvent(EventType.KeyDown, KeyCode.Return);
Check(ui.Coordinate(owner, "X", "2", true) == "2" && ui.Status.Contains("whole-number"), "Terrain rejects fractional cell coordinates");
GUI.BeginEvent(EventType.KeyDown, typedText: "-5"); ui.Coordinate(owner, "X", "2", true);
GUI.BeginEvent(EventType.KeyDown, KeyCode.KeypadEnter);
Check(ui.Coordinate(owner, "X", "2", true) == "-5", "Terrain commits a valid negative whole-cell coordinate");
Focus("Y");
GUI.BeginEvent(EventType.KeyDown, typedText: "999"); ui.Coordinate(owner, "Y", "118");
GUI.BeginEvent(EventType.KeyDown, KeyCode.Escape);
Check(ui.Coordinate(owner, "Y", "118") == "118", "Escape does not apply the pending value");
GUI.BeginEvent(EventType.KeyDown, KeyCode.Return);
Check(ui.Coordinate(owner, "Y", "118") == "118", "Enter after Escape uses the original value, not the discarded draft");
Focus("Y");
GUI.BeginEvent(EventType.KeyDown, typedText: "999"); ui.Coordinate(owner, "Y", "118");
Focus("X");
GUI.BeginEvent(EventType.KeyDown, KeyCode.Return);
Check(ui.Coordinate(owner, "Y", "118") == "118" && Event.current.type == EventType.KeyDown,
    "An unfocused field neither commits nor consumes another field's Enter");
Focus("Y");
GUI.BeginEvent(EventType.KeyDown, KeyCode.Return);
Check(ui.Coordinate(new object(), "Y", "10") == "10", "Selecting a different record cannot apply the previous record's draft");
Check(GUI.UnhandledSubmitKeys == 0, "Coordinate actions are captured before the native text widget can consume them");

void Pointer(EventType type, float mouseX, float mouseY)
{
    GUI.BeginEvent(type);
    Event.current.mousePosition = new Vector2(mouseX, mouseY);
    ui.BeginInput();
}
const string dragField = "StructureHandler.drag-test";
int DragId() => dragField.GetHashCode() + GUI.ControlIdOffset;
void StartDrag()
{
    Pointer(EventType.MouseDown, 20, 20);
    ui.Text("-23.973", dragField);
    Check(GUIUtility.hotControl == DragId() && GUIUtility.keyboardControl == DragId(), "A field click owns mouse capture and keyboard focus");
    Check(GUI.GetNameOfFocusedControl() == dragField, "The field name is registered before allocating its control ID");
}
void ReleaseOutside(bool alreadyConsumed = false)
{
    GUI.BeginEvent(EventType.MouseUp);
    Event.current.mousePosition = new Vector2(1000, 600);
    if (alreadyConsumed) Event.current.Use();
    ui.BeginInput();
}
ui.ResetInput();
StartDrag();
Pointer(EventType.MouseDrag, 1000, 600);
Check(GUIUtility.hotControl == DragId(), "Text drag crossing the preview remains captured, blocking paint/pan");
ui.Text("-23.973", dragField);
var selection = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), DragId());
Check(selection.cursorIndex == 7 && selection.selectIndex == 1, "Dragging extends text selection instead of deselecting it");
ReleaseOutside(); // Deliberately do not draw the original field on mouse-up.
Check(GUIUtility.hotControl == 0 && Event.current.type == EventType.Used && GUIUtility.keyboardControl == DragId(),
    "Release outside a missing field unlocks controls without dropping keyboard selection or leaking the release to the preview");
Check(selection.cursorIndex == 7 && selection.selectIndex == 1, "Mouse release preserves the selected text");
Pointer(EventType.MouseDown, 1000, 600);
Check(GUIUtility.hotControl == 0 && GUIUtility.keyboardControl == 0 && Event.current.type == EventType.MouseDown,
    "The very next outside click can activate a button or the preview without re-clicking the field");

StartDrag();
ReleaseOutside(true);
Check(GUIUtility.hotControl == 0, "Raw mouse-up cleans up text capture even when another handler already used the event");
StartDrag();
Pointer(EventType.MouseDown, 1000, 600); // MouseUp was lost outside the app.
Check(GUIUtility.hotControl == 0 && GUIUtility.keyboardControl == 0 && Event.current.type == EventType.MouseDown,
    "A fresh outside click recovers a lost mouse-up and is not swallowed");

StartDrag();
Pointer(EventType.MouseDrag, 1000, 600); ui.Text("-23.973", dragField);
int oldId = DragId();
selection = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), oldId);
selection.scrollOffset = new Vector2(40, 0);
GUI.ControlIdOffset += 137; // Simulate virtualized controls changing before the field.
GUI.BeginEvent(EventType.Repaint); ui.BeginInput(); ui.Text("-23.973", dragField);
selection = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), DragId());
Check(DragId() != oldId && GUIUtility.hotControl == DragId() && GUIUtility.keyboardControl == DragId() &&
    selection.cursorIndex == 7 && selection.selectIndex == 1 && selection.scrollOffset.x == 40,
    "ID changes transfer drag ownership, selection, and text scroll together without deselecting");
ReleaseOutside();
Check(GUIUtility.hotControl == 0, "Rebound drag releases normally");
StartDrag();
GUIUtility.hotControl = 4242;
ReleaseOutside();
Check(GUIUtility.hotControl == 4242 && Event.current.type == EventType.MouseUp,
    "Text cleanup never steals an unrelated scrollbar/button capture or its mouse-up");
ui.ResetInput();
Check(GUIUtility.hotControl == 4242, "Closing the editor leaves an unrelated control's capture untouched");
GUIUtility.hotControl = 0;
StartDrag();
ui.ResetInput();
Check(GUIUtility.hotControl == 0 && GUIUtility.keyboardControl == 0, "Editor close releases its own active text gesture and focus");
Console.WriteLine($"PASS {checks} headless editor input checks (live Unity interaction still requires an in-game test).");

namespace StructureHandler
{
    internal sealed partial class StructureEditorUI
    {
        private string status = "";
        internal string Status => status;
        internal void BeginInput() => BeginEditorTextInput();
        internal void ResetInput() => ClearEditorTextInput();
        internal string Text(string value, string name) => EditorTextField(value, name);
        internal string Coordinate(object owner, string axis, string current, bool wholeNumber = false)
            => CoordinateTextField(owner, axis, current, wholeNumber);
        private static bool TryFloat(string text, out float value) =>
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);
    }
}
