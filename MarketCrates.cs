#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace StructureHandler;

internal static class MarketCrates
{
    private const string MarketId = "market";
    private static readonly FieldInfo IdentitiesField = AccessTools.Field(typeof(StaticIdentifier), "staticIdentities");
    private static readonly FieldInfo IdField = AccessTools.Field(typeof(StaticIdentifier), "id");
    private static readonly FieldInfo InventoryField = AccessTools.Field(typeof(Market), "marketInventory");
    private static readonly Action<Market> NativeSellStock = AccessTools.MethodDelegate<Action<Market>>(
        AccessTools.Method(typeof(Market), "SellStock"));
    private static readonly HashSet<Market> Members = new();
    private static readonly SharedMarketBook Book = new();
    private static Dictionary<string, GameObject> Identities =>
        (Dictionary<string, GameObject>)IdentitiesField.GetValue(null)!;

    internal static void Install()
    {
        var harmony = new Harmony(Plugin.PluginGuid + ".shared-market");
        harmony.PatchAll(typeof(MarketCrateIdentityStartPatch));
        harmony.PatchAll(typeof(MarketCrateIdentityDestroyPatch));
        harmony.PatchAll(typeof(MarketCrateStartPatch));
        harmony.PatchAll(typeof(MarketCrateDeserializePatch));
        harmony.PatchAll(typeof(MarketCrateDestroyPatch));
        harmony.PatchAll(typeof(MarketCrateHourPatch));
        harmony.PatchAll(typeof(MarketCrateBoardPatch));
    }

    internal static void Reset() { Members.Clear(); Book.Reset(); }
    internal static SharedMarketState? Capture() => Book.Capture();
    internal static void Restore(SharedMarketState? state) => Book.Restore(state);

    internal static void Reconcile()
    {
        // Run on save load and board open, not every frame. The native global
        // market remains the first price source when upgrading an older save.
        Market? original = null;
        if (Identities.TryGetValue(MarketId, out var owner) && owner != null)
            original = owner.GetComponent<Market>();
        if (original != null) Register(original);
        foreach (var market in UnityEngine.Object.FindObjectsOfType<Market>()) Register(market);
        PromoteIdentity();
    }

    private static bool Live(Market market) => market != null && market.gameObject.activeInHierarchy;

    internal static void PrepareStart(Market market)
    {
        if (SerializationManager.loadingSave) return;
        // A new extra crate uses the existing board, without consuming another
        // set of randomized starting prices. Its own sale inventory is new.
        Book.Bind(market);
        if (market.marketItems != null && InventoryField.GetValue(market) == null)
            InventoryField.SetValue(market, new Inventory());
    }

    internal static void Register(Market market)
    {
        if (!Live(market)) return;
        Members.Add(market);
        if (!SerializationManager.loadingSave) Book.Bind(market);
    }

    internal static void Unregister(Market market)
    {
        Members.Remove(market);
        if (Identities.TryGetValue(MarketId, out var owner) && ReferenceEquals(owner, market.gameObject))
        {
            Identities.Remove(MarketId);
            PromoteIdentity(market.gameObject);
        }
    }

    internal static bool RegisterIdentity(StaticIdentifier identifier, string id)
    {
        if (id != MarketId || identifier.GetComponent<Market>() == null) return false;
        Register(identifier.GetComponent<Market>());
        if (!Identities.TryGetValue(MarketId, out var owner) || owner == null)
            Identities[MarketId] = identifier.gameObject;
        return true;
    }

    internal static bool RemoveIdentity(StaticIdentifier identifier, string id)
    {
        if (id != MarketId) return false;
        // Native OnDestroy removes by string alone. A duplicate being unloaded
        // must never unregister the other crate that currently owns the board.
        if (Identities.TryGetValue(MarketId, out var owner) && ReferenceEquals(owner, identifier.gameObject))
        {
            Identities.Remove(MarketId);
            PromoteIdentity(identifier.gameObject);
        }
        return true;
    }

