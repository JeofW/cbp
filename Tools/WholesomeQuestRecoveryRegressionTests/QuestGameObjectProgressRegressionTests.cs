using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;

internal static class QuestGameObjectProgressRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }
    [ModuleInitializer]
    internal static void Run()
    {
        int go = unchecked((int)0x80000000) | 950010;
        var cases = new (string Name, Action Test)[]
        {
            ("matching sparse GO credit suppresses completed work", () => Check(!Work(new[] { 0, 0, go, 0 }, new[] { 0, 0, 3, 0 }, new[] { 0, 0, 3, 0 }), "completed GO credit was rescheduled")),
            ("same-numbered creature cannot complete a GO", () => Check(Work(new[] { 950010, 0, 0, 0 }, new[] { 3, 0, 0, 0 }, new[] { 3, 0, 0, 0 }), "creature credit crossed namespace")),
            ("partial GO credit remains work", () => Check(Work(new[] { 0, go, 0, 0 }, new[] { 0, 3, 0, 0 }, new[] { 0, 2, 0, 0 }), "partial GO credit was completed")),
            ("other raw slot cannot complete a GO", () => Check(Work(new[] { 0, 0, go, 0 }, new[] { 0, 0, 3, 0 }, new[] { 3, 0, 0, 0 }), "GO borrowed a displayed-index counter")),
            ("changed GO requirement remains incomplete", () => Check(Work(new[] { go, 0, 0, 0 }, new[] { 4, 0, 0, 0 }, new[] { 3, 0, 0, 0 }), "changed requirement was ignored")),
            ("duplicate GO identity stays ambiguous", () => Check(Work(new[] { go, go, 0, 0 }, new[] { 3, 3, 0, 0 }, new[] { 3, 3, 0, 0 }), "duplicate normal identity was accepted")),
            ("GO collection still requires carried items", () => Check(Work(new[] { go, 0, 0, 0 }, new[] { 3, 0, 0, 0 }, new[] { 3, 0, 0, 0 }, itemId: 950020, items: 0), "GO use credit replaced carried-item evidence")),
            ("GO collection acknowledges carried count", () => Check(!Work(new int[4], new int[4], new int[4], itemId: 950020, items: 3), "carried GO collection was rescheduled")),
            ("unknown GO metadata remains pending", () => Check(Work(Array.Empty<int>(), Array.Empty<int>(), new[] { 3, 0, 0, 0 }), "unknown metadata fell back to dataset index"))
        };
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { item.Test(); passed++; Console.WriteLine("PASS GO progress: " + item.Name); }
            catch (Failure error) { assertions++; Console.Error.WriteLine("FAIL GO progress: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR GO progress: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"GO progress scenarios: {passed}/{cases.Length}; assertions={assertions}; unexpected={unexpected}; actual scheduler; controlled raw metadata.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("GO progress regression");
    }
    private static bool Work(int[] ids, int[] required, int[] counts, int itemId = 0, int items = 0)
    {
        var db = new QuestDatabase
        {
            Quests = new() { new QuestEntry { Id = 950001, Name = "GO counter fixture", MinLevel = 1, QuestLevel = 1,
                Objectives = new() { new QuestObjective { Type = ObjectiveType.CollectFromGameObject,
                    GameObjectId = 950010, ItemId = itemId, CollectCount = 3, Index = 0 } } } },
            GameObjectSpawns = new() { ["950010"] = new() { new SpawnPoint { Map = 530, X = 10, Y = 10, Z = 10 } } }
        };
        return QuestScheduler.MaterializeSchedule(db, new QuestSchedulerSnapshot
        {
            UtcNow = DateTime.UtcNow, PlayerLevel = 60, PlayerRaceId = 10, MapId = 530, HasAuthoritativeCompletions = true,
            AcceptedQuests = new[] { new QuestSchedulerAcceptedQuest { QuestId = 950001,
                NormalObjectiveIds = ids, NormalObjectiveRequiredCounts = required, ObjectiveCounts = counts } },
            CarriedItemCounts = new Dictionary<int, long> { [950020] = items }
        }, _ => new QuestRecoveryDecision { State = QuestRecoveryState.Eligible, MayAttempt = true }, 20, 250, 80,
            navigationAssessment: _ => new SpawnNavigationAssessment { IsKnownSafe = true, IsKnownReachable = true })
            .Plan.Any(value => value.Quest.Id == 950001 && value.Stage == QuestWorkStage.Objective);
    }
    private static void Check(bool value, string reason) { if (!value) throw new Failure(reason); }
}
