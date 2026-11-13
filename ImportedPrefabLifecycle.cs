#nullable enable
using System;
using Silverpine.ModdingTools;
using UnityEngine;

namespace StructureHandler;

internal static class ImportedPrefabLifecycle
{
    internal static void Prepare(GameObject instance, string prefabName)
    {
        if (!SerializablePrefabs.TryGetRegistration(prefabName, out var registration) ||
            ReferenceEquals(registration.Template, instance)) return;

        // ModdingTools applies these options during native save deserialization.
        // Direct structure imports bypass that path. Honor the same contract on
        // the fresh clone before Deserialize, so its Awake dependencies exist.
        // Never activate the persistent template or deliberately inactive children.
        var options = registration.Options;
        if (options.ClearLoadedHideFlags) instance.hideFlags = HideFlags.None;
        options.OnInstanceRestored?.Invoke(instance);
        if (options.ActivateLoadedInstances && !instance.activeSelf) instance.SetActive(true);
    }
}
