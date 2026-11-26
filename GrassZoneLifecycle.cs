#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace StructureHandler;

internal static class GrassZoneLifecycle
{
    internal static string? GrassId(string name) => PooledGrass.Id(name);

    private static readonly FieldInfo Registered = typeof(MapZone).GetField("registered", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingFieldException("MapZone.registered");
    internal static Dictionary<Vector2Int,MapZone> Registry => (Dictionary<Vector2Int,MapZone>)Registered.GetValue(null)!;

    // Claim pops this exact object, moves it, then queues its pool Start. Reset
    // only a free instance, before any callback or saved component is applied.
    // A regular save/tile load will subsequently deserialize its actual label.
    internal static void BeforeClaim(GameObject prefab, Dictionary<GameObject,Stack<GameObject>> pools)
    {
        if (!pools.TryGetValue(prefab, out var stack) || stack.Count == 0 || GrassId(prefab.name) == null) return;
        var instance = stack.Peek();
        if (instance == null || instance.activeSelf || instance == prefab ||
            !instance.TryGetComponent<MapZone>(out var zone) ||
            !prefab.TryGetComponent<MapZone>(out var original)) return;
        if (!string.IsNullOrEmpty(zone.zoneName))
        {
            var previous = instance.transform.GetVector2IntPosition();
            if (Registry.TryGetValue(previous, out var owner) && owner == zone) Registry.Remove(previous);
        }
        zone.zoneName = original.zoneName ?? "";
    }

    internal static bool ApplyImported(MapZone zone, string name)
    {
        if (GrassId(zone.gameObject.name) == null) return false;
        var cell = zone.transform.GetVector2IntPosition();
        if (!Registry.TryGetValue(cell, out var other) || other == null || other == zone)
        {
            if (other == null) Registry.Remove(cell); // includes dead Unity wrappers
            zone.SetZoneName(name ?? ""); // update the registry as well as the serialized value
        }
        else zone.zoneName = name ?? ""; // another live layer owns the lookup; do not evict it
        return true;
    }
    // Native Unity Start / deferred pool Start can follow explicit import
    // registration. Already registered by THIS grass is not an overlap.
    internal static bool ShouldStart(MapZone zone, Dictionary<Vector2Int,MapZone> registry) =>
        string.IsNullOrEmpty(zone.zoneName) || GrassId(zone.gameObject.name) == null ||
        !registry.TryGetValue(zone.transform.GetVector2IntPosition(), out var owner) || owner != zone;

    internal static bool ShouldUnregister(MapZone zone, Dictionary<Vector2Int,MapZone> registry)
    {
        if (string.IsNullOrEmpty(zone.zoneName) ||
            !registry.TryGetValue(zone.transform.GetVector2IntPosition(), out var current) ||
            current == null || current == zone) return true;
        // A second release/destroy, or an overlapping layer, must not remove
        // the grass now registered at this coordinate (or vice versa).
        return GrassId(current.gameObject.name) == null && GrassId(zone.gameObject.name) == null;
    }
}
