#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Silverpine.ModdingTools;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StructureHandler;

internal sealed partial class StructureControlsUI
{
    private readonly ImportFileSelection importSelection = new();
    private readonly Dictionary<string, Toggle> importToggles = new(StringComparer.OrdinalIgnoreCase);
    private TextMeshProUGUI? importSummary;
    private TextMeshProUGUI? importButtonLabel;
    private Button? importSelectedButton;

    private void ShowImportSelection(InventoryUI inventory)
    {
        string error = "";
        try
        {
            Directory.CreateDirectory(Plugin.StructuresDirectory);
            importSelection.Refresh(Directory.GetFiles(Plugin.StructuresDirectory, "*.json"));
        }
        catch (Exception exception)
        {
            importSelection.Refresh(Array.Empty<string>());
            error = "Could not list structure files: " + exception.Message;
            Plugin.Log.LogError(exception);
        }

        // Keep the existing Mod Tools session and overlay: background controls
        // and player input stay blocked while the selection page is visible.
        ClearPanel();
        importToggles.Clear();
        var layout = GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(24, 24, 24, 24);
        layout.spacing = 8f;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        Button template = ModUi.GetInventoryButtonTemplate(inventory);

        ImportText(template, transform, "Import structures", 40, 32);
        importSummary = ImportText(template, transform, "", 30, 22);

        Transform toolbar = ImportButtonRow("Selection actions", 40);
        ImportButton(template, toolbar, "Select All", () =>
        {
            importSelection.SelectAll();
            UpdateImportChecks();
        }, 40);
        ImportButton(template, toolbar, "Clear Selection", () =>
        {
            importSelection.Clear();
            UpdateImportChecks();
        }, 40);
        ImportButton(template, toolbar, "Refresh", () => ShowImportSelection(inventory), 40);

        RectTransform content = CreateImportScrollList();
        if (importSelection.Paths.Count == 0)
        {
            ImportText(template, content, string.IsNullOrEmpty(error)
                ? "No JSON files found in custom structures.\nExport or save a structure, then click Refresh."
                : error, 100, 20, wrap: true);
        }
        else
        {
            foreach (string path in importSelection.Paths)
                AddImportCheckbox(template, content, path);
        }

        ImportText(template, transform,
            "Selected files replace objects at their saved coordinates.\nOnly selected files are checked for import conflicts.",
            48, 18, wrap: true);
        Transform footer = ImportButtonRow("Import actions", 52);
        ImportButton(template, footer, "Back", () => Rebuild(inventory), 52);
        importSelectedButton = ImportButton(template, footer, "Import Selected", () =>
        {
            string[] chosenPaths = importSelection.Snapshot();
            if (chosenPaths.Length == 0) return;
            CloseAndRun(() => StructureTransfer.ImportSelected(chosenPaths));
        }, 52);
        importButtonLabel = importSelectedButton.GetComponentInChildren<TextMeshProUGUI>(true);
        UpdateImportChecks();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
    }

