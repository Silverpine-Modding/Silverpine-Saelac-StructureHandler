// Headless event/registration surface for the actual production field helpers.
// Deliberately not a replacement for testing mouse/focus behavior in Unity.
namespace UnityEngine;

public enum EventType { Layout, Repaint, KeyDown, KeyUp, MouseDown, MouseDrag, MouseUp, Used }
public enum KeyCode { None, Return, KeypadEnter, Escape, A }
public enum FocusType { Keyboard }
public readonly record struct Vector2(float x, float y);
public readonly record struct Rect(float x, float y, float width, float height)
{
    public bool Contains(Vector2 point) => point.x >= x && point.x < x + width && point.y >= y && point.y < y + height;
}
public sealed class GUIContent(string text = "") { public string text = text; }
public sealed class GUIStyle { }
public sealed class GUISkin { public GUIStyle textField = new(); }
public sealed class GUILayoutOption { }
public sealed class Event
{
    public static Event current = new();
    public EventType type;
    public EventType rawType;
    public KeyCode keyCode;
    public bool control, command, alt;
    public int button, clickCount;
    public Vector2 mousePosition;
    public void Use() => type = EventType.Used;
}
public static class GUILayoutUtility
{
    public static Rect NextRect = new(10, 10, 200, 25);
    public static Rect GetRect(GUIContent _, GUIStyle style, params GUILayoutOption[] options) => NextRect;
}
public static class GUILayout
{
    public static GUILayoutOption MinWidth(float _) => new();
    public static GUILayoutOption ExpandWidth(bool _) => new();
    public static GUILayoutOption Width(float _) => new();
    public static void BeginHorizontal() { }
    public static void EndHorizontal() { }
    public static void Label(string _, params GUILayoutOption[] options) { }
}
public static class Mathf { public static float Abs(float value) => Math.Abs(value); }
public sealed class TextEditor
{
    public string text = "";
    public int cursorIndex, selectIndex;
    public Vector2 scrollOffset;
    public bool HasFocus;
    public void MouseDragSelectsWholeWords(bool _) { }
    public void SelectAll() { selectIndex = 0; cursorIndex = text.Length; }
    public void OnFocus() { HasFocus = true; SelectAll(); }
    public void OnLostFocus() { HasFocus = false; scrollOffset = default; }
}
public static class GUIUtility
{
    public static int keyboardControl, hotControl;
    private static readonly Dictionary<int, TextEditor> editors = new();
    public static int GetControlID(int hint, FocusType _, Rect rect)
    {
        int id = hint + GUI.ControlIdOffset;
        GUI.Register(id);
        return id;
    }
    public static int GetControlID(int hint, FocusType focus) => GetControlID(hint, focus, default);
    public static Vector2 GUIToScreenPoint(Vector2 point) => point;
    public static Rect GUIToScreenRect(Rect rect) => rect;
    public static object GetStateObject(Type _, int id)
    {
        if (!editors.TryGetValue(id, out var editor)) editors[id] = editor = new();
        return editor;
    }
}
public static class GUI
{
    public static GUISkin skin = new();
    private static string nextName = "";
    private static readonly Dictionary<int, string> names = new();
    public static string? TypedText;
    public static int UnhandledSubmitKeys;
    public static int ControlIdOffset;
    public static void BeginEvent(EventType type, KeyCode key = KeyCode.None, string? typedText = null)
    {
        Event.current = new Event { type = type, rawType = type, keyCode = key };
        names.Clear(); // Focus ID persists; this pass has not registered names yet.
        TypedText = typedText;
    }
    public static void SetNextControlName(string name) => nextName = name;
    public static void Register(int id) { names[id] = nextName; nextName = ""; }
    public static string GetNameOfFocusedControl() => names.GetValueOrDefault(GUIUtility.keyboardControl, "");
    public static string TextField(Rect rect, string text, int limit)
    {
        int id = GUIUtility.GetControlID(nextName.GetHashCode(), FocusType.Keyboard, rect);
        var content = new GUIContent(text);
        DoTextField(rect, id, content, false, limit, skin.textField);
        return content.text;
    }
    private static void DoTextField(Rect rect, int id, GUIContent content, bool multiline, int limit, GUIStyle style)
    {
        var editor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), id);
        editor.text = content.text;
        if (GUIUtility.keyboardControl == id && !editor.HasFocus) editor.OnFocus();
        if (GUIUtility.keyboardControl != id && editor.HasFocus) editor.OnLostFocus();
        // The game's desktop text widget uses current.type for mouse input,
        // whereas normal buttons reject clicks while another ID owns capture.
        if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
        {
            GUIUtility.hotControl = GUIUtility.keyboardControl = id;
            editor.HasFocus = true;
            editor.cursorIndex = editor.selectIndex = 1;
            Event.current.Use();
        }
        else if (Event.current.type == EventType.MouseDrag && GUIUtility.hotControl == id)
        {
            editor.cursorIndex = content.text.Length;
            Event.current.Use();
        }
        else if (Event.current.type == EventType.MouseUp && GUIUtility.hotControl == id)
        {
            GUIUtility.hotControl = 0;
            Event.current.Use();
        }
        if (GUIUtility.keyboardControl != id || Event.current.type != EventType.KeyDown) return;
        if (Event.current.keyCode is KeyCode.Return or KeyCode.KeypadEnter or KeyCode.Escape)
        {
            UnhandledSubmitKeys++;
            Event.current.Use();
        }
        else if (TypedText != null)
        {
            content.text = TypedText;
            editor.text = TypedText;
            TypedText = null;
            Event.current.Use();
        }
    }
}
