using System.Reflection;
using System.Runtime.CompilerServices;
using StructureHandler;

internal static class MarketChecks
{
    internal static void Run(Action<bool> check)
    {
        Market Crate(float price)
        {
            var market = (Market)RuntimeHelpers.GetUninitializedObject(typeof(Market));
            market.marketItems = new() { new MarketItem { itemName = "Turnip", price = price,
                minPrice = 1, maxPrice = 10, maxChange = 2 } };
            return market;
        }
        var first = Crate(4);
        var second = Crate(9);
        var book = new SharedMarketBook();
        check(book.Capture() == null);
        book.Bind(first); book.Bind(second);
        check(ReferenceEquals(first.marketItems, second.marketItems) && second.marketItems[0].price == 4);
        first.marketItems[0].price = 6;
        check(second.marketItems[0].price == 6);
        var inventoryField = typeof(Market).GetField("marketInventory", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var firstInventory = new Inventory(); var secondInventory = new Inventory();
        inventoryField.SetValue(first, firstInventory); inventoryField.SetValue(second, secondInventory);
        book.Bind(first); book.Bind(second);
        check(ReferenceEquals(inventoryField.GetValue(first), firstInventory) &&
            ReferenceEquals(inventoryField.GetValue(second), secondInventory));
        Console.WriteLine("PASS Market crates share the same live price objects, not copies; native stock remains independent.");

        int sales = 0, priceUpdates = 0;
        foreach (var ignored in new[] { first, second, first })
        {
            if (!book.TryBegin(12, 0)) continue;
            foreach (var crate in new[] { first, second })
            {
                check(crate.marketItems[0].price == 6); sales++;
            }
            first.marketItems[0].price = 7; priceUpdates++;
        }
        check(sales == 2 && priceUpdates == 1 && second.marketItems[0].price == 7);
        check(book.HasProcessed(12, 0) && !book.HasProcessed(13, 0));
        Console.WriteLine("PASS A shared cycle sells both crates against the old board and permits only one price update.");

        var snapshot = book.Capture()!;
        first.marketItems[0].price = 8;
        check(snapshot.prices[0].price == 7);
        var state = new StructureWorldState { sharedMarket = snapshot };
        var restored = (StructureWorldState)StringSerializationAPI.Deserialize(typeof(StructureWorldState),
            StringSerializationAPI.Serialize(typeof(StructureWorldState), state));
        var reload = new SharedMarketBook(); reload.Restore(restored.sharedMarket);
        var staleUnloadedCrate = Crate(2);
        reload.Bind(staleUnloadedCrate);
        check(staleUnloadedCrate.marketItems[0].price == 7 && !reload.TryBegin(12, 0));
        check(reload.TryBegin(13, 0) && !reload.TryBegin(13, 0));
        check(reload.TryBegin(13, 7) && !reload.TryBegin(13, 7));
        Console.WriteLine("PASS Native JSON round-trip preserves the board and processed cycle; streamed stale crates cannot overwrite prices or repeat sales.");

        foreach (string oldPayload in new[] { "{}", "{\"protectedTiles\":[]}", "{\"importedCells\":[]}" })
        {
            var old = (StructureWorldState)StringSerializationAPI.Deserialize(typeof(StructureWorldState), oldPayload);
            check(old.sharedMarket == null);
        }
        reload.Reset(); check(reload.Capture() == null && !reload.HasProcessed(13, 7));
        var nextSaveCrate = Crate(3); reload.Bind(nextSaveCrate);
        check(reload.Prices![0].price == 3 && !ReferenceEquals(reload.Prices, book.Prices));
        reload.Restore(null); check(reload.Capture() == null);
        Console.WriteLine("PASS Old saves seed their native prices; reset/missing companion data cannot leak another save's board.");

        void Reject(Action<SharedMarketState> corrupt)
        {
            var invalid = book.Capture()!; corrupt(invalid);
            var previous = book.Prices;
            try { book.Restore(invalid); }
            catch (InvalidDataException) { check(ReferenceEquals(book.Prices, previous)); return; }
            throw new Exception("Invalid shared market data was accepted.");
        }
        Reject(s => s.prices = null!);
        Reject(s => s.prices.Add(s.prices[0]));
        Reject(s => s.prices[0].price = float.NaN);
        Reject(s => s.prices[0].maxPrice = float.PositiveInfinity);
        Reject(s => s.prices[0].minPrice = 100);
        Reject(s => s.prices[0].maxChange = -1);
        Reject(s => s.prices[0].itemName = "");
        Reject(s => s.lastHour = 25);
        Reject(s => s.lastDay = -2);
        Console.WriteLine("PASS Invalid shared prices/cycle data is rejected without partially changing the active board.");

    }
}
