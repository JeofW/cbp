using System.Runtime.CompilerServices;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;

internal static class OwnedCollectionPlanningRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var failures = new List<string>(); int scenarios=0;
        foreach (var kind in new[] {ObjectiveType.CollectItem, ObjectiveType.CollectFromGameObject})
        foreach (long quantity in new long[] {0,11,12,13})
        {
            scenarios++;
            try
            {
                // The first objective lacks a supported collection source. The second
                // has legitimate work, so the test checks the full published schedule.
                var quest = new QuestEntry
                {
                    Id=867, Name="Owned inventory fixture", MinLevel=1, QuestLevel=20,
                    Objectives =
                    {
                        new QuestObjective {Index=0, Type=kind, ItemId=5058, CollectCount=12},
                        new QuestObjective {Index=1, Type=ObjectiveType.KillMob, MobId=100, KillCount=2}
                    }
                };
                var db = new QuestDatabase
                {
                    Quests = new List<QuestEntry> {quest},
                    CreatureSpawns = new Dictionary<string,List<SpawnPoint>>
                    {
                        ["100"]=new() {new SpawnPoint {Map=1,X=10,Y=10,Z=0}}
                    }
                };
                var snapshot = new QuestSchedulerSnapshot
                {
                    UtcNow = new DateTime(2026,9,13,0,0,0,DateTimeKind.Utc), PlayerLevel=20,
                    PlayerRaceId=1, MapId=1, X=0,Y=0, HasAuthoritativeCompletions=false,
                    AcceptedQuests = new[] {new QuestSchedulerAcceptedQuest {QuestId=867,IsCompleted=false,ObjectiveCounts=new[] {0,0}}},
                    CarriedItemCounts = new Dictionary<int,long> {[5058]=quantity}
                };
                var dataFailures = new List<QuestAttemptOutcome>();
                var schedule = QuestScheduler.MaterializeSchedule(db,snapshot,
                    _ => new QuestRecoveryDecision {State=QuestRecoveryState.Eligible,MayAttempt=true,Status="eligible"},
                    10,1000,7,reportDataFailure:dataFailures.Add);
                bool rejectedSource=dataFailures.Any(f=>f.Reason==QuestFailureReason.UnsupportedObjective);
                if (rejectedSource != (quantity<12))
                    throw new InvalidOperationException("An already-satisfied item must not acquire a data-failure episode merely because its unused source is unsupported.");
                if (!schedule.Plan.Any(p=>p.ObjectiveIndex==1 && p.Stage==QuestWorkStage.Objective)
                    || schedule.Plan.Any(p=>p.ObjectiveIndex==0)
                    || schedule.Plan.Any(p=>p.Stage==QuestWorkStage.TurnIn))
                    throw new InvalidOperationException("Retain the other unfinished work; never fabricate whole-quest completion from one carried item.");
                Console.WriteLine($"PASS owned collection schedule: {kind}, quantity={quantity}");
            }
            catch(Exception e)
            {
                failures.Add($"{kind}, quantity={quantity}: {e.Message}");
                Console.Error.WriteLine("FAIL owned collection schedule: "+failures[^1]);
            }
        }
        Console.WriteLine($"Owned collection scheduling: {scenarios-failures.Count}/{scenarios}; actual full scheduler, no attached client.");
        if(failures.Count!=0)throw new InvalidOperationException(string.Join(Environment.NewLine,failures));
    }
}
