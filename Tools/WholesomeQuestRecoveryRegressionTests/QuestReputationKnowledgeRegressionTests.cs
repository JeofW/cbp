using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using WholesomeAQ;

internal static class QuestReputationKnowledgeRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    [ModuleInitializer]
    internal static void Run()
    {
        using var authority = new QuestDataRepairPackRegressionTests.Fixture();
        string folder = Path.Combine(QuestTypedStrategyBehaviorRegressionTests.Root(), "runtime-snapshot/Bots/WholesomeAutoQuest-master/quest_data");
        byte[] original = File.ReadAllBytes(Path.Combine(folder,"quest_data.json"));
        var loader = new DataLoader(Path.Combine(folder,"quest_data.json")); var db = loader.Load();
        var cases = new List<(string Name,Action Test)>();
        foreach (var contract in new[] { (Id:9145, Min:3000), (Id:9155, Min:3000), (Id:9173, Min:9000), (Id:9192, Min:3000) })
        {
            var current = contract; var quest = db.Quests.Single(q => q.Id == current.Id);
            using var observation = JsonDocument.Parse("{\"reference_found\":false}");
            var context = typeof(QuestDatasetSimulationRegressionTests).GetMethod("BuildContext", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null,new object[] { db,quest,observation.RootElement.Clone(),true })!;
            bool Pickup(params (string Name,object? Value)[] overrides) => Schedule(context,overrides).Plan.Any(p => p.Quest.Id==current.Id && p.Stage==QuestWorkStage.Pickup);
            cases.Add(($"{current.Id}: exact source faction and threshold are present",()=>
                Check(quest.RequiredMinRepFaction==922 && quest.RequiredMinRepValue==current.Min,"required reputation metadata was omitted")));
            cases.Add(($"{current.Id}: unknown reputation blocks pickup",()=>
                Check(!Pickup(("omitReputations",true)),"missing observation authorized pickup")));
            cases.Add(($"{current.Id}: below threshold blocks pickup",()=>
                Check(!Pickup(("reputationOverride",new Dictionary<int,int>{{922,current.Min-1}})),"below-minimum reputation authorized pickup")));
            cases.Add(($"{current.Id}: exact threshold permits pickup",()=>
                Check(Pickup(("reputationOverride",new Dictionary<int,int>{{922,current.Min}})),"inclusive minimum incorrectly rejected")));
            cases.Add(($"{current.Id}: above threshold permits pickup",()=>
                Check(Pickup(("reputationOverride",new Dictionary<int,int>{{922,current.Min+1}})),"above-minimum reputation rejected")));
            cases.Add(($"{current.Id}: another faction cannot satisfy the requirement",()=>
                Check(!Pickup(("reputationOverride",new Dictionary<int,int>{{933,current.Min+1}})),"another faction authorized pickup")));
            cases.Add(($"{current.Id}: completion authority remains required",()=>
                Check(!Pickup(("authority",false),("reputationOverride",new Dictionary<int,int>{{922,current.Min}})),"reputation bypassed completed-history authority")));
            cases.Add(($"{current.Id}: accepted completed work keeps turn-in",()=>
            {
                var origin = db.QuestEnders.Where(e=>e.QuestId==current.Id).SelectMany(e=>
                    (e.EnderType==QuestObjectType.Creature ? db.CreatureSpawns : db.GameObjectSpawns)
                        .TryGetValue(e.EnderId.ToString(),out var points) ? points : Enumerable.Empty<SpawnPoint>()).First();
                Check(Schedule(context,("accepted",true),("complete",true),("omitReputations",true),("origin",origin))
                    .Plan.Any(p=>p.Quest.Id==current.Id && p.Stage==QuestWorkStage.TurnIn),"pickup reputation gate stranded authoritative completed turn-in");
            }));
        }
        cases.Add(("original dataset remains byte-identical",()=>Check(original.SequenceEqual(File.ReadAllBytes(Path.Combine(folder,"quest_data.json"))),"loader modified base data")));
        cases.Add(("source facts do not synthesize current reputation",()=>
            Check(new QuestSchedulerSnapshot().ReputationValues==null,"reference metadata became live observations")));
        int passed=0, failed=0, unexpected=0;
        foreach(var test in cases)
        {
            try {test.Test();passed++;Console.WriteLine("PASS reputation knowledge: "+test.Name);}
            catch(Failure e){failed++;Console.Error.WriteLine("FAIL reputation knowledge: "+test.Name+": "+e.Message);}
            catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR reputation knowledge: "+test.Name+": "+e);}
        }
        Console.WriteLine($"Reputation knowledge scenarios: {passed}/{cases.Count}; assertions={failed}; unexpected={unexpected}; actual tracked loader and scheduler; controlled observations, no game.");
        if(failed+unexpected!=0)throw new InvalidOperationException("Reputation knowledge regression");
    }
    private static QuestScheduleResult Schedule(object context,params (string Name,object? Value)[] overrides)
    {
        MethodInfo method=context.GetType().GetMethod("Schedule",Hidden)!;
        var changes=overrides.ToDictionary(p=>p.Name,p=>p.Value);
        object?[] args=method.GetParameters().Select(p=>changes.TryGetValue(p.Name!,out var value)?value:p.DefaultValue).ToArray();
        return (QuestScheduleResult)method.Invoke(context,args)!;
    }
    private static void Check(bool value,string message){if(!value)throw new Failure(message);}
}
