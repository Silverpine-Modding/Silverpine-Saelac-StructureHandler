using System;
using System.Collections;
using System.Reflection;
using StructureHandler;

internal static class EditorPerformanceChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var assembly = typeof(StructureItemQuality).Assembly;
        var uiType = assembly.GetType("StructureHandler.StructureEditorUI", true);
        const BindingFlags privateStatic = BindingFlags.NonPublic | BindingFlags.Static;
        check(uiType.GetMethod("ResolveNativeTextField", privateStatic).Invoke(null, null) != null,
            "The real Unity IMGUI text-field implementation accepts a namespaced control-ID delegate under Mono.");
        var fieldAction = uiType.GetMethod("TextFieldAction", privateStatic);
        foreach (var key in new[] { UnityEngine.KeyCode.Return, UnityEngine.KeyCode.KeypadEnter, UnityEngine.KeyCode.Escape })
        {
            string expected = key == UnityEngine.KeyCode.Escape ? "Cancel" : "Submit";
            check(fieldAction.Invoke(null, new object[] { true, true, UnityEngine.EventType.KeyDown, key }).ToString() == expected &&
                fieldAction.Invoke(null, new object[] { true, false, UnityEngine.EventType.KeyDown, key }).ToString() == "None" &&
                fieldAction.Invoke(null, new object[] { false, true, UnityEngine.EventType.KeyDown, key }).ToString() == "None" &&
                fieldAction.Invoke(null, new object[] { true, true, UnityEngine.EventType.Used, key }).ToString() == "None" &&
                fieldAction.Invoke(null, new object[] { true, true, UnityEngine.EventType.KeyUp, key }).ToString() == "None",
                "Compiled coordinate key handling uses the correct Unity event/key values and only acts for the focused field: " + key);
        }
        foreach (string text in new[] { "-23.973", "32.204", "prefab_furniture_table_end", "", "roofed_grass_plant_01" })
        {
            var editor = (UnityEngine.TextEditor)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(UnityEngine.TextEditor));
            typeof(UnityEngine.TextEditor).GetField("m_Content", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(editor, new UnityEngine.GUIContent(text));
            editor.cursorIndex = Math.Min(3, text.Length);
            editor.selectIndex = 0;
            uiType.GetMethod("SelectWholeField", privateStatic).Invoke(null, new object[] { editor });
            check(Math.Abs(editor.cursorIndex - editor.selectIndex) == text.Length && editor.text == text,
                "Full-field selection includes every character without changing text: " + text);
        }
        foreach (string nextText in new[] { "-23.973", "12", "" })
        {
            UnityEngine.TextEditor MakeEditor(string text)
            {
                var value = (UnityEngine.TextEditor)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(UnityEngine.TextEditor));
                typeof(UnityEngine.TextEditor).GetField("m_Content", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(value, new UnityEngine.GUIContent(text));
                return value;
            }
            var oldEditor = MakeEditor("-23.973");
            oldEditor.OnFocus(); oldEditor.cursorIndex = 7; oldEditor.selectIndex = 1;
            oldEditor.scrollOffset = new UnityEngine.Vector2(40, 0);
            var newEditor = MakeEditor("");
            uiType.GetMethod("TransferEditorTextState", privateStatic).Invoke(null, new object[] { oldEditor, newEditor, nextText });
            check(newEditor.text == nextText && newEditor.cursorIndex == Math.Min(7, nextText.Length) &&
                newEditor.selectIndex == Math.Min(1, nextText.Length) && newEditor.scrollOffset.x == 40 &&
                oldEditor.scrollOffset.x == 0,
                "Native TextEditor selection/scroll survives an ID change and safely clamps changed text: '" + nextText + "'.");
        }
        var type = assembly.GetType("StructureHandler.StructureFile", true);
        var copier = assembly.GetType("StructureHandler.EditorDocumentCopy", true);
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        string json = "{\"name\":\"Mono snapshot\",\"objects\":[{\"prefabName\":\"prefab_item\",\"x\":-1.25,\"y\":2.75,\"z\":1,\"turnableIndex\":3,\"components\":[{\"type\":\"WorldItem\",\"dataBase64\":\"AAAA\"}]}],\"supportingTerrain\":[{\"prefabName\":\"prefab_water_bath\",\"hasMapZoneName\":true,\"mapZoneName\":\"Bath\"}],\"planningTiles\":[{\"x\":-1,\"y\":2,\"color\":\"#FF0000\"}]}";
        object original = StringSerializationAPI.Deserialize(type, json);
        object copy = copier.GetMethod("Clone", flags).Invoke(null, new[] { original });
        check((bool)copier.GetMethod("Equal", flags).Invoke(null, new[] { original, copy }) &&
            StringSerializationAPI.Serialize(type, original) == StringSerializationAPI.Serialize(type, copy),
            "Compiled undo accessors execute under the game's Mono runtime and preserve native JSON.");
        var list = (IList)type.GetField("objects").GetValue(copy);
        list[0].GetType().GetField("x").SetValue(list[0], 9f);
        check(!(bool)copier.GetMethod("Equal", flags).Invoke(null, new[] { original, copy }) &&
            (long)copier.GetMethod("Size", flags).Invoke(null, new[] { copy }) > 0,
            "Mono snapshot copies isolate mutations and calculate retained memory without Unity scene calls.");
        var historyType = assembly.GetType("StructureHandler.EditorDocumentHistory", true);
        var history = Activator.CreateInstance(historyType, true);
        const BindingFlags member = BindingFlags.NonPublic | BindingFlags.Instance;
        historyType.GetMethod("Reset", member).Invoke(history, new[] { original });
        historyType.GetMethod("Record", member).Invoke(history, new[] { copy });
        var restored = historyType.GetMethod("Undo", member).Invoke(history, null);
        check((bool)copier.GetMethod("Equal", flags).Invoke(null, new[] { original, restored }),
            "Mono undo restores the original document after an edit.");
    }
}
