using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Xml.Linq;
using Bots.Quest.Actions;
using Bots.Quest.Objectives;
using Bots.Quest.QuestOrder;
using Styx;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles.Quest;
using Styx.Logic.Questing.Recovery;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using WholesomeAQ;
using Kind = WholesomeAQ.ObjectiveType;
using ObjectKind = WholesomeAQ.QuestObjectType;

// Exercises the real scheduler/profile/behavior and loaded-object capture.
// Controlled snapshots and allocated test-process bytes are not realm evidence.
internal static class QuestLiveObjectiveRegressionTests
{
    private const int QuestId = 991001, TargetId = 991020, OtherId = 991021, EndpointId = 991030;
    private const ulong Player = 123, UnitGuid = 99001001;
    private static readonly DateTime Now = new(2026, 9, 30, 10, 0, 0, DateTimeKind.Utc);
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;
    private sealed class Failure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        var tests = new List<(string Name, Action Test)>();
        void Case(string name, Action test) => tests.Add((name, test));
        Case("direct entry supplies missing static objective location", () => Check(Work(Plan(live: new[] { Live() })), "direct entry ignored"));
        Case("direct entry needs no optional alias metadata", () => Check(Work(Plan(live: new[] { Live(first: 0, second: 0) })), "direct target required aliases"));
        Case("another entry can still supply a first credit alias", () => Check(Work(Plan(live: new[] { Live(entry: OtherId, first: TargetId) })), "existing first alias lost"));
        Case("another entry can still supply a second credit alias", () => Check(Work(Plan(live: new[] { Live(entry: OtherId, second: TargetId) })), "existing second alias lost"));
        Case("missing static location does not quarantine a present direct target", () =>
        {
            var failures = new List<QuestAttemptOutcome>(); var result = Plan(live: new[] { Live() }, failures: failures);
            Check(Work(result) && failures.Count == 0, "present live target was reported as missing quest data");
        });
        Case("stored geometry stays usable without live samples", () => Check(Work(Plan(stored: new[] { Point() })), "stored route lost"));
        Case("missing static and missing live stay unresolved", () => Check(!Work(Plan()), "missing route was invented"));
        Case("unrelated live creature is not an objective", () => Check(!Work(Plan(live: new[] { Live(entry: OtherId) })), "unrelated entry selected"));
        Case("different actor cannot donate geometry", () => Check(!Work(Plan(live: new[] { Live(player: Player + 1) })), "wrong player authorized"));
        Case("zero player is unknown", () => Check(!Work(Plan(live: new[] { Live(player: 0) })), "zero player authorized"));
        Case("zero unit GUID is not a loaded actor", () => Check(!Work(Plan(live: new[] { Live(guid: 0) })), "zero GUID authorized"));
        Case("stale sample is rejected", () => Check(!Work(Plan(live: new[] { Live(time: Now.AddSeconds(-1)) })), "stale sample accepted"));
        Case("future sample is rejected", () => Check(!Work(Plan(live: new[] { Live(time: Now.AddSeconds(1)) })), "future sample accepted"));
        Case("another map is rejected", () => Check(!Work(Plan(live: new[] { Live(map: 0) })), "wrong map selected"));
        Case("vertical distance is bounded", () => Check(!Work(Plan(live: new[] { Live(z: 10000) })), "other floor treated as near"));
        Case("nonfinite location is rejected", () => Check(!Work(Plan(live: new[] { Live(x: double.NaN) })), "NaN selected"));
        Case("dead friendly or unselectable observation is rejected", () => Check(!Work(Plan(live: new[] { Live(eligible: false) })), "ineligible actor selected"));
        Case("missing native objective metadata stays unknown", () => Check(!Work(Plan(live: new[] { Live() }, metadata: false)), "reference geometry replaced native metadata"));
        Case("different native objective count prevents borrowing", () => Check(!Work(Plan(live: new[] { Live() }, required: 4)), "wrong native count accepted"));
        Case("duplicated native identity stays ambiguous", () => Check(!Work(Plan(live: new[] { Live() }, duplicateSlot: true)), "ambiguous physical counter accepted"));
        Case("raw partial progress retains objective work", () => Check(Work(Plan(live: new[] { Live() }, progress: 2)), "partial progress lost work"));
        Case("raw complete progress suppresses objective", () => Check(!Work(Plan(live: new[] { Live() }, progress: 3)), "completed objective repeated"));
        Case("failed accepted quest has no objective work", () => Check(!Work(Plan(live: new[] { Live() }, failed: true)), "failed quest scheduled"));
        Case("completed accepted quest uses its ender", () =>
        {
            var result = Plan(live: new[] { Live() }, complete: true);
            Check(!Work(result) && result.Plan.Any(p => p.Stage == QuestWorkStage.TurnIn), "completed quest lost turn-in");
        });
        Case("incomplete log prevents work", () => Check(!Work(Plan(live: new[] { Live() }, logComplete: false)), "incomplete log authorized"));
        foreach (var state in new[] { QuestRecoveryState.CoolingDown, QuestRecoveryState.Quarantined })
        {
            var blocked = state;
            Case("direct sample retains recovery " + state, () => Check(!Work(Plan(live: new[] { Live() }, state: blocked)), "recovery bypassed"));
        }
        Case("half-open keeps one probe", () =>
        {
            var result = Plan(live: new[] { Live(), Live(guid: UnitGuid + 1, x: 120) }, state: QuestRecoveryState.HalfOpen);
            Check(result.Selected.Count == 1, "half-open direct objective lost its single probe");
        });
        Case("navigation veto stays required", () => Check(!Work(Plan(live: new[] { Live() }, reachable: false)), "navigation bypassed"));
        Case("safety veto stays required", () => Check(!Work(Plan(live: new[] { Live() }, safe: false)), "safety bypassed"));
        Case("exact stored negative geometry survives live overlay", () =>
        {
            var point = Point(); point.IsKnownReachable = false;
            Check(!Work(Plan(live: new[] { Live() }, stored: new[] { point })), "live point erased stored veto");
        });
        Case("stale far static point cannot hide a fresh nearby direct actor", () =>
        {
            var result = Plan(live: new[] { Live() }, stored: new[] { Point(x: 9000) });
            Check(Work(result) && result.Plan.Where(p => p.Stage == QuestWorkStage.Objective).SelectMany(p => p.Hotspots).All(p => p.X == 12), "fresh direct location not selected");
        });
        Case("two current actors retain distinct location choices", () =>
        {
            var result = Plan(live: new[] { Live(), Live(guid: UnitGuid + 1, x: 120) });
            Check(Work(result) && result.Plan.SelectMany(p => p.Hotspots).Select(p => p.X).Distinct().Count() == 2, "second direct spawn lost");
        });
        Case("direct sample never invents an item-acquisition owner", () =>
        {
            var db = Database(); db.Quests[0].Objectives[0].Type = Kind.CollectItem; db.Quests[0].Objectives[0].ItemId = 991040;
            db.Quests[0].Objectives[0].CollectCount = 3;
            Check(!Work(Plan(db, live: new[] { Live() })), "kill identity invented item source");
        });
        Case("direct sample never replaces a cast strategy", () =>
        {
            var db = Database(); db.Quests[0].SpecialFlags = 32;
            Check(!Work(Plan(db, live: new[] { Live() })), "cast ownership bypassed");
        });
        Case("live target cannot supply missing giver relations", () =>
        {
            var db = Database(); db.QuestGivers.Clear();
            Check(!Plan(db, live: new[] { Live() }, accepted: false).Plan.Any(p => p.Stage == QuestWorkStage.Pickup), "objective became giver");
        });
        Case("original dataset locations are not changed", () =>
        {
            var db = Database(); _ = Plan(db, live: new[] { Live() });
            Check(!db.CreatureSpawns.ContainsKey(TargetId.ToString()), "live sample wrote global geometry");
        });
        Case("loaded direct unit is captured without a cache row", () => WithLoaded((world, unit, db) =>
        {
            Check(!unit.GetCachedInfo(out _), "fixture unexpectedly has alias metadata");
            var captured = Capture(db, world.Player);
            Check(captured.Length == 1 && captured[0].Entry == TargetId && captured[0].Credit1 == 0 && captured[0].Credit2 == 0,
                "actual loaded direct target was not captured");
        }));
        Case("loaded direct unit retains actor map and XYZ", () => WithLoaded((world, unit, db) =>
        {
            var captured = Capture(db, world.Player);
            Check(captured.Length == 1 && captured[0].Guid == unit.Guid && captured[0].PlayerGuid == world.Player.Guid &&
                  captured[0].MapId == world.Player.MapId && captured[0].ObservedUtc == Now && captured[0].X == 12 && captured[0].Z == 10,
                "capture lost actual identity/location");
        }));
        Case("unrelated loaded unit is not captured", () => WithLoaded((world, unit, db) =>
        {
            db.Quests[0].Objectives[0].MobId = OtherId;
            Check(Capture(db, world.Player).Length == 0, "unrelated loaded unit captured");
        }));
        Case("dead loaded direct target is rejected", () => WithLoaded((world, unit, db) =>
        {
            unit.Alive = false; Check(Capture(db, world.Player).Length == 0, "dead direct target captured");
        }));
        Case("real direct target reaches profile behavior progress turn-in and next scheduling", () => WithLoaded((world, unit, db) =>
        {
            world.SetQuest(QuestId, "Live objective fixture", 60, new[] { TargetId, 0, 0, 0 }, new[] { 3, 0, 0, 0 }, new int[6], new int[6]);
            world.SetAccepted(false); world.SetHistory(Array.Empty<uint>());
            var pickup = Plan(db, accepted: false);
            var pickPlans = pickup.Plan.Where(p => p.Stage == QuestWorkStage.Pickup).ToArray();
            Check(pickPlans.Length == 1, "positive pickup control missing");
            var xml = new ProfileBuilder().BuildProfileXml(pickPlans, db, "Fixture", "Fixture", 60); world.LoadProfile(xml);
            var pickOwner = new ForcedQuestPickUp(QuestId, db.Quests[0].Name, EndpointId, "Fixture giver", new WoWPoint(10, 10, 10), Styx.Logic.Profiles.Quest.QuestObjectType.Npc);
            Check(!pickOwner.IsDone, "pickup acknowledged without acceptance"); world.SetAccepted(true); Check(pickOwner.IsDone, "pickup did not acknowledge acceptance");
            var captured = Capture(db, world.Player); Check(captured.Length == 1, "loaded direct target missing");
            var schedule = Plan(db, live: captured);
            var objectivePlans = schedule.Plan.Where(p => p.Stage == QuestWorkStage.Objective).ToArray();
            Check(objectivePlans.Length == 1, "capture did not reach objective scheduler");
            xml = new ProfileBuilder().BuildProfileXml(objectivePlans, db, "Fixture", "Fixture", 60); world.LoadProfile(xml);
            var element = XDocument.Parse(xml).Descendants("QuestOrder").Descendants("Objective").Single();
            var behavior = world.CreateObjective(ObjectiveNode.FromXml(element));
            Check(behavior.Objective is GrindObjective && !behavior.IsDone, "wrong generated behavior or manufactured completion");
            Check((bool)typeof(GrindObjective).GetMethod("IsMobObjective", Hidden)!.Invoke(behavior.Objective, new object[] { unit })!, "real behavior rejected direct entry");
            world.SetProgress(new[] { 2, 0, 0, 0 }); Check(!behavior.IsDone, "partial credit completed");
            world.SetProgress(new[] { 3, 0, 0, 0 }); Check(behavior.IsDone, "full progress not acknowledged");
            world.ReleaseOwners(); world.SetAccepted(true, complete: true);
            var ending = Plan(db, complete: true).Plan.Where(p => p.Stage == QuestWorkStage.TurnIn).ToArray();
            Check(ending.Length == 1, "authoritative completion lost turn-in");
            xml = new ProfileBuilder().BuildProfileXml(ending, db, "Fixture", "Fixture", 60); world.LoadProfile(xml);
            var endOwner = new ForcedQuestTurnIn(QuestId, db.Quests[0].Name, EndpointId, "Fixture ender", new WoWPoint(10, 10, 10));
            Check(!endOwner.IsDone, "turn-in completed without reward removal");
            world.SetAccepted(false); world.SetHistory(new[] { (uint)QuestId });
            Check(endOwner.IsDone && !Plan(db, accepted: false, rewarded: true).Plan.Any(), "reward or next scheduling acknowledgement failed");
        }));
        int passed = 0, failed = 0, unexpected = 0;
        foreach (var test in tests)
        {
            try { test.Test(); passed++; Console.WriteLine("PASS live objective: " + test.Name); }
            catch (Failure error) { failed++; Console.Error.WriteLine("FAIL live objective: " + test.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR live objective: " + test.Name + ": " + error); }
        }
        Console.WriteLine($"Live objective scenarios: {passed}/{tests.Count}; assertions={failed}; unexpected={unexpected}; actual loaded identity/scheduler/profile/behavior/progress; no game.");
        if (failed + unexpected != 0) throw new InvalidOperationException("Live objective regression");
    }

    private static SpawnPoint Point(double x = 12, double z = 10) => new() { Map = 1, X = x, Y = 10, Z = z };
    private static QuestCreatureCreditObservation Live(int entry = TargetId, int first = 0, int second = 0, ulong guid = UnitGuid,
        ulong player = Player, DateTime? time = null, int map = 1, double x = 12, double z = 10, bool eligible = true) => new()
    { Entry = entry, Credit1 = first, Credit2 = second, Guid = guid, PlayerGuid = player, ObservedUtc = time ?? Now,
      MapId = map, X = x, Y = 10, Z = z, AliveAttackableSelectable = eligible };
    private static QuestDatabase Database() => new()
    {
        Quests = new() { new QuestEntry { Id = QuestId, Name = "Live objective fixture", MinLevel = 58, QuestLevel = 62,
            Objectives = new() { new WholesomeAQ.QuestObjective { Type = Kind.KillMob, MobId = TargetId, KillCount = 3, Index = 0 } } } },
        QuestGivers = new() { new QuestGiverEntry { QuestId = QuestId, GiverId = EndpointId, GiverName = "Fixture giver", GiverType = ObjectKind.Creature } },
        QuestEnders = new() { new QuestEnderEntry { QuestId = QuestId, EnderId = EndpointId, EnderName = "Fixture ender", EnderType = ObjectKind.Creature } },
        CreatureSpawns = new() { [EndpointId.ToString()] = new() { Point(x: 10) } }
    };
    private static bool Work(QuestScheduleResult result) => result.Plan.Any(p => p.Quest.Id == QuestId &&
        (p.Stage == QuestWorkStage.Objective || p.Stage == QuestWorkStage.AncestorCorrection));
    private static QuestScheduleResult Plan(QuestDatabase? db = null, QuestCreatureCreditObservation[]? live = null, SpawnPoint[]? stored = null,
        bool accepted = true, bool failed = false, bool complete = false, bool rewarded = false, bool metadata = true,
        int required = 3, bool duplicateSlot = false, int progress = 0, bool logComplete = true,
        bool? reachable = true, bool? safe = true, QuestRecoveryState state = QuestRecoveryState.Eligible, List<QuestAttemptOutcome>? failures = null)
    {
        db ??= Database(); if (stored != null) db.CreatureSpawns[TargetId.ToString()] = stored.ToList();
        return QuestScheduler.MaterializeSchedule(db, new QuestSchedulerSnapshot
        {
            UtcNow = Now, PlayerGuid = Player, PlayerLevel = 60, PlayerRaceId = 10, PlayerClassId = 2, MapId = 1, X = 10, Y = 10, Z = 10,
            HasCompleteQuestLog = logComplete, HasAuthoritativeCompletions = true,
            CompletedQuestIds = rewarded ? new[] { (uint)QuestId } : Array.Empty<uint>(),
            CarriedItemCounts = new Dictionary<int, long>(), CreatureCredits = live ?? Array.Empty<QuestCreatureCreditObservation>(),
            AcceptedQuests = accepted ? new[] { new QuestSchedulerAcceptedQuest { QuestId = QuestId, IsFailed = failed, IsCompleted = complete,
                ObjectiveCounts = new[] { progress, 0, 0, 0 }, NormalObjectiveIds = metadata ? new[] { TargetId, duplicateSlot ? TargetId : 0, 0, 0 } : null,
                NormalObjectiveRequiredCounts = metadata ? new[] { required, duplicateSlot ? required : 0, 0, 0 } : null } } : Array.Empty<QuestSchedulerAcceptedQuest>()
        }, _ => new QuestRecoveryDecision { State = state, MayAttempt = state is QuestRecoveryState.Eligible or QuestRecoveryState.HalfOpen,
            RetryUtc = state is QuestRecoveryState.Eligible or QuestRecoveryState.HalfOpen ? null : Now.AddMinutes(2) }, 20, 250, 80,
            isKnownUnsafe: safe == false ? _ => true : null,
            navigationAssessment: _ => new SpawnNavigationAssessment { IsKnownSafe = safe, IsKnownReachable = reachable },
            reportDataFailure: result => failures?.Add(result));
    }
    private sealed class LoadedUnit(uint address) : WoWUnit(address)
    {
        internal bool Alive = true;
        public override bool IsAlive => Alive;
        public override WoWPoint Location => new(12, 10, 10);
    }
    private static QuestCreatureCreditObservation[] Capture(QuestDatabase db, LocalPlayer player) =>
        (QuestCreatureCreditObservation[])typeof(QuestScheduler).GetMethod("CaptureCreatureCredits", Hidden)!.Invoke(null, new object[] { db, player, Now })!;
    private static void WithLoaded(Action<QuestDatasetObservationFixture, LoadedUnit, QuestDatabase> test)
    {
        using var world = new QuestDatasetObservationFixture();
        IntPtr storage = Marshal.AllocHGlobal(4096);
        var objects = (Dictionary<ulong, WoWObject>)typeof(ObjectManager).GetField("_objectList", Hidden)!.GetValue(null)!;
        var saved = new Dictionary<ulong, WoWObject>(objects);
        try
        {
            Marshal.Copy(new byte[4096], 0, storage, 4096);
            uint address = unchecked((uint)storage.ToInt32());
            // Keep the retained fixture's simulated client globals. Invalidate
            // only this fresh allocation if the allocator reused an old address.
            var cache = (ThreadLocal<Dictionary<IntPtr, byte[]>>)ObjectManager.Wow.GetType().GetField("_cache", Hidden)!.GetValue(ObjectManager.Wow)!;
            foreach (IntPtr key in cache.Value!.Keys.Where(key => unchecked((uint)key.ToInt32()) >= address &&
                unchecked((uint)key.ToInt32()) < address + 4096).ToArray()) cache.Value.Remove(key);
            void Word(int offset, uint value) => Marshal.WriteInt32(storage, offset, unchecked((int)value));
            Word(8, address + 3072); Word(0x14, 3); Word(48, (uint)UnitGuid); Word(3072, (uint)UnitGuid); Word(3072 + 12, TargetId);
            var unit = new LoadedUnit(address);
            var reactions = (Dictionary<uint, WoWUnitReaction>)typeof(WoWUnit).GetField("_reactionCacheByEntry", Hidden)!.GetValue(world.Player)!;
            reactions[(uint)TargetId] = WoWUnitReaction.Hostile;
            objects.Clear(); objects[UnitGuid] = unit;
            Check(world.Player.Guid == Player && world.Player.MapId == 1 && unit.Entry == TargetId && unit.Attackable && unit.CanSelect,
                "allocated fixture identity/flags invalid");
            Check(unit.MyReaction == WoWUnitReaction.Hostile && ObjectManager.Executor == null, "explicit test reaction or deny-native state missing");
            test(world, unit, Database());
        }
        finally { objects.Clear(); foreach (var pair in saved) objects[pair.Key] = pair.Value; Marshal.FreeHGlobal(storage); }
    }
    private static void Check(bool result, string message) { if (!result) throw new Failure(message); }
}