    private Transform ImportButtonRow(string name, float height)
    {
        GameObject row = new(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(transform, false);
        var layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        SetImportHeight(row, height);
        return row.transform;
    }

    private static Button ImportButton(Button template, Transform parent, string label, Action action, float height)
    {
        var button = ModUi.CloneButton(template, parent, label, action, height);
        var element = button.GetComponent<LayoutElement>();
        element.minWidth = 0;
        element.preferredWidth = 1;
        element.flexibleWidth = 1;
        SetImportHeight(button.gameObject, height);
        var text = button.GetComponentInChildren<TextMeshProUGUI>(true);
        text.richText = false;
        text.fontSize = 22;
        text.enableAutoSizing = true;
        text.fontSizeMin = 16;
        text.fontSizeMax = 22;
        text.overflowMode = TextOverflowModes.Ellipsis;
        return button;
    }

    private static TextMeshProUGUI ImportText(Button template, Transform parent, string value,
        float height, float size, bool wrap = false)
    {
        var text = ModUi.CloneTitle(template, parent, value, height);
        text.gameObject.SetActive(true);
        text.transform.localScale = Vector3.one;
        text.fontSize = size;
        text.enableAutoSizing = false;
        text.enableWordWrapping = wrap;
        text.richText = false;
        text.raycastTarget = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        SetImportHeight(text.gameObject, height);
        return text;
    }

    private static void SetImportHeight(GameObject target, float height)
    {
        var layout = target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
        layout.minHeight = layout.preferredHeight = height;
        layout.flexibleHeight = 0;
    }

    private RectTransform CreateImportScrollList()
    {
        GameObject root = new("Structure file list", typeof(RectTransform), typeof(Image),
            typeof(ScrollRect), typeof(LayoutElement));
        root.transform.SetParent(transform, false);
        root.GetComponent<Image>().color = new Color(0, 0, 0, 0.3f);
        SetImportHeight(root, 300);
        GameObject viewport = new("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
        viewport.transform.SetParent(root.transform, false);
        RectTransform view = (RectTransform)viewport.transform;
        StretchImportRect(view);
        view.offsetMax = new Vector2(-22, 0);
        viewport.GetComponent<Image>().color = new Color(0, 0, 0, 0.001f);

        GameObject contentObject = new("Files", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentObject.transform.SetParent(viewport.transform, false);
        RectTransform content = (RectTransform)contentObject.transform;
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0.5f, 1);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        var layout = contentObject.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(4, 4, 4, 4);
        layout.spacing = 4;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        contentObject.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject track = new("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
        track.transform.SetParent(root.transform, false);
        RectTransform trackRect = (RectTransform)track.transform;
        trackRect.anchorMin = new Vector2(1, 0);
        trackRect.anchorMax = Vector2.one;
        trackRect.pivot = new Vector2(1, 0.5f);
        trackRect.sizeDelta = new Vector2(18, -8);
        trackRect.anchoredPosition = new Vector2(-2, 0);
        track.GetComponent<Image>().color = new Color(0.25f, 0.28f, 0.32f, 1);
        var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handle.transform.SetParent(track.transform, false);
        StretchImportRect((RectTransform)handle.transform);
        handle.GetComponent<Image>().color = new Color(0.68f, 0.73f, 0.8f, 1);
        var scrollbar = track.GetComponent<Scrollbar>();
        scrollbar.handleRect = (RectTransform)handle.transform;
        scrollbar.targetGraphic = handle.GetComponent<Image>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
        var scroll = root.GetComponent<ScrollRect>();
        scroll.viewport = view;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 38f;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalNormalizedPosition = 1f;
        return content;
    }

    private void AddImportCheckbox(Button template, Transform content, string path)
    {
        Button button = ImportButton(template, content, Path.GetFileName(path), () => { }, 40);
        GameObject row = button.gameObject;
        Graphic graphic = button.targetGraphic;
        var colors = button.colors;
        DestroyImmediate(button);
        Toggle toggle = row.AddComponent<Toggle>();
        toggle.targetGraphic = graphic;
        toggle.colors = colors;
        toggle.transition = Selectable.Transition.ColorTint;
        toggle.navigation = new Navigation { mode = Navigation.Mode.None };
        var label = row.GetComponentInChildren<TextMeshProUGUI>(true);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.enableAutoSizing = false;
        StretchImportRect(label.rectTransform);
        label.rectTransform.offsetMin = new Vector2(44, 0);
        label.rectTransform.offsetMax = new Vector2(-10, 0);
        label.raycastTarget = false;

        GameObject box = new("Checkbox", typeof(RectTransform), typeof(Image));
        box.transform.SetParent(row.transform, false);
        RectTransform boxRect = (RectTransform)box.transform;
        boxRect.anchorMin = boxRect.anchorMax = new Vector2(0, 0.5f);
        boxRect.anchoredPosition = new Vector2(22, 0);
        boxRect.sizeDelta = new Vector2(24, 24);
        box.GetComponent<Image>().color = new Color(0.13f, 0.16f, 0.2f, 1);
        box.GetComponent<Image>().raycastTarget = false;
        GameObject check = new("Selected", typeof(RectTransform), typeof(Image));
        check.transform.SetParent(box.transform, false);
        RectTransform checkRect = (RectTransform)check.transform;
        StretchImportRect(checkRect);
        checkRect.offsetMin = new Vector2(4, 4);
        checkRect.offsetMax = new Vector2(-4, -4);
        var checkGraphic = check.GetComponent<Image>();
        checkGraphic.color = new Color(0.45f, 0.85f, 0.55f, 1);
        checkGraphic.raycastTarget = false;
        toggle.graphic = checkGraphic;
        toggle.SetIsOnWithoutNotify(importSelection.IsSelected(path));
        toggle.onValueChanged.AddListener(value =>
        {
            importSelection.SetSelected(path, value);
            UpdateImportSummary();
        });
        row.GetComponent<ButtonSoundPlayer>()?.Subscribe();
        importToggles[path] = toggle;
    }

    private void UpdateImportChecks()
    {
        foreach (var pair in importToggles)
            if (pair.Value != null) pair.Value.SetIsOnWithoutNotify(importSelection.IsSelected(pair.Key));
        UpdateImportSummary();
    }

    private void UpdateImportSummary()
    {
        int count = importSelection.SelectedCount;
        if (importSummary != null)
            importSummary.text = $"custom structures: {count} of {importSelection.Paths.Count} selected";
        if (importSelectedButton != null) importSelectedButton.interactable = count > 0;
        if (importButtonLabel != null) importButtonLabel.text = $"Import Selected ({count})";
    }

    private static void StretchImportRect(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
