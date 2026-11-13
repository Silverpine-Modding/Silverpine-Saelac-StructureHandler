#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace StructureHandler;

internal sealed partial class StructureEditorUI
{
    private readonly List<Item> itemCatalog = new();
    private readonly List<Item> filteredItems = new();
    private string itemSearch = "", lastItemSearch = "\0";
    private Vector2 itemCatalogScroll, itemDescriptionScroll, itemLetterScroll;
    private Item? itemDraft;
    private StructureObject? draftRecord, furnitureItemTemplate;
    private string draftName = "", draftDescription = "", originalItemName = "", originalItemDescription = "";
    private string draftSender = "", draftLetter = "", originalSender = "", originalLetter = "";
    private int draftRotation;
    private bool itemBrush;
    private readonly Dictionary<StructureObject, (string Inventory, string World, Item? Item, int Rotation)> itemPreviewCache = new();
    private readonly Dictionary<Sprite, Sprite> smallItemSprites = new();
    private readonly HashSet<string> itemPreviewErrors = new();
    private SpritePreview? itemSpriteLayout;
    private bool itemCatalogLoaded;
    private readonly Dictionary<string, bool> itemModelAvailability = new(StringComparer.Ordinal);
    private readonly Dictionary<StructureObject, (Item Item, int Rotation, SpritePreview? Part)> itemVisuals = new();
    private readonly EditorWorkQueue<StructureObject> itemPreviewQueue = new();
    private int itemPreviewFrame = -1;
    private static readonly Quality[] ItemQualities = (Quality[])Enum.GetValues(typeof(Quality));
    private static readonly string[] ItemQualityLabels = ItemQualities.Select(q => q.ToString()).ToArray();
    private static readonly ItemComponent_Enchantment.EnchantmentType[] ItemEnchantments =
        (ItemComponent_Enchantment.EnchantmentType[])Enum.GetValues(typeof(ItemComponent_Enchantment.EnchantmentType));
    private static readonly string[] ItemEnchantmentLabels = new[] { "None" }.Concat(ItemEnchantments.Select(t => t.ToString())).ToArray();
    private GUIStyle? itemTextStyle;
    private GUISkin? itemTextSkin;

    private void ClearItemEditor()
    {
        itemBrush = false;
        itemDraft = null;
        draftRecord = furnitureItemTemplate = null;
        itemCatalog.Clear();
        itemCatalogLoaded = false;
        filteredItems.Clear();
        lastItemSearch = "\0";
        itemPreviewCache.Clear();
        itemPreviewQueue.Clear();
        itemPreviewFrame = -1;
        itemVisuals.Clear();
        itemModelAvailability.Clear();
        preparedSprites.Clear();
        InvalidateVisuals();
        itemTextStyle = null; itemTextSkin = null;
        itemPreviewErrors.Clear();
        itemSpriteLayout = null;
        foreach (var sprite in smallItemSprites.Values)
        {
            if (sprite == null) continue;
            Destroy(sprite.texture);
            Destroy(sprite);
        }
        smallItemSprites.Clear();
    }

    private void RefreshItemCatalog()
    {
        itemCatalogLoaded = true;
        itemModelAvailability.Clear();
        foreach (var record in itemVisuals.Keys.ToArray()) DiscardItemVisual(record);
        InvalidateVisuals();
        itemCatalog.Clear();
        itemCatalog.AddRange(ItemLibrary.Items.Where(item => item != null).OrderBy(item => item.name, StringComparer.OrdinalIgnoreCase));
        lastItemSearch = "\0";
    }

