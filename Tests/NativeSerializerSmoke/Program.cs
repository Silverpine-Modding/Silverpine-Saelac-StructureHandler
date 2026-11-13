using StructureHandler;
using System.Reflection;
using System.Runtime.CompilerServices;

void Check(bool value) { if (!value) throw new Exception("Native serialization assertion failed."); }
WaterSettingsChecks.Run(Check);
ItemStorageChecks.Run(Check);
MarketChecks.Run(Check);
var editorFile = EditorPerformanceChecks.FilledFile();
var editorCopy = EditorDocumentCopy.Clone(editorFile);
Check(StringSerializationAPI.Serialize(typeof(StructureFile), editorFile) == StringSerializationAPI.Serialize(typeof(StructureFile), editorCopy));
Check(EditorDocumentCopy.Equal(editorFile, StringSerializationAPI.Deserialize(typeof(StructureFile),
    StringSerializationAPI.Serialize(typeof(StructureFile), editorCopy))));
Console.WriteLine("PASS Optimized editor snapshots match native JSON for every public model field.");
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

var zonedStructure = new StructureFile {
    supportingTerrain = new() {
        new() { prefabName = "prefab_tile_wood", x = -7, y = 12, z = 1, hasMapZoneName = true, mapZoneName = "Bath House" },
        new() { prefabName = "prefab_tile_rock", x = -6, y = 12, z = 1, hasMapZoneName = true, mapZoneName = "" }
    }
};
var zonedReloaded = (StructureFile)StringSerializationAPI.Deserialize(typeof(StructureFile),
    StringSerializationAPI.Serialize(typeof(StructureFile), zonedStructure));
Check(zonedReloaded.supportingTerrain[0].hasMapZoneName && zonedReloaded.supportingTerrain[0].mapZoneName == "Bath House");
Check(zonedReloaded.supportingTerrain[1].hasMapZoneName && zonedReloaded.supportingTerrain[1].mapZoneName == "");
Check(zonedReloaded.supportingTerrain[0].x == -7 && zonedReloaded.supportingTerrain[0].z == 1);
Console.WriteLine("PASS Native FullSerializer preserves painted terrain zones, including explicit blank names.");

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
foreach (bool collisionEnabled in new[] { false, true })
{
    var furniture = new StructureObject { prefabName = "prefab_furniture_table_wood" };
    FurnitureCollision.SetEnabled(furniture, collisionEnabled);
    var importedCollider = NativeState<TurfCollider>();
    importedCollider.abyss = true; // This unrelated prefab flag must stay untouched.
    using (var reader = new BinaryReader(new MemoryStream(Convert.FromBase64String(furniture.components.Single().dataBase64))))
        importedCollider.Deserialize(reader);
    Check(importedCollider.passable == !collisionEnabled && importedCollider.abyss);
    var savedBytes = NativeBytes(importedCollider);
    Check(savedBytes.SequenceEqual(new byte[] { collisionEnabled ? (byte)0 : (byte)1 }));
    var reloadedCollider = NativeState<TurfCollider>();
    using (var reader = new BinaryReader(new MemoryStream(savedBytes))) reloadedCollider.Deserialize(reader);
    Check(reloadedCollider.passable == !collisionEnabled);
}
Console.WriteLine("PASS Furniture collision edits import and survive real native TurfCollider save/load without changing abyss or adding sidecar state.");
var collisionFile = new StructureFile { objects = new() { new() { prefabName = "prefab_furniture_table_wood" } } };
FurnitureCollision.SetEnabled(collisionFile.objects[0], false);
var collisionRoundTrip = (StructureFile)StringSerializationAPI.Deserialize(typeof(StructureFile),
    StringSerializationAPI.Serialize(typeof(StructureFile), collisionFile));
Check(FurnitureCollision.TryReadEnabled(collisionRoundTrip.objects[0].components, false, out var collisionState) && !collisionState);
Check(collisionRoundTrip.objects[0].components.Single().componentIndex == 0);
Console.WriteLine("PASS Game JSON serializer preserves native furniture collision records for export/import.");

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

