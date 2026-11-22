using StructureHandler;

internal static class KitsuneInscriptionChecks
{
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        const string original = "Prefab inscription";
        const string first = "Fox shrine\n月明かり 🦊", second = "A different statue";
        static byte[] Save(string text, int rotation)
        {
            using var frame = new MemoryStream();
            using var writer = new BinaryWriter(frame);
            // Actual native Turnable layout: one VersionSafeStorage integer.
            writer.Write((byte)1); writer.Write("index"); writer.Write((byte)2); writer.Write(rotation);
            KitsuneInscriptionPayload.Append(writer, text, original);
            return frame.ToArray();
        }
        static (int, string) Load(byte[] bytes)
        {
            using var reader = new BinaryReader(new MemoryStream(bytes));
            if (reader.ReadByte() != 1 || reader.ReadString() != "index" || reader.ReadByte() != 2) throw new InvalidDataException();
            int rotation = reader.ReadInt32();
            long position = reader.BaseStream.Position;
            var text = KitsuneInscriptionPayload.Read(reader);
            if (position != reader.BaseStream.Position) throw new InvalidDataException("Native cursor moved");
            return (rotation, text ?? original);
        }
        test("Two Kitsune inscriptions survive tile and full-save round trips independently of position or load order", () =>
        {
            var a = Load(Save(first, 2)); var b = Load(Save(second, 1));
            check(a == (2, first) && b == (1, second));
            using var file = new MemoryStream();
            var writer = new BinaryWriter(file);
            foreach (var state in new[] { b, a }) { var block = Save(state.Item2, state.Item1); writer.Write(block.Length); writer.Write(block); }
            file.Position = 0;
            var reader = new BinaryReader(file);
            check(Load(reader.ReadBytes(reader.ReadInt32())) == (1, second));
            check(Load(reader.ReadBytes(reader.ReadInt32())) == (2, first) && file.Position == file.Length);
        });
        test("Intentionally blank Kitsune inscription differs from a legacy/unedited statue", () =>
        {
            var vanilla = Save(original, 3);
            check(vanilla.Length == 12 && Load(vanilla) == (3, original));
            var blank = Save("", 3);
            check(blank.Length > vanilla.Length && blank.Take(vanilla.Length).SequenceEqual(vanilla) &&
                Load(blank) == (3, ""));
        });
        test("Truncated, foreign or malformed inscription suffix leaves rotation and prefab default intact", () =>
        {
            byte[] saved = Save("a", 1);
            check(Load(saved[..^1]) == (1, original));
            byte[] invalidLength = (byte[])saved.Clone();
            Array.Fill(invalidLength, (byte)0xff, saved.Length - 12, 4);
            check(Load(invalidLength) == (1, original));
            saved[12] = 0xff;
            check(Load(saved) == (1, original));
        });
    }
}
