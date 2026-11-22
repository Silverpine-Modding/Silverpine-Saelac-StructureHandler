#nullable enable
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace StructureHandler;

internal static class TurfRegistrationLifecycle
{
    private static readonly FieldInfo? PoolOwners = typeof(ObjectPool).GetField("claimedMappings",
        BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly FieldInfo? Registered = typeof(TurfRegistrar).GetField("registered",
        BindingFlags.Instance | BindingFlags.NonPublic);

    internal static bool ShouldRunDestroy(TurfRegistrar registrar, bool registered)
    {
        // Native OnDestroy clears preventRegistration even when no removal is
        // needed. Leave that branch, live registrars, and all discrepancies in
        // which native state says "registered" to the game.
        if (registered || registrar.preventRegistration || registrar.gameObject.activeInHierarchy) return true;
        if (!(PoolOwners?.GetValue(null) is IDictionary owners)) return true;
        for (var current = registrar.transform; current != null; current = current.parent)
        {
            // Release deactivates the claimed root before invoking its cleanup
            // callbacks. Claim reactivates that same root at its new coordinates.
            // A disabled non-pool child or mere prefab-name match is not proof.
            if (current.gameObject.activeSelf || !owners.Contains(current.gameObject)) continue;
            // A different object at the same cell must not receive cleanup
            // callbacks on behalf of this one. A genuinely present instance
            // must still be removed (even if something bypassed Register()).
            return Turfs.TryGetTurfGameObjects(registrar.transform.GetVector2IntPosition(), out var occupants) &&
                occupants.Contains(registrar.gameObject);
        }
        return true;
    }

    internal static void RegisterIfLive(TurfRegistrar registrar)
    {
        if (registrar.gameObject.activeInHierarchy && !registrar.preventRegistration &&
            !(Registered?.GetValue(registrar) is true))
            registrar.Register();
    }
}
