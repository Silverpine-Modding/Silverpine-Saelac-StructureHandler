#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace StructureHandler;

// Wrap only the native per-component *framed* call sites. The persistent-entity
// serializer, item/inventory serializers and readable structure exports do not
// all provide separate streams per component; never append data to those.
internal static class KitsuneInscription
{
    private const string PrefabName = "prefab_furniture_statue_kitsune";
    private static readonly FieldInfo Message = AccessTools.Field(typeof(SimpleSign), "message");

    internal static void Install()
    {
        var harmony = new Harmony(Plugin.PluginGuid + ".kitsune-inscription");
        try
        {
            if (Message?.FieldType != typeof(string)) throw new MissingFieldException("SimpleSign.message");
            var writer = AccessTools.GetDeclaredMethods(typeof(SerializationManager)).Single(m =>
                m.Name.StartsWith("<SerializeSerializable>g__Serialize|", StringComparison.Ordinal));
            var reader = AccessTools.Method(typeof(SerializationManager), nameof(SerializationManager.DeserializeSerializable));
            harmony.Patch(writer, transpiler: new HarmonyMethod(typeof(KitsuneInscription), nameof(PatchWrite)));
            harmony.Patch(reader, transpiler: new HarmonyMethod(typeof(KitsuneInscription), nameof(PatchRead)));
        }
        catch (Exception error)
        {
            harmony.UnpatchSelf(); // Do not leave one half of this feature enabled.
            Plugin.Log.LogError("Kitsune inscription persistence could not be installed: " + error);
        }
    }

    internal static IEnumerable<CodeInstruction> PatchWrite(IEnumerable<CodeInstruction> code) =>
        Wrap(code, nameof(ISerializableMonoBehavior.Serialize), nameof(WriteComponent));

    internal static IEnumerable<CodeInstruction> PatchRead(IEnumerable<CodeInstruction> code) =>
        Wrap(code, nameof(ISerializableMonoBehavior.Deserialize), nameof(ReadComponent));

    private static IEnumerable<CodeInstruction> Wrap(IEnumerable<CodeInstruction> code, string nativeName, string wrapper)
    {
        var list = code.ToList();
        var native = AccessTools.Method(typeof(ISerializableMonoBehavior), nativeName);
        var call = list.Single(instruction => instruction.Calls(native));
        // Same stack signature; retain the instruction's labels and exception blocks.
        call.opcode = OpCodes.Call;
        call.operand = AccessTools.Method(typeof(KitsuneInscription), wrapper);
        return list;
    }

    private static bool TryStatue(ISerializableMonoBehavior component, out SimpleSign sign, out string defaultText)
    {
        sign = null!;
        defaultText = "";
        if (!(component is Turnable turnable) || !turnable.TryGetComponent(out sign) ||
            SerializationManager.GetPrefabName(turnable.gameObject) != PrefabName) return false;
        var prefab = SerializationManager.GetPrefabFromName(PrefabName);
        if (prefab == null || prefab == turnable.gameObject || !prefab.TryGetComponent<SimpleSign>(out var template)) return false;
        defaultText = (string?)Message.GetValue(template) ?? "";
        return true;
    }

    // Run the game's component callback exactly once, including other mods'
    // patches. The native component-count and type-name fields remain unaltered.
    internal static void WriteComponent(ISerializableMonoBehavior component, BinaryWriter writer)
    {
        component.Serialize(writer);
        if (TryStatue(component, out var sign, out var defaultText))
            KitsuneInscriptionPayload.Append(writer, (string?)Message.GetValue(sign) ?? "", defaultText);
    }

    internal static void ReadComponent(ISerializableMonoBehavior component, BinaryReader reader)
    {
        component.Deserialize(reader);
        if (TryStatue(component, out var sign, out var defaultText))
            Message.SetValue(sign, KitsuneInscriptionPayload.Read(reader) ?? defaultText);
    }
}
