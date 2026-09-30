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

internal static class QuestAvailabilityAreaRegressionTests
{
    private const int Subject=991001;
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    private sealed class Failure(string message):Exception(message){}

    [ModuleInitializer]
    internal static void Run()
    {
        var tests=new List<(string Name,Action<Probe> Body)>();
        foreach(int? area in new int?[]{null,0,-1,99,100})
            foreach(bool negative in new[]{false,true})
            {
                int? observed=area;bool negate=negative;
                tests.Add(($"area equality/negation observed={observed} negative={negate}",p=>
                {
                    p.Negative(negate);bool known=observed>0;
                    bool expected=known && (negate ? observed!=99 : observed==99);
                    var snapshot=p.Snapshot(observed);
                    Check(p.Pickup(snapshot)==expected,"area condition admitted the wrong observation");
                    var result=QuestAvailabilityPolicy.Evaluate(p.Load().Quests.Single(),snapshot);
                    Check(known || result.Status=="observation-unknown","unavailable area became known absence");
                }));
            }
        tests.Add(("numeric area has no quest-history dependency",p=>{p.F.Contract["ReferencedQuests"]=new JsonArray();Check(p.Pickup(p.Snapshot(99)),"area was treated as a quest reference");}));
        tests.Add(("area and reward predicates preserve AND grouping",p=>
        {p.F.AddCondition(8,991099);Check(!p.Pickup(p.Snapshot(99)),"area erased a reward condition");Check(p.Pickup(p.Snapshot(99,new uint[]{991099})),"both conditions did not admit pickup");}));
        tests.Add(("alternate group can independently satisfy the contract",p=>
        {p.F.AddGroup(1,8,991099);Check(p.Pickup(p.Snapshot(null,new uint[]{991099})),"known alternate group could not satisfy OR");}));
        tests.Add(("accepted turn-in is not an area pickup check",p=>
        {Check(p.Schedule(p.Snapshot(null,ready:true)).Plan.Any(x=>x.Stage==QuestWorkStage.TurnIn),"accepted turn-in acquired area availability");}));
        tests.Add(("known area from the actual observer reaches the policy",p=>
        {using var memory=new QuestAreaSnapshotRegressionTests.Fixture();var s=memory.Capture();Check(p.Pickup(p.Snapshot(QuestAreaSnapshotRegressionTests.Area(s))),"complete actual area observation was lost");}));
        tests.Add(("fresh changed observation revokes area permission",p=>
        {
            using var memory=new QuestAreaSnapshotRegressionTests.Fixture();var q=p.Load().Quests.Single();
            var plan=new[]{new QuestPlanEntry{Quest=q,Stage=QuestWorkStage.Pickup}};
            bool Current()=>QuestAvailabilityPolicy.RequirementsCurrent(plan,p.Snapshot(QuestAreaSnapshotRegressionTests.Area(memory.Capture())));
            Check(Current(),"initial matching area rejected");memory.Area(100);Check(!Current(),"changed area kept permission");
            memory.Area(99);Check(Current(),"fresh matching area did not recover");memory.MissingArea=true;Check(!Current(),"unknown area retained permission");
        }));
        tests.Add(("area diagnostics identify values rather than a referenced quest",p=>
        {
            var decision=QuestAvailabilityPolicy.Evaluate(p.Load().Quests.Single(),p.Snapshot(100));
            var row=decision.Predicates.Single();Check(row.ReferencedQuestId==0,"area99 was named quest99");
            Check((int?)Get(row,"RequiredAreaId")==99 && (int?)Get(row,"ObservedAreaId")==100,"exact required/observed area missing");
            Check(decision.Rejection=="availability-condition-not-satisfied","exact final area rejection missing");
        }));
        foreach(var invalid in new[]{"zero","extra-second","extra-third","integer-negation","foreign-target"})
        {
            string fault=invalid;
            tests.Add(("invalid area contract rejected="+fault,p=>
            {
                switch(fault){case"zero":p.F.Condition["Value1"]=0;break;case"extra-second":p.F.Condition["Value2"]=1;break;
                    case"extra-third":p.F.Condition["Value3"]=1;break;case"integer-negation":p.F.Condition["Negative"]=1;break;
                    case"foreign-target":p.F.Condition["Target"]=1;break;}
                p.F.Write();bool rejected=false;try{p.F.F.Load();}catch(InvalidDataException){rejected=true;}Check(rejected,"unsupported area fields accepted");
            }));
        }
        tests.Add(("area constrained delivery completes only after real acknowledgements",Pipeline));
        int passed=0,failed=0,errors=0;
        foreach(var test in tests)
        {
            try{using var p=new Probe();test.Body(p);passed++;Console.WriteLine("PASS availability area: "+test.Name);}
            catch(Failure e){failed++;Console.Error.WriteLine("FAIL availability area: "+test.Name+": "+e.Message);}
            catch(Exception e){errors++;Console.Error.WriteLine("ERROR availability area: "+test.Name+": "+e);}
        }
        Console.WriteLine($"Availability area scenarios: {passed}/{tests.Count}; assertions={failed}; unexpected={errors}; actual source loader, area observation, scheduler/profile/behavior, no game.");
        if(failed+errors!=0)throw new InvalidOperationException("Availability area regression");
    }

