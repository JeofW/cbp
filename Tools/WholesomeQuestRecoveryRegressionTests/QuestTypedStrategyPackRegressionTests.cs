using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;

internal static class QuestTypedStrategyPackRegressionTests
{
    private sealed class Failure(string message):Exception(message) { }
    [ModuleInitializer]
    internal static void Run()
    {
        var cases=new List<(string,Action)>();
        void Case(string name,Action<QuestDataRepairPackRegressionTests.Fixture,JsonObject> body)=>cases.Add((name,()=>
        {
            using var f=new QuestDataRepairPackRegressionTests.Fixture();var pack=Configure(f);body(f,pack);
        }));
        Case("v2 binds explicit normal credit to repaired dataset",(f,p)=>{var l=Load(f,p);Check(l!=null && l.StrategyPack.Recipes.Count==1,"explicit v2 strategy was not loaded");});
        Case("v2 typed progress admits matching pickup",(f,p)=>{var l=Load(f,p);Check(l!=null && Schedule(l,false).Plan.Any(v=>v.Stage==QuestWorkStage.Pickup),"valid typed recipe not admitted");});
        Case("v2 sparse dataset index emits typed credit not raw index",(f,p)=>
        {
            var l=Load(f,p);Check(l!=null,"v2 pack unavailable");var result=Schedule(l!,true);
            Check(result.Plan.Any(v=>v.Stage==QuestWorkStage.Objective),"typed objective was not scheduled");
            string xml=new ProfileBuilder().BuildProfileXml(result.Plan,l!.Database,"Fixture","Fixture",60,null,l.StrategyPack);
            var nodes=XDocument.Parse(xml).Descendants("CustomBehavior").ToArray();Check(nodes.Length==1,"expected one declared behavior");var node=nodes[0];
            Check((string?)node.Attribute("CreditId")=="991020" && (string?)node.Attribute("RequiredCreditCount")=="3","explicit typed count was lost");
            Check((string?)node.Attribute("ObjectiveIndex")=="17" && (string?)node.Attribute("WaitTime")=="10000","dataset ordinal or source cooldown was rewritten");
            Check(!XDocument.Parse(xml).Descendants("Objective").Any(),"cast strategy fell back to ordinary killing");
        });
        Case("v2 completed credit does not execute again",(f,p)=>{var l=Load(f,p);Check(l!=null && Schedule(l!,true,count:3).Plan.Count==0,"fulfilled typed credit rescheduled");});
        Case("v2 event credit still needs whole-quest strategy",(f,p)=>{f.SetBase(n=>n["Quests"]![0]!["SpecialFlags"]=34);f.Write();p["QuestDataSha256"]=f.BaseHash;p["QuestDataRepairsSha256"]=Hash(File.ReadAllBytes(Path.Combine(f.Folder,"quest_data.repairs.json")));var l=Load(f,p);Check(l!=null && !Schedule(l!,false).Plan.Any(),"typed counter claimed unexplored event completion");});
        Case("v2 mismatched recipe count defers scheduler",(f,p)=>{p["Recipes"]![0]!["CreditCount"]=4;var l=Load(f,p);Check(l!=null && !Schedule(l!,false).Plan.Any(),"strategy disagreed with objective count");});
        Case("v2 recipe cannot borrow another typed target",(f,p)=>{p["Recipes"]![0]!["CreditId"]=991099;var l=Load(f,p);Check(l==null || !Schedule(l,false).Plan.Any(),"foreign credit was admitted");});
        Case("v2 fresh accepted failure cannot dispatch",(f,p)=>{var l=Load(f,p);Check(l!=null && !Schedule(l!,true,failed:true).Plan.Any(),"failed cast quest ran");});
        Case("v2 unknown history retains pickup hold",(f,p)=>{var l=Load(f,p);Check(l!=null && !Schedule(l!,false,authority:false).Plan.Any(),"history hold bypassed");});
        foreach(string fault in new[]{"repair-hash","base-hash","zero-credit","negative-credit","zero-count","oversized-count","negative-wait","oversized-wait","unknown-field","duplicate-recipe","missing-repair-binding"})
        {
            string current=fault;Case("reject "+fault,(f,p)=>
            {
                var r=p["Recipes"]![0]!;
                switch(current)
                {
                    case "repair-hash":p["QuestDataRepairsSha256"]=new string('0',64);break;
                    case "base-hash":p["QuestDataSha256"]=new string('0',64);break;
                    case "zero-credit":r["CreditId"]=0;break;
                    case "negative-credit":r["CreditId"]=-1;break;
                    case "zero-count":r["CreditCount"]=0;break;
                    case "oversized-count":r["CreditCount"]=65536;break;
                    case "negative-wait":r["WaitTime"]=-1;break;
                    case "oversized-wait":r["WaitTime"]=60001;break;
                    case "unknown-field":r["LuaScript"]="not executed";break;
                    case "duplicate-recipe":p["Recipes"]!.AsArray().Add(r.DeepClone());break;
                    case "missing-repair-binding":p.Remove("QuestDataRepairsSha256");break;
                }
                Check(Load(f,p)==null,"invalid v2 pack was accepted");
            });
        }
        int passed=0,assertions=0,unexpected=0;
        foreach(var t in cases)
        {
            try {t.Item2();passed++;Console.WriteLine("PASS typed strategy pack: "+t.Item1);}
            catch(Failure e){assertions++;Console.Error.WriteLine("FAIL typed strategy pack: "+t.Item1+": "+e.Message);}
            catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR typed strategy pack: "+t.Item1+": "+e);}
        }
        Console.WriteLine($"Typed strategy pack scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual loader/scheduler/profile; controlled recipe; no game.");
        if(assertions+unexpected!=0)throw new InvalidOperationException("Typed strategy pack regression");
    }
    internal static JsonObject Configure(QuestDataRepairPackRegressionTests.Fixture f)
    {
        f.SetBase(n=>{n["Quests"]![0]!["SpecialFlags"]=32;n["Quests"]![0]!["Objectives"]=JsonNode.Parse("[{\"Type\":\"KillMob\",\"MobId\":991020,\"KillCount\":3,\"Index\":17}]");});
        f.Pack["QuestMetadata"]![0]!["DeliveryItems"]=null;f.Pack["QuestMetadata"]![0]!["AcceptanceSupplies"]=null;
        f.Pack["RelationAdditions"]!.AsArray().Add(JsonNode.Parse("{\"Role\":\"Giver\",\"QuestId\":991001,\"ObjectType\":\"Creature\",\"Entry\":991020,\"Name\":\"Giver\",\"SourceRef\":\"controlled://giver\"}"));
        f.Write();
        var p=JsonNode.Parse("""
        {"Schema":"quest-strategy-pack-335-v2","ClientBuild":12340,"QuestDataSha256":"BASE","QuestDataRepairsSha256":"REPAIR","SourceKind":"trinitycore-3.3.5","SourceRevision":"controlled-fixture-not-real-recipe",
         "Recipes":[{"QuestId":991001,"ObjectiveIndex":17,"Kind":"UseItemOn","SourceRef":"controlled://spellhit-credit-not-production","ItemId":991010,
         "TargetType":"Creature","TargetId":991020,"TargetState":"Alive","Range":5,"RequireLos":true,"MaxAttempts":3,"SuccessEvidence":"ObjectiveProgress","CreditId":991020,"CreditCount":3,"WaitTime":10000}]}
        """)!.AsObject();p["QuestDataSha256"]=f.BaseHash;p["QuestDataRepairsSha256"]=Hash(File.ReadAllBytes(Path.Combine(f.Folder,"quest_data.repairs.json")));return p;
    }
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    internal static DataLoader? Load(QuestDataRepairPackRegressionTests.Fixture f,JsonObject p)
    {
        f.Write();File.WriteAllText(Path.Combine(f.Folder,"quest_strategies.json"),p.ToJsonString());
        var loader=new DataLoader(f.DataPath);try{loader.Load();return loader;}catch(InvalidDataException){return null;}
    }
    internal static QuestScheduleResult Schedule(DataLoader loader,bool accepted,int count=0,bool failed=false,bool authority=true)
    {
        var snapshot=new QuestSchedulerSnapshot{UtcNow=DateTime.UtcNow,PlayerLevel=60,PlayerClassId=2,PlayerRaceId=10,MapId=530,
            HasAuthoritativeCompletions=authority,CarriedItemCounts=new Dictionary<int,long>(),
            AcceptedQuests=accepted?new[]{new QuestSchedulerAcceptedQuest{QuestId=991001,IsFailed=failed,NormalObjectiveIds=new[]{0,0,991020,0},NormalObjectiveRequiredCounts=new[]{0,0,3,0},ObjectiveCounts=new[]{0,0,count,0}}}:Array.Empty<QuestSchedulerAcceptedQuest>()};
        return (QuestScheduleResult)typeof(QuestScheduler).GetMethod("MaterializeScheduleCore",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,
            new object?[]{loader.Database,snapshot,(Func<QuestRecoveryKey,QuestRecoveryDecision>)(_=>new QuestRecoveryDecision{MayAttempt=true,State=QuestRecoveryState.Eligible}),20,250,80,null,null,null,null,
            (Func<SpawnPoint,SpawnNavigationAssessment>)(_=>new SpawnNavigationAssessment{IsKnownSafe=true,IsKnownReachable=true}),null,loader.StrategyPack})!;
    }
    private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
}
