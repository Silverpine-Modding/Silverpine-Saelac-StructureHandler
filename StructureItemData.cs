#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace StructureHandler;

// Real WorldInventory/WorldItem payloads, not a second companion-save system.
// Never instantiate a prefab or invoke inventory-added/equipped callbacks here.
internal static class StructureItemData
{
    private static readonly FieldInfo StorageField = typeof(VersionSafeStorage).GetField("storage", BindingFlags.NonPublic | BindingFlags.Instance)!;
    internal static bool IsItem(StructureObject record) => record.prefabName.Equals("prefab_item", StringComparison.OrdinalIgnoreCase);
    internal static bool IsComponent(StructureComponent component, string type) =>
        string.IsNullOrEmpty(component.hierarchyPath) && component.componentIndex <= 0 &&
        component.type.Split(',')[0].Trim() == type;
    internal static byte[] Bytes(ISerializable value)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        value.Serialize(writer);
        writer.Flush();
        return stream.ToArray();
    }
    internal static VersionSafeStorage Storage(string base64)
    {
        using var stream = new MemoryStream(Convert.FromBase64String(base64));
        using var reader = new BinaryReader(stream);
        var storage = ReadStorage(reader);
        if (stream.Position != stream.Length) throw new InvalidDataException("Unrecognized trailing item data.");
        return storage;
    }
    private static VersionSafeStorage ReadStorage(BinaryReader reader)
    {
        // Read the native Release 1.7.3 envelope without Deserialize's delayed
        // gameplay audit. This editor inspects/preserves fields directly rather
        // than calling native getters, so that audit falsely reports every field
        // as unread. No global logging or game deserialization is changed.
        var storage = new VersionSafeStorage();
        var names = new HashSet<string>(StringComparer.Ordinal);
        int count = reader.ReadByte();
        for (int i = 0; i < count; i++)
        {
            string name = reader.ReadString();
            if (!names.Add(name)) throw new InvalidDataException("Duplicate item-storage field: " + name);
            byte type = reader.ReadByte();
            switch (type)
            {
                case 0: storage.AddToStorage(name, reader.ReadBoolean()); break;
                case 1: storage.AddToStorage(name, reader.ReadSingle()); break;
                case 2: storage.AddToStorage(name, reader.ReadInt32()); break;
                case 3: storage.AddToStorage(name, reader.ReadString()); break;
                case 4: storage.AddToStorage(name, reader.ReadColor()); break;
                case 5: storage.AddToStorage(name, reader.ReadVector2Int()); break;
                case 6: storage.AddToStorage(name, reader.ReadVector2()); break;
                case 7:
                    int length = reader.ReadInt32();
                    if (length < 0 || length > reader.BaseStream.Length - reader.BaseStream.Position)
                        throw new InvalidDataException("Truncated or invalid item-storage blob: " + name);
                    storage.AddToStorage(name, reader.ReadBytes(length));
                    break;
                default: throw new InvalidDataException("Unknown item-storage field type: " + type);
            }
        }
        return storage;
    }
    private static Dictionary<string, object> Fields(VersionSafeStorage storage) =>
        (Dictionary<string, object>)StorageField.GetValue(storage)!;
    internal static Item Clone(Item item)
    {
        if (item.GetType() != typeof(Item))
            throw new InvalidDataException("This custom Item subclass needs its providing mod's editor; it cannot safely be converted to a base item.");
        using var reader = new BinaryReader(new MemoryStream(Bytes(item)));
        var copy = ReadItem(reader);
        if (reader.BaseStream.Position != reader.BaseStream.Length)
            throw new InvalidDataException("An item component could not be read. Its providing mod may be missing.");
        return copy;
    }
    private static Item ReadItem(BinaryReader reader)
    {
        // The native list reader logs and skips unknown component types without
        // skipping their bytes. Fail closed instead of silently corrupting an
        // item when its providing mod is absent. Never run Initialize/OnUse.
        var storage = ReadStorage(reader);
        var fields = Fields(storage);
        var item = new Item
        {
            name = (string)fields["name"], spriteName = (string)fields["spriteName"],
            description = (string)fields["description"], value = (int)fields["value"],
            bulk = Convert.ToSingle(fields["bulk"]), itemCategory = (ItemCategory)(int)fields["itemCategory"],
            itemSound = (ItemSound)(int)fields["itemSound"]
        };
        using var components = new BinaryReader(new MemoryStream((byte[])fields["itemComponents"]));
        int count = components.ReadInt32();
        if (count < 0 || count > 10000) throw new InvalidDataException("Invalid item component count.");
        var decoded = new List<ItemComponent>();
        for (int i = 0; i < count; i++)
        {
            string name = components.ReadString();
            Type? type = typeof(Item).Assembly.GetType(name, false) ?? Type.GetType(name, false);
            if (type == null)
                type = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(name, false)).FirstOrDefault(candidate => candidate != null);
            if (type == null || type.IsAbstract || !typeof(ItemComponent).IsAssignableFrom(type))
                throw new InvalidDataException("Missing or incompatible item component: " + name);
            var component = (ItemComponent)Activator.CreateInstance(type)!;
            component.Deserialize(components);
            component.item = item;
            decoded.Add(component);
        }
        if (components.BaseStream.Position != components.BaseStream.Length)
            throw new InvalidDataException("Unrecognized item component data; no changes were made.");
        typeof(Item).GetField("itemComponents", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(item, decoded);
        return item;
    }
    internal static Item Read(StructureObject record, out int rotation)
    {
        if (!IsItem(record)) throw new InvalidDataException("Not a loose world item.");
        var inventories = record.components.Where(c => IsComponent(c, "WorldInventory")).ToArray();
        if (inventories.Length != 1) throw new InvalidDataException("Expected one native item inventory.");
        var storage = Storage(inventories[0].dataBase64);
        if (!Fields(storage).TryGetValue("inventory", out object? blob) || blob is not byte[] bytes)
            throw new InvalidDataException("Missing native item inventory data.");
        var inventory = Storage(Convert.ToBase64String(bytes));
        using var reader = new BinaryReader(new MemoryStream((byte[])Fields(inventory)["items"]));
        if (reader.ReadInt32() != 1 || reader.ReadString() != typeof(Item).FullName)
            throw new InvalidDataException("Expected exactly one native item; the record was not changed.");
        Item item = ReadItem(reader);
        if (reader.BaseStream.Position != reader.BaseStream.Length)
            throw new InvalidDataException("Unrecognized trailing inventory data.");
        rotation = 0;
        var world = record.components.LastOrDefault(c => IsComponent(c, "WorldItem"));
        if (world != null && Fields(Storage(world.dataBase64)).TryGetValue("rotation", out object? angle))
            rotation = (int)angle;
        return item;
    }
    internal static StructureObject Create(Item item, float x, float y, int rotation)
    {
        var record = new StructureObject { prefabName = "prefab_item", x = x, y = y, z = 1f };
        Write(record, item, rotation);
        return record;
    }
    internal static void Write(StructureObject record, Item item, int rotation)
    {
        // Populate without TryAddItem: that runs gameplay callbacks (gold,
        // quest items, etc.) and must never happen in the offline editor.
        var inventory = new Inventory();
        inventory.items.Add(item);
        byte[] inventoryBytes = Bytes(inventory);
        var edited = new StructureObject { components = record.components.Select(component => new StructureComponent
        {
            type = component.type, componentIndex = component.componentIndex,
            hierarchyPath = component.hierarchyPath, dataBase64 = component.dataBase64
        }).ToList() };
        Update(edited, "WorldInventory", fields =>
        {
            if (fields.TryGetValue("inventory", out var original) && original is byte[] previous)
            {
                var existing = Storage(Convert.ToBase64String(previous));
                var replacement = Storage(Convert.ToBase64String(inventoryBytes));
                Fields(existing)["items"] = Fields(replacement)["items"];
                fields["inventory"] = Bytes(existing);
            }
            else fields["inventory"] = inventoryBytes;
            fields["initialized"] = true;
            fields["dirty"] = true;
            fields["summary"] = item.name;
        });
        Update(edited, "WorldItem", fields =>
        {
            fields["rotation"] = ItemCustomizationRules.NormalizeRotation(rotation);
            if (!fields.ContainsKey("owned")) fields["owned"] = false;
        });
        record.components = edited.components;
    }
    internal static void Update(StructureObject record, string type, Action<Dictionary<string, object>> change)
    {
        var component = record.components.SingleOrDefault(c => IsComponent(c, type));
        var storage = component == null ? new VersionSafeStorage() : Storage(component.dataBase64);
        change(Fields(storage));
        string payload = Convert.ToBase64String(Bytes(storage));
        if (component == null)
            record.components.Add(new StructureComponent { type = type + ", Assembly-CSharp", componentIndex = 0, dataBase64 = payload });
        else component.dataBase64 = payload;
    }
}

// Quality-only custom items must not acquire weapon/food/ingredient mechanics.
// This is serialized inside the native Item, including after player pickup.
public sealed class StructureItemQuality : ItemComponent, IQualityItemComponent, IItemValueMultiplier
{
    public Quality Quality { get; set; }
    public float GetMultiplier() => (int)Quality + 1f;
    public override void Serialize(BinaryWriter writer) => writer.Write((int)Quality);
    public override void Deserialize(BinaryReader reader)
    {
        int value = reader.ReadInt32();
        if (!Enum.IsDefined(typeof(Quality), value)) throw new InvalidDataException("Unknown item quality.");
        Quality = (Quality)value;
    }
    private static bool installed;
    internal static void InstallTypeResolver()
    {
        if (installed) return;
        installed = true;
        // Process-lifetime, static, scoped to our one type. The BepInEx startup
        // host is transient; this must remain registered after its destruction.
        AppDomain.CurrentDomain.TypeResolve += (_, args) =>
            args.Name.Split(',')[0] == typeof(StructureItemQuality).FullName ? typeof(StructureItemQuality).Assembly : null;
    }
}
