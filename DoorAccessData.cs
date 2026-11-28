#nullable enable
using System;
using System.IO;

namespace StructureHandler;

// The game's Door already saves publicAccess in its VersionSafeStorage.
// Keep a single source of truth for export, import, duplication and save/load.
internal static class DoorAccessData
{
    internal static StructureComponent? Saved(StructureObject record)
    {
        if (record.components == null) return null;
        // Match final-writes import semantics, including old aliases for index 0.
        for (int i = record.components.Count - 1; i >= 0; --i)
        {
            var c = record.components[i];
            if (c != null && string.IsNullOrWhiteSpace(c.hierarchyPath) && c.componentIndex is -1 or 0 &&
                SavedComponentBinding.IsNative(c.type, "Door")) return c;
        }
        return null;
    }
    internal static string FromTemplate(Door door)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        door.Serialize(writer);
        writer.Flush();
        return Convert.ToBase64String(stream.ToArray());
    }
    private static int Read(VersionSafeStorage storage)
    {
        // No audited native Deserialize/getters in the editor repaint loop.
        if (StructureItemData.Fields(storage).TryGetValue("publicAccess", out var value) && value is int n)
            return n;
        throw new InvalidDataException("The saved Door has no integer publicAccess field.");
    }
    internal static int Read(string payload) => Read(StructureItemData.Storage(payload));

    internal static void Set(StructureObject record, string templatePayload, Direction side)
    {
        if (side is not (Direction.Up or Direction.Down or Direction.Left or Direction.Right))
            throw new ArgumentOutOfRangeException(nameof(side));
        var saved = Saved(record);
        // A quick-placed door may have no component records yet. Start from
        // the COMPLETE native template in that case, not a one-field payload.
        var storage = StructureItemData.Storage(saved == null ? templatePayload : saved.dataBase64);
        if (Read(storage) == (int)side) return;
        StructureItemData.Fields(storage)["publicAccess"] = (int)side;
        string payload = Convert.ToBase64String(StructureItemData.Bytes(storage));
        if (saved != null) saved.dataBase64 = payload;
        else (record.components ??= new()).Add(new StructureComponent {
            type = "Door, Assembly-CSharp", componentIndex = 0, dataBase64 = payload
        });
    }
}
