#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;

namespace StructureHandler;

// Temporary migration reader. No Unity objects on the worker, and no native
// blob rewriting. Unrelated prefab payloads are streamed past, not deserialized.
internal sealed class LegacyGrassIndex
{
    internal static bool Enabled(string version) => Version.TryParse(version, out var v) &&
        v >= new Version(1,2,39) && v < new Version(1,2,41);
    private readonly HashSet<(int,int)> imports;
    private readonly Dictionary<(int,int),LegacyGrassCell?> named = new();
    internal LegacyGrassIndex(HashSet<(int,int)> imports) => this.imports = imports;
    internal void Observe(int x, int y, string prefab, string name)
    {
        var id = PooledGrass.Id(prefab);
        if (id == null || string.IsNullOrWhiteSpace(name) || name == "Outside") return;
        if (named.Count >= 1000000) throw new InvalidDataException("Too many named grass cells.");
        if (named.ContainsKey((x,y))) named[(x,y)] = null; // ambiguous layers, even if both have the same name
        else named.Add((x,y), new LegacyGrassCell { x = x, y = y, prefab = id, name = name });
    }
    internal List<LegacyGrassCell> Candidates()
    {
        var proof = new HashSet<(string,string)>(named.Values.Where(c => c != null && imports.Contains((c.x,c.y)))
            .Select(c => (c!.prefab,c.name)));
        return named.Values.Where(c => c != null && !imports.Contains((c.x,c.y)) && proof.Contains((c.prefab,c.name)))
            .Select(c => c!).ToList();
    }
    internal void ReadTile(byte[] blob, (int X,int Y) tile, CancellationToken stop)
    {
        using var input = new BrotliStream(new MemoryStream(blob, false), CompressionMode.Decompress);
        using var reader = new BinaryReader(input);
        int count = Count(reader, 250000);
        long total = 0;
        var scratch = new byte[8192];
        for (int i = 0; i < count; ++i)
        {
            stop.ThrowIfCancellationRequested();
            string prefab = String(reader, 4096);
            float x = reader.ReadSingle(), y = reader.ReadSingle();
            reader.ReadSingle();
            int length = Count(reader, 64 << 20);
            total += length;
            if (total > 256L << 20) throw new InvalidDataException("Oversized world tile.");
            if (PooledGrass.Id(prefab) == null || x != Math.Round(x) || y != Math.Round(y) ||
                Math.Abs(x) > 10000000 || Math.Abs(y) > 10000000 || StructurePlans.TileOf(x,y) != tile)
            {
                while (length != 0)
                {
                    int n = reader.Read(scratch, 0, Math.Min(length, scratch.Length));
                    if (n == 0) throw new EndOfStreamException();
                    length -= n;
                }
                continue;
            }
            if (length > 1 << 20) throw new InvalidDataException("Oversized grass record.");
            using var payload = new MemoryStream(Exact(reader, length), false);
            using var parts = new BinaryReader(payload);
            int components = Count(parts, 10000);
            for (int j = 0; j < components; j++)
            {
                string type = String(parts, 4096);
                if (!parts.ReadBoolean()) continue;
                int size = Count(parts, length);
                long end = payload.Position + size;
                if (end > length) throw new EndOfStreamException();
                if (type == "MapZone")
                {
                    var label = String(parts, size);
                    if (payload.Position != end) throw new InvalidDataException("Unrecognized MapZone data.");
                    Observe((int)x,(int)y,prefab,label);
                }
                else payload.Position = end;
            }
        }
    }
    private static int Count(BinaryReader r, int max)
    {
        int n = r.ReadInt32();
        return n >= 0 && n <= max ? n : throw new InvalidDataException("Invalid record length.");
    }
    private static byte[] Exact(BinaryReader r, int n)
    {
        byte[] bytes = r.ReadBytes(n);
        return bytes.Length == n ? bytes : throw new EndOfStreamException();
    }
    private static string String(BinaryReader r, int limit)
    {
        uint n = 0;
        for (int shift = 0; shift <= 28; shift += 7)
        {
            byte b = r.ReadByte();
            if (shift == 28 && b > 7) break;
            n |= (uint)(b & 127) << shift;
            if ((b & 128) == 0 && n <= limit) return Encoding.UTF8.GetString(Exact(r,(int)n));
            if ((b & 128) == 0) break;
        }
        throw new InvalidDataException("Invalid save string length.");
    }
}
