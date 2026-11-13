#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace StructureHandler;

internal sealed partial class StructureEditorUI
{
    private bool ignorePlacementRules;

    private void DrawPlacementRulesToggle()
    {
        bool next = GUILayout.Toggle(ignorePlacementRules, "Ignore Placement Rules", GUI.skin.button);
        if (next != ignorePlacementRules)
        {
            ignorePlacementRules = next;
            lastBrushCell = null;
            status = next
                ? "Free placement: fractional nudges and blocked/water/wall placement allowed. Tile slots and item-data checks remain enforced."
                : "Native snapping and placement checks restored for future edits; existing positions are unchanged.";
        }
        if (ignorePlacementRules)
            GUILayout.Label("Free placement ON: overlaps may obstruct paths or interactions. Ground/wall slots remain exclusive.");
    }

    private bool ShouldSnap(PlacementProfile profile) =>
        StructurePlacementPolicy.ShouldSnap(ignorePlacementRules, profile.Construction, profile.Pickup, profile.Snap);

    private void DrawNudgeControls(StructureObject item)
    {
        nudgeStep = LabeledTextField("Nudge", nudgeStep);
        float step = TryFloat(nudgeStep, out float parsedStep) && parsedStep > 0f
            ? parsedStep : 0.25f;
        step = StructurePlacementPolicy.NudgeStep(step, ShouldSnap(Profile(item.prefabName)));
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Left")) ChangeObjectPlacement(item, item.x - step, item.y);
        if (GUILayout.Button("Right")) ChangeObjectPlacement(item, item.x + step, item.y);
        if (GUILayout.Button("Up")) ChangeObjectPlacement(item, item.x, item.y + step);
        if (GUILayout.Button("Down")) ChangeObjectPlacement(item, item.x, item.y - step);
        GUILayout.EndHorizontal();
    }

    private sealed class PlacementProfile
    {
        internal PlacementSlot Slot;
        internal bool Construction, Pickup, AllowWall, Water;
        internal int Snap = -1;
        internal readonly List<(Vector2Int Offset, bool Root, bool Passable)> Colliders = new();
    }
    private static readonly Dictionary<string, PlacementProfile> placementProfiles = new(StringComparer.OrdinalIgnoreCase);

