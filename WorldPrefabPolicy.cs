#nullable enable
using System;
using System.IO;
using UnityEngine;

namespace StructureHandler;

internal static class WorldPrefabPolicy
{
    // A registered child turf can belong to the player/NPC even when no
    // character component is on that child. Never export or delete that branch.
    internal static bool CharacterOrSystem(GameObject item) =>
        item.GetComponentInParent<EntityMover>(true) != null ||
        item.GetComponentInParent<NeuralNPC>(true) != null ||
        item.GetComponentInParent<Player>(true) != null ||
        item.GetComponentInParent<Enemy>(true) != null ||
        item.GetComponentInParent<IPersistentSerializableMonoBehavior>(true) != null;

    internal static bool ProtectedFromRemoval(GameObject item) =>
        CharacterOrSystem(item) || item.GetComponentInParent<PersistentEntity>(true) != null ||
        (item.transform.childCount != 0 && (item.GetComponentInChildren<EntityMover>(true) != null ||
        item.GetComponentInChildren<Player>(true) != null || item.GetComponentInChildren<NeuralNPC>(true) != null ||
        item.GetComponentInChildren<Enemy>(true) != null || item.GetComponentInChildren<PersistentEntity>(true) != null ||
        item.GetComponentInChildren<IPersistentSerializableMonoBehavior>(true) != null));

    internal static string? CannotReconstruct(GameObject prefab)
    {
        if (prefab.GetComponentInChildren<EntityMover>(true) != null || prefab.GetComponentInChildren<Player>(true) != null ||
            prefab.GetComponentInChildren<NeuralNPC>(true) != null || prefab.GetComponentInChildren<Enemy>(true) != null ||
            prefab.GetComponentInChildren<IPersistentSerializableMonoBehavior>(true) != null)
            return "contains a character or a persistent game system";
        if (prefab.GetComponent<ISerializableMonoBehavior>() != null)
        {
            // Check the actual ID the native writer emits, not the name in
            // the JSON or the path used to discover this asset in memory.
            var savedName = SerializationManager.GetPrefabName(prefab);
            if (SerializationManager.GetPrefabFromName(savedName) != prefab)
                return $"cannot be restored by Silverpine under its native save ID '{savedName}'; its mod must register a unique saveable prefab";
        }
        else if (prefab.GetComponent<PersistentEntity>() != null)
            return "has a persistent entity marker but no native save component";
        return null;
    }

    internal static void RequirePlacedInstance(GameObject instance, GameObject template, Func<GameObject,GameObject?> supplemental)
    {
        // Awake/custom callbacks and component deserialization can rename,
        // deactivate or destroy a clone even when its original template passed.
        if (instance == null || !instance.activeInHierarchy)
            throw new InvalidDataException($"Restoring '{template.name}' did not leave an active world object.");
        var restored = instance.GetComponent<ISerializableMonoBehavior>() != null
            ? SerializationManager.GetPrefabFromName(SerializationManager.GetPrefabName(instance))
            : supplemental(instance);
        if (restored != template)
            throw new InvalidDataException($"The restored instance of '{template.name}' no longer resolves to its source prefab.");
    }
}
