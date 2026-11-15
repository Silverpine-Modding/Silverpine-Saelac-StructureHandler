#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace StructureHandler;

internal static class PreviewRendererDiscovery
{
    internal static bool IsBranchActive(Transform current, Transform root)
    {
        // A registered template's root is inactive. Honor activeSelf only below
        // it, otherwise active child art vanishes along with its dormant root.
        for (; current != null && current != root; current = current.parent)
            if (!current.gameObject.activeSelf) return false;
        return current == root;
    }

    internal static List<SpriteRenderer> Collect(GameObject prefab, Func<SpriteRenderer, bool> isEffect) =>
        prefab.GetComponentsInChildren<SpriteRenderer>(true)
            .Where(renderer => renderer != null && renderer.enabled && renderer.sprite != null &&
                IsBranchActive(renderer.transform, prefab.transform) && !isEffect(renderer)).ToList();
}
