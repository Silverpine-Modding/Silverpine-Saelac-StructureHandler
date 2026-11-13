using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using StructureHandler;

internal static class MarketChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var patch = typeof(StructureItemQuality).Assembly.GetType("StructureHandler.MarketCrateHourPatch", true);
        var transpiler = patch.GetMethod("Transpiler", BindingFlags.NonPublic | BindingFlags.Static);
        CodeInstruction[] Rewrite(IEnumerable<CodeInstruction> il) =>
            ((IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { il })).ToArray();
        var nativeSell = AccessTools.Method(typeof(Market), "SellStock");
        var nativeHour = AccessTools.Method(typeof(Market), "OnNewHour");
        var native = PatchProcessor.GetOriginalInstructions(nativeHour).ToArray();
        var result = Rewrite(native);
        bool preserved = result.Length == native.Length;
        int substitutions = 0;
        for (int i = 0; i < native.Length && preserved; i++)
        {
            preserved &= native[i].labels.SequenceEqual(result[i].labels) && native[i].blocks.SequenceEqual(result[i].blocks);
            if (native[i].Calls(nativeSell))
            {
                preserved &= result[i].opcode == OpCodes.Call && ((MethodInfo)result[i].operand).Name == "SellAllStock";
                substitutions++;
            }
            else preserved &= native[i].opcode == result[i].opcode && Equals(native[i].operand, result[i].operand);
        }
        check(preserved && substitutions == 1 && native.Count(i => i.Calls(nativeSell)) == 1,
            "Actual native market IL changes only SellStock; schedule, Mirel condition, price RNG/clamp and branch metadata remain intact.");
        foreach (var invalid in new[] { native.Where(i => !i.Calls(nativeSell)), native.Concat(new[] { new CodeInstruction(OpCodes.Call, nativeSell) }) })
        {
            bool rejected = false;
            try { Rewrite(invalid); }
            catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { rejected = true; }
            check(rejected, "Market transpiler rejects missing/ambiguous native sales calls.");
        }
        var controller = typeof(StructureItemQuality).Assembly.GetType("StructureHandler.MarketCrates", true);
        controller.GetMethod("Install", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        foreach (var target in new[] {
            AccessTools.Method(typeof(StaticIdentifier), "Start"),
            AccessTools.Method(typeof(StaticIdentifier), "OnDestroy"),
            AccessTools.Method(typeof(Market), "Start"),
            AccessTools.Method(typeof(Market), "Deserialize"),
            AccessTools.Method(typeof(Market), "OnDestroy"), nativeHour,
            AccessTools.Method(typeof(MarketUI), "Open") })
        {
            var installed = Harmony.GetPatchInfo(target);
            check(installed != null && installed.Owners.Contains("renegadex.silverpine.customstructures.shared-market"),
                "Shared-market runtime patch installs on " + target.DeclaringType.Name + "." + target.Name + ".");
        }
    }
}