    private void SelectItemTemplate(Item source)
    {
        try
        {
            furnitureItemTemplate = null;
            var pickup = source.GetItemComponent<ItemComponent_Pickupable>();
            if (pickup != null)
            {
                // Decode bytes only; the native pickupable getters instantiate
                // objects and call Starts, which is unsafe in a menu editor.
                var bytes = typeof(ItemComponent_Pickupable).GetField("serializedGameObject", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pickup) as byte[];
                var records = LegacyStructureReader.Read(Convert.ToBase64String(bytes ?? throw new InvalidDataException("Missing furniture data.")));
                if (records.Count != 1 || Plugin.ResolvePrefab(records[0].prefabName)?.GetComponent<Pickupable>() == null)
                    throw new InvalidDataException("This item does not contain a supported native pickupable prefab.");
                furnitureItemTemplate = records[0];
                StructureItemData.Update(furnitureItemTemplate, "Pickupable", fields => fields["owned"] = false);
                itemDraft = source; // Never mutate/clone its encoded sprite.
                draftRotation = 0;
            }
            else
            {
                itemDraft = StructureItemData.Clone(source);
                itemDraft.RemoveItemComponentsOfType<ItemComponent_VendorItem>();
                SetItemDraftFields(itemDraft, 0);
            }
            draftRecord = null;
            selectedObject = selectedTerrain = -1;
            itemBrush = false;
            status = "Configure the item, then enable Place copies. Right-click cancels placement.";
        }
        catch (Exception exception)
        {
            itemDraft = null;
            furnitureItemTemplate = null;
            status = "Cannot edit this item safely: " + exception.Message;
        }
    }

