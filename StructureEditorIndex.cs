#nullable enable
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace StructureHandler;

internal sealed partial class StructureEditorUI
{
    private readonly EditorCellIndex<object> placementIndex = new();
    private readonly HashSet<object> placementCandidates = new();
    private bool placementIndexDirty = true;
    private int placementRevision;

    // Native TurfCollider and TileTags register the child's final world
    // position, not the sum of two independently rounded positions.
    private static Vector2Int RegisteredCell(Vector2 origin, Vector2 offset) =>
        Vector2Int.RoundToInt(origin + offset);

    private void EnsurePlacementIndex()
    {
        if (!placementIndexDirty) return;
        placementIndex.Clear();
        if (structure != null)
        {
            foreach (var tile in structure.supportingTerrain) IndexPlacementRecord(tile);
            foreach (var item in structure.objects) IndexPlacementRecord(item);
        }
        placementIndexDirty = false;
    }

    private void IndexPlacementRecord(object record)
    {
        var cells = new HashSet<(int, int)>();
        string name;
        Vector2 origin;
        if (record is StructureObject item)
        {
            name = item.prefabName;
            origin = new Vector2(item.x, item.y);
            foreach (var cell in GetObjectCells(item)) cells.Add((cell.x, cell.y));
        }
        else
        {
            var tile = (SupportingTerrain)record;
            name = tile.prefabName; origin = new Vector2(tile.x, tile.y);
        }
        var anchor = Vector2Int.RoundToInt(origin);
        cells.Add((anchor.x, anchor.y));
        // A child collider can extend beyond the serialized/sprite footprint.
        // Index it independently so optimized checks retain native restrictions.
        var profile = Profile(name);
        void Add(Vector2 offset)
        {
            var cell = RegisteredCell(origin, offset);
            cells.Add((cell.x, cell.y));
        }
        foreach (var collider in profile.Colliders) Add(collider.Offset);
        foreach (var offset in profile.WaterOffsets) Add(offset);
        placementIndex.Set(record, cells);
    }

    private void CollectPlacementCandidates(IEnumerable<Vector2Int> cells)
    {
        EnsurePlacementIndex();
        placementIndex.Collect(cells.Select(cell => (cell.x, cell.y)), placementCandidates);
    }

    private bool RecordOccupies(object record, HashSet<Vector2Int> cells) => record is SupportingTerrain tile
        ? cells.Contains(new Vector2Int(tile.x, tile.y))
        : GetObjectCells((StructureObject)record).Overlaps(cells);

    private int RemoveIndexedRecords(HashSet<object> targets)
    {
        int count = structure!.objects.RemoveAll(item => targets.Contains(item)) +
            structure.supportingTerrain.RemoveAll(item => targets.Contains(item));
        foreach (var record in targets) placementIndex.Remove(record);
        return count;
    }
}
