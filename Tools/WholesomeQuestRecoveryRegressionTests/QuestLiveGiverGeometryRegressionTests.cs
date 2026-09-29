using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;

internal static class QuestLiveGiverGeometryRegressionTests
{
    private static readonly DateTime Now = new(2026, 9, 29, 8, 28, 0, DateTimeKind.Utc);
    private sealed class AssertionFailure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        var tests = new List<(string Name, Action Test)>();
        foreach (var type in new[] { QuestObjectType.Creature, QuestObjectType.GameObject })
        {
            var relationType = type;
            tests.Add(($"{type} loaded giver supplies missing stored coordinates", () =>
                Check(Pickup(Plan(relationType, Array.Empty<SpawnPoint>(), new[] { Live(relationType) })), "live geometry was ignored")));
            tests.Add(($"{type} loaded giver supersedes stale far coordinates", () =>
                Check(Pickup(Plan(relationType, new[] { Point(x: 9000) }, new[] { Live(relationType) })), "stale coordinates hid a nearby giver")));
            tests.Add(($"{type} loaded giver supersedes a stored map mismatch", () =>
                Check(Pickup(Plan(relationType, new[] { Point(map: 1) }, new[] { Live(relationType) })), "stored map hid a current-map giver")));
            tests.Add(($"{type} loaded Z replaces an obsolete floor", () =>
            {
                var result = Plan(relationType, new[] { Point(z: 2000) }, new[] { Live(relationType) });
                Check(Pickup(result) && result.Plan.SelectMany(entry => entry.Hotspots).All(point => point.Z == 10),
                    "the plan retained a stale vertical endpoint instead of the observed giver");
            }));
            tests.Add(($"{type} missing-static observation does not record invalid data", () =>
            {
                var failures = new List<QuestAttemptOutcome>();
                Plan(relationType, Array.Empty<SpawnPoint>(), new[] { Live(relationType) }, failures: failures);
                Check(failures.Count == 0, "a present loaded giver was quarantined for an absent stored spawn");
            }));
            tests.Add(($"{type} correct live namespace cannot borrow the other type", () =>
                Check(!Pickup(Plan(relationType, Array.Empty<SpawnPoint>(), new[] { Live(relationType == QuestObjectType.Creature ? QuestObjectType.GameObject : QuestObjectType.Creature) })),
                    "live coordinates crossed the creature/gameobject namespace")));
        }
        tests.Add(("a stale sample cannot authorize geometry", () =>
            Check(!Pickup(Plan(live: new[] { Live(time: Now.AddSeconds(-1)) })), "a previous scan was reused")));
        tests.Add(("a future sample cannot authorize geometry", () =>
            Check(!Pickup(Plan(live: new[] { Live(time: Now.AddSeconds(1)) })), "a future observation was accepted")));
        tests.Add(("another actor cannot donate a giver observation", () =>
            Check(!Pickup(Plan(live: new[] { Live(player: 78) })), "the actor identity was ignored")));
        tests.Add(("zero GUID is not a loaded object", () =>
            Check(!Pickup(Plan(live: new[] { Live(guid: 0) })), "zero GUID supplied coordinates")));
        tests.Add(("a different map cannot donate a giver observation", () =>
            Check(!Pickup(Plan(live: new[] { Live(map: 1) })), "the observation map was ignored")));
        tests.Add(("vertical separation is checked for nearby live observations", () =>
            Check(!Pickup(Plan(live: new[] { Live(z: 2000) })), "2D proximity accepted another vertical region")));
        tests.Add(("nonfinite live coordinates are ignored", () =>
            Check(!Pickup(Plan(live: new[] { Live(x: double.NaN) })), "nonfinite coordinates reached navigation")));
        tests.Add(("a present live giver cannot bypass navigation veto", () =>
            Check(!Pickup(Plan(live: new[] { Live() }, reachable: false)), "live presence was equated with reachability")));
        tests.Add(("a present live giver cannot bypass safety veto", () =>
            Check(!Pickup(Plan(live: new[] { Live() }, safe: false)), "live presence was equated with safety")));
        tests.Add(("a present live giver cannot bypass relation quarantine", () =>
            Check(!Pickup(Plan(live: new[] { Live() }, blockedScope: QuestRecoveryScope.NpcRelation)), "relation quarantine was bypassed")));
        tests.Add(("a present live giver cannot bypass endpoint quarantine", () =>
            Check(!Pickup(Plan(live: new[] { Live() }, blockedScope: QuestRecoveryScope.Endpoint)), "endpoint quarantine was bypassed")));
        tests.Add(("an exact stored safety veto survives coordinate refresh", () =>
        {
            var point = Point(); point.IsKnownSafe = false;
            Check(!Pickup(Plan(stored: new[] { point }, live: new[] { Live() })), "the live overlay erased known unsafe evidence");
        }));
        tests.Add(("two loaded spawns remain distinct alternatives", () =>
        {
            var result = Plan(live: new[] { Live(x: 20, guid: 8001), Live(x: 170, guid: 8002) });
            Check(Pickup(result) && result.Plan.SelectMany(entry => entry.Hotspots).Select(point => point.X)
                .Distinct().OrderBy(value => value).SequenceEqual(new[] { 20.0, 170.0 }), "a second loaded spawn was discarded");
        }));
        tests.Add(("ordinary stored geometry remains usable without live observations", () =>
            Check(Pickup(Plan(stored: new[] { Point() })), "the ordinary database fallback was removed")));

        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var test in tests)
        {
            try { test.Test(); passed++; Console.WriteLine("PASS live giver: " + test.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL live giver: " + test.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR live giver: " + test.Name + ": " + error); }
        }
        Console.WriteLine($"Live giver scenarios: {passed}/{tests.Count}; assertions={assertions}; unexpected={unexpected}; actual scheduler; controlled observations; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Live giver geometry regression");
    }

    private static SpawnPoint Point(double x = 10, double z = 10, int map = 530) => new() { Map = map, X = x, Y = 0, Z = z };
    private static QuestGiverObservation Live(QuestObjectType type = QuestObjectType.Creature, double x = 10, double z = 10,
        int map = 530, ulong guid = 8001, ulong player = 77, DateTime? time = null) => new()
    {
        Entry = 920010, ObjectType = type, Guid = guid, PlayerGuid = player, ObservedUtc = time ?? Now,
        MapId = map, X = x, Y = 0, Z = z, IsQuestGiver = true, Name = "Observed giver"
    };
    private static bool Pickup(QuestScheduleResult result) => result.Plan.Any(entry => entry.Stage == QuestWorkStage.Pickup && entry.Quest.Id == 920001);

    private static QuestScheduleResult Plan(QuestObjectType type = QuestObjectType.Creature, SpawnPoint[]? stored = null,
        QuestGiverObservation[]? live = null, bool? reachable = true, bool? safe = true,
        QuestRecoveryScope? blockedScope = null, List<QuestAttemptOutcome>? failures = null)
    {
        var db = new QuestDatabase
        {
            Quests = new() { new QuestEntry { Id = 920001, Name = "Observed giver quest", MinLevel = 58, QuestLevel = 61,
                Objectives = new() { new QuestObjective { Type = ObjectiveType.TurnInOnly, Index = 0 } } } },
            QuestGivers = new() { new QuestGiverEntry { QuestId = 920001, GiverId = 920010, GiverType = type, GiverName = "Stored giver" } }
        };
        (type == QuestObjectType.Creature ? db.CreatureSpawns : db.GameObjectSpawns)["920010"] = (stored ?? Array.Empty<SpawnPoint>()).ToList();
        var snapshot = new QuestSchedulerSnapshot
        {
            UtcNow = Now, PlayerGuid = 77, PlayerLevel = 60, PlayerRaceId = 10, PlayerClassId = 2,
            MapId = 530, X = 0, Y = 0, Z = 10, HasCompleteQuestLog = true, HasAuthoritativeCompletions = true,
            NearbyQuestGivers = live ?? Array.Empty<QuestGiverObservation>()
        };
        return QuestScheduler.MaterializeSchedule(db, snapshot, key => new QuestRecoveryDecision
        {
            State = key.Scope == blockedScope ? QuestRecoveryState.Quarantined : QuestRecoveryState.Eligible,
            MayAttempt = key.Scope != blockedScope, RetryUtc = key.Scope == blockedScope ? Now.AddMinutes(30) : null,
            Status = "controlled"
        }, 20, 250, 7, navigationAssessment: _ => new SpawnNavigationAssessment { IsKnownReachable = reachable, IsKnownSafe = safe },
            reportDataFailure: failure => failures?.Add(failure));
    }
    private static void Check(bool valid, string reason) { if (!valid) throw new AssertionFailure(reason); }
}