    private void SetItemDraftFields(Item item, int rotation)
    {
        draftName = originalItemName = item.name;
        draftDescription = originalItemDescription = item.description ?? "";
        draftRotation = rotation;
        var letter = item.GetItemComponent<ItemComponent_Letter>();
        draftSender = originalSender = letter == null ? "" : (string)typeof(ItemComponent_Letter).GetField("sender", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(letter)!;
        draftLetter = originalLetter = letter == null ? "" : (string)typeof(ItemComponent_Letter).GetField("message", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(letter)!;
    }

    private void DrawItemsTab()
    {
        GUILayout.Label("Placeable game items");
        if (!itemCatalogLoaded) RefreshItemCatalog();
        itemSearch = LabeledTextField("Search", itemSearch);
        if (GUILayout.Button("Refresh item library")) RefreshItemCatalog();
        if (lastItemSearch != itemSearch)
        {
            filteredItems.Clear();
            filteredItems.AddRange(itemCatalog.Where(item => (item.name + " " + item.itemCategory).IndexOf(itemSearch, StringComparison.OrdinalIgnoreCase) >= 0));
            lastItemSearch = itemSearch;
        }
        DrawVirtualList(ref itemCatalogScroll, filteredItems.Count, 155,
            index => filteredItems[index].name + " (" + filteredItems[index].itemCategory + ")",
            _ => false, index => SelectItemTemplate(filteredItems[index]));

        if (!itemBrush && selectedObject >= 0 && selectedObject < structure!.objects.Count &&
            StructureItemData.IsItem(structure.objects[selectedObject]))
        {
            DrawSelectedItem(structure.objects[selectedObject]);
            return;
        }
        if (itemDraft == null)
        {
            GUILayout.Label("Choose an item above, or click a placed item in the preview.");
            return;
        }
        GUILayout.Label("Copies of: " + itemDraft.name);
        if (furnitureItemTemplate != null)
        {
            GUILayout.Label("Furniture places its native prefab; " +
                (ignorePlacementRules ? "Ignore Placement Rules is ON. " : "native snapping and collision rules apply. ") +
                "Item-only text/quality do not transfer to placed furniture in the game.");
            int rotations = GetTurnableSpriteCount(furnitureItemTemplate.prefabName);
            if (rotations > 1) DrawItemRotation(rotations, false);
        }
        else DrawItemCustomization();
        bool placing = GUILayout.Toggle(itemBrush, itemBrush ? "Placing copies — right-click to cancel" : "Place copies", GUI.skin.button);
        if (placing != itemBrush)
        {
            itemBrush = placing;
            if (placing)
            {
                StopPlanningBrush(); StopZoneBrush();
                brushPrefabName = ""; deleteBrush = false; lastBrushCell = null;
            }
        }
        GUILayout.Label(furnitureItemTemplate == null
            ? "Left-click any point for one copy. Position is unsnapped, like Place Item. Middle-drag pans; wheel zooms."
            : "Left-click to place furniture; right-click cancels. Select it afterward in Objects to edit it.");
    }

    private void DrawSelectedItem(StructureObject record)
    {
        if (!ReferenceEquals(draftRecord, record))
        {
            try
            {
                itemDraft = StructureItemData.Read(record, out int rotation);
                SetItemDraftFields(itemDraft, rotation);
                draftRecord = record;
                furnitureItemTemplate = null;
            }
            catch (Exception exception)
            {
                GUILayout.Label("Item data cannot be edited: " + exception.Message);
                GUILayout.Label("The original component data is preserved.");
                return;
            }
        }
        GUILayout.Label("Selected item: " + itemDraft!.name);
        string displayedX = Format(record.x), displayedY = Format(record.y);
        GUILayout.Label("Coordinates: press Enter to apply");
        string x = CoordinateTextField(record, "X", displayedX);
        string y = CoordinateTextField(record, "Y", displayedY);
        if (x != displayedX && TryFloat(x, out float nextX)) ChangeObjectPlacement(record, nextX, record.y);
        if (y != displayedY && TryFloat(y, out float nextY)) ChangeObjectPlacement(record, record.x, nextY);
        DrawNudgeControls(record);
        DrawItemCustomization();
        if (GUILayout.Button("Apply item changes"))
        {
            try
            {
                Item result = CustomizedItem();
                StructureItemData.Write(record, result, draftRotation);
                draftRecord = null;
                MarkStructureDirty(geometryChanged: false, renderChanged: true, zonesChanged: false);
                status = "Item changes saved in the structure. Save JSON to keep them.";
            }
            catch (Exception exception) { status = exception.Message; }
        }
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Place copies"))
        {
            try
            {
                itemDraft = CustomizedItem();
                SetItemDraftFields(itemDraft, draftRotation);
                draftRecord = null;
                selectedObject = -1;
                itemBrush = true;
                StopPlanningBrush(); StopZoneBrush(); deleteBrush = false; brushPrefabName = "";
            }
            catch (Exception exception) { status = exception.Message; }
        }
        if (GUILayout.Button("Delete item"))
        {
            structure!.objects.Remove(record);
            selectedObject = -1;
            itemDraft = null; draftRecord = null;
            MarkStructureDirty();
        }
        GUILayout.EndHorizontal();
    }

    private void DrawItemCustomization()
    {
        Item item = itemDraft!;
        bool furniture = item.GetItemComponent<ItemComponent_Pickupable>() != null;
        bool gold = item.GetItemComponent<ItemComponent_Gold>() != null;
        bool letter = item.GetItemComponent<ItemComponent_Letter>() != null;
        bool identity = item.itemCategory != ItemCategory.Material && !furniture && !gold;
        if (identity)
        {
            draftName = LabeledTextField("Name", draftName);
            if (!letter)
            {
                GUILayout.Label($"Description ({ItemCustomizationRules.WordCount(draftDescription)}/30 words)");
                draftDescription = ItemTextArea(draftDescription, ref itemDescriptionScroll, 86);
                GUILayout.Label("The game's LLM limit is words, not characters. Changes apply only when valid.");
            }
        }
        else GUILayout.Label("This item's native placement/generation keeps its original identity.");

        var qualities = ItemQualities;
        Quality current = item.TryGetItemComponent<IQualityItemComponent>(out var quality) ? quality.Quality : Quality.Normal;
        GUILayout.Label("Quality");
        int next = GUILayout.SelectionGrid(Array.IndexOf(qualities, current), ItemQualityLabels, 2);
        if (next >= 0 && next < qualities.Length && qualities[next] != current)
        {
            if (quality == null) item.AddItemComponent(new StructureItemQuality());
            item.SetQuality(qualities[next]);
        }
        if (ItemComponent_Enchantment.IsEnchantable(item))
        {
            var types = ItemEnchantments;
            var enchantment = item.GetItemComponent<ItemComponent_Enchantment>();
            int index = enchantment == null ? 0 : Array.IndexOf(types, enchantment.enchantmentType) + 1;
            GUILayout.Label("Enchantment (one, eligible equipment only)");
            int chosen = GUILayout.SelectionGrid(index, ItemEnchantmentLabels, 2);
            if (chosen != index && chosen >= 0)
            {
                item.RemoveItemComponentsOfType<ItemComponent_Enchantment>();
                if (chosen > 0) item.AddItemComponent(new ItemComponent_Enchantment(types[chosen - 1]));
            }
        }
        if (gold)
        {
            var coins = item.GetItemComponent<ItemComponent_Gold>();
            string amount = LabeledTextField("Gold (0–999)", coins.GetAmount().ToString());
            if (int.TryParse(amount, out int count) && count >= 0 && count <= 999) coins.SetAmount(count);
        }
        if (letter)
        {
            draftSender = LabeledTextField("Sender", draftSender);
            GUILayout.Label($"Letter ({ItemCustomizationRules.WordCount(draftLetter)}/150 words)");
            draftLetter = ItemTextArea(draftLetter, ref itemLetterScroll, 100);
        }
        if (CanRotateItem(item)) DrawItemRotation(8, true);
    }

    private string ItemTextArea(string text, ref Vector2 scroll, float height)
    {
        if (itemTextStyle == null || itemTextSkin != GUI.skin)
        {
            itemTextSkin = GUI.skin;
            itemTextStyle = new GUIStyle(GUI.skin.textArea) { wordWrap = true, stretchWidth = false, fixedWidth = 280 };
        }
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(height));
        string result = GUILayout.TextArea(text, itemTextStyle, GUILayout.Width(280), GUILayout.MinHeight(height - 20));
        GUILayout.EndScrollView();
        return result;
    }

    private void DrawItemRotation(int count, bool degrees)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(degrees ? $"Rotation: {draftRotation}°" : $"Rotation: {draftRotation + 1}/{count}");
        if (GUILayout.Button("Rotate", GUILayout.Width(80)))
            draftRotation = degrees ? (draftRotation + 45) % 360 : (draftRotation + 1) % count;
        GUILayout.EndHorizontal();
    }

