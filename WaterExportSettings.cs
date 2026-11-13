using BepInEx.Configuration;

namespace StructureHandler;

internal static class WaterExportSettings
{
    internal static ConfigEntry<bool> Bind(ConfigFile config)
    {
        // Bind the old key only to read an existing preference, then remove it.
        // A saved new key takes precedence on every subsequent launch.
        bool saveOnSet = config.SaveOnConfigSet;
        config.SaveOnConfigSet = false;
        ConfigEntry<bool> result;
        try
        {
            var legacy = config.Bind("Import", "ImportOtherWaterTiles", false);
            result = config.Bind("Export", "ExportOtherWaterTiles", legacy.Value,
                "Include non-bath water terrain in world exports. Bathhouse water is always exported. " +
                "Imports always load every water tile in the selected JSON, regardless of this setting.");
            config.Remove(legacy.Definition);
        }
        finally
        {
            config.SaveOnConfigSet = saveOnSet;
        }
        if (saveOnSet) config.Save();
        return result;
    }
}
