#nullable enable
using System;
using System.IO;
using System.Text;

namespace StructureHandler;

internal static class KitsuneInscriptionPayload
{
    // "SHKITXT1" in the little-endian, length-delimited native component block.
    private const ulong Marker = 0x31545854494B4853UL;
    private const int FooterSize = sizeof(int) + sizeof(ulong);
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static void Append(BinaryWriter writer, string text, string defaultText)
    {
        if (text == defaultText) return;
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        writer.Write(bytes);
        writer.Write(bytes.Length);
        writer.Write(Marker);
    }

    // null = no known override (including old saves), "" = intentionally blank.
    internal static string? Read(BinaryReader reader)
    {
        var stream = reader.BaseStream;
        long original = stream.Position, end = stream.Length;
        if (end - original < FooterSize) return null;
        try
        {
            stream.Position = end - FooterSize;
            int length = reader.ReadInt32();
            if (reader.ReadUInt64() != Marker || length < 0 || length > end - FooterSize - original) return null;
            stream.Position = end - FooterSize - length;
            return Utf8.GetString(reader.ReadBytes(length));
        }
        catch (DecoderFallbackException) { return null; }
        finally { stream.Position = original; }
    }
}
