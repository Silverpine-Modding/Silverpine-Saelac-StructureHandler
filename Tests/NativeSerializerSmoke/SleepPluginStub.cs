// Compile-time stand-in for the Unity-dependent plugin. These methods must
// never run in managed smoke tests; live world lookup is checked in the IL audit.
namespace StructureHandler;
internal static class Plugin
{
    internal static UnityEngine.Vector2Int GetWorldTile(UnityEngine.Vector2 position) =>
        throw new System.NotSupportedException("World lookup requires the Unity player.");
    internal static bool IsWorldTilePlayerBuildable(UnityEngine.Vector2Int position) =>
        throw new System.NotSupportedException("World lookup requires the Unity player.");
}
