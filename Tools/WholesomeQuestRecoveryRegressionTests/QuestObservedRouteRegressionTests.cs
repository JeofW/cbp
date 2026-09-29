using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;

internal static class QuestObservedRouteRegressionTests
{
    private static readonly DateTime Now = new(2026, 9, 29, 11, 30, 0, DateTimeKind.Utc);
    private sealed class Failure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, Action Test)>();
        void ItemCase(string name, bool expected, QuestItemStarterObservation? item = null, bool authority = true,
            bool logComplete = true, bool accepted = false, bool rewarded = false, long carried = 1, bool blocked = false,
            int level = 60, int specialFlags = 0)
        {
            cases.Add((name, () =>
            {
                var result = Plan(items: new[] { item ?? Item() }, authority: authority, logComplete: logComplete,
                    accepted: accepted, rewarded: rewarded, carried: carried, blocked: blocked, level: level, flags: specialFlags);
                Check(result.Plan.Any(value => value.Stage == QuestWorkStage.Pickup) == expected, "item pickup admission disagreed");
            }));
        }
        ItemCase("fresh carried original-client item quest supplies a missing giver", true);
        ItemCase("stale item sample is rejected", false, Item(time: Now.AddSeconds(-1)));
        ItemCase("future item sample is rejected", false, Item(time: Now.AddSeconds(1)));
        ItemCase("foreign actor item sample is rejected", false, Item(player: 99));
        ItemCase("other-map item sample is rejected", false, Item(map: 1));
        ItemCase("zero physical item GUID is not a source", false, Item(guid: 0));
        ItemCase("already-active item quest is rejected", false, Item(active: true));
        ItemCase("item that starts another quest is not borrowed", false, Item(quest: 950099));
        ItemCase("unknown history cannot admit an item quest", false, authority: false);
        ItemCase("incomplete raw log cannot admit an item quest", false, logComplete: false);
        ItemCase("accepted item quest is not picked up again", false, accepted: true);
        ItemCase("rewarded item quest is not picked up again", false, rewarded: true);
        ItemCase("missing carried quantity revokes an item source", false, carried: 0);
        ItemCase("recovery still blocks an observed item route", false, blocked: true);
        ItemCase("minimum-level gate still applies to an item route", false, level: 57);
        ItemCase("event item quest still requires an explicit recipe", false, specialFlags: 2);
        cases.Add(("dynamic item source generates the existing typed host pickup node", () =>
        {
            var db = Db(); var result = Plan(items: new[] { Item() });
            var xml = XDocument.Parse(new ProfileBuilder().BuildProfileXml(result.Plan, db, "fixture", "fixture", 60));
            var element = xml.Descendants("PickUp").SingleOrDefault();
            Check(element != null && (string?)element.Attribute("GiverType") == "Item" && (string?)element.Attribute("GiverId") == "950020", "no typed Item pickup was generated");
            var node = Styx.Logic.Profiles.Quest.PickUpNode.FromXml(element!);
            Check(node.GiverType == Styx.Logic.Profiles.Quest.QuestObjectType.Item && node.QuestId == 950001, "host parser changed item identity");
        }));
        cases.Add(("an imported Item relation without a live quest association is ignored", () =>
        {
            var db = Db(); db.QuestGivers.Add(new QuestGiverEntry { QuestId = 950001, GiverId = 950020, GiverType = QuestObjectType.Item });
            db.CreatureSpawns["950020"] = new() { new SpawnPoint { Map = 530, X = 10, Y = 10, Z = 10 } };
            Check(!Plan(db: db).Plan.Any(value => value.Stage == QuestWorkStage.Pickup), "item source borrowed a creature spawn");
        }));
        cases.Add(("conflicting quest identities for the same physical item are rejected", () =>
            Check(!Plan(items: new[] { Item(), Item(quest: 950099) }).Plan.Any(value => value.Stage == QuestWorkStage.Pickup), "ambiguous item sample authorized pickup")));

        void CreditCase(string name, bool expected, QuestCreatureCreditObservation? credit = null, int flags = 0,
            bool metadata = true, bool blocked = false, bool? safe = true, bool? reachable = true, int progress = 0)
        {
            cases.Add((name, () => Check(Plan(credits: new[] { credit ?? Credit() }, accepted: true, flags: flags,
                metadata: metadata, blocked: blocked, safe: safe, reachable: reachable, progress: progress).Plan
                .Any(value => value.Stage == QuestWorkStage.Objective) == expected, "credit-alias objective admission disagreed")));
        }
        CreditCase("live client credit alias supplies missing ordinary kill geometry", true);
        CreditCase("second client credit alias is supported", true, Credit(first: 0, second: 950010));
        CreditCase("unrelated credit cannot donate geometry", false, Credit(first: 1234));
        CreditCase("stale credit sample is ignored", false, Credit(time: Now.AddSeconds(-1)));
        CreditCase("foreign actor credit sample is ignored", false, Credit(player: 99));
        CreditCase("other-map credit sample is ignored", false, Credit(map: 1));
        CreditCase("zero GUID credit sample is ignored", false, Credit(guid: 0));
        CreditCase("dead or unattackable alias does not become killing", false, Credit(attackable: false));
        CreditCase("nonfinite alias position is ignored", false, Credit(x: double.NaN));
        CreditCase("another vertical region is not nearby", false, Credit(z: 9000));
        CreditCase("cast-credit flag still requires a strategy", false, flags: 32);
        CreditCase("missing runtime objective identity cannot authorize an alias", false, metadata: false);
        CreditCase("recovery retains authority over alias routes", false, blocked: true);
        CreditCase("unsafe alias endpoint is rejected", false, safe: false);
        CreditCase("unreachable alias endpoint is rejected", false, reachable: false);
        CreditCase("fulfilled credit is not scheduled again", false, progress: 3);
        cases.Add(("alias-backed XML retains credit identity rather than rewriting the quest", () =>
        {
            var result = Plan(credits: new[] { Credit() }, accepted: true);
            var xml = XDocument.Parse(new ProfileBuilder().BuildProfileXml(result.Plan, Db(), "fixture", "fixture", 60));
            Check(xml.Descendants("Objective").Any(value => (string?)value.Attribute("MobId") == "950010"), "credit identity was lost from the profile");
            Check(result.Plan.SelectMany(value => value.Hotspots).Any(value => value.X == 12), "loaded alias coordinates did not reach the profile plan");
        }));
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { item.Test(); passed++; Console.WriteLine("PASS observed quest route: " + item.Name); }
            catch (Failure error) { assertions++; Console.Error.WriteLine("FAIL observed quest route: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR observed quest route: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Observed quest route scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual scheduler/profile/parser; controlled client observations; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Observed quest route regression");
    }

    private static QuestDatabase Db() => new()
    {
        Quests = new() { new QuestEntry { Id = 950001, Name = "Observed route", MinLevel = 58, QuestLevel = 62,
            Objectives = new() { new QuestObjective { Type = ObjectiveType.KillMob, MobId = 950010, KillCount = 3, Index = 0 } } } }
    };
    private static QuestItemStarterObservation Item(int quest = 950001, ulong guid = 800, ulong player = 77, int map = 530,
        DateTime? time = null, bool active = false) => new()
    { QuestId = quest, ItemEntry = 950020, ItemGuid = guid, PlayerGuid = player, MapId = map, ObservedUtc = time ?? Now, IsActive = active, Name = "Observed item" };
    private static QuestCreatureCreditObservation Credit(int first = 950010, int second = 0, ulong guid = 900, ulong player = 77,
        int map = 530, DateTime? time = null, bool attackable = true, double x = 12, double z = 10) => new()
    { Entry = 950030, Credit1 = first, Credit2 = second, Guid = guid, PlayerGuid = player, MapId = map,
        ObservedUtc = time ?? Now, AliveAttackableSelectable = attackable, X = x, Y = 10, Z = z };
    private static QuestScheduleResult Plan(QuestDatabase? db = null, QuestItemStarterObservation[]? items = null,
        QuestCreatureCreditObservation[]? credits = null, bool authority = true, bool logComplete = true, bool accepted = false,
        bool rewarded = false, long carried = 1, bool blocked = false, int level = 60, int flags = 0,
        bool metadata = true, bool? safe = true, bool? reachable = true, int progress = 0)
    {
        db ??= Db(); db.Quests[0].SpecialFlags = flags;
        return QuestScheduler.MaterializeSchedule(db, new QuestSchedulerSnapshot
        {
            UtcNow = Now, PlayerLevel = level, PlayerRaceId = 10, PlayerClassId = 2, PlayerGuid = 77,
            MapId = 530, X = 10, Y = 10, Z = 10, ItemStarters = items ?? Array.Empty<QuestItemStarterObservation>(),
            CreatureCredits = credits ?? Array.Empty<QuestCreatureCreditObservation>(), CarriedItemCounts = new Dictionary<int, long> { [950020] = carried },
            HasAuthoritativeCompletions = authority, HasCompleteQuestLog = logComplete,
            CompletedQuestIds = rewarded ? new uint[] { 950001 } : Array.Empty<uint>(),
            AcceptedQuests = accepted ? new[] { new QuestSchedulerAcceptedQuest { QuestId = 950001,
                ObjectiveCounts = new[] { progress, 0, 0, 0 }, NormalObjectiveIds = metadata ? new[] { 950010, 0, 0, 0 } : null,
                NormalObjectiveRequiredCounts = metadata ? new[] { 3, 0, 0, 0 } : null } } : Array.Empty<QuestSchedulerAcceptedQuest>()
        }, _ => new QuestRecoveryDecision { MayAttempt = !blocked, State = blocked ? QuestRecoveryState.Quarantined : QuestRecoveryState.Eligible,
            RetryUtc = blocked ? Now.AddMinutes(5) : null, Status = "controlled" }, 20, 250, 7,
            navigationAssessment: _ => new SpawnNavigationAssessment { IsKnownSafe = safe, IsKnownReachable = reachable });
    }
    private static void Check(bool value, string reason) { if (!value) throw new Failure(reason); }
}
