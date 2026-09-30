using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using WholesomeAQ;
using Styx.Logic.Questing;
using Styx.Logic.Questing.Recovery;

internal static class QuestRequiredStockRegressionTests
{
    private const BindingFlags Hidden=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    private sealed class Failure(string message):Exception(message) { }
    [ModuleInitializer]
    internal static void Run()
    {
        var tests=new List<(string Name,Action Body)>();
        void Case(string name,Action<Fixture> body)=>tests.Add((name,()=>{using var f=new Fixture();body(f);}));
        Case("complete carried prerequisites admit an ordinary pickup",f=>Check(Pickup(f.Schedule(Stock())),"complete required stock rejected"));
        Case("unknown stock is never an empty receipt",f=>Check(!Pickup(f.Schedule(null)),"unknown stock admitted pickup"));
        Case("absent required materials block pickup",f=>Check(!Pickup(f.Schedule(new())),"missing stock admitted pickup"));
        foreach(int item in new[]{301,302})
        {
            int id=item;
            Case("partial required material "+id+" blocks pickup",f=>{var s=Stock();s[id]--;Check(!Pickup(f.Schedule(s)),"partial stock admitted");});
            Case("negative observed material "+id+" is unknown",f=>{var s=Stock();s[id]=-1;Check(!Pickup(f.Schedule(s)),"negative count admitted");});
        }
        Case("unrelated item cannot satisfy the contract",f=>Check(!Pickup(f.Schedule(new(){{999,999}})),"foreign stock admitted"));
        Case("large observed stacks do not overflow admission",f=>Check(Pickup(f.Schedule(new(){{301,long.MaxValue},{302,long.MaxValue}})),"large valid counts overflowed"));
        Case("accepted ordinary work is preserved after stock loss",f=>Check(f.Schedule(new(),accepted:true).Plan.Any(p=>p.Stage==QuestWorkStage.Objective),"stock gate stranded an accepted ordinary objective"));
        Case("required stock does not fabricate authoritative readiness",f=>Check(!TurnIn(f.Schedule(Stock(),accepted:true,progress:3)),"held materials fabricated server readiness"));
        Case("ready quest still requires all return materials",f=>Check(!TurnIn(f.Schedule(new(),accepted:true,ready:true,progress:3)),"ready bit replaced material receipt"));
        Case("ready quest with actual materials can turn in",f=>Check(TurnIn(f.Schedule(Stock(),accepted:true,ready:true,progress:3)),"completed ordinary quest lost turn-in"));
        Case("failed accepted quest remains blocked",f=>Check(!f.Schedule(Stock(),accepted:true,ready:true,failed:true).Plan.Any(),"failed quest gained work"));
        Case("required stock cannot bypass a recovery quarantine",f=>Check(!f.Schedule(Stock(),quarantine:true).Plan.Any(),"stock bypassed quarantine"));
        Case("required stock cannot bypass unknown completion history",f=>Check(!Pickup(f.Schedule(Stock(),authority:false)),"stock bypassed history authority"));
        Case("no recipe is inferred for missing materials",f=>
        {
            var db=f.Load();Check(db.Quests.Single().Objectives.Count==1 && db.Quests.Single().Objectives.Single().Type==ObjectiveType.KillMob,
                "stock metadata replaced or invented an acquisition objective");
        });
        Case("supplemental supplied item coexists with carried prerequisites",f=>
        {
            f.Supplemental();Check(Pickup(f.Schedule(Stock())),"source-proven supplied item incorrectly required before pickup");
            Check(!TurnIn(f.Schedule(Stock(),accepted:true,ready:true,progress:3)),"supplied promise became a turn-in receipt");
            var all=Stock();all[991010]=1;Check(TurnIn(f.Schedule(all,accepted:true,ready:true,progress:3)),"ordinary, supplied and stock requirements failed to coexist");
        });
        Case("required stock is repair-bound and changes execution identity",f=>
        {
            string before=f.Inner.Loader().ExecutionFingerprint;f.Load();var loader=f.Inner.Loader();
            Check(before!=loader.ExecutionFingerprint,"new stock contract did not change execution identity");
        });
        Case("repair load preserves original dataset bytes",f=>{var bytes=File.ReadAllBytes(f.Inner.DataPath);f.Load();Check(bytes.SequenceEqual(File.ReadAllBytes(f.Inner.DataPath)),"repair rewrote original dataset");});
        Case("base JSON cannot inject a stock contract",f=>
        {
            f.Inner.SetBase(root=>root["Quests"]![0]!["RequiredStockItems"]=JsonNode.Parse("[{\"ItemId\":301,\"Count\":2}]"));
            var q=f.Inner.Load().Quests.Single();var property=typeof(QuestEntry).GetProperty("RequiredStockItems");
            Check(property?.GetValue(q)==null,"base JSON injected repair-only stock authority");
        });
        Case("publication guard rechecks actual carried inventory",f=>
        {
            var quest=f.Load().Quests.Single();using var live=new QuestInventorySnapshotRegressionTests.Fixture();
            var item=live.Item(23,301,2);live.Item(24,302,1);
            var method=typeof(QuestScheduler).GetMethod("CreateInventoryRequirementGuard",Hidden)!;
            var plan=new[]{new QuestPlanEntry{Quest=quest,Stage=QuestWorkStage.Pickup}};
            Func<QuestInventorySnapshot> capture=()=> (QuestInventorySnapshot)live.Capture();
            var guard=(Func<bool>?)Invoke(method,null,plan,capture);
            Check(guard!=null && guard(),"stock-sensitive pickup has no current-inventory guard");
            live.MainSlot(23,0);Check(!guard!(),"lost required stock stayed published");
            live.MainSlot(23,item.Guid);Check(guard!(),"fresh actual stock did not recover");
        });
        Case("completed turn-in guard rejects changed required stock",f=>
        {
            var quest=f.Load().Quests.Single();using var live=new QuestInventorySnapshotRegressionTests.Fixture();
            var item=live.Item(23,301,2);live.Item(24,302,1);
            var method=typeof(QuestScheduler).GetMethod("CreateInventoryRequirementGuard",Hidden)!;
            Func<QuestInventorySnapshot> capture=()=> (QuestInventorySnapshot)live.Capture();
            var guard=(Func<bool>?)Invoke(method,null,new[]{new QuestPlanEntry{Quest=quest,Stage=QuestWorkStage.TurnIn}},capture);
            Check(guard!=null && guard(),"turn-in guard absent");live.Write32(item.Fields+56,1);
            Check(!guard!(),"partial stock retained turn-in permission");
        });
        foreach(string fault in new[]{"foreign-quest","duplicate-quest","empty-items","null-items","duplicate-item","zero-count","negative-count",
            "bool-count","null-item","empty-source","unknown-field","ordinary-item-overlap","supplied-item-overlap","scripted","pure-delivery"})
        {
            string current=fault;
            Case("reject malformed stock contract: "+current,f=>
            {
                f.Corrupt(current);f.Write();bool rejected=false;
                try{f.Inner.Load();}catch(InvalidDataException){rejected=true;}
                Check(rejected,"invalid stock contract was accepted");
            });
        }
        int passed=0,assertions=0,errors=0;
        foreach(var test in tests)
        {
            try{test.Body();passed++;Console.WriteLine("PASS required stock: "+test.Name);}
            catch(Failure error){assertions++;Console.Error.WriteLine("FAIL required stock: "+test.Name+": "+error.Message);}
            catch(Exception error){errors++;Console.Error.WriteLine("ERROR required stock: "+test.Name+": "+error);}
        }
        Console.WriteLine($"Required stock scenarios: {passed}/{tests.Count}; assertions={assertions}; unexpected={errors}; real loader/scheduler/inventory guard, controlled observations, no game.");
        if(assertions+errors!=0)throw new InvalidOperationException("Required stock regression");
    }
    private static Dictionary<int,long> Stock()=>new(){{301,2},{302,1}};
    private static bool Pickup(QuestScheduleResult result)=>result.Plan.Any(p=>p.Stage==QuestWorkStage.Pickup);
    private static bool TurnIn(QuestScheduleResult result)=>result.Plan.Any(p=>p.Stage==QuestWorkStage.TurnIn);
    private static void Check(bool condition,string message){if(!condition)throw new Failure(message);}
    private static object? Invoke(MethodInfo method,object? target,params object?[] args)
    {try{return method.Invoke(target,args);}catch(TargetInvocationException e)when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}}

    internal sealed class Fixture:IDisposable
    {
        internal readonly QuestDataRepairPackRegressionTests.Fixture Inner=new();
        internal Fixture()
        {
            Inner.SetBase(root=>
            {
                root["Quests"]![0]!["StartItem"]=0;
                root["Quests"]![0]!["Objectives"]=JsonSerializer.SerializeToNode(new[]{new QuestObjective{Type=ObjectiveType.KillMob,MobId=991020,KillCount=3,Index=0}});
            });
            Inner.Pack["QuestMetadata"]![0]!["DeliveryItems"]=null;Inner.Pack["QuestMetadata"]![0]!["AcceptanceSupplies"]=null;
            Inner.Pack["QuestRequiredStock"]=JsonNode.Parse("[{\"QuestId\":991001,\"SourceRef\":\"controlled://quest-required-items/991001\",\"Items\":[{\"ItemId\":301,\"Count\":2},{\"ItemId\":302,\"Count\":1}]}]");
        }
        internal void Write()=>Inner.Write();
        internal QuestDatabase Load()
        {
            Write();QuestDatabase db;
            try{db=Inner.Load();}catch(InvalidDataException e){throw new Failure("supported required-stock contract was not loaded: "+e.Message);}
            db.QuestGivers.Add(new(){QuestId=991001,GiverId=991020});
            db.CreatureSpawns["991030"]=new(){new(){Map=530,X=10,Y=20,Z=37}};
            return db;
        }
        internal QuestScheduleResult Schedule(Dictionary<int,long>? stock,bool accepted=false,bool ready=false,bool failed=false,int progress=0,bool quarantine=false,bool authority=true)
        {
            return QuestScheduler.MaterializeSchedule(Load(),new QuestSchedulerSnapshot
            {
                PlayerGuid=123,PlayerLevel=60,PlayerRaceId=10,PlayerClassId=2,MapId=530,X=10,Y=20,Z=37,UtcNow=DateTime.UtcNow,
                HasCompleteQuestLog=true,HasAuthoritativeCompletions=authority,CompletedQuestIds=Array.Empty<uint>(),CarriedItemCounts=stock,
                AcceptedQuests=accepted?new[]{new QuestSchedulerAcceptedQuest{QuestId=991001,IsCompleted=ready,IsFailed=failed,
                    NormalObjectiveIds=new[]{991020,0,0,0},NormalObjectiveRequiredCounts=new[]{3,0,0,0},ObjectiveCounts=new[]{progress,0,0,0}}}:Array.Empty<QuestSchedulerAcceptedQuest>()
            },_=>new QuestRecoveryDecision{MayAttempt=!quarantine,State=quarantine?QuestRecoveryState.Quarantined:QuestRecoveryState.Eligible},10,250,80,
                navigationAssessment:_=>new SpawnNavigationAssessment{IsKnownSafe=true,IsKnownReachable=true});
        }
        internal void Supplemental()
        {
            Inner.SetBase(root=>root["Quests"]![0]!["StartItem"]=991010);
            Inner.Pack["QuestMetadata"]![0]!["SupplementalSupply"]=JsonNode.Parse("{\"ItemId\":991010,\"RequiredCount\":1,\"ProvidedCount\":1}");
        }
        internal void Corrupt(string fault)
        {
            var rows=Inner.Pack["QuestRequiredStock"]!.AsArray();var row=rows[0]!;
            switch(fault)
            {
                case "foreign-quest":row["QuestId"]=991999;break;
                case "duplicate-quest":rows.Add(row.DeepClone());break;
                case "empty-items":row["Items"]=new JsonArray();break;
                case "null-items":row["Items"]=null;break;
                case "duplicate-item":row["Items"]!.AsArray().Add(row["Items"]![0]!.DeepClone());break;
                case "zero-count":row["Items"]![0]!["Count"]=0;break;
                case "negative-count":row["Items"]![0]!["Count"]=-1;break;
                case "bool-count":row["Items"]![0]!["Count"]=true;break;
                case "null-item":row["Items"]![0]=null;break;
                case "empty-source":row["SourceRef"]="";break;
                case "unknown-field":row["SupplyNow"]=true;break;
                case "ordinary-item-overlap":Inner.SetBase(root=>root["Quests"]![0]!["Objectives"]=JsonSerializer.SerializeToNode(new[]{new QuestObjective{Type=ObjectiveType.CollectItem,MobId=991020,ItemId=301,CollectCount=3,Index=0}}));break;
                case "supplied-item-overlap":Supplemental();row["Items"]![0]!["ItemId"]=991010;break;
                case "scripted":Inner.SetBase(root=>root["Quests"]![0]!["SpecialFlags"]=2);break;
                case "pure-delivery":Inner.SetBase(root=>root["Quests"]![0]!["Objectives"]=JsonSerializer.SerializeToNode(new[]{new QuestObjective{Type=ObjectiveType.TurnInOnly,Index=0}}));break;
            }
        }
        public void Dispose()=>Inner.Dispose();
    }
}
