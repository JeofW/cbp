using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;

internal static class QuestFailedAndStrategyAdmissionRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, Action Test)>();
        foreach (bool complete in new[] { false, true })
        {
            bool isComplete = complete;
            cases.Add(($"failed accepted quest with complete={complete} produces no objective or turn-in", () =>
            {
                var failures = new List<QuestAttemptOutcome>(); int navigation = 0;
                var result = Plan(Db(), accepted: true, failed: true, complete: isComplete,
                    failures: failures, probe: () => navigation++);
                Check(result.Plan.Count == 0, "a failed quest was scheduled for execution");
                Check(navigation == 0 && failures.Count == 0, "failed state became an endpoint probe or invalid-data quarantine");
            }));
        }
        cases.Add(("cleared failure with fresh observations resumes the objective", () =>
        {
            var db = Db(); var failed = Plan(db, accepted: true, failed: true);
            var resumed = Plan(db, accepted: true);
            Check(failed.Plan.Count == 0 && resumed.Plan.Any(value => value.Stage == QuestWorkStage.Objective),
                "failure handling either admitted old work or retained a permanent exclusion");
        }));
        cases.Add(("failed accepted work does not block a separate eligible pickup", () =>
        {
            var db = Db(); var second = Db().Quests[0]; second.Id = 940002;
            db.Quests.Add(second); db.QuestGivers.Add(new QuestGiverEntry { QuestId = second.Id, GiverId = 940010 });
            var result = Plan(db, accepted: true, failed: true);
            Check(result.Plan.All(value => value.Quest.Id != 940001) && result.Plan.Any(value => value.Quest.Id == 940002),
                "failed work contaminated an independent quest");
        }));
        foreach (string kind in new[] { "UseItemOn", "GossipEvent" })
        {
            string recipeKind = kind;
            cases.Add(($"bound {kind} cast recipe permits pickup and emits its declared behavior", () => WithPack(recipeKind, (db, pack) =>
            {
                Check(Plan(db, pack: pack).Plan.Any(value => value.Stage == QuestWorkStage.Pickup),
                    "pickup ignored a valid strategy accepted by the execution materializer");
                var active = Plan(db, pack: pack, accepted: true);
                Check(active.Plan.Any(value => value.Stage == QuestWorkStage.Objective), "accepted source-bound recipe did not execute");
                var xml = XDocument.Parse(new ProfileBuilder().BuildProfileXml(active.Plan, db, "Fixture", "Fixture", 60, null, pack));
                Check(xml.Descendants("CustomBehavior").Any(value => ((string?)value.Attribute("File") ?? "").Contains(recipeKind, StringComparison.Ordinal)),
                    "strategy pickup produced no matching behavior recipe");
                Check(!xml.Descendants("Objective").Any(value => (string?)value.Attribute("Type") == "KillMob"),
                    "cast recipe silently reverted to ordinary killing");
            })));
        }
        cases.Add(("missing strategy pack does not invent cast actions", () =>
        {
            var db = Db(); db.Quests[0].SpecialFlags = 32;
            Check(Plan(db).Plan.Count == 0, "missing recipes admitted cast work");
        }));
        cases.Add(("event-flagged quest needs a whole-quest strategy before pickup", () =>
        {
            var db = Db(); db.Quests[0].SpecialFlags = 2;
            Check(Plan(db).Plan.Count == 0, "ordinary kill rows hid required event credit during pickup");
        }));
        cases.Add(("event-flagged turn-in-only row cannot invent the missing event", () =>
        {
            var db = Db(); db.Quests[0].SpecialFlags = 2;
            db.Quests[0].Objectives = new() { new QuestObjective { Type = ObjectiveType.TurnInOnly, Index = 0 } };
            Check(Plan(db).Plan.Count == 0, "turn-in-only data authorized an unimplemented event");
        }));
        cases.Add(("accepted event quest retains independent ordinary work", () =>
        {
            var db = Db(); db.Quests[0].SpecialFlags = 2;
            Check(Plan(db, accepted: true).Plan.Any(value => value.Stage == QuestWorkStage.Objective), "event admission restriction erased accepted ordinary work");
        }));
        cases.Add(("server-completed event quest retains turn-in", () =>
        {
            var db = Db(); db.Quests[0].SpecialFlags = 2;
            Check(Plan(db, accepted: true, complete: true).Plan.Any(value => value.Stage == QuestWorkStage.TurnIn), "event admission restriction erased authoritative turn-in");
        }));
        cases.Add(("bound whole-quest gossip recipe can satisfy the declared event", () => WithPack("GossipEvent", (db, pack) =>
            Check(Plan(db, pack: pack).Plan.Any(value => value.Stage == QuestWorkStage.Pickup), "supported bound event strategy was rejected"), specialFlags: 2)));
        foreach (string kind in new[] { "Escort", "UseItemOn" })
        {
            string recipeKind = kind;
            cases.Add(($"unsupported {kind} target/materializer defers pickup", () => WithPack(recipeKind, (db, pack) =>
                Check(Plan(db, pack: pack).Plan.Count == 0, "unsupported recipe was picked up"), wrongTarget: kind == "UseItemOn")));
        }
        cases.Add(("unsupported declared recipe overrides an otherwise ordinary objective", () => WithPack("Escort", (db, pack) =>
        {
            db.Quests[0].SpecialFlags = 0;
            Check(Plan(db, pack: pack).Plan.Count == 0, "pickup ignored an explicit recipe the objective owner must reject");
        })));
        cases.Add(("failed strategy quest does not become recipe execution", () => WithPack("UseItemOn", (db, pack) =>
            Check(Plan(db, pack: pack, accepted: true, failed: true).Plan.Count == 0, "failure bypassed the recipe owner"))));
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { item.Test(); passed++; Console.WriteLine("PASS quest state/strategy: " + item.Name); }
            catch (Failure error) { assertions++; Console.Error.WriteLine("FAIL quest state/strategy: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR quest state/strategy: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Quest state/strategy scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual loader/materializer/profile; controlled recipes; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Quest failed/strategy admission regression");
    }

    private static QuestDatabase Db() => new()
    {
        Quests = new() { new QuestEntry { Id = 940001, Name = "Controlled state", MinLevel = 58, QuestLevel = 62,
            Objectives = new() { new QuestObjective { Type = ObjectiveType.KillMob, MobId = 940010, KillCount = 2, Index = 0 } } } },
        QuestGivers = new() { new QuestGiverEntry { QuestId = 940001, GiverId = 940010, GiverName = "Giver" } },
        QuestEnders = new() { new QuestEnderEntry { QuestId = 940001, EnderId = 940010, EnderName = "Ender" } },
        CreatureSpawns = new() { ["940010"] = new() { new SpawnPoint { Map = 530, X = 10, Y = 10, Z = 10 } } }
    };
    private static QuestScheduleResult Plan(QuestDatabase db, QuestStrategyPack? pack = null, bool accepted = false,
        bool failed = false, bool complete = false, List<QuestAttemptOutcome>? failures = null, Action? probe = null)
    {
        var snapshot = new QuestSchedulerSnapshot
        {
            UtcNow = new DateTime(2026, 9, 29, 8, 28, 0, DateTimeKind.Utc), PlayerLevel = 60, PlayerRaceId = 10,
            MapId = 530, HasAuthoritativeCompletions = true,
            AcceptedQuests = accepted ? new[] { new QuestSchedulerAcceptedQuest { QuestId = 940001,
                IsFailed = failed, IsCompleted = complete, ObjectiveCounts = new[] { 0, 0, 0, 0 },
                NormalObjectiveIds = new[] { 940010, 0, 0, 0 }, NormalObjectiveRequiredCounts = new[] { 2, 0, 0, 0 } } } : Array.Empty<QuestSchedulerAcceptedQuest>()
        };
        Func<QuestRecoveryKey, QuestRecoveryDecision> evaluate = _ => new QuestRecoveryDecision
            { State = QuestRecoveryState.Eligible, MayAttempt = true, Status = "controlled" };
        Func<SpawnPoint, SpawnNavigationAssessment> navigation = _ =>
            { probe?.Invoke(); return new SpawnNavigationAssessment { IsKnownSafe = true, IsKnownReachable = true }; };
        Action<QuestAttemptOutcome> report = value => failures?.Add(value);
        try
        {
            return (QuestScheduleResult)typeof(QuestScheduler).GetMethod("MaterializeScheduleCore", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object?[] { db, snapshot, evaluate, 20, 250, 7, null, null, null, null, navigation, report, pack })!;
        }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }

    private static void WithPack(string kind, Action<QuestDatabase, QuestStrategyPack> test, bool wrongTarget = false, int specialFlags = 32)
    {
        string directory = Path.Combine(Path.GetTempPath(), "quest-admission-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var db = Db(); db.Quests[0].SpecialFlags = specialFlags;
            byte[] data = JsonSerializer.SerializeToUtf8Bytes(db);
            string dataPath = Path.Combine(directory, "quest_data.json"); File.WriteAllBytes(dataPath, data);
            var recipe = new Dictionary<string, object>
            {
                ["QuestId"] = 940001, ["ObjectiveIndex"] = 0, ["Kind"] = kind,
                ["SourceRef"] = "controlled://admission/not-a-live-quest-recipe", ["TargetType"] = "Creature",
                ["TargetId"] = wrongTarget ? 940099 : 940010, ["Range"] = 4, ["RequireLos"] = true,
                ["MaxAttempts"] = 2, ["SuccessEvidence"] = "QuestComplete"
            };
            if (kind == "UseItemOn") { recipe["ItemId"] = 999; recipe["TargetState"] = "Alive"; }
            if (kind == "GossipEvent") recipe["GossipOptionIndex"] = 1;
            File.WriteAllBytes(Path.Combine(directory, "quest_strategies.json"), JsonSerializer.SerializeToUtf8Bytes(new
            {
                Schema = "quest-strategy-pack-335-v1", ClientBuild = 12340,
                QuestDataSha256 = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(),
                SourceKind = "curated-profile", SourceRevision = "controlled-admission-fixture-v1", Recipes = new[] { recipe }
            }));
            // The actual strategy loader validates byte binding and recipe fields.
            // Avoid publishing a synthetic prerequisite graph into a shared test process.
            var pack = QuestStrategyPackLoader.Load(Path.Combine(directory, "quest_strategies.json"),
                Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant());
            test(db, pack);
        }
        finally { Directory.Delete(directory, true); }
    }
    private static void Check(bool value, string reason) { if (!value) throw new Failure(reason); }
}