    private static PlacementProfile Profile(string name)
    {
        if (placementProfiles.TryGetValue(name, out var result)) return result;
        result = new PlacementProfile { Slot = StructurePlacementPolicy.Slot(name) };
        GameObject? prefab = Plugin.ResolvePrefab(name);
        if (prefab != null)
        {
            if (prefab.GetComponent<GrassTileHandler>() != null) result.Slot = PlacementSlot.Ground;
            if (prefab.GetComponent<BathhouseSanityRegenerator>() != null) result.Slot = PlacementSlot.WaterOverlay;
            if (prefab.GetComponent<TileMapWallHandler>() != null && result.Slot == PlacementSlot.None)
                result.Slot = PlacementSlot.Wall;
            var pickup = prefab.GetComponent<Pickupable>();
            result.Pickup = pickup != null;
            result.AllowWall = pickup != null && pickup.allowWallPlacement;
            result.Snap = pickup != null ? pickup.snappingOffset : 0;
            result.Construction = result.Slot == PlacementSlot.Wall || prefab.GetComponent<GrassTileHandler>() != null ||
                name.Equals("prefab_furniture_firepit", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("prefab_furniture_fireplace", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("prefab_furniture_pillar_stone", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("prefab_furniture_bathtub", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("prefab_furniture_sign", StringComparison.OrdinalIgnoreCase);
            if (name.Equals("prefab_furniture_fireplace", StringComparison.OrdinalIgnoreCase)) result.Snap = 5;
            result.Water = prefab.GetComponentsInChildren<TileTags>(false)
                .Any(tags => tags.tags != null && tags.tags.Contains(TileTag.Submerge));
            foreach (var collider in prefab.GetComponentsInChildren<TurfCollider>(false))
                result.Colliders.Add((Vector2Int.RoundToInt(collider.transform.position - prefab.transform.position),
                    collider.transform == prefab.transform, collider.passable));
        }
        placementProfiles[name] = result;
        return result;
    }

    internal static PlacementSlot GetPlacementSlot(string name) => Profile(name).Slot;

    private bool CheckPlacement(StructureObject candidate, bool replaceSlot, object? ignore = null)
    {
        if (structure == null) return false;
        if (StructureItemData.IsItem(candidate) &&
            !candidate.components.Any(component => StructureItemData.IsComponent(component, "WorldInventory")))
        {
            status = "Use the Items tab to create a populated item; an empty prefab_item is not placeable.";
            return false;
        }
        var profile = Profile(candidate.prefabName);
        var cells = GetObjectCells(candidate);
        Vector2Int anchor = Vector2Int.RoundToInt(new Vector2(candidate.x, candidate.y));
        bool water = false, blocked = false, wallBlocked = false;
        // Use native collider positions, not sprite bounds: a tall sprite is not
        // an extra collision cell. Native multi-part blockers have child colliders.
        bool Inspect(object record, string name, float x, float y, IEnumerable<Vector2Int> occupied,
            List<StructureComponent>? components)
        {
            if (ReferenceEquals(record, ignore)) return true;
            var existing = Profile(name);
            bool sameSlot = profile.Slot != PlacementSlot.None && existing.Slot == profile.Slot && occupied.Any(cells.Contains);
            if (sameSlot)
            {
                if (replaceSlot) return true;
                status = $"{profile.Slot} slot is occupied by {name}. Paint to replace it, or choose another cell.";
                return false;
            }
            var existingAnchor = Vector2Int.RoundToInt(new Vector2(x, y));
            if (existingAnchor == anchor && existing.Water) water = true;
            foreach (var collider in existing.Colliders)
            {
                if (existingAnchor + collider.Offset != anchor) continue;
                bool blocks = !collider.Passable;
                if (collider.Root && FurnitureCollision.TryReadEnabled(components, collider.Passable, out bool savedBlocks))
                    blocks = savedBlocks;
                if (!blocks) continue;
                blocked = true;
                wallBlocked |= name.Contains("wall", StringComparison.OrdinalIgnoreCase);
            }
            return true;
        }
        bool rebuiltMovingRecord = placementIndexDirty && ignore != null;
        CollectPlacementCandidates(cells.Append(anchor));
        // Callers temporarily move the candidate before validating. If a cold
        // index saw that tentative position, don't retain it after a rejection.
        if (rebuiltMovingRecord) placementIndexDirty = true;
        foreach (var record in placementCandidates)
        {
            if (ReferenceEquals(record, ignore)) continue;
            if (record is SupportingTerrain terrain)
            {
                if (!Inspect(terrain, terrain.prefabName, terrain.x, terrain.y,
                    new[] { new Vector2Int(terrain.x, terrain.y) }, terrain.components)) return false;
            }
            else
            {
                var item = (StructureObject)record;
                if (!Inspect(item, item.prefabName, item.x, item.y, CachedCells(item), item.components)) return false;
            }
        }
        // Editing the ground layer must remain possible under an existing wall.
        // It replaces the old surface, just as GrassTileHandler does, rather than
        // creating another object or retroactively invoking gameplay callbacks.
        string? reason = profile.Slot == PlacementSlot.Ground ? null :
            StructurePlacementPolicy.Rejection(profile.Construction, profile.Pickup,
                profile.Snap, profile.AllowWall, water, blocked, wallBlocked, ignorePlacementRules);
        if (reason == null) return true;
        status = reason;
        return false;
    }

    private int RemoveSlotOccupants(string name, HashSet<Vector2Int> cells, object? keep = null)
    {
        var slot = GetPlacementSlot(name);
        if (slot == PlacementSlot.None) return 0;
        CollectPlacementCandidates(cells);
        var targets = new HashSet<object>();
        foreach (var record in placementCandidates)
        {
            string existing = record is SupportingTerrain tile ? tile.prefabName : ((StructureObject)record).prefabName;
            if (!ReferenceEquals(record, keep) && GetPlacementSlot(existing) == slot && RecordOccupies(record, cells)) targets.Add(record);
        }
        return RemoveIndexedRecords(targets);
    }

    private void SnapEditorPosition(StructureObject item)
    {
        var profile = Profile(item.prefabName);
        if (!ShouldSnap(profile)) return;
        float offset = profile.Snap / 32f;
        item.x = Mathf.Round(item.x);
        item.y = Mathf.Round(item.y - offset) + offset;
    }

    private bool PlaceEditorObject(StructureObject item, bool replaceSlot)
    {
        SnapEditorPosition(item);
        if (!CheckPlacement(item, replaceSlot)) return false;
        int removed = replaceSlot ? RemoveSlotOccupants(item.prefabName, GetObjectCells(item)) : 0;
        structure!.objects.Add(item);
        selectedObject = structure.objects.Count - 1;
        selectedTerrain = -1;
        MarkStructureDirty(changedRecord: item);
        status = $"Placed {CatalogDisplayName(item.prefabName)} at {Format(item.x)}, {Format(item.y)}." +
            (removed > 0 ? $" Replaced {removed} record(s) in that slot; Undo restores them." : "");
        return true;
    }

    private void ChangeObjectPlacement(StructureObject item, float x, float y, int? rotation = null)
    {
        if (item.x == x && item.y == y && (!rotation.HasValue || rotation.Value == item.turnableIndex)) return;
        float oldX = item.x, oldY = item.y;
        int oldRotation = item.turnableIndex;
        var oldOffsets = item.occupiedOffsets;
        item.x = x; item.y = y;
        SnapEditorPosition(item);
        if (rotation.HasValue)
        {
            item.turnableIndex = rotation.Value;
            item.occupiedOffsets = GetPrefabOccupiedOffsets(item.prefabName, rotation.Value);
        }
        if (!CheckPlacement(item, false, item))
        { item.x = oldX; item.y = oldY; item.turnableIndex = oldRotation; item.occupiedOffsets = oldOffsets; return; }
        bool snapped = item.x != x || item.y != y;
        if (item.x == oldX && item.y == oldY && item.turnableIndex == oldRotation)
        {
            status = $"Native snapping kept the position at {Format(item.x)}, {Format(item.y)}. Enable Ignore Placement Rules for fractional movement.";
            return;
        }
        MarkStructureDirty(changedRecord: item);
        status = $"Position: {Format(item.x)}, {Format(item.y)}." +
            (snapped ? " Adjusted to native snapping; enable Ignore Placement Rules for fractional movement." : "");
    }

    private bool PlaceEditorTerrain(SupportingTerrain item, bool replaceSlot, bool moving = false)
    {
        var candidate = new StructureObject { prefabName = item.prefabName, x = item.x, y = item.y, z = item.z };
        if (!CheckPlacement(candidate, replaceSlot, moving ? item : null)) return false;
        int removed = replaceSlot ? RemoveSlotOccupants(item.prefabName, GetObjectCells(candidate), item) : 0;
        if (!moving) structure!.supportingTerrain.Add(item);
        selectedTerrain = structure!.supportingTerrain.IndexOf(item);
        selectedObject = -1;
        MarkStructureDirty(changedRecord: item);
        status = $"Placed {CatalogDisplayName(item.prefabName)} at {item.x}, {item.y}." +
            (removed > 0 ? $" Replaced {removed} record(s) in that slot; Undo restores them." : "");
        return true;
    }

    private void ChangeTerrainPlacement(SupportingTerrain item, int x, int y)
    {
        if (item.x == x && item.y == y) return;
        int oldX = item.x, oldY = item.y;
        item.x = x; item.y = y;
        if (!PlaceEditorTerrain(item, false, true)) { item.x = oldX; item.y = oldY; }
    }

    private int conflictRevision = -1;
    private StructureFile? conflictStructure;
    private string? cachedPlacementConflict;
    private string? PlacementConflict()
    {
        if (structure == null) return null;
        if (conflictRevision != placementRevision || !ReferenceEquals(conflictStructure, structure))
        {
            cachedPlacementConflict = StructurePlacementPolicy.FindConflict(structure, GetPlacementSlot);
            conflictRevision = placementRevision;
            conflictStructure = structure;
        }
        return cachedPlacementConflict;
    }
}
