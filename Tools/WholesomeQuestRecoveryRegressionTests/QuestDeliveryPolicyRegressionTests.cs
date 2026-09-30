using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;

// The source supplies a contract, not an inventory receipt. Real scheduler
// admission and completion must still use current observations at each stage.
internal static class QuestDeliveryPolicyRegressionTests
{
    private sealed class Failure(string message):Exception(message) { }
    private static readonly JsonSerializerOptions Json=new() { Converters={new JsonStringEnumConverter()} };
    [ModuleInitializer]
    internal static void Run()
    {
        var cases=new List<(string,Action)>();
        void Pickup(string name,long? carried,int supplied,bool expected,int required=3,bool authority=true,bool completeLog=true,int capacity=25)
            =>cases.Add((name,()=>Check(Plan(carried,supplied,required:required,authority:authority,completeLog:completeLog,capacity:capacity).Plan.Any(p=>p.Stage==QuestWorkStage.Pickup)==expected,"pickup disagreed")));
        Pickup("source-provided delivery admits before receipt",0,3,true);
        Pickup("delivery with no acquisition route and no stock defers",0,0,false);
        Pickup("unknown carried stock does not become a delivery route",null,0,false);
        Pickup("partial stock defers",2,0,false);
        Pickup("exact carried stock admits",3,0,true);
        Pickup("excess carried stock admits",4,0,true);
        Pickup("partial source supply still needs remaining stock",0,2,false);
        Pickup("carried stock plus declared acceptance supply admits",1,2,true);
        Pickup("negative inventory observation is not stock",-1,0,false);
        Pickup("large carried count avoids overflow",long.MaxValue,0,true);
        Pickup("unknown history still defers supplied delivery",0,3,false,authority:false);
        Pickup("incomplete raw log still defers supplied delivery",0,3,false,completeLog:false);
        Pickup("full log still defers supplied delivery",0,3,false,capacity:0);
        foreach(var scenario in new[] { ("server-ready without delivery stock is not actionable",0L,true,false),
            ("server-ready with partial stock is not actionable",2L,true,false),
            ("stock alone never manufactures server readiness",3L,false,false),
            ("server-ready plus current stock permits turn-in",3L,true,true) })
        {
            var row=scenario;
            cases.Add((row.Item1,()=>Check(Plan(row.Item2,3,accepted:true,complete:row.Item3).Plan.Any(p=>p.Stage==QuestWorkStage.TurnIn)==row.Item4,"turn-in ignored independent receipt/flag requirements")));
        }
        cases.Add(("accepted delivery with missing stock does not fabricate kill/use work",()=>
            Check(Plan(0,3,accepted:true).Plan.All(p=>p.Stage!=QuestWorkStage.Objective),"delivery became an ordinary objective action")));
        cases.Add(("accepted delivery does not quarantine a missing geometry contract",()=>
        {
            var errors=new List<QuestAttemptOutcome>();Plan(0,3,accepted:true,errors:errors);
            Check(errors.Count==0,"ordinary delivery emitted invalid-data recovery failure");
        }));
        cases.Add(("failed delivery cannot turn in despite stock",()=>Check(Plan(3,3,accepted:true,complete:true,failed:true).Plan.Count==0,"failed delivery executed")));
        cases.Add(("rewarded delivery does not restart",()=>Check(Plan(3,3,rewarded:true).Plan.Count==0,"rewarded delivery restarted")));
        cases.Add(("quarantine retains authority over delivery",()=>Check(Plan(3,3,blocked:true).Plan.Count==0,"delivery bypassed recovery")));
        cases.Add(("unsafe ender retains navigation veto",()=>Check(!Plan(3,3,accepted:true,complete:true,safe:false).Plan.Any(p=>p.Stage==QuestWorkStage.TurnIn),"unsafe ender selected")));
        cases.Add(("receipt loss revokes turn-in then fresh receipt resumes",()=>
        {
            Check(Plan(3,3,accepted:true,complete:true).Plan.Count==1,"initial ready delivery absent");
            Check(Plan(0,3,accepted:true,complete:true).Plan.Count==0,"lost item kept turn-in authorization");
            Check(Plan(3,3,accepted:true,complete:true).Plan.Count==1,"fresh receipt failed to recover");
        }));
        foreach(string fault in new[] {"zero-count","negative-id","duplicate-item","unrelated-supply","supply-without-delivery"})
        {
            string value=fault;cases.Add(("invalid contract "+value,()=>Check(Plan(3,3,fault:value).Plan.Count==0,"malformed delivery admitted")));
        }
        int passed=0,assertions=0,unexpected=0;
        foreach(var t in cases)
        {
            try { t.Item2();passed++;Console.WriteLine("PASS delivery policy: "+t.Item1); }
            catch(Failure e) { assertions++;Console.Error.WriteLine("FAIL delivery policy: "+t.Item1+": "+e.Message); }
            catch(Exception e) { unexpected++;Console.Error.WriteLine("ERROR delivery policy: "+t.Item1+": "+e); }
        }
        Console.WriteLine($"Delivery policy scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual scheduler, controlled stock/source contract; no game.");
        if(assertions+unexpected!=0)throw new InvalidOperationException("Delivery policy regression");
    }
    private static QuestScheduleResult Plan(long? carried,int supplied,int required=3,bool accepted=false,bool complete=false,bool failed=false,
        bool authority=true,bool completeLog=true,int capacity=25,bool rewarded=false,bool blocked=false,bool safe=true,string? fault=null,List<QuestAttemptOutcome>? errors=null)
    {
        var node=System.Text.Json.Nodes.JsonNode.Parse("""
        {"Id":992001,"Name":"Delivery fixture","MinLevel":1,"QuestLevel":1,"StartItem":992010,"Objectives":[{"Type":"TurnInOnly","Index":0}],
         "DeliveryItems":[{"ItemId":992010,"Count":3}],"AcceptanceSupplies":[]}
        """)!;
        node["DeliveryItems"]![0]!["Count"]=required;
        if(supplied>0)node["AcceptanceSupplies"]!.AsArray().Add(new System.Text.Json.Nodes.JsonObject { ["ItemId"]=992010,["Count"]=supplied });
        switch(fault)
        {
            case "zero-count":node["DeliveryItems"]![0]!["Count"]=0;break;
            case "negative-id":node["DeliveryItems"]![0]!["ItemId"]=-1;break;
            case "duplicate-item":node["DeliveryItems"]!.AsArray().Add(node["DeliveryItems"]![0]!.DeepClone());break;
            case "unrelated-supply":node["AcceptanceSupplies"]![0]!["ItemId"]=992099;break;
            case "supply-without-delivery":node["DeliveryItems"]=null;break;
        }
        var q=JsonSerializer.Deserialize<QuestEntry>(node.ToJsonString(),Json)!;
        var db=new QuestDatabase {Quests=new(){q},QuestGivers=new(){new QuestGiverEntry{QuestId=q.Id,GiverId=992020}},QuestEnders=new(){new QuestEnderEntry{QuestId=q.Id,EnderId=992020}},
            CreatureSpawns=new(){["992020"]=new(){new SpawnPoint{Map=530,X=10,Y=10,Z=10,IsKnownSafe=safe?null:false}}}};
        return QuestScheduler.MaterializeSchedule(db,new QuestSchedulerSnapshot {
            UtcNow=DateTime.UtcNow,PlayerLevel=60,PlayerRaceId=10,MapId=530,HasAuthoritativeCompletions=authority,HasCompleteQuestLog=completeLog,QuestLogCapacity=25,
            AcceptedQuests=accepted?new[]{new QuestSchedulerAcceptedQuest{QuestId=(uint)q.Id,IsCompleted=complete,IsFailed=failed}}:
                capacity==0?Enumerable.Range(0,25).Select(i=>new QuestSchedulerAcceptedQuest{QuestId=(uint)(995000+i)}).ToArray():Array.Empty<QuestSchedulerAcceptedQuest>(),
            CompletedQuestIds=rewarded?new[]{(uint)q.Id}:Array.Empty<uint>(),CarriedItemCounts=carried.HasValue?new Dictionary<int,long>{[992010]=carried.Value}:null
        },_=>new QuestRecoveryDecision{State=blocked?QuestRecoveryState.Quarantined:QuestRecoveryState.Eligible,MayAttempt=!blocked},20,250,80,
            navigationAssessment:_=>new SpawnNavigationAssessment{IsKnownReachable=true,IsKnownSafe=safe},reportDataFailure:errors==null?null:errors.Add);
    }
    private static void Check(bool value,string message) {if(!value)throw new Failure(message);}
}