    private Item CustomizedItem()
    {
        if (itemDraft == null) throw new InvalidDataException("Choose an item first.");
        string? error = ItemCustomizationRules.ValidateText(draftName, draftDescription, originalItemName, originalItemDescription);
        if (error != null) throw new InvalidDataException(error);
        if (draftSender != originalSender || draftLetter != originalLetter)
        {
            error = ItemCustomizationRules.ValidateLetter(draftSender, draftLetter);
            if (error != null) throw new InvalidDataException(error);
        }
        Item result = StructureItemData.Clone(itemDraft);
        result.name = draftName;
        result.description = draftDescription;
        if (draftDescription != originalItemDescription && !string.IsNullOrWhiteSpace(draftDescription) &&
            result.GetItemComponent<ItemComponent_CustomLore>() == null)
            result.AddItemComponent(new ItemComponent_CustomLore());
        result.GetItemComponent<ItemComponent_Letter>()?.SetMessage(draftSender, draftLetter);
        return result;
    }

    private bool HandleItemInput(Vector2 center)
    {
        if (!itemBrush || itemDraft == null) return false;
        Event current = Event.current;
        if (current.type == EventType.MouseDown && current.button == 1)
        {
            itemBrush = false;
            status = "Item placement cancelled.";
            current.Use();
        }
        else if (current.type == EventType.MouseDown && current.button == 0)
        {
            Vector2 origin = GetStructureCenter();
            float x = origin.x + (current.mousePosition.x - center.x) / zoom;
            float y = origin.y - (current.mousePosition.y - center.y) / zoom;
            try
            {
                StructureObject record;
                if (furnitureItemTemplate != null)
                {
                    record = (StructureObject)StringSerializationAPI.Deserialize(typeof(StructureObject),
                        StringSerializationAPI.Serialize(typeof(StructureObject), furnitureItemTemplate));
                    var profile = Profile(record.prefabName);
                    record.x = ShouldSnap(profile) ? Mathf.Round(x) : x;
                    record.y = ShouldSnap(profile) ? Mathf.Round(y) + profile.Snap / 32f : y;
                    record.turnableIndex = GetTurnableSpriteCount(record.prefabName) > 0 ? draftRotation : -1;
                    record.occupiedOffsets = GetPrefabOccupiedOffsets(record.prefabName, record.turnableIndex);
                }
                else record = StructureItemData.Create(CustomizedItem(), x, y, draftRotation);
                PlaceEditorObject(record, false);
            }
            catch (Exception exception) { status = "Item was not placed: " + exception.Message; }
            current.Use();
        }
        // Consume the drag so one native Place Item click means one copy.
        else if (current.type == EventType.MouseDrag && current.button == 0) current.Use();
        return true;
    }

