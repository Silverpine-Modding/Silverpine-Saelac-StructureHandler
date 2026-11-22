using StructureHandler;

internal static class ReviewedBoundaryChecks
{
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        static bool Rejects(Action action)
        { try { action(); return false; } catch (InvalidDataException) { return true; } }

        test("Native rounded turf owns nudged objects and support on both sides of world seams", () =>
        {
            foreach (var (x, cell, tile) in new[] { (49.49f,49,0), (49.5f,50,1), (49.75f,50,1),
                        (-50.25f,-50,0), (-50.5f,-50,0), (-50.51f,-51,-1) })
            {
                var file = new StructureFile();
                file.objects.Add(new() { x = x, y = x });
                file.supportingTerrain.Add(new() { x = cell, y = cell });
                var parts = StructurePlans.Partition(file);
                check(parts.Count == 1 && parts[(tile,tile)].objects.Single().x == x &&
                    parts[(tile,tile)].supportingTerrain.Count == 1);
            }
        });
        test("Sections from one identifiable import reunite without swallowing later winners", () =>
        {
            var nudged = new StructureObject { x = 49.75f };
            var inside = new StructureObject { x = 12 };
            var floor = new SupportingTerrain { x = 50 };
            var a = new PendingTileImport { structure = new() { name = "Cabin", objects = new() { inside, nudged } } };
            var b = new PendingTileImport { x = 1, structure = new() { name = "Cabin", supportingTerrain = new() { floor } } };
            a.importId = b.importId = "same-import";
            var later = new PendingTileImport { x = 1, structure = new() { name = "Cabin", objects = new() { new() { x = 50 } } } };
            var other = new PendingTileImport { x = 1, structure = new() { name = "Castle", supportingTerrain = new() { floor } } };
            var queue = StructurePlans.RehomePending(new[] { a, b, later, other });
            var east = queue.Where(e => e.x == 1).ToArray();
            check(queue.Single(e => e.x == 0).structure.objects.Single() == inside && east.Length == 3 &&
                east[0].structure.objects.Single() == nudged && east[0].structure.supportingTerrain.Single() == floor &&
                east[1] == later && east[2] == other && StructurePlans.RehomePending(queue).SequenceEqual(queue));
        });
        test("Ambiguous legacy sections survive migration until a validated, non-stale choice", () =>
        {
            var a = new PendingTileImport { structure = new() { name="Cabin", objects=new() { new() { x=49.75f } } } };
            var b = new PendingTileImport { x=1, structure=new() { name="Cabin", supportingTerrain=new() { new() { x=50 } } } };
            var later = new PendingTileImport { x=1, structure=new() { name="Cabin", objects=new() { new() { x=55 } } } };
            foreach (bool merge in new[] { true, false })
            {
                var q = StructurePlans.RehomePending(new[] { a,b,later });
                var p = q[0];
                check(q.Count==2 && p.x==1 && p.boundaryTail!.Single().supportingTerrain.Single().x==50 &&
                    StructurePlans.RehomePending(q).SequenceEqual(q));
                int calls = 0;
                check(Rejects(() => StructurePlans.ResolveBoundary(q,p,merge,_ =>
                    { if (++calls == (merge ? 1 : 2)) throw new InvalidDataException(); })) && q[0]==p && q.Count==2);
                check(StructurePlans.ResolveBoundary(q,p,merge) && !StructurePlans.ResolveBoundary(q,p,merge) &&
                    q.Last()==later && q.Count==(merge?2:3) && q[0].structure.objects.Count==1 &&
                    q[merge?0:1].structure.supportingTerrain.Count==1 && q[0].importId.Length>0);
            }
            a.importId="first"; b.importId="second";
            var separate=StructurePlans.RehomePending(new[] { a,b });
            check(separate.Count==2 && separate.All(p=>p.boundaryTail==null) && separate[1]==b);
        });
        test("Only individual footprints that cross world owners are refused", () =>
        {
            foreach (var (anchor,step) in new[] { (49,1), (-50,-1) })
            {
                var o=new StructureObject { x=anchor, occupiedOffsets=new() {new(),new() { x=step }} };
                check(Rejects(()=>StructurePlans.RequireSingleOwner(o)));
                var f=new StructureFile { objects=new() {o} };
                check(Rejects(()=>StructurePlans.RequireSectionOwner(f,0,0)));
                o.occupiedOffsets.Clear();
                f.supportingTerrain.Add(new() { x=anchor+step });
                check(Rejects(()=>StructurePlans.RequireSectionOwner(f,0,0)));
                var parts=StructurePlans.Partition(f);
                foreach (var part in parts) StructurePlans.RequireSectionOwner(part.Value, part.Key.X,part.Key.Y);
                check(parts.Count==2);
            }
        });
        test("Invalid positions and footprints are refused before rounding, including the outermost occupied cell", () =>
        {
            var o=new StructureObject { prefabName="chair", x=10000000, occupiedOffsets=new() {new(),new() { x=1 }} };
            var f=new StructureFile { objects=new() {o} };
            check(Rejects(()=>StructurePlacementPolicy.ValidateDocumentGeometry(f)) && !StructurePlacementPolicy.FitsAt(o,o.x,o.y));
            o.x=9999999; StructurePlacementPolicy.ValidateDocumentGeometry(f);
            o.z=float.PositiveInfinity; check(Rejects(()=>StructurePlacementPolicy.ValidateDocumentGeometry(f)));
            o.z=0; o.y=float.NaN; check(Rejects(()=>StructurePlacementPolicy.ValidateDocumentGeometry(f)));
            f.objects[0]=null!; check(Rejects(()=>StructurePlacementPolicy.ValidateDocumentGeometry(f)));
            f.objects.Clear(); f.supportingTerrain.Add(new() {prefabName="floor",x=int.MinValue});
            check(Rejects(()=>StructurePlacementPolicy.ValidateDocumentGeometry(f)));
            f.supportingTerrain[0]=null!; check(Rejects(()=>StructurePlacementPolicy.ValidateDocumentGeometry(f)));
        });
        test("Scenery saves a position swap without losing state, preserving unloaded keys", () =>
        {
            var ledger=new StructurePersistenceLedger();
            StructureObject Make(int x,int variant)=>new() { prefabName="urn", x=x, spriteVariantIndex=variant };
            var a=Make(1,5); var b=Make(2,6); var unloaded=Make(99,7);
            ledger.Restore(new[] {a,b,unloaded},null);
            ledger.ReplaceTracked(new[] {StructurePersistenceLedger.Key(a),StructurePersistenceLedger.Key(b)},
                new[] {Make(2,5),Make(1,6)});
            check(ledger.Objects.Count==3 && ledger.Objects[StructurePersistenceLedger.Key(a)].spriteVariantIndex==6 &&
                ledger.Objects[StructurePersistenceLedger.Key(b)].spriteVariantIndex==5 && ledger.Objects.ContainsValue(unloaded));
        });
        test("Cycling furniture appearances between occupied keys never erases an override", () =>
        {
            var data=Enumerable.Range(0,4).Select(i=>new StructureAppearance {prefabName="table",x=i,spriteVariantIndex=i}).ToArray();
            var index=data.ToDictionary(d=>StructurePersistenceLedger.Key(d.prefabName,d.x,d.y,d.z));
            StructurePersistenceLedger.MoveAppearanceRecords(index,new[] {(data[0],1f,0f,0f),(data[1],2f,0f,0f),(data[2],0f,0f,0f)});
            check(index.Count==4 && Enumerable.Range(0,4).Select(i=>index[StructurePersistenceLedger.Key("table",i,0,0)].spriteVariantIndex)
                .SequenceEqual(new[] {2,0,1,3}));
        });
        test("A staged Save As header does not rename or replace metadata on the open file", () =>
        {
            var f=new StructureFile { name="original",serializedObjectsBase64="original bytes" };
            f.occupiedPositions.Add(new() {x=1});
            var draft=f.WithSaveHeader("another",new() {new() {x=2}});
            check(f.name=="original" && f.occupiedPositions.Single().x==1 && f.serializedObjectsBase64=="original bytes" &&
                draft.name=="another" && draft.occupiedPositions.Single().x==2 && draft.serializedObjectsBase64=="");
        });
        test("Explicit footprints must include the anchor; the editor can repair it once without touching other offsets", () =>
        {
            var o = new StructureObject { x = 12, y = -3, occupiedOffsets = new() { new() { x = 1 } } };
            check(Rejects(() => StructurePlacementPolicy.ValidateFootprint(o)) && o.occupiedOffsets.Count == 1);
            check(StructurePlacementPolicy.Cells(o).SequenceEqual(new[] { (12,-3), (13,-3) }));
            check(StructurePlacementPolicy.ValidateFootprint(o, true) && o.occupiedOffsets.Count == 2 &&
                !StructurePlacementPolicy.ValidateFootprint(o, true));
            check(!StructurePlacementPolicy.ValidateFootprint(new StructureObject()) &&
                StructurePlacementPolicy.Cells(new StructureObject()).Single() == (0,0));
        });
        test("Bounded export refuses crossing any of four sides but accepts an exact inclusive fit", () =>
        {
            foreach (var (dx,dy) in new[] { (-1,0), (1,0), (0,1), (0,-1) })
            {
                var o = new StructureObject { x = -11, y = -8, occupiedOffsets = new() { new(), new() { x=dx, y=dy } } };
                check(Rejects(() => StructurePlacementPolicy.RequireInsideExportBounds(new[] { o }, -11,-8,-11,-8)));
                StructurePlacementPolicy.RequireInsideExportBounds(new[] { o }, -12,-7,-10,-9);
            }
        });
        test("A shared Unity scene cannot resurrect unloaded or regenerated scenery across saves", () =>
        {
            var originals = new SuppressedSceneOriginals<int>();
            var committed=originals.Begin();
            committed.Remember("wild", false, 1, () => throw new Exception("Captured transient terrain"));
            committed.Remember("regen", true, 1, () => new() { x=101 });
            committed.Remember("resident", true, 1, () => new() { x=1 });
            committed.Remember("resident", true, 1, () => throw new Exception("Overwrote original"));
            committed.Remember("zone gone", true, 1, () => new() { x=2 });
            committed.Remember("scene gone", true, 2, () => new() { x=3 });
            committed.Commit();
            originals.ClearTile(o => StructurePlans.TileOf(o.x, o.y) == (1,0));
            check(originals.Restorable(s => s == 1, o => o.x != 2).Single().Record.x == 1);
            originals.Clear();
            check(!originals.Restorable(_ => true, _ => true).Any());
        });
        test("A snowy Terrain Construction layer makes a double-grass repair ambiguous", () =>
        {
            const string snowy = "terrainconstruction_snowy_prefab_tile_grass";
            var items = new[] { TerrainRepairPolicy.Grass, TerrainRepairPolicy.Grass, snowy }
                .Where(StructurePlacementPolicy.IsTerrainRecordName).Select((n, i) =>
                    new TerrainRepairPolicy.Item { Id = i+1, Prefab=n, Z=1, Safe=n==TerrainRepairPolicy.Grass });
            var plan = TerrainRepairPolicy.Build(items);
            check(plan.RemoveCount == 0 && plan.Skipped.Single().Reasons.Contains("Other terrain layers share the cell"));
        });
        test("Combine keeps different sprite locks, footprint offsets and component state; exact copies do not multiply", () =>
        {
            var a = new StructureFile();
            a.objects.Add(new() { prefabName="table", components=new() { new() { type="test", dataBase64="AQ==" } } });
            a.objects.Add(a.objects[0]); // An intentional double stack in one source.
            a.supportingTerrain.Add(new() { prefabName="floor", spriteVariantIndex=2 });
            var b = EditorDocumentCopy.Clone(a);
            b.objects[0].lockSpriteVariant=true;
            b.objects[1].occupiedOffsets.Add(new() { x=1 });
            b.supportingTerrain[0].lockSpriteVariant=true;
            var c = EditorDocumentCopy.Clone(a);
            c.objects[0].components[0].dataBase64="Ag==";
            var files = new[] { a, b, c, EditorDocumentCopy.Clone(a) };
            var objects = EditorRecordMerge.Objects(files);
            check(objects.Count==5 && objects.Count(o => o.lockSpriteVariant)==1 &&
                objects.Count(o => o.occupiedOffsets.Count==1)==1 && objects.Count(o => o.components[0].dataBase64=="Ag==")==1);
            check(EditorRecordMerge.Terrain(files).Count==2);
        });
    }
}
