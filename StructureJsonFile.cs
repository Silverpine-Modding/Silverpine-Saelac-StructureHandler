#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
namespace StructureHandler;

internal sealed class StructureFileChangedException(string revision) : IOException("The structure file changed on disk.")
{
    internal string Revision { get; } = revision;
}

internal static class StructureJsonFile
{
    // A digest is never empty. Distinguish "expected absent" from null
    // (the existing export API, which does not track an open document).
    internal const string Missing = "";

    internal static string Read(string path, out string revision)
    {
        using var stream = File.OpenRead(path);
        using var hash = SHA256.Create();
        revision = Convert.ToBase64String(hash.ComputeHash(stream));
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static FileStream? OpenExisting(string path)
    {
        try { return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete); }
        catch (FileNotFoundException) { return null; }
    }

    internal static string Write(string path, string json, bool allowOverwrite = true, string? expectedRevision = null)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(json);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            using var hash = expectedRevision != null ? SHA256.Create() : null;
            string writtenRevision = hash == null ? Missing : Convert.ToBase64String(hash.ComputeHash(bytes));
            using var original = expectedRevision != null ? OpenExisting(path) : null;
            if (expectedRevision != null)
            {
                var current = original == null ? Missing : Convert.ToBase64String(hash!.ComputeHash(original));
                if (current != expectedRevision) throw new StructureFileChangedException(current);
            }
            // Never switch a checked create into an overwrite if another program
            // creates the file between comparison and commit.
            if (allowOverwrite && (expectedRevision != null ? original != null : File.Exists(path)))
                File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
            return writtenRevision;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
