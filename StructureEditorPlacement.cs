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
        internal bool Construction, Pickup, AllowWall;
        internal int Snap = -1;
        internal readonly List<Vector2> WaterOffsets = new();
        internal readonly List<(Vector2 Offset, string Path, int Index, bool Passable, bool Wall)> Colliders = new();
    }
    private static readonly Dictionary<string, PlacementProfile> placementProfiles = new(StringComparer.OrdinalIgnoreCase);

    private static PlacementProfile Profile(string name)
    {
        if (placementProfiles.TryGetValue(name, out var result)) return result;
        string requested = name;
        result = new PlacementProfile { Slot = StructurePlacementPolicy.Slot(name) };
        GameObject? prefab = Plugin.ResolvePrefab(name);
        if (prefab != null)
        {
            name = Plugin.PrefabNameOf(prefab);
            result.Slot = StructurePlacementPolicy.Slot(name);
            // Native ground registers a turf. A tile-prefixed sprite/utility
            // with no registrar is not evidence that it supplies a floor.
            if (result.Slot == PlacementSlot.Ground && prefab.GetComponent<TurfRegistrar>() == null)
                result.Slot = PlacementSlot.None;
            if (prefab.GetComponent<GrassTileHandler>() != null) result.Slot = PlacementSlot.Ground;
            if (prefab.GetComponent<BathhouseSanityRegenerator>() != null) result.Slot = PlacementSlot.WaterOverlay;
            if (prefab.GetComponent<TileMapWallHandler>() != null && prefab.GetComponent<GrassTileHandler>() == null &&
                result.Slot != PlacementSlot.WaterOverlay)
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
            foreach (var tags in prefab.GetComponentsInChildren<TileTags>(true))
                if (PreviewRendererDiscovery.IsBranchActive(tags.transform, prefab.transform) &&
                    tags.tags != null && tags.tags.Contains(TileTag.Submerge))
                    result.WaterOffsets.Add(tags.transform.position - prefab.transform.position);
            foreach (var collider in prefab.GetComponentsInChildren<TurfCollider>(true))
                if (PreviewRendererDiscovery.IsBranchActive(collider.transform, prefab.transform))
                    result.Colliders.Add((collider.transform.position - prefab.transform.position,
                        StructureTransfer.GetHierarchyPath(prefab.transform, collider.transform),
                        collider.GetType() != typeof(TurfCollider) ? -1 : Array.IndexOf(
                            collider.GetComponents<TurfCollider>().Where(c => c.GetType() == typeof(TurfCollider)).ToArray(), collider),
                        collider.passable,
                        collider.name.Contains("wall", StringComparison.OrdinalIgnoreCase)));
        }
        if (prefab != null) placementProfiles[requested] = result;
        return result;
    }

    internal static PlacementSlot GetPlacementSlot(string name) => Profile(name).Slot;

    // The manual entry fields bypass the Quick Place catalog. Check only the
    // newly entered ID; still allow opening, moving or removing a missing mod's records.
    private bool TryEditorPrefab(string input, out string name, bool? terrain = null,
        List<StructureComponent>? components = null, int lockedVariant = -1, bool sign = false, bool namedZone = false)
    {
        name = input.Trim();
        try
        {
            var prefab = Plugin.ResolvePrefab(name);
            if (prefab == null) { status = $"Prefab '{name}' is not available."; return false; }
            name = Plugin.RegisteredNameResolver()(name);
            if (WorldPrefabPolicy.CannotReconstruct(prefab) is string reason)
            { status = $"Cannot place '{name}': it {reason}."; return false; }
            if (name.Equals("prefab_item", StringComparison.OrdinalIgnoreCase))
            { status = "Use the Items tab to create a populated item."; return false; }
            if (terrain.HasValue && terrain.Value != IsQuickPlaceSupportingTerrain(name))
            { status = "Use Quick Place to switch between a terrain record and an object."; return false; }
            if ((sign && !PrefabHasSign(name)) || (namedZone && !PrefabHasMapZone(name)) ||
                (lockedVariant >= 0 && lockedVariant >= GetSpriteVariants(name).Length))
            { status = "This prefab cannot keep the saved sign, zone or sprite override. Place a new record instead."; return false; }
            StructureTransfer.ValidateComponentData(components, name, prefab);
            return true;
        }
        catch (Exception e)
        { status = "Cannot use this prefab: " + e.Message; return false; }
    }

    private void ChangeObjectPrefab(StructureObject item, string name)
    {
        if (!TryEditorPrefab(name, out var resolved, false, item.components,
            item.lockSpriteVariant ? item.spriteVariantIndex : -1, item.hasEditableSignMessage,
            item.hasMapZoneName && !string.IsNullOrEmpty(item.mapZoneName))) return;
        var draft = item.PlacementDraft();
        draft.prefabName = resolved;
        int rotations = GetTurnableSpriteCount(resolved);
        draft.turnableIndex = item.turnableIndex >= 0 && item.turnableIndex < rotations
            ? item.turnableIndex : GetDefaultTurnableIndex(resolved);
        if (!TryEditorFootprint(resolved, draft.turnableIndex, out draft.occupiedOffsets)) return;
        if (!CheckPlacement(draft, false, item)) return;
        item.prefabName = resolved; item.turnableIndex = draft.turnableIndex;
        item.occupiedOffsets = draft.occupiedOffsets;
        MarkStructureDirty(changedRecord: item);
    }

    private bool CheckPlacement(StructureObject candidate, bool replaceSlot, object? ignore = null)
    {
        if (structure == null) return false;
        if (!StructurePlacementPolicy.FitsAt(candidate, candidate.x, candidate.y))
        { status = "Placement would exceed the supported coordinate range (±10,000,000)."; return false; }
        if (StructurePlans.OwnerConflictAt(candidate, candidate.x, candidate.y) is string conflict)
        { status = conflict; return false; }
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
            var origin = new Vector2(x, y);
            foreach (var offset in existing.WaterOffsets)
                if (RegisteredCell(origin, offset) == anchor) { water = true; break; }
            foreach (var collider in existing.Colliders)
            {
                if (RegisteredCell(origin, collider.Offset) != anchor) continue;
                if (!FurnitureCollision.BlocksAt(components, collider.Path, collider.Index, collider.Passable)) continue;
                blocked = true;
                wallBlocked |= collider.Wall;
            }
            return true;
        }
        CollectPlacementCandidates(cells.Append(anchor));
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
        var draft = item.PlacementDraft();
        draft.x = x; draft.y = y;
        SnapEditorPosition(draft);
        if (rotation.HasValue)
        {
            draft.turnableIndex = rotation.Value;
            if (!TryEditorFootprint(item.prefabName, rotation.Value, out draft.occupiedOffsets)) return;
        }
        if (!CheckPlacement(draft, false, item)) return;
        bool snapped = draft.x != x || draft.y != y;
        if (draft.x == item.x && draft.y == item.y && draft.turnableIndex == item.turnableIndex)
        {
            status = $"Native snapping kept the position at {Format(item.x)}, {Format(item.y)}. Enable Ignore Placement Rules for fractional movement.";
            return;
        }
        item.x = draft.x; item.y = draft.y;
        item.turnableIndex = draft.turnableIndex;
        item.occupiedOffsets = draft.occupiedOffsets;
        MarkStructureDirty(changedRecord: item);
        status = $"Position: {Format(item.x)}, {Format(item.y)}." +
            (snapped ? " Adjusted to native snapping; enable Ignore Placement Rules for fractional movement." : "");
    }

    private bool PlaceEditorTerrain(SupportingTerrain draft, bool replaceSlot, SupportingTerrain? existing = null)
    {
        var candidate = new StructureObject { prefabName = draft.prefabName, x = draft.x, y = draft.y, z = draft.z };
        if (!CheckPlacement(candidate, replaceSlot, existing)) return false;
        int removed = replaceSlot ? RemoveSlotOccupants(draft.prefabName, GetObjectCells(candidate), existing) : 0;
        var item = existing ?? draft;
        bool changed = existing == null || removed != 0 || item.prefabName != draft.prefabName ||
            item.x != draft.x || item.y != draft.y || item.z != draft.z ||
            item.hasMapZoneName != draft.hasMapZoneName || item.mapZoneName != draft.mapZoneName;
        if (existing == null) structure!.supportingTerrain.Add(item);
        else if (changed)
        {
            item.prefabName = draft.prefabName;
            item.x = draft.x; item.y = draft.y; item.z = draft.z;
            item.hasMapZoneName = draft.hasMapZoneName; item.mapZoneName = draft.mapZoneName;
        }
        selectedTerrain = existing == null ? structure!.supportingTerrain.Count - 1 : structure!.supportingTerrain.IndexOf(item);
        selectedObject = -1;
        if (changed) MarkStructureDirty(changedRecord: item);
        status = $"Placed {CatalogDisplayName(item.prefabName)} at {item.x}, {item.y}." +
            (removed > 0 ? $" Replaced {removed} record(s) in that slot; Undo restores them." : "");
        return true;
    }

    private void ChangeTerrainPlacement(SupportingTerrain item, int x, int y)
    {
        if (item.x == x && item.y == y) return;
        var draft = item.PlacementDraft();
        draft.x = x; draft.y = y;
        PlaceEditorTerrain(draft, false, item);
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
