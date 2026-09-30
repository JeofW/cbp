using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;

internal static class QuestAdmissionDiagnosticRegressionTests
{
    private sealed class AssertionFailure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        var tests = new List<(string Name, Action Test)>
        {
            ("all twenty-four nearby rejected quests survive the activity-text limit", () =>
            {
                var rows = Capture();
                var quests = rows.Where(row => row.GetProperty("kind").GetString() == "quest").ToArray();
                Check(quests.Length == 24, "structured diagnostics hid nearby candidates beyond twelve exclusions");
                Check(quests.Select(row => row.GetProperty("questId").GetInt32()).OrderBy(id => id)
                    .SequenceEqual(Enumerable.Range(910000, 24)), "a nearby quest identity was omitted or duplicated");
                Check(quests.All(row => row.GetProperty("finalReason").GetString() == "below-min-level"),
                    "diagnostics did not report the actual minimum-level rejection");
            }),
            ("player and completion authority are recorded", () =>
            {
                var row = Capture().SingleOrDefault(item => item.GetProperty("kind").GetString() == "player");
                Check(row.ValueKind == JsonValueKind.Object, "player diagnostic is absent");
                Check(row.GetProperty("level").GetInt32() == 60 && row.GetProperty("raceId").GetInt32() == 10 &&
                    row.GetProperty("mapId").GetInt32() == 530 && row.GetProperty("hasCompleteQuestLog").GetBoolean() &&
                    row.GetProperty("hasAuthoritativeCompletions").GetBoolean(), "player/log authority evidence was changed");
            }),
            ("unknown history is the final admission barrier", () =>
            {
                var quests = Capture(authority: false).Where(row => row.GetProperty("kind").GetString() == "quest").ToArray();
                Check(quests.Length == 24 && quests.All(row => row.GetProperty("finalReason").GetString() == "completion-history-unknown"),
                    "unknown history was presented as an empty completed set or hidden");
            }),
            ("incomplete quest-log early return retains diagnostics", () =>
            {
                var quests = Capture(completeLog: false).Where(row => row.GetProperty("kind").GetString() == "quest").ToArray();
                Check(quests.Length == 24 && quests.All(row => row.GetProperty("finalReason").GetString() == "quest-log-incomplete"),
                    "the early quest-log barrier lost nearby rejection evidence");
            }),
            ("recovery exclusions retain scope state and retry", () =>
            {
                var rows = Capture(minLevel: 1, quarantine: true);
                var quests = rows.Where(row => row.GetProperty("kind").GetString() == "quest").ToArray();
                Check(quests.Length == 24 && quests.All(row => row.GetProperty("finalReason").GetString() == "recovery-blocked"),
                    "recovery was not identified as the final barrier");
                Check(quests.All(row => row.GetProperty("recovery").EnumerateArray().Any(value =>
                    value.GetProperty("scope").GetString() == "NpcRelation" && value.GetProperty("state").GetString() == "Quarantined" &&
                    value.GetProperty("retryUtc").ValueKind == JsonValueKind.String)), "recovery owner evidence was omitted");
            }),
            ("diagnostic rows are valid single-line JSON with bounded names", () =>
            {
                var rows = Capture(name: "Quest\n\"quoted\"\r" + new string('x', 10000));
                Check(rows.Length >= 26, "structured snapshot is absent");
                Check(rows.Where(row => row.GetProperty("kind").GetString() == "quest")
                    .All(row => row.GetProperty("name").GetString()!.Length <= 512), "unbounded imported names entered diagnostics");
            })
        };
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var test in tests)
        {
            try { test.Test(); passed++; Console.WriteLine("PASS admission diagnostic: " + test.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL admission diagnostic: " + test.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR admission diagnostic: " + test.Name + ": " + error); }
        }
        Console.WriteLine($"Admission diagnostic scenarios: {passed}/{tests.Count}; assertions={assertions}; unexpected={unexpected}; actual materializer; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Admission diagnostic regression");
    }

    private static JsonElement[] Capture(bool authority = true, bool completeLog = true, int minLevel = 61,
        bool quarantine = false, string name = "Nearby quest")
    {
        var database = new QuestDatabase
        {
            Quests = Enumerable.Range(910000, 24).Select(id => new QuestEntry
            {
                Id = id, Name = name, MinLevel = minLevel, QuestLevel = 60,
                Objectives = new() { new QuestObjective { Type = ObjectiveType.TurnInOnly, Index = 0 } }
            }).ToList(),
            QuestGivers = Enumerable.Range(910000, 24).Select(id => new QuestGiverEntry
            { QuestId = id, GiverId = 910100, GiverType = QuestObjectType.Creature, GiverName = "Nearby giver" }).ToList(),
            CreatureSpawns = new() { ["910100"] = new() { new SpawnPoint { Map = 530, X = 1, Y = 2, Z = 3 } } }
        };
        var snapshot = new QuestSchedulerSnapshot
        {
            UtcNow = new DateTime(2026, 9, 29, 8, 28, 0, DateTimeKind.Utc), PlayerLevel = 60, PlayerRaceId = 10, MapId = 530,
            HasCompleteQuestLog = completeLog, HasAuthoritativeCompletions = authority
        };
        var lines = new List<string>();
        QuestScheduler.MaterializeSchedule(database, snapshot, key => new QuestRecoveryDecision
        {
            State = quarantine && key.Scope == QuestRecoveryScope.NpcRelation ? QuestRecoveryState.Quarantined : QuestRecoveryState.Eligible,
            MayAttempt = !(quarantine && key.Scope == QuestRecoveryScope.NpcRelation),
            RetryUtc = quarantine ? snapshot.UtcNow.AddHours(1) : null, Status = "controlled"
        }, 20, 250, 7, log: lines.Add,
            navigationAssessment: _ => new SpawnNavigationAssessment { IsKnownSafe = true, IsKnownReachable = true });
        return lines.Where(line => line.StartsWith("quest-audit ", StringComparison.Ordinal)).Select(line =>
        {
            using var document = JsonDocument.Parse(line.Substring("quest-audit ".Length));
            return document.RootElement.Clone();
        }).ToArray();
    }

    private static void Check(bool valid, string reason)
    {
        if (!valid) throw new AssertionFailure(reason);
    }
}
