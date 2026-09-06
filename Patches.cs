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
    private static void Prefix(out bool __state)
    {
        __state = StructureSaveState.Unloading;
        StructureSaveState.Unloading = true;
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
    private static void Prefix(GameObject __0) =>
        __0?.GetComponent<StructureInstanceState>()?.ReleasePooledState();
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
