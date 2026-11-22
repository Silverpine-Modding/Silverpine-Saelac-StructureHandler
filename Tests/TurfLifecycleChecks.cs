using StructureHandler;
using UnityEngine;

internal static class TurfLifecycleChecks
{
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("1,800 released registrars perform one real removal each, never a second removal at shutdown", () =>
        {
            Turfs.Reset();
            var released = Enumerable.Range(0, 1800).Select(i => new TurfRegistrar(new GameObject { activeSelf = true, cell = i })).ToArray();
            foreach (var r in released) { ObjectPool.Track(r.gameObject); r.Register(); r.gameObject.SetActive(false); r.DestroyCallback(); }
            foreach (var r in released) r.DestroyCallback();
            check(Turfs.Unregisters == 1800 && Turfs.Missing == 0);
        });
        test("A replacement sharing the old cell is untouched; a reclaimed instance cleans up at its new cell", () =>
        {
            Turfs.Reset();
            var pooled = new TurfRegistrar(new GameObject { activeSelf = true, cell = 9 });
            ObjectPool.Track(pooled.gameObject);
            pooled.Register(); pooled.gameObject.SetActive(false); pooled.DestroyCallback();
            var replacement = new TurfRegistrar(new GameObject { activeSelf = true, cell = 9 });
            replacement.Register(); pooled.DestroyCallback();
            pooled.gameObject.cell = 17; pooled.gameObject.SetActive(true);
            check(TurfRegistrationLifecycle.ShouldRunDestroy(pooled, false)); // reclaimed but not yet registered
            TurfRegistrationLifecycle.RegisterIfLive(pooled);
            pooled.gameObject.SetActive(false); pooled.DestroyCallback(); pooled.DestroyCallback();
            check(Turfs.Unregisters == 2 && Turfs.Missing == 0 && Turfs.Has(replacement.gameObject) && !Turfs.Has(pooled.gameObject));
        });
        test("An externally registered occupant is cleaned up; nonpool or still-registered inconsistencies aren't silenced", () =>
        {
            Turfs.Reset();
            var pool = new TurfRegistrar(new GameObject());
            ObjectPool.Track(pool.gameObject);
            Turfs.Register(pool.gameObject); pool.DestroyCallback();
            check(!Turfs.Has(pool.gameObject) && Turfs.Unregisters == 1);
            check(TurfRegistrationLifecycle.ShouldRunDestroy(pool, true));
            var unrelated = new TurfRegistrar(new GameObject());
            unrelated.DestroyCallback();
            check(Turfs.Missing == 1);
            pool.preventRegistration = true; pool.DestroyCallback();
            check(!pool.preventRegistration && Turfs.Unregisters == 2);
        });
        test("Inactive child branches are not registered; active imported registrars are only registered once", () =>
        {
            Turfs.Reset();
            var root = new GameObject { activeSelf = true };
            var child = new TurfRegistrar(root.AddChild());
            ObjectPool.Track(root);
            child.gameObject.SetActive(false);
            TurfRegistrationLifecycle.RegisterIfLive(child);
            check(!Turfs.Has(child.gameObject) && TurfRegistrationLifecycle.ShouldRunDestroy(child, false));
            child.gameObject.SetActive(true);
            TurfRegistrationLifecycle.RegisterIfLive(child); TurfRegistrationLifecycle.RegisterIfLive(child);
            root.SetActive(false); child.DestroyCallback(); child.DestroyCallback();
            check(Turfs.Unregisters == 1 && Turfs.Missing == 0);
        });
    }
}

// Model only the game/Unity dependencies. Both the registration decision and
// the Harmony prefix's destruction decision above are the production code.
internal static class ObjectPool
{
    private static readonly Dictionary<GameObject, object> claimedMappings = new();
    internal static void Track(GameObject item) => claimedMappings.Add(item, item);
    internal static void Clear() => claimedMappings.Clear();
}
internal static class Turfs
{
    private static readonly Dictionary<int, List<GameObject>> entries = new();
    internal static int Unregisters, Missing;
    internal static void Reset() { entries.Clear(); ObjectPool.Clear(); Unregisters = Missing = 0; }
    internal static bool TryGetTurfGameObjects(int cell, out List<GameObject> list) => entries.TryGetValue(cell, out list!);
    internal static bool Has(GameObject item) => TryGetTurfGameObjects(item.cell, out var list) && list.Contains(item);
    internal static void Register(GameObject item)
    {
        if (!entries.TryGetValue(item.cell, out var list)) entries[item.cell] = list = new();
        list.Add(item);
    }
    internal static void Unregister(GameObject item)
    {
        Unregisters++;
        if (!entries.TryGetValue(item.cell, out var list)) { Missing++; return; }
        list.Remove(item);
        if (list.Count == 0) entries.Remove(item.cell);
    }
}
internal sealed class TurfRegistrar(GameObject item)
{
    internal GameObject gameObject => item;
    internal Transform transform => item.transform;
    internal bool preventRegistration;
    private bool registered;
    internal void Register()
    {
        if (registered || preventRegistration) throw new InvalidOperationException("Unexpected Register");
        registered = true; Turfs.Register(item);
    }
    internal void DestroyCallback()
    {
        if (!TurfRegistrationLifecycle.ShouldRunDestroy(this, registered)) return;
        if (!preventRegistration || registered) Turfs.Unregister(item);
        registered = preventRegistration = false;
    }
}
namespace UnityEngine
{
    public partial class GameObject
    {
        internal int cell;
        public bool activeInHierarchy => activeSelf && (transform.parent?.gameObject.activeInHierarchy ?? true);
    }
    internal static class TurfTestPosition
    {
        internal static int GetVector2IntPosition(this Transform t) => t.gameObject.cell;
    }
}
