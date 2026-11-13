using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using StructureHandler;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        checks++;
        Console.WriteLine("PASS " + message);
    }
    private static readonly Type Codec = typeof(StructureItemQuality).Assembly.GetType("StructureHandler.StructureItemData", true);
    private static object Call(string name, params object[] args) => Codec.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
    private static void Main()
    {
        try { Run(); Console.WriteLine("PASS " + checks + " Unity-Mono item checks."); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }
    private static void Run()
    {
        MarketChecks.Run(Check);
        EditorPerformanceChecks.Run(Check);
        typeof(StructureItemQuality).GetMethod("InstallTypeResolver", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        foreach (Quality quality in Enum.GetValues(typeof(Quality)))
        {
            var item = new Item { name = "Custom blade", description = "A fine blade.", value = 73, bulk = 2.25f };
            item.AddItemComponent(new StructureItemQuality { Quality = quality });
            item.AddItemComponent(new ItemComponent_Durability(42));
            item.AddItemComponent(new ItemComponent_Enchantment(ItemComponent_Enchantment.EnchantmentType.Knowledge));
            var record = Call("Create", item, 1.125f, -3.75f, 315);
            object[] args = { record, 0 };
            var decoded = (Item)Call("Read", args);
            Check(decoded.name == item.name && decoded.description == item.description && decoded.value == 73 && decoded.bulk == 2.25f,
                "Editor record restores item fields (" + quality + ").");
            Check((int)args[1] == 315 && decoded.GetItemComponent<StructureItemQuality>().Quality == quality &&
                decoded.GetItemComponent<ItemComponent_Durability>().Durability == 42,
                "Editor record restores rotation, quality and durability (" + quality + ").");
            // Use the ACTUAL game serializer and Type.GetType resolution, not
            // the editor's strict decoder, for the save/load/pickup test.
            var clone = decoded.DeepClone();
            Check(clone.GetItemComponent<StructureItemQuality>().Quality == quality &&
                clone.GetItemComponent<ItemComponent_Enchantment>().enchantmentType == ItemComponent_Enchantment.EnchantmentType.Knowledge,
                "Native Item deep clone resolves custom quality without other mods (" + quality + ").");
            var inventory = new Inventory();
            inventory.items.Add(decoded);
            var reloaded = new Inventory();
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) inventory.Serialize(writer);
                stream.Position = 0;
                using (var reader = new BinaryReader(stream)) reloaded.Deserialize(reader);
            }
            Check(reloaded.items.Single().GetItemComponent<StructureItemQuality>().Quality == quality &&
                ReferenceEquals(reloaded.items[0].inventory, reloaded), "Native inventory save/load retains quality and back-reference (" + quality + ").");
        }
        var letter = new Item { name = "A letter", description = "" };
        var message = new ItemComponent_Letter(); message.SetMessage("Darian", "Meet at 15, 1.\nBring supplies.");
        letter.AddItemComponent(message);
        var letterCopy = (Item)Call("Clone", letter);
        Check(letterCopy.GetItemComponent<ItemComponent_Letter>().GetNPCVisibleAdditionString().Contains("Meet at 15, 1."), "Letter contents survive native component copying.");
        var coins = new Item(); var gold = new ItemComponent_Gold(1, 8); gold.SetAmount(999); coins.AddItemComponent(gold);
        var coinCopy = (Item)Call("Clone", coins);
        Check(coinCopy.GetItemComponent<ItemComponent_Gold>().GetAmount() == 999, "Gold amount survives without invoking inventory-added gold conversion.");
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "SaltExtraDebug" || a.GetName().Name == "CustomItemLoader"),
            "Native item persistence does not depend on another item/debug mod.");
    }
}
