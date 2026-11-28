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
    // The compatibility GUID remains available in 1.12.0; tile visuals need its new API.
    "renegadex.silverpine.moddingtools",
    "1.12.0")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "renegadex.silverpine.customstructures";
    public const string PluginName = "Structure Handler";
    public const string PluginVersion = "1.2.40";
    public const int EditorApiVersion = 2;

    internal static ManualLogSource Log = null!;
    internal static Plugin Instance = null!;
    internal static ConfigEntry<bool> ExportOtherWaterTiles = null!;
    internal static readonly HashSet<Vector2Int> ProtectedWorldTiles = new();
    internal static readonly HashSet<Vector2Int> BuildableWorldTiles = new();
    internal static string StructuresDirectory =>
        Path.Combine(Application.persistentDataPath, "custom structures");
    private static readonly PrefabAssetIndex<GameObject> PrefabFallbackCache = new();
    private static bool prefabFallbacksIndexed;
    private static float nextAssetScan;

    internal static string PrefabNameOf(GameObject instance) =>
        PrefabIdentity.FromInstance(instance.name, HasExactAssetName);
    internal static GameObject? TemplateOf(GameObject instance) => ResolvePrefab(PrefabNameOf(instance));

    // Only use aliases owned and resolved by the native registry/ModdingTools.
    internal static Func<string,string> RegisteredNameResolver()
    {
        var names = new Dictionary<string,string>(StringComparer.Ordinal);
        return id =>
        {
            if (names.TryGetValue(id, out var canonical)) return canonical;
            if (Silverpine.ModdingTools.SerializablePrefabs.TryGetRegistration(id, out var direct))
                return names[id] = direct.PrefabName;
            var template = SerializationManager.loadingSave ? null : SerializationManager.GetPrefabFromName(id);
            canonical = template != null && Silverpine.ModdingTools.SerializablePrefabs.TryGetRegistration(template.name, out var reg) &&
                reg.Template == template ? reg.PrefabName : id;
            return names[id] = canonical;
        };
    }

    private static bool HasExactAssetName(string name)
    {
        if (!PrefabFallbackCache.ContainsName(name)) IndexAssetPrefabs();
        return PrefabFallbackCache.ContainsName(name);
    }

    private static bool IsAssetRoot(GameObject item) =>
        item != null && !item.scene.IsValid() && item.transform.parent == null && !string.IsNullOrWhiteSpace(item.name);

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
        if (Silverpine.ModdingTools.SerializablePrefabs.TryGetRegistration(prefabName, out var registered) && registered.Template != null)
            return registered.Template;
        prefab = PrefabFallbackCache.Get(prefabName);
        if (prefab != null)
            return prefab;
        // Unity's destroyed object compares equal to null but still occupies
        // a dictionary entry. Rebuild instead of returning the dead wrapper.
        if (!ReferenceEquals(prefab, null)) prefabFallbacksIndexed = false;
        if (PrefabFallbackCache.ContainsName(prefabName))
        {
            IndexAssetPrefabs();
            prefab = PrefabFallbackCache.Get(prefabName);
            if (prefab != null) return prefab;
            if (PrefabFallbackCache.ContainsName(prefabName)) return null;
        }

        prefab = Resources.Load<GameObject>(prefabName);
        if (prefab != null && IsAssetRoot(prefab) && string.Equals(prefab.name, prefabName, StringComparison.OrdinalIgnoreCase))
        {
            IndexAssetPrefabs();
            PrefabFallbackCache.Add(prefab.name, prefab);
            return PrefabFallbackCache.Get(prefabName);
        }

        IndexAssetPrefabs();
        prefab = PrefabFallbackCache.Get(prefabName);
        return prefab != null ? prefab : null;
    }

    private static void IndexAssetPrefabs()
    {
        if (prefabFallbacksIndexed && Time.realtimeSinceStartup < nextAssetScan)
            return;
        PrefabFallbackCache.Clear();
        foreach (GameObject candidate in
                 Resources.FindObjectsOfTypeAll<GameObject>())
        {
            // FindObjectsOfTypeAll also returns live scene instances. Only
            // scene-less assets are safe to treat as prefab templates.
            if (IsAssetRoot(candidate)) PrefabFallbackCache.Add(candidate.name, candidate);
        }
        prefabFallbacksIndexed = true;
        nextAssetScan = Time.realtimeSinceStartup + 5f;
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
            .Concat(PrefabFallbackCache.Names.Where(name =>
                name.StartsWith(
                    "prefab_", StringComparison.OrdinalIgnoreCase)))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void Awake()
    {
        StructureItemQuality.InstallTypeResolver();
        Instance = this;
        Log = Logger;
        MarketCrates.Install();
        RailingPrefab.EnsureRegistered();
        ExportOtherWaterTiles = WaterExportSettings.Bind(Config);
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
        KitsuneInscription.Install();
        Harmony.CreateAndPatchAll(typeof(StructureReleasedPoolSavePatch), PluginGuid + ".released-pool-save");
        Harmony.CreateAndPatchAll(typeof(StructurePrepareNativeSavePatch), PluginGuid + ".prepare-native-save");
        Harmony.CreateAndPatchAll(typeof(StructureRestoredGroundPatch), PluginGuid + ".restored-ground");
        Harmony.CreateAndPatchAll(typeof(StructureTileEnteredPatch), PluginGuid + ".tile-entered");
        Harmony.CreateAndPatchAll(typeof(StructureTileUnloadPatch), PluginGuid + ".tile-unload");
        Harmony.CreateAndPatchAll(typeof(StructureTileRestoredPatch), PluginGuid + ".tile-restored");
        Harmony.CreateAndPatchAll(typeof(StructureMidnightPatch), PluginGuid + ".midnight");
        Harmony.CreateAndPatchAll(typeof(StructureSpriteRefreshPatch), PluginGuid + ".sprite-refresh");
        Harmony.CreateAndPatchAll(typeof(StructurePoolReleasePatch), PluginGuid + ".pool-release");
        Harmony.CreateAndPatchAll(typeof(GrassZoneClaimPatch), PluginGuid + ".grass-zone-claim");
        Harmony.CreateAndPatchAll(typeof(GrassZoneStartPatch), PluginGuid + ".grass-zone-start");
        Harmony.CreateAndPatchAll(typeof(GrassZoneUnregisterPatch), PluginGuid + ".grass-zone-release");
        Harmony.CreateAndPatchAll(typeof(ReleasedTurfCleanupPatch), PluginGuid + ".released-turf-cleanup");
        Harmony.CreateAndPatchAll(typeof(PlayerBuildableSleepPatch), PluginGuid + ".worldtile-sleep");
        Harmony.CreateAndPatchAll(typeof(PlayerBuildablePickupPatch), PluginGuid + ".worldtile-pickup");
    }

    private void OnDestroy()
    {
        // Silverpine destroys BepInEx's bootstrap host on entering gameplay.
        // The static save registration and scene hooks belong to the process,
        // not this short-lived component. Do not unregister them here.
        PrefabFallbackCache.Clear();
        prefabFallbacksIndexed = false;
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

    internal static Vector2Int GetWorldTile(Vector2 position)
    {
        var tile = StructurePlans.TileOf(position.x, position.y);
        return new Vector2Int(tile.X, tile.Y);
    }

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
