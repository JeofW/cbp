using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;

internal static class QuestSupplementalSupplyRegressionTests
{
    private sealed class Failure(string message):Exception(message) { }
    [ModuleInitializer]
    internal static void Run()
    {
        int passed=0,assertions=0,unexpected=0,total=0;
        void Case(string name,Action test)
        {
            total++;try{test();passed++;Console.WriteLine("PASS supplemental supply: "+name);}
            catch(Failure e){assertions++;Console.Error.WriteLine("FAIL supplemental supply: "+name+": "+e.Message);}
            catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR supplemental supply: "+name+": "+e);}
        }
        Case("bound supplemental item contract loads without replacing the kill",()=>
        {
            using var f=new QuestDataRepairPackRegressionTests.Fixture();Configure(f);f.Write();QuestDatabase? db=null;try{db=f.Load();}catch(InvalidDataException){}
            Check(db!=null,"bound supplemental supply was rejected");
            var node=JsonNode.Parse(JsonSerializer.Serialize(db!.Quests[0]))!;
            Check(node["SupplementalSupply"]?["RequiredCount"]?.GetValue<int>()==3 && db.Quests[0].Objectives[0].Type==ObjectiveType.KillMob,"supplemental supply replaced objective ownership");
        });
        foreach(string fault in new[]{"other-item","zero-required","short-supply","already-collection-owned","also-delivery","missing-source"})
        {
            string value=fault;Case("reject "+fault,()=>
            {
                using var f=new QuestDataRepairPackRegressionTests.Fixture();Configure(f);
                var m=f.Pack["QuestMetadata"]![0]!;
                switch(value)
                {
                    case "other-item":m["SupplementalSupply"]!["ItemId"]=991099;break;
                    case "zero-required":m["SupplementalSupply"]!["RequiredCount"]=0;break;
                    case "short-supply":m["SupplementalSupply"]!["ProvidedCount"]=2;break;
                    case "already-collection-owned":f.SetBase(n=>n["Quests"]![0]!["Objectives"]=JsonNode.Parse("[{\"Type\":\"CollectItem\",\"MobId\":991020,\"ItemId\":991010,\"CollectCount\":3,\"Index\":0}]"));break;
                    case "also-delivery":m["DeliveryItems"]=JsonNode.Parse("[{\"ItemId\":991010,\"Count\":3}]");m["AcceptanceSupplies"]=new JsonArray();break;
                    case "missing-source":m["SourceRef"]="";break;
                }
                f.Write();bool refused=false;try{f.Load();}catch(InvalidDataException){refused=true;}Check(refused,"malformed supplemental supply accepted");
            });
        }
        Case("acceptance supply permits pickup but is not a held receipt",()=>Check(Plan(stock:null).Plan.Any(p=>p.Stage==QuestWorkStage.Pickup),"source-proven supply could not admit pickup"));
        Case("normal objective still runs while the source item is held",()=>Check(Plan(stock:3,accepted:true).Plan.Any(p=>p.Stage==QuestWorkStage.Objective),"supplemental supply suppressed real objective"));
        Case("source item alone never invents quest readiness",()=>Check(!Plan(stock:3,accepted:true).Plan.Any(p=>p.Stage==QuestWorkStage.TurnIn),"held tool manufactured completion"));
        Case("server-ready but missing required source item cannot turn in",()=>Check(!Plan(stock:0,accepted:true,complete:true).Plan.Any(p=>p.Stage==QuestWorkStage.TurnIn),"source promise became inventory receipt"));
        Case("partial required source item cannot turn in",()=>Check(!Plan(stock:2,accepted:true,complete:true).Plan.Any(p=>p.Stage==QuestWorkStage.TurnIn),"partial source stock was sufficient"));
        Case("unknown inventory cannot acknowledge required source item",()=>Check(!Plan(stock:null,accepted:true,complete:true).Plan.Any(p=>p.Stage==QuestWorkStage.TurnIn),"unknown inventory acknowledged"));
        Case("current complete stock and server readiness permit turn-in",()=>Check(Plan(stock:3,accepted:true,complete:true).Plan.Any(p=>p.Stage==QuestWorkStage.TurnIn),"valid source item receipt rejected"));
        Case("failed quest retains its lifecycle veto",()=>Check(Plan(stock:3,accepted:true,complete:true,failed:true).Plan.Count==0,"failed quest dispatched"));
        Case("unknown history still defers pickup",()=>Check(Plan(stock:3,authority:false).Plan.Count==0,"supply bypassed history"));
        Case("source item never creates a missing cast strategy",()=>Check(Plan(stock:3,cast:true).Plan.Count==0,"source item inferred a cast recipe"));
        Case("quarantine is not reset by source supply metadata",()=>Check(Plan(stock:3,blocked:true).Plan.Count==0,"supply cleared recovery hold"));
        Case("restart after source item loss revokes turn-in",()=>
        {
            Check(Plan(stock:3,accepted:true,complete:true).Plan.Count==1,"initial ready result missing");
            Check(Plan(stock:0,accepted:true,complete:true).Plan.Count==0,"lost receipt retained after restart");
            Check(Plan(stock:3,accepted:true,complete:true).Plan.Count==1,"fresh receipt did not resume");
        });
        Console.WriteLine($"Supplemental supply scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; actual loader/scheduler; source promise separate from inventory and normal credit; no game.");
        if(assertions+unexpected!=0)throw new InvalidOperationException("Supplemental supply regression");
    }
    private static void Configure(QuestDataRepairPackRegressionTests.Fixture f)
    {
        f.SetBase(n=>n["Quests"]![0]!["Objectives"]=JsonNode.Parse("[{\"Type\":\"KillMob\",\"MobId\":991020,\"KillCount\":3,\"Index\":0}]"));
        var metadata=f.Pack["QuestMetadata"]![0]!;metadata["DeliveryItems"]=null;metadata["AcceptanceSupplies"]=null;
        metadata["SupplementalSupply"]=JsonNode.Parse("{\"ItemId\":991010,\"RequiredCount\":3,\"ProvidedCount\":3}");
    }
    private static QuestScheduleResult Plan(long? stock,bool accepted=false,bool complete=false,bool failed=false,bool authority=true,bool cast=false,bool blocked=false)
    {
        var q=JsonSerializer.Deserialize<QuestEntry>("""
        {"Id":991001,"Name":"Supplemental supply","MinLevel":1,"QuestLevel":1,"StartItem":991010,"SupplementalSupply":{"ItemId":991010,"RequiredCount":3,"ProvidedCount":3},
        "Objectives":[{"Type":"KillMob","MobId":991020,"KillCount":3,"Index":0}]}
        """,new JsonSerializerOptions{Converters={new JsonStringEnumConverter()}})!;q.SpecialFlags=cast?32:0;
        var db=new QuestDatabase{Quests=new(){q},QuestGivers=new(){new QuestGiverEntry{QuestId=q.Id,GiverId=991020}},QuestEnders=new(){new QuestEnderEntry{QuestId=q.Id,EnderId=991020}},
            CreatureSpawns=new(){["991020"]=new(){new SpawnPoint{Map=530,X=10,Y=10,Z=10}}}};
        return QuestScheduler.MaterializeSchedule(db,new QuestSchedulerSnapshot{UtcNow=DateTime.UtcNow,MapId=530,PlayerLevel=60,PlayerRaceId=10,HasAuthoritativeCompletions=authority,
            CarriedItemCounts=stock.HasValue?new Dictionary<int,long>{[991010]=stock.Value}:null,
            AcceptedQuests=accepted?new[]{new QuestSchedulerAcceptedQuest{QuestId=(uint)q.Id,IsCompleted=complete,IsFailed=failed,NormalObjectiveIds=new[]{991020,0,0,0},NormalObjectiveRequiredCounts=new[]{3,0,0,0},ObjectiveCounts=new int[4]}}:Array.Empty<QuestSchedulerAcceptedQuest>()},
            _=>new QuestRecoveryDecision{MayAttempt=!blocked,State=blocked?QuestRecoveryState.Quarantined:QuestRecoveryState.Eligible},20,250,80,
            navigationAssessment:_=>new SpawnNavigationAssessment{IsKnownSafe=true,IsKnownReachable=true});
    }
    private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
}
