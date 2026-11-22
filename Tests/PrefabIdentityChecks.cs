using StructureHandler;
using System.Reflection;
using System.Reflection.Emit;

internal static class PrefabIdentityChecks
{
    internal static void Run(Action<string,Action> test, Action<bool> check)
    {
        static bool Invalid(Action a) { try { a(); return false; } catch (InvalidDataException) { return true; } }
        test("Numbered standalone assets retain their exact ID; ordinary scene copies keep the native ID", () =>
        {
            bool Known(string n) => n == "prefab_tile_grass_overgrowth (3)" || n == "custom bench";
            check(PrefabIdentity.FromInstance("prefab_tile_grass_overgrowth (3)(Clone)",Known)=="prefab_tile_grass_overgrowth (3)");
            check(PrefabIdentity.FromInstance("prefab_tile_grass_overgrowth (4)",Known)=="prefab_tile_grass_overgrowth");
            check(PrefabIdentity.FromInstance("custom bench(Clone)",Known)=="custom bench");
            check(PrefabIdentity.FromInstance("prefab_tile_grass(Clone)",_=>throw new Exception("Unneeded asset scan"))=="prefab_tile_grass");
        });
        test("Ambiguous fallback names never select a template by enumeration order and refresh drops stale results", () =>
        {
            var a=new object(); var b=new object(); var index=new PrefabAssetIndex<object>();
            foreach(bool reverse in new[]{false,true}) {
                index.Clear(); index.Add("lamp",reverse?b:a); index.Add("lamp",reverse?b:a);
                check(index.Names.Single()=="lamp" && index.Get("LAMP")== (reverse?b:a));
                index.Add("Lamp",reverse?a:b); check(!index.Names.Any() && index.ContainsName("lamp") && index.Get("lamp")==null);
            }
            index.Clear(); index.Add("lamp",b); check(index.Get("lamp")==b);
        });
        test("A component's explicit mod identity is respected even with two identical CLR type names", () =>
        {
            object Part(string assembly)
            {
                var mod=AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assembly),AssemblyBuilderAccess.Run)
                    .DefineDynamicModule(assembly);
                return Activator.CreateInstance(mod.DefineType("Furniture.SignData",TypeAttributes.Public).CreateType()!)!;
            }
            var a=Part("LampMod"); var b=Part("OtherMod"); object[] both={a,b};
            object Bind(string name, int i=-1)=>SavedComponentBinding.Select(both,new(){type=name,componentIndex=i},"table");
            check(Bind("Furniture.SignData, OtherMod, Version=9.0.0.0")==b);
            check(Bind("Furniture.SignData, LampMod")==a && Invalid(()=>Bind("Furniture.SignData, Missing")));
            check(Invalid(()=>Bind("SignData")) && Invalid(()=>Bind("Furniture.SignData")) && Invalid(()=>Bind("Furniture.SignData, OtherMod",-2)));
            check(SavedComponentBinding.Select(new[]{a},new(){type="SignData"},"legacy")==a);
            check(SavedComponentBinding.Select(new[]{a,a},new(){type="SignData",componentIndex=1},"legacy")==a);
        });
        test("Duplicate component aliases keep the final write order and never repeat stateful deserialization", () =>
        {
            var first=new object(); var second=new object();
            var writes=SavedComponentBinding.FinalWrites(new List<StructureComponent> {
                new(){type="Inventory",dataBase64="old"}, new(){type="Other",dataBase64="first"},
                new(){type="Inventory, Assembly-CSharp",dataBase64="final"} }, r=>r.type=="Other"?second:first);
            check(writes.Count==2 && writes[0].Target==second && writes[1].Target==first &&
                writes[1].Record.dataBase64=="final");
        });
        test("Editing a native setting does not alter another mod's same-named component", () =>
        {
            var foreign=new StructureComponent{type="TurfCollider, AnotherMod",dataBase64="unchanged"};
            var o=new StructureObject{components=new(){foreign}};
            FurnitureCollision.SetEnabled(o,false);
            check(o.components.Count==2 && foreign.dataBase64=="unchanged");
            var f=new StructureFile{objects=new(){new(){components=new(){
                new(){type="GrassTileHandler"},new(){type="GrassTileHandler, AnotherMod"}}}}};
            DestinationGroundPolicy.StripExportedHistory(f);
            check(f.objects[0].components.Single().type=="GrassTileHandler, AnotherMod");
        });
        test("Alias restore rekeys scenery, clearances, furniture and midnight markers without merging distinct records", () =>
        {
            string Alias(string n)=>n=="old"?"new":n;
            var s=new StructureWorldState{supplementalObjects=new(){new(){prefabName="old"}},
                clearedScenery=new(){new(){prefabName="old"}},resources=new(){new(){prefabName="old"}},
                appearances=new(){new(){prefabName="old",extender=true}}};
            var ready=new PreparedWorldState(s,Alias);
            check(ready.Resources["new|0|0"].prefabName=="new" && ready.Appearances["new|0|0|0"].extender &&
                ready.Ledger.Objects.ContainsKey("new|0|0|0") && ready.Ledger.Cleared.ContainsKey("new|0|0|0"));
            s.appearances.Add(new(){prefabName="old"});
            check(Invalid(()=>new PreparedWorldState(s,Alias)));
        });
        test("Inactive custom placement runs rollback rather than committing an empty tile", () =>
        {
            var source=new UnityEngine.GameObject(); var clone=new UnityEngine.GameObject();
            Silverpine.ModdingTools.SerializablePrefabs.Registrations["inactive"]=new(source,new(){ActivateLoadedInstances=false});
            bool restored=false;
            check(Invalid(()=>ImportTransaction.Execute(()=>ImportedPrefabLifecycle.PrepareForPlacement(clone,"inactive"),
                ()=>restored=true,"fixture")) && restored && !clone.activeSelf && !source.activeSelf);
        });
    }
}
