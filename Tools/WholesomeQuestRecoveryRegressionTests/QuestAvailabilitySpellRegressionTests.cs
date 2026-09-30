using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Bots.Quest.QuestOrder;
using Styx.Logic.Pathing;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;

internal static class QuestAvailabilitySpellRegressionTests
{
    private const int Spell=54197;
    private const BindingFlags H=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    private sealed class Failure(string message):Exception(message){}
    [ModuleInitializer]
    internal static void Run()
    {
        var tests=new List<(string Name,Action<Probe> Body)>();
        void Case(string name,Action<Probe> body)=>tests.Add((name,body));
        foreach(string observed in new[]{"unknown","empty","other","known","duplicate","invalid"})
        {
            string state=observed;Case("spell evidence="+state,p=>{
                uint[]? ids=state switch{"unknown"=>null,"empty"=>Array.Empty<uint>(),"other"=>new[]{123U},
                    "known"=>new[]{(uint)Spell},"duplicate"=>new[]{(uint)Spell,(uint)Spell},_=>new[]{0U}};
                var s=p.Snapshot(ids);Check(p.Pickup(s)==(state=="known"),"positive spell admission disagrees");
                if(state!="known")Check(QuestAvailabilityPolicy.Evaluate(p.Load().Quests.Single(),s).Status=="observation-unknown","missing spell evidence was labelled authoritative absence");});
        }
        Case("reward history with a matching numeric ID is not spell knowledge",p=>Check(!p.Pickup(p.Snapshot(null,rewarded:new[]{(uint)Spell})),"quest history became a learned spell"));
        Case("carried item with a matching ID is not spell knowledge",p=>{var s=p.Snapshot(null);typeof(QuestSchedulerSnapshot).GetProperty("CarriedItemCounts")!.SetValue(s,new Dictionary<int,long>{{Spell,1}});Check(!p.Pickup(s),"item became a learned spell");});
        Case("every AND predicate must be satisfied",p=>{
            p.F.AddCondition(8,991098);Check(!p.Pickup(p.Snapshot(new[]{(uint)Spell})),"spell erased reward requirement");
            Check(p.Pickup(p.Snapshot(new[]{(uint)Spell},rewarded:new[]{991098U})),"combined true source group rejected");});
        Case("known alternative can satisfy an unknown spell OR group",p=>{
            p.F.AddGroup(1,8,991098);Check(p.Pickup(p.Snapshot(null,rewarded:new[]{991098U})),"OR alternative was lost");});
        Case("accepted turn-in retains independent inventory and readiness",p=>Check(p.Schedule(p.Snapshot(null,ready:true)).Plan.Any(x=>x.Stage==QuestWorkStage.TurnIn),"accepted quest was spell-pickup gated"));
        Case("a lost positive confirmation revokes pickup permission",p=>{
            var plan=p.Schedule(p.Snapshot(new[]{(uint)Spell})).Plan;
            Check(QuestAvailabilityPolicy.RequirementsCurrent(plan,p.Snapshot(new[]{(uint)Spell})),"initial confirmation rejected");
            Check(!QuestAvailabilityPolicy.RequirementsCurrent(plan,p.Snapshot(Array.Empty<uint>())),"unconfirmed spell retained pickup permission");});
        Case("diagnostics keep spell evidence outside quest and item namespaces",p=>{
            var result=QuestAvailabilityPolicy.Evaluate(p.Load().Quests.Single(),p.Snapshot(new[]{(uint)Spell}));var row=result.Predicates.Single();
            Check((int?)row.GetType().GetProperty("RequiredSpellId")?.GetValue(row)==Spell,"missing exact spell ID");
            Check((bool?)row.GetType().GetProperty("SpellPositivelyKnown")?.GetValue(row)==true,"missing positive receipt");
            Check(row.ReferencedQuestId==0 && row.RawAcceptedState==null && row.PermanentlyRewarded==null && row.ItemId==null,"spell knowledge borrowed another namespace");});
        foreach(string fault in new[]{"negative","missing-reference","duplicate-reference","zero-reference","wrong-reference","empty-source","extra-value","extra-third","foreign-target"})
        {
            string invalid=fault;Case("invalid spell contract="+invalid,p=>{
                switch(invalid){case"negative":p.F.Condition["Negative"]=true;break;
                    case"missing-reference":p.F.Contract.Remove("ReferencedSpells");break;
                    case"duplicate-reference":p.F.Contract["ReferencedSpells"]!.AsArray().Add(p.F.Contract["ReferencedSpells"]![0]!.DeepClone());break;
                    case"zero-reference":p.F.Contract["ReferencedSpells"]![0]!["SpellId"]=0;break;
                    case"wrong-reference":p.F.Contract["ReferencedSpells"]![0]!["SpellId"]=123;break;
                    case"empty-source":p.F.Contract["ReferencedSpells"]![0]!["SourceRef"]="";break;
                    case"extra-value":p.F.Condition["Value2"]=1;break;case"extra-third":p.F.Condition["Value3"]=1;break;
                    case"foreign-target":p.F.Condition["Target"]=1;break;}
                p.F.Write();try{p.F.F.Load();throw new Failure("invalid spell contract loaded");}catch(InvalidDataException){} });
        }
        Case("actual positive Lua observation reaches pickup and reward lifecycle",Pipeline);
        int pass=0,fail=0,errors=0;
        foreach(var test in tests)
        {
            try{using var p=new Probe();test.Body(p);pass++;Console.WriteLine("PASS availability spell: "+test.Name);}
            catch(Failure e){fail++;Console.Error.WriteLine("FAIL availability spell: "+test.Name+": "+e.Message);}
            catch(Exception e){errors++;Console.Error.WriteLine("ERROR availability spell: "+test.Name+": "+e);}
        }
        Console.WriteLine($"Availability spell scenarios: {pass}/{tests.Count}; assertions={fail}; unexpected={errors}; source-bound loader, actual Lua5.1 observation, scheduler/profile/acknowledgement; no game.");
        if(fail+errors!=0)throw new InvalidOperationException("Availability spell regression");
    }
    private sealed class Probe:IDisposable
    {
        internal readonly QuestAvailabilityConditionRegressionTests.Fixture F=new();
        internal Probe(){F.Condition["Type"]=25;F.Condition["Value1"]=Spell;
            F.Contract["ReferencedSpells"]=JsonNode.Parse("[{\"SpellId\":54197,\"SourceRef\":\"controlled://conditions/spell=54197\"}]");}
        internal QuestDatabase Load(){F.Write();try{return F.F.Load();}catch(InvalidDataException e){throw new Failure("positive spell contract rejected: "+e.Message);}}
        internal QuestSchedulerSnapshot Snapshot(uint[]? ids,uint[]? rewarded=null,bool ready=false)
        {
            var s=F.Snapshot(rewarded:rewarded,subjectReady:ready);var property=typeof(QuestSchedulerSnapshot).GetProperty("ConfirmedSpellIds");
            Check(property!=null,"positive spell snapshot field missing");property!.SetValue(s,ids);return s;
        }
        internal QuestScheduleResult Schedule(QuestSchedulerSnapshot s)=>QuestScheduler.MaterializeSchedule(Load(),s,
            _=>new QuestRecoveryDecision{MayAttempt=true,State=QuestRecoveryState.Eligible},3,100,100);
        internal bool Pickup(QuestSchedulerSnapshot s)=>Schedule(s).Plan.Any(x=>x.Stage==QuestWorkStage.Pickup);
        public void Dispose()=>F.Dispose();
    }
    private static void Pipeline(Probe p)
    {
        using var f=new QuestSpellKnowledgeSnapshotRegressionTests.Fixture();var world=f.Native;
        var db=p.Load();var q=db.Quests.Single();uint id=(uint)q.Id;
        world.SetQuest(id,q.Name,60,new int[4],new int[4],new[]{991010,0,0,0,0,0},new[]{1,0,0,0,0,0});
        world.SetAccepted(false);world.SetHistory(Array.Empty<uint>());world.SetInventory(new(){{991010,1}});
        QuestSchedulerSnapshot Observe(bool ready=false,uint[]? rewarded=null)
        {
            var sample=f.Capture(new[]{Spell});var s=p.Snapshot(QuestSpellKnowledgeSnapshotRegressionTests.Ids(sample)?.ToArray(),rewarded,ready);
            typeof(QuestSchedulerSnapshot).GetProperty("CarriedItemCounts")!.SetValue(s,world.Player.CarriedItems.GroupBy(i=>(int)i.Entry).ToDictionary(g=>g.Key,g=>g.Sum(i=>(long)i.StackCount)));
            typeof(QuestSchedulerSnapshot).GetProperty("PlayerGuid")!.SetValue(s,world.Player.Guid);return s;
        }
        var pickup=p.Schedule(Observe());Check(pickup.Plan.Any(x=>x.Stage==QuestWorkStage.Pickup),"positive API receipt did not admit pickup");
        f.Mode="false";Check(!QuestAvailabilityPolicy.RequirementsCurrent(pickup.Plan,Observe()),"lost confirmation retained permission");
        f.Mode="positive";Check(QuestAvailabilityPolicy.RequirementsCurrent(pickup.Plan,Observe()),"fresh positive knowledge did not recover");
        var builder=new ProfileBuilder();string xml=builder.BuildProfileXml(pickup.Plan,db,"Controlled zone","Spell fixture",60);
        Check(XDocument.Parse(xml).Descendants("PickUp").Any(),"no generated pickup");world.LoadProfile(xml);
        var pick=new ForcedQuestPickUp(id,q.Name,991020,"Giver",new WoWPoint(10,20,37),Styx.Logic.Profiles.Quest.QuestObjectType.Npc);
        Check(!pick.IsDone,"spell receipt fabricated acceptance");world.SetAccepted(true);Check(pick.IsDone,"raw acceptance not acknowledged");
        f.Mode="false";world.SetInventory(new());Check(!p.Schedule(Observe(ready:true)).Plan.Any(x=>x.Stage==QuestWorkStage.TurnIn),"source promise replaced stock");
        world.SetInventory(new(){{991010,1}});var ending=p.Schedule(Observe(ready:true));Check(ending.Plan.Any(x=>x.Stage==QuestWorkStage.TurnIn),"unknown pickup spell blocked accepted turn-in");
        world.LoadProfile(builder.BuildProfileXml(ending.Plan,db,"Controlled zone","Spell fixture",60));
        var turn=new ForcedQuestTurnIn(id,q.Name,991030,"Ender",new WoWPoint(11,20,37),Styx.Logic.Profiles.Quest.QuestObjectType.Npc);
        world.SetAccepted(true,complete:true);Check(!turn.IsDone,"turn-in lacked reward acknowledgement");
        world.SetAccepted(false);world.SetHistory(new[]{id});Check(turn.IsDone,"actual rewarded removal not acknowledged");
        Check(!p.Schedule(Observe(rewarded:new[]{id})).Plan.Any(x=>x.Quest.Id==q.Id),"rewarded subject scheduled again");
    }
    private static void Check(bool value,string message){if(!value)throw new Failure(message);}
}
