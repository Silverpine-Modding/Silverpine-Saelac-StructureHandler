#nullable enable
using System;
using System.IO;

namespace StructureHandler;

internal static class TerrainRepairBackup
{
    internal static string Create(string sourcePath, string root, Action<string> snapshot)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(sourcePath) || !string.Equals(Path.GetExtension(sourcePath), ".sav", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Save the game normally once before repairing terrain.");
        string directory = Path.Combine(Path.GetFullPath(root),
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        // Never overwrite or rename the player's real save. Preserve both its
        // last disk version and a fresh checkpoint including unsaved progress.
        File.Copy(sourcePath, Path.Combine(directory, "original.sav"), false);
        CopyIfExists(sourcePath + ".moddingtools", Path.Combine(directory, "original.sav.moddingtools"));
        CopyIfExists(Path.ChangeExtension(sourcePath, ".savmeta"), Path.Combine(directory, "original.savmeta"));
        CopyIfExists(sourcePath + ".structurehandler-resources.json",
            Path.Combine(directory, "original.sav.structurehandler-resources.json"));
        File.WriteAllText(Path.Combine(directory, "RESTORE.txt"),
            "Original save: " + sourcePath + Environment.NewLine +
            "With Silverpine CLOSED, copy original.sav back to the original save path, and restore its original.sav.moddingtools companion alongside it if present. Restore original.savmeta to the matching .savmeta path if needed. Keep the current files elsewhere first." + Environment.NewLine +
            "before-repair.sav and its companion are a fresh pre-repair checkpoint, including unsaved progress. To restore that checkpoint instead, copy BOTH checkpoint files over the matching original save/companion paths. Do not mix original and checkpoint companions. If no companion exists in a chosen pair, do not leave an unrelated companion beside it." + Environment.NewLine +
            "These backups are outside the normal save-slot list. No repair has been auto-saved over the original slot.");
        string checkpoint = Path.Combine(directory, "before-repair.sav");
        snapshot(checkpoint); // A failed backup must abort before world mutation.
        if (!File.Exists(checkpoint) || new FileInfo(checkpoint).Length < 8)
            throw new IOException("The pre-repair checkpoint was not written correctly. No terrain was repaired.");
        return directory;
    }
    private static void CopyIfExists(string source, string destination)
    {
        if (File.Exists(source)) File.Copy(source, destination, false);
    }
}
