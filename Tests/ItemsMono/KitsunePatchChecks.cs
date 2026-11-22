using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using StructureHandler;

internal static class KitsunePatchChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var type = typeof(StructureItemQuality).Assembly.GetType("StructureHandler.KitsuneInscription", true);
        var writer = AccessTools.GetDeclaredMethods(typeof(SerializationManager)).Single(m =>
            m.Name.StartsWith("<SerializeSerializable>g__Serialize|", StringComparison.Ordinal));
        var reader = AccessTools.Method(typeof(SerializationManager), "DeserializeSerializable");
        foreach (bool write in new[] { true, false })
        {
            var target = write ? writer : reader;
            var original = PatchProcessor.GetOriginalInstructions(target).ToArray();
            var call = AccessTools.Method(typeof(ISerializableMonoBehavior), write ? "Serialize" : "Deserialize");
            var replacement = AccessTools.Method(type, write ? "WriteComponent" : "ReadComponent");
            var result = ((IEnumerable<CodeInstruction>)AccessTools.Method(type, write ? "PatchWrite" : "PatchRead")
                .Invoke(null, new object[] { original.Select(i => new CodeInstruction(i)).ToArray() })).ToArray();
            check(original.Count(i => i.Calls(call)) == 1 && original.Length == result.Length &&
                original.Select((x, i) => x.labels.SequenceEqual(result[i].labels) && x.blocks.SequenceEqual(result[i].blocks) &&
                    (x.Calls(call) ? result[i].opcode == OpCodes.Call && Equals(result[i].operand, replacement)
                        : x.opcode == result[i].opcode && Equals(x.operand, result[i].operand))).All(same => same),
                "Kitsune " + (write ? "write" : "read") + " hook changes exactly the native framed component call.");
        }
    }
}
