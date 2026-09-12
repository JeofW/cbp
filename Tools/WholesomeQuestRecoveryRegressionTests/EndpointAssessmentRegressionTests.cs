using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;

internal static class EndpointAssessmentRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new (string Name, Action Run)[]
        {
            ("an unreachable representative does not hide another floor", AlternateFloor),
            ("objective hotspots receive individual safety vetoes", () => IndividualVeto(QuestWorkStage.Objective)),
            ("pickup hotspots receive individual safety vetoes", () => IndividualVeto(QuestWorkStage.Pickup)),
            ("turn-in hotspots receive individual safety vetoes", () => IndividualVeto(QuestWorkStage.TurnIn)),
            ("explicit unreachable annotations are not inherited away", () => DataVeto(false)),
            ("explicit unsafe annotations are not inherited away", () => DataVeto(true)),
            ("unprobed floors retain unknown navigation evidence", UnknownFloor),
            ("same-coordinate records retain their own safety veto", RecordVeto),
            ("same-coordinate records retain their own safety score", RecordScore),
            ("blackspot-query failure cannot erase a known unsafe result", FailedSafetyProbe),
            ("per-scan native query budget does not grow with spawn density", QueryBudget),
            ("navigation cache does not cross map identity", MapIdentity),
            ("provider failures remain unknown and are memoized", ProviderFailure)
        };
        var failures = new List<string>();
        foreach (var test in cases)
        {
            try { test.Run(); Console.WriteLine("PASS: " + test.Name); }
            catch (Exception error)
            {
                failures.Add(test.Name + ": " + error.Message);
                Console.Error.WriteLine("FAIL: " + failures[failures.Count - 1]);
            }
        }
        Console.WriteLine($"Endpoint assessment scenarios: {cases.Length - failures.Count}/{cases.Length} passed. No game attached.");
        if (failures.Count != 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
    }

    private static SpawnPoint Point(double x = 10, double z = 0, int map = 1) =>
        new SpawnPoint { Map = map, X = x, Y = 10, Z = z };

    private static SpawnNavigationAssessment Safe(int score = 100) =>
        new SpawnNavigationAssessment { IsKnownSafe = true, IsKnownReachable = true, SafetyScore = score };

    // Call the real existing cache factory, not a model of its behavior. Reflection
    // keeps the production method private while testing evidence ownership directly.
    private static Func<SpawnPoint, SpawnNavigationAssessment> Assessor(
        Func<SpawnPoint, SpawnNavigationAssessment> probe,
        Func<SpawnPoint, bool>? unsafePoint = null) =>
        (Func<SpawnPoint, SpawnNavigationAssessment>)typeof(QuestScheduler)
            .GetMethod("CreateCachedNavigationAssessment", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object?[] { probe, unsafePoint })!;

    private static QuestScheduleResult Schedule(
        IEnumerable<SpawnPoint> points,
        QuestWorkStage stage,
        Func<SpawnPoint, SpawnNavigationAssessment> probe,
        Func<SpawnPoint, bool>? unsafePoint = null)
    {
        var quest = new QuestEntry
        {
            Id = 867, Name = "Endpoint evidence fixture", MinLevel = 1, QuestLevel = 20,
            Objectives = { new QuestObjective { Index = 0, Type = ObjectiveType.KillMob, MobId = 2000, KillCount = 2 } }
        };
        var db = new QuestDatabase
        {
            Quests = new List<QuestEntry> { quest },
            QuestGivers = new List<QuestGiverEntry> { new QuestGiverEntry { QuestId = 867, GiverId = 1001 } },
            QuestEnders = new List<QuestEnderEntry> { new QuestEnderEntry { QuestId = 867, EnderId = 1002 } },
            CreatureSpawns = new Dictionary<string, List<SpawnPoint>>
            {
                ["1001"] = points.ToList(), ["1002"] = points.ToList(), ["2000"] = points.ToList()
            }
        };
        var snapshot = new QuestSchedulerSnapshot
        {
            UtcNow = new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc),
            PlayerLevel = 20, PlayerRaceId = 1, MapId = 1, X = 0, Y = 0,
            HasAuthoritativeCompletions = true,
            AcceptedQuests = stage == QuestWorkStage.Pickup
                ? Array.Empty<QuestSchedulerAcceptedQuest>()
                : new[] { new QuestSchedulerAcceptedQuest
                {
                    QuestId = 867, IsCompleted = stage == QuestWorkStage.TurnIn,
                    ObjectiveCounts = new[] { stage == QuestWorkStage.TurnIn ? 2 : 0 }
                } }
        };
        return QuestScheduler.MaterializeSchedule(db, snapshot,
            _ => new QuestRecoveryDecision { State = QuestRecoveryState.Eligible, MayAttempt = true, Status = "eligible" },
            10, 1000, 7, isKnownUnsafe: unsafePoint, navigationAssessment: probe);
    }

    private static SpawnPoint[] Hotspots(QuestScheduleResult result) =>
        result.Plan.SelectMany(entry => entry.Hotspots).ToArray();

    private static void AlternateFloor()
    {
        foreach (bool reverse in new[] { false, true })
        {
            var points = new[] { Point(z: 0), Point(z: 30) };
            int calls = 0;
            var result = Schedule(reverse ? points.Reverse() : points, QuestWorkStage.Objective, point =>
            {
                calls++;
                return point.Z == 0
                    ? new SpawnNavigationAssessment { IsKnownSafe = true, IsKnownReachable = false }
                    : Safe();
            });
            var hotspots = Hotspots(result);
            Check(hotspots.Length == 1 && hotspots[0].Z == 30,
                "an unprobed alternate floor must remain eligible as unknown, not inherit the lower floor's rejection");
            Check(calls == 1, "fixing evidence scope must not multiply native path queries in one retry cell");
        }
    }

    private static void IndividualVeto(QuestWorkStage stage)
    {
        var result = Schedule(new[] { Point(10), Point(20) }, stage, _ => Safe(), point => point.X == 20);
        var hotspots = Hotspots(result);
        Check(result.Plan.Count == 1 && result.Plan[0].Stage == stage, "fixture must produce the requested quest stage");
        Check(hotspots.Length == 1 && hotspots[0].X == 10,
            "the unassessed second hotspot must not inherit permission from a safe cluster representative");
    }

    private static void DataVeto(bool unsafeAnnotation)
    {
        var blocked = Point(20);
        if (unsafeAnnotation) blocked.IsKnownSafe = false;
        else blocked.IsKnownReachable = false;
        var hotspots = Hotspots(Schedule(new[] { Point(10), blocked }, QuestWorkStage.Objective, _ => Safe()));
        Check(hotspots.Length == 1 && hotspots[0].X == 10, "each record's explicit negative annotation must be enforced before grouping");
    }

    private static void UnknownFloor()
    {
        int calls = 0;
        var assess = Assessor(_ => { calls++; return Safe(); });
        Check(assess(Point()).IsKnownReachable == true, "first point must receive actual probe evidence");
        var other = assess(Point(z: 30));
        Check(other.IsKnownSafe == null && other.IsKnownReachable == null,
            "a shared query-budget cell is not proof that another floor is safe or reachable");
        Check(calls == 1, "the existing per-cell query budget must be preserved");
    }

    private static void RecordVeto()
    {
        int calls = 0;
        var assess = Assessor(_ => { calls++; return Safe(); });
        assess(Point());
        var blocked = Point(); blocked.IsKnownSafe = false;
        Check(assess(blocked).IsKnownSafe == false, "memoized geometry must not bypass the current record's veto");
        Check(calls == 1, "an explicit veto should not require another path query");
    }

    private static void RecordScore()
    {
        int calls = 0;
        var assess = Assessor(_ => { calls++; return Safe(100); });
        var first = Point(); first.SafetyScore = 7;
        var second = Point(); second.SafetyScore = 11;
        Check(assess(first).SafetyScore == 107 && assess(second).SafetyScore == 111,
            "cache only live evidence, not the previous record's combined score");
        Check(calls == 1, "identical destinations must reuse the real probe");
    }

    private static void FailedSafetyProbe()
    {
        var assess = Assessor(_ => new SpawnNavigationAssessment { IsKnownSafe = false },
            _ => throw new InvalidOperationException("blackspot provider unavailable"));
        Check(assess(Point()).IsKnownSafe == false, "query failure must not erase a live unsafe veto");
        var blocked = Point(20); blocked.IsKnownSafe = false;
        Check(assess(blocked).IsKnownSafe == false, "query failure must not erase a data unsafe veto");
    }

    private static void QueryBudget()
    {
        int calls = 0;
        SpawnNavigationAssessment Probe(SpawnPoint _) { calls++; return Safe(); }
        var assess = Assessor(Probe);
        for (int repeat = 0; repeat < 2; repeat++)
            for (int index = 0; index < 70; index++)
            {
                assess(Point(index + 1, index));
                assess(Point(index + 161, index));
            }
        Check(calls == 2, "spawn density and height variation must not grow the two-cell native-query budget");
        Assessor(Probe)(Point());
        Check(calls == 3, "a new scan must acquire fresh navigation evidence");
    }

    private static void MapIdentity()
    {
        int calls = 0;
        var assess = Assessor(point => { calls++; return new SpawnNavigationAssessment { IsKnownReachable = point.Map == 1 }; });
        Check(assess(Point(map: 1)).IsKnownReachable == true && assess(Point(map: 0)).IsKnownReachable == false,
            "identical coordinates on different maps are different evidence");
        Check(calls == 2, "each map requires its own query");
    }

    private static void ProviderFailure()
    {
        int calls = 0;
        var assess = Assessor(_ => { calls++; throw new InvalidOperationException("path provider unavailable"); });
        foreach (var point in new[] { Point(), Point(), Point(z: 30) })
        {
            var result = assess(point);
            Check(result.IsKnownSafe == null && result.IsKnownReachable == null, "missing evidence must remain unknown");
        }
        Check(calls == 1, "provider failure must not create a repeated-probe loop");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