    private bool CanRotateItem(Item item)
    {
        if (SpinningItemManager.Instance == null) return false;
        string key = item.spriteName ?? "";
        if (!itemModelAvailability.TryGetValue(key, out bool result))
            itemModelAvailability[key] = result = SpinningItemManager.Instance.Has3DModel(item);
        return result;
    }

    private void PruneItemPreviews()
    {
        if (itemPreviewCache.Count == 0 && itemPreviewQueue.Count == 0) return;
        var live = new HashSet<StructureObject>(structure!.objects);
        itemPreviewQueue.Retain(live);
        foreach (var record in itemPreviewCache.Keys.Where(record => !live.Contains(record)).ToArray())
        {
            itemPreviewCache.Remove(record);
            DiscardItemVisual(record);
        }
    }

    private void DiscardItemVisual(StructureObject record)
    {
        if (!itemVisuals.TryGetValue(record, out var visual)) return;
        if (visual.Part != null && visual.Part.sprite != null)
            preparedSprites.Remove((visual.Part, visual.Part.sprite));
        itemVisuals.Remove(record);
    }

    private bool AddItemPreviewCommands(StructureObject record)
    {
        if (!StructureItemData.IsItem(record)) return false;
        string inventory = record.components.LastOrDefault(c => StructureItemData.IsComponent(c, "WorldInventory"))?.dataBase64 ?? "";
        string world = record.components.LastOrDefault(c => StructureItemData.IsComponent(c, "WorldItem"))?.dataBase64 ?? "";
        if (!itemPreviewCache.TryGetValue(record, out var cache) || cache.Inventory != inventory || cache.World != world ||
            (cache.Item != null && !itemVisuals.ContainsKey(record)))
        {
            itemPreviewQueue.Enqueue(record);
            return true;
        }
        if (!itemVisuals.TryGetValue(record, out var visual)) return true;
        var part = visual.Part;
        if (part == null || part.sprite == null) return true;
        previewDrawCommands.Add(new PreviewDrawCommand
        {
            part = part, sprite = part.sprite, x = record.x, y = record.y,
            key = new PreviewSortKey(part.sortingLayer, part.ySorted ? PreviewSortKey.YOrder(record.y + part.sorterY, part.sorterOffset, 0) : part.sortingOrder,
                part.renderQueue, record.z + part.localZ, previewDrawCommands.Count)
        });
        return true;
    }

    private void ProcessItemPreviewQueue()
    {
        if (itemPreviewQueue.Count == 0 || itemPreviewFrame == Time.frameCount) return;
        itemPreviewFrame = Time.frameCount;
        // A single native model render cannot be interrupted, but never start a
        // second expensive job after the budget expires. At most four icons/frame.
        if (itemPreviewQueue.Process(GenerateItemPreview, PreviewMilliseconds, 2, 4) > 0)
            InvalidateVisuals();
    }

    private static double PreviewMilliseconds() =>
        System.Diagnostics.Stopwatch.GetTimestamp() * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    private void GenerateItemPreview(StructureObject record)
    {
        string inventory = record.components.LastOrDefault(c => StructureItemData.IsComponent(c, "WorldInventory"))?.dataBase64 ?? "";
        string world = record.components.LastOrDefault(c => StructureItemData.IsComponent(c, "WorldItem"))?.dataBase64 ?? "";
        try
        {
            if (!itemPreviewCache.TryGetValue(record, out var cache) || cache.Inventory != inventory || cache.World != world)
            {
                Item decoded = StructureItemData.Read(record, out int rotation);
                cache = (inventory, world, decoded, rotation);
                itemPreviewCache[record] = cache;
            }
            if (cache.Item == null) return;
            Item item = cache.Item;
            if (!itemVisuals.TryGetValue(record, out var visual) || !ReferenceEquals(visual.Item, item) || visual.Rotation != cache.Rotation)
            {
                DiscardItemVisual(record);
                visual = (item, cache.Rotation, BuildItemVisual(item, cache.Rotation));
                itemVisuals[record] = visual;
            }
        }
        catch (Exception exception)
        {
            itemPreviewCache[record] = (inventory, world, null, 0);
            DiscardItemVisual(record);
            if (itemPreviewErrors.Add(inventory)) status = "An item preview is unavailable; its data is preserved. " + exception.Message;
        }
    }