foreach (NPCName owner in Enum.GetValues<NPCName>())
{
    Check(PlayerBuildableSleepPatch.ResolveSleepOwner(owner, true) == NPCName.None);
    Check(PlayerBuildableSleepPatch.ResolveSleepOwner(owner, false) == owner);
}
Console.WriteLine("PASS Buildable sleep permissions bypass every bed owner only while the tile flag is on.");

var bedOwnerField = typeof(Bed).GetField(nameof(Bed.owner))!;
var freeHoursField = typeof(Bed).GetField(nameof(Bed.remainingFreeForUseHours))!;
var plotCheck = typeof(WorldInfoManager).GetMethod(nameof(WorldInfoManager.IsOnPlayerOwnedPlot))!;
var roofCheck = typeof(TileTags).GetMethod(nameof(TileTags.HasTileTag))!;
var instructions = new List<HarmonyLib.CodeInstruction> {
    new(System.Reflection.Emit.OpCodes.Ldfld, bedOwnerField),
    new(System.Reflection.Emit.OpCodes.Ldfld, bedOwnerField),
    new(System.Reflection.Emit.OpCodes.Ldfld, bedOwnerField),
    new(System.Reflection.Emit.OpCodes.Callvirt, plotCheck),
    new(System.Reflection.Emit.OpCodes.Ldfld, freeHoursField),
    new(System.Reflection.Emit.OpCodes.Call, roofCheck),
    new(System.Reflection.Emit.OpCodes.Ldstr, "You're too cold to sleep.")
};
var generator = new System.Reflection.Emit.DynamicMethod("Labels", typeof(void), Type.EmptyTypes).GetILGenerator();
var branchLabel = generator.DefineLabel();
instructions[0].labels.Add(branchLabel);
instructions[0].blocks.Add(new HarmonyLib.ExceptionBlock(HarmonyLib.ExceptionBlockType.BeginExceptionBlock));
var rewritten = PlayerBuildableSleepPatch.Transpiler(instructions).ToArray();
Check(rewritten.Length == instructions.Count);
Check(rewritten.Take(3).All(i => i.opcode == System.Reflection.Emit.OpCodes.Call &&
    i.operand is MethodInfo m && m.Name == "GetSleepOwner"));
Check(rewritten[3].opcode == System.Reflection.Emit.OpCodes.Call && ((MethodInfo)rewritten[3].operand).Name == "IsSleepingPlot");
Check(rewritten.Skip(4).Select(i => (i.opcode, i.operand)).SequenceEqual(instructions.Skip(4).Select(i => (i.opcode, i.operand))));
Check(rewritten[0].labels.SequenceEqual(instructions[0].labels) && rewritten[0].blocks.SequenceEqual(instructions[0].blocks));
Check(instructions[0].opcode == System.Reflection.Emit.OpCodes.Ldfld && instructions[3].opcode == System.Reflection.Emit.OpCodes.Callvirt);
Console.WriteLine("PASS Sleep transpiler changes only ownership reads, preserves labels, rental/roof/cold instructions and original IL.");

void RejectSleepIL(IEnumerable<HarmonyLib.CodeInstruction> input)
{
    try { _ = PlayerBuildableSleepPatch.Transpiler(input).ToArray(); }
    catch (InvalidOperationException) { return; }
    throw new Exception("Changed native bed IL was not rejected.");
}
RejectSleepIL(instructions.Skip(1));
RejectSleepIL(instructions.Where(i => !Equals(i.operand, plotCheck)));
RejectSleepIL(instructions.Append(new(System.Reflection.Emit.OpCodes.Ldfld, bedOwnerField)));
Console.WriteLine("PASS Sleep transpiler rejects missing or unexpected ownership checks before modifying instructions.");

var testBed = NativeState<Bed>();
testBed.owner = Enum.GetValues<NPCName>().First(n => n != NPCName.None);
testBed.remainingFreeForUseHours = 5;
var bedBefore = NativeBytes(testBed);
_ = PlayerBuildableSleepPatch.ResolveSleepOwner(testBed.owner, true);
Check(bedBefore.SequenceEqual(NativeBytes(testBed)));
Console.WriteLine("PASS Granting buildable sleep permission leaves serialized bed ownership and rental hours unchanged.");

