#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace StructureHandler;

internal static class NativeAppearance
{
    private static readonly FieldInfo RandomSprites = AccessTools.Field(typeof(RandomSprite), "sprites");
    private static readonly FieldInfo RandomIndex = AccessTools.Field(typeof(RandomSprite), "index");
    private static readonly FieldInfo RandomInitialized = AccessTools.Field(typeof(RandomSprite), "initialized");
    private static readonly FieldInfo SeasonalIndex = AccessTools.Field(typeof(SeasonRandomSprite), "randomIndex");
    private static readonly FieldInfo[] SeasonalSprites = new[] { "springSprites", "summerSprites", "autumnSprites", "winterSprites" }
        .Select(name => AccessTools.Field(typeof(SeasonRandomSprite), name)).ToArray();

    // Returns true only when no sprite override is needed, or the native root
    // component will serialize this choice and restore it on the same prefab.
    internal static bool TryApply(GameObject instance, GameObject? prefab, int sprite, bool locked)
    {
        if (sprite < 0 || instance.GetComponentInChildren<Turnable>(true) != null) return true;
        if (prefab == null || StructureTerrainPersistence.NeedsSupplemental(instance)) return false;
        Sprite[] variants = StructureEditorUI.GetSpriteVariantsForPrefab(prefab.name);
        if (variants.Length == 0) return false;
        Sprite selected = variants[sprite % variants.Length];
        var renderer = instance.GetComponentsInChildren<SpriteRenderer>(true)
            .FirstOrDefault(candidate => candidate.enabled && candidate.sprite != null);
        if (renderer == null) return false;

        var randoms = instance.GetComponents<RandomSprite>();
        int nativeCount = prefab.GetComponents<RandomSprite>().Length;
        for (int i = 0; i < Math.Min(randoms.Length, nativeCount); i++)
        {
            var random = randoms[i];
            if (random.gameObject.GetSpriteRendererExtended() != renderer) continue;
            var sprites = RandomSprites.GetValue(random) as Sprite[] ?? Array.Empty<Sprite>();
            int index = Array.IndexOf(sprites, selected);
            if (index < 0) continue;
            RandomIndex.SetValue(random, index);
            RandomInitialized.SetValue(random, true);
            renderer.sprite = selected;
            return true;
        }

        var seasonals = instance.GetComponents<SeasonRandomSprite>();
        nativeCount = prefab.GetComponents<SeasonRandomSprite>().Length;
        for (int i = 0; i < Math.Min(seasonals.Length, nativeCount); i++)
        {
            var seasonal = seasonals[i];
            if (seasonal.gameObject.GetSpriteRendererExtended() != renderer) continue;
            int index = (int)SeasonalIndex.GetValue(seasonal);
            var seasons = SeasonalSprites.Select(field =>
                (IReadOnlyList<Sprite>)(field.GetValue(seasonal) as List<Sprite> ?? new())).ToArray();
            if (!NativeSavePolicy.IsCapturedSeasonalVariant(index, selected, locked, seasons)) continue;
            // An export's ordinary seasonal snapshot is not an instruction to
            // freeze that season forever. Retain the serialized native seed.
            var world = WorldInfoManager.Instance;
            if (world == null) return false;
            Season season = world.GetSeasonFor(instance);
            int slot = season == Season.Spring ? 0 : season == Season.Autumn ? 2 : season == Season.Winter ? 3 : 1;
            var current = seasons[slot];
            if (current.Count == 0) return false;
            int currentIndex = season == Season.Autumn && world.currentBiome == typeof(MountainTileGenerator)
                ? 0 : index % current.Count;
            renderer.sprite = current[currentIndex];
            return true;
        }
        return false;
    }
}
