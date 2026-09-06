using StructureHandler;
using System.Reflection;
using System.Runtime.CompilerServices;

void Check(bool value) { if (!value) throw new Exception("Native serialization assertion failed."); }
var state = new StructureWorldState();
state.protectedTiles.Add(new StructurePosition { x = -2, y = 1 });
state.buildableTiles.Add(new StructurePosition { x = 0, y = 0 });
state.resources.Add(new RegeneratingResourceMarker { prefabName = "prefab_herb", x = -205, y = 12, lastCheckedDay = 8 });
state.pending.Add(new PendingTileImport { x = 1, y = 2 });
state.appearances.Add(new StructureAppearance { prefabName = "prefab_table", extender = true });
state.supplementalObjects.Add(new StructureObject {
    prefabName = "prefab_tile_dirt_dry", x = -204, y = 110, z = 1, spriteVariantIndex = 2, lockSpriteVariant = true,
    components = new() { new StructureComponent { type = "NestedState", hierarchyPath = "0", componentIndex = 0, dataBase64 = "AQID" } }
});
state.clearedScenery.Add(new StructureObject { prefabName = "prefab_old_ground", x = -204, y = 110 });
state.importedCells.Add(new StructurePosition { x = 47, y = 14 });
string json = StringSerializationAPI.Serialize(typeof(StructureWorldState), state);
var loaded = (StructureWorldState)StringSerializationAPI.Deserialize(typeof(StructureWorldState), json);
Check(loaded.protectedTiles.Single().x == -2 && loaded.buildableTiles.Count == 1);
Check(loaded.resources.Single().lastCheckedDay == 8 && loaded.pending.Count == 1 && loaded.appearances.Single().extender);
Check(loaded.supplementalObjects.Single().x == -204 && loaded.supplementalObjects.Single().spriteVariantIndex == 2);
Check(loaded.supplementalObjects.Single().components.Single().dataBase64 == "AQID");
Check(loaded.supplementalObjects.Single().lockSpriteVariant);
Check(loaded.clearedScenery.Single().prefabName == "prefab_old_ground");
Check(loaded.importedCells.Single().x == 47 && loaded.importedCells.Single().y == 14);
Console.WriteLine("PASS Native FullSerializer round-trips all per-save fields, supplemental scenery, and child component state.");

const string legacyJson = "{\"protectedTiles\":[{\"x\":1,\"y\":2}],\"buildableTiles\":[],\"resources\":[],\"pending\":[],\"appearances\":[]}";
var legacy = (StructureWorldState)StringSerializationAPI.Deserialize(typeof(StructureWorldState), legacyJson);
Check(legacy.protectedTiles.Single().y == 2 && legacy.supplementalObjects.Count == 0 && legacy.clearedScenery.Count == 0 && legacy.importedCells.Count == 0);
Console.WriteLine("PASS Native FullSerializer accepts version-1 companion payloads without new fields.");

var blank = (StructureWorldState)StringSerializationAPI.Deserialize(typeof(StructureWorldState), "{}");
Check(blank.supplementalObjects.Count == 0 && blank.protectedTiles.Count == 0 && blank.importedCells.Count == 0);
Console.WriteLine("PASS Native FullSerializer initializes empty state without leaking another save's records.");

// Execute the real game's binary serializers, but never create a Unity scene
// object or call rendering methods. These paths only read/write managed fields.
T NativeState<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
void Field(object value, string name, object fieldValue) => value.GetType()
    .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(value, fieldValue);
object ReadField(object value, string name) => value.GetType()
    .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
byte[] NativeBytes(ISerializableMonoBehavior value)
{
    using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
    value.Serialize(writer); writer.Flush(); return stream.ToArray();
}
var randomSprite = NativeState<RandomSprite>();
Field(randomSprite, "index", 1); Field(randomSprite, "initialized", true);
var randomReloaded = NativeState<RandomSprite>();
using (var reader = new BinaryReader(new MemoryStream(NativeBytes(randomSprite)))) randomReloaded.Deserialize(reader);
Check((int)ReadField(randomReloaded, "index") == 1 && (bool)ReadField(randomReloaded, "initialized"));
Console.WriteLine("PASS Real RandomSprite saves and restores the native sprite index without companion data.");
var seasonal = NativeState<SeasonRandomSprite>(); Field(seasonal, "randomIndex", 527);
var seasonalReloaded = NativeState<SeasonRandomSprite>();
using (var reader = new BinaryReader(new MemoryStream(NativeBytes(seasonal)))) seasonalReloaded.Deserialize(reader);
Check((int)ReadField(seasonalReloaded, "randomIndex") == 527);
Console.WriteLine("PASS Real SeasonRandomSprite saves and restores its native seasonal variation seed.");
var turnable = NativeState<Turnable>(); Field(turnable, "index", 2);
var rotationState = new VersionSafeStorage();
using (var reader = new BinaryReader(new MemoryStream(NativeBytes(turnable)))) rotationState.Deserialize(reader);
Check(rotationState.GetInt("index", -1) == 2);
Console.WriteLine("PASS Real Turnable binary data contains the selected native rotation.");
var ground = NativeState<GrassTileHandler>();
Field(ground, "previousTilePrefabName", "prefab_tile_dirt_dry");
var groundReloaded = NativeState<GrassTileHandler>();
using (var reader = new BinaryReader(new MemoryStream(NativeBytes(ground)))) groundReloaded.Deserialize(reader);
Check((string)ReadField(groundReloaded, "previousTilePrefabName") == "prefab_tile_dirt_dry");
typeof(GrassTileHandler).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(groundReloaded, null);
Check((string)ReadField(groundReloaded, "previousTilePrefabName") == "prefab_tile_dirt_dry");
Console.WriteLine("PASS Real floor handler saves destination ground and Start does not replace it with default grass.");
