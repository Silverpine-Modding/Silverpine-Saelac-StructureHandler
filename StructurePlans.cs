#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace StructureHandler;
// Midnight/restore notifications are not a new visit. A callback from a
// previous visit must not dismiss or resolve a prompt on a later one.
internal sealed class BoundaryPromptVisit
{
    internal int Id { get; private set; }
    internal bool Deferred { get; private set; }
    internal void Enter() { Id++; Deferred = false; }
    internal void Defer(int visit) { if (visit == Id) Deferred = true; }
}

internal static class StructurePlans
{
    // Native Turfs.Register rounds first; WorldTile.UpdateOwned then groups
    // those integer cells into 100x100 tiles. Keep the object's fractional XY.
    internal static (int X, int Y) TileOf(float x, float y) => (Axis(x), Axis(y));
    private static int Axis(float position) =>
        checked((int)Math.Floor((Math.Round(position, MidpointRounding.ToEven) + 50d) / 100d));

    internal static void RequireSingleOwner(StructureObject item)
    {
        if (OwnerConflictAt(item, item.x, item.y) is string conflict)
            throw new InvalidDataException(conflict);
    }

    internal static string? OwnerConflictAt(StructureObject item, float x, float y)
    {
        if (item.occupiedOffsets == null || item.occupiedOffsets.Count == 0) return null;
        var owner = TileOf(x, y);
        foreach (var c in StructurePlacementPolicy.CellsAt(item, x, y))
        {
            if (TileOf(c.X, c.Y) != owner)
                return $"{item.prefabName} at ({x}, {y}) crosses a world-tile boundary at ({c.X}, {c.Y}). Move this object fully onto one tile; the rest of the structure may span tiles.";
        }
        return null;
    }

    internal static void RequireSectionOwner(StructureFile file, int tileX, int tileY)
    {
        if (file.formatVersion != 3 || file.objects == null || file.supportingTerrain == null ||
            file.objects.Any(o => o == null || TileOf(o.x, o.y) != (tileX,tileY)) ||
            file.supportingTerrain.Any(t => t == null || TileOf(t.x,t.y) != (tileX,tileY)))
            throw new InvalidDataException($"This import section does not belong to the loaded world tile ({tileX}, {tileY}).");
        foreach (var o in file.objects) RequireSingleOwner(o);
    }

    internal static Dictionary<(int X, int Y), StructureFile> Partition(StructureFile file)
    {
        var parts = new Dictionary<(int, int), StructureFile>();
        StructureFile Part((int, int) tile)
        {
            if (!parts.TryGetValue(tile, out var part))
                parts[tile] = part = new StructureFile { name = file.name, formatVersion = 3 };
            return part;
        }
        foreach (var item in file.objects)
            Part(TileOf(item.x, item.y)).objects.Add(item);
        // Import all records in the file. Export preferences must not change
        // the footprint or silently remove water from distant queued sections.
        foreach (var item in file.supportingTerrain)
            Part(TileOf(item.x, item.y)).supportingTerrain.Add(item);
        return parts;
    }

    // Correct pending sections from older saves without moving a single live
    // object. Preserve queue order, even when one old entry spans two owners.
    internal static List<PendingTileImport> RehomePending(IEnumerable<PendingTileImport> entries)
    {
        var result = new List<PendingTileImport>();
        var batch = new List<PendingTileImport>();
        var oldOwners = new HashSet<(int, int)>();
        void Flush()
        {
            if (batch.Count == 0) return;
            if (batch.All(p => p.structure.objects.All(o => TileOf(o.x, o.y) == (p.x, p.y)) &&
                p.structure.supportingTerrain.All(t => TileOf(t.x, t.y) == (p.x, p.y))))
                result.AddRange(batch);
            else
            {
                var byOwner = new Dictionary<(int,int), PendingTileImport>();
                foreach (var p in batch)
                foreach (var pair in Partition(p.structure))
                {
                    if (!byOwner.TryGetValue(pair.Key, out var first))
                    {
                        byOwner.Add(pair.Key, first = new PendingTileImport { x = pair.Key.X, y = pair.Key.Y,
                            importId = p.importId, structure = pair.Value });
                        result.Add(first);
                    }
                    else if (!string.IsNullOrEmpty(p.importId))
                        Append(first.structure, pair.Value);
                    else
                        (first.boundaryTail ??= new List<StructureFile>()).Add(pair.Value);
                }
            }
            batch.Clear();
            oldOwners.Clear();
        }
        foreach (var entry in entries)
        {
            if (entry?.structure == null) throw new InvalidDataException("Missing queued structure.");
            var file = entry.structure;
            // Leave malformed/legacy payloads intact for normal import diagnostics.
            if (entry.boundaryTail?.Count > 0 || file.formatVersion != 3 || file.objects == null || file.supportingTerrain == null ||
                file.objects.Any(o => o == null || !(Math.Abs(o.x) <= 10000000 && Math.Abs(o.y) <= 10000000)) ||
                file.supportingTerrain.Any(t => t == null || Math.Abs((long)t.x) > 10000000 || Math.Abs((long)t.y) > 10000000) ||
                (file.objects.Count == 0 && file.supportingTerrain.Count == 0))
            { Flush(); result.Add(entry); continue; }
            if (batch.Count > 0 && (batch[0].importId != entry.importId || batch[0].structure.name != file.name ||
                oldOwners.Contains((entry.x, entry.y))))
                Flush();
            oldOwners.Add((entry.x, entry.y));
            batch.Add(entry);
        }
        Flush();
        return result;
    }

    private static void Append(StructureFile target, StructureFile source)
    {
        target.objects.AddRange(source.objects);
        target.supportingTerrain.AddRange(source.supportingTerrain);
    }

    // Replace only this queue entry, in place. No world edits here. A stale
    // dialog from before a tile clear, load or transaction rollback is a no-op.
    internal static bool ResolveBoundary(List<PendingTileImport> queue, PendingTileImport entry, bool combine,
        Action<StructureFile>? validate = null)
    {
        int index = queue.IndexOf(entry);
        if (index < 0 || entry.boundaryTail == null || entry.boundaryTail.Count == 0) return false;
        var pieces = new[] { entry.structure }.Concat(entry.boundaryTail).ToList();
        foreach (var file in pieces) RequireSectionOwner(file, entry.x, entry.y);
        if (combine)
        {
            var joined = new StructureFile { name = entry.structure.name };
            foreach (var file in pieces) Append(joined, file);
            pieces = new List<StructureFile> { joined };
        }
        // Validate every proposed section before consuming either alternative.
        foreach (var file in pieces) validate?.Invoke(file);
        var ready = pieces.Select(file => new PendingTileImport { x=entry.x, y=entry.y, structure=file,
            importId=Guid.NewGuid().ToString("N") }).ToArray();
        queue.RemoveAt(index);
        queue.InsertRange(index, ready);
        return true;
    }
}
