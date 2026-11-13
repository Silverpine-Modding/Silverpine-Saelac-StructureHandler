using StructureHandler;
using UnityEngine;
using Silverpine.ModdingTools;

internal static class PrefabLifecycleChecks
{
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("Direct import activates registered interior grass and clears template flags without altering its source", () =>
        {
            var template = new GameObject(); var clone = new GameObject();
            const string name = "prefab_tile_terrainconstruction_interior_grass";
            SerializablePrefabs.Registrations[name] = new(template, new());
            ImportedPrefabLifecycle.Prepare(clone, name);
            check(clone.activeSelf && clone.hideFlags == HideFlags.None && clone.Activations == 1);
            check(!template.activeSelf && template.hideFlags == HideFlags.HideAndDontSave && template.Activations == 0);
        });
        test("Imported prefabs honor all activation/hide-flag option combinations and callback-before-Awake order", () =>
        {
            foreach (bool activate in new[] { false, true })
            foreach (bool clear in new[] { false, true })
            foreach (bool alreadyActive in new[] { false, true })
            {
                var clone = new GameObject { activeSelf = alreadyActive };
                int callbacks = 0;
                SerializablePrefabs.Registrations["custom"] = new(new(), new()
                {
                    ActivateLoadedInstances = activate, ClearLoadedHideFlags = clear,
                    OnInstanceRestored = item =>
                    {
                        callbacks++;
                        check(ReferenceEquals(item, clone) && item.activeSelf == alreadyActive);
                        check(item.hideFlags == (clear ? HideFlags.None : HideFlags.HideAndDontSave));
                    }
                });
                ImportedPrefabLifecycle.Prepare(clone, "custom");
                check(callbacks == 1 && clone.activeSelf == (alreadyActive || activate));
                check(clone.Activations == (activate && !alreadyActive ? 1 : 0));
            }
        });
        test("Import preparation never activates registry templates or unregistered/native objects", () =>
        {
            var source = new GameObject();
            SerializablePrefabs.Registrations["template"] = new(source, new()
            {
                OnInstanceRestored = _ => throw new Exception("Template callback must never run.")
            });
            ImportedPrefabLifecycle.Prepare(source, "template");
            check(!source.activeSelf && source.hideFlags == HideFlags.HideAndDontSave);
            ImportedPrefabLifecycle.Prepare(source, "unregistered");
            check(!source.activeSelf && source.hideFlags == HideFlags.HideAndDontSave);
        });
        test("Failed custom-prefab initialization propagates to the import transaction instead of reporting false success", () =>
        {
            var clone = new GameObject();
            SerializablePrefabs.Registrations["broken"] = new(new(), new()
            { OnInstanceRestored = _ => throw new InvalidOperationException("Owner callback failed.") });
            bool rejected = false;
            try { ImportedPrefabLifecycle.Prepare(clone, "broken"); }
            catch (InvalidOperationException) { rejected = true; }
            check(rejected && !clone.activeSelf);
        });
    }
}

// A small lifecycle contract host, not a Unity renderer/scene simulation.
namespace UnityEngine
{
    public enum HideFlags { None = 0, HideAndDontSave = 61 }
    public class GameObject
    {
        public bool activeSelf;
        public HideFlags hideFlags = HideFlags.HideAndDontSave;
        public int Activations;
        public void SetActive(bool value) { activeSelf = value; Activations++; }
    }
}
namespace Silverpine.ModdingTools
{
    public sealed class SerializablePrefabOptions
    {
        public bool ClearLoadedHideFlags { get; set; } = true;
        public bool ActivateLoadedInstances { get; set; } = true;
        public Action<GameObject>? OnInstanceRestored { get; set; }
    }
    public sealed record SerializablePrefabRegistration(GameObject Template, SerializablePrefabOptions Options);
    public static class SerializablePrefabs
    {
        public static readonly Dictionary<string, SerializablePrefabRegistration> Registrations = new();
        public static bool TryGetRegistration(string name, out SerializablePrefabRegistration registration) =>
            Registrations.TryGetValue(name, out registration!);
    }
}
