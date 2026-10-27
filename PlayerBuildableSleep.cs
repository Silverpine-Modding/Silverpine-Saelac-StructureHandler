#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace StructureHandler;

[HarmonyPatch(typeof(Bed), nameof(Bed.OnInteract))]
internal static class PlayerBuildableSleepPatch
{
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var source = instructions.ToList();
        var owner = typeof(Bed).GetField(nameof(Bed.owner));
        var plot = typeof(WorldInfoManager).GetMethod(nameof(WorldInfoManager.IsOnPlayerOwnedPlot), new[] { typeof(Vector2Int) });
        bool IsPlotCall(CodeInstruction instruction) =>
            (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) && Equals(instruction.operand, plot);
        // Fail before changing IL if the native permission checks have changed.
        if (source.Count(i => i.opcode == OpCodes.Ldfld && Equals(i.operand, owner)) != 3 ||
            source.Count(IsPlotCall) != 1)
            throw new InvalidOperationException("Could not identify native bed ownership checks safely.");

        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var readOwner = typeof(PlayerBuildableSleepPatch).GetMethod(nameof(GetSleepOwner), flags);
        var checkPlot = typeof(PlayerBuildableSleepPatch).GetMethod(nameof(IsSleepingPlot), flags);
        return source.Select(instruction =>
        {
            var result = new CodeInstruction(instruction);
            if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, owner))
            {
                result.opcode = OpCodes.Call;
                result.operand = readOwner;
            }
            else if (IsPlotCall(instruction))
            {
                result.opcode = OpCodes.Call;
                result.operand = checkPlot;
            }
            return result;
        }).ToArray();
    }

    internal static NPCName ResolveSleepOwner(NPCName owner, bool playerBuildable) =>
        playerBuildable ? NPCName.None : owner;

    private static NPCName GetSleepOwner(Bed bed) => ResolveSleepOwner(bed.owner,
        Plugin.IsWorldTilePlayerBuildable(Plugin.GetWorldTile(bed.transform.GetVector2IntPosition())));

    private static bool IsSleepingPlot(WorldInfoManager world, Vector2Int position) =>
        Plugin.IsWorldTilePlayerBuildable(Plugin.GetWorldTile(position)) || world.IsOnPlayerOwnedPlot(position);

    // Only permission reads inside player interaction are changed. Bed.owner,
    // NPC routines, rental hours, roof/cold checks and native sleep remain intact.
}
