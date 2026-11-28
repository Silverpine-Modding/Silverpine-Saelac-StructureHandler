using System.Reflection;
using System.Runtime.CompilerServices;
using StructureHandler;

internal static class DoorAccessChecks
{
    internal static void Run(Action<bool> check)
    {
        // Actual Door.Serialize reads these fields directly (no live renderer
        // required). Make the template deliberately different from an export.
        var door = (Door)RuntimeHelpers.GetUninitializedObject(typeof(Door));
        var priv = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(Door).GetField("publicAccess",priv)!.SetValue(door, Direction.Left);
        typeof(Door).GetField("keyItem",priv)!.SetValue(door, "Blue Key");
        typeof(Door).GetField("turnsUntilClosing",priv)!.SetValue(door, 3);
        door.owners = new List<NPCName> { (NPCName)4, (NPCName)5 };
        door.isLocked = true;
        string template = DoorAccessData.FromTemplate(door);
        var fresh = new StructureObject { prefabName = "prefab_wall_wood_door" };
        check(DoorAccessData.Read(template) == (int)Direction.Left);
        DoorAccessData.Set(fresh, template, Direction.Left);
        check(fresh.components.Count == 0);
        var file = new StructureFile { objects = new() { fresh } };
        foreach (var side in new[] { Direction.Right, Direction.Up, Direction.Down, Direction.Left })
        {
            DoorAccessData.Set(fresh, template, side);
            var copy = (StructureFile)StringSerializationAPI.Deserialize(typeof(StructureFile),
                StringSerializationAPI.Serialize(typeof(StructureFile), file));
            var saved = copy.objects.Single().components.Single();
            check(saved.type == "Door, Assembly-CSharp" && saved.componentIndex == 0 && saved.hierarchyPath == "");
            var fields = StructureItemData.Storage(saved.dataBase64);
            check(fields.GetInt("publicAccess",-1) == (int)side && fields.GetBool("isLocked",false) &&
                  fields.GetString("keyItem","") == "Blue Key" && fields.GetInt("turnsUntilClosing",0) == 3 &&
                  !fields.GetBool("IsOpen",true));
            check(fields.TryCreateBinaryReader("owners", out var reader));
            using (reader) check(TypeSerializationUtility.DeserializeEnumList<NPCName>(reader).SequenceEqual(door.owners));
        }
        check(template == DoorAccessData.FromTemplate(door));
        Console.WriteLine("PASS Quick-placed doors preserve native template settings and all four approach sides round-trip through blueprint JSON.");

        // An exported door may have non-template state and extra fields.
        var source = new VersionSafeStorage();
        source.AddToStorage("publicAccess",(int)Direction.Up);
        source.AddToStorage("keyItem","Manor Key");
        source.AddToStorage("future",new byte[]{1,0,255,4});
        var legacy = new StructureComponent { type = "Door", dataBase64 = template };
        var effective = new StructureComponent { type = "Door, Assembly-CSharp", componentIndex = 0,
            dataBase64 = Convert.ToBase64String(StructureItemData.Bytes(source)) };
        var child = new StructureComponent { type = "Door", hierarchyPath = "1", dataBase64 = "child" };
        var other = new StructureComponent { type = "Door, OtherMod", dataBase64 = "other" };
        var record = new StructureObject { components = new(){legacy, effective, child, other} };
        DoorAccessData.Set(record,template,Direction.Down);
        var edited = StructureItemData.Storage(effective.dataBase64);
        check(edited.GetInt("publicAccess",-1) == (int)Direction.Down && edited.GetString("keyItem","") == "Manor Key" &&
            ((byte[])StructureItemData.Fields(edited)["future"]).SequenceEqual(new byte[]{1,0,255,4}) &&
            legacy.dataBase64 == template && child.dataBase64 == "child" && other.dataBase64 == "other");
        effective.dataBase64 = "corrupt!";
        try { DoorAccessData.Set(record,template,Direction.Left); throw new Exception("Replaced corrupt saved door data."); }
        catch (FormatException) { check(effective.dataBase64 == "corrupt!" && record.components.Count == 4); }
        Console.WriteLine("PASS Door edits honor the importer's last-write rule, preserve unrelated data and refuse a malformed payload.");
    }
}
