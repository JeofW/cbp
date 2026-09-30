using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Bots.Quest.Objectives;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;

internal static class QuestCreditSourceGeometryRegressionTests
{
    private const int QuestId = 991001, CreditId = 991010, CreatureId = 991020;
    private static readonly DateTime Now = new(2026, 9, 30, 6, 0, 0, DateTimeKind.Utc);
    private sealed class Failure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        var tests = new List<(string Name, Action Test)>();
        void Case(string name, Action<QuestDataRepairPackRegressionTests.Fixture> action) =>
            tests.Add((name, () => { using var f = Fixture(); action(f); }));
        Case("bound source supplies search geometry without inventing a creature spawn", f =>
        {
            var db = Accepted(f);
            Check(Plan(db).Plan.Any(p => p.Stage == QuestWorkStage.Objective), "accepted credit has no source search route");
            Check(!db.CreatureSpawns.ContainsKey(CreditId.ToString()) && !db.CreatureSpawns.ContainsKey(CreatureId.ToString()),
                "producer search hints polluted global spawn identities");
            Check(db.Quests.Single().Objectives.Single().MobId == CreditId, "quest credit identity was rewritten");
        });
        Case("source bytes participate in execution identity", f =>
        {
            string before = f.Loader().ExecutionFingerprint; _ = Accepted(f);
            Check(before != f.Loader().ExecutionFingerprint, "source hints did not invalidate execution identity");
        });
        Case("base bytes remain unchanged and hint metadata is not deserialized from base JSON", f =>
        {
            var hint = f.Pack["ObjectiveCreditSources"]!.DeepClone();
            f.Pack.Remove("ObjectiveCreditSources");
            f.SetBase(n => n["ObjectiveCreditSources"] = hint);
            byte[] before = File.ReadAllBytes(f.DataPath); f.Write();
            var db = f.Load();
            Check(!Plan(db).Plan.Any(p => p.Stage == QuestWorkStage.Objective), "unvalidated base hint became executable geometry");
            Check(before.SequenceEqual(File.ReadAllBytes(f.DataPath)), "base dataset was modified");
        });
        Case("hint-only credit never supplies giver geometry", f =>
        {
            f.SetBase(n => n["QuestGivers"] = new JsonArray(new JsonObject { ["QuestId"] = QuestId,
                ["GiverId"] = CreditId, ["GiverType"] = 0, ["GiverName"] = "unobserved" }));
            Check(!Plan(Accepted(f), accepted: false).Plan.Any(p => p.Stage == QuestWorkStage.Pickup), "credit hint became a quest giver");
        });
        Case("missing native objective identity cannot use source hints", f =>
            Check(!Plan(Accepted(f), metadata: false).Plan.Any(p => p.Stage == QuestWorkStage.Objective), "hint substituted for native objective metadata"));
        Case("changed native required count cannot use source hints", f =>
            Check(!Plan(Accepted(f), required: 4).Plan.Any(p => p.Stage == QuestWorkStage.Objective), "hint ignored live required count"));
        Case("fulfilled objective does not search source hints", f =>
            Check(!Plan(Accepted(f), progress: 3).Plan.Any(p => p.Stage == QuestWorkStage.Objective), "completed credit was rescheduled"));
        Case("failed accepted quest does not search source hints", f =>
            Check(!Plan(Accepted(f), failed: true).Plan.Any(p => p.Stage == QuestWorkStage.Objective), "failed quest retained source work"));
        Case("source hint keeps recovery quarantine", f =>
            Check(!Plan(Accepted(f), blocked: true).Plan.Any(p => p.Stage == QuestWorkStage.Objective), "hint bypassed recovery"));
        Case("source hint keeps live navigation veto", f =>
            Check(!Plan(Accepted(f), reachable: false).Plan.Any(p => p.Stage == QuestWorkStage.Objective), "hint bypassed navigation"));
        Case("source hint keeps current map and three-dimensional scan bounds", f =>
        {
            var db = Accepted(f);
            Check(!Plan(db, map: 1).Plan.Any(p => p.Stage == QuestWorkStage.Objective), "other-map hint was selected");
            Check(!Plan(db, z: 9999).Plan.Any(p => p.Stage == QuestWorkStage.Objective), "another elevation was treated as nearby");
        });
        foreach (int entry in new[] { CreditId, CreatureId })
        {
            int bound = entry;
            Case("stored negative geometry survives for entry " + bound, f =>
            {
                f.SetBase(n => n["CreatureSpawns"]![bound.ToString()] = new JsonArray(new JsonObject
                { ["Map"] = 530, ["X"] = 12, ["Y"] = 10, ["Z"] = 10, ["IsKnownReachable"] = false }));
                Check(!Plan(Accepted(f)).Plan.Any(p => p.Stage == QuestWorkStage.Objective), "hint replaced a stored veto");
            });
        }
        Case("a changed effective objective cannot borrow an old hint", f =>
        {
            var db = Accepted(f); db.Quests.Single().Objectives.Single().MobId++;
            Check(!Plan(db).Plan.Any(p => p.Stage == QuestWorkStage.Objective), "changed objective retained old source hint");
        });
        Case("actual generated owner retains credit and acknowledges only progress", f =>
        {
            var db = Accepted(f); var quest = db.Quests.Single();
            var plans = Plan(db).Plan.Where(p => p.Stage == QuestWorkStage.Objective).ToArray();
            Check(plans.Length != 0, "source hint did not reach profile planning");
            string xml = new ProfileBuilder().BuildProfileXml(plans, db, "Fixture", "Fixture", 60);
            var node = XDocument.Parse(xml).Descendants("QuestOrder").Descendants("Objective").Single();
            Check((string?)node.Attribute("MobId") == CreditId.ToString(), "generated profile rewrote the required credit");
            using var game = new QuestDatasetObservationFixture();
            game.SetQuest(QuestId, quest.Name, 60, new[] { CreditId, 0, 0, 0 }, new[] { 3, 0, 0, 0 }, new int[6], new int[6]);
            game.SetAccepted(true); game.LoadProfile(xml); game.SetProgress(new int[4]);
            var owner = game.CreateObjective(Styx.Logic.Profiles.Quest.ObjectiveNode.FromXml(node));
            Check(owner.Objective is GrindObjective && !owner.IsDone, "source hint manufactured completion or wrong behavior");
            typeof(QuestObservedDatasetRoutesRegressionTests).GetMethod("CheckActualAliasTarget", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { (GrindObjective)owner.Objective, CreatureId, CreditId });
            game.SetProgress(new[] { 2, 0, 0, 0 }); Check(!owner.IsDone, "partial credit completed the owner");
            game.SetProgress(new[] { 3, 0, 0, 0 }); Check(owner.IsDone, "authoritative progress was not acknowledged");
            Check(!Plan(db, progress: 3).Plan.Any(p => p.Stage == QuestWorkStage.Objective), "acknowledged objective was repeated");
            game.ReleaseOwners();
        });
        Case("dataset simulation context retains the validated source catalog", f =>
        {
            var db = Accepted(f);
            using var observations = JsonDocument.Parse("{\"reference_found\":false}");
            var context = typeof(QuestDatasetSimulationRegressionTests).GetMethod("BuildContext", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { db, db.Quests.Single(), observations.RootElement.Clone(), true })!;
            var copied = (QuestDatabase)context.GetType().GetField("Database", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context)!;
            Check(copied.ObjectiveCreditSources.Count == db.ObjectiveCreditSources.Count, "comparison context silently dropped validated hints");
        });
        Case("comparison model explicitly exports validated catalog outside the base schema", f =>
        {
            var db = Accepted(f);
            var method = typeof(QuestDatasetSimulationRegressionTests).GetMethod("SerializeEffectiveModel", BindingFlags.NonPublic | BindingFlags.Static);
            Check(method != null, "comparison model has no explicit validated-source export");
            using var exported = JsonDocument.Parse((string)method!.Invoke(null, new object[] { db })!);
            Check(exported.RootElement.TryGetProperty("ObjectiveCreditSources", out var sources) && sources.GetArrayLength() == 1
                && sources[0].GetProperty("CreditId").GetInt32() == CreditId, "validated catalog missing from effective evidence");
            Check(!JsonSerializer.Serialize(db).Contains("ObjectiveCreditSources", StringComparison.Ordinal), "base-schema exclusion was removed");
        });
        foreach (string fault in new[] { "quest", "row", "index", "credit", "count", "same-entry", "field", "map", "duplicate", "duplicate-point", "script", "item", "unknown-field", "empty-points", "navigation-authority", "missing-source" })
        {
            string current = fault;
            Case("reject " + current, f =>
            {
                var row = f.Pack["ObjectiveCreditSources"]![0]!;
                switch (current)
                {
                    case "quest": row["QuestId"] = QuestId + 1; break;
                    case "row": row["RowIndex"] = 1; break;
                    case "index": row["ObjectiveIndex"] = 1; break;
                    case "credit": row["CreditId"] = CreditId + 1; break;
                    case "count": row["RequiredCount"] = 4; break;
                    case "same-entry": row["CreatureId"] = CreditId; break;
                    case "field": row["CreditField"] = "GameObjectId"; break;
                    case "map": row["Points"]![0]!["Map"] = -1; break;
                    case "duplicate": f.Pack["ObjectiveCreditSources"]!.AsArray().Add(row.DeepClone()); break;
                    case "duplicate-point": row["Points"]!.AsArray().Add(row["Points"]![0]!.DeepClone()); break;
                    case "script": f.SetBase(n => n["Quests"]![0]!["SpecialFlags"] = 32); break;
                    case "item": f.SetBase(n => n["Quests"]![0]!["Objectives"]![0]!["ItemId"] = 1); break;
                    case "unknown-field": row["CastSpell"] = 1; break;
                    case "empty-points": row["Points"] = new JsonArray(); break;
                    case "navigation-authority": row["Points"]![0]!["IsKnownReachable"] = true; break;
                    case "missing-source": row["SourceRef"] = ""; break;
                }
                f.Write(); bool rejected = false;
                try { _ = f.Load(); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "invalid credit-source contract was accepted");
            });
        }
        int passed = 0, failed = 0, unexpected = 0;
        foreach (var test in tests)
        {
            try { test.Test(); passed++; Console.WriteLine("PASS credit source: " + test.Name); }
            catch (Failure error) { failed++; Console.Error.WriteLine("FAIL credit source: " + test.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR credit source: " + test.Name + ": " + error); }
        }
        Console.WriteLine($"Credit source scenarios: {passed}/{tests.Count}; assertions={failed}; unexpected={unexpected}; actual loader/scheduler/profile/progress; controlled observations, no game.");
        if (failed + unexpected != 0) throw new InvalidOperationException("Credit source geometry regression");
    }

    private static QuestDataRepairPackRegressionTests.Fixture Fixture()
    {
        var f = new QuestDataRepairPackRegressionTests.Fixture();
        f.SetBase(n =>
        {
            n["Quests"]![0]!["StartItem"] = 0; n["Quests"]![0]!["MinLevel"] = 58; n["Quests"]![0]!["QuestLevel"] = 62;
            n["Quests"]![0]!["Objectives"] = new JsonArray(new JsonObject { ["Type"] = (int)ObjectiveType.KillMob,
                ["Index"] = 0, ["MobId"] = CreditId, ["GameObjectId"] = 0, ["ItemId"] = 0, ["KillCount"] = 3, ["CollectCount"] = 0 });
        });
        f.Pack["QuestMetadata"] = new JsonArray(); f.Pack["SpawnAdditions"] = new JsonArray();
        f.Pack["ObjectiveCreditSources"] = new JsonArray(new JsonObject { ["QuestId"] = QuestId, ["RowIndex"] = 0,
            ["ObjectiveIndex"] = 0, ["CreditId"] = CreditId, ["RequiredCount"] = 3, ["CreatureId"] = CreatureId,
            ["CreditField"] = "KillCredit1", ["SourceRef"] = "controlled://creature_template/991020/KillCredit1",
            ["Points"] = new JsonArray(new JsonObject { ["Map"] = 530, ["X"] = 12, ["Y"] = 10, ["Z"] = 10 }) });
        return f;
    }
    private static QuestDatabase Accepted(QuestDataRepairPackRegressionTests.Fixture f)
    {
        f.Write();
        try { return f.Load(); } catch (InvalidDataException e) { throw new Failure("bound source contract rejected: " + e.Message); }
    }
    private static QuestScheduleResult Plan(QuestDatabase db, bool accepted = true, bool metadata = true, int required = 3,
        int progress = 0, bool failed = false, bool blocked = false, bool reachable = true, int map = 530, double z = 10) =>
        QuestScheduler.MaterializeSchedule(db, new QuestSchedulerSnapshot
        {
            UtcNow = Now, PlayerGuid = 77, PlayerLevel = 60, PlayerRaceId = 10, PlayerClassId = 2,
            MapId = map, X = 10, Y = 10, Z = z, HasCompleteQuestLog = true, HasAuthoritativeCompletions = true,
            CompletedQuestIds = Array.Empty<uint>(), CarriedItemCounts = new Dictionary<int, long>(),
            AcceptedQuests = accepted ? new[] { new QuestSchedulerAcceptedQuest { QuestId = QuestId, IsFailed = failed,
                ObjectiveCounts = new[] { progress, 0, 0, 0 }, NormalObjectiveIds = metadata ? new[] { CreditId, 0, 0, 0 } : null,
                NormalObjectiveRequiredCounts = metadata ? new[] { required, 0, 0, 0 } : null } } : Array.Empty<QuestSchedulerAcceptedQuest>()
        }, _ => new QuestRecoveryDecision { MayAttempt = !blocked, State = blocked ? QuestRecoveryState.Quarantined : QuestRecoveryState.Eligible },
        20, 250, 7, navigationAssessment: _ => new SpawnNavigationAssessment { IsKnownSafe = true, IsKnownReachable = reachable });
    private static void Check(bool value, string message) { if (!value) throw new Failure(message); }
}
