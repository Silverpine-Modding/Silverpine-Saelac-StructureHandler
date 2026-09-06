#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
namespace StructureHandler;
internal static class LegacyStructureReader
{
    internal static List<StructureObject> Read(string base64)
    {
        using var compressed = new MemoryStream(Convert.FromBase64String(base64));
        using var decompressed = new BrotliStream(compressed, CompressionMode.Decompress);
        using var reader = new BinaryReader(decompressed);
        int count = Count(reader, 250000);
        long total = 0;
        var objects = new List<StructureObject>(count);
        for (int i = 0; i < count; i++)
        {
            var item = new StructureObject
            {
                prefabName = reader.ReadString(),
                x = reader.ReadSingle(), y = reader.ReadSingle(), z = reader.ReadSingle()
            };
            byte[] payload = Bytes(reader);
            total += payload.Length;
            if (total > 256 * 1024 * 1024)
                throw new InvalidDataException("Legacy structure data exceeds 256 MiB.");
            using var componentReader = new BinaryReader(new MemoryStream(payload));
            int components = Count(componentReader, 10000);
            var indices = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int j = 0; j < components; j++)
            {
                string type = componentReader.ReadString();
                indices.TryGetValue(type, out int index);
                indices[type] = index + 1;
                if (!componentReader.ReadBoolean()) continue;
                item.components.Add(new StructureComponent
                {
                    type = type, componentIndex = index,
                    dataBase64 = Convert.ToBase64String(Bytes(componentReader))
                });
            }
            objects.Add(item);
        }
        return objects;
    }
    private static int Count(BinaryReader reader, int limit)
    {
        int value = reader.ReadInt32();
        if (value < 0 || value > limit) throw new InvalidDataException("Invalid legacy record count.");
        return value;
    }
    private static byte[] Bytes(BinaryReader reader)
    {
        int length = Count(reader, 64 * 1024 * 1024);
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException();
        return bytes;
    }
}
