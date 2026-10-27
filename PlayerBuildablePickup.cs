#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace StructureHandler;

[HarmonyPatch]
internal static class PlayerBuildablePickupPatch
{
    internal static IEnumerable<MethodBase> TargetMethods()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        yield return typeof(Pickupable).GetMethod("SetEnabled", flags)!;
        yield return typeof(Pickupable).GetMethod("OnMouseUpAsButton", flags)!;
    }

    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var source = instructions.ToList();
        var owned = typeof(Pickupable).GetField(nameof(Pickupable.owned));
        // Both native methods have exactly one ownership read. Preserve the
        // surrounding distance/active/inventory checks and all pickup behavior.
        if (source.Count(i => i.opcode == OpCodes.Ldfld && Equals(i.operand, owned)) != 1)
            throw new InvalidOperationException("Could not identify native furniture pickup ownership safely.");

        var check = typeof(PlayerBuildablePickupPatch).GetMethod(nameof(IsOwnershipBlocked),
            BindingFlags.Static | BindingFlags.NonPublic);
        return source.Select(instruction =>
        {
            var result = new CodeInstruction(instruction);
            if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, owned))
            {
                result.opcode = OpCodes.Call;
                result.operand = check;
            }
            return result;
        }).ToArray();
    }

    internal static bool ResolveOwnershipBlock(bool owned, bool playerBuildable) => owned && !playerBuildable;

    private static bool IsOwnershipBlocked(Pickupable furniture) => ResolveOwnershipBlock(furniture.owned,
        Plugin.IsWorldTilePlayerBuildable(Plugin.GetWorldTile(furniture.transform.GetVector2IntPosition())));

    // No owned-field writes: switching the tile flag off restores normal
    // permission. Actual pickup and subsequent placement use native game code.
}
