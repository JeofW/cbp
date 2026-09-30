using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Bots.Quest.QuestOrder;
using Styx.Logic.Pathing;
using Styx.Logic.Questing;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;

internal static class QuestAvailabilityItemRegressionTests
{
    private const int Item=991010;
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    private sealed class Failure(string message):Exception(message){}
    [ModuleInitializer]
    internal static void Run()
    {
        var tests=new List<(string Name,Action<Probe> Body)>();
        foreach(long? count in new long?[]{null,0,1,2,3,-1,long.MaxValue})
            foreach(bool negative in new[]{false,true})
            {
                long? captured=count;bool negate=negative;
                tests.Add(($"carried item count={count?.ToString() ?? "unknown"} negative={negative}",p=>
                {
                    p.F.Condition["Negative"]=negate;
                    var observation=p.Snapshot(captured.HasValue ? new(){{Item,captured.Value}}:null);
                    bool known=captured>=0;bool expected=known && (negate ? captured<2 : captured>=2);
                    Check(p.Pickup(observation)==expected,"item count/negation admitted wrong quantity");
                    if(!known)Check(QuestAvailabilityPolicy.Evaluate(p.Load().Quests.Single(),observation).Status=="observation-unknown",
                        "unavailable quantity became a negative fact");
                }));
            }
        tests.Add(("complete missing item is zero while other item stock is irrelevant",p=>
        {
            Check(!p.Pickup(p.Snapshot(new(){{Item+1,99}})),"other item donated stock");
            p.F.Condition["Negative"]=true;Check(p.Pickup(p.Snapshot(new(){{Item+1,99}})),"complete absence was not known zero");
        }));
        tests.Add(("permanent quest ID cannot substitute for the same-number item",p=>Check(!p.Pickup(p.Snapshot(new(),new[]{(uint)Item})),"quest reward became inventory")));
        tests.Add(("source-promised acceptance item is not pre-pickup stock",p=>Check(!p.Pickup(p.Snapshot(new())),"acceptance promise satisfied held-item condition")));
        tests.Add(("AND group preserves quest and item requirements",p=>
        {
            p.F.AddCondition(8,991099);p.F.Contract["Groups"]![0]!["Conditions"]![1]!["Value2"]=0;
            Check(!p.Pickup(p.Snapshot(new(){{Item,2}})),"item erased reward requirement");
            Check(p.Pickup(p.Snapshot(new(){{Item,2}},new[]{991099U})),"both valid inputs rejected");
        }));
        tests.Add(("OR can satisfy an unknown item through known reward evidence",p=>
        {p.F.AddGroup(1,8,991099);p.F.Contract["Groups"]![1]!["Conditions"]![0]!["Value2"]=0;Check(p.Pickup(p.Snapshot(null,new[]{991099U})),"known alternative was lost");}));
        tests.Add(("accepted turn-in does not retain its pickup item predicate",p=>
        {p.F.Condition["Value2"]=5;Check(p.Schedule(p.Snapshot(new(){{Item,1}},ready:true)).Plan.Any(x=>x.Stage==QuestWorkStage.TurnIn),"accepted turn-in pickup gated");}));
        tests.Add(("lost inventory revokes availability permission and fresh stock recovers",p=>
        {
            using var f=new QuestInventorySnapshotRegressionTests.Fixture();var item=f.Item(23,Item,2);var q=p.Load().Quests.Single();
            var plan=new[]{new QuestPlanEntry{Quest=q,Stage=QuestWorkStage.Pickup}};
            bool Current()=>QuestAvailabilityPolicy.RequirementsCurrent(plan,p.FromInventory((QuestInventorySnapshot)f.Capture()));
            Check(Current(),"initial held item rejected");f.MainSlot(23,0);Check(!Current(),"lost item kept permission");
            f.MainSlot(23,item.Guid);Check(Current(),"fresh held stock did not recover");f.MainSlot(23,777);Check(!Current(),"unresolved occupied slot became known stock");
        }));
        tests.Add(("active trade does not satisfy a negated absent-item predicate",p=>
        {using var f=new QuestInventorySnapshotRegressionTests.Fixture();p.F.Condition["Negative"]=true;f.TradeOwners(777,777);Check(!p.Pickup(p.FromInventory((QuestInventorySnapshot)f.Capture())),"pending trade became absent stock");}));
        tests.Add(("bank slot cannot satisfy a carried-item condition",p=>
        {using var f=new QuestInventorySnapshotRegressionTests.Fixture();f.Item(39,Item,20);Check(!p.Pickup(p.FromInventory((QuestInventorySnapshot)f.Capture())),"bank item counted as carried");}));
        tests.Add(("diagnostics preserve item identity threshold and observed quantity",p=>
        {
            var result=QuestAvailabilityPolicy.Evaluate(p.Load().Quests.Single(),p.Snapshot(new(){{Item,1}}));var row=result.Predicates.Single();
            Check((int?)row.GetType().GetProperty("ItemId")?.GetValue(row)==Item && (long?)row.GetType().GetProperty("ObservedItemCount")?.GetValue(row)==1,
                "exact item evidence absent in diagnostic");
            Check(row.ReferencedQuestId==0 && row.RawAcceptedState==null && row.PermanentlyRewarded==null,"item identity conflated with quest history");
        }));
        foreach(string invalid in new[]{"missing-item-reference","duplicate-item-reference","empty-source","invalid-item-id","bank","zero-count","negative-count","foreign-target","unknown-field"})
        {
            string fault=invalid;tests.Add(("invalid item contract="+fault,p=>
            {
                switch(fault){case"missing-item-reference":p.F.Contract.Remove("ReferencedItems");break;
                    case"duplicate-item-reference":p.F.Contract["ReferencedItems"]!.AsArray().Add(p.F.Contract["ReferencedItems"]![0]!.DeepClone());break;
                    case"empty-source":p.F.Contract["ReferencedItems"]![0]!["SourceRef"]="";break;
                    case"invalid-item-id":p.F.Contract["ReferencedItems"]![0]!["ItemId"]=0;break;
                    case"bank":p.F.Condition["Value3"]=1;break;case"zero-count":p.F.Condition["Value2"]=0;break;
                    case"negative-count":p.F.Condition["Value2"]=-1;break;case"foreign-target":p.F.Condition["Target"]=1;break;
                    case"unknown-field":p.F.Contract["ReferencedItems"]![0]!["GuessedCount"]=2;break;}
                Reject(p);
            }));
        }
        tests.Add(("held item permits real pickup and subsequent reward acknowledgement",Pipeline));
        int passed=0,failed=0,errors=0;
        foreach(var test in tests)
        {try{using var p=new Probe();test.Body(p);passed++;Console.WriteLine("PASS availability item: "+test.Name);}
         catch(Failure e){failed++;Console.Error.WriteLine("FAIL availability item: "+test.Name+": "+e.Message);}
         catch(Exception e){errors++;Console.Error.WriteLine("ERROR availability item: "+test.Name+": "+e);}}
        Console.WriteLine($"Availability item scenarios: {passed}/{tests.Count}; assertions={failed}; unexpected={errors}; real source loader/inventory/scheduler/profile/acknowledgement, no game.");
        if(failed+errors!=0)throw new InvalidOperationException("Availability item regression");
    }
    private sealed class Probe:IDisposable
    {
        internal readonly QuestAvailabilityConditionRegressionTests.Fixture F=new();
        internal Probe()
        {F.Condition["Type"]=2;F.Condition["Value1"]=Item;F.Condition["Value2"]=2;
         F.Contract["ReferencedItems"]=JsonNode.Parse("[{\"ItemId\":991010,\"SourceRef\":\"controlled://item_template/991010\"}]");}
        internal QuestDatabase Load(){F.Write();try{return F.F.Load();}catch(InvalidDataException e){throw new Failure("supported item contract rejected: "+e.Message);}}
        internal QuestSchedulerSnapshot Snapshot(Dictionary<int,long>? counts,uint[]? rewarded=null,bool ready=false)
        {var s=F.Snapshot(rewarded:rewarded,subjectReady:ready);typeof(QuestSchedulerSnapshot).GetProperty("CarriedItemCounts")!.SetValue(s,counts);return s;}
        internal QuestSchedulerSnapshot FromInventory(QuestInventorySnapshot sample)=>Snapshot(sample.IsCurrent() && sample.ItemCounts!=null ? new Dictionary<int,long>(sample.ItemCounts):null);
        internal QuestScheduleResult Schedule(QuestSchedulerSnapshot s)=>QuestScheduler.MaterializeSchedule(Load(),s,
            _=>new QuestRecoveryDecision{MayAttempt=true,State=QuestRecoveryState.Eligible},3,100,100);
        internal bool Pickup(QuestSchedulerSnapshot s)=>Schedule(s).Plan.Any(x=>x.Quest.Id==991001 && x.Stage==QuestWorkStage.Pickup);
        public void Dispose()=>F.Dispose();
    }
    private static void Pipeline(Probe p)
    {
        using var f=new QuestInventorySnapshotRegressionTests.Fixture();var world=f.NativeFixture;var db=p.Load();var q=db.Quests.Single();uint id=(uint)q.Id;
        world.SetQuest(id,q.Name,60,new int[4],new int[4],new[]{Item,0,0,0,0,0},new[]{1,0,0,0,0,0});world.SetAccepted(false);world.SetHistory(Array.Empty<uint>());
        var held=f.Item(23,Item,2);var observation=p.FromInventory((QuestInventorySnapshot)f.Capture());var pickup=p.Schedule(observation);
        Check(pickup.Plan.Any(x=>x.Stage==QuestWorkStage.Pickup),"held stock did not admit pickup");
        var builder=new ProfileBuilder();var xml=builder.BuildProfileXml(pickup.Plan,db,"Controlled zone","Item fixture",60);
        Check(XDocument.Parse(xml).Descendants("PickUp").Any(),"pickup profile absent");world.LoadProfile(xml);
        var pick=new ForcedQuestPickUp(id,q.Name,991020,"Giver",new WoWPoint(10,20,37),Styx.Logic.Profiles.Quest.QuestObjectType.Npc);
        Check(!pick.IsDone,"held item became acceptance");world.SetAccepted(true);Check(pick.IsDone,"accepted log not acknowledged");
        f.MainSlot(23,0);Check(!p.Schedule(p.Snapshot(new(),ready:true)).Plan.Any(x=>x.Stage==QuestWorkStage.TurnIn),"source supply substituted for lost return item");
        f.MainSlot(23,held.Guid);f.Write32(held.Fields+56,1);var ending=p.Schedule(p.Snapshot(new(){{Item,1}},ready:true));
        Check(ending.Plan.Any(x=>x.Stage==QuestWorkStage.TurnIn),"pickup threshold incorrectly gated accepted turn-in");
        world.LoadProfile(builder.BuildProfileXml(ending.Plan,db,"Controlled zone","Item fixture",60));
        var turn=new ForcedQuestTurnIn(id,q.Name,991030,"Ender",new WoWPoint(11,20,37),Styx.Logic.Profiles.Quest.QuestObjectType.Npc);
        world.SetAccepted(true,complete:true);Check(!turn.IsDone,"stock/ready state became reward receipt");world.SetAccepted(false);world.SetHistory(new[]{id});Check(turn.IsDone,"rewarded removal not acknowledged");
        Check(!p.Schedule(p.Snapshot(new(){{Item,2}},new[]{id})).Plan.Any(x=>x.Quest.Id==q.Id),"rewarded quest scheduled again");
    }
    private static void Reject(Probe p){p.F.Write();bool rejected=false;try{p.F.F.Load();}catch(InvalidDataException){rejected=true;}Check(rejected,"unsupported item contract accepted");}
    private static void Check(bool ok,string message){if(!ok)throw new Failure(message);}
}
