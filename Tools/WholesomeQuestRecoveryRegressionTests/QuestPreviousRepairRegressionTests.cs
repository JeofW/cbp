using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Bots.Quest.QuestOrder;
using Styx.Logic.Pathing;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;

internal static class QuestPreviousRepairRegressionTests
{
    private sealed class Failure(string reason) : Exception(reason) { }
    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, Action Test)>();
        void Case(string name, Action<QuestDataRepairPackRegressionTests.Fixture> action) => cases.Add((name, () =>
            { using var f = new QuestDataRepairPackRegressionTests.Fixture(); Configure(f); action(f); }));
        Case("empty dependent requirements do not admit an unrewarded child", f => Check(!Pickup(Load(f), Array.Empty<uint>()), "source alternatives were ignored"));
        Case("first rewarded alternative admits", f => Check(Pickup(Load(f), new uint[]{991099}), "first ordinary alternative blocked"));
        Case("second rewarded alternative admits", f => Check(Pickup(Load(f), new uint[]{991098}), "second ordinary alternative blocked"));
        Case("unknown history cannot satisfy an alternative", f => Check(!Pickup(Load(f), new uint[]{991099}, false), "unknown history authorized a child"));
        Case("direct reward stays independent of alternative membership", f =>
        {
            f.SetBase(n => n["Quests"]![0]!["PrevQuestID"] = 991077); Repair(f)["ExpectedPrevQuestId"] = 991077;
            var db = Load(f); Check(!Pickup(db,new uint[]{991099}),"dependent alternative bypassed direct previous");
            Check(Pickup(db,new uint[]{991077,991099}),"both independent gates failed positive control");
        });
        Case("external predecessors never create schedulable quests", f => { var db=Load(f); Check(db.Quests.Count==1 && db.Quests[0].PreviousQuestsIds.SequenceEqual(new[]{991098,991099}),"source membership or schedulable boundary wrong"); });
        Case("every added alternative protects its active descendant", f =>
        {
            Load(f);
            foreach(uint id in new uint[]{991098,991099}) Check(QuestPrerequisiteAuthority.DetermineFromPublishedDependencies(id,new uint[]{991001})==QuestPrerequisiteStatus.Active,"new predecessor lacks active dependency protection");
        });
        Case("repair changes execution identity and leaves raw dataset unchanged", f =>
        {
            string original=File.ReadAllText(f.DataPath); var row=Repair(f).DeepClone(); f.Pack.Remove("DependentPreviousQuestRepairs"); f.Write(); string old=f.Loader().ExecutionFingerprint;
            f.Pack["DependentPreviousQuestRepairs"]=new JsonArray(row); f.Write(); Check(old!=f.Loader().ExecutionFingerprint,"dependency change retained execution fingerprint");
            Check(File.ReadAllText(f.DataPath)==original,"repair overwrote original dataset");
        });
        Case("accepted turn-in remains independent of pickup predecessors", f =>
        {
            var db=Load(f); db.CreatureSpawns["991030"]=new(){new(){Map=530,X=10,Y=20,Z=37}};
            Check(Schedule(db,Array.Empty<uint>(),accepted:true,ready:true,items:new(){[991010]=1}).Plan.Any(p=>p.Stage==QuestWorkStage.TurnIn),"pickup dependency blocked accepted complete quest");
        });
        Case("level navigation recovery and full log still gate pickup", f =>
        {
            var db=Load(f);
            Check(!Pickup(db,new uint[]{991098},logFull:true),"full log bypassed");
            Check(!Schedule(db,new uint[]{991098},mayAttempt:false).Plan.Any(),"quarantined recovery bypassed");
            Check(!Schedule(db,new uint[]{991098},reachable:false).Plan.Any(),"unreachable endpoint bypassed");
            Check(!Schedule(db,new uint[]{991098},level:0).Plan.Any(),"below minimum level bypassed");
        });
        Case("dependent pickup reaches profile acceptance reward and next scheduling", Pipeline);
        foreach(string fault in new[]{"foreign-subject","duplicate-subject","nonempty-base","expected-nonempty","empty-new","duplicate-new","signed-new","self-reference","unknown-reference","negative-group","group-mismatch","duplicate-reference","missing-reference","unused-reference","direct-mismatch","missing-source","extra-field"})
        {
            string name=fault;
            Case("reject "+name,f=>{ Corrupt(f,name); f.Write(); bool refused=false; try{f.Load();}catch(InvalidDataException){refused=true;} Check(refused,"invalid predecessor repair accepted: "+name); });
        }
        Case("failure preserves original model and allows corrected reload", f=>
        {
            Repair(f)["SourceRef"]=""; f.Write(); var loader=new DataLoader(f.DataPath); try{loader.Load();}catch(InvalidDataException){}
            Repair(f)["SourceRef"]="controlled://dependency-repair";f.Write();Check(loader.Load().Quests.Single().PreviousQuestsIds.Count==2,"failed load cached partial model");
        });
        int passed=0,assertions=0,unexpected=0;
        foreach(var item in cases) {try{item.Test();passed++;Console.WriteLine("PASS predecessor repair: "+item.Name);}catch(Failure e){assertions++;Console.Error.WriteLine("FAIL predecessor repair: "+item.Name+": "+e.Message);}catch(InvalidDataException e){assertions++;Console.Error.WriteLine("FAIL predecessor repair: "+item.Name+": supported contract rejected: "+e.Message);}catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR predecessor repair: "+item.Name+": "+e);}}
        Console.WriteLine($"Predecessor repair scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual loader/scheduler/profile/dependency protection, no game.");
        if(assertions+unexpected!=0)throw new InvalidOperationException("Predecessor repair regression");
    }
    private static JsonObject Repair(QuestDataRepairPackRegressionTests.Fixture f)=>f.Pack["DependentPreviousQuestRepairs"]![0]!.AsObject();
    private static void Configure(QuestDataRepairPackRegressionTests.Fixture f)
    {
        f.Pack["DependencyMetadata"]!.AsArray().Add(JsonNode.Parse("{\"QuestId\":991098,\"ExclusiveGroup\":0,\"GroupMembers\":[],\"SourceRef\":\"controlled://parent-two\"}"));
        f.Pack["RelationAdditions"]!.AsArray().Add(JsonNode.Parse("{\"Role\":\"Giver\",\"QuestId\":991001,\"ObjectType\":\"Creature\",\"Entry\":991020,\"Name\":\"Controlled giver\",\"SourceRef\":\"controlled://giver\"}"));
        f.Pack["DependentPreviousQuestRepairs"]=JsonNode.Parse("""
        [{"QuestId":991001,"ExpectedPrevQuestId":0,"ExpectedPreviousQuestIds":[],"PreviousQuestIds":[991098,991099],"ReferencedQuestGroups":[{"QuestId":991098,"ExclusiveGroup":0,"SourceRef":"controlled://parent-two"},{"QuestId":991099,"ExclusiveGroup":0,"SourceRef":"controlled://parent-one"}],"SourceRef":"controlled://dependency-repair"}]
        """);
    }
    private static QuestDatabase Load(QuestDataRepairPackRegressionTests.Fixture f)
    { f.Write();try{return f.Load();}catch(InvalidDataException e){throw new Failure("complete supported predecessor repair was not loaded: "+e.Message);} }
    private static QuestScheduleResult Schedule(QuestDatabase db,uint[] history,bool authority=true,bool accepted=false,bool ready=false,Dictionary<int,long>? items=null,bool mayAttempt=true,bool reachable=true,bool logFull=false,int level=60)
    {
        var active=accepted?new List<QuestSchedulerAcceptedQuest>{new(){QuestId=991001,IsCompleted=ready}}:new();
        if(logFull)for(uint i=0;active.Count<25;i++)active.Add(new(){QuestId=800000+i});
        return QuestScheduler.MaterializeSchedule(db,new(){UtcNow=DateTime.UtcNow,PlayerGuid=123,PlayerLevel=level,PlayerRaceId=10,PlayerClassId=2,MapId=530,X=10,Y=20,Z=37,
            HasAuthoritativeCompletions=authority,CompletedQuestIds=history,AcceptedQuests=active,CarriedItemCounts=items??new()},
            _=>new QuestRecoveryDecision{MayAttempt=mayAttempt,State=mayAttempt?QuestRecoveryState.Eligible:QuestRecoveryState.Quarantined},10,250,80,
            navigationAssessment:_=>new SpawnNavigationAssessment{IsKnownSafe=true,IsKnownReachable=reachable});
    }
    private static bool Pickup(QuestDatabase db,uint[] history,bool authority=true,bool logFull=false)=>Schedule(db,history,authority,logFull:logFull).Plan.Any(p=>p.Stage==QuestWorkStage.Pickup);
    private static void Pipeline(QuestDataRepairPackRegressionTests.Fixture f)
    {
        var db=Load(f);var q=db.Quests.Single();uint id=(uint)q.Id;db.CreatureSpawns["991030"]=new(){new(){Map=530,X=10,Y=20,Z=37}};
        using var live=new QuestDatasetObservationFixture();live.SetQuest(id,q.Name,60,new int[4],new int[4],new[]{991010,0,0,0,0,0},new[]{1,0,0,0,0,0});live.SetAccepted(false);live.SetHistory(new uint[]{991098});
        var plan=Schedule(db,new uint[]{991098});var builder=new ProfileBuilder();string xml=builder.BuildProfileXml(plan.Plan,db,"Fixture","Fixture",60);live.LoadProfile(xml);
        Check(XDocument.Parse(xml).Descendants("PickUp").Any(),"pickup profile missing");
        var pickup=new ForcedQuestPickUp(id,q.Name,991020,"Giver",new WoWPoint(10,20,37),Styx.Logic.Profiles.Quest.QuestObjectType.Npc);
        Check(!pickup.IsDone,"pickup acknowledged without accepted state");live.SetAccepted(true);Check(pickup.IsDone,"accepted observation unacknowledged");
        Check(!Schedule(db,Array.Empty<uint>(),accepted:true,ready:true).Plan.Any(p=>p.Stage==QuestWorkStage.TurnIn),"acceptance supply promise replaced inventory");
        live.SetInventory(new(){[991010]=1});Check(!Schedule(db,Array.Empty<uint>(),accepted:true,items:new(){[991010]=1}).Plan.Any(p=>p.Stage==QuestWorkStage.TurnIn),"inventory fabricated readiness");
        var ending=Schedule(db,Array.Empty<uint>(),accepted:true,ready:true,items:new(){[991010]=1});xml=builder.BuildProfileXml(ending.Plan,db,"Fixture","Fixture",60);live.LoadProfile(xml);
        Check(XDocument.Parse(xml).Descendants("TurnIn").Any(),"completed turn-in missing");live.SetAccepted(true,complete:true);
        var turnin=new ForcedQuestTurnIn(id,q.Name,991030,"Ender",new WoWPoint(10,20,37),Styx.Logic.Profiles.Quest.QuestObjectType.Npc);
        Check(!turnin.IsDone,"turn-in acknowledged before reward");live.SetAccepted(false);live.SetHistory(new uint[]{991098,id});Check(turnin.IsDone,"reward acknowledgement absent");
        Check(!Schedule(db,new uint[]{991098,id}).Plan.Any(p=>p.Quest.Id==q.Id),"rewarded quest rescheduled");
    }
    private static void Corrupt(QuestDataRepairPackRegressionTests.Fixture f,string mode)
    {
        var row=Repair(f);var refs=row["ReferencedQuestGroups"]!.AsArray();
        switch(mode)
        {
            case "foreign-subject":row["QuestId"]=999999;break;
            case "duplicate-subject":f.Pack["DependentPreviousQuestRepairs"]!.AsArray().Add(row.DeepClone());break;
            case "nonempty-base":f.SetBase(n=>n["Quests"]![0]!["PreviousQuestsIds"]=new JsonArray(991099));break;
            case "expected-nonempty":row["ExpectedPreviousQuestIds"]=new JsonArray(991099);break;
            case "empty-new":row["PreviousQuestIds"]=new JsonArray();break;
            case "duplicate-new":row["PreviousQuestIds"]=new JsonArray(991098,991098);break;
            case "signed-new":row["PreviousQuestIds"]=new JsonArray(-991098,991099);break;
            case "self-reference":row["PreviousQuestIds"]=new JsonArray(991001,991099);break;
            case "unknown-reference":f.Pack["DependencyMetadata"]=new JsonArray();break;
            case "negative-group":refs[0]!["ExclusiveGroup"]=-1;break;
            case "group-mismatch":refs[0]!["ExclusiveGroup"]=1;break;
            case "duplicate-reference":refs.Add(refs[0]!.DeepClone());break;
            case "missing-reference":refs.RemoveAt(0);break;
            case "unused-reference":refs.Add(new JsonObject{["QuestId"]=991077,["ExclusiveGroup"]=0,["SourceRef"]="controlled://unused"});break;
            case "direct-mismatch":row["ExpectedPrevQuestId"]=991099;break;
            case "missing-source":row["SourceRef"]="";break;
            case "extra-field":row["Rewarded"]=true;break;
        }
    }
    private static void Check(bool value,string message){if(!value)throw new Failure(message);}
}
