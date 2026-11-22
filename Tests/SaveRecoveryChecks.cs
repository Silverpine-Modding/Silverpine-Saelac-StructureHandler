using StructureHandler;

internal static class SaveRecoveryChecks
{
    private sealed class Renderer { internal bool Alive = true; internal string Sprite = "native"; }
    internal static void Run(Action<string,Action> test, Action<bool> check)
    {
        static bool Rejects(Action body)
        {
            try { body(); return false; } catch (InvalidDataException) { return true; }
        }
        test("A late corrupt companion record cannot publish flags, pending edits, scenery or resource markers", () =>
        {
            var active=new PreparedWorldState(new() {protectedTiles=new() {new(){x=3}}});
            var original=active;
            var next=new StructureWorldState {
                protectedTiles=new(){new(){x=8}}, pending=new(){new()},
                supplementalObjects=new(){new(){prefabName="grass"}},
                resources=new(){new(){prefabName="ore"}},
                appearances=new(){new(){prefabName="chair"},null!} };
            check(Rejects(()=>active=new PreparedWorldState(next)) && active==original &&
                active.Protected.SetEquals(new[]{(3,0)}) && active.Pending.Count==0 && active.Ledger.Objects.Count==0 &&
                active.Resources.Count==0 && active.Appearances.Count==0);
            next.appearances.Clear();
            next.buildableTiles.Add(null!);
            check(Rejects(()=>active=new PreparedWorldState(next)) && active==original);
            next.buildableTiles.Clear(); active=new PreparedWorldState(next);
            check(active.Protected.SetEquals(new[]{(8,0)}) && active.Pending.Count==1 &&
                active.Resources.Count==1 && active.Ledger.Objects.Count==1);
        });
        test("Malformed resource markers cannot partially replace the previous generation's markers", () =>
        {
            var old=new RegeneratingResourceMarker {prefabName="herb", x=-0.25f, y=1.5f};
            var active=PreparedWorldState.ReadResources(new[]{old});
            check(active["herb|0|2"]==old);
            foreach(float bad in new[]{float.PositiveInfinity,float.NaN,10000001f})
                check(Rejects(()=>active=PreparedWorldState.ReadResources(new[]{new RegeneratingResourceMarker{prefabName="ore"},
                    new RegeneratingResourceMarker{prefabName="herb", x=bad}})) && active.Values.Single()==old);
        });
        test("A stale appearance callback cannot erase a restored or replacement object's override", () =>
        {
            var old=new StructureAppearance {prefabName="chair",x=1};
            var current=new StructureAppearance {prefabName="chair",x=1,spriteVariantIndex=2};
            var records=new Dictionary<string,StructureAppearance>{{"chair|1|0|0",current}};
            check(!StructurePersistenceLedger.ForgetOwnedAppearance(records,old));
            StructurePersistenceLedger.MoveAppearanceRecords(records,new[]{(old,2f,0f,0f)});
            check(records.Values.Single()==current && current.x==1 && old.x==1);
            StructurePersistenceLedger.MoveAppearanceRecords(records,new[]{(current,2f,0f,0f)});
            check(records["chair|2|0|0"]==current && StructurePersistenceLedger.ForgetOwnedAppearance(records,current));
        });
        test("Temporary sprite release restores current native art without overwriting later changes", () =>
        {
            var paint=new TemporarySpriteOverride<Renderer,string>(r=>r.Alive,r=>r.Sprite,(r,s)=>r.Sprite=s);
            var first=new Renderer(); var second=new Renderer {Sprite="second native"};
            paint.Paint(first,"locked"); paint.Paint(first,"locked"); paint.Release();
            check(first.Sprite=="native");
            paint.Paint(first,"locked"); first.Sprite="winter"; paint.Paint(first,"locked");
            paint.Paint(second,"other"); check(first.Sprite=="winter" && second.Sprite=="other");
            second.Sprite="new native"; paint.Release(); check(second.Sprite=="new native");
            paint.Paint(first,"locked"); first.Alive=false; paint.Release(); check(first.Sprite=="locked");
        });
        test("A rejected import cannot create an original-scenery record for the next save switch", () =>
        {
            var originals=new SuppressedSceneOriginals<int>();
            var existing=originals.Begin(); existing.Remember("existing",true,1,()=>new(){x=1}); existing.Commit();
            var abandoned=originals.Begin();
            abandoned.Remember("fresh",true,1,()=>new(){x=99});
            check(Rejects(()=>ImportTransaction.Execute(()=>throw new Exception("component failure"),()=>{},"Cabin")));
            check(originals.Restorable(_=>true,_=>true).Single().Record.x==1);
            var good=originals.Begin();
            good.Remember("existing",true,1,()=>throw new Exception("overwrote an earlier original"));
            good.Remember("fresh",true,1,()=>new(){x=2}); good.Commit();
            check(originals.Restorable(_=>true,_=>true).Select(x=>x.Record.x).OrderBy(x=>x).SequenceEqual(new[]{1f,2f}));
        });
        test("Deferring a legacy queue decision lasts the visit and stale callbacks cannot defer the next visit", () =>
        {
            var visit=new BoundaryPromptVisit();
            visit.Enter(); int old=visit.Id; visit.Defer(old);
            check(visit.Deferred); // A midnight pass merely reads this state.
            visit.Enter(); check(!visit.Deferred); visit.Defer(old); check(!visit.Deferred);
            visit.Defer(visit.Id); check(visit.Deferred);
        });
        test("Incomplete recovery is distinguishable from an ordinary queued validation or rollback failure", () =>
        {
            try { ImportTransaction.Execute(()=>throw new Exception("commit"),()=>throw new Exception("rollback"),"Cabin"); }
            catch (ImportRecoveryException e) { check(e.InnerExceptions.Count==2 &&
                e.Message.Contains("without saving") && e.InnerExceptions[1].Message=="rollback"); return; }
            throw new Exception("Missing incomplete-recovery signal");
        });
    }
}
