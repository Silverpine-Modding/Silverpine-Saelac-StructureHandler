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
    BepInDependency.DependencyFlags.HardDependency)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "renegadex.silverpine.customstructures";
    public const string PluginName = "Structure Handler";
    public const string PluginVersion = "1.1.10";

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
        PrefabFallbackCache.Clear();
        prefabFallbacksIndexed = false;
    }

    internal static string[] GetKnownPrefabNames()
    {
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
        ImportOtherWaterTiles = Config.Bind(
            "Import",
            "ImportOtherWaterTiles",
            false,
            "Import ordinary water terrain. Bathhouse water is always imported.");
        ProtectedWorldTilesConfig = Config.Bind(
            "Import",
            "ProtectedWorldTiles",
            "",
            "World-tile coordinates protected because they contain imported structures.");
        BuildableWorldTilesConfig = Config.Bind(
            "Construction",
            "BuildableWorldTiles",
            "",
            "World-tile coordinates treated entirely as player-owned property.");
        LoadProtectedWorldTiles();
        LoadBuildableWorldTiles();
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
        Harmony.CreateAndPatchAll(
            typeof(ResourceMarkerSavePatch),
            PluginGuid + ".resource-marker-save");
        Harmony.CreateAndPatchAll(
            typeof(ResourceMarkerLoadPatch),
            PluginGuid + ".resource-marker-load");
        Harmony.CreateAndPatchAll(
            typeof(ResourceMarkerMainMenuPatch),
            PluginGuid + ".resource-marker-reset");
        Harmony.CreateAndPatchAll(
            typeof(ResourceMarkerMidnightPatch),
            PluginGuid + ".resource-marker-midnight");
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= ClearPrefabFallbacks;
        PrefabFallbackCache.Clear();
        prefabFallbacksIndexed = false;
    }

    private static void LoadProtectedWorldTiles()
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

    private static void LoadBuildableWorldTiles()
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

        SaveProtectedWorldTiles();
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

        SaveProtectedWorldTiles();
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

        BuildableWorldTilesConfig.Value = string.Join(
            ";",
            BuildableWorldTiles
                .OrderBy(position => position.x)
                .ThenBy(position => position.y)
                .Select(position => $"{position.x},{position.y}"));
    }

    internal static Vector2Int GetWorldTile(Vector2 position) =>
        new(
            Mathf.FloorToInt((position.x + 50f) / 100f),
            Mathf.FloorToInt((position.y + 50f) / 100f));

    private static void SaveProtectedWorldTiles()
    {
        ProtectedWorldTilesConfig.Value = string.Join(
            ";",
            ProtectedWorldTiles
                .OrderBy(position => position.x)
                .ThenBy(position => position.y)
                .Select(position => $"{position.x},{position.y}"));
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

[HarmonyPatch(typeof(SettingsUI), "Start")]
internal static class BaseShedCapturePatch
{
    [HarmonyPostfix]
    private static void CaptureUntouchedBaseShed()
    {
        // Settings and plots have initialized by this point, while no save
        // needs to have been selected yet.
        StructureTransfer.CaptureBaseShed();
    }
}

internal sealed class StructureControlsUI :
    Silverpine.ModdingTools.ModToolBehaviour
{
    private static StructureControlsUI? instance;
    private bool open;
    private GameObject canvasRoot = null!;

    internal static void Open(
        InventoryUI inventory,
        Silverpine.ModdingTools.ModToolSession session)
    {
        if (instance != null)
            Destroy(instance.canvasRoot);
        Silverpine.ModdingTools.ModOverlay overlay =
            Silverpine.ModdingTools.ModUi.CreateOverlay(
                inventory,
                "Structure Handler In-Game Controls",
                new Vector2(620f, 700f));
        GameObject panel = overlay.Panel;
        instance = panel.AddComponent<StructureControlsUI>();
        instance.canvasRoot = overlay.Root;
        instance.AttachSession(session);
        instance.open = true;
        instance.Build(inventory);
        StructureTransfer.CaptureBaseShed();
    }

    private void Update()
    {
        if (open && Input.GetKeyDown(KeyCode.Escape))
            Close();
    }

    private void Build(InventoryUI inventory)
    {
        VerticalLayoutGroup layout = GetComponent<VerticalLayoutGroup>() ??
            gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(42, 42, 32, 32);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlHeight = false;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;

        Button template =
            Silverpine.ModdingTools.ModUi.GetInventoryButtonTemplate(inventory);
        AddTitle(template, "Structure Handler");
        AddButton(template, "Structures Export", () => CloseAndRun(StructureTransfer.PromptExport));
        AddButton(template, "Structures Import", () => CloseAndRun(StructureTransfer.ConfirmImportAll));
        AddButton(template, StructureTransfer.TopLeftButtonLabel,
            () => CloseAndRun(() => StructureTransfer.ToggleTopLeft(_ => { })));
        AddButton(template, StructureTransfer.BottomRightButtonLabel,
            () => CloseAndRun(() => StructureTransfer.ToggleBottomRight(_ => { })));
        AddButton(template, WaterLabel(), () =>
        {
            Plugin.ImportOtherWaterTiles.Value = !Plugin.ImportOtherWaterTiles.Value;
            Rebuild(inventory);
        });
        AddButton(template, RegenerationLabel(), () =>
        {
            if (!Plugin.TryGetPlayerWorldTile(out Vector2Int worldTile))
            {
                UpperNotificationUI.Instance.OneOff(
                    "Could not identify the player's current world tile.");
                return;
            }

            bool enabled =
                !Plugin.IsWorldTileRegenerationEnabled(worldTile);
            Plugin.SetWorldTileRegeneration(worldTile, enabled);
            UpperNotificationUI.Instance.OneOff(
                $"World tile {worldTile.x}, {worldTile.y} regeneration is now " +
                $"{(enabled ? "On" : "Off")}.");
            Rebuild(inventory);
        });
        AddButton(template, PlayerBuildableLabel(), () =>
        {
            if (!Plugin.TryGetPlayerWorldTile(out Vector2Int worldTile))
            {
                UpperNotificationUI.Instance.OneOff(
                    "Could not identify the player's current world tile.");
                return;
            }

            bool enabled =
                !Plugin.IsWorldTilePlayerBuildable(worldTile);
            Plugin.SetWorldTilePlayerBuildable(worldTile, enabled);
            UpperNotificationUI.Instance.OneOff(
                $"World tile {worldTile.x}, {worldTile.y} is now " +
                $"{(enabled ? "player buildable" : "no longer marked player buildable")}.");
            Rebuild(inventory);
        });
        AddButton(template, "Close", Close);
    }

    private void Rebuild(InventoryUI inventory)
    {
        foreach (Transform child in transform.Cast<Transform>().ToArray())
            Destroy(child.gameObject);
        Build(inventory);
    }

    private void AddTitle(Button template, string text)
    {
        Silverpine.ModdingTools.ModUi.CloneTitle(
            template, transform, text);
    }

    private Button AddButton(Button template, string label, Action action)
    {
        return Silverpine.ModdingTools.ModUi.CloneButton(
            template, transform, label, action);
    }

    private static string WaterLabel() =>
        $"Import Other Water Tiles: {(Plugin.ImportOtherWaterTiles.Value ? "On" : "Off")}";
    private static string RegenerationLabel()
    {
        if (!Plugin.TryGetPlayerWorldTile(out Vector2Int worldTile))
            return "Current World Tile Regeneration: Unavailable";

        bool enabled =
            Plugin.IsWorldTileRegenerationEnabled(worldTile);
        return $"World Tile {worldTile.x}, {worldTile.y} Regeneration: " +
               $"{(enabled ? "On" : "Off")}";
    }
    private static string PlayerBuildableLabel()
    {
        if (!Plugin.TryGetPlayerWorldTile(out Vector2Int worldTile))
            return "Current World Tile Player Buildable: Unavailable";

        bool enabled =
            Plugin.IsWorldTilePlayerBuildable(worldTile);
        return $"World Tile {worldTile.x}, {worldTile.y} Player Buildable: " +
               $"{(enabled ? "On" : "Off")}";
    }
    private void CloseAndRun(Action action)
    {
        Close();
        action();
    }

    private void Close()
    {
        if (!open)
            return;
        open = false;
        ReleaseSession();
        if (canvasRoot != null)
            Destroy(canvasRoot);
        else
            Destroy(gameObject);
    }

    protected override void OnDisable()
    {
        if (open)
            Close();
        base.OnDisable();
    }

    protected override void OnDestroy()
    {
        open = false;
        base.OnDestroy();
        if (instance == this)
            instance = null;
    }
}

[Serializable]
internal sealed class StructureFile
{
    public int formatVersion = 3;
    public string name = "";
    public string gameVersion = "";
    public string exportedUtc = "";
    public List<StructurePosition> occupiedPositions = new();
    public List<SupportingTerrain> supportingTerrain = new();
    public List<StructureObject> objects = new();
    public string serializedObjectsBase64 = "";
}

[Serializable]
internal sealed class StructurePosition
{
    public int x;
    public int y;
}

[Serializable]
internal sealed class SupportingTerrain
{
    public string prefabName = "";
    public int x;
    public int y;
    public float z;
    public int spriteVariantIndex = -1;
    public bool hasMapZoneName;
    public string mapZoneName = "";
    public List<StructureComponent> components = new();
}

[Serializable]
internal sealed class StructureObject
{
    public string prefabName = "";
    public float x;
    public float y;
    public float z;
    public bool npcInteractionRangeExtender;
    public int turnableIndex = -1;
    public int spriteVariantIndex = -1;
    public bool hasEditableSignMessage;
    public string signMessage = "";
    public bool hasMapZoneName;
    public string mapZoneName = "";
    public List<StructurePosition> occupiedOffsets = new();
    public List<StructureComponent> components = new();
}

[Serializable]
internal sealed class StructureComponent
{
    public string type = "";
    public string hierarchyPath = "";
    public int componentIndex = -1;
    public string dataBase64 = "";
}

[Serializable]
internal sealed class RegeneratingResourceFile
{
    public int formatVersion = 1;
    public List<RegeneratingResourceMarker> resources = new();
}

[Serializable]
internal sealed class RegeneratingResourceMarker
{
    public string prefabName = "";
    public float x;
    public float y;
    public float z;
}

internal static class ResourceRegeneration
{
    private static readonly List<RegeneratingResourceMarker> Markers = new();
    private static string currentSidecarPath = "";
    private static int lastProcessedMidnightDay = int.MinValue;

    internal static void ResetForMainMenu()
    {
        Markers.Clear();
        currentSidecarPath = "";
        lastProcessedMidnightDay = int.MinValue;
    }

    internal static void LoadForSave(string savePath)
    {
        Markers.Clear();
        currentSidecarPath = GetSidecarPath(savePath);
        lastProcessedMidnightDay = int.MinValue;
        if (!File.Exists(currentSidecarPath))
            return;

        try
        {
            object? deserialized = StringSerializationAPI.Deserialize(
                typeof(RegeneratingResourceFile),
                File.ReadAllText(currentSidecarPath));
            if (deserialized is not RegeneratingResourceFile file ||
                file.formatVersion != 1)
                throw new InvalidDataException(
                    "Unsupported resource-marker format.");

            foreach (RegeneratingResourceMarker marker in
                     file.resources ?? new List<RegeneratingResourceMarker>())
            {
                if (IsValidMarker(marker))
                    Upsert(marker);
            }
        }
        catch (Exception exception)
        {
            Markers.Clear();
            Plugin.Log.LogError(
                "Could not load regenerating resource markers: " + exception);
        }
    }

    internal static void SaveForSave(string savePath)
    {
        currentSidecarPath = GetSidecarPath(savePath);
        SaveCurrent();
    }

    internal static void ReplaceImportedResources(
        HashSet<Vector2Int> occupied,
        IEnumerable<GameObject> importedObjects)
    {
        Markers.RemoveAll(marker => occupied.Contains(
            new Vector2Int(
                Mathf.RoundToInt(marker.x),
                Mathf.RoundToInt(marker.y))));

        foreach (GameObject gameObject in importedObjects)
        {
            if (!TryCreateMarker(gameObject, out RegeneratingResourceMarker marker))
                continue;
            Upsert(marker);
        }
        SaveCurrent();
    }

    internal static void RemoveMarkersForWorldTile(Vector2Int worldTile)
    {
        int removed = Markers.RemoveAll(marker =>
            Plugin.GetWorldTile(new Vector2(marker.x, marker.y)) ==
            worldTile);
        if (removed > 0)
            SaveCurrent();
    }

    internal static void RegenerateAtMidnight(int day)
    {
        if (day == lastProcessedMidnightDay)
            return;
        lastProcessedMidnightDay = day;

        HashSet<string> liveResources = new(
            StringComparer.OrdinalIgnoreCase);
        HashSet<Vector2Int> liveResourceCells = new();
        HashSet<Vector2Int> constructedBlockerCells = new();
        GameObject[] sceneObjects = UtilityFunctions
            .GetGameObjectsWithComponent<ISerializableMonoBehavior>()
            .Where(gameObject => gameObject != null)
            .ToArray();
        foreach (GameObject gameObject in sceneObjects)
        {
            if (TryCreateMarker(
                    gameObject, out RegeneratingResourceMarker marker))
            {
                liveResources.Add(GetMarkerKey(marker));
                liveResourceCells.Add(new Vector2Int(
                    Mathf.RoundToInt(marker.x),
                    Mathf.RoundToInt(marker.y)));
            }
            else if (IsConstructedBlocker(gameObject))
            {
                constructedBlockerCells.Add(
                    gameObject.transform.GetVector2IntPosition());
            }
        }

        int spawned = 0;
        foreach (RegeneratingResourceMarker marker in Markers.ToArray())
        {
            string key = GetMarkerKey(marker);
            Vector2Int cell = new(
                Mathf.RoundToInt(marker.x),
                Mathf.RoundToInt(marker.y));
            if (liveResources.Contains(key) ||
                liveResourceCells.Contains(cell) ||
                constructedBlockerCells.Contains(cell))
                continue;

            GameObject? prefab = Plugin.ResolvePrefab(marker.prefabName);
            if (prefab == null)
            {
                Plugin.Log.LogWarning(
                    "Could not regenerate missing resource prefab: " +
                    marker.prefabName);
                continue;
            }

            Vector3 position = new(marker.x, marker.y, marker.z);
            GameObject gameObject = ObjectPool.IsObjectPoolTarget(prefab)
                ? ObjectPool.Claim(prefab, position)
                : UnityEngine.Object.Instantiate(
                    prefab, position, prefab.transform.rotation);
            foreach (TurfRegistrar registrar in
                     gameObject.GetComponentsInChildren<TurfRegistrar>(true))
                registrar.Register();
            liveResources.Add(key);
            liveResourceCells.Add(cell);
            spawned++;
        }

        if (spawned > 0)
            ObjectPool.CallStarts();
    }

    private static bool IsConstructedBlocker(GameObject gameObject)
    {
        return !SerializationManager.GetPrefabName(gameObject).StartsWith(
                "prefab_tile_", StringComparison.OrdinalIgnoreCase) &&
               (gameObject.GetComponentInChildren<Deconstructable>(true) != null ||
             gameObject.GetComponentInChildren<ConstructionSite>(true) != null ||
             gameObject.GetComponentInChildren<Door>(true) != null ||
             gameObject.GetComponentInChildren<Window>(true) != null ||
             gameObject.GetComponentInChildren<Pickupable>(true) != null ||
             gameObject.GetComponentInChildren<Sign>(true) != null);
    }

    private static bool TryCreateMarker(
        GameObject gameObject,
        out RegeneratingResourceMarker marker)
    {
        marker = new RegeneratingResourceMarker();
        if (gameObject == null || !IsRegeneratingResource(gameObject))
            return false;
        string prefabName = SerializationManager.GetPrefabName(gameObject);
        if (string.IsNullOrWhiteSpace(prefabName))
            return false;
        Vector3 position = gameObject.transform.position;
        marker.prefabName = prefabName;
        marker.x = position.x;
        marker.y = position.y;
        marker.z = position.z;
        return true;
    }

    private static bool IsRegeneratingResource(GameObject gameObject)
    {
        ResourceNode? node =
            gameObject.GetComponentInChildren<ResourceNode>(true);
        return node != null &&
               (node.resourceNodeType == ResourceNodeType.Herb ||
                node.resourceNodeType == ResourceNodeType.Ore);
    }

    private static bool IsValidMarker(RegeneratingResourceMarker marker)
    {
        return marker != null &&
               !string.IsNullOrWhiteSpace(marker.prefabName) &&
               IsFinite(marker.x) &&
               IsFinite(marker.y) &&
               IsFinite(marker.z);
    }

    private static bool IsFinite(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value);

    private static void Upsert(RegeneratingResourceMarker marker)
    {
        string key = GetMarkerKey(marker);
        Markers.RemoveAll(existing =>
            GetMarkerKey(existing).Equals(
                key, StringComparison.OrdinalIgnoreCase));
        Markers.Add(new RegeneratingResourceMarker
        {
            prefabName = marker.prefabName,
            x = marker.x,
            y = marker.y,
            z = marker.z
        });
    }

    private static string GetMarkerKey(RegeneratingResourceMarker marker)
    {
        return marker.prefabName + "|" +
               Mathf.RoundToInt(marker.x) + "|" +
               Mathf.RoundToInt(marker.y);
    }

    private static string GetSidecarPath(string savePath) =>
        savePath + ".structurehandler-resources.json";

    private static void SaveCurrent()
    {
        if (string.IsNullOrWhiteSpace(currentSidecarPath))
            return;
        try
        {
            string? directory = Path.GetDirectoryName(currentSidecarPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            RegeneratingResourceFile file = new()
            {
                resources = Markers
                    .OrderBy(marker => marker.prefabName,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(marker => marker.x)
                    .ThenBy(marker => marker.y)
                    .Select(marker => new RegeneratingResourceMarker
                    {
                        prefabName = marker.prefabName,
                        x = marker.x,
                        y = marker.y,
                        z = marker.z
                    })
                    .ToList()
            };
            File.WriteAllText(
                currentSidecarPath,
                StringSerializationAPI.Serialize(
                    typeof(RegeneratingResourceFile), file));
        }
        catch (Exception exception)
        {
            Plugin.Log.LogError(
                "Could not save regenerating resource markers: " + exception);
        }
    }
}

internal static class StructureFileNormalizer
{
    internal static int MoveTileObjectsToTerrain(StructureFile file)
    {
        file.objects ??= new List<StructureObject>();
        file.supportingTerrain ??= new List<SupportingTerrain>();
        file.objects.RemoveAll(item => item == null);
        file.supportingTerrain.RemoveAll(item => item == null);
        List<StructureObject> tileObjects = file.objects
            .Where(item => !string.IsNullOrWhiteSpace(item.prefabName) &&
                           item.prefabName.StartsWith(
                "prefab_tile_", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (StructureObject item in tileObjects)
        {
            int x = Mathf.RoundToInt(item.x);
            int y = Mathf.RoundToInt(item.y);
            if (!file.supportingTerrain.Any(terrain =>
                    terrain.x == x &&
                    terrain.y == y &&
                    terrain.prefabName.Equals(
                        item.prefabName,
                        StringComparison.OrdinalIgnoreCase)))
            {
                file.supportingTerrain.Add(new SupportingTerrain
                {
                    prefabName = item.prefabName,
                    x = x,
                    y = y,
                    z = item.z,
                    spriteVariantIndex = item.spriteVariantIndex,
                    hasMapZoneName = item.hasMapZoneName,
                    mapZoneName = item.mapZoneName,
                    components = (item.components ??
                                  new List<StructureComponent>())
                        .Select(component => new StructureComponent
                        {
                            type = component.type,
                            hierarchyPath = component.hierarchyPath,
                            componentIndex = component.componentIndex,
                            dataBase64 = component.dataBase64
                        })
                        .ToList()
                });
            }
        }
        file.objects.RemoveAll(item => tileObjects.Contains(item));
        if (tileObjects.Count > 0 && file.formatVersion >= 3)
            file.serializedObjectsBase64 = "";
        return tileObjects.Count;
    }
}

internal sealed class StructureEditorUI :
    Silverpine.ModdingTools.ModToolBehaviour
{
    private const float DesignWidth = 1920f;
    private const float DesignHeight = 1080f;
    private static StructureEditorUI? instance;
    private readonly List<string> files = new();
    private readonly HashSet<string> checkedFiles =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<SpritePreview>> spritePreviews =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Sprite[]> spriteVariants =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Rect> spriteLocalBounds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> placementCatalog = new();
    private StructureFile? structure;
    private string currentPath = "";
    private string saveAsName = "";
    private string addPrefabName = "";
    private string brushPrefabName = "";
    private string catalogSearch = "";
    private string addX = "0";
    private string addY = "0";
    private string addZ = "0";
    private string offsetX = "0";
    private string offsetY = "0";
    private string nudgeStep = "0.25";
    private string terrainBrushZone = "";
    private bool addNpcInteractionRangeExtender;
    private bool deleteBrush;
    private bool showFootprintOverlay = true;
    private string status = "";
    private Vector2 fileScroll;
    private Vector2 objectScroll;
    private Vector2 terrainScroll;
    private Vector2 catalogScroll;
    private Vector2 signTextScroll;
    private Vector2 previewPan;
    private Vector2 previewOrigin;
    private int selectedObject = -1;
    private int selectedTerrain = -1;
    private int editorTab;
    private int quickPlaceTab;
    private int movementReferenceSelection = int.MinValue;
    private Vector2Int? lastBrushCell;
    private Vector2Int? lastSelectionCell;
    private bool open;
    private bool dirty;
    private float zoom = 22f;
    private bool renderOrderDirty = true;
    private StructureFile? renderOrderStructure;
    private int[] floorRenderOrder = Array.Empty<int>();
    private int[] objectRenderOrder = Array.Empty<int>();

    private sealed class SpritePreview
    {
        public Sprite sprite = null!;
        public Vector2 localOrigin;
        public Vector2 localAxisX;
        public Vector2 localAxisY;
        public Sprite[] turnableSprites = Array.Empty<Sprite>();
        public bool flipX;
        public bool flipY;
        public int sortingLayer;
        public int sortingOrder;
        public Color color = Color.white;
    }

    internal static void Open(
        Silverpine.ModdingTools.ModToolSession session)
    {
        if (instance == null)
        {
            GameObject root = new("Structure Handler Editor IMGUI");
            instance = root.AddComponent<StructureEditorUI>();
        }
        else
        {
            instance.gameObject.SetActive(true);
        }

        instance.open = true;
        instance.AttachSession(session);
        instance.BuildPlacementCatalog();
        instance.RefreshFiles();
    }

    private void Close()
    {
        open = false;
        // These are references to the game's existing prefab sprites, not
        // instantiated objects. Drop every editor-held reference on close.
        spritePreviews.Clear();
        spriteVariants.Clear();
        spriteLocalBounds.Clear();
        structure = null;
        InvalidateRenderOrder();
        selectedObject = -1;
        selectedTerrain = -1;
        brushPrefabName = "";
        lastBrushCell = null;
        gameObject.SetActive(false);
        ReleaseSession();
    }

    private void OnGUI()
    {
        if (!open)
            return;

        GUI.enabled = true;
        GUI.color = Color.white;
        GUI.backgroundColor = Color.white;
        GUI.depth = -1000;
        using Silverpine.ModdingTools.ModGuiScope guiScope =
            Silverpine.ModdingTools.ModGui.BeginScaled(
                DesignWidth, DesignHeight);

        Color oldBackdropColor = GUI.color;
        GUI.color = new Color(0.025f, 0.035f, 0.045f, 0.97f);
        GUI.DrawTexture(
            new Rect(0, 0, DesignWidth, DesignHeight),
            Texture2D.whiteTexture);
        GUI.color = oldBackdropColor;
        GUI.Box(new Rect(0, 0, DesignWidth, DesignHeight), "");
        GUILayout.BeginArea(new Rect(
            12, 10, DesignWidth - 24, DesignHeight - 20));
        GUILayout.BeginHorizontal();
        GUILayout.Label("Structure Editor", GUILayout.Width(180));
        GUILayout.Label(
            structure == null
                ? "Choose a format-3 JSON file."
                : $"{Path.GetFileName(currentPath)}{(dirty ? "  (unsaved)" : "")}");
        if (GUILayout.Button("Close", GUILayout.Width(90)))
            Close();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        DrawFilePanel();
        DrawPreviewPanel();
        DrawDetailsPanel();
        GUILayout.EndHorizontal();

        if (!string.IsNullOrWhiteSpace(status))
            GUILayout.Label(status);
        GUILayout.EndArea();
    }

    private void DrawFilePanel()
    {
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(230));
        GUILayout.Label("Structure files");
        if (GUILayout.Button("New Blank Structure"))
            CreateBlankStructure();
        if (GUILayout.Button("Refresh"))
            RefreshFiles();
        GUI.enabled = checkedFiles.Count > 0;
        if (GUILayout.Button(
                $"Combine Checked ({checkedFiles.Count})"))
            CombineCheckedFiles();
        GUI.enabled = true;
        fileScroll = GUILayout.BeginScrollView(fileScroll);
        foreach (string path in files)
        {
            GUILayout.BeginHorizontal();
            bool wasChecked = checkedFiles.Contains(path);
            bool isChecked = GUILayout.Toggle(
                wasChecked, "", GUILayout.Width(22));
            if (isChecked != wasChecked)
            {
                if (isChecked)
                    checkedFiles.Add(path);
                else
                    checkedFiles.Remove(path);
            }
            if (GUILayout.Button(Path.GetFileNameWithoutExtension(path)))
                Load(path);
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();
        GUILayout.Space(8);
        GUILayout.Label("Save As name");
        saveAsName = GUILayout.TextField(saveAsName);
        GUI.enabled = structure != null &&
                      !string.IsNullOrWhiteSpace(currentPath);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Save"))
            Save(currentPath);
        GUI.enabled = structure != null;
        if (GUILayout.Button("Save As"))
            SaveAs();
        GUILayout.EndHorizontal();
        GUI.enabled = true;
        GUILayout.EndVertical();
    }

    private void DrawPreviewPanel()
    {
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.BeginHorizontal();
        GUILayout.Label("2D preview");
        if (GUILayout.Button("-", GUILayout.Width(28)))
            zoom = Mathf.Max(8f, zoom - 2f);
        GUILayout.Label($"{zoom:0}px", GUILayout.Width(48));
        if (GUILayout.Button("+", GUILayout.Width(28)))
            zoom = Mathf.Min(60f, zoom + 2f);
        if (GUILayout.Button("Center", GUILayout.Width(65)))
        {
            previewPan = Vector2.zero;
            previewOrigin = CalculateStructureCenter();
        }
        bool nextDeleteBrush = GUILayout.Toggle(
            deleteBrush, "Quick Delete", GUI.skin.button,
            GUILayout.Width(95));
        if (nextDeleteBrush != deleteBrush)
        {
            deleteBrush = nextDeleteBrush;
            lastBrushCell = null;
            if (deleteBrush)
            {
                brushPrefabName = "";
                status =
                    "Quick Delete enabled: left-click and drag to erase; " +
                    "right-click to cancel.";
            }
        }
        showFootprintOverlay = GUILayout.Toggle(
            showFootprintOverlay, "Green Tiles", GUI.skin.button,
            GUILayout.Width(90));
        GUILayout.EndHorizontal();

        Rect preview = GUILayoutUtility.GetRect(
            300, 10000, 300, 10000, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        GUI.Box(preview, "");
        if (structure != null)
        {
            Vector2 center = preview.center + previewPan;
            // Keep oversized furniture and multi-part sprites inside the
            // preview instead of allowing IMGUI textures to cover controls.
            GUI.BeginGroup(preview);
            Rect clippedPreview =
                new(0f, 0f, preview.width, preview.height);
            Vector2 clippedCenter =
                center - new Vector2(preview.x, preview.y);
            DrawGrid(clippedPreview, clippedCenter);
            DrawRecords(clippedPreview, clippedCenter);
            DrawWorldTileOutlines(clippedPreview, clippedCenter);
            GUI.EndGroup();
            HandlePreviewInput(preview, center);
        }
        GUILayout.Label(
            string.IsNullOrWhiteSpace(brushPrefabName)
                ? "Drag empty space to pan. Click a square to select it."
                : $"Brush: {CatalogDisplayName(brushPrefabName)} — left-click/drag to place, right-click to cancel.");
        GUILayout.EndVertical();
    }

    private void DrawGrid(Rect rect, Vector2 center)
    {
        Vector2 origin = GetStructureCenter();
        Vector2 worldZero = new(
            center.x - origin.x * zoom,
            center.y + origin.y * zoom);
        float firstX = Mathf.Repeat(
            worldZero.x + zoom * 0.5f - rect.x, zoom);
        float firstY = Mathf.Repeat(
            worldZero.y + zoom * 0.5f - rect.y, zoom);

        Color old = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.12f);
        for (float x = firstX; x < rect.width; x += zoom)
            GUI.DrawTexture(
                new Rect(rect.x + x, rect.y, 1f, rect.height),
                Texture2D.whiteTexture);
        for (float y = firstY; y < rect.height; y += zoom)
            GUI.DrawTexture(
                new Rect(rect.x, rect.y + y, rect.width, 1f),
                Texture2D.whiteTexture);
        GUI.color = old;
    }

    private void DrawWorldTileOutlines(Rect rect, Vector2 center)
    {
        Vector2 origin = GetStructureCenter();
        float worldLeft =
            origin.x + (rect.xMin - center.x) / zoom;
        float worldRight =
            origin.x + (rect.xMax - center.x) / zoom;
        float worldTop =
            origin.y - (rect.yMin - center.y) / zoom;
        float worldBottom =
            origin.y - (rect.yMax - center.y) / zoom;

        int firstTileX = Mathf.FloorToInt((worldLeft + 50f) / 100f);
        int lastTileX = Mathf.CeilToInt((worldRight + 50f) / 100f);
        int firstTileY = Mathf.FloorToInt((worldBottom + 50f) / 100f);
        int lastTileY = Mathf.CeilToInt((worldTop + 50f) / 100f);

        Color old = GUI.color;
        GUI.color = new Color(1f, 0.55f, 0.08f, 0.95f);
        for (int tileX = firstTileX; tileX <= lastTileX; tileX++)
        {
            float boundaryX = tileX * 100f - 50f;
            float screenX = WorldToPreview(
                center, boundaryX, origin.y).x;
            if (screenX >= rect.xMin && screenX <= rect.xMax)
            {
                GUI.DrawTexture(
                    new Rect(screenX - 2f, rect.yMin, 5f, rect.height),
                    Texture2D.whiteTexture);
            }
        }

        for (int tileY = firstTileY; tileY <= lastTileY; tileY++)
        {
            float boundaryY = tileY * 100f - 50f;
            float screenY = WorldToPreview(
                center, origin.x, boundaryY).y;
            if (screenY >= rect.yMin && screenY <= rect.yMax)
            {
                GUI.DrawTexture(
                    new Rect(rect.xMin, screenY - 2f, rect.width, 5f),
                    Texture2D.whiteTexture);
            }
        }
        GUI.color = old;
    }

    private void DrawRecords(Rect preview, Vector2 center)
    {
        for (int i = 0; i < structure!.supportingTerrain.Count; i++)
        {
            SupportingTerrain terrain = structure.supportingTerrain[i];
            if (!IsWaterRecord(terrain.prefabName))
                DrawSupportingTerrain(
                    preview, center, terrain, i == selectedTerrain);
        }

        EnsureRenderOrder();
        foreach (int i in floorRenderOrder)
        {
            StructureObject item = structure.objects[i];
            DrawObject(preview, center, item,
                i == selectedObject
                    ? new Color(1f, 0.75f, 0.1f, 0.95f)
                    : new Color(
                        0.25f, 0.9f, 0.35f,
                        showFootprintOverlay ? 0.8f : 0f));
        }

        // Bathhouse and other supporting water visually sit on top of the
        // underlying floor, but remain beneath walls and placed furnishings.
        for (int i = 0; i < structure.supportingTerrain.Count; i++)
        {
            SupportingTerrain terrain = structure.supportingTerrain[i];
            if (!IsWaterRecord(terrain.prefabName))
                continue;
            DrawSupportingTerrain(
                preview,
                center,
                terrain,
                i == selectedTerrain);
        }

        foreach (int i in objectRenderOrder)
        {
            StructureObject item = structure.objects[i];
            DrawObject(preview, center, item,
                i == selectedObject
                    ? new Color(1f, 0.75f, 0.1f, 0.95f)
                    : new Color(
                        0.25f, 0.9f, 0.35f,
                        showFootprintOverlay ? 0.8f : 0f));
        }

        Vector2 topLeft = GetTopLeftCoordinate();
        Vector2 marker = WorldToPreview(center, topLeft.x, topLeft.y);
        Color old = GUI.color;
        GUI.color = Color.red;
        GUI.DrawTexture(
            new Rect(marker.x - 5f, marker.y - 5f, 10f, 10f),
            Texture2D.whiteTexture);
        GUI.color = old;
    }

    private void EnsureRenderOrder()
    {
        if (!renderOrderDirty &&
            ReferenceEquals(renderOrderStructure, structure))
            return;

        floorRenderOrder = Enumerable.Range(0, structure!.objects.Count)
            .Where(index => IsFloorRecord(structure.objects[index]))
            .OrderByDescending(index => structure.objects[index].y)
            .ToArray();
        objectRenderOrder = Enumerable.Range(0, structure.objects.Count)
            .Where(index => !IsFloorRecord(structure.objects[index]))
            .OrderByDescending(index => structure.objects[index].y)
            .ToArray();
        renderOrderStructure = structure;
        renderOrderDirty = false;
    }

    private void DrawSupportingTerrain(
        Rect preview,
        Vector2 center,
        SupportingTerrain terrain,
        bool selected)
    {
        Vector2 point = WorldToPreview(center, terrain.x, terrain.y);
        if (!IsPrefabVisible(preview, point, terrain.prefabName))
            return;
        DrawPrefabSprite(
            preview,
            point,
            terrain.prefabName,
            new Color(1f, 1f, 1f, 0.9f),
            terrainSpriteVariant: terrain.spriteVariantIndex);
        DrawTile(preview, center, terrain.x, terrain.y,
            selected
                ? new Color(1f, 0.75f, 0.1f, 0.55f)
                : new Color(1f, 1f, 1f, 0f));
    }

    private void DrawObject(
        Rect preview, Vector2 center, StructureObject item, Color markerColor)
    {
        Vector2 point = WorldToPreview(center, item.x, item.y);
        if (!IsPrefabVisible(preview, point, item.prefabName))
            return;
        List<SpritePreview> previews = GetSpritePreviews(item.prefabName);
        DrawPrefabSprite(
            preview, point, item.prefabName, Color.white, item);

        // A full one-world-unit square matches prefab_tile_rock and makes the
        // serialized coordinate footprint unambiguous.
        float markerAlpha = previews.Count == 0
            ? Mathf.Max(markerColor.a, 0.35f)
            : markerColor.a > 0f ? 0.28f : 0f;
        if (markerAlpha <= 0f)
            return;
        Color old = GUI.color;
        GUI.color = new Color(
            markerColor.r, markerColor.g, markerColor.b,
            markerAlpha);
        foreach (Vector2Int occupied in GetObjectCells(item))
        {
            Vector2 occupiedPoint =
                WorldToPreview(center, occupied.x, occupied.y);
            Rect marker = new(
                occupiedPoint.x - zoom * 0.5f,
                occupiedPoint.y - zoom * 0.5f,
                zoom,
                zoom);
            if (preview.Overlaps(marker))
                GUI.DrawTexture(marker, Texture2D.whiteTexture);
        }
        GUI.color = old;
    }

    private bool IsPrefabVisible(
        Rect preview, Vector2 point, string prefabName)
    {
        Rect local = GetPrefabLocalBounds(prefabName);
        Rect screen = new(
            point.x + local.xMin * zoom,
            point.y - local.yMax * zoom,
            local.width * zoom,
            local.height * zoom);
        return preview.Overlaps(screen);
    }

    private void DrawPrefabSprite(
        Rect preview,
        Vector2 point,
        string prefabName,
        Color tint,
        StructureObject? record = null,
        int terrainSpriteVariant = -1)
    {
        int requestedVariant =
            record?.spriteVariantIndex ?? terrainSpriteVariant;
        Sprite[] variants = requestedVariant < 0
            ? Array.Empty<Sprite>()
            : GetSpriteVariants(prefabName);
        bool variantApplied = false;
        foreach (SpritePreview spritePreview in GetSpritePreviews(prefabName))
        {
            Sprite sprite = spritePreview.sprite;
            if ((record == null || record.turnableIndex < 0) &&
                requestedVariant >= 0 &&
                variants.Length > 0 &&
                !variantApplied)
            {
                int index = (requestedVariant % variants.Length +
                             variants.Length) % variants.Length;
                sprite = variants[index];
                variantApplied = true;
            }
            else if (record != null &&
                record.turnableIndex >= 0 &&
                spritePreview.turnableSprites.Length > 0)
            {
                int index = (record.turnableIndex %
                             spritePreview.turnableSprites.Length +
                             spritePreview.turnableSprites.Length) %
                            spritePreview.turnableSprites.Length;
                sprite = spritePreview.turnableSprites[index];
            }

            Bounds bounds = sprite.bounds;
            Vector2 localOffset =
                spritePreview.localOrigin +
                spritePreview.localAxisX * bounds.center.x +
                spritePreview.localAxisY * bounds.center.y;
            Vector2 worldSize = new(
                Mathf.Abs(spritePreview.localAxisX.x * bounds.size.x) +
                Mathf.Abs(spritePreview.localAxisY.x * bounds.size.y),
                Mathf.Abs(spritePreview.localAxisX.y * bounds.size.x) +
                Mathf.Abs(spritePreview.localAxisY.y * bounds.size.y));
            Vector2 spriteCenter = point + new Vector2(
                localOffset.x * zoom,
                -localOffset.y * zoom);
            Rect spriteRect = new(
                spriteCenter.x - worldSize.x * zoom * 0.5f,
                spriteCenter.y - worldSize.y * zoom * 0.5f,
                worldSize.x * zoom,
                worldSize.y * zoom);
            if (!preview.Overlaps(spriteRect))
                continue;

            Rect source = sprite.textureRect;
            Rect uv = new(
                source.x / sprite.texture.width,
                source.y / sprite.texture.height,
                source.width / sprite.texture.width,
                source.height / sprite.texture.height);
            if (spritePreview.flipX)
            {
                uv.x += uv.width;
                uv.width = -uv.width;
            }
            if (spritePreview.flipY)
            {
                uv.y += uv.height;
                uv.height = -uv.height;
            }
            Color old = GUI.color;
            GUI.color = new Color(
                tint.r * spritePreview.color.r,
                tint.g * spritePreview.color.g,
                tint.b * spritePreview.color.b,
                tint.a * spritePreview.color.a);
            GUI.DrawTextureWithTexCoords(
                spriteRect, sprite.texture, uv, alphaBlend: true);
            GUI.color = old;
        }
    }

    private static bool IsFloorRecord(StructureObject item) =>
        item.prefabName.StartsWith(
            "prefab_tile_", StringComparison.OrdinalIgnoreCase);

    private static bool IsWaterRecord(string prefabName) =>
        prefabName.Contains("water", StringComparison.OrdinalIgnoreCase);

    private List<SpritePreview> GetSpritePreviews(string prefabName)
    {
        if (spritePreviews.TryGetValue(
                prefabName, out List<SpritePreview> cached))
            return cached;

        List<SpritePreview> previews = new();
        spritePreviews[prefabName] = previews;
        try
        {
            GameObject? prefab = Plugin.ResolvePrefab(prefabName);
            if (prefab == null)
                return previews;

            Turnable turnable =
                prefab.GetComponentInChildren<Turnable>(true);
            List<SpriteRenderer> renderers =
                prefab.GetComponentsInChildren<SpriteRenderer>(false)
                    .Where(candidate =>
                        candidate.sprite != null &&
                        candidate.enabled &&
                        !IsPreviewLightingEffect(candidate))
                    .ToList();

            // Plain constructed walls can contain extra child renderers used by
            // the game's world lighting/shadow setup. Drawing those children as
            // ordinary GUI sprites makes adjacent previews overlap into a
            // light/dark checkerboard. Doors, windows, furniture, baths, and
            // other multi-part prefabs retain all visual renderers.
            if (IsPlainWallPreview(prefabName) && renderers.Count > 1)
            {
                List<SpriteRenderer> rootRenderers = renderers
                    .Where(renderer => renderer.transform == prefab.transform)
                    .ToList();
                renderers = rootRenderers.Count > 0
                    ? rootRenderers
                    : renderers
                        .Where(renderer =>
                            GetPreviewHierarchyDepth(
                                renderer.transform, prefab.transform) ==
                            renderers.Min(candidate =>
                                GetPreviewHierarchyDepth(
                                    candidate.transform, prefab.transform)))
                        .ToList();
            }

            foreach (SpriteRenderer renderer in renderers)
            {
                Vector3 relativeOrigin = prefab.transform.InverseTransformPoint(
                    renderer.transform.TransformPoint(Vector3.zero));
                Vector3 relativeX = prefab.transform.InverseTransformVector(
                    renderer.transform.TransformVector(
                        Vector3.right));
                Vector3 relativeY = prefab.transform.InverseTransformVector(
                    renderer.transform.TransformVector(
                        Vector3.up));
                bool usesTurnableSprites =
                    turnable != null &&
                    turnable.sprites != null &&
                    turnable.sprites.Contains(renderer.sprite);
                previews.Add(new SpritePreview
                {
                    sprite = renderer.sprite,
                    localOrigin = new Vector2(
                        relativeOrigin.x, relativeOrigin.y),
                    localAxisX = new Vector2(relativeX.x, relativeX.y),
                    localAxisY = new Vector2(relativeY.x, relativeY.y),
                    turnableSprites = usesTurnableSprites
                        ? turnable!.sprites!
                        : Array.Empty<Sprite>(),
                    flipX = renderer.flipX,
                    flipY = renderer.flipY,
                    sortingLayer = renderer.sortingLayerID,
                    sortingOrder = renderer.sortingOrder,
                    color = renderer.color
                });
            }
            previews.Sort((left, right) =>
            {
                int layer = left.sortingLayer.CompareTo(right.sortingLayer);
                return layer != 0
                    ? layer
                    : left.sortingOrder.CompareTo(right.sortingOrder);
            });
            return previews;
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning(
                $"Could not preview sprite for {prefabName}: {exception.Message}");
            return previews;
        }
    }

    private Rect GetPrefabLocalBounds(string prefabName)
    {
        if (spriteLocalBounds.TryGetValue(prefabName, out Rect cached))
            return cached;

        bool initialized = false;
        float minX = -0.5f;
        float minY = -0.5f;
        float maxX = 0.5f;
        float maxY = 0.5f;
        foreach (SpritePreview preview in GetSpritePreviews(prefabName))
        {
            IEnumerable<Sprite> sprites =
                new[] { preview.sprite }
                    .Concat(preview.turnableSprites)
                    .Concat(GetSpriteVariants(prefabName))
                    .Where(sprite => sprite != null)
                    .Distinct();
            foreach (Sprite sprite in sprites)
            {
                Bounds bounds = sprite.bounds;
                Vector2 localOffset =
                    preview.localOrigin +
                    preview.localAxisX * bounds.center.x +
                    preview.localAxisY * bounds.center.y;
                Vector2 size = new(
                    Mathf.Abs(preview.localAxisX.x * bounds.size.x) +
                    Mathf.Abs(preview.localAxisY.x * bounds.size.y),
                    Mathf.Abs(preview.localAxisX.y * bounds.size.x) +
                    Mathf.Abs(preview.localAxisY.y * bounds.size.y));
                float spriteMinX = localOffset.x - size.x * 0.5f;
                float spriteMaxX = localOffset.x + size.x * 0.5f;
                float spriteMinY = localOffset.y - size.y * 0.5f;
                float spriteMaxY = localOffset.y + size.y * 0.5f;
                if (!initialized)
                {
                    minX = spriteMinX;
                    maxX = spriteMaxX;
                    minY = spriteMinY;
                    maxY = spriteMaxY;
                    initialized = true;
                }
                else
                {
                    minX = Mathf.Min(minX, spriteMinX);
                    maxX = Mathf.Max(maxX, spriteMaxX);
                    minY = Mathf.Min(minY, spriteMinY);
                    maxY = Mathf.Max(maxY, spriteMaxY);
                }
            }
        }

        Rect result = Rect.MinMaxRect(minX, minY, maxX, maxY);
        spriteLocalBounds[prefabName] = result;
        return result;
    }

    private void MarkStructureDirty()
    {
        dirty = true;
        InvalidateRenderOrder();
    }

    private void InvalidateRenderOrder()
    {
        renderOrderDirty = true;
        renderOrderStructure = null;
    }

    private static bool IsPreviewLightingEffect(SpriteRenderer renderer)
    {
        string objectName = renderer.gameObject.name;
        string materialName = renderer.sharedMaterial?.name ?? "";
        string shaderName = renderer.sharedMaterial?.shader?.name ?? "";
        return objectName.ContainsAnyIgnoreCase(
                   "light", "glow", "shadow", "ambient", "occlusion") ||
               materialName.ContainsAnyIgnoreCase(
                   "light", "glow", "shadow", "ambient", "occlusion") ||
               shaderName.ContainsAnyIgnoreCase(
                   "light", "glow", "shadow", "ambient", "occlusion");
    }

    internal Sprite[] GetSpriteVariants(string prefabName)
    {
        if (spriteVariants.TryGetValue(prefabName, out Sprite[] cached))
            return cached;

        Sprite[] variants = GetSpriteVariantsForPrefab(prefabName);
        spriteVariants[prefabName] = variants;
        return variants;
    }

    internal static Sprite[] GetSpriteVariantsForPrefab(string prefabName)
    {
        List<Sprite> result = new();
        try
        {
            GameObject? prefab =
                Plugin.ResolvePrefab(prefabName);
            if (prefab != null)
            {
                foreach (MonoBehaviour component in
                         prefab.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (component == null || component is Turnable)
                        continue;
                    foreach (FieldInfo field in component.GetType().GetFields(
                                 BindingFlags.Instance |
                                 BindingFlags.Public |
                                 BindingFlags.NonPublic))
                    {
                        if (field.FieldType == typeof(Sprite[]) &&
                            field.GetValue(component) is Sprite[] sprites)
                        {
                            result.AddRange(sprites.Where(sprite => sprite != null));
                        }
                        else if (typeof(IEnumerable<Sprite>).IsAssignableFrom(
                                     field.FieldType) &&
                                 field.GetValue(component) is
                                     IEnumerable<Sprite> spriteList)
                        {
                            result.AddRange(
                                spriteList.Where(sprite => sprite != null));
                        }
                    }
                }
            }
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning(
                $"Could not inspect sprite variants for {prefabName}: " +
                exception.Message);
        }

        return result.Distinct().ToArray();
    }

    private static bool IsPlainWallPreview(string prefabName) =>
        prefabName.StartsWith(
            "prefab_wall_", StringComparison.OrdinalIgnoreCase) &&
        !prefabName.ContainsAnyIgnoreCase("door", "window");

    private static int GetPreviewHierarchyDepth(
        Transform transform, Transform prefabRoot)
    {
        int depth = 0;
        while (transform != null && transform != prefabRoot)
        {
            depth++;
            transform = transform.parent;
        }

        return depth;
    }

    private void DrawTile(
        Rect preview, Vector2 center, float worldX, float worldY, Color color)
    {
        Vector2 point = WorldToPreview(center, worldX, worldY);
        Rect tile = new(
            point.x - zoom * 0.42f, point.y - zoom * 0.42f,
            zoom * 0.84f, zoom * 0.84f);
        if (!preview.Overlaps(tile))
            return;
        Color old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(tile, Texture2D.whiteTexture);
        GUI.color = old;
    }

    private void HandlePreviewInput(Rect preview, Vector2 center)
    {
        Event current = Event.current;
        if (!preview.Contains(current.mousePosition))
            return;

        if (current.type == EventType.MouseDrag && current.button == 2)
        {
            previewPan += current.delta;
            current.Use();
            return;
        }

        if (deleteBrush)
        {
            if (current.type == EventType.MouseDown && current.button == 1)
            {
                deleteBrush = false;
                lastBrushCell = null;
                status = "Quick Delete cancelled.";
                current.Use();
                return;
            }
            if ((current.type == EventType.MouseDown ||
                 current.type == EventType.MouseDrag) &&
                current.button == 0)
            {
                Vector2Int cell =
                    PreviewToWorldCell(center, current.mousePosition);
                if (!lastBrushCell.HasValue || lastBrushCell.Value != cell)
                {
                    DeleteAtCell(cell);
                    lastBrushCell = cell;
                }
                current.Use();
            }
            else if (current.type == EventType.MouseUp &&
                     current.button == 0)
            {
                lastBrushCell = null;
                current.Use();
            }
            return;
        }

        if (current.type == EventType.MouseDown && current.button == 1 &&
            !string.IsNullOrWhiteSpace(brushPrefabName))
        {
            status = "Quick-place brush cancelled.";
            brushPrefabName = "";
            lastBrushCell = null;
            current.Use();
            return;
        }

        if (!string.IsNullOrWhiteSpace(brushPrefabName))
        {
            if ((current.type == EventType.MouseDown ||
                 current.type == EventType.MouseDrag) &&
                current.button == 0)
            {
                Vector2Int cell = PreviewToWorldCell(center, current.mousePosition);
                if (!lastBrushCell.HasValue || lastBrushCell.Value != cell)
                {
                    PlaceBrushObject(cell);
                    lastBrushCell = cell;
                }
                current.Use();
            }
            else if (current.type == EventType.MouseUp && current.button == 0)
            {
                lastBrushCell = null;
                current.Use();
            }
            return;
        }

        if (current.type == EventType.MouseDrag && current.button == 0)
        {
            previewPan += current.delta;
            current.Use();
        }
        else if (current.type == EventType.MouseDown && current.button == 0)
        {
            Vector2Int clickedCell =
                PreviewToWorldCell(center, current.mousePosition);
            List<Tuple<bool, int>> candidates = Enumerable.Range(
                    0, structure!.objects.Count)
                .Where(index =>
                {
                    StructureObject item = structure.objects[index];
                    return GetObjectCells(item).Contains(clickedCell);
                })
                .Select(index => Tuple.Create(false, index))
                .Concat(Enumerable.Range(
                        0, structure.supportingTerrain.Count)
                    .Where(index =>
                    {
                        SupportingTerrain item =
                            structure.supportingTerrain[index];
                        return Vector2.Distance(
                                   current.mousePosition,
                                   WorldToPreview(center, item.x, item.y)) <
                               zoom * 0.55f;
                    })
                    .Select(index => Tuple.Create(true, index)))
                .OrderBy(candidate => candidate.Item1 ? 1 : 0)
                .ThenBy(candidate => candidate.Item2)
                .ToList();

            if (candidates.Count == 0)
            {
                selectedObject = -1;
                selectedTerrain = -1;
            }
            else
            {
                int currentIndex = candidates.FindIndex(candidate =>
                    candidate.Item1
                        ? selectedTerrain == candidate.Item2
                        : selectedObject == candidate.Item2);
                int nextIndex =
                    lastSelectionCell == clickedCell && currentIndex >= 0
                        ? (currentIndex + 1) % candidates.Count
                        : 0;
                Tuple<bool, int> next = candidates[nextIndex];
                selectedObject = next.Item1 ? -1 : next.Item2;
                selectedTerrain = next.Item1 ? next.Item2 : -1;
                editorTab = next.Item1 ? 1 : 0;
            }

            lastSelectionCell = clickedCell;
            if (candidates.Count > 1)
            {
                status =
                    $"Overlapping records: click again for the next " +
                    $"({candidates.Count} total).";
            }
            current.Use();
        }
    }

    private Vector2Int PreviewToWorldCell(Vector2 center, Vector2 screenPoint)
    {
        Vector2 origin = GetStructureCenter();
        return new Vector2Int(
            Mathf.RoundToInt(origin.x + (screenPoint.x - center.x) / zoom),
            Mathf.RoundToInt(origin.y - (screenPoint.y - center.y) / zoom));
    }

    private void PlaceBrushObject(Vector2Int cell)
    {
        if (structure == null)
            return;
        float z = TryFloat(addZ, out float parsedZ) ? parsedZ : 0f;
        if (IsQuickPlaceSupportingTerrain(brushPrefabName))
        {
            SupportingTerrain existingTerrain =
                structure.supportingTerrain.FirstOrDefault(item =>
                    item.x == cell.x &&
                    item.y == cell.y &&
                    item.prefabName.Equals(
                        brushPrefabName,
                        StringComparison.OrdinalIgnoreCase));
            if (existingTerrain == null)
            {
                structure.supportingTerrain.Add(new SupportingTerrain
                {
                    prefabName = brushPrefabName,
                    x = cell.x,
                    y = cell.y,
                    z = z,
                    hasMapZoneName =
                        !string.IsNullOrWhiteSpace(terrainBrushZone),
                    mapZoneName = terrainBrushZone
                });
                selectedTerrain = structure.supportingTerrain.Count - 1;
            }
            else
            {
                existingTerrain.z = z;
                existingTerrain.hasMapZoneName =
                    !string.IsNullOrWhiteSpace(terrainBrushZone);
                existingTerrain.mapZoneName = terrainBrushZone;
                selectedTerrain =
                    structure.supportingTerrain.IndexOf(existingTerrain);
            }
            selectedObject = -1;
            MarkStructureDirty();
            status =
                $"Painted {CatalogDisplayName(brushPrefabName)} at " +
                $"{cell.x}, {cell.y}.";
            return;
        }

        structure.objects.Add(new StructureObject
        {
            prefabName = brushPrefabName,
            x = cell.x,
            y = cell.y,
            z = z,
            npcInteractionRangeExtender =
                addNpcInteractionRangeExtender,
            turnableIndex = GetDefaultTurnableIndex(brushPrefabName),
            occupiedOffsets = GetPrefabOccupiedOffsets(
                brushPrefabName, GetDefaultTurnableIndex(brushPrefabName))
        });
        selectedObject = structure.objects.Count - 1;
        selectedTerrain = -1;
        addX = cell.x.ToString(CultureInfo.InvariantCulture);
        addY = cell.y.ToString(CultureInfo.InvariantCulture);
        MarkStructureDirty();
        status = $"Placed {CatalogDisplayName(brushPrefabName)} at {cell.x}, {cell.y}.";
    }

    private void DeleteAtCell(Vector2Int cell)
    {
        if (structure == null)
            return;

        int objectsRemoved = structure.objects.RemoveAll(item =>
            GetObjectCells(item).Contains(cell));
        int terrainRemoved = structure.supportingTerrain.RemoveAll(item =>
            item.x == cell.x && item.y == cell.y);
        if (objectsRemoved + terrainRemoved == 0)
        {
            status = $"Nothing to delete at {cell.x}, {cell.y}.";
            return;
        }

        selectedObject = -1;
        selectedTerrain = -1;
        MarkStructureDirty();
        status =
            $"Deleted {objectsRemoved + terrainRemoved} record(s) at " +
            $"{cell.x}, {cell.y}.";
    }

    private Vector2 WorldToPreview(Vector2 center, float x, float y)
    {
        Vector2 origin = GetStructureCenter();
        return center + new Vector2((x - origin.x) * zoom, -(y - origin.y) * zoom);
    }

    private Vector2 GetStructureCenter()
    {
        return previewOrigin;
    }

    private Vector2 CalculateStructureCenter()
    {
        if (structure == null)
            return Vector2.zero;
        IEnumerable<Vector2> points = structure.objects
            .Select(item => new Vector2(item.x, item.y))
            .Concat(structure.supportingTerrain.Select(
                item => new Vector2(item.x, item.y)));
        if (!points.Any())
            return Vector2.zero;
        return new Vector2(
            (points.Min(point => point.x) + points.Max(point => point.x)) / 2f,
            (points.Min(point => point.y) + points.Max(point => point.y)) / 2f);
    }

    private void DrawDetailsPanel()
    {
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(330));
        GUILayout.BeginHorizontal();
        if (GUILayout.Toggle(
                editorTab == 0, "Objects", GUI.skin.button))
            editorTab = 0;
        if (GUILayout.Toggle(
                editorTab == 1, "Terrain", GUI.skin.button))
            editorTab = 1;
        if (GUILayout.Toggle(
                editorTab == 2, "Quick Place", GUI.skin.button))
            editorTab = 2;
        GUILayout.EndHorizontal();

        if (structure == null)
        {
            GUILayout.Label("No structure loaded.");
            GUILayout.EndVertical();
            return;
        }

        if (editorTab == 0)
            DrawObjectsTab();
        else if (editorTab == 1)
            DrawTerrainTab();
        else
            DrawQuickPlaceTab();

        GUILayout.EndVertical();
    }

    private void DrawObjectsTab()
    {
        GUILayout.Label("Objects");
        objectScroll = GUILayout.BeginScrollView(objectScroll, GUILayout.Height(190));
        for (int i = 0; i < structure!.objects.Count; i++)
        {
            StructureObject item = structure.objects[i];
            string label = $"{i + 1}. {item.prefabName}  ({item.x:0.##}, {item.y:0.##})";
            if (GUILayout.Toggle(selectedObject == i, label, GUI.skin.button))
            {
                selectedObject = i;
                selectedTerrain = -1;
            }
        }
        GUILayout.EndScrollView();

        if (selectedObject >= 0 && selectedObject < structure.objects.Count)
            DrawSelectedObject();

        DrawMoveEntireStructure();
    }

    private void DrawTerrainTab()
    {
        GUILayout.Label("Supporting terrain");
        terrainScroll = GUILayout.BeginScrollView(
            terrainScroll, GUILayout.Height(240));
        for (int i = 0; i < structure!.supportingTerrain.Count; i++)
        {
            SupportingTerrain item = structure.supportingTerrain[i];
            string label =
                $"{i + 1}. {item.prefabName}  ({item.x}, {item.y})";
            if (GUILayout.Toggle(
                    selectedTerrain == i, label, GUI.skin.button))
            {
                selectedTerrain = i;
                selectedObject = -1;
            }
        }
        GUILayout.EndScrollView();

        if (selectedTerrain >= 0 &&
            selectedTerrain < structure.supportingTerrain.Count)
            DrawSelectedTerrain();
    }

    private void DrawQuickPlaceTab()
    {
        string[] categories =
            { "Terrain", "Structural", "Furniture", "Nature", "Utility" };
        GUILayout.BeginHorizontal();
        for (int index = 0; index < 3; index++)
            if (GUILayout.Toggle(
                    quickPlaceTab == index,
                    categories[index],
                    GUI.skin.button))
                quickPlaceTab = index;
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        for (int index = 3; index < categories.Length; index++)
            if (GUILayout.Toggle(
                    quickPlaceTab == index,
                    categories[index],
                    GUI.skin.button))
                quickPlaceTab = index;
        GUILayout.EndHorizontal();

        string category = categories[
            Mathf.Clamp(quickPlaceTab, 0, categories.Length - 1)];
        GUILayout.Label($"Quick-place {category.ToLowerInvariant()}");
        if (quickPlaceTab == 0)
        {
            terrainBrushZone =
                LabeledTextField("Zone", terrainBrushZone);
            GUILayout.Label(
                "Terrain painted with this brush keeps the same zone name.");
        }
        catalogSearch = LabeledTextField("Search", catalogSearch);
        catalogScroll = GUILayout.BeginScrollView(
            catalogScroll, GUILayout.Height(330));
        foreach (string prefabName in placementCatalog.Where(name =>
                     GetQuickPlaceCategory(name) == quickPlaceTab &&
                     (string.IsNullOrWhiteSpace(catalogSearch) ||
                      name.Contains(
                          catalogSearch, StringComparison.OrdinalIgnoreCase) ||
                      CatalogDisplayName(name).Contains(
                          catalogSearch, StringComparison.OrdinalIgnoreCase))))
        {
            if (GUILayout.Button(CatalogDisplayName(prefabName)))
            {
                addPrefabName = prefabName;
                brushPrefabName = prefabName;
                addZ = GetQuickPlaceDefaultZ(prefabName)
                    .ToString(CultureInfo.InvariantCulture);
                deleteBrush = false;
                lastBrushCell = null;
                status =
                    $"Quick-place brush selected: {CatalogDisplayName(prefabName)}. " +
                    "Left-click the preview to place; right-click to cancel.";
            }
        }
        GUILayout.EndScrollView();

        GUILayout.Space(8);
        GUILayout.Label("Add generic prefab (default state)");
        addPrefabName = LabeledTextField("Prefab", addPrefabName);
        addX = LabeledTextField("X", addX);
        addY = LabeledTextField("Y", addY);
        addZ = LabeledTextField("Z", addZ);
        addNpcInteractionRangeExtender = GUILayout.Toggle(
            addNpcInteractionRangeExtender,
            " Add NPC interaction range extender to new objects");
        if (GUILayout.Button("Add object"))
            AddObject();
    }

    private void DrawMoveEntireStructure()
    {
        GUILayout.Space(8);
        GUILayout.Label("Move entire structure");
        Vector2 movementReference = GetMovementReferenceCoordinate();
        int movementSelectionKey =
            selectedObject >= 0
                ? selectedObject
                : selectedTerrain >= 0 ? -selectedTerrain - 2 : -1;
        if (movementReferenceSelection != movementSelectionKey)
        {
            offsetX = Format(movementReference.x);
            offsetY = Format(movementReference.y);
            movementReferenceSelection = movementSelectionKey;
        }
        GUILayout.Label(
            selectedObject >= 0 && selectedObject < structure!.objects.Count
                ? $"Selected-object reference: {Format(movementReference.x)}, " +
                  $"{Format(movementReference.y)}"
                : selectedTerrain >= 0 &&
                  selectedTerrain < structure!.supportingTerrain.Count
                    ? $"Selected-terrain reference: " +
                      $"{Format(movementReference.x)}, " +
                      $"{Format(movementReference.y)}"
                : $"Top-left reference: {Format(movementReference.x)}, " +
                  $"{Format(movementReference.y)}  (red dot)");
        offsetX = LabeledTextField("Target X", offsetX);
        offsetY = LabeledTextField("Target Y", offsetY);
        if (GUILayout.Button(
                selectedObject >= 0 || selectedTerrain >= 0
                    ? "Move selected reference to coordinate"
                    : "Move top-left to coordinate"))
            MoveToCoordinate();
    }

    private void DrawSelectedTerrain()
    {
        SupportingTerrain item =
            structure!.supportingTerrain[selectedTerrain];
        GUILayout.Label("Selected supporting terrain");
        string prefab = LabeledTextField("Prefab", item.prefabName);
        string x = LabeledTextField("X", item.x.ToString(
            CultureInfo.InvariantCulture));
        string y = LabeledTextField("Y", item.y.ToString(
            CultureInfo.InvariantCulture));
        string z = LabeledTextField("Z", Format(item.z));
        string zone = LabeledTextField(
            "Zone", item.hasMapZoneName ? item.mapZoneName : "");
        if (prefab != item.prefabName)
        {
            item.prefabName = prefab;
            MarkStructureDirty();
        }
        if (int.TryParse(x, out int nextX) && nextX != item.x)
        {
            item.x = nextX;
            MarkStructureDirty();
        }
        if (int.TryParse(y, out int nextY) && nextY != item.y)
        {
            item.y = nextY;
            MarkStructureDirty();
        }
        SetFloat(z, value => item.z = value, item.z);
        string currentZone =
            item.hasMapZoneName ? item.mapZoneName : "";
        if (zone != currentZone)
        {
            item.mapZoneName = zone;
            item.hasMapZoneName = !string.IsNullOrWhiteSpace(zone);
            terrainBrushZone = zone;
            MarkStructureDirty();
        }

        Sprite[] variants = GetSpriteVariants(item.prefabName);
        if (variants.Length > 1)
        {
            if (item.spriteVariantIndex < 0)
                item.spriteVariantIndex = 0;
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                $"Sprite: {item.spriteVariantIndex + 1}/{variants.Length}");
            if (GUILayout.Button("Previous", GUILayout.Width(75)))
            {
                item.spriteVariantIndex =
                    (item.spriteVariantIndex - 1 + variants.Length) %
                    variants.Length;
                MarkStructureDirty();
            }
            if (GUILayout.Button("Next", GUILayout.Width(55)))
            {
                item.spriteVariantIndex =
                    (item.spriteVariantIndex + 1) % variants.Length;
                MarkStructureDirty();
            }
            GUILayout.EndHorizontal();
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Left")) { item.x--; MarkStructureDirty(); }
        if (GUILayout.Button("Right")) { item.x++; MarkStructureDirty(); }
        if (GUILayout.Button("Up")) { item.y++; MarkStructureDirty(); }
        if (GUILayout.Button("Down")) { item.y--; MarkStructureDirty(); }
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Duplicate"))
        {
            structure.supportingTerrain.Add(new SupportingTerrain
            {
                prefabName = item.prefabName,
                x = item.x + 1,
                y = item.y,
                z = item.z,
                spriteVariantIndex = item.spriteVariantIndex,
                hasMapZoneName = item.hasMapZoneName,
                mapZoneName = item.mapZoneName,
                components = (item.components ??
                              new List<StructureComponent>())
                    .Select(component => new StructureComponent
                    {
                        type = component.type,
                        hierarchyPath = component.hierarchyPath,
                        componentIndex = component.componentIndex,
                        dataBase64 = component.dataBase64
                    }).ToList()
            });
            selectedTerrain = structure.supportingTerrain.Count - 1;
            MarkStructureDirty();
        }
        if (GUILayout.Button("Delete"))
        {
            structure.supportingTerrain.RemoveAt(selectedTerrain);
            selectedTerrain = Mathf.Min(
                selectedTerrain,
                structure.supportingTerrain.Count - 1);
            MarkStructureDirty();
        }
        GUILayout.EndHorizontal();
    }

    private void DrawSelectedObject()
    {
        StructureObject item = structure!.objects[selectedObject];
        GUILayout.Label("Selected object");
        string prefab = LabeledTextField("Prefab", item.prefabName);
        string x = LabeledTextField("X", Format(item.x));
        string y = LabeledTextField("Y", Format(item.y));
        string z = LabeledTextField("Z", Format(item.z));
        if (prefab != item.prefabName)
        {
            item.prefabName = prefab;
            MarkStructureDirty();
        }
        SetFloat(x, value => item.x = value, item.x);
        SetFloat(y, value => item.y = value, item.y);
        SetFloat(z, value => item.z = value, item.z);

        if (PrefabHasSign(item.prefabName) ||
            item.hasEditableSignMessage)
        {
            string currentMessage = item.hasEditableSignMessage
                ? item.signMessage
                : GetPrefabStringField(
                    item.prefabName, "message", typeof(Sign),
                    typeof(SimpleSign));
            string nextMessage =
                ScrollableSignTextField(currentMessage);
            if (nextMessage != currentMessage)
            {
                item.signMessage = nextMessage;
                item.hasEditableSignMessage = true;
                MarkStructureDirty();
            }
        }

        if (item.prefabName.StartsWith(
                "prefab_tile_", StringComparison.OrdinalIgnoreCase) ||
            PrefabHasMapZone(item.prefabName) ||
            item.hasMapZoneName)
        {
            string currentZone =
                item.hasMapZoneName ? item.mapZoneName : "";
            string nextZone = LabeledTextField("Floor zone", currentZone);
            if (nextZone != currentZone)
            {
                item.mapZoneName = nextZone;
                item.hasMapZoneName =
                    !string.IsNullOrWhiteSpace(nextZone);
                terrainBrushZone = nextZone;
                MarkStructureDirty();
            }
        }

        bool nextRangeExtender = GUILayout.Toggle(
            item.npcInteractionRangeExtender,
            " Add NPC interaction range extender");
        if (nextRangeExtender != item.npcInteractionRangeExtender)
        {
            item.npcInteractionRangeExtender = nextRangeExtender;
            MarkStructureDirty();
        }

        int rotationCount = GetTurnableSpriteCount(item.prefabName);
        if (rotationCount > 0)
        {
            if (item.turnableIndex < 0)
                item.turnableIndex = 0;
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                $"Furniture rotation: {item.turnableIndex + 1}/{rotationCount}");
            if (GUILayout.Button("Previous", GUILayout.Width(75)))
            {
                item.turnableIndex =
                    (item.turnableIndex - 1 + rotationCount) % rotationCount;
                item.occupiedOffsets = GetPrefabOccupiedOffsets(
                    item.prefabName, item.turnableIndex);
                MarkStructureDirty();
            }
            if (GUILayout.Button("Next", GUILayout.Width(55)))
            {
                item.turnableIndex =
                    (item.turnableIndex + 1) % rotationCount;
                item.occupiedOffsets = GetPrefabOccupiedOffsets(
                    item.prefabName, item.turnableIndex);
                MarkStructureDirty();
            }
            GUILayout.EndHorizontal();
        }

        Sprite[] variants = item.turnableIndex >= 0
            ? Array.Empty<Sprite>()
            : GetSpriteVariants(item.prefabName);
        if (variants.Length > 1)
        {
            if (item.spriteVariantIndex < 0)
                item.spriteVariantIndex = 0;
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                $"Sprite: {item.spriteVariantIndex + 1}/{variants.Length}");
            if (GUILayout.Button("Previous", GUILayout.Width(75)))
            {
                item.spriteVariantIndex =
                    (item.spriteVariantIndex - 1 + variants.Length) %
                    variants.Length;
                MarkStructureDirty();
            }
            if (GUILayout.Button("Next", GUILayout.Width(55)))
            {
                item.spriteVariantIndex =
                    (item.spriteVariantIndex + 1) % variants.Length;
                MarkStructureDirty();
            }
            GUILayout.EndHorizontal();
        }

        nudgeStep = LabeledTextField("Nudge", nudgeStep);
        float step = TryFloat(nudgeStep, out float parsedStep) &&
                     parsedStep > 0f
            ? parsedStep
            : 0.25f;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Left")) { item.x -= step; MarkStructureDirty(); }
        if (GUILayout.Button("Right")) { item.x += step; MarkStructureDirty(); }
        if (GUILayout.Button("Up")) { item.y += step; MarkStructureDirty(); }
        if (GUILayout.Button("Down")) { item.y -= step; MarkStructureDirty(); }
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Duplicate"))
        {
            StructureObject copy = new()
            {
                prefabName = item.prefabName,
                x = item.x + 1,
                y = item.y,
                z = item.z,
                npcInteractionRangeExtender =
                    item.npcInteractionRangeExtender,
                turnableIndex = item.turnableIndex,
                spriteVariantIndex = item.spriteVariantIndex,
                hasEditableSignMessage =
                    item.hasEditableSignMessage,
                signMessage = item.signMessage,
                hasMapZoneName = item.hasMapZoneName,
                mapZoneName = item.mapZoneName,
                occupiedOffsets = (item.occupiedOffsets ??
                    new List<StructurePosition>())
                    .Select(offset => new StructurePosition
                        { x = offset.x, y = offset.y }).ToList(),
                components = (item.components ??
                              new List<StructureComponent>())
                    .Select(component => new StructureComponent
                    {
                        type = component.type,
                        hierarchyPath = component.hierarchyPath,
                        componentIndex = component.componentIndex,
                        dataBase64 = component.dataBase64
                    }).ToList()
            };
            structure.objects.Add(copy);
            selectedObject = structure.objects.Count - 1;
            MarkStructureDirty();
        }
        if (GUILayout.Button("Delete"))
        {
            structure.objects.RemoveAt(selectedObject);
            selectedObject = Mathf.Min(selectedObject, structure.objects.Count - 1);
            MarkStructureDirty();
        }
        GUILayout.EndHorizontal();
    }

    private static bool PrefabHasSign(string prefabName)
    {
        GameObject? prefab =
            Plugin.ResolvePrefab(prefabName);
        return prefab != null &&
               (prefab.GetComponentInChildren<Sign>(true) != null ||
                prefab.GetComponentInChildren<SimpleSign>(true) != null);
    }

    private static bool PrefabHasMapZone(string prefabName)
    {
        GameObject? prefab =
            Plugin.ResolvePrefab(prefabName);
        return prefab != null &&
               prefab.GetComponentInChildren<MapZone>(true) != null;
    }

    private static string GetPrefabStringField(
        string prefabName, string fieldName, params Type[] componentTypes)
    {
        GameObject? prefab =
            Plugin.ResolvePrefab(prefabName);
        if (prefab == null)
            return "";
        foreach (Type type in componentTypes)
        {
            Component component = prefab.GetComponentInChildren(type, true);
            FieldInfo field = type.GetField(
                fieldName,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            if (component != null && field?.GetValue(component) is string value)
                return value;
        }

        return "";
    }

    private static string LabeledTextField(string label, string value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(65));
        value = GUILayout.TextField(value);
        GUILayout.EndHorizontal();
        return value;
    }

    private string ScrollableSignTextField(string value)
    {
        GUILayout.Label("Sign text");
        float contentWidth = 275f;
        float contentHeight = Mathf.Max(
            52f,
            GUI.skin.textArea.CalcHeight(
                new GUIContent(value ?? ""), contentWidth));
        signTextScroll = GUILayout.BeginScrollView(
            signTextScroll,
            alwaysShowHorizontal: false,
            alwaysShowVertical: contentHeight > 68f,
            GUILayout.Height(72f));
        value = GUILayout.TextArea(
            value ?? "",
            GUILayout.Width(contentWidth),
            GUILayout.Height(contentHeight));
        GUILayout.EndScrollView();
        return value;
    }

    private void SetFloat(string text, Action<float> set, float oldValue)
    {
        if (TryFloat(text, out float value) && !Mathf.Approximately(value, oldValue))
        {
            set(value);
            MarkStructureDirty();
        }
    }

    private void RefreshFiles()
    {
        Directory.CreateDirectory(Plugin.StructuresDirectory);
        files.Clear();
        files.AddRange(Directory.GetFiles(Plugin.StructuresDirectory, "*.json")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
        checkedFiles.RemoveWhere(path => !files.Contains(
            path, StringComparer.OrdinalIgnoreCase));
        status = $"{files.Count} structure file(s) found.";
    }

    private void BuildPlacementCatalog()
    {
        placementCatalog.Clear();
        try
        {
            placementCatalog.AddRange(
                Plugin.GetKnownPrefabNames()
                    .Where(name =>
                    {
                        if (IsUnsafeQuickPlaceName(name))
                            return false;
                        GameObject? prefab =
                            Plugin.ResolvePrefab(name);
                        if (name.Equals(
                                "prefab_furniture_railing_wood",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            return prefab != null &&
                                   prefab.GetComponentsInChildren<SpriteRenderer>(
                                       includeInactive: true)
                                       .Any(renderer => renderer.sprite != null);
                        }
                        return prefab != null &&
                               prefab.GetComponent<EntityMover>() == null &&
                               prefab.GetComponent<NeuralNPC>() == null &&
                               prefab.GetComponent<Enemy>() == null &&
                               prefab.GetComponentsInChildren<SpriteRenderer>(
                                   includeInactive: true)
                                   .Any(renderer => renderer.sprite != null);
                    })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(CatalogDisplayName, StringComparer.OrdinalIgnoreCase));
            const string woodenRailing =
                "prefab_furniture_railing_wood";
            if (!placementCatalog.Contains(
                    woodenRailing,
                    StringComparer.OrdinalIgnoreCase) &&
                Plugin.ResolvePrefab(woodenRailing) != null)
            {
                placementCatalog.Add(woodenRailing);
                placementCatalog.Sort((left, right) =>
                    StringComparer.OrdinalIgnoreCase.Compare(
                        CatalogDisplayName(left),
                        CatalogDisplayName(right)));
            }
        }
        catch (Exception exception)
        {
            Plugin.Log.LogError(
                "Could not build the editor placement catalog: " + exception);
            status = "Could not build the quick-place catalog.";
        }
    }

    private static string CatalogDisplayName(string prefabName)
    {
        string value = prefabName.StartsWith(
                "prefab_", StringComparison.OrdinalIgnoreCase)
            ? prefabName.Substring("prefab_".Length)
            : prefabName;
        return value.Replace('_', ' ');
    }

    private static int GetDefaultTurnableIndex(string prefabName)
    {
        Turnable? turnable =
            Plugin.ResolvePrefab(prefabName)
                ?.GetComponentInChildren<Turnable>(true);
        return turnable == null ? -1 : turnable.GetIndex();
    }

    private static int GetTurnableSpriteCount(string prefabName)
    {
        Turnable? turnable =
            Plugin.ResolvePrefab(prefabName)
                ?.GetComponentInChildren<Turnable>(true);
        return turnable?.sprites?.Length ?? 0;
    }

    internal static List<StructurePosition> GetPrefabOccupiedOffsets(
        string prefabName, int turnableIndex)
    {
        GameObject? prefab = Plugin.ResolvePrefab(prefabName);
        Turnable? turnable =
            prefab?.GetComponentInChildren<Turnable>(true);
        BoxCollider2D? box =
            prefab?.GetComponentInChildren<BoxCollider2D>(true);
        if (box != null)
        {
            Vector2 min = box.offset - box.size * 0.5f;
            Vector2 max = box.offset + box.size * 0.5f;
            List<Vector2Int> cells = new();
            // Tile anchors are integer coordinates. Treat the collider's lower
            // edge as inclusive and upper edge as exclusive so centered
            // even-sized colliders cover the expected number of cells.
            for (int x = Mathf.CeilToInt(min.x - 0.001f);
                 x < Mathf.CeilToInt(max.x - 0.001f); x++)
            for (int y = Mathf.CeilToInt(min.y - 0.001f);
                 y < Mathf.CeilToInt(max.y - 0.001f); y++)
                cells.Add(new Vector2Int(x, y));
            if (cells.Count > 0)
            {
                int turns = turnable != null &&
                            turnable.sprites?.Length == 4
                    ? (turnableIndex - turnable.GetIndex() + 4) % 4
                    : 0;
                for (int turn = 0; turn < turns; turn++)
                    cells = cells.Select(cell =>
                        new Vector2Int(-cell.y, cell.x)).ToList();
                if (!cells.Contains(Vector2Int.zero))
                    cells.Add(Vector2Int.zero);
                return cells.Distinct().Select(cell =>
                    new StructurePosition { x = cell.x, y = cell.y }).ToList();
            }
        }
        if (turnable == null || turnable.sprites == null ||
            turnable.sprites.Length == 0)
            return new List<StructurePosition>
                { new() { x = 0, y = 0 } };

        int index = (turnableIndex % turnable.sprites.Length +
                     turnable.sprites.Length) % turnable.sprites.Length;
        Bounds bounds = turnable.sprites[index].bounds;
        int minX = Mathf.FloorToInt(bounds.min.x + 0.001f);
        int maxX = Mathf.CeilToInt(bounds.max.x - 0.001f) - 1;
        int minY = Mathf.FloorToInt(bounds.min.y + 0.001f);
        int maxY = Mathf.CeilToInt(bounds.max.y - 0.001f) - 1;
        List<StructurePosition> result = new();
        for (int x = minX; x <= maxX; x++)
        for (int y = minY; y <= maxY; y++)
            result.Add(new StructurePosition { x = x, y = y });
        if (!result.Any(offset => offset.x == 0 && offset.y == 0))
            result.Add(new StructurePosition { x = 0, y = 0 });
        return result;
    }

    internal static HashSet<Vector2Int> GetObjectCells(StructureObject item)
    {
        int anchorX = Mathf.RoundToInt(item.x);
        int anchorY = Mathf.RoundToInt(item.y);
        List<StructurePosition> offsets =
            item.occupiedOffsets != null && item.occupiedOffsets.Count > 0
                ? item.occupiedOffsets
                : new List<StructurePosition> { new() };
        return offsets.Select(offset =>
            new Vector2Int(anchorX + offset.x, anchorY + offset.y)).ToHashSet();
    }

    private static bool IsQuickPlaceSupportingTerrain(string prefabName)
    {
        return prefabName.StartsWith(
            "prefab_tile_", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetQuickPlaceCategory(string prefabName)
    {
        GameObject? prefab = Plugin.ResolvePrefab(prefabName);
        if (IsStructuralQuickPlace(prefabName, prefab))
            return 1;

        if (prefabName.ContainsAnyIgnoreCase(
                "tree", "grass", "bush", "plant", "crop", "flower",
                "herb", "mushroom", "rock", "boulder", "ore", "stump",
                "log", "reed", "vine", "cactus", "sapling"))
            return 3;

        if (prefab != null &&
            (prefab.GetComponent<Tree>() != null ||
             prefab.GetComponent<ResourceNode>() != null))
            return 3;

        if (IsQuickPlaceSupportingTerrain(prefabName))
            return 0;

        if (prefabName.ContainsAnyIgnoreCase(
                "furniture", "chair", "table", "bench", "bed", "bath",
                "sofa", "couch", "shelf", "cabinet", "counter", "stool",
                "desk", "wardrobe", "dresser", "rug", "carpet", "towel",
                "candle", "lantern", "lamp", "decoration", "decor"))
            return 2;

        return 4;
    }

    private static bool IsStructuralQuickPlace(
        string prefabName, GameObject? prefab)
    {
        if (prefabName.StartsWith(
                "prefab_wall_", StringComparison.OrdinalIgnoreCase) ||
            prefabName.ContainsAnyIgnoreCase(
                "door", "window", "railing", "fence", "gate", "roof",
                "stair", "bridge", "foundation", "pillar", "column"))
            return true;

        if (prefabName.StartsWith(
                "prefab_tile_", StringComparison.OrdinalIgnoreCase) &&
            prefabName.ContainsAnyIgnoreCase(
                "floor", "wood", "plank", "stone", "brick", "cobble",
                "concrete", "metal", "marble", "slate", "carpet"))
            return true;

        return prefab != null &&
               (prefab.GetComponentInChildren<Door>(true) != null ||
                prefab.GetComponentInChildren<Window>(true) != null);
    }

    private static float GetQuickPlaceDefaultZ(string prefabName)
    {
        GameObject? prefab =
            Plugin.ResolvePrefab(prefabName);
        bool isWallDoorOrWindow =
            prefabName.StartsWith(
                "prefab_wall_", StringComparison.OrdinalIgnoreCase) ||
            prefabName.ContainsAnyIgnoreCase("door", "window") ||
            (prefab != null &&
             (prefab.GetComponentInChildren<Door>(true) != null ||
              prefab.GetComponentInChildren<Window>(true) != null));
        return isWallDoorOrWindow ? 0.5f : 1f;
    }

    private static bool IsUnsafeQuickPlaceName(string prefabName)
    {
        return string.IsNullOrWhiteSpace(prefabName) ||
               prefabName.ContainsAnyIgnoreCase(
                   "player", "npc_", "enemy", "monster",
                   "projectile", "particle", "effect_", "vfx", "sfx",
                   "audio", "music", "camera", "cursor", "selector",
                   "marker", "spawner", "spawnpoint", "trigger",
                   "cutscene", "dialog", "quest", "ui_", "canvas");
    }

    private void Load(string path)
    {
        try
        {
            StructureFile loaded =
                ReadEditableFile(path, out int normalizedTiles);
            structure = loaded;
            InvalidateRenderOrder();
            currentPath = path;
            saveAsName = Path.GetFileNameWithoutExtension(path);
            selectedObject = -1;
            selectedTerrain = -1;
            movementReferenceSelection = -1;
            previewPan = Vector2.zero;
            previewOrigin = CalculateStructureCenter();
            Vector2 topLeft = GetTopLeftCoordinate();
            offsetX = Format(topLeft.x);
            offsetY = Format(topLeft.y);
            dirty = normalizedTiles > 0;
            status = $"Loaded {loaded.objects.Count} objects and " +
                     $"{loaded.supportingTerrain.Count} terrain records." +
                     (normalizedTiles > 0
                         ? $" Normalized {normalizedTiles} tile object(s) to terrain."
                         : "");
        }
        catch (Exception exception)
        {
            status = "Could not load file: " + exception.Message;
            Plugin.Log.LogError(exception);
        }
    }

    private void CombineCheckedFiles()
    {
        if (dirty)
        {
            status =
                "Save the current structure before combining checked files.";
            return;
        }

        try
        {
            List<StructureFile> sources = checkedFiles
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => ReadEditableFile(path, out _))
                .ToList();

            List<StructureObject> objects = sources
                .SelectMany(source =>
                    source.objects ?? new List<StructureObject>())
                .GroupBy(GetObjectMergeKey, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();
            List<SupportingTerrain> terrain = sources
                .SelectMany(source =>
                    source.supportingTerrain ??
                    new List<SupportingTerrain>())
                .GroupBy(GetTerrainMergeKey, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();

            structure = new StructureFile
            {
                formatVersion = 3,
                name = "Combined Structure",
                gameVersion = Application.version,
                exportedUtc = DateTime.UtcNow.ToString("O"),
                objects = objects,
                supportingTerrain = terrain,
                occupiedPositions = new List<StructurePosition>(),
                serializedObjectsBase64 = ""
            };
            InvalidateRenderOrder();
            currentPath = "";
            saveAsName = "Combined Structure";
            selectedObject = -1;
            selectedTerrain = -1;
            movementReferenceSelection = -1;
            previewPan = Vector2.zero;
            previewOrigin = CalculateStructureCenter();
            Vector2 topLeft = GetTopLeftCoordinate();
            offsetX = Format(topLeft.x);
            offsetY = Format(topLeft.y);
            MarkStructureDirty();
            status =
                $"Combined {sources.Count} JSON files into " +
                $"{objects.Count} objects and {terrain.Count} terrain records. " +
                "Choose Save As to create the shared JSON.";
        }
        catch (Exception exception)
        {
            status = "Could not combine files: " + exception.Message;
            Plugin.Log.LogError(exception);
        }
    }

    private static StructureFile ReadEditableFile(
        string path, out int normalizedTiles)
    {
        object? deserialized = StringSerializationAPI.Deserialize(
            typeof(StructureFile), File.ReadAllText(path));
        if (deserialized is not StructureFile file)
            throw new InvalidDataException(
                $"Could not read {Path.GetFileName(path)} as a structure file.");
        if (file.formatVersion != 3)
            throw new InvalidDataException(
                "Only readable format-3 files can be edited safely.");
        file.objects ??= new List<StructureObject>();
        file.supportingTerrain ??= new List<SupportingTerrain>();
        file.occupiedPositions ??= new List<StructurePosition>();
        normalizedTiles =
            StructureFileNormalizer.MoveTileObjectsToTerrain(file);
        return file;
    }

    private static string GetObjectMergeKey(StructureObject item)
    {
        string componentData = string.Join(
            ";",
            (item.components ?? new List<StructureComponent>())
                .Select(component =>
                    component.type + ":" +
                    component.hierarchyPath + ":" +
                    component.componentIndex + ":" +
                    component.dataBase64));
        return string.Join("|",
            item.prefabName,
            item.x.ToString("R", CultureInfo.InvariantCulture),
            item.y.ToString("R", CultureInfo.InvariantCulture),
            item.z.ToString("R", CultureInfo.InvariantCulture),
            item.npcInteractionRangeExtender,
            item.turnableIndex,
            item.spriteVariantIndex,
            item.hasEditableSignMessage,
            item.signMessage,
            item.hasMapZoneName,
            item.mapZoneName,
            componentData);
    }

    private static string GetTerrainMergeKey(SupportingTerrain item)
    {
        string componentData = string.Join(
            ";",
            (item.components ?? new List<StructureComponent>())
                .Select(component =>
                    component.type + ":" +
                    component.hierarchyPath + ":" +
                    component.componentIndex + ":" +
                    component.dataBase64));
        return string.Join("|",
            item.prefabName,
            item.x,
            item.y,
            item.z.ToString("R", CultureInfo.InvariantCulture),
            item.spriteVariantIndex,
            item.hasMapZoneName,
            item.mapZoneName,
            componentData);
    }

    private void CreateBlankStructure()
    {
        if (dirty)
        {
            status =
                "Save the current structure before creating a blank one.";
            return;
        }

        string baseName = "New Structure";
        string path = Path.Combine(
            Plugin.StructuresDirectory, baseName + ".json");
        int suffix = 2;
        while (File.Exists(path))
        {
            path = Path.Combine(
                Plugin.StructuresDirectory,
                $"{baseName} {suffix++}.json");
        }

        structure = new StructureFile
        {
            formatVersion = 3,
            name = Path.GetFileNameWithoutExtension(path),
            gameVersion = Application.version,
            exportedUtc = DateTime.UtcNow.ToString("O"),
            occupiedPositions = new List<StructurePosition>(),
            supportingTerrain = new List<SupportingTerrain>(),
            objects = new List<StructureObject>(),
            serializedObjectsBase64 = ""
        };
        InvalidateRenderOrder();
        currentPath = path;
        saveAsName = structure.name;
        selectedObject = -1;
        selectedTerrain = -1;
        movementReferenceSelection = -1;
        previewPan = Vector2.zero;
        previewOrigin = Vector2.zero;
        offsetX = "0";
        offsetY = "0";
        MarkStructureDirty();
        Save(path);
        RefreshFiles();
        status =
            $"Created blank structure {Path.GetFileName(path)}. " +
            "Use Quick Place to begin building.";
    }

    private void AddObject()
    {
        if (structure == null || string.IsNullOrWhiteSpace(addPrefabName) ||
            !TryFloat(addX, out float x) ||
            !TryFloat(addY, out float y) ||
            !TryFloat(addZ, out float z))
        {
            status = "Enter a prefab name and valid X, Y, and Z numbers.";
            return;
        }

        structure.objects.Add(new StructureObject
        {
            prefabName = addPrefabName.Trim(),
            x = x,
            y = y,
            z = z,
            npcInteractionRangeExtender =
                addNpcInteractionRangeExtender,
            turnableIndex = GetDefaultTurnableIndex(addPrefabName.Trim()),
            occupiedOffsets = GetPrefabOccupiedOffsets(
                addPrefabName.Trim(),
                GetDefaultTurnableIndex(addPrefabName.Trim()))
        });
        selectedObject = structure.objects.Count - 1;
        MarkStructureDirty();
        status = "Generic object added. It will use the prefab's default state.";
    }

    private void MoveToCoordinate()
    {
        if (structure == null ||
            !TryFloat(offsetX, out float targetX) ||
            !TryFloat(offsetY, out float targetY))
        {
            status = "Enter valid destination coordinates.";
            return;
        }

        Vector2 reference = GetMovementReferenceCoordinate();
        float x = targetX - reference.x;
        float y = targetY - reference.y;
        foreach (StructureObject item in structure.objects)
        {
            item.x += x;
            item.y += y;
        }
        foreach (SupportingTerrain item in structure.supportingTerrain)
        {
            item.x += Mathf.RoundToInt(x);
            item.y += Mathf.RoundToInt(y);
        }
        MarkStructureDirty();
        status =
            $"Moved the structure reference to {Format(targetX)}, {Format(targetY)}.";
    }

    private void SaveAs()
    {
        if (structure == null || string.IsNullOrWhiteSpace(saveAsName) ||
            saveAsName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            status = "Enter a valid Save As name.";
            return;
        }
        Save(Path.Combine(
            Plugin.StructuresDirectory, saveAsName.Trim() + ".json"));
        RefreshFiles();
    }

    private void Save(string path)
    {
        if (structure == null || string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            structure.name = Path.GetFileNameWithoutExtension(path);
            structure.occupiedPositions = structure.objects
                .SelectMany(GetObjectCells)
                .Concat(structure.supportingTerrain.Select(
                    item => new Vector2Int(item.x, item.y)))
                .Distinct()
                .OrderBy(item => item.x)
                .ThenBy(item => item.y)
                .Select(item => new StructurePosition { x = item.x, y = item.y })
                .ToList();
            structure.serializedObjectsBase64 = "";
            File.WriteAllText(
                path,
                StringSerializationAPI.Serialize(typeof(StructureFile), structure));
            currentPath = path;
            dirty = false;
            status = "Saved " + Path.GetFileName(path) + ".";
        }
        catch (Exception exception)
        {
            status = "Could not save: " + exception.Message;
            Plugin.Log.LogError(exception);
        }
    }

    private static string Format(float value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private Vector2 GetTopLeftCoordinate()
    {
        if (structure == null)
            return Vector2.zero;

        List<Vector2> points = structure.objects
            .Select(item => new Vector2(item.x, item.y))
            .Concat(structure.supportingTerrain.Select(
                item => new Vector2(item.x, item.y)))
            .ToList();
        if (points.Count == 0)
            return Vector2.zero;
        return new Vector2(
            points.Min(point => point.x),
            points.Max(point => point.y));
    }

    private Vector2 GetMovementReferenceCoordinate()
    {
        if (structure != null &&
            selectedObject >= 0 &&
            selectedObject < structure.objects.Count)
        {
            StructureObject selected = structure.objects[selectedObject];
            return new Vector2(selected.x, selected.y);
        }
        if (structure != null &&
            selectedTerrain >= 0 &&
            selectedTerrain < structure.supportingTerrain.Count)
        {
            SupportingTerrain selected =
                structure.supportingTerrain[selectedTerrain];
            return new Vector2(selected.x, selected.y);
        }

        return GetTopLeftCoordinate();
    }

    private static bool TryFloat(string value, out float result) =>
        float.TryParse(
            value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
}

internal static class StructureTransfer
{
    private static readonly HashSet<string> BaseShedObjects = new();
    private static Vector2Int? ExportTopLeft;
    private static Vector2Int? ExportBottomRight;
    private static bool HasCompleteExportBounds =>
        ExportTopLeft.HasValue && ExportBottomRight.HasValue;

    internal static string TopLeftButtonLabel =>
        ExportTopLeft.HasValue
            ? $"Top Left: {ExportTopLeft.Value.x}, {ExportTopLeft.Value.y}"
            : "Select Export Top Left";

    internal static string BottomRightButtonLabel =>
        ExportBottomRight.HasValue
            ? $"Bottom Right: {ExportBottomRight.Value.x}, {ExportBottomRight.Value.y}"
            : "Select Export Bottom Right";

    internal static void ToggleTopLeft(Action<string> updateLabel)
    {
        ConfigureBound(topLeft: true, updateLabel);
    }

    internal static void ToggleBottomRight(Action<string> updateLabel)
    {
        ConfigureBound(topLeft: false, updateLabel);
    }

    private static void ConfigureBound(
        bool topLeft,
        Action<string> updateLabel)
    {
        Vector2Int playerPosition =
            Player.Instance.transform.GetVector2IntPosition();
        PauseMenuManager.Instance.Close();

        List<DialogOption> options = new()
        {
            new DialogOption("Use Player Position", () =>
                SetBound(topLeft, playerPosition, updateLabel)),
            new DialogOption("Click World Tile", () =>
                Plugin.Instance.SelectWorldTile(
                    topLeft ? "top-left" : "bottom-right",
                    position => SetBound(topLeft, position, updateLabel))),
            new DialogOption("Type Coordinates", () =>
                TextInputUI.Instance.Open(
                    topLeft
                        ? "Top Left Coordinates (x, y)"
                        : "Bottom Right Coordinates (x, y)",
                    IsValidCoordinates,
                    input =>
                    {
                        SetBound(
                            topLeft, ParseCoordinates(input), updateLabel);
                    }))
        };

        if ((topLeft && ExportTopLeft.HasValue) ||
            (!topLeft && ExportBottomRight.HasValue))
        {
            options.Add(new DialogOption("Clear", () =>
            {
                if (topLeft)
                    ExportTopLeft = null;
                else
                    ExportBottomRight = null;

                updateLabel(topLeft
                    ? TopLeftButtonLabel
                    : BottomRightButtonLabel);
                UpperNotificationUI.Instance.OneOff(
                    topLeft
                        ? "Export top-left bound cleared."
                        : "Export bottom-right bound cleared.");
            }));
        }

        options.Add(new DialogOption("Cancel", null));
        DialogBox.Instance.DisplayTextNoDialog(
            topLeft
                ? "Choose the export top-left bound."
                : "Choose the export bottom-right bound.",
            options.ToArray());
    }

    private static IEnumerator ReturnToStructureControls()
    {
        // Let the text-input dialog finish releasing exclusive pause-menu mode.
        for (int frame = 0; frame < 300; frame++)
        {
            yield return null;
            if (Silverpine.ModdingTools.InventoryModTools.TryOpen(
                    Plugin.PluginGuid + ".game-controls"))
                yield break;
        }
        Plugin.Log.LogWarning(
            "Could not return to Structure Handler after coordinate entry.");
    }

    private static void SetBound(
        bool topLeft,
        Vector2Int position,
        Action<string> updateLabel)
    {
        if (topLeft)
            ExportTopLeft = position;
        else
            ExportBottomRight = position;

        updateLabel(topLeft
            ? TopLeftButtonLabel
            : BottomRightButtonLabel);
        UpperNotificationUI.Instance.OneOff(
            $"Export {(topLeft ? "top-left" : "bottom-right")} set to " +
            $"{position.x}, {position.y}.");
        Player.Instance.StartCoroutine(ReturnToStructureControls());
    }

    private static bool IsValidCoordinates(string input)
    {
        return TryParseCoordinates(input, out _);
    }

    private static Vector2Int ParseCoordinates(string input)
    {
        TryParseCoordinates(input, out Vector2Int position);
        return position;
    }

    private static bool TryParseCoordinates(
        string input,
        out Vector2Int position)
    {
        position = default;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        string[] parts = input.Split(
            new[] { ',', ' ' },
            StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out int x) ||
            !int.TryParse(parts[1], out int y))
            return false;

        position = new Vector2Int(x, y);
        return true;
    }

    internal static void CaptureBaseShed()
    {
        try
        {
            Plot shedPlot = Plot.allPlots.FirstOrDefault(
                plot => plot.owners.Contains(NPCName.None));
            if (shedPlot == null)
            {
                Plugin.Log.LogWarning("Could not find the base-game shed plot.");
                return;
            }

            foreach (GameObject gameObject in
                     UtilityFunctions.GetGameObjectsWithComponent<ISerializableMonoBehavior>())
            {
                if (gameObject != null &&
                    shedPlot.bounds.Contains(gameObject.transform.GetVector3IntPosition()))
                {
                    BaseShedObjects.Add(GetObjectSignature(gameObject));
                }
            }

        }
        catch (Exception exception)
        {
            Plugin.Log.LogError("Could not capture the base shed: " + exception);
        }
    }

    internal static void PromptExport()
    {
        PauseMenuManager.Instance.Close();
        TextInputUI.Instance.Open(
            "Structure Export Name",
            IsValidFileName,
            Export);
    }

    private static bool IsValidFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 80)
            return false;

        return value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    private static void Export(string requestedName)
    {
        try
        {
            if (!TryValidateExportBounds(out string boundsError))
            {
                DialogBox.Instance.DisplayTextNoDialog(boundsError);
                return;
            }

            Directory.CreateDirectory(Plugin.StructuresDirectory);
            string cleanName = requestedName.Trim();
            string path = Path.Combine(Plugin.StructuresDirectory, cleanName + ".json");

            List<GameObject> exportCandidates = FindPlayerStructures();
            List<GameObject> objects = exportCandidates
                .Where(gameObject => !IsTerrainObject(gameObject))
                .ToList();
            List<StructureObject> readableObjects = CreateObjectRecords(objects);
            List<SupportingTerrain> supportingTerrain =
                FindSupportingTerrain(readableObjects);
            if (objects.Count == 0 && supportingTerrain.Count == 0)
            {
                DialogBox.Instance.DisplayTextNoDialog(
                    "No player-built structures or placed furniture were found on an owned plot.");
                return;
            }

            IEnumerable<Vector2Int> allOccupiedPositions = readableObjects
                .SelectMany(StructureEditorUI.GetObjectCells)
                .Concat(supportingTerrain.Select(
                    terrain => new Vector2Int(terrain.x, terrain.y)));
            StructureFile file = new()
            {
                name = cleanName,
                gameVersion = Application.version,
                exportedUtc = DateTime.UtcNow.ToString("O"),
                occupiedPositions = allOccupiedPositions
                    .Distinct()
                    .OrderBy(p => p.x)
                    .ThenBy(p => p.y)
                    .Select(p => new StructurePosition { x = p.x, y = p.y })
                    .ToList(),
                supportingTerrain = supportingTerrain,
                objects = readableObjects,
                // Format 3 is fully readable. Keeping a second binary copy can
                // resurrect obsolete records after users edit the JSON.
                serializedObjectsBase64 = ""
            };

            Action write = () =>
            {
                File.WriteAllText(path, StringSerializationAPI.Serialize(typeof(StructureFile), file));
                DialogBox.Instance.DisplayTextNoDialog(
                    $"Exported {objects.Count} structure/furniture objects and " +
                    $"{supportingTerrain.Count} terrain tiles to <i>\"{path}\"</i>.");
            };

            if (File.Exists(path))
            {
                DialogBox.Instance.DisplayTextNoDialog(
                    $"A structure named <i>\"{cleanName}\"</i> already exists. Overwrite it?",
                    new DialogOption("Overwrite", write),
                    new DialogOption("Cancel", null));
            }
            else
            {
                write();
            }
        }
        catch (Exception exception)
        {
            Plugin.Log.LogError(exception);
            DialogBox.Instance.DisplayTextNoDialog(
                "Failed to export structures: " + exception.Message.Truncate(200));
        }
    }

    internal static void ConfirmImportAll()
    {
        PauseMenuManager.Instance.Close();
        Directory.CreateDirectory(Plugin.StructuresDirectory);
        string[] paths = Directory.GetFiles(Plugin.StructuresDirectory, "*.json")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (paths.Length == 0)
        {
            DialogBox.Instance.DisplayTextNoDialog(
                $"No JSON files were found in <i>\"{Plugin.StructuresDirectory}\"</i>.");
            return;
        }

        string names = string.Join(", ", paths.Select(Path.GetFileNameWithoutExtension));
        DialogBox.Instance.DisplayTextNoDialog(
            $"Import {paths.Length} structure file(s): <i>{names}</i>?\n\n" +
            "Existing world objects occupying their positions will be replaced.",
            new DialogOption("Import All", () => PrepareImport(paths)),
            new DialogOption("Cancel", null));
    }

    private static void PrepareImport(IEnumerable<string> paths)
    {
        try
        {
            List<StructureFile> files = paths.Select(ReadFile).ToList();
            ValidateImportFiles(files);
            List<StructureFile> conflictingFiles = files
                .Where(file => files.Any(other =>
                    other != file &&
                    GetOccupiedPositions(file).Overlaps(
                        GetOccupiedPositions(other))))
                .ToList();

            if (conflictingFiles.Count == 0)
            {
                ImportAll(files);
                return;
            }

            List<Tuple<StructureFile, StructureFile, int>> conflicts = new();
            for (int first = 0; first < files.Count; first++)
            {
                for (int second = first + 1; second < files.Count; second++)
                {
                    int overlapCount = GetOccupiedPositions(files[first])
                        .Intersect(GetOccupiedPositions(files[second]))
                        .Count();
                    if (overlapCount > 0)
                    {
                        conflicts.Add(Tuple.Create(
                            files[first], files[second], overlapCount));
                    }
                }
            }

            Dictionary<StructureFile, int> wins = files.ToDictionary(
                file => file, file => 0);
            ResolveConflict(
                files, conflicts, wins, conflictIndex: 0);
        }
        catch (Exception exception)
        {
            Plugin.Log.LogError(exception);
            DialogBox.Instance.DisplayTextNoDialog(
                "Failed to import structures: " + exception.Message.Truncate(200));
        }
    }

    private static void ResolveConflict(
        List<StructureFile> files,
        List<Tuple<StructureFile, StructureFile, int>> conflicts,
        Dictionary<StructureFile, int> wins,
        int conflictIndex)
    {
        if (conflictIndex >= conflicts.Count)
        {
            List<StructureFile> importOrder = files
                .Select((file, originalIndex) => new
                {
                    file,
                    originalIndex,
                    wins = wins[file]
                })
                .OrderBy(item => item.wins)
                .ThenBy(item => item.originalIndex)
                .Select(item => item.file)
                .ToList();
            ImportAll(importOrder);
            return;
        }

        Tuple<StructureFile, StructureFile, int> conflict =
            conflicts[conflictIndex];
        StructureFile first = conflict.Item1;
        StructureFile second = conflict.Item2;
        string firstName = GetDisplayName(first);
        string secondName = GetDisplayName(second);

        Action<StructureFile> chooseWinner = winner =>
        {
            wins[winner]++;
            ResolveConflict(
                files, conflicts, wins, conflictIndex + 1);
        };

        DialogBox.Instance.DisplayTextNoDialog(
            $"Conflict {conflictIndex + 1} of {conflicts.Count}: " +
            $"<i>{firstName}</i> and <i>{secondName}</i> overlap at " +
            $"{conflict.Item3} position(s).\n\nWhich one should overwrite the other?",
            new DialogOption(firstName + " Wins", () => chooseWinner(first)),
            new DialogOption(secondName + " Wins", () => chooseWinner(second)));
    }

    private static void ImportAll(List<StructureFile> files)
    {
        try
        {
            int importedCount = 0;
            foreach (StructureFile file in files)
            {
                HashSet<Vector2Int> occupied = GetOccupiedPositions(file);
                HashSet<Vector2Int> vegetationClearance =
                    GetVegetationClearancePositions(occupied);
                HashSet<GameObject> replacedObjects =
                    CollectObjectsAt(occupied);
                replacedObjects.UnionWith(
                    CollectVegetationAt(vegetationClearance));
                List<GameObject> createdObjects = new();
                try
                {
                    if (file.formatVersion >= 3)
                    {
                        InstantiateSupportingTerrain(
                            file.supportingTerrain, createdObjects);
                        importedCount += InstantiateObjectRecords(
                            file.objects, createdObjects);
                    }
                    else
                    {
                        byte[] payload = Convert.FromBase64String(
                            file.serializedObjectsBase64);
                        List<GameObject> legacyObjects = SerializationManager
                            .DeserializeSerializable(payload);
                        createdObjects.AddRange(legacyObjects);
                        importedCount += legacyObjects.Count;
                    }

                    // Commit only after every replacement was created and
                    // deserialized successfully. The captured originals remain
                    // untouched if anything above throws.
                    RemoveGameObjects(replacedObjects);
                    if (file.formatVersion >= 3)
                        RegisterImportedObjects(createdObjects);
                    ResourceRegeneration.ReplaceImportedResources(
                        occupied, createdObjects);
                    Plugin.ProtectImportedPositions(occupied);
                }
                catch (Exception exception)
                {
                    RemoveGameObjects(createdObjects);
                    throw new InvalidDataException(
                        $"Import of '{GetDisplayName(file)}' failed; " +
                        "the existing world objects were kept.",
                        exception);
                }
            }

            ObjectPool.CallStarts();
            ActionQueue.Instance.DoAfterXFrames(1, WorldInfoManager.Instance.CheckDarknessMode);
            DialogBox.Instance.DisplayTextNoDialog(
                $"Imported {files.Count} structure file(s) containing {importedCount} objects.");
        }
        catch (Exception exception)
        {
            Plugin.Log.LogError(exception);
            DialogBox.Instance.DisplayTextNoDialog(
                "Failed to import structures: " + exception.Message.Truncate(200));
        }
    }

    private static HashSet<Vector2Int> GetOccupiedPositions(StructureFile file)
    {
        if (file.formatVersion >= 3)
        {
            return (file.objects ?? new List<StructureObject>())
                .SelectMany(StructureEditorUI.GetObjectCells)
                .Concat((file.supportingTerrain ??
                         new List<SupportingTerrain>())
                    .Where(item => ShouldImportTerrain(item.prefabName))
                    .Select(item => new Vector2Int(item.x, item.y)))
                .ToHashSet();
        }

        return file.occupiedPositions
            .Select(position => new Vector2Int(position.x, position.y))
            .ToHashSet();
    }

    private static void ValidateImportFiles(
        IEnumerable<StructureFile> files)
    {
        HashSet<string> missingPrefabs =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (StructureFile file in files)
        {
            if (file.formatVersion < 3)
            {
                Convert.FromBase64String(file.serializedObjectsBase64);
                continue;
            }

            foreach (StructureObject record in
                     file.objects ?? new List<StructureObject>())
            {
                ValidatePosition(record.x, record.y, record.z, record.prefabName);
                if (Plugin.ResolvePrefab(record.prefabName) == null)
                    missingPrefabs.Add(record.prefabName);
                ValidateComponentData(record.components, record.prefabName);
            }
            foreach (SupportingTerrain terrain in
                     file.supportingTerrain ?? new List<SupportingTerrain>())
            {
                if (!ShouldImportTerrain(terrain.prefabName))
                    continue;
                ValidatePosition(
                    terrain.x, terrain.y, terrain.z, terrain.prefabName);
                if (Plugin.ResolvePrefab(terrain.prefabName) == null)
                    missingPrefabs.Add(terrain.prefabName);
                ValidateComponentData(
                    terrain.components, terrain.prefabName);
            }
        }

        if (missingPrefabs.Count > 0)
            throw new InvalidDataException(
                "Import cancelled before changing the world because these " +
                "prefabs are unavailable: " +
                string.Join(", ", missingPrefabs.OrderBy(
                    name => name, StringComparer.OrdinalIgnoreCase)));
    }

    private static void ValidatePosition(
        float x, float y, float z, string prefabName)
    {
        if (float.IsNaN(x) || float.IsInfinity(x) ||
            float.IsNaN(y) || float.IsInfinity(y) ||
            float.IsNaN(z) || float.IsInfinity(z))
            throw new InvalidDataException(
                $"Invalid coordinates for {prefabName}.");
    }

    private static void ValidateComponentData(
        IEnumerable<StructureComponent>? components,
        string prefabName)
    {
        foreach (StructureComponent component in
                 components ?? Enumerable.Empty<StructureComponent>())
        {
            if (string.IsNullOrWhiteSpace(component.type))
                throw new InvalidDataException(
                    $"A component on {prefabName} has no type.");
            Convert.FromBase64String(component.dataBase64 ?? "");
        }
    }

    private static string GetDisplayName(StructureFile file)
    {
        return string.IsNullOrWhiteSpace(file.name) ? "Unnamed Structure" : file.name;
    }

    private static StructureFile ReadFile(string path)
    {
        object? deserialized = StringSerializationAPI.Deserialize(
            typeof(StructureFile), File.ReadAllText(path));
        if (deserialized is not StructureFile file)
            throw new InvalidDataException(
                $"Could not read {Path.GetFileName(path)} as a structure file.");

        if (file.formatVersion < 1 || file.formatVersion > 3)
            throw new InvalidDataException(
                $"Unsupported format version {file.formatVersion} in {Path.GetFileName(path)}.");
        file.objects ??= new List<StructureObject>();
        file.supportingTerrain ??= new List<SupportingTerrain>();
        file.occupiedPositions ??= new List<StructurePosition>();
        file.serializedObjectsBase64 ??= "";
        if (file.formatVersion >= 3)
            StructureFileNormalizer.MoveTileObjectsToTerrain(file);
        bool hasReadableData =
            file.objects.Count > 0 || file.supportingTerrain.Count > 0;
        if (file.formatVersion >= 3 && !hasReadableData)
            throw new InvalidDataException(
                $"No readable object data in {Path.GetFileName(path)}.");
        if (file.formatVersion < 3 &&
            string.IsNullOrWhiteSpace(file.serializedObjectsBase64))
            throw new InvalidDataException(
                $"No legacy object data in {Path.GetFileName(path)}.");

        if (file.formatVersion >= 3)
        {
            file.occupiedPositions = (file.objects ?? new List<StructureObject>())
                .SelectMany(StructureEditorUI.GetObjectCells)
                .Concat((file.supportingTerrain ?? new List<SupportingTerrain>())
                    .Select(item => new Vector2Int(item.x, item.y)))
                .Distinct()
                .Select(position => new StructurePosition
                {
                    x = position.x,
                    y = position.y
                })
                .ToList();
        }

        return file;
    }

    private static List<GameObject> FindPlayerStructures()
    {
        return UtilityFunctions.GetGameObjectsWithComponent<ISerializableMonoBehavior>()
            .Where(gameObject =>
                gameObject != null &&
                gameObject.activeSelf &&
                (HasCompleteExportBounds ||
                 (gameObject.GetComponent<IPersistentSerializableMonoBehavior>() == null &&
                  gameObject.GetComponent<PersistentEntity>() == null)) &&
                (HasCompleteExportBounds ||
                 (!IsNaturalCliffWall(gameObject) &&
                  !BaseShedObjects.Contains(GetObjectSignature(gameObject)))) &&
                IsInsideSelectedExportBounds(
                    gameObject.transform.GetVector2IntPosition()) &&
                (HasCompleteExportBounds ||
                 WorldInfoManager.Instance.IsOnPlayerOwnedPlot(
                     gameObject.transform.GetVector2IntPosition())) &&
                (HasCompleteExportBounds
                    ? IsBoundedExportFixture(gameObject)
                    : gameObject.GetComponent<Deconstructable>() != null ||
                      gameObject.GetComponent<ConstructionSite>() != null ||
                      gameObject.GetComponent<Sign>() != null ||
                      IsPlayerPlacedFurniture(gameObject)))
            .Distinct()
            .ToList();
    }

    private static bool IsBoundedExportFixture(GameObject gameObject)
    {
        if (gameObject == Player.Instance.gameObject ||
            gameObject.GetComponent<EntityMover>() != null ||
            gameObject.GetComponent<NeuralNPC>() != null ||
            gameObject.GetComponent<Enemy>() != null)
            return false;

        return true;
    }

    private static bool IsNaturalCliffWall(GameObject gameObject)
    {
        string prefabName = SerializationManager.GetPrefabName(gameObject);
        return prefabName.Equals(
                   "prefab_wall_rock_cliff",
                   StringComparison.OrdinalIgnoreCase) ||
               prefabName.Contains(
                   "rock_cliff",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryValidateExportBounds(out string error)
    {
        error = "";
        if (!ExportTopLeft.HasValue && !ExportBottomRight.HasValue)
            return true;

        if (!ExportTopLeft.HasValue || !ExportBottomRight.HasValue)
        {
            error = "Export bounds are incomplete. Select both the top-left and " +
                    "bottom-right bounds, or clear both to export everything.";
            return false;
        }

        Vector2Int topLeft = ExportTopLeft.Value;
        Vector2Int bottomRight = ExportBottomRight.Value;
        if (topLeft.x > bottomRight.x || topLeft.y < bottomRight.y)
        {
            error = "The selected export bounds are reversed. Top-left must be " +
                    "left of and above the bottom-right tile.";
            return false;
        }

        return true;
    }

    private static bool IsInsideSelectedExportBounds(Vector2Int position)
    {
        if (!ExportTopLeft.HasValue && !ExportBottomRight.HasValue)
            return true;
        if (!ExportTopLeft.HasValue || !ExportBottomRight.HasValue)
            return false;

        Vector2Int topLeft = ExportTopLeft.Value;
        Vector2Int bottomRight = ExportBottomRight.Value;
        return position.x >= topLeft.x &&
               position.x <= bottomRight.x &&
               position.y <= topLeft.y &&
               position.y >= bottomRight.y;
    }

    private static List<SupportingTerrain> FindSupportingTerrain(
        IEnumerable<StructureObject> objects)
    {
        List<SupportingTerrain> result = new();
        Vector2Int[] objectPositions = objects
            .SelectMany(StructureEditorUI.GetObjectCells)
            .Distinct()
            .ToArray();

        foreach (Vector2Int position in objectPositions)
        {
            IEnumerable<GameObject> terrain = Turfs.GetTurfGameObjects(position)
                .Where(gameObject =>
                    gameObject != null &&
                    gameObject.GetComponent<Door>() == null &&
                    gameObject.GetComponent<Deconstructable>() == null &&
                    gameObject.GetComponent<IPersistentSerializableMonoBehavior>() == null &&
                    gameObject.GetComponent<PersistentEntity>() == null &&
                    IsTerrainObject(gameObject));

            foreach (GameObject gameObject in terrain)
            {
                AddTerrainRecord(result, gameObject, position);
            }
        }

        if (HasCompleteExportBounds)
        {
            Vector2Int topLeft = ExportTopLeft.GetValueOrDefault();
            Vector2Int bottomRight = ExportBottomRight.GetValueOrDefault();
            for (int x = topLeft.x; x <= bottomRight.x; x++)
            {
                for (int y = bottomRight.y; y <= topLeft.y; y++)
                {
                    Vector2Int position = new(x, y);
                    foreach (GameObject gameObject in
                             Turfs.GetTurfGameObjects(position)
                                 .Where(IsTerrainObject))
                    {
                        AddTerrainRecord(result, gameObject, position);
                    }
                }
            }
        }

        return result;
    }

    private static void AddTerrainRecord(
        List<SupportingTerrain> result,
        GameObject gameObject,
        Vector2Int position)
    {
        string prefabName = SerializationManager.GetPrefabName(gameObject);
        if (result.Any(item =>
            item.x == position.x &&
            item.y == position.y &&
            item.prefabName == prefabName))
            return;

        result.Add(new SupportingTerrain
        {
            prefabName = prefabName,
            x = position.x,
            y = position.y,
            z = gameObject.transform.position.z,
            spriteVariantIndex = GetCurrentSpriteVariantIndex(gameObject),
            hasMapZoneName =
                TryGetMapZoneName(gameObject, out string zoneName),
            mapZoneName = zoneName,
            components = CreateComponentRecords(gameObject)
        });
    }

    private static void InstantiateSupportingTerrain(
        IEnumerable<SupportingTerrain> supportingTerrain,
        ICollection<GameObject> createdObjects)
    {
        if (supportingTerrain == null)
            return;

        foreach (SupportingTerrain terrain in supportingTerrain)
        {
            if (!ShouldImportTerrain(terrain.prefabName))
                continue;

            GameObject? prefab = Plugin.ResolvePrefab(terrain.prefabName);
            if (prefab == null)
            {
                Plugin.Log.LogWarning(
                    "Missing supporting terrain prefab: " + terrain.prefabName);
                continue;
            }

            GameObject gameObject = UnityEngine.Object.Instantiate(
                prefab,
                new Vector3(terrain.x, terrain.y, terrain.z),
                prefab.transform.rotation);
            createdObjects.Add(gameObject);
            ApplyComponentRecords(
                gameObject, terrain.components, terrain.prefabName);
            ApplySpriteVariant(
                gameObject,
                terrain.prefabName,
                terrain.spriteVariantIndex);
            if (terrain.hasMapZoneName)
                SetMapZoneName(gameObject, terrain.mapZoneName);
        }
    }

    private static bool ShouldImportTerrain(string prefabName)
    {
        bool isWater = prefabName.Contains(
            "water", StringComparison.OrdinalIgnoreCase);
        if (!isWater)
            return true;

        bool isBathhouseWater = prefabName.Contains(
            "water_bathhouse", StringComparison.OrdinalIgnoreCase);
        return isBathhouseWater || Plugin.ImportOtherWaterTiles.Value;
    }

    private static List<StructureObject> CreateObjectRecords(
        IEnumerable<GameObject> gameObjects)
    {
        List<StructureObject> records = new();
        foreach (GameObject gameObject in gameObjects)
        {
            Vector3 position = gameObject.transform.position;
            bool hasSignMessage = TryGetEditableSignMessage(
                gameObject, out string signMessage);
            bool hasZoneName = TryGetMapZoneName(
                gameObject, out string zoneName);
            Turnable turnable =
                gameObject.GetComponentInChildren<Turnable>(true);
            StructureObject record = new()
            {
                prefabName = SerializationManager.GetPrefabName(gameObject),
                x = position.x,
                y = position.y,
                z = position.z,
                npcInteractionRangeExtender =
                    gameObject.GetComponentInChildren<NPCInteractionRangeExtender>(
                        includeInactive: true) != null,
                turnableIndex = turnable != null ? turnable.GetIndex() : -1,
                spriteVariantIndex =
                    turnable == null
                        ? GetCurrentSpriteVariantIndex(gameObject)
                        : -1,
                hasEditableSignMessage = hasSignMessage,
                signMessage = signMessage,
                hasMapZoneName = hasZoneName,
                mapZoneName = zoneName,
                occupiedOffsets = StructureEditorUI.GetPrefabOccupiedOffsets(
                    SerializationManager.GetPrefabName(gameObject),
                    turnable != null ? turnable.GetIndex() : -1)
            };

            record.components = CreateComponentRecords(gameObject);

            records.Add(record);
        }

        return records;
    }

    private static List<StructureComponent> CreateComponentRecords(
        GameObject gameObject)
    {
        List<StructureComponent> result = new();
        IEnumerable<ISerializableMonoBehavior> components =
            gameObject.GetComponentsInChildren<MonoBehaviour>(true)
                .OfType<ISerializableMonoBehavior>();
        foreach (ISerializableMonoBehavior component in components)
        {
            if (component is not Component unityComponent)
                continue;
            Type type = component.GetType();
            ISerializableMonoBehavior[] sameType =
                unityComponent.gameObject.GetComponents<MonoBehaviour>()
                    .OfType<ISerializableMonoBehavior>()
                    .Where(candidate => candidate.GetType() == type)
                    .ToArray();
            int componentIndex = Array.IndexOf(sameType, component);
            using MemoryStream stream = new();
            using BinaryWriter writer = new(stream);
            component.Serialize(writer);
            writer.Flush();
            result.Add(new StructureComponent
            {
                type = (type.FullName ?? type.Name) + ", " +
                       type.Assembly.GetName().Name,
                hierarchyPath = GetHierarchyPath(
                    gameObject.transform, unityComponent.transform),
                componentIndex = componentIndex,
                dataBase64 = Convert.ToBase64String(stream.ToArray())
            });
        }

        return result;
    }

    private static string GetHierarchyPath(
        Transform root, Transform target)
    {
        if (target == root)
            return "";
        Stack<int> indices = new();
        Transform? current = target;
        while (current != null && current != root)
        {
            indices.Push(current.GetSiblingIndex());
            current = current.parent;
        }
        return current == root
            ? string.Join("/", indices)
            : "";
    }

    private static Transform? ResolveHierarchyPath(
        Transform root, string hierarchyPath)
    {
        if (string.IsNullOrWhiteSpace(hierarchyPath))
            return root;
        Transform current = root;
        foreach (string part in hierarchyPath.Split('/'))
        {
            if (!int.TryParse(
                    part,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int siblingIndex) ||
                siblingIndex < 0 ||
                siblingIndex >= current.childCount)
                return null;
            current = current.GetChild(siblingIndex);
        }
        return current;
    }

    private static bool ComponentTypeMatches(
        Type candidate, string savedType)
    {
        string savedFullName = (savedType ?? "")
            .Split(',')[0]
            .Trim();
        string compactName =
            (candidate.FullName ?? candidate.Name) + ", " +
            candidate.Assembly.GetName().Name;
        return candidate.AssemblyQualifiedName == savedType ||
               compactName == savedType ||
               candidate.FullName == savedType ||
               candidate.FullName == savedFullName ||
               candidate.Name == savedType;
    }

    private static void ApplyComponentRecords(
        GameObject gameObject,
        IEnumerable<StructureComponent>? savedComponents,
        string prefabName)
    {
        foreach (StructureComponent savedComponent in
                 savedComponents ?? Enumerable.Empty<StructureComponent>())
        {
            Transform? target = ResolveHierarchyPath(
                gameObject.transform, savedComponent.hierarchyPath);
            if (target == null)
            {
                Plugin.Log.LogWarning(
                    $"Missing component path '{savedComponent.hierarchyPath}' " +
                    $"on {prefabName}.");
                continue;
            }

            ISerializableMonoBehavior[] matches =
                target.GetComponents<MonoBehaviour>()
                    .OfType<ISerializableMonoBehavior>()
                    .Where(candidate => ComponentTypeMatches(
                        candidate.GetType(), savedComponent.type))
                    .ToArray();
            int index = savedComponent.componentIndex >= 0
                ? savedComponent.componentIndex
                : 0;
            if (index >= matches.Length)
            {
                Plugin.Log.LogWarning(
                    $"Missing component {savedComponent.type} " +
                    $"at '{savedComponent.hierarchyPath}' on {prefabName}.");
                continue;
            }

            byte[] data = Convert.FromBase64String(
                savedComponent.dataBase64 ?? "");
            using MemoryStream stream = new(data);
            using BinaryReader reader = new(stream);
            matches[index].Deserialize(reader);
        }
    }

    private static int InstantiateObjectRecords(
        IEnumerable<StructureObject> records,
        ICollection<GameObject> createdObjects)
    {
        int count = 0;
        foreach (StructureObject record in records)
        {
            GameObject? prefab = Plugin.ResolvePrefab(record.prefabName);
            if (prefab == null)
            {
                Plugin.Log.LogWarning("Missing prefab: " + record.prefabName);
                continue;
            }

            Vector3 position = new(record.x, record.y, record.z);
            GameObject gameObject;
            if (ObjectPool.IsObjectPoolTarget(prefab))
            {
                gameObject = ObjectPool.Claim(prefab, position);
            }
            else
            {
                gameObject = UnityEngine.Object.Instantiate(
                    prefab, position, prefab.transform.rotation);
            }
            createdObjects.Add(gameObject);
            ApplyComponentRecords(
                gameObject, record.components, record.prefabName);

            if (record.npcInteractionRangeExtender &&
                gameObject.GetComponentInChildren<NPCInteractionRangeExtender>(
                    includeInactive: true) == null)
            {
                gameObject.AddComponent<NPCInteractionRangeExtender>();
            }

            Turnable importedTurnable =
                gameObject.GetComponentInChildren<Turnable>(true);
            if (record.turnableIndex >= 0 &&
                importedTurnable != null)
            {
                importedTurnable.SetRotation(record.turnableIndex);
            }

            ApplySpriteVariant(gameObject, record);
            if (record.hasEditableSignMessage)
                SetEditableSignMessage(gameObject, record.signMessage);
            if (record.hasMapZoneName)
                SetMapZoneName(gameObject, record.mapZoneName);
            count++;
        }

        return count;
    }

    private static int GetCurrentSpriteVariantIndex(GameObject gameObject)
    {
        string prefabName = SerializationManager.GetPrefabName(gameObject);
        Sprite[] variants =
            StructureEditorUI.GetSpriteVariantsForPrefab(prefabName);
        if (variants.Length < 2)
            return -1;

        SpriteRenderer renderer =
            gameObject.GetComponentsInChildren<SpriteRenderer>(true)
                .FirstOrDefault(candidate =>
                    candidate.enabled && candidate.sprite != null);
        return renderer == null
            ? -1
            : Array.IndexOf(variants, renderer.sprite);
    }

    private static void ApplySpriteVariant(
        GameObject gameObject, StructureObject record)
    {
        ApplySpriteVariant(
            gameObject, record.prefabName, record.spriteVariantIndex);
    }

    private static void ApplySpriteVariant(
        GameObject gameObject, string prefabName, int spriteVariantIndex)
    {
        if (spriteVariantIndex < 0 ||
            gameObject.GetComponentInChildren<Turnable>(true) != null)
            return;

        Sprite[] variants =
            StructureEditorUI.GetSpriteVariantsForPrefab(prefabName);
        if (variants.Length == 0)
            return;

        SpriteRenderer renderer =
            gameObject.GetComponentsInChildren<SpriteRenderer>(true)
                .FirstOrDefault(candidate =>
                    candidate.enabled && candidate.sprite != null);
        if (renderer == null)
            return;

        int index = (spriteVariantIndex % variants.Length +
                     variants.Length) % variants.Length;
        renderer.sprite = variants[index];
    }

    private static bool TryGetEditableSignMessage(
        GameObject gameObject, out string message)
    {
        foreach (Component component in new Component[]
                 {
                     gameObject.GetComponentInChildren<Sign>(true),
                     gameObject.GetComponentInChildren<SimpleSign>(true)
                 })
        {
            if (component != null &&
                TryGetStringField(component, "message", out message))
                return true;
        }

        message = "";
        return false;
    }

    private static void SetEditableSignMessage(
        GameObject gameObject, string message)
    {
        foreach (Component component in new Component[]
                 {
                     gameObject.GetComponentInChildren<Sign>(true),
                     gameObject.GetComponentInChildren<SimpleSign>(true)
                 })
        {
            if (component != null)
                SetStringField(component, "message", message);
        }
    }

    private static bool TryGetMapZoneName(
        GameObject gameObject, out string zoneName)
    {
        MapZone mapZone = gameObject.GetComponentInChildren<MapZone>(true);
        if (mapZone != null &&
            TryGetStringField(mapZone, "zoneName", out zoneName))
            return true;

        zoneName = "";
        return false;
    }

    private static void SetMapZoneName(
        GameObject gameObject, string zoneName)
    {
        MapZone mapZone = gameObject.GetComponentInChildren<MapZone>(true);
        if (mapZone != null)
            SetStringField(mapZone, "zoneName", zoneName);
    }

    private static bool TryGetStringField(
        Component component, string fieldName, out string value)
    {
        FieldInfo field = component.GetType().GetField(
            fieldName,
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic);
        if (field?.GetValue(component) is string text)
        {
            value = text;
            return true;
        }

        value = "";
        return false;
    }

    private static void SetStringField(
        Component component, string fieldName, string value)
    {
        FieldInfo field = component.GetType().GetField(
            fieldName,
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic);
        field?.SetValue(component, value ?? "");
    }

    private static bool IsPlayerPlacedFurniture(GameObject gameObject)
    {
        Pickupable pickupable = gameObject.GetComponent<Pickupable>();

        // Pickupable.owned means the furnishing still belongs to the world/NPC
        // and cannot be picked up. The placement code explicitly sets it false
        // when the player places the furnishing.
        return pickupable != null && !pickupable.owned;
    }

    private static HashSet<GameObject> CollectObjectsAt(
        HashSet<Vector2Int> positions)
    {
        HashSet<GameObject> existing = new();
        foreach (Vector2Int position in positions)
        {
            foreach (GameObject gameObject in Turfs.GetTurfGameObjects(position))
            {
                if (gameObject == null ||
                    gameObject == Player.Instance.gameObject ||
                    gameObject.GetComponent<NeuralNPC>() != null ||
                    gameObject.GetComponent<IPersistentSerializableMonoBehavior>() != null ||
                    gameObject.GetComponent<PersistentEntity>() != null)
                    continue;

                if (gameObject.GetComponentsInChildren<MonoBehaviour>(true)
                        .OfType<ISerializableMonoBehavior>().Any() ||
                    IsTerrainObject(gameObject))
                    existing.Add(gameObject);
            }
        }

        // An object instantiated on a world tile other than the player's
        // current tile may not be present in Turfs yet. Scan serialized scene
        // objects as well so later imports can still replace it instead of
        // creating an invisible/off-tile stack.
        foreach (GameObject gameObject in
                 UtilityFunctions.GetGameObjectsWithComponent<ISerializableMonoBehavior>())
        {
            if (gameObject == null ||
                !positions.Contains(
                    gameObject.transform.GetVector2IntPosition()) ||
                gameObject == Player.Instance.gameObject ||
                gameObject.GetComponent<NeuralNPC>() != null ||
                gameObject.GetComponent<IPersistentSerializableMonoBehavior>() != null ||
                gameObject.GetComponent<PersistentEntity>() != null)
                continue;

            existing.Add(gameObject);
        }

        return existing;
    }

    private static HashSet<GameObject> CollectVegetationAt(
        HashSet<Vector2Int> positions)
    {
        HashSet<GameObject> vegetation = new();
        foreach (Vector2Int position in positions)
        {
            foreach (GameObject gameObject in Turfs.GetTurfGameObjects(position))
            {
                if (gameObject != null &&
                    !IsTerrainObject(gameObject) &&
                    (gameObject.GetComponent<Tree>() != null ||
                     gameObject.name.ContainsAnyIgnoreCase(
                         "grass", "tree", "bush", "plant", "herb", "crop")))
                {
                    vegetation.Add(gameObject);
                }
            }
        }

        return vegetation;
    }

    private static void RemoveGameObjects(
        IEnumerable<GameObject> gameObjects)
    {
        foreach (GameObject gameObject in gameObjects
                     .Where(item => item != null)
                     .Distinct())
        {
            if (ObjectPool.IsObjectPoolTarget(gameObject))
                ObjectPool.Release(gameObject);
            else
            {
                // Deactivation removes gameplay interaction immediately while
                // allowing Unity to run normal end-of-frame destruction.
                gameObject.SetActive(false);
                UnityEngine.Object.Destroy(gameObject);
            }
        }
    }

    private static void RegisterImportedObjects(
        IEnumerable<GameObject> gameObjects)
    {
        foreach (TurfRegistrar registrar in gameObjects
                     .Where(item => item != null)
                     .SelectMany(item =>
                         item.GetComponentsInChildren<TurfRegistrar>(true))
                     .Where(item => item != null)
                     .Distinct())
        {
            registrar.Register();
        }
    }

    private static HashSet<Vector2Int> GetVegetationClearancePositions(
        HashSet<Vector2Int> occupied)
    {
        // Correct occupied footprints now include every covered cell, so
        // neighboring tiles no longer need destructive vegetation clearing.
        return new HashSet<Vector2Int>(occupied);
    }

    private static string GetObjectSignature(GameObject gameObject)
    {
        Vector2Int position = gameObject.transform.GetVector2IntPosition();
        return SerializationManager.GetPrefabName(gameObject) + "|" +
               position.x + "|" + position.y;
    }

    private static bool IsTerrainObject(GameObject gameObject)
    {
        string prefabName = SerializationManager.GetPrefabName(gameObject);
        return prefabName.StartsWith(
                   "prefab_tile_", StringComparison.OrdinalIgnoreCase) &&
               gameObject.GetComponent<Door>() == null;
    }

}

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

[HarmonyPatch(typeof(SerializationManager), nameof(SerializationManager.Save))]
internal static class ResourceMarkerSavePatch
{
    private static void Postfix(string __0)
    {
        ResourceRegeneration.SaveForSave(__0);
    }
}

[HarmonyPatch(typeof(SerializationManager), nameof(SerializationManager.Load))]
internal static class ResourceMarkerLoadPatch
{
    private static void Prefix(string __0)
    {
        ResourceRegeneration.LoadForSave(__0);
    }
}

[HarmonyPatch(typeof(MainMenuUI), "Start")]
internal static class ResourceMarkerMainMenuPatch
{
    private static void Prefix()
    {
        ResourceRegeneration.ResetForMainMenu();
    }
}

[HarmonyPatch(typeof(WalkUpManager), "OnNewDay")]
internal static class ResourceMarkerMidnightPatch
{
    private static void Postfix(int __0)
    {
        ResourceRegeneration.RegenerateAtMidnight(__0);
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

        ResourceRegeneration.RemoveMarkersForWorldTile(
            __instance.position);
        return true;
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
