using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using TreeSharp;
using WholesomeAQ;

// Reuse the real loaded-publication/full-root fixture, including its alive-player
// observations. Only callback faults and observed lifecycle/rest state are controlled.
// These assertions run before teardown and count cleanup before any support effect.
internal static class QuestRootOwnerBoundaryRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed class AssertionFailure : Exception
    { internal AssertionFailure(string message) : base(message) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Root owner-boundary tests require Windows x86.");
        var cases = new List<(string Name, System.Action Test)>
        {
            ("ordinary exclusivity observation cannot mask cleanup cancellation", () => SignalCase(0, 1)),
            ("ordinary exclusivity observation cannot mask cleanup interruption", () => SignalCase(0, 2)),
            ("observation cancellation survives an ordinary cleanup error", () => SignalCase(1, 0)),
            ("observation interruption survives an ordinary cleanup error", () => SignalCase(2, 0)),
            ("first observation cancellation wins over later cleanup interruption", () => SignalCase(1, 2)),
            ("first observation interruption wins over later cleanup cancellation", () => SignalCase(2, 1)),
            ("ordinary observation failure cleans the quest without support fallback", OrdinaryControl),
            ("stopped state observed inside exclusivity prevents same-cycle support", () => LifecycleCase(false, false)),
            ("stopped state reached during preemption cleanup prevents new combat", () => LifecycleCase(true, false)),
            ("rest pause observed inside exclusivity prevents same-cycle support", () => LifecycleCase(false, true)),
            ("rest pause reached during preemption cleanup prevents new combat", () => LifecycleCase(true, true)),
            ("refresh cancellation alone still allows protective combat after cleanup", () => RefreshControl(false)),
            ("pending refresh during cleanup retains protective combat", () => RefreshControl(true)),
        };
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { item.Test(); passed++; Console.WriteLine("PASS root owner-boundary: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL root owner-boundary assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR root owner-boundary fixture/owner: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Root owner-boundary scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual published root/executor with callback faults and observed lifecycle state; no game attached.");
        if (assertions + unexpected != 0)
            throw new InvalidOperationException($"Root owner-boundary regressions: assertions={assertions}; unexpected={unexpected}");
    }

    private static Exception Error(int kind) => kind == 1 ? new OperationCanceledException("root cancellation")
        : kind == 2 ? new ThreadInterruptedException("root interruption")
        : new InvalidOperationException("ordinary root observation/cleanup error");

    private static void SignalCase(int observationKind, int cleanupKind)
    {
        using var h = new Harness(); h.StartQuest(); h.EnableService();
        Exception observation = Error(observationKind), cleanup = Error(cleanupKind);
        int observations = 0, cleanups = 0;
        h.OnExclusive = () => { observations++; throw observation; };
        h.OnCleanup = () => { cleanups++; throw cleanup; };
        Exception? actual = null;
        try { h.Step(); } catch (Exception error) { actual = error; }
        finally { h.OnExclusive = null; h.OnCleanup = null; }
        Check(observations == 1 && cleanups == 1, "actual observation/cleanup callbacks were not both reached exactly once");
        h.AssertStoppedWithoutSupport();
        Exception expected = observationKind == 0 ? cleanup : observation;
        Check(ReferenceEquals(actual, expected), "first authoritative stop signal was lost: " + (actual?.GetType().Name ?? "no exception"));
    }

    private static void OrdinaryControl()
    {
        using var h = new Harness(); h.StartQuest(); h.EnableService();
        int callbacks = 0; h.OnExclusive = () => { callbacks++; throw Error(0); };
        RunStatus status = h.Step(); h.OnExclusive = null;
        Check(callbacks == 1 && status == RunStatus.Failure, "ordinary error no longer follows the retained failure contract");
        h.AssertStoppedWithoutSupport();
    }

    private static void LifecycleCase(bool duringCleanup, bool rest)
    {
        using var h = new Harness(); h.StartQuest(); h.EnableCombat();
        int callbacks = 0;
        System.Action transition = () =>
        {
            callbacks++;
            if (rest) Set(h.Bot, "_restingPaused", true);
            else { Set(h.Bot, "_stopped", true); h.Gate.Stop(); }
        };
        if (duringCleanup) h.OnCleanup = transition; else h.OnExclusive = transition;
        RunStatus status = h.Step(); h.OnCleanup = null; h.OnExclusive = null;
        Check(callbacks == 1, "observed lifecycle transition was not reached exactly once");
        h.AssertStoppedWithoutSupport();
        Check(status == RunStatus.Failure, "ended lifecycle/rest cycle did not fail closed");
    }

    private static void RefreshControl(bool pendingOnly)
    {
        using var h = new Harness(); h.StartQuest(); h.EnableCombat();
        int callbacks = 0;
        h.OnCleanup = () =>
        {
            callbacks++;
            if (pendingOnly) Check(h.Gate.TryRequest(), "pending refresh was not accepted");
            else h.Gate.Stop();
        };
        RunStatus status = h.Step(); h.OnCleanup = null;
        Check(callbacks == 1 && status == RunStatus.Success && h.CombatEffects == 1,
            "quest-refresh state was incorrectly promoted to a whole-root veto");
        Check(h.Effects == 1 && h.Cleanups == 1 && h.ServiceEffects == 0 && h.RoamEffects == 0,
            "protective control repeated quest effects or missed cleanup");
    }

    private sealed class Harness : IDisposable
    {
        private readonly object source, behavior, body, combat, service, roam;
        internal readonly WholesomeAutoQuest Bot;
        internal readonly RefreshGate Gate;
        internal Harness()
        {
            Type type = typeof(QuestRootPreemptionRegressionTests).GetNestedType("Case", BindingFlags.NonPublic)!;
            source = Activator.CreateInstance(type, true)!;
            behavior = Get(source, "Behavior"); body = Get(behavior, "Body");
            combat = Get(source, "Combat"); service = Get(source, "Service"); roam = Get(source, "Roam");
            Bot = (WholesomeAutoQuest)Get(source, "Bot"); Gate = (RefreshGate)Get(source, "Gate");
            Set(behavior, "Exclusive", true);
        }
        internal int Effects => (int)Get(body, "Effects");
        internal int Cleanups => (int)Get(body, "Cleanups");
        internal int CombatEffects => (int)Get(combat, "Effects");
        internal int ServiceEffects => (int)Get(service, "Effects");
        internal int RoamEffects => (int)Get(roam, "Effects");
        internal System.Action? OnExclusive { set => Set(behavior, "OnExclusive", value); }
        internal System.Action? OnCleanup { set => Set(body, "OnCleanup", value); }
        internal void StartQuest() => Call(source, "StartQuest");
        internal void EnableCombat() => Call(source, "EnableCombat");
        internal void EnableService() => Call(source, "EnableService");
        internal RunStatus Step() => (RunStatus)Call(source, "Step")!;
        internal void AssertStoppedWithoutSupport() => Check(Effects == 1 && Cleanups == 1 && CombatEffects == 0 && ServiceEffects == 0 && RoamEffects == 0,
            $"old root admitted effects or missed cleanup: quest={Effects}, cleanup={Cleanups}, combat={CombatEffects}, service={ServiceEffects}, roam={RoamEffects}");
        public void Dispose() { OnExclusive = null; OnCleanup = null; ((IDisposable)source).Dispose(); }
    }
    private static object Get(object owner, string name) => owner.GetType().GetField(name, Hidden)!.GetValue(owner)!;
    private static void Set(object owner, string name, object? value) => owner.GetType().GetField(name, Hidden)!.SetValue(owner, value);
    private static object? Call(object owner, string name)
    {
        try { return owner.GetType().GetMethod(name, Hidden)!.Invoke(owner, null); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new AssertionFailure(message); }
}
