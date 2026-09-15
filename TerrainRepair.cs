#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Silverpine.ModdingTools;
using UnityEngine;

namespace StructureHandler;

internal static class TerrainRepair
{
    private static string savePath = "";
    private static int epoch;
    private static bool busy, notified;
    private static readonly HashSet<Vector2Int> inspectedTiles = new();
    private static readonly HashSet<Type> GrassComponents = new()
    {
        typeof(Transform), typeof(SpriteRenderer), typeof(RandomRotation), typeof(TurfRegistrar),
        typeof(OvergrowthTile), typeof(StepSoundHandler), typeof(MapZone), typeof(DarknessModeTile),
        typeof(SeasonStepSound), typeof(SeasonRandomSprite), typeof(SnowStepSpawner), typeof(SeasonNPCVisibleObject)
    };
    private sealed class Scan
    {
        internal int Epoch;
        internal TerrainRepairPolicy.Plan Plan = new();
        internal Dictionary<int, GameObject> Objects = new();
    }
    internal static void Reset()
    {
        epoch++;
        savePath = "";
        notified = false;
        inspectedTiles.Clear();
    }
    internal static void RecordSavePath(string path)
    {
        if (!busy) savePath = Path.GetFullPath(path);
    }
    internal static void CheckAfterLoad()
    {
        if (busy || notified || SerializationManager.loadingSave ||
            !StructureSaveState.TryCurrentTile(out var tile) || !inspectedTiles.Add(tile)) return;
        try
        {
            var scan = Inspect();
            if (scan.Plan.Groups.Count == 0 && scan.Plan.Skipped.Count == 0) return;
            notified = true;
            UpperNotificationUI.Instance?.OneOff(
                "Stacked grass terrain detected. Use Mods > Structure Handler > Repair Duplicate Terrain to review. Nothing was removed.");
        }
        catch (Exception error)
        {
            Plugin.Log.LogWarning("Duplicate-terrain detection could not complete; no terrain changed: " + error.Message);
        }
    }
    private static Scan Inspect()
    {
        var scan = new Scan { Epoch = epoch };
        var items = new List<TerrainRepairPolicy.Item>();
        // Include nonserializable supporting layers too, so water/dirt/floors
        // block ambiguous repairs. Assets, inactive pool entries and editor
        // templates are not live terrain. This scan runs on demand, not Update.
        foreach (var transform in UnityEngine.Object.FindObjectsOfType<Transform>())
        {
            GameObject item = transform.gameObject;
            if (!item.activeInHierarchy || !item.scene.IsValid() || !item.scene.isLoaded) continue;
            string prefab = SerializationManager.GetPrefabName(item);
            if (!prefab.StartsWith("prefab_tile_", StringComparison.OrdinalIgnoreCase)) continue;
            // Native edging has a prefab_tile_ name, but it is not a ground
            // layer. Only exclude verified children tracked by OvergrowthTile.
            if (TerrainRepairDecorations.IsNativeOvergrowth(item)) continue;
            Vector3 p = transform.position;
            var turf = Turfs.GetTurfGameObjects(transform.GetVector2IntPosition());
            int id = item.GetInstanceID();
            scan.Objects[id] = item;
            string reason = prefab == TerrainRepairPolicy.Grass ? GrassSafetyReason(item, turf) : "";
            items.Add(new TerrainRepairPolicy.Item
            {
                Id = id, Prefab = prefab, X = p.x, Y = p.y, Z = p.z,
                Safe = prefab == TerrainRepairPolicy.Grass && reason.Length == 0,
                UnsafeReason = reason,
                Order = turf.IndexOf(item)
            });
        }
        scan.Plan = TerrainRepairPolicy.Build(items);
        return scan;
    }
    private static string GrassSafetyReason(GameObject item, List<GameObject> turf)
    {
        if (item.transform.parent != null || item.transform.localScale != Vector3.one ||
            item.hideFlags != HideFlags.None) return "Modified grass transform or object flags";
        if (turf.Count(g => g == item) != 1) return "Grass not registered exactly once in the tile";
        var zone = item.GetComponent<MapZone>();
        if (zone == null || zone.zoneName != "") return "Named or missing grass zone";
        var components = item.GetComponents<Component>();
        // A strict native component set avoids deleting mod-added state or a
        // deliberately edited appearance/supplemental object. Revisit explicitly
        // if a future game version changes this prefab.
        if (components.Length != GrassComponents.Count || components.Any(c => c == null) ||
            !GrassComponents.SetEquals(components.Select(c => c.GetType())) ||
            components.OfType<Behaviour>().Any(c => !c.enabled)) return "Modified or unsupported grass components";
        // OvergrowthTile owns seasonal visual children and removes them during
        // normal pool release. Those are expected native decoration, not custom
        // gameplay state. Unknown children (even renderer-only ones) still block.
        foreach (Transform child in item.transform)
            if (!TerrainRepairDecorations.IsNativeOvergrowth(child.gameObject))
                return "Unrecognized content attached to grass";
        return "";
    }
    internal static void Prompt()
    {
        PauseMenuManager.Instance?.Close();
        if (!Ready()) return;
        try
        {
            Scan scan = Inspect();
            string skipped = $"\nAmbiguous cells left unchanged: {scan.Plan.Skipped.Count}.";
            if (scan.Plan.Skipped.Count != 0)
            {
                skipped += "\n" + scan.Plan.SkipSummary;
                Plugin.Log.LogInfo("Terrain repair skipped cells: " + scan.Plan.SkipSummary.Replace("\n", "; ") +
                    " Examples: " + string.Join("; ", scan.Plan.Skipped.Take(8).Select(p =>
                        $"({p.X}, {p.Y}): {string.Join(", ", p.Reasons)}")));
            }
            if (scan.Plan.RemoveCount == 0)
            {
                DialogBox.Instance.DisplayTextNoDialog("No safely repairable duplicate grass ground found in loaded areas." + skipped);
                return;
            }
            DialogBox.Instance.DisplayTextNoDialog(
                $"Remove {scan.Plan.RemoveCount} redundant grass ground tiles at {scan.Plan.Groups.Count} loaded cells? " +
                "One existing grass tile and its appearance will remain at each cell. This includes cells outside imports. " +
                "Floors, water, named zones, furniture, plants and trees are not removed." + skipped +
                "\nA disk-save backup and fresh pre-repair checkpoint will be created first. Save normally afterward to keep the repair.",
                new DialogOption("Back Up and Repair", () => Apply(scan)),
                new DialogOption("Cancel", null));
        }
        catch (Exception error) { Fail(error); }
    }
    private static bool Ready()
    {
        if (busy || SerializationManager.loadingSave || StructureSaveState.Mutating || StructureSaveState.Unloading || Player.Instance == null)
        {
            DialogBox.Instance.DisplayTextNoDialog("Wait for the world to finish loading or importing before repairing terrain.");
            return false;
        }
        if (ModSaveData.Warnings.Count != 0)
        {
            DialogBox.Instance.DisplayTextNoDialog("Resolve the mod save-data warnings before repairing terrain. A complete pre-repair checkpoint is required.");
            return false;
        }
        if (string.IsNullOrEmpty(savePath) || !File.Exists(savePath))
        {
            DialogBox.Instance.DisplayTextNoDialog("Save the game normally once, then choose Repair Duplicate Terrain again. No terrain was changed.");
            return false;
        }
        return true;
    }
    private static void Apply(Scan preview)
    {
        if (!Ready()) return;
        string backupDirectory = "";
        try
        {
            Scan current = Inspect();
            if (!SamePlan(preview, current))
                throw new InvalidOperationException("The save or terrain changed since the preview. Open Repair Duplicate Terrain again; nothing was removed.");
            busy = true;
            backupDirectory = TerrainRepairBackup.Create(savePath,
                Path.Combine(Application.persistentDataPath, "Saves", "StructureHandler Repair Backups"), path =>
                {
                    SerializationManager.Save(path);
                    if (ModSaveData.Warnings.Count != 0 || !File.Exists(path + ".moddingtools"))
                        throw new IOException("The checkpoint companion could not be saved cleanly. No repair was performed.");
                });
            // Native save hooks may migrate objects. Revalidate after backup,
            // before any deletion, instead of trusting stale GameObject refs.
            current = Inspect();
            if (!SamePlan(preview, current))
                throw new InvalidOperationException("Terrain changed while creating the backup. Review the repair again; nothing was removed.");
            WritePlan(backupDirectory, current);
            var removed = current.Plan.Groups.SelectMany(g => g.Remove)
                .Select(i => current.Objects[i.Id]).ToArray();
            var snapshots = removed.Select(item =>
                (Item: item, Record: StructureTransfer.CreateObjectRecords(new[] { item }).Single())).ToArray();
            StructureSaveState.Mutating = true;
            try
            {
                ImportTransaction.Execute(() => StructureTransfer.RemoveGameObjects(removed), () =>
                {
                    var restored = new List<GameObject>();
                    // Decide before reclaiming any pooled instances: restoring
                    // one record can reactivate another removed object's handle.
                    var records = snapshots.Where(entry => entry.Item == null || !entry.Item.activeInHierarchy)
                        .Select(entry => entry.Record).ToArray();
                    StructureTransfer.InstantiateObjectRecords(records, restored);
                    StructureTransfer.RegisterRestoredObjects(restored);
                    ObjectPool.CallStarts();
                }, "duplicate terrain repair");
            }
            finally { StructureSaveState.Mutating = false; }
            notified = true;
            Plugin.Log.LogInfo($"Repaired {removed.Length} duplicate grass ground objects at {current.Plan.Groups.Count} cells. Backup: {backupDirectory}");
            DialogBox.Instance.DisplayTextNoDialog(
                $"Removed {removed.Length} duplicate grass ground tiles at {current.Plan.Groups.Count} cells. " +
                $"Skipped {current.Plan.Skipped.Count} ambiguous cells.\nSave the game normally (preferably to a new slot) to keep the repair. " +
                "No existing save was overwritten.\nBackup and coordinate report:\n" + backupDirectory);
        }
        catch (Exception error)
        {
            Fail(error, backupDirectory);
        }
        finally { busy = false; }
    }
    private static bool SamePlan(Scan a, Scan b) => a.Epoch == b.Epoch &&
        a.Plan.Signature == b.Plan.Signature && a.Plan.RemoveCount == b.Plan.RemoveCount;
    private static void WritePlan(string directory, Scan scan)
    {
        var lines = new List<string> { "Structure Handler " + Plugin.PluginVersion + " confirmed repair plan", "Source save: " + savePath };
        lines.AddRange(scan.Plan.Groups.Select(g => $"REPAIR ({g.Keep.X}, {g.Keep.Y}, {g.Keep.Z}): keep one existing grass; remove {g.Remove.Count}."));
        lines.AddRange(scan.Plan.Skipped.Select(p => $"SKIP ({p.X}, {p.Y}): {string.Join("; ", p.Reasons)}."));
        File.WriteAllLines(Path.Combine(directory, "repair-plan.txt"), lines);
    }
    private static void Fail(Exception error, string backup = "")
    {
        Plugin.Log.LogError("Duplicate-terrain repair stopped: " + error);
        DialogBox.Instance.DisplayTextNoDialog("Terrain repair stopped: " + error.Message.Truncate(350) +
            (backup.Length == 0 ? "" : "\nPre-repair backup: " + backup));
    }
}
