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
        return IsFakeAO(name) ||
               name.IndexOf("light", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("glow", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("shadow", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("ambient", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("occlusion", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
