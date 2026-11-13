using StructureHandler;
using UnityEngine;

internal static class ItemStorageChecks
{
    internal static void Run(Action<bool> check)
    {
        var native = new VersionSafeStorage();
        native.AddToStorage("bool", true);
        native.AddToStorage("float", 1.25f);
        native.AddToStorage("int", -17);
        native.AddToStorage("string", "A gardener's note — 雨");
        native.AddToStorage("color", new Color(.1f, .2f, .3f, .4f));
        native.AddToStorage("cell", new Vector2Int(-9, 14));
        native.AddToStorage("point", new Vector2(-.25f, .125f));
        native.AddToStorage("blob", new byte[] { 0, 127, 255 });
        native.AddToStorage("empty blob", Array.Empty<byte>());
        byte[] bytes = StructureItemData.Bytes(native);
        var decoded = StructureItemData.Storage(Convert.ToBase64String(bytes));
        check(StructureItemData.Bytes(decoded).SequenceEqual(bytes));
        check(decoded.GetBool("bool", false) && decoded.GetInt("int", 0) == -17);
        check(decoded.GetFloat("float", 0) == 1.25f && decoded.GetString("string", "") == "A gardener's note — 雨");
        check(decoded.GetColor("color", default).a == .4f && decoded.GetVector2Int("cell", default).x == -9);
        check(decoded.GetVector2("point", default).y == .125f);
        check(decoded.TryCreateBinaryReader("blob", out var blob));
        using (blob) check(blob.ReadBytes(3).SequenceEqual(new byte[] { 0, 127, 255 }));
        Console.WriteLine("PASS Editor storage reader preserves all eight native types and unknown fields byte-for-byte, without scheduling the native unread-field audit.");

        byte[] Envelope(Action<BinaryWriter> write)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) write(writer);
            return stream.ToArray();
        }
        void Reject(byte[] data)
        {
            try { StructureItemData.Storage(Convert.ToBase64String(data)); }
            catch (Exception error) when (error is InvalidDataException || error is EndOfStreamException) { return; }
            throw new Exception("Malformed item storage was accepted.");
        }
        Reject(Array.Empty<byte>());
        Reject(Envelope(w => { w.Write((byte)1); w.Write("future"); w.Write((byte)8); }));
        Reject(Envelope(w => { w.Write((byte)1); w.Write("truncated int"); w.Write((byte)2); w.Write((byte)0); }));
        foreach (int length in new[] { -1, 3, int.MaxValue })
            Reject(Envelope(w => { w.Write((byte)1); w.Write("blob"); w.Write((byte)7); w.Write(length); w.Write((byte)7); }));
        Reject(Envelope(w => { w.Write((byte)2); for (int i = 0; i < 2; i++) { w.Write("duplicate"); w.Write((byte)0); w.Write(true); } }));
        Reject(bytes.Concat(new byte[] { 0 }).ToArray());
        Console.WriteLine("PASS Editor storage reader rejects unknown types, duplicate names, truncated fields, invalid blob sizes and trailing data rather than suppressing real errors.");

        var record = new StructureObject { prefabName = "prefab_item", components = new() {
            new() { type = "WorldItem, Assembly-CSharp", dataBase64 = Convert.ToBase64String(bytes) } } };
        string previous = record.components[0].dataBase64;
        try
        {
            StructureItemData.Update(record, "WorldItem", fields => throw new InvalidDataException("Invalid edit"));
            throw new Exception("Expected rejected edit.");
        }
        catch (InvalidDataException) { }
        check(record.components[0].dataBase64 == previous);
        Console.WriteLine("PASS Failed editor storage changes leave the original component payload intact.");

        var plants = new StructureFile { objects = new() {
            new() { prefabName = "prefab_furniture_pottedplant", x = 7.125f, y = -2.75f, z = 1 },
            new() { prefabName = "prefab_furniture_pottedplant", x = 7.25f, y = -2.625f, z = 1 } } };
        var reloaded = (StructureFile)StringSerializationAPI.Deserialize(typeof(StructureFile),
            StringSerializationAPI.Serialize(typeof(StructureFile), plants));
        check(reloaded.objects[0].x == 7.125f && reloaded.objects[0].y == -2.75f);
        check(reloaded.objects[1].x == 7.25f && reloaded.objects[1].y == -2.625f);
        Console.WriteLine("PASS Nudged potted-plant coordinates survive native JSON save/load without a new per-file override flag.");
    }
}
