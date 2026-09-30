using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;

internal static class QuestDiagnosticBoundaryRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }
    private static readonly DateTime Now = new(2026, 9, 29, 8, 28, 0, DateTimeKind.Utc);
    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new (string Name, Action Test)[]
        {
            ("known unsafe is the exact final navigation reason", () => Reason("navigation-known-unsafe", Capture(safe: false))),
            ("known unreachable is the exact final navigation reason", () => Reason("navigation-known-unreachable", Capture(reachable: false))),
            ("nearby giver without database relations is explicit", () =>
            {
                var row = Capture(noRelations: true).Single(value => value.GetProperty("kind").GetString() == "giver");
                Check(row.TryGetProperty("relationStatus", out var status) && status.GetString() == "no-database-giver-relations", "visible giver without relations has no explanation");
            }),
            ("large spawn collections are bounded without dropping the quest", () =>
            {
                var row = Quest(Capture(manySpawns: true));
                var relation = row.GetProperty("relations")[0];
                Check(relation.GetProperty("effectiveSpawns").GetArrayLength() <= 8, "one diagnostic row emitted an unbounded spawn collection");
                Check(relation.TryGetProperty("effectiveSpawnCount", out var count) && count.GetInt32() == 100, "bounded geometry hid its total count");
            }),
            ("reward-ready quest reports ender geometry", () =>
            {
                var row = Quest(Capture(acceptedComplete: true));
                Check(row.TryGetProperty("relationRole", out var role) && role.GetString() == "ender", "turn-in diagnostic described giver geometry");
                Check(row.GetProperty("relations")[0].GetProperty("entry").GetInt32() == 970011, "turn-in diagnostic did not use its actual ender");
            }),
            ("minimum-level rejection does not pretend prerequisites were evaluated", () =>
            {
                var gates = Quest(Capture(minLevel: 61)).GetProperty("gates");
                Check(gates.TryGetProperty("admissionDecisions", out var decisions) && !decisions.TryGetProperty("prerequisitesComplete", out _),
                    "a skipped admission gate became an invented result");
            }),
            ("missing prerequisite has a recorded exact gate result", () =>
            {
                var row = Quest(Capture(prerequisite: 99)); Reason("prerequisites-not-satisfied", new[] { row });
                var gates = row.GetProperty("gates");
                Check(gates.TryGetProperty("admissionDecisions", out var decisions) && decisions.TryGetProperty("prerequisitesComplete", out var done) && !done.GetBoolean(),
                    "final prerequisite rejection omitted the evaluated result");
            }),
            ("available nearby status is retained with exact live/static delta", () =>
            {
                var row = Capture().Single(value => value.GetProperty("kind").GetString() == "giver");
                Check(row.GetProperty("rawQuestStatus").GetUInt32() == 7 && row.GetProperty("hasAvailableQuest").GetBoolean(), "loaded availability observation was lost");
                Check(row.GetProperty("liveStaticDelta3D").GetDouble() == 0, "live/static geometry mismatch");
            })
        };
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var test in cases)
        {
            try { test.Test(); passed++; Console.WriteLine("PASS diagnostic boundary: " + test.Name); }
            catch (Failure error) { assertions++; Console.Error.WriteLine("FAIL diagnostic boundary: " + test.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR diagnostic boundary: " + test.Name + ": " + error); }
        }
        Console.WriteLine($"Diagnostic boundary scenarios: {passed}/{cases.Length}; assertions={assertions}; unexpected={unexpected}; actual materializer and captured decisions.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Diagnostic boundary regression");
    }
    private static JsonElement Quest(JsonElement[] rows) => rows.Single(row => row.GetProperty("kind").GetString() == "quest" && row.GetProperty("questId").GetInt32() == 970001);
    private static void Reason(string reason, JsonElement[] rows) => Check(Quest(rows).GetProperty("finalReason").GetString() == reason, "expected final reason " + reason);
    private static JsonElement[] Capture(bool? safe = true, bool? reachable = true, bool noRelations = false,
        bool manySpawns = false, bool acceptedComplete = false, int minLevel = 1, int prerequisite = 0)
    {
        var quest = new QuestEntry { Id = 970001, Name = "Diagnostic boundary", MinLevel = minLevel, QuestLevel = 60, PrevQuestID = prerequisite,
            Objectives = new() { new QuestObjective { Type = ObjectiveType.TurnInOnly, Index = 0 } } };
        var db = new QuestDatabase
        {
            Quests = new() { quest },
            QuestGivers = noRelations ? new() : new() { new QuestGiverEntry { QuestId = quest.Id, GiverId = 970010, GiverType = QuestObjectType.Creature } },
            QuestEnders = new() { new QuestEnderEntry { QuestId = quest.Id, EnderId = 970011, EnderType = QuestObjectType.Creature } },
            CreatureSpawns = new() {
                ["970010"] = Enumerable.Range(0, manySpawns ? 100 : 1).Select(index => new SpawnPoint { Map = 530, X = 10 + index, Y = 0, Z = 10 }).ToList(),
                ["970011"] = new() { new SpawnPoint { Map = 530, X = 30, Y = 0, Z = 10 } } }
        };
        var lines = new List<string>();
        QuestScheduler.MaterializeSchedule(db, new QuestSchedulerSnapshot
        {
            UtcNow = Now, PlayerGuid = 77, PlayerLevel = 60, PlayerRaceId = 10, PlayerClassId = 2, MapId = 530, Z = 10,
            HasAuthoritativeCompletions = true,
            AcceptedQuests = acceptedComplete ? new[] { new QuestSchedulerAcceptedQuest { QuestId = 970001, IsCompleted = true } } : Array.Empty<QuestSchedulerAcceptedQuest>(),
            NearbyQuestGivers = manySpawns ? Array.Empty<QuestGiverObservation>() : new[] { new QuestGiverObservation { Entry = 970010,
                ObjectType = QuestObjectType.Creature, Guid = 8801, PlayerGuid = 77, ObservedUtc = Now, MapId = 530, X = 10, Z = 10,
                RawQuestStatus = 7, HasAvailableQuest = true, IsQuestGiver = true, QuestStatusEvidence = "controlled raw dialog status" } }
        }, _ => new QuestRecoveryDecision { State = QuestRecoveryState.Eligible, MayAttempt = true }, manySpawns ? 0 : 20, 250, 7,
            log: lines.Add, navigationAssessment: _ => new SpawnNavigationAssessment { IsKnownSafe = safe, IsKnownReachable = reachable });
        return lines.Where(line => line.StartsWith("quest-audit ")).Select(line => JsonDocument.Parse(line.Substring(12)).RootElement.Clone()).ToArray();
    }
    private static void Check(bool value, string reason) { if (!value) throw new Failure(reason); }
}
