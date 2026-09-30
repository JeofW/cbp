using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;

internal static class QuestSpatialRangeRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }
    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, Action Test)>();
        foreach (var work in new[] { QuestWorkStage.Pickup, QuestWorkStage.Objective, QuestWorkStage.TurnIn })
        {
            var stage = work;
            cases.Add(($"{stage}: distant floor is outside the world-space radius", () =>
            {
                var result = Schedule(stage, new SpawnPoint { Map = 1, X = 3, Y = 4, Z = 300 }, 250, true);
                Check(!result.Result.Plan.Any(p => p.Stage == stage), "vertical separation was discarded");
                Check(result.Result.Status.Contains("outside-scan-radius", StringComparison.Ordinal), "wrong rejection reason");
                Check(result.Failures == 0 && result.Probes == 0, "out-of-range geometry was quarantined or probed");
            }));
            cases.Add(($"{stage}: expanded radius retains a reachable different floor", () =>
                Check(Schedule(stage, new SpawnPoint { Map = 1, X = 3, Y = 4, Z = 300 }, 500, true).Result.Plan.Any(p => p.Stage == stage),
                    "a reachable floor remained permanently excluded after radius expansion")));
            cases.Add(($"{stage}: unknown elevation is not an in-range point", () =>
                Check(!Schedule(stage, new SpawnPoint { Map = 1, X = 3, Y = 4, Z = double.NaN }, 250, true).Result.Plan.Any(p => p.Stage == stage),
                    "unknown elevation authorized work")));
            cases.Add(($"{stage}: exact three-dimensional boundary remains inclusive", () =>
            {
                var point = new SpawnPoint { Map = 1, X = 3, Y = 4, Z = 12 };
                Check(!Schedule(stage, point, 12, true).Result.Plan.Any(p => p.Stage == stage), "point beyond radius was admitted");
                Check(Schedule(stage, point, 13, true).Result.Plan.Any(p => p.Stage == stage), "point exactly on radius was rejected");
            }));
            cases.Add(($"{stage}: radius does not replace navigation authority", () =>
                Check(!Schedule(stage, new SpawnPoint { Map = 1, X = 3, Y = 4, Z = 12 }, 250, false).Result.Plan.Any(p => p.Stage == stage),
                    "in-range point bypassed an unreachable result")));
        }
        int passed = 0, failed = 0, errors = 0;
        foreach (var test in cases)
        {
            try { test.Test(); passed++; Console.WriteLine("PASS spatial range: " + test.Name); }
            catch (Failure error) { failed++; Console.Error.WriteLine("FAIL spatial range: " + test.Name + ": " + error.Message); }
            catch (Exception error) { errors++; Console.Error.WriteLine("ERROR spatial range: " + test.Name + ": " + error); }
        }
        Console.WriteLine($"Spatial range scenarios: {passed}/{cases.Count}; assertions={failed}; unexpected={errors}; actual scheduler; no game.");
        if (failed + errors != 0) throw new InvalidOperationException("Quest spatial range regression");
    }

    private static (QuestScheduleResult Result, int Failures, int Probes) Schedule(QuestWorkStage stage, SpawnPoint point, int radius, bool reachable)
    {
        const int id = 991080, mob = 991081;
        var db = new QuestDatabase
        {
            Quests = new() { new QuestEntry { Id = id, Name = "World-space range fixture", MinLevel = 1, QuestLevel = 20,
                Objectives = new() { new QuestObjective { Type = ObjectiveType.KillMob, Index = 0, MobId = mob, KillCount = 2 } } } },
            QuestGivers = new() { new QuestGiverEntry { QuestId = id, GiverId = 991082 } },
            QuestEnders = new() { new QuestEnderEntry { QuestId = id, EnderId = 991083 } },
            CreatureSpawns = new() { [mob.ToString()] = new() { point }, ["991082"] = new() { point }, ["991083"] = new() { point } }
        };
        var snapshot = new QuestSchedulerSnapshot
        {
            UtcNow = new DateTime(2026, 9, 30, 6, 0, 0, DateTimeKind.Utc), PlayerLevel = 20, PlayerRaceId = 1,
            MapId = 1, X = 0, Y = 0, Z = 0, HasCompleteQuestLog = true, HasAuthoritativeCompletions = true,
            AcceptedQuests = stage == QuestWorkStage.Pickup ? Array.Empty<QuestSchedulerAcceptedQuest>() :
                new[] { new QuestSchedulerAcceptedQuest { QuestId = id, IsCompleted = stage == QuestWorkStage.TurnIn,
                    ObjectiveCounts = new[] { stage == QuestWorkStage.TurnIn ? 2 : 0, 0, 0, 0 },
                    NormalObjectiveIds = new[] { mob, 0, 0, 0 }, NormalObjectiveRequiredCounts = new[] { 2, 0, 0, 0 } } }
        };
        int failures = 0, probes = 0;
        var result = QuestScheduler.MaterializeSchedule(db, snapshot,
            _ => new QuestRecoveryDecision { MayAttempt = true, State = QuestRecoveryState.Eligible }, 10, radius, 7,
            reportDataFailure: _ => failures++, navigationAssessment: _ =>
            { probes++; return new SpawnNavigationAssessment { IsKnownSafe = true, IsKnownReachable = reachable }; });
        return (result, failures, probes);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Failure(message); }
}
