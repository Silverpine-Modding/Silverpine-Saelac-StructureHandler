#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StructureHandler;

// Only the price board and its cycle stamp are shared. Stock, unsold items and
// ownership stay in each crate's native Market/WorldInventory save records.
internal sealed class SharedMarketBook
{
    internal List<MarketItem>? Prices { get; private set; }
    private int lastDay = -1, lastHour = -1;

    internal void Bind(Market market)
    {
        Prices ??= market.marketItems;
        if (Prices != null) market.marketItems = Prices;
    }

    internal bool HasProcessed(int day, int hour) => day == lastDay && hour == lastHour;
    internal bool TryBegin(int day, int hour)
    {
        if (HasProcessed(day, hour)) return false;
        lastDay = day; lastHour = hour;
        return true;
    }

    internal void Reset() { Prices = null; lastDay = lastHour = -1; }

    internal SharedMarketState? Capture() => Prices == null ? null : new SharedMarketState
    {
        lastDay = lastDay, lastHour = lastHour,
        prices = Prices.Select(item => new SharedMarketPrice
        {
            itemName = item.itemName, price = item.price, minPrice = item.minPrice,
            maxPrice = item.maxPrice, maxChange = item.maxChange
        }).ToList()
    };

    internal void Restore(SharedMarketState? state)
    {
        if (state == null) { Reset(); return; }
        if (state.prices == null || state.lastDay < -1 || state.lastHour < -1 || state.lastHour > 24)
            throw new InvalidDataException("Invalid shared market state.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        var prices = new List<MarketItem>();
        foreach (var saved in state.prices)
        {
            if (saved == null || string.IsNullOrEmpty(saved.itemName) || !names.Add(saved.itemName) ||
                !Finite(saved.price) || !Finite(saved.minPrice) || !Finite(saved.maxPrice) || !Finite(saved.maxChange) ||
                saved.minPrice > saved.maxPrice || saved.maxChange < 0)
                throw new InvalidDataException("Invalid shared market price data.");
            // Use the empty native constructor; the priced constructor rolls RNG.
            prices.Add(new MarketItem { itemName = saved.itemName, price = saved.price,
                minPrice = saved.minPrice, maxPrice = saved.maxPrice, maxChange = saved.maxChange });
        }
        Prices = prices;
        lastDay = state.lastDay; lastHour = state.lastHour;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