    private static void PromoteIdentity(GameObject? excluding = null)
    {
        if (Identities.TryGetValue(MarketId, out var owner) && owner != null && !ReferenceEquals(owner, excluding)) return;
        Members.RemoveWhere(market => market == null);
        var replacement = Members.FirstOrDefault(market => Live(market) &&
            !ReferenceEquals(market.gameObject, excluding) &&
            market.GetComponents<StaticIdentifier>().Any(id => (string)IdField.GetValue(id)! == MarketId));
        if (replacement != null) Identities[MarketId] = replacement.gameObject;
    }

    internal static bool PrepareHour(Market market)
    {
        if (SerializationManager.loadingSave || !Live(market)) return false;
        Register(market);
        var world = WorldInfoManager.Instance;
        return world != null && !Book.HasProcessed(world.GetCurrentDay(), world.GetCurrentHourOfDay());
    }

    internal static void SellAllStock(Market coordinator)
    {
        var world = WorldInfoManager.Instance;
        if (!Book.TryBegin(world.GetCurrentDay(), world.GetCurrentHourOfDay())) return;
        Register(coordinator);
        Members.RemoveWhere(market => market == null);
        // All crates sell against the same pre-update board. The remainder of
        // native OnNewHour then rolls the shared price list exactly once.
        foreach (var market in Members.Where(Live).ToArray())
        {
            Book.Bind(market);
            if (InventoryField.GetValue(market) == null) continue; // Start has not initialized this crate yet.
            try { NativeSellStock(market); }
            catch (Exception error)
            {
                Plugin.Log.LogError("Market crate could not sell its stock at " + market.transform.position + ": " + error);
            }
        }
    }

    internal static bool PrepareBoard()
    {
        Reconcile();
        if (Identities.TryGetValue(MarketId, out var owner) && owner != null &&
            owner.GetComponent<Market>()?.marketItems != null)
            return true;
        UpperNotificationUI.Instance?.OneOff("No market crate is ready in the loaded area.");
        return false;
    }
}

[HarmonyPatch(typeof(StaticIdentifier), "Start")]
internal static class MarketCrateIdentityStartPatch
{
    private static bool Prefix(StaticIdentifier __instance, string ___id) => !MarketCrates.RegisterIdentity(__instance, ___id);
}
[HarmonyPatch(typeof(StaticIdentifier), "OnDestroy")]
internal static class MarketCrateIdentityDestroyPatch
{
    private static bool Prefix(StaticIdentifier __instance, string ___id) => !MarketCrates.RemoveIdentity(__instance, ___id);
}
[HarmonyPatch(typeof(Market), "Start")]
internal static class MarketCrateStartPatch
{
    private static void Prefix(Market __instance) => MarketCrates.PrepareStart(__instance);
    private static void Postfix(Market __instance) => MarketCrates.Register(__instance);
}
[HarmonyPatch(typeof(Market), nameof(Market.Deserialize))]
internal static class MarketCrateDeserializePatch
{
    private static void Postfix(Market __instance) => MarketCrates.Register(__instance);
}
[HarmonyPatch(typeof(Market), "OnDestroy")]
internal static class MarketCrateDestroyPatch
{
    private static void Prefix(Market __instance) => MarketCrates.Unregister(__instance);
}
[HarmonyPatch(typeof(Market), "OnNewHour")]
internal static class MarketCrateHourPatch
{
    private static bool Prefix(Market __instance) => MarketCrates.PrepareHour(__instance);
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var result = instructions.Select(instruction => new CodeInstruction(instruction)).ToList();
        var sell = AccessTools.Method(typeof(Market), "SellStock");
        var matches = result.Where(instruction => instruction.Calls(sell)).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("Expected one native market sale before its price update.");
        matches[0].opcode = OpCodes.Call;
        matches[0].operand = AccessTools.Method(typeof(MarketCrates), nameof(MarketCrates.SellAllStock));
        return result;
    }
}
[HarmonyPatch(typeof(MarketUI), nameof(MarketUI.Open))]
internal static class MarketCrateBoardPatch
{
    private static bool Prefix() => MarketCrates.PrepareBoard();
}
