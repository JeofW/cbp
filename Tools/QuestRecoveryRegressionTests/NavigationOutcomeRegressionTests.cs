using System.Reflection;
using System.Runtime.CompilerServices;
using Styx.Logic.Pathing;
using Styx.WoWInternals;

internal static class NavigationOutcomeRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        TestMoveOutcomeRecordsAnActualReturnedResult();
        TestSuccessfulAndFailedPathsCannotShareStaleEvidence();
        TestExhaustedPathCanRegenerate();
        TestStationaryPartialEndpointEscalatesIntoRecovery();
    }

    private static void TestMoveOutcomeRecordsAnActualReturnedResult()
    {
        var nav = new MeshNavigator();
        Assert(nav.LastMoveResult == null, "a new navigator has no observed failure");
        var before = DateTime.UtcNow;
        var result = nav.MoveTo(WoWPoint.Zero);
        Assert(nav.LastMoveResult == result && result == MoveResult.Failed,
            "the snapshot must describe the actual MoveTo result");
        Assert(nav.LastMoveAttemptUtc >= before && nav.LastMoveAttemptSequence == 1,
            "the result needs a fresh timestamp and sequence");
        nav.Clear();
        Assert(nav.LastMoveResult == null && nav.LastMoveDestination == WoWPoint.Zero
            && nav.LastMoveAttemptUtc == DateTime.MinValue,
            "clearing navigation must invalidate old failure evidence");
    }

    private static void TestSuccessfulAndFailedPathsCannotShareStaleEvidence()
    {
        var nav = new MeshNavigator();
        var from = new WoWPoint(10, 10, 10);
        var target = new WoWPoint(20, 10, 10);
        nav.RecordMoveOutcome(from, target, MoveResult.PathGenerationFailed);
        Assert(nav.LastMoveResult == MoveResult.PathGenerationFailed && nav.LastMoveDestination == target
            && nav.LastMoveOrigin == from, "failure must retain its origin and endpoint");
        nav.RecordMoveOutcome(from, target, MoveResult.ReachedDestination);
        Assert(nav.LastMoveResult == MoveResult.ReachedDestination && nav.LastMoveAttemptSequence == 2,
            "successful completion must replace previous failure evidence");
    }

    private static void TestExhaustedPathCanRegenerate()
    {
        var nav = new MeshNavigator();
        nav.OverrideCurrentPath(new[] { new WoWPoint(10, 10, 10), new WoWPoint(20, 10, 10) });
        nav.DiscardExhaustedPath();
        Assert(nav.CurrentPath.Count == 2, "an active path must survive the exhaustion check");
        typeof(MeshNavigator).GetField("_currentPathIndex", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(nav, 2);
        Assert(!nav.HasActivePath, "fixture must model a consumed final waypoint");
        nav.DiscardExhaustedPath();
        Assert(nav.CurrentPath.Count == 0 && nav.CurrentPathIndex == 0,
            "a consumed path must become eligible for regeneration instead of permanent failure");
    }

    private static void TestStationaryPartialEndpointEscalatesIntoRecovery()
    {
        var tracker = new TerminalPartialPathRecovery();
        var now = new DateTime(2026, 9, 12, 2, 35, 52, DateTimeKind.Utc);
        var location = new WoWPoint(-5479.10f, -2394.91f, 57.02f);
        var destination = new WoWPoint(-5454.95f, -2442.79f, 90.02f);

        Assert(!tracker.Observe(location, destination, now),
            "one exhausted partial-path result must not immediately disturb the character");
        Assert(!tracker.Observe(location, destination, now.AddSeconds(1)),
            "a brief partial-route retry must remain eligible for normal regeneration");
        Assert(!tracker.Observe(location, destination, now.AddSeconds(2)),
            "partial-route recovery must require three seconds of stationary evidence");
        Assert(tracker.Observe(location, destination, now.AddSeconds(3)),
            "a stationary exhausted partial route must escalate into stuck recovery instead of stalling forever");

        Assert(!tracker.Observe(location.Add(1f, 0f, 0f), destination, now.AddSeconds(4)),
            "movement produced by recovery must start a fresh observation window");
        Assert(!tracker.Observe(location.Add(1f, 0f, 0f), destination.Add(30f, 0f, 0f), now.AddSeconds(5)),
            "a different navigation request must not inherit the previous route failure");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Navigation outcome regression: " + message);
    }
}