    private sealed class Probe:IDisposable
    {
        internal readonly QuestAvailabilityConditionRegressionTests.Fixture F=new();
        internal Probe(){F.Condition["Type"]=23;F.Condition["Value1"]=99;}
        internal void Negative(bool value)=>F.Condition["Negative"]=value;
        internal QuestDatabase Load(){F.Write();try{return F.F.Load();}catch(InvalidDataException e){throw new Failure("supported area contract rejected: "+e.Message);}}
        internal QuestSchedulerSnapshot Snapshot(int? area,uint[]? rewarded=null,bool ready=false)
        {var value=F.Snapshot(rewarded:rewarded,subjectReady:ready);var property=typeof(QuestSchedulerSnapshot).GetProperty("PlayerAreaId");Check(property!=null,"nullable current area observation missing");property!.SetValue(value,area);return value;}
        internal QuestScheduleResult Schedule(QuestSchedulerSnapshot snapshot)=>QuestScheduler.MaterializeSchedule(Load(),snapshot,
            _=>new QuestRecoveryDecision{MayAttempt=true,State=QuestRecoveryState.Eligible},3,100,100);
        internal bool Pickup(QuestSchedulerSnapshot snapshot)=>Schedule(snapshot).Plan.Any(p=>p.Quest.Id==Subject && p.Stage==QuestWorkStage.Pickup);
        public void Dispose()=>F.Dispose();
    }

    private static void Pipeline(Probe p)
    {
        using var memory=new QuestAreaSnapshotRegressionTests.Fixture();var world=memory.Native;
        QuestDatabase db=p.Load();QuestEntry q=db.Quests.Single();uint id=(uint)q.Id;
        world.SetQuest(id,q.Name,60,new int[4],new int[4],new[]{991010,0,0,0,0,0},new[]{1,0,0,0,0,0});
        world.SetAccepted(false);world.SetHistory(Array.Empty<uint>());world.SetInventory(new(){{991010,1}});
        QuestSchedulerSnapshot Current(int? area,bool ready=false,uint[]? rewarded=null)
        {
            var snapshot=p.Snapshot(area,rewarded,ready);
            var counts=world.Player.CarriedItems.GroupBy(item=>(int)item.Entry).ToDictionary(group=>group.Key,group=>group.Sum(item=>(long)item.StackCount));
            typeof(QuestSchedulerSnapshot).GetProperty("CarriedItemCounts")!.SetValue(snapshot,counts);
            return snapshot;
        }
        var observed=memory.Capture();Check(QuestAreaSnapshotRegressionTests.Area(observed)==99,"initial actual area unavailable");
        var pickup=p.Schedule(Current(QuestAreaSnapshotRegressionTests.Area(observed)));
        Check(pickup.Plan.Any(x=>x.Stage==QuestWorkStage.Pickup),"area did not admit generated pickup");
        var builder=new ProfileBuilder();string xml=builder.BuildProfileXml(pickup.Plan,db,"Controlled zone","Controlled area actor",60);
        Check(XDocument.Parse(xml).Descendants("PickUp").Any(),"area pickup profile absent");world.LoadProfile(xml);
        var pick=new ForcedQuestPickUp(id,q.Name,991020,"Controlled giver",new WoWPoint(10,20,37),Styx.Logic.Profiles.Quest.QuestObjectType.Npc);
        Check(!pick.IsDone,"area observation replaced acceptance");world.SetAccepted(true);Check(pick.IsDone,"real accepted log not acknowledged");
        memory.Area(100);world.SetAccepted(true,complete:true);world.SetInventory(new());
        Check(!p.Schedule(Current(100,ready:true)).Plan.Any(x=>x.Stage==QuestWorkStage.TurnIn),"area state replaced a missing delivery item");
        world.SetInventory(new(){{991010,1}});var ending=p.Schedule(Current(100,ready:true));
        Check(ending.Plan.Any(x=>x.Stage==QuestWorkStage.TurnIn),"leaving pickup area blocked accepted turn-in");
        string endXml=builder.BuildProfileXml(ending.Plan,db,"Controlled zone","Controlled area actor",60);world.LoadProfile(endXml);
        Check(XDocument.Parse(endXml).Descendants("TurnIn").Any(),"actual turn-in profile absent");
        var turn=new ForcedQuestTurnIn(id,q.Name,991030,"Controlled ender",new WoWPoint(11,20,37),Styx.Logic.Profiles.Quest.QuestObjectType.Npc);
        world.SetAccepted(true,complete:true);Check(!turn.IsDone,"turn-in acknowledged without reward history");
        world.SetAccepted(false);world.SetHistory(new[]{id});Check(turn.IsDone,"real reward/removal was not acknowledged");
        Check(!p.Schedule(Current(99,rewarded:new[]{id})).Plan.Any(x=>x.Quest.Id==q.Id),"rewarded quest scheduled again");
    }
    private static object? Get(object value,string name){var property=value.GetType().GetProperty(name);Check(property!=null,"missing field "+name);return property!.GetValue(value);}
    private static void Check(bool value,string message){if(!value)throw new Failure(message);}
}
