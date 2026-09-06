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

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(
    Silverpine.ModdingTools.Plugin.PluginGuid,
    "1.10.0")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "renegadex.silverpine.customstructures";
    public const string PluginName = "Structure Handler";
    public const string PluginVersion = "1.2.2";
    public const int EditorApiVersion = 2;

    internal static ManualLogSource Log = null!;
    internal static Plugin Instance = null!;
    internal static ConfigEntry<bool> ImportOtherWaterTiles = null!;
    private static ConfigEntry<string> ProtectedWorldTilesConfig = null!;
    private static ConfigEntry<string> BuildableWorldTilesConfig = null!;
    internal static readonly HashSet<Vector2Int> ProtectedWorldTiles = new();
    internal static readonly HashSet<Vector2Int> BuildableWorldTiles = new();
    internal static string StructuresDirectory =>
        Path.Combine(Application.persistentDataPath, "custom structures");
    private static readonly Dictionary<string, GameObject> PrefabFallbackCache =
        new(StringComparer.OrdinalIgnoreCase);
    private static bool prefabFallbacksIndexed;

    internal static GameObject? ResolvePrefab(string prefabName)
    {
        if (string.IsNullOrWhiteSpace(prefabName))
            return null;

        if (prefabName.Equals(RailingPrefab.Name, StringComparison.OrdinalIgnoreCase))
            RailingPrefab.EnsureRegistered();

        GameObject? prefab =
            SerializationManager.GetPrefabFromName(prefabName);
        if (prefab != null)
            return prefab;
        if (PrefabFallbackCache.TryGetValue(prefabName, out prefab))
            return prefab;

        prefab = Resources.Load<GameObject>(prefabName);
        if (prefab != null)
        {
            PrefabFallbackCache[prefabName] = prefab;
            return prefab;
        }

        IndexAssetPrefabs();
        return PrefabFallbackCache.TryGetValue(prefabName, out prefab)
            ? prefab
            : null;
    }

    private static void IndexAssetPrefabs()
    {
        if (prefabFallbacksIndexed)
            return;
        prefabFallbacksIndexed = true;
        foreach (GameObject candidate in
                 Resources.FindObjectsOfTypeAll<GameObject>())
        {
            // FindObjectsOfTypeAll also returns live scene instances. Only
            // scene-less assets are safe to treat as prefab templates.
            if (candidate == null ||
                candidate.scene.IsValid() ||
                string.IsNullOrWhiteSpace(candidate.name) ||
                PrefabFallbackCache.ContainsKey(candidate.name))
                continue;
            PrefabFallbackCache[candidate.name] = candidate;
        }
    }

    private static void ClearPrefabFallbacks(Scene _, LoadSceneMode __)
    {
        StructureEditorUI.ClearSharedMetadata();
        PrefabFallbackCache.Clear();
        prefabFallbacksIndexed = false;
        RailingPrefab.EnsureRegistered();
    }

    internal static string[] GetKnownPrefabNames()
    {
        RailingPrefab.EnsureRegistered();
        StructureEditorUI.ClearSharedMetadata();
        // Re-scan on each editor open so prefabs registered or loaded by other
        // mods after the previous visit are not omitted.
        prefabFallbacksIndexed = false;
        IndexAssetPrefabs();
        IEnumerable<string> registered;
        try
        {
            registered = SerializationManager.GetExistingPrefabNames();
        }
        catch
        {
            registered = Enumerable.Empty<string>();
        }

        return registered
            .Concat(PrefabFallbackCache.Keys.Where(name =>
                name.StartsWith(
                    "prefab_", StringComparison.OrdinalIgnoreCase)))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void Awake()
    {
        Instance = this;
        Log = Logger;
        RailingPrefab.EnsureRegistered();
        ImportOtherWaterTiles = Config.Bind(
            "Import",
            "ImportOtherWaterTiles",
            false,
            "Import ordinary water terrain. Bathhouse water is always imported.");
        ProtectedWorldTilesConfig = Config.Bind(
            "Import",
            "ProtectedWorldTiles",
            "",
            "Legacy migration only. New protection choices are stored per game save.");
        BuildableWorldTilesConfig = Config.Bind(
            "Construction",
            "BuildableWorldTiles",
            "",
            "Legacy migration only. New buildable-tile choices are stored per game save.");
        StructureSaveState.Initialize();
        Directory.CreateDirectory(StructuresDirectory);
        SceneManager.sceneLoaded += ClearPrefabFallbacks;
        Silverpine.ModdingTools.ModdingToolsMenu.RegisterSession(
            PluginGuid + ".editor",
            "Structure Editor",
            (_, session) => StructureEditorUI.Open(session),
            order: 100);
        Silverpine.ModdingTools.InventoryModTools.RegisterSession(
            PluginGuid + ".game-controls",
            "Structure Handler",
            (inventory, session) =>
                StructureControlsUI.Open(inventory, session),
            order: 100);
        Harmony.CreateAndPatchAll(
            typeof(WorldTileClearSerializedBlobPatch),
            PluginGuid + ".worldtile-protection");
        Harmony.CreateAndPatchAll(
            typeof(PlayerBuildableWorldTilePatch),
            PluginGuid + ".worldtile-buildable");
        Harmony.CreateAndPatchAll(
            typeof(WorldItemUpdateSpriteGuard),
            PluginGuid + ".world-item-sprite-guard");
        Harmony.CreateAndPatchAll(
            typeof(BaseShedCapturePatch),
            PluginGuid + ".base-shed-capture");
        Harmony.CreateAndPatchAll(typeof(StructureSaveLoadPatch), PluginGuid + ".save-load");
        Harmony.CreateAndPatchAll(typeof(StructureTileEnteredPatch), PluginGuid + ".tile-entered");
        Harmony.CreateAndPatchAll(typeof(StructureTileUnloadPatch), PluginGuid + ".tile-unload");
        Harmony.CreateAndPatchAll(typeof(StructureTileRestoredPatch), PluginGuid + ".tile-restored");
        Harmony.CreateAndPatchAll(typeof(StructureMidnightPatch), PluginGuid + ".midnight");
        Harmony.CreateAndPatchAll(typeof(StructureSpriteRefreshPatch), PluginGuid + ".sprite-refresh");
        Harmony.CreateAndPatchAll(typeof(StructurePoolReleasePatch), PluginGuid + ".pool-release");
    }

    private void OnDestroy()
    {
        StructureSaveState.Shutdown();
        SceneManager.sceneLoaded -= ClearPrefabFallbacks;
        PrefabFallbackCache.Clear();
        prefabFallbacksIndexed = false;
    }

    internal static void LoadProtectedWorldTiles()
    {
        ProtectedWorldTiles.Clear();
        foreach (string entry in ProtectedWorldTilesConfig.Value.Split(
                     new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = entry.Split(',');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out int x) &&
                int.TryParse(parts[1], out int y))
            {
                ProtectedWorldTiles.Add(new Vector2Int(x, y));
            }
        }
    }

    internal static void LoadBuildableWorldTiles()
    {
        BuildableWorldTiles.Clear();
        foreach (string entry in BuildableWorldTilesConfig.Value.Split(
                     new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = entry.Split(',');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out int x) &&
                int.TryParse(parts[1], out int y))
            {
                BuildableWorldTiles.Add(new Vector2Int(x, y));
            }
        }
    }

    internal static void ProtectImportedPositions(
        IEnumerable<Vector2Int> positions)
    {
        foreach (Vector2Int position in positions)
        {
            ProtectedWorldTiles.Add(GetWorldTile(position));
        }

    }

    internal static bool TryGetPlayerWorldTile(out Vector2Int worldTile)
    {
        worldTile = default;
        if (Player.Instance == null)
            return false;

        worldTile = GetWorldTile(Player.Instance.transform.position);
        return true;
    }

    internal static bool IsWorldTileRegenerationEnabled(
        Vector2Int worldTile) =>
        !ProtectedWorldTiles.Contains(worldTile);

    internal static void SetWorldTileRegeneration(
        Vector2Int worldTile,
        bool enabled)
    {
        if (enabled)
            ProtectedWorldTiles.Remove(worldTile);
        else
            ProtectedWorldTiles.Add(worldTile);

    }

    internal static bool IsWorldTilePlayerBuildable(
        Vector2Int worldTile) =>
        BuildableWorldTiles.Contains(worldTile);

    internal static void SetWorldTilePlayerBuildable(
        Vector2Int worldTile,
        bool enabled)
    {
        if (enabled)
            BuildableWorldTiles.Add(worldTile);
        else
            BuildableWorldTiles.Remove(worldTile);

    }

    internal static Vector2Int GetWorldTile(Vector2 position) =>
        new(
            Mathf.FloorToInt((position.x + 50f) / 100f),
            Mathf.FloorToInt((position.y + 50f) / 100f));

    internal void SelectWorldTile(
        string cornerName,
        Action<Vector2Int> selected)
    {
        UpperNotificationUI.Instance.OneOff(
            $"Click a world tile for the export {cornerName}. Press Escape to cancel.");
        Player.Instance.StartCoroutine(
            SelectWorldTileCoroutine(cornerName, selected));
    }

    private static IEnumerator SelectWorldTileCoroutine(
        string cornerName,
        Action<Vector2Int> selected)
    {
        // Let the dialog finish consuming its own click.
        yield return null;
        yield return null;
        yield return null;

        while (true)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                UpperNotificationUI.Instance.OneOff(
                    $"Export {cornerName} selection cancelled.");
                yield break;
            }

            if (!Input.GetKeyUp(KeyCode.Mouse0))
            {
                yield return null;
                continue;
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                Transform cameraTransform =
                    Player.Instance.transform.Find("Main Camera");
                if (cameraTransform != null)
                    camera = cameraTransform.GetComponent<Camera>();
            }
            if (camera == null)
                camera = Camera.allCameras.FirstOrDefault(
                    candidate => candidate != null && candidate.isActiveAndEnabled);

            if (camera == null)
            {
                UpperNotificationUI.Instance.OneOff(
                    "Could not select a tile because no world camera is active.");
                yield break;
            }

            Vector3 world = camera.ScreenToWorldPoint(Input.mousePosition);
            Vector2Int tile = Vector2Int.RoundToInt(new Vector2(world.x, world.y));
            if (!Turfs.IsValidTurf(tile))
            {
                UpperNotificationUI.Instance.OneOff(
                    $"No loaded world tile exists at {tile.x}, {tile.y}. Try again.");
                yield return null;
                continue;
            }

            selected(tile);
            yield break;
        }
    }

}