    private SpritePreview? BuildItemVisual(Item item, int rotation)
    {
            Sprite? sprite;
            if (CanRotateItem(item))
            {
                sprite = SpinningItemManager.Instance.GetPreview(item, true, rotation, true);
                // Match WorldItem.GetSprite: very thin models can have an empty
                // view at one angle; the game advances until a visible one exists.
                for (int attempt = 1; attempt < 16 && (sprite == null || sprite.texture == null); attempt++)
                    sprite = SpinningItemManager.Instance.GetPreview(item, true, (rotation + attempt * 45) % 360, true);
            }
            else
            {
                sprite = item.Sprite;
                if (sprite != null && sprite.texture.isReadable && item.GetItemComponent<ItemComponent_Gold>() == null &&
                    item.GetItemComponent<ItemComponent_Pickupable>() == null)
                {
                    if (!smallItemSprites.TryGetValue(sprite, out var small))
                    {
                        Texture2D texture = FantResize.DownsampleImage(sprite.texture, 2.0, 0.25f);
                        texture.filterMode = FilterMode.Point;
                        small = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 32f);
                        smallItemSprites[sprite] = small;
                    }
                    sprite = small;
                }
            }
            if (sprite == null) return null;
            var native = ItemSpriteLayout();
            return new SpritePreview
            {
                sprite = sprite, localAxisX = native?.localAxisX ?? Vector2.right, localAxisY = native?.localAxisY ?? Vector2.up,
                sortingLayer = native?.sortingLayer ?? SortingLayer.GetLayerValueFromName("Characters"),
                sortingOrder = native?.sortingOrder ?? 0, renderQueue = native?.renderQueue ?? 3000,
                ySorted = native?.ySorted ?? true, sorterOffset = native?.sorterOffset ?? 0,
                sorterY = native?.sorterY ?? 0, localZ = native?.localZ ?? 0,
                localOrigin = native?.localOrigin ?? Vector2.zero,
                flipX = native?.flipX ?? false, flipY = native?.flipY ?? false,
                color = native?.color ?? Color.white
            };
    }

    private SpritePreview? ItemSpriteLayout()
    {
        if (itemSpriteLayout != null) return itemSpriteLayout;
        var prefab = Plugin.ResolvePrefab("prefab_item");
        if (prefab == null) return null;
        // Match WorldItem.UpdateSprite's exact renderer, including its initially
        // empty sprite. The generic preview collector would choose the shadow.
        var renderer = prefab.GetComponentInChildren<SpriteRenderer>();
        if (renderer == null) return null;
        var sorter = prefab.GetComponentsInChildren<YSorter>(true)
            .FirstOrDefault(candidate => candidate.gameObject.GetSpriteRendererExtended() == renderer);
        Vector3 origin = renderer.transform.position - prefab.transform.position;
        Vector3 axisX = renderer.transform.TransformVector(Vector3.right);
        Vector3 axisY = renderer.transform.TransformVector(Vector3.up);
        itemSpriteLayout = new SpritePreview
        {
            localOrigin = new Vector2(origin.x, origin.y), localZ = origin.z,
            localAxisX = new Vector2(axisX.x, axisX.y), localAxisY = new Vector2(axisY.x, axisY.y),
            sortingLayer = SortingLayer.GetLayerValueFromID(renderer.sortingLayerID), sortingOrder = renderer.sortingOrder,
            renderQueue = renderer.sharedMaterial != null ? renderer.sharedMaterial.renderQueue : 3000,
            ySorted = sorter != null, sorterY = sorter != null ? sorter.transform.position.y - prefab.transform.position.y : 0,
            sorterOffset = sorter != null ? sorter.offset : 0,
            flipX = renderer.flipX, flipY = renderer.flipY, color = renderer.color
        };
        return itemSpriteLayout;
    }
}
