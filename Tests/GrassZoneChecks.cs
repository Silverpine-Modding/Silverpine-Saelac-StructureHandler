using System.IO.Compression;
using StructureHandler;
using UnityEngine;

internal static class GrassZoneChecks
{
    const string Grass = "prefab_tile_grass";
    internal static void Run(Action<string,Action> test, Action<bool> check)
    {
        test("A fresh grass claim forgets its old room; native restore at its saved coordinate wins", () => {
            GrassZoneLifecycle.Registry.Clear();
            var template = new MapZone(Grass,0,"");
            var reused = new MapZone(Grass+"(Clone)",17,"Garden");
            var registry = GrassZoneLifecycle.Registry;
            registry[17] = reused;
            var pools = new Dictionary<GameObject,Stack<GameObject>> { [template.gameObject] = new(new[]{reused.gameObject}) };
            GrassZoneLifecycle.BeforeClaim(template.gameObject,pools);
            check(reused.zoneName == "" && !registry.ContainsKey(17) && pools[template.gameObject].Count == 1);
            reused.gameObject.cell = 26; // Native Claim moves it; native saved component deserialization follows.
            reused.zoneName = "Bakery";
            check(GrassZoneLifecycle.ShouldStart(reused,registry));
            registry[26] = reused;
            check(!GrassZoneLifecycle.ShouldStart(reused,registry));
            check(GrassZoneLifecycle.ApplyImported(reused,"Orchard") && registry[26].zoneName == "Orchard");
            var replacement = new MapZone(Grass,26,"Keep");
            registry[26] = replacement;
            check(!GrassZoneLifecycle.ShouldUnregister(reused,registry));
            GrassZoneLifecycle.BeforeClaim(template.gameObject,pools);
            check(registry[26] == replacement && registry[26].zoneName == "Keep");
            reused.gameObject.activeSelf = true;
            reused.zoneName = "Live";
            GrassZoneLifecycle.BeforeClaim(template.gameObject,pools);
            check(reused.zoneName == "Live");
        });
        test("Legacy index corroborates both the exact import coordinate and the grass prefab across saved areas", () => {
            var idx = new LegacyGrassIndex(new(){(4,4),(6,4)});
            idx.Observe(4,4,Grass+"(Clone)","Garden");
            idx.Observe(6,4,Grass,"Other");
            idx.Observe(6,4,Grass,"Garden"); // ambiguous source cannot establish Other
            idx.Observe(12,4,Grass,"Other");
            idx.Observe(24,5,Grass,"Garden");
            idx.Observe(24,5,Grass,"Garden"); // ambiguous target
            idx.ReadTile(Blob((103,1,Grass,"Garden"),(104,1,Grass+"_swamp","Garden"),
                (105,1,"prefab_construction_floor","Garden"),(106,1,Grass,"Unrelated"),
                (107,1,Grass,"Outside"),(1,1,Grass,"Garden")), (1,0),default);
            var c = idx.Candidates().Single();
            check(c.x == 103 && c.y == 1 && c.prefab == Grass && c.name == "Garden");
        });
        test("Damaged legacy data never yields a repair plan; retirement excludes 1.2.41 and beyond", () => {
            check(LegacyGrassIndex.Enabled("1.2.39") && LegacyGrassIndex.Enabled("1.2.40") &&
                !LegacyGrassIndex.Enabled("1.2.41") && !LegacyGrassIndex.Enabled("1.3.0"));
            try {
                using var compressed = new MemoryStream();
                using (var s = new BinaryWriter(new BrotliStream(compressed,CompressionLevel.Fastest,true)))
                { s.Write(1); s.Write(Grass); s.Write(1f); s.Write(2f); s.Write(1f); s.Write(99); }
                new LegacyGrassIndex(new()).ReadTile(compressed.ToArray(),(0,0),default);
                throw new Exception("Accepted truncated tile.");
            } catch (EndOfStreamException) {}
        });
    }
    private static byte[] Blob(params (int X,int Y,string Id,string Name)[] rows)
    {
        using var output = new MemoryStream();
        using (var writer = new BinaryWriter(new BrotliStream(output,CompressionLevel.Fastest,true)))
        {
            writer.Write(rows.Length);
            foreach (var r in rows)
            {
                writer.Write(r.Id); writer.Write((float)r.X); writer.Write((float)r.Y); writer.Write(1f);
                using var zone = new MemoryStream();
                new BinaryWriter(zone).Write(r.Name);
                using var parts = new MemoryStream();
                var w = new BinaryWriter(parts);
                w.Write(3); w.Write("OtherComponent"); w.Write(true); w.Write(3); w.Write(new byte[3]);
                w.Write("Skipped"); w.Write(false);
                w.Write("MapZone"); w.Write(true); w.Write((int)zone.Length); w.Write(zone.ToArray());
                writer.Write((int)parts.Length); writer.Write(parts.ToArray());
            }
        }
        return output.ToArray();
    }
}
internal sealed class MapZone
{
    private static readonly Dictionary<Vector2Int,MapZone> registered = new();
    internal string zoneName;
    internal readonly GameObject gameObject;
    internal Transform transform => gameObject.transform;
    internal MapZone(string prefab,int cell,string name) {
        gameObject = new GameObject { FixtureName = prefab, cell = cell };
        gameObject.FixtureComponents.Add(this);
        zoneName = name;
    }
    internal void SetZoneName(string value) {
        if (zoneName != "") registered.Remove(gameObject.cell);
        zoneName = value;
        if (zoneName != "") registered.Add(gameObject.cell,this);
    }
}
namespace UnityEngine
{
    internal readonly record struct Vector2Int(int X,int Y)
    {
        public static implicit operator Vector2Int(int testCell) => new(testCell,0);
    }
    public partial class GameObject
    {
        internal bool TryGetComponent<T>(out T result) where T:class { result = GetComponent<T>()!; return result != null; }
    }
}