Check(!PlayerBuildablePickupPatch.ResolveOwnershipBlock(false, false));
Check(!PlayerBuildablePickupPatch.ResolveOwnershipBlock(false, true));
Check(PlayerBuildablePickupPatch.ResolveOwnershipBlock(true, false));
Check(!PlayerBuildablePickupPatch.ResolveOwnershipBlock(true, true));
Console.WriteLine("PASS Buildable furniture pickup bypasses ownership only on marked tiles and preserves normal unowned pickup.");

var pickupTargets = PlayerBuildablePickupPatch.TargetMethods().ToArray();
Check(pickupTargets.All(m => m.DeclaringType == typeof(Pickupable)) &&
    pickupTargets.Select(m => m.Name).SequenceEqual(new[] { "SetEnabled", "OnMouseUpAsButton" }));
Console.WriteLine("PASS Furniture pickup patches both selection highlighting and click permission, not serialization or placement.");

var pickupOwnedField = typeof(Pickupable).GetField(nameof(Pickupable.owned))!;
var pickupActiveField = typeof(Pickupable).GetField("active", BindingFlags.NonPublic | BindingFlags.Instance)!;
var pickupInstructions = new List<HarmonyLib.CodeInstruction> {
    new(System.Reflection.Emit.OpCodes.Ldfld, pickupActiveField),
    new(System.Reflection.Emit.OpCodes.Ldc_R4, 1.55f),
    new(System.Reflection.Emit.OpCodes.Ldfld, pickupOwnedField),
    new(System.Reflection.Emit.OpCodes.Ldstr, "You don't own this."),
    new(System.Reflection.Emit.OpCodes.Call, typeof(Pickupable).GetMethod(nameof(Pickupable.GenerateItem))!)
};
pickupInstructions[2].labels.Add(branchLabel);
pickupInstructions[2].blocks.Add(new HarmonyLib.ExceptionBlock(HarmonyLib.ExceptionBlockType.BeginExceptionBlock));
var pickupRewritten = PlayerBuildablePickupPatch.Transpiler(pickupInstructions).ToArray();
Check(pickupRewritten.Length == pickupInstructions.Count);
for (int i = 0; i < pickupInstructions.Count; i++)
{
    if (i == 2)
        Check(pickupRewritten[i].opcode == System.Reflection.Emit.OpCodes.Call &&
            ((MethodInfo)pickupRewritten[i].operand).Name == "IsOwnershipBlocked");
    else Check(pickupRewritten[i].opcode == pickupInstructions[i].opcode && Equals(pickupRewritten[i].operand, pickupInstructions[i].operand));
}
Check(pickupRewritten[2].labels.SequenceEqual(pickupInstructions[2].labels) && pickupRewritten[2].blocks.SequenceEqual(pickupInstructions[2].blocks));
Check(pickupInstructions[2].opcode == System.Reflection.Emit.OpCodes.Ldfld);
Console.WriteLine("PASS Furniture transpiler replaces only the permission read, preserving labels, active/range checks and item generation.");

void RejectPickupIL(IEnumerable<HarmonyLib.CodeInstruction> input)
{
    try { _ = PlayerBuildablePickupPatch.Transpiler(input).ToArray(); }
    catch (InvalidOperationException) { return; }
    throw new Exception("Changed native furniture IL was not rejected.");
}
RejectPickupIL(pickupInstructions.Where(i => !Equals(i.operand, pickupOwnedField)));
RejectPickupIL(pickupInstructions.Append(new(System.Reflection.Emit.OpCodes.Ldfld, pickupOwnedField)));
Console.WriteLine("PASS Furniture transpiler rejects missing or duplicate ownership checks before changing IL.");

var testFurniture = NativeState<Pickupable>();
testFurniture.owned = true;
var furnitureBefore = NativeBytes(testFurniture);
Check(!PlayerBuildablePickupPatch.ResolveOwnershipBlock(testFurniture.owned, true));
Check(furnitureBefore.SequenceEqual(NativeBytes(testFurniture)));
var reloadedFurniture = NativeState<Pickupable>();
using (var reader = new BinaryReader(new MemoryStream(furnitureBefore))) reloadedFurniture.Deserialize(reader);
Check(reloadedFurniture.owned && PlayerBuildablePickupPatch.ResolveOwnershipBlock(reloadedFurniture.owned, false));
Console.WriteLine("PASS Buildable pickup leaves native ownership intact through save/load and restores restrictions when disabled.");

