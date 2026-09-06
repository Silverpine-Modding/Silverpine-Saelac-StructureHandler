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

internal sealed partial class StructureControlsUI :
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
        layout.childForceExpandHeight = false;

        Button template =
            Silverpine.ModdingTools.ModUi.GetInventoryButtonTemplate(inventory);
        AddTitle(template, "Structure Handler");
        AddButton(template, "Structures Export", () => CloseAndRun(StructureTransfer.PromptExport));
        AddButton(template, "Structures Import", () => ShowImportSelection(inventory));
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
        ClearPanel();
        Build(inventory);
    }

    private void ClearPanel()
    {
        foreach (Transform child in transform.Cast<Transform>().ToArray())
        {
            // Destroy is deferred; remove old controls from layout and input now.
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
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
