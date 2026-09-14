using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using Bots.Quest;
using Bots.Quest.QuestOrder;
using Styx;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles;
using Styx.Logic.Profiles.Quest;
using Styx.WoWInternals.WoWObjects;
using WholesomeAQ;
using Action = System.Action;

// Actual scheduler navigation assessment/cache and actual DoScan/RunPendingRefresh.
// External navigation is controlled and counted; no native path or game is attached.
internal static class QuestNavigationCancellationRegressionTests
{
    private const BindingFlags I = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags S = BindingFlags.Static | BindingFlags.NonPublic;
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string text) : base(text) { } }
    private static SpawnPoint Point() => new SpawnPoint { Map = 1, X = 10, Y = 10, Z = 10 };

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Navigation cancellation tests require Windows x86.");
        var tests = new List<(string Name, Action Test)>
        {
            ("live assessment preserves path-provider cancellation", () => Direct(new OperationCanceledException("path cancel"), false)),
            ("live assessment preserves path-provider interruption", () => Direct(new ThreadInterruptedException("path interrupt"), false)),
            ("live assessment preserves safety-provider cancellation", () => Direct(new OperationCanceledException("safety cancel"), true)),
            ("live assessment preserves safety-provider interruption", () => Direct(new ThreadInterruptedException("safety interrupt"), true)),
            ("cached assessment preserves navigation cancellation", () => Cached(new OperationCanceledException("cached path cancel"), false)),
            ("cached assessment preserves navigation interruption", () => Cached(new ThreadInterruptedException("cached path interrupt"), false)),
            ("cached assessment preserves safety cancellation", () => Cached(new OperationCanceledException("cached safety cancel"), true)),
            ("cached assessment preserves safety interruption", () => Cached(new ThreadInterruptedException("cached safety interrupt"), true)),
            ("ordinary path-provider error remains unknown", () =>
            {
                var previous = Navigator.NavigationProvider; int calls = 0;
                try
                {
                    Navigator.NavigationProvider = new TestNavigationProvider(_ => { calls++; throw new InvalidOperationException("ordinary path failure"); });
                    var result = QuestScheduler.AssessNavigation(Point(), new WoWPoint(10, 10, 10), _ => false);
                    Check(calls == 1 && result.IsKnownReachable == null && result.IsKnownSafe == null, "ordinary path failure stopped being unknown");
                }
                finally { Navigator.NavigationProvider = previous; }
            }),
            ("ordinary safety-provider error remains unknown", () =>
            {
                int calls = 0;
                var result = QuestScheduler.AssessNavigation(Point(), new WoWPoint(10, 10, 10), _ => { calls++; throw new InvalidOperationException("ordinary safety failure"); });
                Check(calls == 1 && result.IsKnownReachable == null && result.IsKnownSafe == null, "ordinary safety failure stopped being unknown");
            }),
            ("ordinary cached path failure is probed once per scan", () =>
            {
                int calls = 0; var assess = Cache(_ => { calls++; throw new InvalidOperationException("ordinary cached failure"); }, _ => false);
                var first = assess(Point()); var second = assess(Point());
                Check(calls == 1 && first.IsKnownReachable == null && second.IsKnownReachable == null, "failure cache retried or fabricated reachability");
            }),
            ("ordinary safety failure cannot erase an explicit unsafe endpoint", () =>
            {
                int probes = 0; var point = Point(); point.IsKnownSafe = false;
                var assess = Cache(_ => { probes++; return new SpawnNavigationAssessment { IsKnownSafe = true }; }, _ => throw new InvalidOperationException("ordinary safety failure"));
                Check(assess(point).IsKnownSafe == false && probes == 0, "ordinary failure erased a decisive safety veto");
            }),
            ("actual pending rescan cancellation propagates after its counted navigation call", () => Scan(new OperationCanceledException("rescan cancel"), false)),
            ("actual pending rescan interruption propagates after its counted navigation call", () => Scan(new ThreadInterruptedException("rescan interrupt"), false)),
            ("late cancelled navigation cannot revoke a real replacement publication", () => Scan(new OperationCanceledException("late rescan cancel"), true)),
            ("late interrupted navigation cannot revoke a real replacement publication", () => Scan(new ThreadInterruptedException("late rescan interrupt"), true))
        };
        int assertions = 0, unexpected = 0;
        foreach (var test in tests)
        {
            try { test.Test(); Console.WriteLine("PASS navigation cancellation: " + test.Name); }
            catch (AssertionFailure e) { assertions++; Console.Error.WriteLine("FAIL navigation cancellation assertion: " + test.Name + ": " + e); }
            catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR navigation cancellation fixture/owner: " + test.Name + ": " + e); }
        }
        Console.WriteLine($"Navigation cancellation scenarios: {tests.Count - assertions - unexpected}/{tests.Count}; assertions={assertions}; unexpected={unexpected}; actual assessment/cache/refresh; counted external provider; no game attached.");
        if (assertions != 0 || unexpected != 0)
            throw new InvalidOperationException("Navigation cancellation regressions: assertions=" + assertions + "; unexpected=" + unexpected);
    }

    private static void Direct(Exception signal, bool safety)
    {
        var previous = Navigator.NavigationProvider; int calls = 0;
        try
        {
            Navigator.NavigationProvider = new TestNavigationProvider(_ => { calls++; throw signal; });
            Exception? actual = Capture(() => QuestScheduler.AssessNavigation(Point(), new WoWPoint(10, 10, 10),
                _ => { if (safety) { calls++; throw signal; } return false; }));
            Check(calls == 1, "the intended direct provider was not reached exactly once"); Same(actual, signal);
        }
        finally { Navigator.NavigationProvider = previous; }
    }
    private static void Cached(Exception signal, bool safety)
    {
        int calls = 0;
        var assess = Cache(_ => { calls++; throw signal; }, _ => { if (safety) { calls++; throw signal; } return false; });
        Exception? actual = Capture(() => assess(Point()));
        Check(calls == 1, "the intended cached provider was not reached exactly once"); Same(actual, signal);
    }
    private static Func<SpawnPoint, SpawnNavigationAssessment> Cache(
        Func<SpawnPoint, SpawnNavigationAssessment> navigation, Func<SpawnPoint, bool> safety) =>
        (Func<SpawnPoint, SpawnNavigationAssessment>)Invoke(typeof(QuestScheduler).GetMethod("CreateCachedNavigationAssessment", S)!, null, navigation, safety)!;

    private static void Scan(Exception signal, bool replacement)
    {
        using var c = new ScanCase(); int calls = 0; QuestScheduleResult? published = null;
        Set(c.Fixture, "BeforeNavigation", (Action)(() =>
        {
            Set(c.Fixture, "BeforeNavigation", null); calls++;
            if (replacement)
            {
                c.Gate.Stop(); c.Gate.Start(); c.Run(); c.AssertPublished(); published = c.Scheduler.LastSchedule;
            }
            throw signal;
        }));
        Exception? actual = Capture(c.Run);
        Check(calls == 1, "actual rescan never reached its configured navigation callback");
        if (replacement)
            Check(published != null && ReferenceEquals(published, c.Scheduler.LastSchedule)
                && c.Scheduler.ActiveQuestIds.Contains(867) && c.Scheduler.CurrentProfilePath == c.Output,
                "obsolete cancelled scan revoked the replacement publication");
        else
            Check(c.Scheduler.LastSchedule.Selected.Count == 0 && c.Scheduler.CurrentProfilePath == null
                && c.Scheduler.ActiveQuestIds.Contains(867), "cancelled rescan retained work or released item protection");
        Same(actual, signal);
        Check(c.Gate.TryRequest(), "cancelled scan retained a running/pending refresh lease");
        var next = c.Gate.Begin(); Check(next.HasValue, "next real refresh lease was unavailable"); c.Gate.Complete(next!.Value);
    }
    private sealed class ScanCase : IDisposable
    {
        internal readonly object Fixture;
        internal readonly QuestScheduler Scheduler = null!;
        internal readonly WholesomeAutoQuest Bot = null!;
        internal readonly RefreshGate Gate = null!;
        internal readonly string Output = null!;
        private readonly Bots.Quest.QuestOrder.QuestOrder order;
        private readonly OrderNodeCollection nodes;
        private readonly ForcedBehavior? behavior;
        internal ScanCase()
        {
            Fixture = Activator.CreateInstance(typeof(QuestPublicationRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
            order = QuestState.Instance.Order; nodes = order.Nodes; behavior = order.CurrentBehavior;
            try
            {
                Output = (string)Fixture.GetType().GetProperty("Output", I)!.GetValue(Fixture)!;
                Scheduler = (QuestScheduler)Call(Fixture, "Scheduler", Output)!;
                Bot = (WholesomeAutoQuest)Invoke(typeof(QuestPublicationRegressionTests).GetMethod("Bot", S)!, null, Scheduler)!;
                Gate = (RefreshGate)typeof(WholesomeAutoQuest).GetField("_refreshGate", I)!.GetValue(Bot)!;
                Gate.Start(); Run(); AssertPublished();
            }
            catch { Dispose(); throw; }
        }
        internal void Run() { Check(Gate.TryRequest(), "actual scan request refused"); Call(Bot, "RunPendingRefresh"); }
        internal void AssertPublished() => Check(Scheduler.LastSchedule.Selected.Any(q => q.QuestId == 867)
            && Scheduler.CurrentProfilePath == Output && ProfileManager.XmlLocation == Output && ProfileManager.CurrentProfile != null,
            "actual initial/replacement scan did not publish the control quest");
        public void Dispose()
        {
            try { Gate?.Stop(); order.Nodes = nodes; order.CurrentBehavior = behavior; }
            finally { ((IDisposable)Fixture).Dispose(); }
        }
    }
    private static Exception? Capture(Action action) { try { action(); return null; } catch (Exception error) { return error; } }
    private static void Same(Exception? actual, Exception expected) => Check(ReferenceEquals(actual, expected), "stop signal was swallowed or replaced: " + actual?.GetType().Name);
    private static void Set(object target, string field, object? value) => target.GetType().GetField(field, I)!.SetValue(target, value);
    private static object? Call(object target, string method, params object?[] args) => Invoke(target.GetType().GetMethod(method, I)!, target, args);
    private static object? Invoke(MethodInfo method, object? target, params object?[] args)
    {
        try { return method.Invoke(target, args); }
        catch (TargetInvocationException e) when (e.InnerException != null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new AssertionFailure(message); }
}
