using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Bots.Quest.QuestOrder;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles.Quest;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;
using DataType = WholesomeAQ.ObjectiveType;

// Real source-bound loader, generated profile and item acknowledgement owners.
// Fixtures never attach to the game or manufacture production observations.
internal static class QuestCollectionRouteRegressionTests
{
    private const int QuestId = 991001, ItemId = 991010, OldTarget = 991021, NewTarget = 991040;
    private sealed class Failure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, Action Body)>();
        void Case(string name, Action<QuestDataRepairPackRegressionTests.Fixture> action) =>
            cases.Add((name, () => { using var f = new QuestDataRepairPackRegressionTests.Fixture(); Configure(f); action(f); }));
        Case("unproved creature collector gains the bound ordinary target", f =>
        {
            var objective = Load(f).Quests.Single().Objectives.Single();
            Check(objective.Type == DataType.CollectItem && objective.MobId == NewTarget && objective.GameObjectId == 0 &&
                  objective.ItemId == ItemId && objective.CollectCount == 2 && objective.Index == 0, "collector identity was not repaired");
        });
        Case("wrong gameobject collector becomes the exact item creature route", f =>
        {
            f.SetBase(data => { var objective=data["Quests"]![0]!["Objectives"]![0]!; objective["Type"]=2; objective["MobId"]=0; objective["GameObjectId"]=OldTarget; objective["GameObjectName"]="Old object"; });
            Repair(f)["ExpectedObjectiveType"]="CollectFromGameObject";
            var objective=Load(f).Quests.Single().Objectives.Single();
            Check(objective.Type==DataType.CollectItem && objective.MobId==NewTarget && objective.GameObjectId==0 && string.IsNullOrEmpty(objective.GameObjectName),"old object namespace leaked into creature route");
        });
        Case("append retains the original source and a distinct index", f =>
        {
            Repair(f)["Operation"]="Append"; Repair(f)["NewObjectiveIndex"]=1;
            var objectives=Load(f).Quests.Single().Objectives;
            Check(objectives.Count==2 && objectives[0].MobId==OldTarget && objectives[0].Index==0 &&
                  objectives[1].MobId==NewTarget && objectives[1].Index==1 && objectives.All(o=>o.ItemId==ItemId && o.CollectCount==2),"append changed original ownership");
        });
        Case("unrelated kill and auxiliary item objectives are preserved", f =>
        {
            f.SetBase(data => { var values=data["Quests"]![0]!["Objectives"]!.AsArray();
                values.Add(JsonNode.Parse("{\"Type\":0,\"MobId\":991050,\"ItemId\":0,\"KillCount\":3,\"CollectCount\":0,\"Index\":1}"));
                values.Add(JsonNode.Parse("{\"Type\":1,\"MobId\":991060,\"ItemId\":991011,\"KillCount\":0,\"CollectCount\":1,\"Index\":2}")); });
            var objectives=Load(f).Quests.Single().Objectives;
            Check(objectives.Count==3 && objectives[1].Type==DataType.KillMob && objectives[1].MobId==991050 && objectives[2].ItemId==991011,"unrelated objective was dropped or relabelled");
        });
        Case("repair changes execution identity and leaves the dataset bytes intact", f =>
        {
            string original=File.ReadAllText(f.DataPath); var patch=f.Pack["CollectionRouteRepairs"]!.DeepClone();
            f.Pack.Remove("CollectionRouteRepairs"); f.Write(); string before=f.Loader().ExecutionFingerprint;
            f.Pack["CollectionRouteRepairs"]=patch; f.Write();
            Check(f.Loader().ExecutionFingerprint!=before && File.ReadAllText(f.DataPath)==original,"repair identity or original bytes changed incorrectly");
        });
        Case("reference location does not become positive navigation authority", f =>
        {
            var point=Load(f).CreatureSpawns[NewTarget.ToString()].Single();
            Check(point.IsKnownSafe!=true && point.IsKnownReachable!=true,"reference point became confirmed navigation");
        });
        Case("scheduler selects repaired source and suppresses observed complete items", f =>
        {
            var db=Load(f); var active=Plan(db,true,items:new(){[ItemId]=0});
            Check(active.Plan.Any(p=>p.Stage==QuestWorkStage.Objective && p.ObjectiveIndex==0),"repaired source was not materialized");
            string xml=new ProfileBuilder().BuildProfileXml(active.Plan,db,"Fixture","Fixture",60);
            Check(XDocument.Parse(xml).Descendants("QuestOrder").Descendants("Objective").Any(e=>(string?)e.Attribute("MobId")==NewTarget.ToString()),"generated order did not use repaired creature");
            Check(!Plan(db,true,items:new(){[ItemId]=2}).Plan.Any(p=>p.Stage==QuestWorkStage.Objective),"complete item was recollected");
        });
        Case("unknown history full log recovery navigation and level still gate", f =>
        {
            var db=Load(f);
            Check(Plan(db).Plan.Any(p=>p.Stage==QuestWorkStage.Pickup),"positive pickup control failed");
            Check(!Plan(db,authority:false).Plan.Any(),"unknown history admitted new work");
            Check(!Plan(db,full:true).Plan.Any(p=>p.Stage==QuestWorkStage.Pickup),"full log admitted pickup");
            Check(!Plan(db,attempt:false).Plan.Any(),"quarantine bypassed");
            Check(!Plan(db,reachable:false).Plan.Any(),"unreachable route admitted");
            Check(!Plan(db,level:0).Plan.Any(),"below minimum level admitted");
        });
        Case("accepted failed quest cannot produce work", f => Check(!Plan(Load(f),true,failed:true).Plan.Any(),"failed quest scheduled"));
        Case("collection route runs pickup objective acknowledgement turn-in and next scheduling", Pipeline);

        foreach(string fault in new[]{"foreign-quest","duplicate-row","bad-operation","negative-row","wrong-index","wrong-type","wrong-old-target",
            "wrong-item","wrong-count","same-target","negative-target","replace-index-change","append-index-collision","duplicate-target",
            "scripted","kill-overlap","missing-geometry","negative-geometry","navigation-authority","missing-source","extra-field","count-owner","object-owner"})
        {
            string name=fault;
            Case("reject "+name,f=>{Corrupt(f,name);f.Write();bool refused=false;try{f.Load();}catch(InvalidDataException){refused=true;}Check(refused,"invalid route repair was accepted: "+name);});
        }
        Case("invalid repair remains retryable without a partially cached model", f=>
        {
            Repair(f)["SourceRef"]=""; f.Write();var loader=new DataLoader(f.DataPath);try{loader.Load();}catch(InvalidDataException){}
            Repair(f)["SourceRef"]="controlled://route";f.Write();Check(loader.Load().Quests.Single().Objectives[0].MobId==NewTarget,"failed repair cached partial state");
        });
        int passed=0,assertions=0,unexpected=0;
        foreach(var test in cases)
        {
            try{test.Body();passed++;Console.WriteLine("PASS collection route: "+test.Name);}
            catch(Failure error){assertions++;Console.Error.WriteLine("FAIL collection route: "+test.Name+": "+error.Message);}
            catch(InvalidDataException error){assertions++;Console.Error.WriteLine("FAIL collection route: "+test.Name+": supported contract rejected: "+error.Message);}
            catch(Exception error){unexpected++;Console.Error.WriteLine("ERROR collection route: "+test.Name+": "+error);}
        }
        Console.WriteLine($"Collection route scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual loader/scheduler/profile/item acknowledgement; no game.");
        if(assertions+unexpected!=0)throw new InvalidOperationException("Collection route regressions");
    }

    private static JsonObject Repair(QuestDataRepairPackRegressionTests.Fixture f)=>f.Pack["CollectionRouteRepairs"]![0]!.AsObject();
    private static void Configure(QuestDataRepairPackRegressionTests.Fixture f)
    {
        f.SetBase(data=>{var quest=data["Quests"]![0]!;quest["StartItem"]=0;quest["Objectives"]=JsonNode.Parse("[{\"Type\":1,\"MobId\":991021,\"ItemId\":991010,\"GameObjectId\":0,\"GameObjectName\":\"\",\"KillCount\":0,\"CollectCount\":2,\"Index\":0}]");});
        var metadata=f.Pack["QuestMetadata"]![0]!;metadata["DeliveryItems"]=null;metadata["AcceptanceSupplies"]=null;
        f.Pack["SpawnAdditions"]!.AsArray().Add(JsonNode.Parse("{\"ObjectType\":\"Creature\",\"Entry\":991040,\"SourceRef\":\"controlled://creature\",\"Points\":[{\"Map\":530,\"X\":10,\"Y\":20,\"Z\":37}]}"));
        f.Pack["SpawnAdditions"]!.AsArray().Add(JsonNode.Parse("{\"ObjectType\":\"Creature\",\"Entry\":991030,\"SourceRef\":\"controlled://ender\",\"Points\":[{\"Map\":530,\"X\":10,\"Y\":20,\"Z\":37}]}"));
        f.Pack["RelationAdditions"]!.AsArray().Add(JsonNode.Parse("{\"Role\":\"Giver\",\"QuestId\":991001,\"ObjectType\":\"Creature\",\"Entry\":991020,\"Name\":\"Giver\",\"SourceRef\":\"controlled://giver\"}"));
        f.Pack["CollectionRouteRepairs"]=JsonNode.Parse("""
        [{"Operation":"Replace","QuestId":991001,"RowIndex":0,"ObjectiveIndex":0,"ExpectedObjectiveType":"CollectItem","ExpectedTargetId":991021,"ItemId":991010,"RequiredCount":2,"CreatureId":991040,"NewObjectiveIndex":0,"SourceRef":"controlled://route"}]
        """);
    }
    private static QuestDatabase Load(QuestDataRepairPackRegressionTests.Fixture f)
    {f.Write();try{return f.Load();}catch(InvalidDataException error){throw new Failure("supported collection route was not loaded: "+error.Message);}}

    private static QuestScheduleResult Plan(QuestDatabase db,bool accepted=false,bool ready=false,bool failed=false,bool rewarded=false,
        Dictionary<int,long>? items=null,bool authority=true,bool full=false,bool attempt=true,bool reachable=true,int level=60)
    {
        var active=accepted?new List<QuestSchedulerAcceptedQuest>{new(){QuestId=QuestId,IsCompleted=ready,IsFailed=failed,ObjectiveCounts=new int[4]}}:new();
        if(full)for(uint id=800001;active.Count<25;id++)active.Add(new(){QuestId=id});
        return QuestScheduler.MaterializeSchedule(db,new(){UtcNow=DateTime.UtcNow,PlayerGuid=123,PlayerLevel=level,PlayerRaceId=10,PlayerClassId=2,
            MapId=530,X=10,Y=20,Z=37,HasAuthoritativeCompletions=authority,CompletedQuestIds=rewarded?new uint[]{QuestId}:Array.Empty<uint>(),
            AcceptedQuests=active,CarriedItemCounts=items??new()},
            _=>new QuestRecoveryDecision{MayAttempt=attempt,State=attempt?QuestRecoveryState.Eligible:QuestRecoveryState.Quarantined},10,250,80,
            navigationAssessment:_=>new SpawnNavigationAssessment{IsKnownSafe=true,IsKnownReachable=reachable});
    }
    private static void Pipeline(QuestDataRepairPackRegressionTests.Fixture f)
    {
        var db=Load(f);var quest=db.Quests.Single();var builder=new ProfileBuilder();using var live=new QuestDatasetObservationFixture();
        live.SetQuest(QuestId,quest.Name,60,new int[4],new int[4],new[]{ItemId,0,0,0,0,0},new[]{2,0,0,0,0,0});
        live.SetAccepted(false);live.SetHistory(Array.Empty<uint>());
        string xml=builder.BuildProfileXml(Plan(db).Plan,db,"Fixture","Fixture",60);live.LoadProfile(xml);
        var pickup=new ForcedQuestPickUp(QuestId,quest.Name,991020,"Giver",new WoWPoint(10,20,37),Styx.Logic.Profiles.Quest.QuestObjectType.Npc);
        Check(!pickup.IsDone,"unaccepted pickup acknowledged");live.SetAccepted(true);Check(pickup.IsDone,"accepted pickup not acknowledged");
        var active=Plan(db,true,items:new(){[ItemId]=0});xml=builder.BuildProfileXml(active.Plan,db,"Fixture","Fixture",60);live.LoadProfile(xml);
        var element=XDocument.Parse(xml).Descendants("QuestOrder").Descendants("Objective").Single();
        Check((string?)element.Attribute("MobId")==NewTarget.ToString(),"old target retained in executable order");
        var owner=live.CreateObjective(ObjectiveNode.FromXml(element));
        live.SetInventory(new());Check(!owner.IsDone,"source route invented inventory");
        live.SetInventory(new(){[ItemId]=1});Check(!owner.IsDone,"partial inventory acknowledged complete");
        live.SetInventory(new(){[ItemId]=2});Check(owner.IsDone,"actual item owner did not acknowledge two items");
        live.SetInventory(new());Check(!owner.IsDone,"lost stock retained item completion");live.SetInventory(new(){[ItemId]=2});
        Check(!Plan(db,true,items:new(){[ItemId]=2}).Plan.Any(p=>p.Stage==QuestWorkStage.TurnIn),"inventory fabricated authoritative ready state");
        var end=Plan(db,true,true,items:new(){[ItemId]=2});Check(end.Plan.Any(p=>p.Stage==QuestWorkStage.TurnIn),"ready quest did not turn in");
        xml=builder.BuildProfileXml(end.Plan,db,"Fixture","Fixture",60);live.LoadProfile(xml);live.SetAccepted(true,complete:true);
        var turnin=new ForcedQuestTurnIn(QuestId,quest.Name,991030,"Ender",new WoWPoint(10,20,37),Styx.Logic.Profiles.Quest.QuestObjectType.Npc);
        Check(!turnin.IsDone,"turn-in acknowledged without reward");live.SetAccepted(false);live.SetHistory(new uint[]{QuestId});Check(turnin.IsDone,"rewarded removal not acknowledged");
        Check(!Plan(db,rewarded:true).Plan.Any(p=>p.Quest.Id==QuestId),"rewarded quest rescheduled");
    }
    private static void Corrupt(QuestDataRepairPackRegressionTests.Fixture f,string fault)
    {
        var row=Repair(f);
        switch(fault)
        {
            case "foreign-quest":row["QuestId"]=999999;break;
            case "duplicate-row":f.Pack["CollectionRouteRepairs"]!.AsArray().Add(row.DeepClone());break;
            case "bad-operation":row["Operation"]="Delete";break;
            case "negative-row":row["RowIndex"]=-1;break;
            case "wrong-index":row["ObjectiveIndex"]=3;break;
            case "wrong-type":row["ExpectedObjectiveType"]="KillMob";break;
            case "wrong-old-target":row["ExpectedTargetId"]=12345;break;
            case "wrong-item":row["ItemId"]=991011;break;
            case "wrong-count":row["RequiredCount"]=3;break;
            case "same-target":row["CreatureId"]=OldTarget;break;
            case "negative-target":row["CreatureId"]=-1;break;
            case "replace-index-change":row["NewObjectiveIndex"]=1;break;
            case "append-index-collision":row["Operation"]="Append";break;
            case "duplicate-target":f.SetBase(data=>data["Quests"]![0]!["Objectives"]!.AsArray().Add(JsonNode.Parse("{\"Type\":1,\"MobId\":991040,\"ItemId\":991010,\"GameObjectId\":0,\"KillCount\":0,\"CollectCount\":2,\"Index\":1}")));break;
            case "scripted":f.SetBase(data=>data["Quests"]![0]!["SpecialFlags"]=32);break;
            case "kill-overlap":f.SetBase(data=>data["Quests"]![0]!["Objectives"]![0]!["KillCount"]=1);break;
            case "missing-geometry":f.Pack["SpawnAdditions"]!.AsArray().RemoveAt(1);break;
            case "negative-geometry":f.Pack["SpawnAdditions"]!.AsArray().RemoveAt(1);f.SetBase(data=>data["CreatureSpawns"]![NewTarget.ToString()]=JsonNode.Parse("[{\"Map\":530,\"X\":10,\"Y\":20,\"Z\":37,\"IsKnownSafe\":false}]"));break;
            case "navigation-authority":row["IsKnownSafe"]=true;break;
            case "missing-source":row["SourceRef"]="";break;
            case "extra-field":row["InventoryCount"]=2;break;
            case "count-owner":f.Pack["ObjectiveCountRepairs"]=JsonNode.Parse("[{\"QuestId\":991001,\"RowIndex\":0,\"ObjectiveIndex\":0,\"ObjectiveType\":\"CollectItem\",\"TargetId\":991021,\"ItemId\":991010,\"ExpectedCount\":2,\"RequiredCount\":3,\"SourceRef\":\"controlled://count\"}]");row["RequiredCount"]=3;break;
            case "object-owner":f.SetBase(data=>{var o=data["Quests"]![0]!["Objectives"]![0]!;o["Type"]=2;o["MobId"]=0;o["GameObjectId"]=991019;});
                f.Pack["GameObjectObjectiveRepairs"]=JsonNode.Parse("[{\"QuestId\":991001,\"RowIndex\":0,\"ObjectiveIndex\":0,\"ObjectiveType\":\"CollectFromGameObject\",\"ExpectedGameObjectId\":991019,\"GameObjectId\":991021,\"ItemId\":991010,\"RequiredCount\":2,\"SourceRef\":\"controlled://object\"}]");row["ExpectedObjectiveType"]="CollectFromGameObject";break;
        }
    }
    private static void Check(bool value,string message){if(!value)throw new Failure(message);}
}
