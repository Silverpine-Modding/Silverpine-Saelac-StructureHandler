using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;
using StructureHandler;

internal static class EditorPlacementChecks
{
    // Exercise production editor methods in the game's Mono process. Only the
    // asset metadata and managed UI host are supplied, not a scene or save.
    internal static void Run(Action<bool, string> check)
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        const BindingFlags stat = BindingFlags.Static | BindingFlags.NonPublic;
        var asm = typeof(StructureItemQuality).Assembly;
        var type = asm.GetType("StructureHandler.StructureEditorUI", true);
        var ui = FormatterServices.GetUninitializedObject(type);
        object New(string n) => Activator.CreateInstance(asm.GetType("StructureHandler." + n, true), true);
        void Set(object o, string key, object v) => o.GetType().GetField(key, instance).SetValue(o, v);
        object Get(object o, string key) => o.GetType().GetField(key, instance).GetValue(o);
        object Call(string method, params object[] args) => type.GetMethod(method, instance).Invoke(ui, args);
        var profiles = (IDictionary)type.GetField("placementProfiles", stat).GetValue(null);
        const string ground = "prefab_tile_test_ground", overlay = "prefab_tile_test_overlay", prop = "test_prop";
        object Profile(string name, string slot, bool construction)
        {
            var p = Activator.CreateInstance(type.GetNestedType("PlacementProfile", BindingFlags.NonPublic), true);
            Set(p, "Slot", Enum.Parse(p.GetType().GetField("Slot", instance).FieldType, slot));
            Set(p, "Construction", construction);
            profiles.Add(name, p);
            return p;
        }
        var doc = New("StructureFile");
        Set(ui, "structure", doc);
        foreach (var f in new[] { "placementIndex", "placementCandidates", "cachedCells" })
            Set(ui, f, Activator.CreateInstance(type.GetField(f, instance).FieldType, true));
        var waterProfile = Profile(ground, "Ground", false);
        ((IList)Get(waterProfile, "WaterOffsets")).Add(Vector2.zero);
        Profile(overlay, "WaterOverlay", true);
        var propProfile = Profile(prop, "None", true);
        try
        {
            object Add(string recordType, string list, string name, object x)
            {
                var record = New(recordType); Set(record, "prefabName", name); Set(record, "x", x);
                ((IList)Get(doc, list)).Add(record);
                return record;
            }
            Add("SupportingTerrain", "supportingTerrain", ground, 0);
            var existing = Add("SupportingTerrain", "supportingTerrain", overlay, 0);
            Set(existing, "z", 1f); Set(existing, "mapZoneName", "Original"); Set(existing, "hasMapZoneName", true);
            var record = Add("StructureObject", "objects", prop, 5f);
            Set(ui, "brushPrefabName", overlay); Set(ui, "terrainBrushZone", "Changed"); Set(ui, "addZ", "2");
            Set(ui, "placementIndexDirty", true);
            Call("PlaceBrushObject", Vector2Int.zero, null);
            check((float)Get(existing,"z") == 1f && (string)Get(existing,"mapZoneName") == "Original" &&
                !(bool)Get(ui,"dirty") && ((string)Get(ui,"status")).Contains("submerged"),
                "Rejected brush repaint preserves the existing terrain Z, zone, and dirty flag.");
            Set(ui, "addZ", "not a number"); Call("PlaceBrushObject", Vector2Int.zero, null);
            check(((string)Get(ui,"status")).Contains("valid Z") && (float)Get(existing,"z") == 1f,
                "Invalid brush Z is rejected, not silently replaced by zero.");
            Set(ui, "placementIndexDirty", true);
            Call("ChangeObjectPlacement", record, 0f, 0f, null);
            check((float)Get(record, "x") == 5f && !(bool)Get(ui,"dirty") && ((string)Get(ui,"status")).Contains("submerged"),
                "Cold-index object validation rejects submerged placement without moving the original record.");
            // A fractional child at 5.4 + 0.4 registers at 6, not 5. The
            // query must work even though the source has a single-cell footprint.
            Set(record, "x", 5.4f);
            var offsets = (IList)Get(propProfile, "WaterOffsets"); offsets.Add(new Vector2(.4f, 0));
            var colliders = (IList)Get(propProfile, "Colliders");
            colliders.Add((new Vector2(.4f, 0), "1", 0, false, true));
            Set(ui, "placementIndexDirty", true);
            var candidate = New("StructureObject"); Set(candidate,"prefabName",prop); Set(candidate,"x",6f);
            check(!(bool)Call("CheckPlacement", candidate, false, null) && ((string)Get(ui,"status")).Contains("submerged"),
                "Preview indexes and checks a submerged child at the rounded final world position.");
            offsets.Clear();
            check(!(bool)Call("CheckPlacement", candidate, false, null) && ((string)Get(ui,"status")).Contains("impassable"),
                "A fractional child collider blocks its registered cell even outside the root's footprint.");
            Set(candidate,"x",5f);
            check((bool)Call("CheckPlacement", candidate, false, null),
                "Child collision and submersion do not incorrectly block the parent's rounded anchor.");
            Set(propProfile,"Construction",false); Set(propProfile,"Pickup",true); Set(candidate,"x",6f);
            check(!(bool)Call("CheckPlacement", candidate, false, null) && ((string)Get(ui,"status")).Contains("walls"),
                "Unsnapped wall-restricted pickups use the blocking child collider's wall identity.");
            var saved = New("StructureComponent"); Set(saved,"type","TurfCollider, Assembly-CSharp");
            Set(saved,"hierarchyPath","1"); Set(saved,"componentIndex",0); Set(saved,"dataBase64","AQ==");
            ((IList)Get(record,"components")).Add(saved);
            check((bool)Call("CheckPlacement", candidate, false, null),
                "The compiled placement check honors the saved passable flag of that exact child collider.");
            Set(ui,"ignorePlacementRules",true); Set(ui,"brushPrefabName",overlay); Set(ui,"addZ","1"); Set(ui,"terrainBrushZone","Original");
            Call("PlaceBrushObject", Vector2Int.zero, null);
            check(!(bool)Get(ui,"dirty") && ((IList)Get(doc,"supportingTerrain")).Count == 2,
                "An unchanged repaint retains both layers, selects the existing tile, and does not dirty the document.");
        }
        finally { profiles.Remove(ground); profiles.Remove(overlay); profiles.Remove(prop); }
    }
}
