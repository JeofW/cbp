using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;

// Regression for the level-60 Hellfire report, using the real materializer.
// MinLevel is admission; QuestLevel is difficulty/XP and the user's low-level filter.
// This fixture supplies observations. It does not certify a realm's quest offer.
internal static class QuestPickupLevelRegressionTests
{
    private sealed class AssertionFailure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        var tests = new List<(string Name, Action Test)>();
        foreach (int questLevel in new[] { 60, 61, 62, 65 })
        {
            int level = questLevel;
            tests.Add(($"MinLevel 58 permits level 60 to request QuestLevel {level}", () =>
                Check(Pickup(Plan(Q(level, 58))), "MinLevel-eligible quest was excluded by its XP level")));
        }
        tests.Add(("exact minimum level admits a higher quest level", () =>
            Check(Pickup(Plan(Q(62, 60))), "exact MinLevel was rejected")));
        tests.Add(("below minimum level remains excluded", () =>
            Check(!Pickup(Plan(Q(62, 61))), "minimum pickup level was bypassed")));
        tests.Add(("configured low-level filter remains a preference", () =>
            Check(!Pickup(Plan(Q(52, 1))), "the seven-level low-level preference was removed")));
        tests.Add(("exact low-level preference boundary remains eligible", () =>
            Check(Pickup(Plan(Q(53, 1))), "the configured low-level boundary changed")));
        tests.Add(("scaled quest level uses its declared minimum", () =>
            Check(Pickup(Plan(Q(-1, 60))), "scaled quest was rejected")));
        tests.Add(("unknown completion history still defers new pickups", () =>
            Check(!Pickup(Plan(Q(62, 58), authority: false)), "unknown history authorized a pickup")));
        tests.Add(("race mismatch remains excluded", () =>
        {
            var quest = Q(62, 58); quest.AllowableRaces = 1;
            Check(!Pickup(Plan(quest)), "Alliance-only quest admitted a Blood Elf");
        }));
        tests.Add(("rewarded quest remains excluded", () =>
            Check(!Pickup(Plan(Q(62, 58), completed: new uint[] { 900001 })), "rewarded quest was republished")));
        tests.Add(("relation quarantine remains authoritative", () =>
            Check(!Pickup(Plan(Q(62, 58), quarantine: true)), "level repair bypassed recovery")));
        tests.Add(("higher-level descendant triggers accepted ancestor correction", () =>
        {
            var child = Q(62, 58); child.PrevQuestID = 900002;
            var parent = Q(60, 58); parent.Id = 900002;
            parent.Objectives = new() { new QuestObjective { Type = ObjectiveType.KillMob, MobId = 900020, KillCount = 3, Index = 0 } };
            var result = Plan(child, parent: parent);
            Check(result.Plan.Any(entry => entry.Quest.Id == parent.Id && entry.Stage == QuestWorkStage.AncestorCorrection),
                "the ancestor-correction admission path still used QuestLevel as a ceiling");
        }));

        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var test in tests)
        {
            try { test.Test(); passed++; Console.WriteLine("PASS pickup level: " + test.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL pickup level: " + test.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR pickup level: " + test.Name + ": " + error); }
        }
        Console.WriteLine($"Pickup level scenarios: {passed}/{tests.Count}; assertions={assertions}; unexpected={unexpected}; actual scheduler; controlled observations; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Pickup level regression");
    }

    private static QuestEntry Q(int questLevel, int minLevel) => new()
    {
        Id = 900001, Name = "Controlled Hellfire eligibility", QuestLevel = questLevel, MinLevel = minLevel,
        Objectives = new() { new QuestObjective { Type = ObjectiveType.TurnInOnly, Index = 0 } }
    };

    private static bool Pickup(QuestScheduleResult result) => result.Plan.Any(entry =>
        entry.Quest.Id == 900001 && entry.Stage == QuestWorkStage.Pickup);

    private static QuestScheduleResult Plan(QuestEntry quest, bool authority = true, bool quarantine = false,
        uint[]? completed = null, QuestEntry? parent = null)
    {
        var point = new SpawnPoint { Map = 530, X = 5, Y = 5, Z = 10 };
        var database = new QuestDatabase
        {
            Quests = parent == null ? new() { quest } : new() { quest, parent },
            QuestGivers = new() { new QuestGiverEntry { QuestId = quest.Id, GiverId = 900010, GiverName = "Controlled nearby giver", GiverType = QuestObjectType.Creature } },
            CreatureSpawns = new() { ["900010"] = new() { point }, ["900020"] = new() { point } }
        };
        var snapshot = new QuestSchedulerSnapshot
        {
            UtcNow = new DateTime(2026, 9, 29, 8, 28, 0, DateTimeKind.Utc),
            PlayerLevel = 60, PlayerRaceId = 10, MapId = 530,
            HasCompleteQuestLog = true, HasAuthoritativeCompletions = authority,
            CompletedQuestIds = completed ?? Array.Empty<uint>(),
            AcceptedQuests = parent == null ? Array.Empty<QuestSchedulerAcceptedQuest>() : new[]
            {
                new QuestSchedulerAcceptedQuest { QuestId = (uint)parent.Id, ObjectiveCounts = new[] { 0, 0, 0, 0 },
                    NormalObjectiveIds = new[] { 900020, 0, 0, 0 }, NormalObjectiveRequiredCounts = new[] { 3, 0, 0, 0 } }
            }
        };
        return QuestScheduler.MaterializeSchedule(database, snapshot, key => new QuestRecoveryDecision
        {
            State = quarantine && key.Scope == QuestRecoveryScope.NpcRelation ? QuestRecoveryState.Quarantined : QuestRecoveryState.Eligible,
            MayAttempt = !(quarantine && key.Scope == QuestRecoveryScope.NpcRelation),
            RetryUtc = quarantine ? snapshot.UtcNow.AddHours(1) : null, Status = "controlled"
        }, 20, 250, 7, navigationAssessment: _ => new SpawnNavigationAssessment { IsKnownSafe = true, IsKnownReachable = true });
    }

    private static void Check(bool valid, string reason)
    {
        if (!valid) throw new AssertionFailure(reason);
    }
}
