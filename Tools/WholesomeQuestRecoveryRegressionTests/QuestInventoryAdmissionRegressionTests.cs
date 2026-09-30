using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Xml.Linq;
using Bots.Quest.QuestOrder;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles.Quest;
using Styx.Logic.Questing;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;
using DataObjectType = WholesomeAQ.QuestObjectType;
using DataObjectiveType = WholesomeAQ.ObjectiveType;

internal static class QuestInventoryAdmissionRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private sealed class Failure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        var tests = new List<(string Name, Action Test)>();
        void Case(string name, Action<QuestInventorySnapshotRegressionTests.Fixture> body) =>
            tests.Add((name, () => { using var f = new QuestInventorySnapshotRegressionTests.Fixture(); body(f); }));
        Case("complete stock permits a retained pickup", f => { f.Items((301, 2)); Check(Guard(f, Delivery(), QuestWorkStage.Pickup)(), "current stock rejected"); });
        Case("lost stock revokes a retained pickup", f => { var item = f.Item(23, 301, 2); var check = Guard(f, Delivery(), QuestWorkStage.Pickup); Check(check(), "initial stock rejected"); f.MainSlot(23, 0); Check(!check(), "lost stock stayed authorized"); });
        Case("partial stock revokes a retained turn-in", f => { var item = f.Item(23, 301, 2); var check = Guard(f, Delivery(), QuestWorkStage.TurnIn); Check(check(), "initial turn-in rejected"); f.Write32(item.Fields + 56, 1); Check(!check(), "partial stock stayed authorized"); });
        Case("unreadable occupied inventory never becomes a receipt", f => { f.MainSlot(23, 777); Check(!Guard(f, Delivery(), QuestWorkStage.TurnIn)(), "missing object authorized turn-in"); });
        Case("source promise permits pickup without claiming inventory", f => { var quest = Delivery(); quest.StartItem = 301; quest.AcceptanceSupplies.Add(new() { ItemId = 301, Count = 2 }); f.TradeReadable = false; Check(Guard(f, quest, QuestWorkStage.Pickup)(), "source-proven pickup was changed"); });
        Case("source promise does not authorize turn-in", f => { var quest = Delivery(); quest.StartItem = 301; quest.AcceptanceSupplies.Add(new() { ItemId = 301, Count = 2 }); Check(!Guard(f, quest, QuestWorkStage.TurnIn)(), "promise replaced actual stock"); });
        Case("partial source supply still needs carried stock", f => { var quest = Delivery(); quest.StartItem = 301; quest.AcceptanceSupplies.Add(new() { ItemId = 301, Count = 1 }); Check(!Guard(f, quest, QuestWorkStage.Pickup)(), "missing carried remainder bypassed"); f.Items((301, 1)); Check(Guard(f, quest, QuestWorkStage.Pickup)(), "valid carried remainder rejected"); });
        Case("supplemental return item must remain present", f => { var item = f.Item(23, 301, 2); var check = Guard(f, Supplemental(), QuestWorkStage.TurnIn); Check(check(), "initial supplemental stock rejected"); f.MainSlot(23, 0); Check(!check(), "lost supplemental stock was a receipt"); });
        Case("supplemental item does not gate the ordinary objective", f => { f.TradeReadable = false; Check(Guard(f, Supplemental(), QuestWorkStage.Objective)(), "normal objective was pickup gated"); });
        Case("unrelated quests do not require inventory reads", f => { var quest = Delivery(); quest.DeliveryItems = null; quest.AcceptanceSupplies = null; f.ReadError = new IOException("must not query inventory"); Check(Guard(f, quest, QuestWorkStage.TurnIn)(), "unrelated work acquired an inventory gate"); });
        Case("ordinary capture failure withholds constrained work", f => { f.ReadError = new IOException("read failed"); Check(!Guard(f, Delivery(), QuestWorkStage.TurnIn)(), "unknown stock authorized work"); });
        Case("a fresh valid sample recovers after a trade observation", f => { f.Items((301, 2)); var check = Guard(f, Delivery(), QuestWorkStage.TurnIn); f.TradeOwners(777, 777); f.Write64(f.Trade, 777); Check(!check(), "trade bypassed inventory gate"); f.TradeOwners(0, 0); Check(check(), "fresh post-trade stock did not recover despite confirmed close"); });
        Case("source delivery runs scheduler profile acceptance and reward acknowledgement", Pipeline);
        tests.Add(("published execution permission obeys the inventory guard", PublishedGate));
        int passed = 0, failed = 0, errors = 0;
        foreach (var test in tests)
        {
            try { test.Test(); passed++; Console.WriteLine("PASS inventory admission: " + test.Name); }
            catch (Failure error) { failed++; Console.Error.WriteLine("FAIL inventory admission: " + test.Name + ": " + error.Message); }
            catch (Exception error) { errors++; Console.Error.WriteLine("ERROR inventory admission: " + test.Name + ": " + error); }
        }
        Console.WriteLine($"Inventory admission scenarios: {passed}/{tests.Count}; assertions={failed}; unexpected={errors}; actual inventory/scheduler/profile/behavior/publication owners, no game.");
        if (failed + errors != 0) throw new InvalidOperationException("Inventory admission regression");
    }

    private static QuestEntry Delivery() => new()
    {
        Id = 991001, Name = "Controlled inventory delivery", MinLevel = 1, QuestLevel = 1,
        DeliveryItems = new() { new() { ItemId = 301, Count = 2 } }, AcceptanceSupplies = new(),
        Objectives = new() { new() { Type = DataObjectiveType.TurnInOnly, Index = 0 } }
    };
    private static QuestEntry Supplemental()
    {
        var q = Delivery(); q.DeliveryItems = null; q.AcceptanceSupplies = null; q.StartItem = 301;
        q.SupplementalSupply = new() { ItemId = 301, RequiredCount = 2, ProvidedCount = 2 };
        q.Objectives = new() { new() { Type = DataObjectiveType.KillMob, MobId = 991020, KillCount = 3 } };
        return q;
    }

    private static Func<bool> Guard(QuestInventorySnapshotRegressionTests.Fixture f, QuestEntry quest, QuestWorkStage stage)
    {
        MethodInfo? method = typeof(QuestScheduler).GetMethod("CreateInventoryRequirementGuard", Hidden);
        Check(method != null, "inventory requirement publication guard missing");
        var plan = new[] { new QuestPlanEntry { Quest = quest, Stage = stage } };
        Func<QuestInventorySnapshot> capture = () => (QuestInventorySnapshot)f.Capture();
        return (Func<bool>?)Invoke(method!, null, plan, capture) ?? (() => true);
    }

    private static void Pipeline(QuestInventorySnapshotRegressionTests.Fixture f)
    {
        using var source = new QuestDataRepairPackRegressionTests.Fixture(); source.Write();
        QuestDatabase db = source.Load(); QuestEntry q = db.Quests.Single(); uint id = (uint)q.Id;
        db.QuestGivers.Add(new() { QuestId = q.Id, GiverId = 991020, GiverName = "Controlled giver" });
        db.CreatureSpawns["991030"] = new() { new() { Map = 530, X = 10, Y = 20, Z = 37 } };
        var fixture = f.NativeFixture;
        fixture.SetQuest(id, q.Name, 60, new int[4], new int[4], new[] { 991010, 0, 0, 0, 0, 0 }, new[] { 1, 0, 0, 0, 0, 0 });
        fixture.SetAccepted(false); fixture.SetHistory(Array.Empty<uint>());
        QuestScheduleResult Plan(bool accepted = false, bool ready = false, bool rewarded = false)
        {
            var observed = (QuestInventorySnapshot)f.Capture();
            return QuestScheduler.MaterializeSchedule(db, new QuestSchedulerSnapshot
            {
                PlayerGuid = f.Player.Guid, PlayerLevel = 60, PlayerRaceId = 10, PlayerClassId = 2,
                MapId = 530, X = 10, Y = 20, Z = 37, UtcNow = DateTime.UtcNow,
                HasAuthoritativeCompletions = true, CompletedQuestIds = rewarded ? new[] { id } : Array.Empty<uint>(),
                CarriedItemCounts = observed.ItemCounts,
                AcceptedQuests = accepted ? new[] { new QuestSchedulerAcceptedQuest { QuestId = id, IsCompleted = ready } } : Array.Empty<QuestSchedulerAcceptedQuest>()
            }, _ => new QuestRecoveryDecision { MayAttempt = true, State = QuestRecoveryState.Eligible }, 10, 250, 80,
                navigationAssessment: _ => new SpawnNavigationAssessment { IsKnownSafe = true, IsKnownReachable = true });
        }
        var pickup = Plan(); Check(pickup.Plan.Any(p => p.Stage == QuestWorkStage.Pickup), "verified source supply did not admit pickup");
        var builder = new ProfileBuilder(); string xml = builder.BuildProfileXml(pickup.Plan, db, "Controlled zone", "Controlled actor", 60);
        Check(XDocument.Parse(xml).Descendants("PickUp").Any(), "pickup profile not generated"); fixture.LoadProfile(xml);
        var pick = new ForcedQuestPickUp(id, q.Name, 991020, "Controlled giver", new WoWPoint(10, 20, 37), Styx.Logic.Profiles.Quest.QuestObjectType.Npc);
        Check(!pick.IsDone, "pickup completed without acceptance"); fixture.SetAccepted(true); Check(pick.IsDone, "accepted log was not acknowledged");
        Check(!Plan(true, true).Plan.Any(p => p.Stage == QuestWorkStage.TurnIn), "source promise became an actual item");
        var item = f.Item(23, 991010, 1);
        Check(!Plan(true).Plan.Any(p => p.Stage == QuestWorkStage.TurnIn), "held item became authoritative quest readiness");
        var turnin = Plan(true, true); Check(turnin.Plan.Any(p => p.Stage == QuestWorkStage.TurnIn), "ready state and actual held item did not admit turn-in");
        string ending = builder.BuildProfileXml(turnin.Plan, db, "Controlled zone", "Controlled actor", 60);
        Check(XDocument.Parse(ending).Descendants("TurnIn").Any(), "turn-in profile not generated"); fixture.LoadProfile(ending);
        var permission = Guard(f, q, QuestWorkStage.TurnIn); Check(permission(), "held receipt rejected");
        f.MainSlot(23, 0); Check(!permission(), "lost inventory retained execution permission"); f.MainSlot(23, item.Guid); Check(permission(), "fresh held receipt did not recover");
        var end = new ForcedQuestTurnIn(id, q.Name, 991030, "Controlled ender", new WoWPoint(10, 20, 37), Styx.Logic.Profiles.Quest.QuestObjectType.Npc);
        fixture.SetAccepted(true, complete: true); Check(!end.IsDone, "turn-in finished before reward acknowledgement");
        fixture.SetAccepted(false); fixture.SetHistory(new[] { id }); Check(end.IsDone, "rewarded removal was not acknowledged");
        Check(Plan(rewarded: true).Plan.All(p => p.Quest.Id != q.Id), "rewarded quest was scheduled again");
    }

    private static void PublishedGate()
    {
        Type type = typeof(QuestSchedulerRawPublicationRegressionTests).GetNestedType("Case", Hidden)!;
        using var fixture = (IDisposable)Activator.CreateInstance(type, Hidden, null, new object[] { false }, null)!;
        Invoke(type.GetMethod("Scan", Hidden)!, fixture);
        Invoke(type.GetMethod("Published", Hidden)!, fixture);
        var scheduler = (QuestScheduler)type.GetField("Scheduler", Hidden)!.GetValue(fixture)!;
        object work = typeof(QuestScheduler).GetField("_publishedWork", Hidden)!.GetValue(scheduler)!;
        var field = work.GetType().GetField("InventoryCurrent", Hidden);
        Check(field != null, "published work has no continuing inventory guard");
        int calls = 0; field!.SetValue(work, (Func<bool>)(() => { calls++; return false; }));
        var check = (Func<bool>)Invoke(typeof(QuestScheduler).GetMethod("CaptureExecutionPermission", Hidden)!, scheduler)!;
        Check(!check() && calls == 1 && scheduler.LastSchedule.Selected.Count == 0 && scheduler.CurrentProfilePath == null,
            "failed inventory guard retained published execution");
    }

    private static object? Invoke(MethodInfo method, object? target, params object?[] args)
    {
        try { return method.Invoke(target, args); }
        catch (TargetInvocationException error) when (error.InnerException != null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static void Check(bool value, string message) { if (!value) throw new Failure(message); }
}
