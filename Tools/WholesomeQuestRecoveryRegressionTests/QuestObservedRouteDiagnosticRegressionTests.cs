using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;

internal static class QuestObservedRouteDiagnosticRegressionTests
{
    private static readonly DateTime Now = new(2026, 9, 29, 11, 30, 0, DateTimeKind.Utc);
    private sealed class Failure(string message) : Exception(message) { }
    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new (string Name, Action Test)[]
        {
            ("item source survives successful pickup without a nearby NPC", () =>
            {
                var rows = Capture();
                Check(rows.Any(row => row.GetProperty("kind").GetString() == "item-starter"), "observed item source was not emitted");
                var quest = Quest(rows);
                Check(quest.GetProperty("finalReason").GetString() == "selected", "selected item route was lost");
                var relation = quest.GetProperty("relations")[0];
                Check(relation.GetProperty("objectType").GetString() == "Item" && relation.GetProperty("source").GetString() == "original-client:observed-item-starter", "item was presented as a stored NPC relation");
            }),
            ("below-minimum item quest remains a relevant rejected candidate", () =>
                Check(Quest(Capture(minimum: 61)).GetProperty("finalReason").GetString() == "below-min-level", "rejected item quest vanished")),
            ("unknown history is explicit for a carried starter", () =>
                Check(Quest(Capture(authority: false)).GetProperty("finalReason").GetString() == "completion-history-unknown", "unknown item history became an empty list")),
            ("item diagnostic retains physical and actor identities", () =>
            {
                var row = Row(Capture(), "item-starter");
                Check(row.GetProperty("itemGuid").GetString() == "0000000000000320" && row.GetProperty("questId").GetInt32() == 950001 &&
                    row.GetProperty("usableForPlanning").GetBoolean(), "item identity evidence was omitted");
            }),
            ("stale item observation remains visible but cannot authorize a route", () =>
            {
                var rows = Capture(stale: true);
                var row = Row(rows, "item-starter");
                Check(!row.GetProperty("usableForPlanning").GetBoolean(), "stale observation was called current");
                Check(Quest(rows).GetProperty("finalReason").GetString() == "no-giver-relations", "stale source invented a giver");
            }),
            ("accepted alias diagnostic identifies its client credit evidence", () =>
            {
                var rows = Capture(alias: true);
                var credit = Row(rows, "creature-credit");
                Check(credit.GetProperty("credit1").GetInt32() == 950010 && credit.GetProperty("entry").GetInt32() == 950030, "alias identity was rewritten");
                var quest = Quest(rows);
                Check(quest.GetProperty("objectiveGeometry").EnumerateArray().Any(value => value.GetProperty("effectiveSpawnCount").GetInt32() == 1), "actual alias geometry was omitted");
            })
        };
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var row in cases)
        {
            try { row.Test(); passed++; Console.WriteLine("PASS observed route diagnostic: " + row.Name); }
            catch (Failure error) { assertions++; Console.Error.WriteLine("FAIL observed route diagnostic: " + row.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR observed route diagnostic: " + row.Name + ": " + error); }
        }
        Console.WriteLine($"Observed route diagnostic scenarios: {passed}/{cases.Length}; assertions={assertions}; unexpected={unexpected}; actual materializer diagnostics.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Observed route diagnostic regression");
    }
    private static JsonElement Quest(JsonElement[] rows)
    {
        var matching = rows.Where(value => value.GetProperty("kind").GetString() == "quest").ToArray();
        Check(matching.Length == 1, "expected one relevant quest row; got " + matching.Length);
        return matching[0];
    }
    private static JsonElement Row(JsonElement[] rows, string kind)
    {
        var matching = rows.Where(value => value.GetProperty("kind").GetString() == kind).ToArray();
        Check(matching.Length == 1, "expected one " + kind + " row; got " + matching.Length);
        return matching[0];
    }
    private static JsonElement[] Capture(int minimum = 58, bool authority = true, bool stale = false, bool alias = false)
    {
        var db = new QuestDatabase { Quests = new() { new QuestEntry { Id = 950001, Name = "Observed route", MinLevel = minimum, QuestLevel = 62,
            Objectives = new() { new QuestObjective { Type = ObjectiveType.KillMob, MobId = 950010, KillCount = 3, Index = 0 } } } } };
        var lines = new List<string>();
        QuestScheduler.MaterializeSchedule(db, new QuestSchedulerSnapshot
        {
            UtcNow = Now, PlayerGuid = 77, PlayerLevel = 60, PlayerRaceId = 10, MapId = 530, X = 10, Y = 10, Z = 10,
            HasAuthoritativeCompletions = authority, CarriedItemCounts = new Dictionary<int, long> { [950020] = 1 },
            ItemStarters = alias ? Array.Empty<QuestItemStarterObservation>() : new[] { new QuestItemStarterObservation { QuestId = 950001, ItemEntry = 950020,
                ItemGuid = 800, PlayerGuid = 77, MapId = 530, ObservedUtc = stale ? Now.AddSeconds(-1) : Now } },
            CreatureCredits = alias ? new[] { new QuestCreatureCreditObservation { Entry = 950030, Credit1 = 950010, Guid = 900, PlayerGuid = 77,
                ObservedUtc = Now, MapId = 530, X = 12, Y = 10, Z = 10, AliveAttackableSelectable = true } } : Array.Empty<QuestCreatureCreditObservation>(),
            AcceptedQuests = alias ? new[] { new QuestSchedulerAcceptedQuest { QuestId = 950001, ObjectiveCounts = new int[4],
                NormalObjectiveIds = new[] { 950010, 0, 0, 0 }, NormalObjectiveRequiredCounts = new[] { 3, 0, 0, 0 } } } : Array.Empty<QuestSchedulerAcceptedQuest>()
        }, _ => new QuestRecoveryDecision { MayAttempt = true, State = QuestRecoveryState.Eligible }, 20, 250, 7,
            log: lines.Add, navigationAssessment: _ => new SpawnNavigationAssessment { IsKnownSafe = true, IsKnownReachable = true });
        return lines.Where(line => line.StartsWith("quest-audit ", StringComparison.Ordinal)).Select(line => JsonDocument.Parse(line.Substring(12)).RootElement.Clone()).ToArray();
    }
    private static void Check(bool value, string message) { if (!value) throw new Failure(message); }
}
