#nullable enable

using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace StructureHandler;

[HarmonyPatch(typeof(WorldItem), "UpdateSprite")]
internal static class WorldItemUpdateSpriteGuard
{
    private static bool Prefix(WorldItem __instance)
    {
        // Overlapping structure files intentionally replace earlier imports.
        // WorldItem.Deserialize queues UpdateSprite one frame later, so an
        // overwritten item may already be destroyed when that callback runs.
        return __instance != null && __instance.gameObject != null;
    }
}

[HarmonyPatch(typeof(SerializationManager), nameof(SerializationManager.Load))]
internal static class StructureSaveLoadPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Prefix(string __0) => StructureSaveState.LegacySavePath = __0;
}

[HarmonyPatch(typeof(SerializationManager), "GetGameObjectsForSerialization")]
internal static class StructureReleasedPoolSavePatch
{
    private static readonly FieldInfo FreePools = AccessTools.Field(typeof(ObjectPool), "freePools");
    private static readonly HashSet<GameObject> DiscardedByImports = new();

    internal static void MarkReleased(GameObject item)
    {
        if (item != null && !item.activeSelf) DiscardedByImports.Add(item);
    }
    internal static void MarkClaimed(GameObject item) => DiscardedByImports.Remove(item);

    [HarmonyPriority(Priority.Last)]
    private static void Postfix(HashSet<GameObject> __result)
    {
        // Native global collection includes inactive objects. Release removes
        // scenery from Turfs but leaves it in these stacks, so without this
        // filter a save resurrects the discarded grass/trees on next load.
        // Also catch copies already sitting unused in the pool before import,
        // including scenery released during a previous load in this session.
        // Never remove active restored ground or items outside the free pools.
        DiscardedByImports.RemoveWhere(item => item == null);
        var pools = (Dictionary<GameObject, Stack<GameObject>>)FreePools.GetValue(null);
        NativeSavePolicy.RemoveReleased(__result, pools.Values, DiscardedByImports,
            item => item != null && item.activeInHierarchy,
            item =>
            {
                if (item == null) return false;
                var cell = item.transform.GetVector2IntPosition();
                return StructureSaveState.ImportedCells.Contains(cell.x, cell.y);
            });
    }
}

[HarmonyPatch(typeof(ObjectPool), nameof(ObjectPool.Claim))]
internal static class StructurePoolClaimPatch
{
    private static void Postfix(GameObject __result) => StructureReleasedPoolSavePatch.MarkClaimed(__result);
}

[HarmonyPatch(typeof(SerializationManager), nameof(SerializationManager.Save))]
internal static class StructurePrepareNativeSavePatch
{
    private static void Prefix() => StructureSaveState.PrepareNativeSave();
}

[HarmonyPatch(typeof(GrassTileHandler), nameof(GrassTileHandler.OnDeconstructed))]
internal static class StructureRestoredGroundPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        int replacements = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo method &&
                method.DeclaringType == typeof(UnityEngine.Object) && method.Name == "Instantiate" &&
                method.IsGenericMethod && method.GetGenericArguments().SequenceEqual(new[] { typeof(GameObject) }) &&
                method.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(GameObject), typeof(Vector3), typeof(Quaternion) }))
            {
                instruction.operand = AccessTools.Method(typeof(DestinationGround), nameof(DestinationGround.InstantiateRestoredGround));
                replacements++;
            }
            yield return instruction;
        }
        if (replacements != 1) throw new InvalidOperationException("Could not identify native floor ground restoration safely.");
    }
}

[HarmonyPatch(typeof(DungeonGenerationManager), nameof(DungeonGenerationManager.EnterWorldTile))]
internal static class StructureTileEnteredPatch
{
    private static void Postfix() => StructureSaveState.ScheduleLoadedTile();
}

[HarmonyPatch(typeof(WorldTile), nameof(WorldTile.TryLoadFromMemory))]
internal static class StructureTileRestoredPatch
{
    private static void Postfix() => StructureSaveState.ScheduleLoadedTile();
}

[HarmonyPatch(typeof(WorldTile), nameof(WorldTile.Unload))]
internal static class StructureTileUnloadPatch
{
    private static void Prefix(WorldTile __instance, out bool __state)
    {
        __state = StructureSaveState.Unloading;
        StructureSaveState.Unloading = true;
        StructureTerrainPersistence.BeforeUnload(__instance.position);
    }
    private static Exception? Finalizer(bool __state, Exception? __exception)
    {
        StructureSaveState.Unloading = __state;
        return __exception;
    }
}

[HarmonyPatch(typeof(DungeonGenerationManager), "OnNewDay")]
internal static class StructureMidnightPatch
{
    private static void Postfix() => StructureSaveState.ScheduleLoadedTile();
}

[HarmonyPatch]
internal static class StructureSpriteRefreshPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(RandomSprite), nameof(RandomSprite.Initialize));
        yield return AccessTools.Method(typeof(SeasonRandomSprite), "SetSprite");
    }
    private static void Postfix(Component __instance) =>
        __instance.GetComponentInParent<StructureInstanceState>()?.Apply();
}

[HarmonyPatch(typeof(ObjectPool), nameof(ObjectPool.Release))]
internal static class StructurePoolReleasePatch
{
    private static void Prefix(GameObject __0)
    {
        __0?.GetComponent<StructureSupplementalInstance>()?.ReleasePooledState();
        __0?.GetComponent<StructureInstanceState>()?.ReleasePooledState();
    }
}

[HarmonyPatch(typeof(WorldTile), nameof(WorldTile.ClearSerializedBlob))]
internal static class WorldTileClearSerializedBlobPatch
{
    private static bool Prefix(WorldTile __instance)
    {
        bool protectedTile =
            Plugin.ProtectedWorldTiles.Contains(__instance.position);
        if (protectedTile)
            return false;

        return true;
    }

    private static void Postfix(WorldTile __instance, bool __runOriginal)
    {
        if (__runOriginal) StructureSaveState.TileCleared(__instance.position);
    }
}

[HarmonyPatch(
    typeof(WorldInfoManager),
    nameof(WorldInfoManager.IsOnPlayerOwnedPlot))]
internal static class PlayerBuildableWorldTilePatch
{
#pragma warning disable Harmony003
    private static void Postfix(Vector2Int __0, ref bool __result)
    {
        if (__result)
            return;

        Vector2Int worldTile = Plugin.GetWorldTile(__0);
        if (Plugin.BuildableWorldTiles.Contains(worldTile))
            __result = true;
    }
#pragma warning restore Harmony003
}
