using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Styx.Logic.Questing.Recovery;
using Styx.WoWInternals.WoWObjects;
using WholesomeAQ;

internal static class QuestLogCompletenessRegressionTests
{
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static QuestDatabase Database() => new QuestDatabase
    {
        Quests = new List<QuestEntry> { new QuestEntry { Id=867, Name="observation", MinLevel=1, QuestLevel=20, Objectives=new List<QuestObjective>{ new QuestObjective{Type=ObjectiveType.TurnInOnly} } } },
        QuestGivers = new List<QuestGiverEntry> { new QuestGiverEntry { QuestId=867, GiverId=77, GiverType=QuestObjectType.Creature } },
        QuestEnders = new List<QuestEnderEntry> { new QuestEnderEntry { QuestId=867, EnderId=77, EnderType=QuestObjectType.Creature } },
        CreatureSpawns = new Dictionary<string,List<SpawnPoint>> { ["77"] = new List<SpawnPoint>{ new SpawnPoint{Map=1,X=10,Y=10,Z=10} } }
    };
    private static QuestSchedulerSnapshot Snapshot() => new QuestSchedulerSnapshot { UtcNow=new DateTime(2026,9,14,0,0,0,DateTimeKind.Utc),PlayerLevel=20,PlayerRaceId=1,MapId=1,HasAuthoritativeCompletions=true };
    private static void Set(object value, string name, object? contents)
    {
        var p=value.GetType().GetProperty(name);
        Check(p!=null,"missing scheduler observation contract "+name);
        p!.SetValue(value,contents);
    }
    private static QuestScheduleResult Schedule(QuestSchedulerSnapshot snapshot, List<uint> completed, List<QuestAttemptOutcome> failures, Action? evaluate=null, string? grind=null) => QuestScheduler.MaterializeSchedule(Database(),snapshot,
        _=>{evaluate?.Invoke();return new QuestRecoveryDecision{State=QuestRecoveryState.Eligible,MayAttempt=true};},10,500,7,grind,
        id=>completed.Add(id),navigationAssessment:_=>new SpawnNavigationAssessment{IsKnownSafe=true,IsKnownReachable=true},reportDataFailure:failures.Add);
    [ModuleInitializer]
    internal static void Run()
    {
        var tests=new List<(string,Action)>();
        tests.Add(("null accepted observations must not become an empty log",()=>{
            var s=Snapshot();Set(s,"AcceptedQuests",null);var marks=new List<uint>();var errors=new List<QuestAttemptOutcome>();
            var r=Schedule(s,marks,errors);Check(r.Selected.Count==0 && r.FallbackMode==QuestFallbackMode.TimedIdle,"null log fabricated pickup work");
        }));
        tests.Add(("incomplete log defers before recovery side effects",()=>{
            var s=Snapshot();Set(s,"HasCompleteQuestLog",false);Set(s,"CompletedQuestIds",new uint[]{999});int evaluations=0;var marks=new List<uint>();var errors=new List<QuestAttemptOutcome>();
            var r=Schedule(s,marks,errors,()=>evaluations++);Check(r.Selected.Count==0 && r.FallbackMode==QuestFallbackMode.TimedIdle && evaluations==0 && marks.Count==0 && errors.Count==0,"incomplete log changed recovery or selected work");
        }));
        tests.Add(("incomplete snapshot must not activate a grind fallback",()=>{
            var s=Snapshot();Set(s,"HasCompleteQuestLog",false);var r=Schedule(s,new(),new(),grind:"already-vetted.xml");
            Check(r.FallbackMode==QuestFallbackMode.TimedIdle && r.Selected.Count==0,"uncertainty activated fallback execution");
        }));
        tests.Add(("complete empty snapshot retains normal pickup",()=>{
            var r=Schedule(Snapshot(),new(),new());Check(r.Selected.Count==1 && r.Selected[0].Stage==QuestWorkStage.Pickup,"working pickup changed");
        }));
        tests.Add(("production scan with unavailable live owner becomes timed idle",()=>{
            var loader=new DataLoader();typeof(DataLoader).GetField("_database",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(loader,Database());
            var scheduler=new QuestScheduler(loader,new ProfileBuilder(),new WholesomeAQSettings());
            bool built=scheduler.ScanAndRefresh(new LocalPlayer(0));
            Check(!built && scheduler.LastSchedule.FallbackMode==QuestFallbackMode.TimedIdle && scheduler.CurrentProfilePath==null,"unavailable log did not safely defer publication");
        }));
        int failed=0;
        foreach(var test in tests){try{test.Item2();Console.WriteLine("PASS scheduler observation: "+test.Item1);}catch(Exception ex){failed++;Console.Error.WriteLine("FAIL scheduler observation: "+test.Item1+": "+ex);}}
        Console.WriteLine($"Scheduler observation scenarios: {tests.Count-failed}/{tests.Count}; actual full scheduler and host; no client attached.");
        if(failed!=0)throw new InvalidOperationException("Scheduler observation regressions: "+failed);
    }
}
