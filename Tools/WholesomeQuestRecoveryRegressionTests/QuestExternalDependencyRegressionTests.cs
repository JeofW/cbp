using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;

internal static class QuestExternalDependencyRegressionTests
{
    private sealed class Failure(string message):Exception(message) { }
    [ModuleInitializer]
    internal static void Run()
    {
        var cases=new List<(string,Action)>();
        void Case(string name,Action<QuestDataRepairPackRegressionTests.Fixture> test)=>cases.Add((name,()=>
            {using var f=new QuestDataRepairPackRegressionTests.Fixture();Configure(f);test(f);}));
        Case("completed external ordinary predecessor admits",f=>Check(Pickup(f,new uint[]{991099}),"bound ordinary predecessor stayed unknown"));
        Case("unrewarded external predecessor remains blocked",f=>Check(!Pickup(f,Array.Empty<uint>()),"metadata became rewarded history"));
        Case("unknown history remains blocked with external metadata",f=>Check(!Pickup(f,new uint[]{991099},authority:false),"catalog bypassed history authority"));
        Case("absent catalog never guesses external group",f=>{f.Pack["DependencyMetadata"]=new JsonArray();Check(!Pickup(f,new uint[]{991099}),"unknown group was assumed ordinary");});
        Case("full catalog is not part of the schedulable quest collection",f=>{f.Write();var db=f.Load();Check(db.Quests.Count==1 && db.Quests[0].Id==991001,"external records became executable quests");});
        Case("direct rewarded requirement remains independent",f=>{f.SetBase(n=>n["Quests"]![0]!["PrevQuestID"]=991077);Check(!Pickup(f,new uint[]{991099}),"dependent alternative bypassed direct previous");});
        Case("direct and dependent requirements both met admit",f=>{f.SetBase(n=>n["Quests"]![0]!["PrevQuestID"]=991077);Check(Pickup(f,new uint[]{991077,991099}),"independent direct requirement lost valid admission");});
        Case("negative external group requires every member",f=>{Negative(f);Check(!Pickup(f,new uint[]{991099}),"missing negative member admitted");});
        Case("complete rewarded external negative group admits",f=>{Negative(f);Check(Pickup(f,new uint[]{991099,991098}),"fully rewarded group stayed unknown");});
        Case("negative group also publishes sibling dependency protection",f=>
        {
            Negative(f);f.Write();f.Load();
            Check(QuestPrerequisiteAuthority.DetermineFromPublishedDependencies(991098,new uint[]{991001})==QuestPrerequisiteStatus.Active,
                "external negative sibling did not protect active dependent");
        });
        Case("ordered negative group cannot fall through to later alternative",f=>
        {
            Negative(f);Add(f,991100,0);f.SetBase(n=>n["Quests"]![0]!["PreviousQuestsIds"]=new JsonArray(991099,991100));
            Check(!Pickup(f,new uint[]{991099,991100}),"incomplete rewarded negative group fell through to a later ordinary predecessor");
        });
        Case("unrewarded negative candidate permits later rewarded ordinary alternative",f=>
        {
            Negative(f);Add(f,991100,0);f.SetBase(n=>n["Quests"]![0]!["PreviousQuestsIds"]=new JsonArray(991099,991100));
            Check(Pickup(f,new uint[]{991100}),"unrewarded negative candidate blocked ordered ordinary alternative");
        });
        Case("external positive exclusive peer rewarded blocks pickup",f=>
        {
            f.SetBase(n=>{n["Quests"]![0]!["ExclusiveGroup"]=99;n["Quests"]![0]!["PreviousQuestsIds"]=new JsonArray();});
            f.Pack["DependencyMetadata"]![0]!["ExclusiveGroup"]=99;
            Check(!Pickup(f,new uint[]{991099}),"external rewarded exclusive peer was ignored");
        });
        Case("external positive exclusive peer accepted blocks pickup",f=>
        {
            f.SetBase(n=>{n["Quests"]![0]!["ExclusiveGroup"]=99;n["Quests"]![0]!["PreviousQuestsIds"]=new JsonArray();});
            f.Pack["DependencyMetadata"]![0]!["ExclusiveGroup"]=99;
            Check(!Pickup(f,Array.Empty<uint>(),acceptedPeer:true),"external accepted exclusive peer was ignored");
        });
        Case("raw JSON cannot self-declare a trusted external catalog",f=>
        {
            f.SetBase(n=>n["DependencyMetadata"]=JsonNode.Parse("{\"991099\":{\"QuestId\":991099,\"ExclusiveGroup\":0}}"));
            f.Pack["DependencyMetadata"]=new JsonArray();Check(!Pickup(f,new uint[]{991099}),"unbound JSON bypassed the source-bound catalog");
        });
        int passed=0,assertions=0,unexpected=0;
        foreach(var t in cases)
        {
            try { t.Item2();passed++;Console.WriteLine("PASS external dependency: "+t.Item1); }
            catch(Failure e){assertions++;Console.Error.WriteLine("FAIL external dependency: "+t.Item1+": "+e.Message);}
            catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR external dependency: "+t.Item1+": "+e);}
        }
        Console.WriteLine($"External dependency scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual loader, scheduler and dependency authority; no game.");
        if(assertions+unexpected!=0)throw new InvalidOperationException("External dependency regression");
    }
    private static void Configure(QuestDataRepairPackRegressionTests.Fixture f)
    {
        f.SetBase(n=>n["Quests"]![0]!["PreviousQuestsIds"]=new JsonArray(991099));
        f.Pack["RelationAdditions"]!.AsArray().Add(JsonNode.Parse("{\"Role\":\"Giver\",\"QuestId\":991001,\"ObjectType\":\"Creature\",\"Entry\":991020,\"Name\":\"Giver\",\"SourceRef\":\"controlled://giver\"}"));
    }
    private static void Add(QuestDataRepairPackRegressionTests.Fixture f,int id,int group)
        =>f.Pack["DependencyMetadata"]!.AsArray().Add(new JsonObject{["QuestId"]=id,["ExclusiveGroup"]=group,["GroupMembers"]=group<0?new JsonArray(991098,991099):new JsonArray(),["SourceRef"]="controlled://dependency/"+id});
    private static void Negative(QuestDataRepairPackRegressionTests.Fixture f)
    {
        f.Pack["DependencyMetadata"]![0]!["ExclusiveGroup"]=-99;f.Pack["DependencyMetadata"]![0]!["GroupMembers"]=new JsonArray(991098,991099);Add(f,991098,-99);
    }
    private static bool Pickup(QuestDataRepairPackRegressionTests.Fixture f,uint[] completed,bool authority=true,bool acceptedPeer=false)
    {
        f.Write();var db=f.Load();
        var result=QuestScheduler.MaterializeSchedule(db,new QuestSchedulerSnapshot{UtcNow=DateTime.UtcNow,PlayerLevel=60,PlayerClassId=2,PlayerRaceId=10,MapId=530,
            HasAuthoritativeCompletions=authority,CompletedQuestIds=completed,CarriedItemCounts=new Dictionary<int,long>(),
            AcceptedQuests=acceptedPeer?new[]{new QuestSchedulerAcceptedQuest{QuestId=991099}}:Array.Empty<QuestSchedulerAcceptedQuest>()},
            _=>new QuestRecoveryDecision{MayAttempt=true,State=QuestRecoveryState.Eligible},20,250,80,
            navigationAssessment:_=>new SpawnNavigationAssessment{IsKnownSafe=true,IsKnownReachable=true});
        Check(result.Plan.All(p=>p.Quest.Id==991001),"metadata-only quest created work");
        return result.Plan.Any(p=>p.Stage==QuestWorkStage.Pickup);
    }
    private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
}
