#nullable enable
using System;
using Silverpine.ModdingTools;
using UnityEngine;

namespace StructureHandler;

internal static class RailingPrefab
{
    internal const string Name = "prefab_furniture_railing_wood";
    // Verified in the shipped ResourceManager. This is not in Resources/Prefabs.
    internal const string ResourcePath = "miscprefabs/" + Name;
    private static SerializablePrefabRegistration? registration;
    private static bool warned;

    internal static void EnsureRegistered()
    {
        if (registration?.Template != null) return;
        GameObject? staging = null;
        GameObject? template = null;
        try
        {
            // Respect a native or other-mod registration instead of replacing it.
            if (SerializationManager.GetPrefabFromName(Name) != null) return;
            var source = Resources.Load<GameObject>(ResourcePath);
            if (source == null || source.GetComponent<SpriteRenderer>()?.sprite == null)
                throw new InvalidOperationException("Could not load the wooden railing asset at " + ResourcePath);

            // An inactive parent prevents scene callbacks while preparing the
            // template. The shipped asset and live bridge pieces remain untouched.
            staging = new GameObject("Structure Handler Railing Staging");
            staging.SetActive(false);
            template = UnityEngine.Object.Instantiate(source, staging.transform, false);
            template.SetActive(false);
            template.transform.SetParent(null, false);
            template.name = Name;
            if (template.GetComponent<TurfRegistrar>() == null) template.AddComponent<TurfRegistrar>();
            if (template.GetComponent<StructureRailingState>() == null) template.AddComponent<StructureRailingState>();
            registration = SerializablePrefabs.Register(Plugin.PluginGuid, Name, template);
        }
        catch (Exception exception)
        {
            if (registration == null && template != null) UnityEngine.Object.DestroyImmediate(template);
            if (!warned)
            {
                warned = true;
                Plugin.Log.LogError("Could not register the wooden bridge railing: " + exception);
            }
        }
        finally
        {
            if (staging != null) UnityEngine.Object.DestroyImmediate(staging);
        }
    }

    internal static void ActivateImportedInstance(GameObject instance)
    {
        if (instance.GetComponent<StructureRailingState>() == null ||
            ReferenceEquals(registration?.Template, instance)) return;
        // Native save loads use ModdingTools' activation hook. Direct structure
        // imports must also activate clones of this inactive persistent template.
        instance.hideFlags = HideFlags.None;
        instance.SetActive(true);
    }
}
