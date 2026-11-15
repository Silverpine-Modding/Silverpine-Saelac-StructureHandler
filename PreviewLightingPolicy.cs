#nullable enable

using System;

namespace StructureHandler;

// Editor-only classification. Never change the prefab's renderers/materials.
internal static class PreviewLightingPolicy
{
    internal static bool IsFakeAO(string? name) =>
        name?.IndexOf("fakeao", StringComparison.OrdinalIgnoreCase) >= 0;

    internal static bool IsLightingEffect(
        string? objectName, string? spriteName,
        string? materialName, string? shaderName) =>
        IsEffectName(objectName) || IsFakeAO(spriteName) ||
        IsEffectName(materialName) || IsEffectName(shaderName);

    private static bool IsEffectName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (IsFakeAO(name)) return true;
        // A material such as "TerrainLighting" or "Sprites/Lit" is not a
        // light overlay. Nor is a lantern/furniture prefab a light effect.
        if (name.StartsWith("prefab_", StringComparison.OrdinalIgnoreCase) &&
            !name.StartsWith("prefab_effect_", StringComparison.OrdinalIgnoreCase)) return false;
        foreach (string token in name.Split(new[] { ' ', '_', '/', '-', '(', ')' },
                     StringSplitOptions.RemoveEmptyEntries))
            if (token.Equals("light", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("glow", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("shadow", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("ambient", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("occlusion", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
