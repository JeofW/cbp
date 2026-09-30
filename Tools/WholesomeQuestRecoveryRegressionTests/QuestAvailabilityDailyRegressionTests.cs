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

internal static class QuestAvailabilityDailyRegressionTests
{
    private const uint Daily=991099;
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    private sealed class Failure(string message):Exception(message){}
    [ModuleInitializer]
    internal static void Run()
    {
        var tests=new List<(string Name,Action<Probe> Body)>();
        foreach(var state in new[]{"unknown","empty","present","other","duplicate","invalid"})
            foreach(bool negative in new[]{false,true})
            {
                string observed=state;bool negate=negative;
                tests.Add(($"daily membership state={state} negative={negative}",p=>
                {
                    p.F.Condition["Negative"]=negate;
                    uint[]? ids=observed switch{"unknown"=>null,"empty"=>Array.Empty<uint>(),"present"=>new[]{Daily},
                        "other"=>new[]{991098U},"duplicate"=>new[]{Daily,Daily},_=>new[]{uint.MaxValue}};
                    bool known=observed is "empty" or "present" or "other";
                    bool expected=known && (negate ? observed!="present" : observed=="present");
                    Check(p.Pickup(p.Snapshot(ids))==expected,"daily membership/negation admitted wrong state");
                    var result=QuestAvailabilityPolicy.Evaluate(p.Load().Quests.Single(),p.Snapshot(ids));
                    if(!known)Check(result.Status=="observation-unknown","unknown daily state became a negative fact");
                }));
            }
        tests.Add(("permanent reward does not satisfy current daily membership",p=>Check(!p.Pickup(p.Snapshot(Array.Empty<uint>(),rewarded:new[]{Daily})),"lifetime history became daily completion")));
        tests.Add(("accepted raw complete does not satisfy current daily membership",p=>Check(!p.Pickup(p.Snapshot(Array.Empty<uint>(),states:new(){{Daily,1}})),"raw completed quest became rewarded daily")));
        tests.Add(("repeatable daily membership is allowed without permanent reward",p=>Check(p.Pickup(p.Snapshot(new[]{Daily})),"repeatable daily reference was rejected")));
        tests.Add(("daily reference cannot relax a permanent reward predicate",p=>{p.F.AddCondition(8,(int)Daily);Reject(p); }));
        tests.Add(("AND group retains other reward requirements",p=>{p.F.AddCondition(8,991098);Check(!p.Pickup(p.Snapshot(new[]{Daily})),"daily state erased permanent prerequisite");Check(p.Pickup(p.Snapshot(new[]{Daily},rewarded:new[]{991098U})),"combined true predicates rejected");}));
        tests.Add(("OR group may satisfy an unknown daily predicate",p=>{p.F.AddGroup(1,8,991098);Check(p.Pickup(p.Snapshot(null,rewarded:new[]{991098U})),"known alternative did not satisfy OR");}));
        tests.Add(("accepted turn-in does not require daily pickup condition",p=>Check(p.Schedule(p.Snapshot(null,ready:true)).Plan.Any(x=>x.Stage==QuestWorkStage.TurnIn),"accepted work was gated by daily availability")));
        tests.Add(("actual player daily IDs reach the fresh availability capture",p=>
        {
            using var f=new QuestDailySnapshotRegressionTests.Fixture();f.Slot(0,Daily);
            var method=typeof(QuestScheduler).GetMethod("CaptureAvailabilitySnapshot",Hidden)!;
            var value=(QuestSchedulerSnapshot?)Invoke(method,null,f.Player,f.Player.QuestLog);
            Check(value!=null && DailyIds(value)?.Contains(Daily)==true,"actual fresh snapshot omitted daily IDs");
        }));
        tests.Add(("daily reset revokes retained pickup and fresh state recovers",p=>
        {
            using var f=new QuestDailySnapshotRegressionTests.Fixture();f.Slot(0,Daily);
            var q=p.Load().Quests.Single();var plan=new[]{new QuestPlanEntry{Quest=q,Stage=QuestWorkStage.Pickup}};
            bool Current()=>QuestAvailabilityPolicy.RequirementsCurrent(plan,p.Snapshot(QuestDailySnapshotRegressionTests.Ids(f.Capture())?.ToArray()));
            Check(Current(),"baseline daily not admitted");f.Slot(0,0);Check(!Current(),"daily reset kept permission");
            f.Slot(0,Daily);Check(Current(),"fresh daily completion did not recover");f.Missing=true;Check(!Current(),"unknown read retained permission");
        }));
        tests.Add(("diagnostics identify daily membership separately from permanent history",p=>
        {
            var result=QuestAvailabilityPolicy.Evaluate(p.Load().Quests.Single(),p.Snapshot(Array.Empty<uint>(),rewarded:new[]{Daily}));
            var row=result.Predicates.Single();var property=row.GetType().GetProperty("DailyCompleted");
            Check(property!=null && (bool?)property.GetValue(row)==false,"daily receipt missing from diagnostics");
            Check(row.PermanentlyRewarded==null && row.RawAcceptedState==null && row.ReferencedQuestId==(int)Daily,"daily and lifetime states conflated");
        }));
        foreach(string invalid in new[]{"missing-reference","extra-second","extra-third","foreign-target","wrong-method"})
        {
            string fault=invalid;tests.Add(("invalid daily contract="+fault,p=>
            {
                switch(fault){case"missing-reference":p.F.Contract["ReferencedQuests"]=new JsonArray();break;
                    case"extra-second":p.F.Condition["Value2"]=1;break;case"extra-third":p.F.Condition["Value3"]=1;break;
                    case"foreign-target":p.F.Condition["Target"]=1;break;case"wrong-method":p.F.Contract["ReferencedQuests"]![0]!["QuestType"]=7;break;}
                Reject(p);
            }));
        }
        tests.Add(("daily constrained pickup follows actual acceptance stock and reward",Pipeline));
        int passed=0,failed=0,errors=0;
        foreach(var test in tests)
        {
            try{using var p=new Probe();test.Body(p);passed++;Console.WriteLine("PASS availability daily: "+test.Name);}
            catch(Failure e){failed++;Console.Error.WriteLine("FAIL availability daily: "+test.Name+": "+e.Message);}
            catch(Exception e){errors++;Console.Error.WriteLine("ERROR availability daily: "+test.Name+": "+e);}
        }
        Console.WriteLine($"Availability daily scenarios: {passed}/{tests.Count}; assertions={failed}; unexpected={errors}; real loader/scheduler/daily snapshot/profile/acknowledgement, no game.");
        if(failed+errors!=0)throw new InvalidOperationException("Availability daily regression");
    }
    private sealed class Probe:IDisposable
    {
        internal readonly QuestAvailabilityConditionRegressionTests.Fixture F=new();
        internal Probe(){F.Condition["Type"]=43;F.Contract["ReferencedQuests"]![0]!["SpecialFlags"]=1;}
        internal QuestDatabase Load(){F.Write();try{return F.F.Load();}catch(InvalidDataException e){throw new Failure("supported daily contract rejected: "+e.Message);}}
        internal QuestSchedulerSnapshot Snapshot(uint[]? daily,uint[]? rewarded=null,Dictionary<uint,int>? states=null,bool ready=false)
        {
            var s=F.Snapshot(rewarded:rewarded,states:states,subjectReady:ready);
            var property=typeof(QuestSchedulerSnapshot).GetProperty("DailyQuestIds");Check(property!=null,"nullable daily observation missing");property!.SetValue(s,daily);return s;
        }
        internal QuestScheduleResult Schedule(QuestSchedulerSnapshot s)=>QuestScheduler.MaterializeSchedule(Load(),s,
            _=>new QuestRecoveryDecision{MayAttempt=true,State=QuestRecoveryState.Eligible},3,100,100);
        internal bool Pickup(QuestSchedulerSnapshot s)=>Schedule(s).Plan.Any(p=>p.Quest.Id==991001 && p.Stage==QuestWorkStage.Pickup);
        public void Dispose()=>F.Dispose();
    }
    private static void Pipeline(Probe p)
    {
        using var f=new QuestDailySnapshotRegressionTests.Fixture();var world=f.Native;f.Slot(0,Daily);
        var db=p.Load();var q=db.Quests.Single();uint id=(uint)q.Id;
        world.SetQuest(id,q.Name,60,new int[4],new int[4],new[]{991010,0,0,0,0,0},new[]{1,0,0,0,0,0});
        world.SetAccepted(false);world.SetHistory(Array.Empty<uint>());world.SetInventory(new(){{991010,1}});
        QuestSchedulerSnapshot Observe(bool ready=false,uint[]? rewarded=null)
        {
            var s=p.Snapshot(QuestDailySnapshotRegressionTests.Ids(f.Capture())?.ToArray(),rewarded,ready:ready);
            typeof(QuestSchedulerSnapshot).GetProperty("CarriedItemCounts")!.SetValue(s,world.Player.CarriedItems
                .GroupBy(i=>(int)i.Entry).ToDictionary(g=>g.Key,g=>g.Sum(i=>(long)i.StackCount)));
            return s;
        }
        var pickup=p.Schedule(Observe());Check(pickup.Plan.Any(x=>x.Stage==QuestWorkStage.Pickup),"daily did not admit pickup");
        var builder=new ProfileBuilder();string xml=builder.BuildProfileXml(pickup.Plan,db,"Controlled zone","Daily fixture",60);
        Check(XDocument.Parse(xml).Descendants("PickUp").Any(),"no generated pickup");world.LoadProfile(xml);
        var pick=new ForcedQuestPickUp(id,q.Name,991020,"Giver",new WoWPoint(10,20,37),Styx.Logic.Profiles.Quest.QuestObjectType.Npc);
        Check(!pick.IsDone,"daily receipt became quest acceptance");world.SetAccepted(true);Check(pick.IsDone,"raw acceptance not acknowledged");
        f.Slot(0,0);world.SetInventory(new());Check(!p.Schedule(Observe(ready:true)).Plan.Any(x=>x.Stage==QuestWorkStage.TurnIn),"missing stock accepted at turn-in");
        world.SetInventory(new(){{991010,1}});var ending=p.Schedule(Observe(ready:true));Check(ending.Plan.Any(x=>x.Stage==QuestWorkStage.TurnIn),"daily reset blocked accepted turn-in");
        world.LoadProfile(builder.BuildProfileXml(ending.Plan,db,"Controlled zone","Daily fixture",60));
        var turn=new ForcedQuestTurnIn(id,q.Name,991030,"Ender",new WoWPoint(11,20,37),Styx.Logic.Profiles.Quest.QuestObjectType.Npc);
        world.SetAccepted(true,complete:true);Check(!turn.IsDone,"turn-in lacked reward acknowledgement");
        world.SetAccepted(false);world.SetHistory(new[]{id});Check(turn.IsDone,"actual rewarded removal not acknowledged");
        Check(!p.Schedule(Observe(rewarded:new[]{id})).Plan.Any(x=>x.Quest.Id==q.Id),"rewarded subject scheduled again");
    }
    private static IReadOnlyCollection<uint>? DailyIds(QuestSchedulerSnapshot s)=>(IReadOnlyCollection<uint>?)s.GetType().GetProperty("DailyQuestIds")?.GetValue(s);
    private static void Reject(Probe p){p.F.Write();bool rejected=false;try{p.F.F.Load();}catch(InvalidDataException){rejected=true;}Check(rejected,"unsupported daily contract accepted");}
    private static object? Invoke(MethodInfo method,object? target,params object?[] args)
    {try{return method.Invoke(target,args);}catch(TargetInvocationException e)when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}}
    private static void Check(bool ok,string message){if(!ok)throw new Failure(message);}
}
