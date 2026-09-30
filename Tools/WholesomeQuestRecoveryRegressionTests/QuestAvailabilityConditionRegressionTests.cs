using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using WholesomeAQ;
using Styx.Logic.Questing.Recovery;

// The real loader and scheduler receive explicit raw-state/history observations.
// These fixtures do not attach to a client or claim actual server completion.
internal static class QuestAvailabilityConditionRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }
    private static readonly DateTime Now = new(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc);
    private const int Subject = 991001, Parent = 991099, Other = 991098;

    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, Action Body)>();
        void Case(string name, Action<Fixture> body) => cases.Add((name, () => { using var f = new Fixture(); body(f); }));
        Case("unmet reward condition blocks otherwise valid pickup", f => Check(!f.Pickup(f.Snapshot()), "missing reward was admitted"));
        Case("authoritative permanent reward admits pickup", f => Check(f.Pickup(f.Snapshot(rewarded: new uint[] { Parent })), "known reward was rejected"));
        Case("unknown history never becomes a reward", f => Check(!f.Pickup(f.Snapshot(history: false, rewarded: new uint[] { Parent })), "unknown history admitted"));
        Case("negated unknown history is still unknown", f => { f.Condition["Negative"] = true; Check(!f.Pickup(f.Snapshot(history: false)), "negation manufactured knowledge"); });
        Case("known absent reward satisfies its negation", f => { f.Condition["Negative"] = true; Check(f.Pickup(f.Snapshot()), "negated absent reward rejected"); });
        Case("same ElseGroup requires every predicate", f => { f.AddCondition(8, Other); Check(!f.Pickup(f.Snapshot(rewarded: new uint[] { Parent })), "AND became OR"); });
        Case("different ElseGroup supplies an alternative", f => { f.AddGroup(1, 8, Other); Check(f.Pickup(f.Snapshot(rewarded: new uint[] { Other })), "OR became AND"); });
        Case("taken means raw incomplete", f => { f.Condition["Type"] = 9; Check(f.Pickup(f.Snapshot(states: new() { [Parent] = 3 })), "incomplete parent rejected"); });
        foreach (int status in new[] { 1, 5, 6, 0 })
        {
            int captured = status;
            Case("taken rejects status " + captured, f =>
            {
                f.Condition["Type"] = 9;
                Check(!f.Pickup(f.Snapshot(states: captured is 1 or 5 ? new() { [Parent] = captured } : new(),
                    rewarded: captured == 6 ? new uint[] { Parent } : Array.Empty<uint>())), "taken broadened to another state");
            });
        }
        Case("raw completion does not borrow computed inventory readiness", f =>
        {
            f.Condition["Type"] = 28;
            Check(!f.Pickup(f.Snapshot(states: new() { [Parent] = 3 }, computedCompleted: true)), "computed readiness replaced server flag");
        });
        Case("complete requires the raw complete state", f => { f.Condition["Type"] = 28; Check(f.Pickup(f.Snapshot(states: new() { [Parent] = 1 })), "raw complete rejected"); });
        Case("complete also requires not rewarded", f => { f.Condition["Type"] = 28; Check(!f.Pickup(f.Snapshot(states: new() { [Parent] = 1 }, rewarded: new uint[] { Parent })), "rewarded satisfied complete-unrewarded"); });
        Case("none requires both no active state and no reward", f => { f.Condition["Type"] = 14; Check(f.Pickup(f.Snapshot()), "known none rejected"); Check(!f.Pickup(f.Snapshot(rewarded: new uint[] { Parent })), "rewarded became none"); });
        Case("missing raw state collection does not prove none", f => { f.Condition["Type"] = 14; Check(!f.Pickup(f.Snapshot(rawAvailable: false)), "missing raw states became empty"); });
        Case("state mask distinguishes failed from incomplete", f => { f.Condition["Type"] = 47; f.Condition["Value2"] = 32; Check(f.Pickup(f.Snapshot(states: new() { [Parent] = 5 })), "failed mask rejected"); Check(!f.Pickup(f.Snapshot(states: new() { [Parent] = 3 })), "incomplete matched failed"); });
        Case("state mask rewarded bit is independent of accepted state", f => { f.Condition["Type"] = 47; f.Condition["Value2"] = 64; Check(f.Pickup(f.Snapshot(states: new() { [Parent] = 1 }, rewarded: new uint[] { Parent })), "reward bit lost behind accepted state"); });
        Case("other quest cannot satisfy the referenced identity", f => Check(!f.Pickup(f.Snapshot(rewarded: new uint[] { Other })), "wrong identity borrowed"));
        Case("condition contract changes the execution fingerprint", f => { f.Load(); string one = f.F.Loader().ExecutionFingerprint; f.Condition["Negative"] = true; f.Load(); Check(one != f.F.Loader().ExecutionFingerprint, "condition bytes absent from execution identity"); });
        Case("accepted objective and turn-in are not pickup gated", f => Check(f.Schedule(f.Snapshot(subjectReady: true)).Plan.Any(p => p.Quest.Id == Subject && p.Stage == QuestWorkStage.TurnIn), "availability prevented accepted turn-in"));
        Case("debug rejection identifies the condition", f => { var messages = new List<string>(); f.Schedule(f.Snapshot(), messages.Add); Check(messages.Any(s => s.Contains("availability-condition", StringComparison.Ordinal)), "condition rejection hidden"); });
        Case("publication recheck uses current condition state", f =>
        {
            QuestDatabase db = f.Load(); var plan = f.Schedule(f.Snapshot(rewarded: new uint[] { Parent })).Plan;
            var type = typeof(QuestScheduler).Assembly.GetType("WholesomeAQ.QuestAvailabilityPolicy");
            MethodInfo? method = type?.GetMethod("RequirementsCurrent", BindingFlags.Public | BindingFlags.Static);
            Check(method != null, "current-condition publication guard missing");
            Check((bool)method!.Invoke(null, new object[] { plan, f.Snapshot(rewarded: new uint[] { Parent }) })!, "current passing condition rejected");
            Check(!(bool)method.Invoke(null, new object[] { plan, f.Snapshot() })!, "lost reward remained authorized");
            Check(!(bool)method.Invoke(null, new object[] { plan, f.Snapshot(history: false) })!, "lost authority remained authorized");
        });
        foreach (string fault in new[] { "foreign-quest", "duplicate-subject", "unsupported-type", "empty-groups", "empty-conditions",
            "duplicate-group", "duplicate-condition", "missing-reference", "repeatable-reference", "seasonal-reference",
            "auto-complete-reference", "bad-mask", "foreign-target-field", "negative-not-bool", "unexpected-value", "empty-source" })
        {
            string captured = fault;
            Case("invalid condition pack rejected: " + captured, f =>
            {
                f.Corrupt(captured); f.Write();
                bool failed = false; try { f.F.Load(); } catch (InvalidDataException) { failed = true; }
                Check(failed, "invalid condition contract loaded: " + captured);
            });
        }
        int passed = 0, failed = 0, errors = 0;
        foreach (var test in cases)
        {
            try { test.Body(); passed++; Console.WriteLine("PASS availability: " + test.Name); }
            catch (Failure e) { failed++; Console.Error.WriteLine("FAIL availability: " + test.Name + ": " + e.Message); }
            catch (Exception e) { errors++; Console.Error.WriteLine("ERROR availability: " + test.Name + ": " + e); }
        }
        Console.WriteLine($"Availability condition scenarios: {passed}/{cases.Count}; assertions={failed}; unexpected={errors}; actual loader/scheduler; controlled raw flags/history; no game.");
        if (failed + errors != 0) throw new InvalidOperationException("Availability condition regression");
    }
    private static void Check(bool value, string message) { if (!value) throw new Failure(message); }

    private sealed class Fixture : IDisposable
    {
        internal readonly QuestDataRepairPackRegressionTests.Fixture F = new();
        internal JsonObject Contract, Condition;
        internal Fixture()
        {
            F.SetBase(root =>
            {
                root["QuestGivers"] = JsonNode.Parse("[{\"QuestId\":991001,\"GiverId\":991020,\"GiverName\":\"Controlled giver\",\"GiverType\":0}]");
                root["CreatureSpawns"]!["991030"] = JsonNode.Parse("[{\"Map\":530,\"X\":11,\"Y\":20,\"Z\":37}]");
            });
            Contract = JsonNode.Parse("""
            {"QuestId":991001,"SourceRef":"controlled://conditions/991001","ReferencedQuests":[
              {"QuestId":991099,"QuestType":2,"SpecialFlags":0,"QuestSortID":0,"SourceRef":"controlled://template/991099"},
              {"QuestId":991098,"QuestType":2,"SpecialFlags":0,"QuestSortID":0,"SourceRef":"controlled://template/991098"}],
             "Groups":[{"ElseGroup":0,"Conditions":[{"Type":8,"Value1":991099,"Value2":0,"Value3":0,"Negative":false,"SourceRef":"controlled://condition/1"}]}]}
            """)!.AsObject();
            Condition = Contract["Groups"]![0]!["Conditions"]![0]!.AsObject();
            F.Pack["QuestAvailabilityConditions"] = new JsonArray(Contract);
        }
        internal void AddCondition(int type, int parent)
        {
            var next = Condition.DeepClone().AsObject(); next["Type"] = type; next["Value1"] = parent;
            next["SourceRef"] = "controlled://condition/" + parent;
            Contract["Groups"]![0]!["Conditions"]!.AsArray().Add(next);
        }
        internal void AddGroup(int group, int type, int parent)
        {
            var next = Contract["Groups"]![0]!.DeepClone(); next["ElseGroup"] = group;
            next["Conditions"]![0]!["Type"] = type; next["Conditions"]![0]!["Value1"] = parent;
            next["Conditions"]![0]!["SourceRef"] = "controlled://condition/group/" + group;
            Contract["Groups"]!.AsArray().Add(next);
        }
        internal void Write() => F.Write();
        internal QuestDatabase Load()
        {
            Write();
            try { return F.Load(); }
            catch (InvalidDataException e) { throw new Failure("supported condition was not loaded: " + e.Message); }
        }
        internal QuestSchedulerSnapshot Snapshot(bool history = true, uint[]? rewarded = null,
            Dictionary<uint, int>? states = null, bool rawAvailable = true, bool computedCompleted = false, bool subjectReady = false)
        {
            states ??= new();
            var accepted = states.Select(row => new QuestSchedulerAcceptedQuest { QuestId = row.Key,
                IsCompleted = computedCompleted || row.Value == 1, IsFailed = row.Value == 5 }).ToList();
            if (subjectReady) accepted.Add(new QuestSchedulerAcceptedQuest { QuestId = Subject, IsCompleted = true });
            var snapshot = new QuestSchedulerSnapshot { UtcNow = Now, PlayerGuid = 42, PlayerLevel = 1,
                PlayerRaceId = 1, PlayerClassId = 2, MapId = 530, X = 10, Y = 20, Z = 37,
                HasCompleteQuestLog = true, HasAuthoritativeCompletions = history,
                CompletedQuestIds = rewarded ?? Array.Empty<uint>(), AcceptedQuests = accepted,
                CarriedItemCounts = new Dictionary<int, long> { [991010] = 1 } };
            PropertyInfo? property = typeof(QuestSchedulerSnapshot).GetProperty("RawQuestStates");
            if (property != null) property.SetValue(snapshot, rawAvailable ? states : null);
            return snapshot;
        }
        internal QuestScheduleResult Schedule(QuestSchedulerSnapshot snapshot, Action<string>? log = null) =>
            QuestScheduler.MaterializeSchedule(Load(), snapshot, _ => new QuestRecoveryDecision { MayAttempt = true, State = QuestRecoveryState.Eligible }, 3, 100, 100, null, null, log);
        internal bool Pickup(QuestSchedulerSnapshot snapshot) => Schedule(snapshot).Plan.Any(p => p.Quest.Id == Subject && p.Stage == QuestWorkStage.Pickup);
        internal void Corrupt(string fault)
        {
            switch (fault)
            {
                case "foreign-quest": Contract["QuestId"] = 999999; break;
                case "duplicate-subject": F.Pack["QuestAvailabilityConditions"]!.AsArray().Add(Contract.DeepClone()); break;
                case "unsupported-type": Condition["Type"] = 2; break;
                case "empty-groups": Contract["Groups"] = new JsonArray(); break;
                case "empty-conditions": Contract["Groups"]![0]!["Conditions"] = new JsonArray(); break;
                case "duplicate-group": Contract["Groups"]!.AsArray().Add(Contract["Groups"]![0]!.DeepClone()); break;
                case "duplicate-condition": Contract["Groups"]![0]!["Conditions"]!.AsArray().Add(Condition.DeepClone()); break;
                case "missing-reference": Contract["ReferencedQuests"] = new JsonArray(); break;
                case "repeatable-reference": Contract["ReferencedQuests"]![0]!["SpecialFlags"] = 1; break;
                case "seasonal-reference": Contract["ReferencedQuests"]![0]!["QuestSortID"] = -22; break;
                case "auto-complete-reference": Contract["ReferencedQuests"]![0]!["QuestType"] = 0; break;
                case "bad-mask": Condition["Type"] = 47; Condition["Value2"] = 4; break;
                case "foreign-target-field": Condition["Target"] = 1; break;
                case "negative-not-bool": Condition["Negative"] = 1; break;
                case "unexpected-value": Condition["Value3"] = 1; break;
                case "empty-source": Contract["SourceRef"] = ""; break;
            }
        }
        public void Dispose() => F.Dispose();
    }
}
