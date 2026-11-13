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
        Vector2Int anchor;
        if (record is StructureObject item)
        {
            name = item.prefabName;
            anchor = Vector2Int.RoundToInt(new Vector2(item.x, item.y));
            foreach (var cell in GetObjectCells(item)) cells.Add((cell.x, cell.y));
        }
        else
        {
            var tile = (SupportingTerrain)record;
            name = tile.prefabName; anchor = new Vector2Int(tile.x, tile.y);
        }
        cells.Add((anchor.x, anchor.y));
        // A child collider can extend beyond the serialized/sprite footprint.
        // Index it independently so optimized checks retain native restrictions.
        foreach (var collider in Profile(name).Colliders)
            cells.Add((anchor.x + collider.Offset.x, anchor.y + collider.Offset.y));
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
