#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace StructureHandler;

internal static class DestinationGround
{
    private static readonly FieldInfo Previous = AccessTools.Field(typeof(GrassTileHandler), "previousTilePrefabName");

    internal static Dictionary<Vector2Int, string> Capture(StructureFile file, IEnumerable<GameObject> originals,
        Func<string, bool> includeTerrain)
    {
        var required = new HashSet<Vector2Int>();
        var offsets = new Dictionary<string, Vector3[]>(StringComparer.OrdinalIgnoreCase);
        void Add(string prefabName, float x, float y)
        {
            if (!offsets.TryGetValue(prefabName, out var positions))
            {
                var prefab = Plugin.ResolvePrefab(prefabName) ?? throw new InvalidDataException("Missing prefab: " + prefabName);
                positions = prefab.GetComponentsInChildren<GrassTileHandler>(true)
                    .Select(handler => handler.transform.position - prefab.transform.position).ToArray();
                offsets[prefabName] = positions;
            }
            foreach (var offset in positions)
                required.Add(new Vector2Int(Mathf.RoundToInt(x + offset.x), Mathf.RoundToInt(y + offset.y)));
        }
        foreach (var item in file.objects) Add(item.prefabName, item.x, item.y);
        foreach (var item in file.supportingTerrain.Where(item => includeTerrain(item.prefabName))) Add(item.prefabName, item.x, item.y);
        if (required.Count == 0) return new();

        var byCell = new Dictionary<Vector2Int, List<(string Prefab, bool Constructed, string? Previous, float Z)>>();
        foreach (var item in originals.Where(item => item != null && item.activeInHierarchy && StructureTransfer.IsTerrainObject(item)))
        {
            // A world-exit utility tile is not a reconstructible ground type:
            // its direction cannot be retained in GrassTileHandler's one string.
            if (item.GetComponentInChildren<WorldTileChanger>(true) != null) continue;
            var handler = item.GetComponent<GrassTileHandler>();
            var cell = item.transform.GetVector2IntPosition();
            if (!required.Contains(cell)) continue;
            if (!byCell.TryGetValue(cell, out var candidates)) byCell[cell] = candidates = new();
            candidates.Add((SerializationManager.GetPrefabName(item), handler != null,
                handler == null ? null : Previous.GetValue(handler) as string, item.transform.position.z));
        }

        var result = new Dictionary<Vector2Int, string>();
        foreach (var cell in required)
        {
            string? name = byCell.TryGetValue(cell, out var candidates) ? DestinationGroundPolicy.Choose(candidates) : null;
            // Fail before replacement instead of recording exported/default
            // grass, an old building, or a prefab native deconstruction cannot spawn.
            if (string.IsNullOrWhiteSpace(name) || SerializationManager.GetPrefabFromName(name) == null)
                throw new InvalidDataException($"Cannot determine restorable destination ground beneath the floor at {cell.x}, {cell.y}. This import part was not applied.");
            result.Add(cell, name);
        }
        return result;
    }

    internal static void Apply(IEnumerable<GameObject> created, IReadOnlyDictionary<Vector2Int, string> ground)
    {
        foreach (var handler in created.SelectMany(item => item.GetComponentsInChildren<GrassTileHandler>(true)))
        {
            var cell = handler.transform.GetVector2IntPosition();
            if (!ground.TryGetValue(cell, out var name))
                throw new InvalidDataException($"Missing captured destination ground at {cell.x}, {cell.y}.");
            Previous.SetValue(handler, name);
        }
    }

    internal static GameObject InstantiateRestoredGround(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        var result = UnityEngine.Object.Instantiate(prefab, position, rotation);
        var cell = result.transform.GetVector2IntPosition();
        if (StructureSaveState.ImportedCells.Contains(cell.x, cell.y))
        {
            // Native deconstruction creates fresh terrain. It is not a pool
            // duplicate; plain dirt needs supplemental saving after restoration.
            StructureTransfer.RegisterRestoredObjects(new[] { result });
            StructureTerrainPersistence.RecordRestoredGround(result);
        }
        return result;
    }
}
