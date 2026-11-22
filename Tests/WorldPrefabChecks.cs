using StructureHandler;
using UnityEngine;

internal static class WorldPrefabChecks
{
    internal static void Run(Action<string,Action> test, Action<bool> check)
    {
        test("A native-serializable asset must reload as itself; standalone scenery may still use the companion", () =>
        {
            var native=new GameObject { FixtureName="prefab_fixture" };
            native.FixtureComponents.Add(new StructureRailingState());
            var numbered=new GameObject { FixtureName="prefab_fixture (2)" };
            numbered.FixtureComponents.Add(new StructureRailingState());
            var decor=new GameObject { FixtureName="decor" };
            SerializationManager.Templates.Clear();
            check(WorldPrefabPolicy.CannotReconstruct(native)!=null && WorldPrefabPolicy.CannotReconstruct(decor)==null);
            SerializationManager.Templates["prefab_fixture"]=native;
            check(WorldPrefabPolicy.CannotReconstruct(native)==null && WorldPrefabPolicy.CannotReconstruct(numbered)!=null);
            decor.FixtureComponents.Add(new PersistentEntity());
            check(WorldPrefabPolicy.CannotReconstruct(decor)!=null);
        });
        test("A custom load callback cannot commit a renamed, inactive, or wrongly bound instance", () =>
        {
            var source=new GameObject {FixtureName="source"}; var clone=new GameObject {FixtureName="source(Clone)",activeSelf=true};
            SerializationManager.Templates["source"]=source;
            clone.FixtureComponents.Add(new StructureRailingState());
            WorldPrefabPolicy.RequirePlacedInstance(clone,source,_=>throw new Exception("probing wrong registry"));
            bool Refuses() { try { WorldPrefabPolicy.RequirePlacedInstance(clone,source,_=>null); return false; } catch(InvalidDataException) {return true;} }
            clone.FixtureName="renamed"; check(Refuses());
            clone.FixtureName="source"; clone.activeSelf=false; check(Refuses());
            clone.activeSelf=true; clone.FixtureComponents.Clear(); check(Refuses());
            WorldPrefabPolicy.RequirePlacedInstance(clone,source,_=>source);
        });
        test("A plant-like child of a character and a fixture temporarily holding an actor cannot be removed", () =>
        {
            var actor=new GameObject(); var child=actor.AddChild(); child.FixtureName="prefab_plant_grass";
            actor.FixtureComponents.Add(new Enemy());
            check(WorldPrefabPolicy.CharacterOrSystem(child) && WorldPrefabPolicy.ProtectedFromRemoval(child));
            var fixture=new GameObject(); fixture.AddChild().FixtureComponents.Add(new Player());
            check(WorldPrefabPolicy.CannotReconstruct(fixture)!=null && WorldPrefabPolicy.ProtectedFromRemoval(fixture));
            var plain=new GameObject(); plain.AddChild(); check(!WorldPrefabPolicy.ProtectedFromRemoval(plain));
        });
    }
}

// Headless contract for the native registry and Unity hierarchy lookups used by WorldPrefabPolicy.
internal static class SerializationManager
{
    internal static readonly Dictionary<string,GameObject> Templates=new();
    internal static string GetPrefabName(GameObject o)=>o.FixtureName.Split(' ','(')[0];
    internal static GameObject? GetPrefabFromName(string name)=>Templates.GetValueOrDefault(name);
}
internal sealed class EntityMover {}
internal sealed class Enemy {}
internal sealed class Player {}
internal sealed class NeuralNPC {}
internal sealed class PersistentEntity {}
internal interface IPersistentSerializableMonoBehavior {}
namespace UnityEngine
{
    public partial class GameObject
    {
        internal string FixtureName="";
        public string name => FixtureName;
        internal readonly List<object> FixtureComponents=new();
        internal int FixtureChildCount=>children.Count;
        internal T? GetComponent<T>() where T:class => FixtureComponents.OfType<T>().FirstOrDefault();
        internal T? GetComponentInParent<T>(bool includeInactive) where T:class =>
            GetComponent<T>() ?? transform.parent?.gameObject.GetComponentInParent<T>(includeInactive);
        internal T? GetComponentInChildren<T>(bool includeInactive) where T:class =>
            GetComponent<T>() ?? children.Where(c=>includeInactive||c.activeSelf).Select(c=>c.GetComponentInChildren<T>(includeInactive)).FirstOrDefault(c=>c!=null);
    }
}
