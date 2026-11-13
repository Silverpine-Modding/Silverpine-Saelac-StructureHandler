using BepInEx.Configuration;
using StructureHandler;

internal static class WaterSettingsChecks
{
    internal static void Run(Action<bool> check)
    {
        string directory = Path.Combine(Path.GetTempPath(), "StructureHandler-water-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "water.cfg");
        var oldKey = new ConfigDefinition("Import", "ImportOtherWaterTiles");
        try
        {
            foreach (bool legacyValue in new[] { false, true })
            {
                var original = new ConfigFile(path, false) { SaveOnConfigSet = false };
                original.Bind(oldKey, legacyValue).Value = legacyValue;
                original.Bind("Unrelated", "KeepMe", "unchanged");
                original.Save();
                var migrated = new ConfigFile(path, false) { SaveOnConfigSet = false };
                var setting = WaterExportSettings.Bind(migrated);
                check(setting.Value == legacyValue && !migrated.ContainsKey(oldKey));
                check(!migrated.SaveOnConfigSet);
                check(migrated.Bind("Unrelated", "KeepMe", "wrong").Value == "unchanged");
                migrated.Save();
                check(!File.ReadAllText(path).Contains("ImportOtherWaterTiles"));
                var reloaded = new ConfigFile(path, false) { SaveOnConfigSet = false };
                check(WaterExportSettings.Bind(reloaded).Value == legacyValue);
                File.Delete(path);
                Console.WriteLine($"PASS Existing water setting {legacyValue} migrates to export-only and survives config reload without losing other settings.");
            }
            var both = new ConfigFile(path, false) { SaveOnConfigSet = false };
            both.Bind(oldKey, true);
            both.Bind("Export", "ExportOtherWaterTiles", false);
            both.Save();
            var upgraded = new ConfigFile(path, false); // Autosave enabled: exercise that path too.
            check(!WaterExportSettings.Bind(upgraded).Value && upgraded.SaveOnConfigSet);
            check(!File.ReadAllText(path).Contains("ImportOtherWaterTiles"));
            Console.WriteLine("PASS New export preference wins over a leftover old key and preserves config autosave.");
            File.Delete(path);
            var fresh = new ConfigFile(path, false) { SaveOnConfigSet = false };
            check(!WaterExportSettings.Bind(fresh).Value && !fresh.ContainsKey(oldKey));
            Console.WriteLine("PASS Fresh configuration defaults to excluding non-bath water from exports only.");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            Directory.Delete(directory);
        }
    }
}
