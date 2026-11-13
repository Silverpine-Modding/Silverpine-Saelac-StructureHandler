#nullable enable

using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace StructureHandler;

internal static class StructureFileNormalizer
{
    internal static int MoveTileObjectsToTerrain(StructureFile file)
    {
        file.objects ??= new List<StructureObject>();
        file.supportingTerrain ??= new List<SupportingTerrain>();
        file.objects.RemoveAll(item => item == null);
        file.supportingTerrain.RemoveAll(item => item == null);
        List<StructureObject> tileObjects = file.objects
            .Where(item => !string.IsNullOrWhiteSpace(item.prefabName) &&
                           item.prefabName.StartsWith(
                "prefab_tile_", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (StructureObject item in tileObjects)
        {
            if (!StructureAlgorithms.IsFinite(item.x) || !StructureAlgorithms.IsFinite(item.y) ||
                Math.Abs(item.x) > 10000000 || Math.Abs(item.y) > 10000000)
                throw new InvalidDataException($"Invalid terrain coordinates for {item.prefabName}.");
            int x = Mathf.RoundToInt(item.x);
            int y = Mathf.RoundToInt(item.y);
            // Preserve overlaps for the editor's conflict check. Identical
            // prefab names can still have different zones or saved state.
            file.supportingTerrain.Add(new SupportingTerrain
            {
                prefabName = item.prefabName,
                x = x,
                y = y,
                z = item.z,
                spriteVariantIndex = item.spriteVariantIndex,
                lockSpriteVariant = item.lockSpriteVariant,
                hasMapZoneName = item.hasMapZoneName,
                mapZoneName = item.mapZoneName,
                components = (item.components ?? new List<StructureComponent>())
                    .Select(component => new StructureComponent
                    {
                        type = component.type,
                        hierarchyPath = component.hierarchyPath,
                        componentIndex = component.componentIndex,
                        dataBase64 = component.dataBase64
                    }).ToList()
            });
        }
        var tileSet = new HashSet<StructureObject>(tileObjects);
        file.objects.RemoveAll(tileSet.Contains);
        if (tileObjects.Count > 0 && file.formatVersion >= 3)
            file.serializedObjectsBase64 = "";
        return tileObjects.Count;
    }
}
