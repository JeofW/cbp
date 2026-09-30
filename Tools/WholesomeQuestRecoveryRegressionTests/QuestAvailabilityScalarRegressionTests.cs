using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;

internal static class QuestAvailabilityScalarRegressionTests
{
    private const int Subject = 991001, Parent = 991099;
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;
    private sealed class Failure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        var tests = new List<(string Name, Action<Probe> Body)>();
        foreach (int comparison in Enumerable.Range(0, 5))
            foreach (int observed in new[] { 74, 75, 76 })
                foreach (bool negative in new[] { false, true })
                {
                    int c = comparison, level = observed; bool n = negative;
                    tests.Add(($"level comparison={c} observed={level} negative={n}", p =>
                    {
                        p.Level(c, n); bool expected = c switch { 0 => level == 75, 1 => level > 75, 2 => level < 75, 3 => level >= 75, 4 => level <= 75, _ => throw new Failure("invalid oracle") };
                        Check(p.Pickup(p.F.Snapshot(level: level)) == (n ? !expected : expected), "primary level comparison disagrees");
                    }));
                }
        tests.Add(("level-only contract needs no referenced quest", p =>
        { p.Level(); p.F.Contract["ReferencedQuests"] = new JsonArray(); Check(p.Pickup(p.F.Snapshot(level: 75)), "numeric level was treated as a quest ID"); }));
        foreach (bool negative in new[] { false, true })
        {
            bool captured = negative;
            tests.Add(("unknown level stays unknown under negation=" + captured, p =>
            {
                p.Level(3, captured); var db = p.Load();
                var decision = QuestAvailabilityPolicy.Evaluate(db.Quests.Single(), p.F.Snapshot(level: 0));
                Check(decision.Status == "observation-unknown" && decision.Rejection != null, "unknown level became a condition result");
            }));
        }
        tests.Add(("auto-complete permanent reward admits pickup", p =>
        { p.AutoComplete(); Check(p.Pickup(p.F.Snapshot(rewarded: new uint[] { Parent })), "source-backed permanent reward was excluded by quest type"); }));
        tests.Add(("auto-complete readiness is not reward history", p =>
        { p.AutoComplete(); Check(!p.Pickup(p.F.Snapshot(states: new() { [Parent] = 1 })), "computed or raw readiness donated reward history"); }));
        tests.Add(("auto-complete negation cannot turn unknown history into absence", p =>
        { p.AutoComplete(); p.F.Condition["Negative"] = true; Check(!p.Pickup(p.F.Snapshot(history: false)), "unknown became negated permission"); Check(p.Pickup(p.F.Snapshot()), "authoritative absent reward did not satisfy negation"); }));
        foreach (int type in new[] { 9, 14, 28, 47 })
        {
            int captured = type;
            tests.Add(("auto-complete raw-state type rejected=" + captured, p =>
            { p.AutoComplete(); p.F.Condition["Type"] = captured; p.F.Condition["Value2"] = captured == 47 ? 1 : 0; p.Rejected(); }));
        }
        foreach (int comparator in new[] { -1, 5 })
        { int value = comparator; tests.Add(("invalid comparison rejected=" + value, p => { p.Level(value); p.Rejected(); })); }
        tests.Add(("level observation reaches the actual current snapshot", p =>
        {
            using var native = new QuestDatasetObservationFixture();
            native.SetQuest(Subject, "scalar observation", 75, new int[4], new int[4], new int[6], new int[6]);
            var snapshot = (QuestSchedulerSnapshot?)Invoke(typeof(QuestScheduler).GetMethod("CaptureAvailabilitySnapshot", Hidden)!, null, native.Player, native.Player.QuestLog);
            Check(snapshot != null && snapshot.PlayerLevel == 75, "actual level read missing from current availability snapshot");
        }));
        tests.Add(("level guard loses permission after the actual player's level changes", p =>
        {
            p.Level(); var db = p.Load();
            using var native = new QuestDatasetObservationFixture();
            native.SetQuest(Subject, "scalar guard", 75, new int[4], new int[4], new int[6], new int[6]);
            var plan = new[] { new QuestPlanEntry { Quest = db.Quests.Single(), Stage = QuestWorkStage.Pickup } };
            var guard = (Func<bool>)Invoke(typeof(QuestScheduler).GetMethod("CreateAvailabilityGuard", Hidden)!, null, plan, native.Player, native.Player.QuestLog)!;
            Check(guard(), "matching current level rejected");
            native.SetQuest(Subject, "scalar guard", 74, new int[4], new int[4], new int[6], new int[6]);
            Check(!guard(), "changed observed level retained pickup permission");
        }));
        tests.Add(("accepted turn-in does not become level pickup gated", p =>
        { p.Level(); Check(p.Schedule(p.F.Snapshot(subjectReady: true, level: 1)).Plan.Any(x => x.Stage == QuestWorkStage.TurnIn), "availability gated already accepted turn-in"); }));
        int passed = 0, failed = 0, errors = 0;
        foreach (var test in tests)
        {
            try { using var p = new Probe(); test.Body(p); passed++; Console.WriteLine("PASS availability scalar: " + test.Name); }
            catch (Failure e) { failed++; Console.Error.WriteLine("FAIL availability scalar: " + test.Name + ": " + e.Message); }
            catch (Exception e) { errors++; Console.Error.WriteLine("ERROR availability scalar: " + test.Name + ": " + e); }
        }
        Console.WriteLine($"Availability scalar scenarios: {passed}/{tests.Count}; assertions={failed}; unexpected={errors}; actual loader/scheduler/current player observation; no game.");
        if (failed + errors != 0) throw new InvalidOperationException("Availability scalar regression");
    }

    private sealed class Probe : IDisposable
    {
        internal readonly QuestAvailabilityConditionRegressionTests.Fixture F = new();
        internal void AutoComplete() => F.Contract["ReferencedQuests"]![0]!["QuestType"] = 0;
        internal void Level(int comparison = 3, bool negative = false)
        { F.Condition["Type"] = 27; F.Condition["Value1"] = 75; F.Condition["Value2"] = comparison; F.Condition["Negative"] = negative; }
        internal QuestDatabase Load()
        { F.Write(); try { return F.F.Load(); } catch (InvalidDataException e) { throw new Failure("supported scalar contract rejected: " + e.Message); } }
        internal void Rejected()
        { F.Write(); bool rejected = false; try { F.F.Load(); } catch (InvalidDataException) { rejected = true; } Check(rejected, "invalid contract accepted"); }
        internal QuestScheduleResult Schedule(QuestSchedulerSnapshot snapshot) => QuestScheduler.MaterializeSchedule(Load(), snapshot,
            _ => new QuestRecoveryDecision { MayAttempt = true, State = QuestRecoveryState.Eligible }, 3, 100, 100);
        internal bool Pickup(QuestSchedulerSnapshot snapshot) => Schedule(snapshot).Plan.Any(x => x.Quest.Id == Subject && x.Stage == QuestWorkStage.Pickup);
        public void Dispose() => F.Dispose();
    }
    private static object? Invoke(MethodInfo method, object? target, params object?[] args)
    { try { return method.Invoke(target, args); } catch (TargetInvocationException e) when (e.InnerException != null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; } }
    private static void Check(bool result, string message) { if (!result) throw new Failure(message); }
}
