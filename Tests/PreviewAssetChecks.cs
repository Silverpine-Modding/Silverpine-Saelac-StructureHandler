using StructureHandler;
using UnityEngine;

internal static class PreviewAssetChecks
{
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("Preview retries deduplicate names and do not retry every frame", () =>
        {
            var queue = new PreviewRetryQueue();
            for (int i = 0; i < 1000; i++) queue.Fail("prefab_grass", 0);
            queue.Fail("PREFAB_GRASS", 1);
            check(queue.Count == 1 && queue.TakeDue(4.99, 4).Length == 0);
            check(queue.TakeDue(5, 4).SequenceEqual(new[] { "prefab_grass" }));
            queue.Fail("prefab_grass", 5);
            check(queue.TakeDue(5, 4).Length == 0 && queue.TakeDue(14.99, 4).Length == 0);
            check(queue.TakeDue(15, 4).Length == 1 && queue.TakeDue(34.99, 4).Length == 0);
            check(queue.TakeDue(35, 4).Length == 1 && queue.TakeDue(74.99, 4).Length == 0);
            check(queue.TakeDue(75, 4).Length == 1 && queue.TakeDue(134.99, 4).Length == 0);
            check(queue.TakeDue(135, 4).Length == 1 && queue.TakeDue(195, 4).Length == 1);
        });
        test("Preview retry work is bounded and never starves other missing prefabs", () =>
        {
            var queue = new PreviewRetryQueue();
            foreach (string name in Enumerable.Range(0, 20).Select(i => "prefab_" + i)) queue.Fail(name, 0);
            var seen = new HashSet<string>();
            for (int i = 0; i < 5; i++)
            {
                var batch = queue.TakeDue(5 + i, 4);
                check(batch.Length == 4 && batch.All(seen.Add));
            }
            check(seen.Count == 20 && queue.TakeDue(9, 4).Length == 0);
        });
        test("Recovery, file changes and editor close discard old preview retry state", () =>
        {
            var queue = new PreviewRetryQueue();
            queue.Fail("missing", 0); queue.Fail("recovered", 0); queue.Remove("RECOVERED");
            queue.Retain(new[] { "MISSING" });
            check(queue.Count == 1 && queue.TakeDue(5, 4).SequenceEqual(new[] { "missing" }));
            queue.Retain(Array.Empty<string>()); check(queue.Count == 0);
            queue.Fail("fresh", 20); queue.Clear(); check(queue.TakeDue(1000, 4).Length == 0);
        });
        test("Inactive template roots retain their own enabled surface sprite", () =>
        {
            var prefab = new GameObject { activeSelf = false };
            var renderer = prefab.AddRenderer();
            check(PreviewRendererDiscovery.Collect(prefab, _ => false).SequenceEqual(new[] { renderer }));
        });
        test("Inactive template roots retain enabled active child art", () =>
        {
            var prefab = new GameObject { activeSelf = false };
            var child = prefab.AddChild(); var renderer = child.AddRenderer();
            check(PreviewRendererDiscovery.Collect(prefab, _ => false).SequenceEqual(new[] { renderer }));
        });
        test("Discovery excludes disabled renderers, inactive alternate branches and effect sprites", () =>
        {
            var prefab = new GameObject { activeSelf = false };
            var surface = prefab.AddRenderer();
            prefab.AddRenderer().enabled = false;
            prefab.AddRenderer().sprite = null;
            var inactive = prefab.AddChild(); inactive.activeSelf = false;
            inactive.AddChild().AddRenderer();
            var effect = prefab.AddChild().AddRenderer();
            check(PreviewRendererDiscovery.Collect(prefab, r => r == effect).SequenceEqual(new[] { surface }));
            check(!PreviewRendererDiscovery.IsBranchActive(new GameObject().transform, prefab.transform));
        });
        test("Surface lighting names and light-bearing furniture are not effect overlays", () =>
        {
            foreach (string name in new[] { "TerrainLighting", "Sprites/Lit", "Unlit/Transparent", "AmbientTerrain", "MoonlightGrass", "ShadowedStone" })
                check(!PreviewLightingPolicy.IsLightingEffect("Sprite", "grass", name, name));
            check(!PreviewLightingPolicy.IsLightingEffect("prefab_furniture_light", "sprite_lamp", "sprite-diffuse", "Sprites/Diffuse"));
            check(PreviewLightingPolicy.IsLightingEffect("prefab_effect_light", "", "", ""));
        });
    }
}

// Only the external Unity hierarchy API is stubbed. Discovery above is the
// production implementation, including traversal of inactive prefab templates.
namespace UnityEngine
{
    public class Sprite { }
    public class Transform
    {
        public int childCount => gameObject.FixtureChildCount;
        public GameObject gameObject = null!;
        public Transform parent = null!;
    }
    public class SpriteRenderer
    {
        public bool enabled = true;
        public Sprite? sprite = new();
        public Transform transform = null!;
    }
    public partial class GameObject
    {
        public Transform transform;
        private readonly List<SpriteRenderer> renderers = new();
        private readonly List<GameObject> children = new();
        public GameObject() { transform = new Transform { gameObject = this }; }
        public GameObject AddChild()
        {
            var child = new GameObject { activeSelf = true }; child.transform.parent = transform; children.Add(child); return child;
        }
        public SpriteRenderer AddRenderer()
        {
            var renderer = new SpriteRenderer { transform = transform }; renderers.Add(renderer); return renderer;
        }
        public T[] GetComponentsInChildren<T>(bool includeInactive)
        {
            IEnumerable<SpriteRenderer> Visit(GameObject node) => node.renderers.Concat(
                node.children.Where(child => includeInactive || child.activeSelf).SelectMany(Visit));
            return Visit(this).Cast<T>().ToArray();
        }
    }
}
