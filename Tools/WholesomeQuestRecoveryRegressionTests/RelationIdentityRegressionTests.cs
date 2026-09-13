using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;
using CoreType = Styx.Logic.Profiles.Quest.QuestObjectType;
using PickUpNode = Styx.Logic.Profiles.Quest.PickUpNode;
using TurnInNode = Styx.Logic.Profiles.Quest.TurnInNode;

// Executes the real scheduler -> generated XML -> host parser contract. No client.
internal static class RelationIdentityRegressionTests
{
    private static QuestEntry Quest(int id = 867) => new QuestEntry
    {
        Id = id, Name = "Typed relation fixture", MinLevel = 1, QuestLevel = 20,
        Objectives = new List<QuestObjective> { new QuestObjective { Type = ObjectiveType.TurnInOnly } }
    };
    private static SpawnPoint Point(double x) => new SpawnPoint { Map = 1, X = x, Y = 10, Z = 10 };
    private static QuestDatabase Database(QuestObjectType type, bool bothRelations = false)
    {
        var db = new QuestDatabase
        {
            Quests = new List<QuestEntry> { Quest() },
            CreatureSpawns = new Dictionary<string, List<SpawnPoint>> { ["77"] = new List<SpawnPoint> { Point(10) } },
            GameObjectSpawns = new Dictionary<string, List<SpawnPoint>> { ["77"] = new List<SpawnPoint> { Point(210) } }
        };
        foreach (var kind in bothRelations ? new[] { QuestObjectType.Creature, QuestObjectType.GameObject } : new[] { type })
        {
            db.QuestGivers.Add(new QuestGiverEntry { QuestId = 867, GiverId = 77, GiverName = "source", GiverType = kind });
            db.QuestEnders.Add(new QuestEnderEntry { QuestId = 867, EnderId = 77, EnderName = "source", EnderType = kind });
        }
        return db;
    }
    private static QuestScheduleResult Schedule(QuestDatabase db, QuestWorkStage stage, int range = 500,
        List<QuestAttemptOutcome>? failures = null, QuestSchedulerAcceptedQuest[]? accepted = null)
    {
        var snapshot = new QuestSchedulerSnapshot
        {
            UtcNow = new DateTime(2026,9,13,0,0,0,DateTimeKind.Utc), PlayerLevel = 20, PlayerRaceId = 1,
            MapId = 1, HasAuthoritativeCompletions = true,
            AcceptedQuests = accepted ?? (stage == QuestWorkStage.Pickup ? Array.Empty<QuestSchedulerAcceptedQuest>() :
                new[] { new QuestSchedulerAcceptedQuest { QuestId = 867, IsCompleted = true } })
        };
        return QuestScheduler.MaterializeSchedule(db, snapshot,
            _ => new QuestRecoveryDecision { State = QuestRecoveryState.Eligible, MayAttempt = true, Status = "eligible" },
            10, range, 7, navigationAssessment: _ => new SpawnNavigationAssessment { IsKnownSafe = true, IsKnownReachable = true },
            reportDataFailure: failure => failures?.Add(failure));
    }
    private static QuestObjectType TypeOf(QuestPlanEntry e) => e.Giver?.GiverType ?? e.Ender!.EnderType;
    private static void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }

    [ModuleInitializer]
    internal static void Run()
    {
        var tests = new List<(string Name, Action Run)>();
        void Test(string name, Action run) => tests.Add((name, run));
        foreach (var requestedStage in new[] { QuestWorkStage.Pickup, QuestWorkStage.TurnIn })
        {
            var stage = requestedStage;
            Test(stage + " object uses object coordinates despite equal creature entry", () =>
            {
                var r = Schedule(Database(QuestObjectType.GameObject), stage);
                Check(r.Plan.Count == 1 && r.Plan[0].Hotspots.All(p => p.X == 210), "borrowed creature coordinates");
            });
            Test(stage + " creature does not inherit object coordinates", () =>
            {
                var r = Schedule(Database(QuestObjectType.Creature), stage);
                Check(r.Plan.Count == 1 && r.Plan[0].Hotspots.All(p => p.X == 10), "creature control");
            });
            foreach (var requestedType in new[] { QuestObjectType.Creature, QuestObjectType.GameObject })
            {
                var type = requestedType;
                Test(stage + " missing " + type + " spawns do not fall back into another namespace", () =>
                {
                    var db = Database(type);
                    if (type == QuestObjectType.Creature) db.CreatureSpawns.Clear(); else db.GameObjectSpawns.Clear();
                    var failures = new List<QuestAttemptOutcome>();
                    var result = Schedule(db, stage, failures: failures);
                    Check(result.Plan.Count == 0 && failures.Count > 0, "wrong namespace fabricated a valid endpoint");
                });
                Test(stage + " " + type + " type survives real profile parsing", () =>
                {
                    var db = Database(type);
                    var plan = Schedule(db, stage);
                    string xml = new ProfileBuilder().BuildProfileXml(plan.Plan, db, "Fixture", "Fixture", 20);
                    var element = XDocument.Parse(xml).Descendants(stage == QuestWorkStage.Pickup ? "PickUp" : "TurnIn").Single();
                    CoreType? parsed = stage == QuestWorkStage.Pickup ? PickUpNode.FromXml(element).GiverType : TurnInNode.FromXml(element).TurnInType;
                    Check(parsed == (type == QuestObjectType.GameObject ? CoreType.GameObject : CoreType.Npc), "profile erased relation type");
                });
            }
            Test(stage + " same numeric ID in two namespaces retains two alternatives", () =>
            {
                var db = Database(QuestObjectType.Creature, bothRelations: true);
                var result = Schedule(db, stage);
                Check(result.Plan.Count == 2 && result.Plan.Select(TypeOf).Distinct().Count() == 2, "cross-type deduplication hid an alternative");
                foreach (var entry in result.Plan)
                    Check(entry.Hotspots.All(p => p.X == (TypeOf(entry) == QuestObjectType.Creature ? 10 : 210)), "alternative received another type's points");
            });
            Test(stage + " same-type duplicate still deduplicates", () =>
            {
                var db = Database(QuestObjectType.GameObject);
                db.QuestGivers.Add(db.QuestGivers[0]); db.QuestEnders.Add(db.QuestEnders[0]);
                Check(Schedule(db, stage).Plan.Count == 1, "same-type duplicate multiplies work");
            });
            Test(stage + " correct namespace range is authoritative", () =>
            {
                var db = Database(QuestObjectType.GameObject);
                Check(Schedule(db, stage, range: 100).Plan.Count == 0, "near creature hid out-of-range object");
            });
            Test(stage + " invalid type is not silently treated as a creature", () =>
                Check(Schedule(Database((QuestObjectType)99), stage).Plan.Count == 0, "invalid type generated work"));
            Test(stage + " valid object-only source remains supported", () =>
            {
                var db = Database(QuestObjectType.GameObject); db.CreatureSpawns.Clear();
                Check(Schedule(db, stage).Plan.Count == 1, "valid object control");
            });
        }
        Test("descendant namespace must not fabricate an ancestor-correction trigger", () =>
        {
            var db = Database(QuestObjectType.GameObject);
            db.Quests[0].PrevQuestID = 866;
            db.Quests.Insert(0, new QuestEntry { Id = 866, Name = "Ancestor", MinLevel = 1, QuestLevel = 20,
                Objectives = new List<QuestObjective> { new QuestObjective { Type = ObjectiveType.KillMob, MobId = 88, KillCount = 2, Index = 0 } } });
            db.CreatureSpawns["88"] = new List<SpawnPoint> { Point(30) };
            var accepted = new[] { new QuestSchedulerAcceptedQuest { QuestId = 866, ObjectiveCounts = new[] { 0 }, IsCompleted = false } };
            var result = Schedule(db, QuestWorkStage.Pickup, range: 100, accepted: accepted);
            Check(result.Plan.Count == 1 && result.Plan[0].Stage == QuestWorkStage.Objective, "unreachable typed descendant borrowed another namespace to force correction");
        });
        var errors = new List<string>();
        foreach (var t in tests)
        {
            try { t.Run(); Console.WriteLine("PASS relation identity: " + t.Name); }
            catch (Exception e) { errors.Add(t.Name + ": " + e.Message); Console.Error.WriteLine("FAIL relation identity: " + errors[errors.Count - 1]); }
        }
        Console.WriteLine($"Relation identity: {tests.Count-errors.Count}/{tests.Count}; actual scheduler/profile/parser, no client attached.");
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
    }
}
