using System.Reflection;
using System.Runtime.CompilerServices;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Tripper.Navigation;

internal static class TerminalRouteOwnershipRegressionTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly WoWPoint Origin = new(-1135.72f, 142.12f, 91.15f);
    private static readonly WoWPoint Grod = new(-1152.76f, 71.41f, 145.87f);

    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new (string Name, Action Test)[]
        {
            ("upper-floor partial exhaustion never commands physical unsticking", () => NoUnstick(Grod)),
            ("lower-floor partial exhaustion never commands physical unsticking", () => NoUnstick(Origin.Add(20, 0, -55))),
            ("same-floor path exhaustion alone is not physical obstruction", () => NoUnstick(Origin.Add(20, 0, 0))),
            ("a complete route retains its successful terminal result", CompleteControl),
            ("partial exhaustion exposes an unresolved vertical-access reason", () => Reason(Grod, 0x40000040, "VerticalAccessUnresolved")),
            ("native node exhaustion remains a search-resource failure", () => Reason(Grod, 0x40000060, "SearchResourceLimit")),
            ("native output truncation is not proven unreachable", () => Reason(Origin.Add(20,0,0), 0x40000050, "SearchResourceLimit")),
            ("same-floor partial search keeps a distinct reason", () => Reason(Origin.Add(20,0,0), 0x40000040, "PartialPath")),
            ("unchanged terminal request is deferred but expires exactly", RetryExpiry),
            ("changed request bypasses terminal retry deferral", () => Bypass(Origin, Grod.Add(3,0,0), 0, false)),
            ("changed player position bypasses terminal retry deferral", () => Bypass(Origin.Add(3,0,0), Grod, 0, false)),
            ("changed map bypasses terminal retry deferral", () => Bypass(Origin, Grod, 1, false)),
            ("clock rollback cannot extend terminal retry deferral", () => Bypass(Origin, Grod, 0, true)),
            ("navigation Clear revokes terminal retry evidence", Clear),
            ("a replacement path revokes terminal retry evidence", Replacement),
            ("repeated terminal observations cannot slide the deadline", StableDeadline),
            ("invalid terminal coordinates never become success or unsticking", InvalidCoordinates),
            ("a validated active prefix is not discarded as a terminal failure", ActivePrefix)
        };
        var failures = new List<string>();
        foreach (var test in cases)
        {
            try { test.Test(); Console.WriteLine("PASS: " + test.Name); }
            catch (Exception e)
            {
                while (e is TargetInvocationException t && t.InnerException != null) e = t.InnerException;
                failures.Add(test.Name + ": " + e.Message);
                Console.Error.WriteLine("FAIL: " + failures[^1]);
            }
        }
        Console.WriteLine($"Terminal route ownership: {cases.Length - failures.Count}/{cases.Length} passed. Actual MeshNavigator owner; no game attached.");
        if (failures.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
    }

    private static (MeshNavigator Nav, SpyStuck Spy) Fixture(WoWPoint target, bool partial = true)
    {
        var nav = new MeshNavigator();
        var spy = new SpyStuck();
        Set(nav, "_stuckHandler", spy);
        Set(nav, "_destination", target);
        Set(nav, "_isPartialPath", partial);
        // Prime the real old tracker without a wall-clock sleep. A repair may retire
        // this tracker; it must not change what an exhausted route is allowed to do.
        if (typeof(MeshNavigator).GetField("_terminalPartialRecovery", Private)?.GetValue(nav) is TerminalPartialPathRecovery old)
        {
            var now = DateTime.UtcNow;
            old.Observe(Origin, target, now.AddSeconds(-3.3));
            old.Observe(Origin, target, now.AddSeconds(-2.3));
            old.Observe(Origin, target, now.AddSeconds(-1.3));
        }
        return (nav, spy);
    }

    private static void Set(MeshNavigator nav, string name, object value) =>
        (typeof(MeshNavigator).GetField(name, Private) ?? throw new InvalidOperationException("Missing owner state: " + name)).SetValue(nav, value);
    private static MoveResult Finish(MeshNavigator nav, WoWPoint? position = null) =>
        (MoveResult)typeof(MeshNavigator).GetMethod("CompletePathOrRecover", Private)!.Invoke(nav, new object[] { new Player(position ?? Origin) })!;
    private static DateTime Deadline(MeshNavigator nav) => (DateTime)(typeof(MeshNavigator).GetProperty("NextRouteRetryUtc")
        ?? throw new InvalidOperationException("Terminal retry deadline is not observable")).GetValue(nav)!;
    private static bool Deferred(MeshNavigator nav, WoWPoint origin, WoWPoint target, uint map, DateTime now) =>
        (bool)(typeof(MeshNavigator).GetMethod("ShouldDeferTerminalRetry", Private)
        ?? throw new InvalidOperationException("Exhausted route requests have no bounded retry owner"))
        .Invoke(nav, new object[] { origin, target, map, now })!;
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static void NoUnstick(WoWPoint target)
    {
        var (nav, spy) = Fixture(target);
        var result = Finish(nav);
        Check(spy.Calls == 0 && result == MoveResult.Failed,
            $"an exhausted search is not a commanded ground obstruction: result={result}, unstick={spy.Calls}");
    }
    private static void CompleteControl()
    {
        var (nav, spy) = Fixture(Grod, false);
        Check(Finish(nav) == MoveResult.ReachedDestination && spy.Calls == 0, "complete routes must keep their existing result");
    }
    private static void Reason(WoWPoint target, uint status, string expected)
    {
        var (nav, spy) = Fixture(target);
        Set(nav, "_lastPathStatus", new Status(status));
        Check(Finish(nav) == MoveResult.Failed && spy.Calls == 0, "search exhaustion must not enter physical recovery");
        string? actual = typeof(MeshNavigator).GetProperty("LastRouteFailure")?.GetValue(nav)?.ToString();
        Check(actual == expected, $"expected {expected}, observed {actual ?? "missing reason"}");
    }
    private static void RetryExpiry()
    {
        var (nav, _) = Fixture(Grod); Finish(nav);
        var until = Deadline(nav);
        Check(until > DateTime.UtcNow && until <= DateTime.UtcNow.AddSeconds(5), "retry must be positive and bounded to at most five seconds");
        Check(Deferred(nav, Origin, Grod, 0, until.AddTicks(-1)), "identical request must not re-query immediately");
        Check(!Deferred(nav, Origin, Grod, 0, until), "expiry boundary must allow another search");
    }
    private static void Bypass(WoWPoint origin, WoWPoint target, uint map, bool rollback)
    {
        var (nav, _) = Fixture(Grod); Finish(nav);
        Check(!Deferred(nav, origin, target, map, rollback ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow), "changed context must not inherit a retry hold");
    }
    private static void Clear()
    {
        var (nav, _) = Fixture(Grod); Finish(nav); nav.Clear();
        Check(Deadline(nav) == DateTime.MinValue && !Deferred(nav, Origin, Grod, 0, DateTime.UtcNow), "Clear must invalidate route evidence and deadline");
    }
    private static void Replacement()
    {
        var (nav, _) = Fixture(Grod); Finish(nav);
        nav.OverrideCurrentPath(new[] { Origin, Origin.Add(10,0,0) });
        Check(!Deferred(nav, Origin, Grod, 0, DateTime.UtcNow), "an explicitly replaced route must not be blocked by old terminal evidence");
    }
    private static void StableDeadline()
    {
        var (nav, _) = Fixture(Grod); Finish(nav); var until = Deadline(nav);
        for (int i = 0; i < 20; i++) Finish(nav);
        Check(Deadline(nav) == until, "observing the same exhausted route must not renew its hold forever");
    }
    private static void InvalidCoordinates()
    {
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            var (nav, spy) = Fixture(new WoWPoint(bad,1,1), false);
            Check(Finish(nav) == MoveResult.Failed && spy.Calls == 0, "non-finite destinations cannot become complete");
        }
    }
    private static void ActivePrefix()
    {
        var (nav, spy) = Fixture(Grod);
        nav.OverrideCurrentPath(new[] { Origin, Origin.Add(10,0,0) });
        nav.DiscardExhaustedPath();
        Check(nav.HasActivePath && nav.CurrentPath.Count == 2 && spy.Calls == 0, "usable path prefixes must remain consumable");
    }
    private sealed class Player : LocalPlayer
    {
        private readonly WoWPoint _position;
        internal Player(WoWPoint point) : base(0) { _position = point; }
        public override WoWPoint Location => _position;
    }
    private sealed class SpyStuck : StuckHandler
    {
        internal int Calls;
        public override bool IsStuck() => false;
        public override void Unstick() { Calls++; }
    }
}
