#nullable enable
namespace StructureHandler;

internal static class PooledGrass
{
    internal static string? Id(string name) => name switch {
        "prefab_tile_grass" or "prefab_tile_grass(Clone)" => "prefab_tile_grass",
        "prefab_tile_grass_swamp" or "prefab_tile_grass_swamp(Clone)" => "prefab_tile_grass_swamp",
        _ => null };
}