StructureItemQuality.InstallTypeResolver();
foreach (Quality rarity in Enum.GetValues(typeof(Quality)))
{
    var quality = new StructureItemQuality { Quality = rarity };
    var restored = new StructureItemQuality();
    using (var reader = new BinaryReader(new MemoryStream(StructureItemData.Bytes(quality)))) restored.Deserialize(reader);
    Check(restored.Quality == rarity && restored.GetMultiplier() == (int)rarity + 1);
}
Console.WriteLine("PASS Custom-item quality serializes every native level, Normal through Legendary.");

var placedItem = new Item { name = "A Handmade Blade", description = "A fine blade.", value = 73, bulk = 2f };
placedItem.AddItemComponent(new StructureItemQuality { Quality = Quality.Legendary });
placedItem.AddItemComponent(new ItemComponent_Durability(42));
placedItem.AddItemComponent(new ItemComponent_Enchantment(ItemComponent_Enchantment.EnchantmentType.Knowledge));
var itemRecord = StructureItemData.Create(placedItem, 1.125f, -3.75f, 315);
Check(itemRecord.z == 1 && itemRecord.x == 1.125f && itemRecord.y == -3.75f);
Check(itemRecord.components.Count == 2 && itemRecord.components[0].type.StartsWith("WorldInventory,"));
var worldState = StructureItemData.Storage(itemRecord.components.Single(c => c.type.StartsWith("WorldItem,")).dataBase64);
Check(worldState.GetInt("rotation", -1) == 315 && !worldState.GetBool("owned", true));
var inventoryState = StructureItemData.Storage(itemRecord.components[0].dataBase64);
Check(inventoryState.GetBool("initialized", false) && inventoryState.GetBool("dirty", false));
Check(inventoryState.GetString("summary", "") == "A Handmade Blade");
Console.WriteLine("PASS Editor items create native WorldItem/WorldInventory records with fractional placement, ownership and rotation.");

// Type.GetType in Assembly-CSharp cannot find plugin FullNames on CoreCLR;
// a separate Unity-Mono test exercises full item deserialization/type resolution.
var itemJson = StringSerializationAPI.Serialize(typeof(StructureFile), new StructureFile { objects = new() { itemRecord } });
var itemJsonReloaded = (StructureFile)StringSerializationAPI.Deserialize(typeof(StructureFile), itemJson);
Check(itemJsonReloaded.objects.Single().components.Select(c => c.dataBase64).SequenceEqual(itemRecord.components.Select(c => c.dataBase64)));
Check(itemJsonReloaded.objects[0].x == itemRecord.x && itemJsonReloaded.objects[0].z == 1);
StructureItemData.Update(itemRecord, "WorldItem", fields => { fields["owned"] = true; fields["futureField"] = "kept"; });
StructureItemData.Write(itemRecord, placedItem, 405);
worldState = StructureItemData.Storage(itemRecord.components.Single(c => c.type.StartsWith("WorldItem,")).dataBase64);
Check(worldState.GetBool("owned", false) && worldState.GetInt("rotation", -1) == 45 && worldState.GetString("futureField", "") == "kept");
Console.WriteLine("PASS JSON/duplicate/queued-item payloads preserve native bytes; edits retain ownership and unknown native fields.");

var readItem = StructureItemData.Read(itemRecord, out int readRotation);
Check(readRotation == 45 && readItem.GetItemComponent<StructureItemQuality>().Quality == Quality.Legendary);
Check(readItem.GetItemComponent<ItemComponent_Durability>().Durability == 42 && readItem.value == 73);
Console.WriteLine("PASS Strict editor decoder reads native component payloads without invoking item-use or inventory callbacks.");
string preservedInventory = itemRecord.components[0].dataBase64;
itemRecord.components.Single(c => c.type.StartsWith("WorldItem,")).dataBase64 = "not base64!";
try { StructureItemData.Write(itemRecord, new Item { name = "Replacement" }, 0); throw new Exception("Expected malformed state to fail."); }
catch (FormatException) { }
Check(itemRecord.components[0].dataBase64 == preservedInventory);
Console.WriteLine("PASS Invalid WorldItem metadata cannot partially overwrite the inventory component.");
