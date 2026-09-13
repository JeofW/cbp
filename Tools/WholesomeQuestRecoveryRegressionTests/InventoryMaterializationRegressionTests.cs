using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;

// Characterizes existing carried-item behavior through the public scheduler.
// It does not simulate the client inventory reader or server quest completion.
internal static class InventoryMaterializationRegressionTests
{
    private static QuestObjective Item(int index = 0, int id = 500, ObjectiveType kind = ObjectiveType.CollectItem) =>
        new QuestObjective { Index = index, ItemId = id, Type = kind, CollectCount = 12, MobId = 2000, GameObjectId = 3000 };
    private static Dictionary<int,long> Bag(long count, int id = 500) => new Dictionary<int,long> { [id] = count };
    private static QuestScheduleResult Plan(QuestObjective[] objectives, IReadOnlyDictionary<int,long>? bag,
        int[]? counts = null, bool complete = false, bool geometry = true, List<QuestAttemptOutcome>? failures = null)
    {
        var point = new SpawnPoint { Map = 1, X = 10, Y = 10, Z = 0 };
        var db = new QuestDatabase
        {
            Quests = new List<QuestEntry> { new QuestEntry { Id = 867, Name = "Inventory fixture", MinLevel = 1,
                QuestLevel = 20, Objectives = objectives.ToList() } },
            QuestGivers = new List<QuestGiverEntry> { new QuestGiverEntry { QuestId = 867, GiverId = 1001 } },
            QuestEnders = new List<QuestEnderEntry> { new QuestEnderEntry { QuestId = 867, EnderId = 1002 } },
            CreatureSpawns = new Dictionary<string,List<SpawnPoint>>
                { ["1001"] = new List<SpawnPoint> { point }, ["1002"] = new List<SpawnPoint> { point } }
        };
        if (geometry)
        {
            db.CreatureSpawns["2000"] = new List<SpawnPoint> { point };
            db.GameObjectSpawns["3000"] = new List<SpawnPoint> { point };
        }
        var snapshot = new QuestSchedulerSnapshot
        {
            UtcNow = new DateTime(2026,9,13,0,0,0,DateTimeKind.Utc), PlayerLevel = 20, PlayerRaceId = 1,
            MapId = 1, HasAuthoritativeCompletions = true, CarriedItemCounts = bag,
            AcceptedQuests = new[] { new QuestSchedulerAcceptedQuest { QuestId = 867, IsCompleted = complete,
                ObjectiveCounts = counts ?? Array.Empty<int>() } }
        };
        return QuestScheduler.MaterializeSchedule(db, snapshot,
            _ => new QuestRecoveryDecision { State = QuestRecoveryState.Eligible, MayAttempt = true, Status = "eligible" },
            10, 1000, 7, navigationAssessment: _ => new SpawnNavigationAssessment { IsKnownSafe = true, IsKnownReachable = true },
            reportDataFailure: outcome => failures?.Add(outcome));
    }
    private static int Work(QuestScheduleResult plan) => plan.Plan.Count(p => p.Stage == QuestWorkStage.Objective);
    [ModuleInitializer]
    internal static void Run()
    {
        var tests = new List<(string Name, Action Run)>();
        void Test(string n, Action r) => tests.Add((n,r));
        void Check(bool v,string m) { if (!v) throw new InvalidOperationException(m); }
        foreach (var type in new[] { ObjectiveType.CollectItem, ObjectiveType.CollectFromGameObject })
        {
            var kind = type;
            Test(type + " quantity boundaries override unrelated index counters", () =>
            {
                foreach (long count in new long[] { 0, 1, 11, 12, 13, long.MaxValue })
                    Check(Work(Plan(new[] { Item(kind: kind) }, Bag(count), new[] { 99 })) == (count < 12 ? 1 : 0),
                        "incorrect carried-quantity decision at " + count);
            });
        }
        Test("alternative sources share the same actual item identity", () =>
            Check(Work(Plan(new[] { Item(), Item(1, kind: ObjectiveType.CollectFromGameObject) }, Bag(12))) == 0,
                "a second source was scheduled for an already-carried item"));
        Test("unrelated carried items cannot satisfy the objective", () =>
            Check(Work(Plan(new[] { Item() }, Bag(999, 501), new[] { 99 })) == 1, "unrelated item suppressed collection"));
        Test("known-empty inventory is distinct from unknown inventory", () =>
        {
            Check(Work(Plan(new[] { Item() }, new Dictionary<int,long>(), new[] { 12 })) == 1, "known absence ignored");
            Check(Work(Plan(new[] { Item() }, null, new[] { 12 })) == 0, "unknown inventory lost valid counter fallback");
        });
        Test("inventory loss causes fresh materialization to restore work", () =>
        {
            Check(Work(Plan(new[] { Item() }, Bag(12))) == 0, "setup collection not suppressed");
            Check(Work(Plan(new[] { Item() }, Bag(11))) == 1, "previous scan's completion leaked");
        });
        Test("a satisfied item needs no source geometry or data quarantine", () =>
        {
            var failures = new List<QuestAttemptOutcome>();
            var result = Plan(new[] { Item() }, Bag(12), geometry:false, failures:failures);
            Check(Work(result) == 0 && failures.Count == 0, "completed item reported missing source geometry");
        });
        Test("actual completed quest selects turn-in despite empty inventory", () =>
        {
            var result = Plan(new[] { Item() }, Bag(0), complete:true);
            Check(result.Plan.Count == 1 && result.Plan[0].Stage == QuestWorkStage.TurnIn, "server completion lost authority");
        });
        Test("item possession alone does not fabricate whole-quest completion", () =>
            Check(!Plan(new[] { Item() }, Bag(12)).Plan.Any(p => p.Stage == QuestWorkStage.TurnIn),
                "possession was promoted into unobserved quest completion"));
        Test("mixed item objectives retain only unsatisfied identities", () =>
        {
            var result = Plan(new[] { Item(), Item(1,501) }, Bag(12));
            Check(result.Plan.Count == 1 && result.Plan[0].ObjectiveIndex == 1, "wrong remaining item objective");
        });
        Test("object interaction without an item retains live-counter completion", () =>
            Check(Work(Plan(new[] { Item(id:0,kind:ObjectiveType.CollectFromGameObject) }, Bag(0), new[] { 12 })) == 0,
                "non-item interaction was treated as missing bag contents"));
        Test("unknown inventory and missing live counters remain incomplete", () =>
            Check(Work(Plan(new[] { Item() }, null)) == 1, "missing observations fabricated completion"));
        var errors = new List<string>();
        foreach (var test in tests)
        {
            try { test.Run(); Console.WriteLine("PASS inventory materialization: " + test.Name); }
            catch (Exception e) { errors.Add(test.Name+": "+e.Message); Console.Error.WriteLine("FAIL inventory materialization: "+errors[errors.Count-1]); }
        }
        Console.WriteLine($"Inventory materialization: {tests.Count-errors.Count}/{tests.Count}; 12 quantity rows; actual scheduler, no client attached.");
        if (errors.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine,errors));
    }
}
