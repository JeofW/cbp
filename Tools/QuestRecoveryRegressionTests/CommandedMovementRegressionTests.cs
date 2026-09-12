using System;
using System.Runtime.CompilerServices;
using Styx.Logic.Pathing;

internal static class CommandedMovementRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var now = new DateTime(2026, 9, 8, 1, 14, 0, DateTimeKind.Utc);
        var location = new WoWPoint(1722.58f, -661.0491f, 91.39041f);
        var destination = new WoWPoint(1246.34f, -2253.31f, 108.37f);
        var tracker = new CommandedMovementProgressTracker();
        Check(!tracker.ObserveCommand(location, destination, now), "first command cannot establish a stall");
        Check(!tracker.ObserveCommand(location, destination, now.AddSeconds(1)), "one failed pulse is insufficient");
        Check(!tracker.ObserveCommand(location, destination, now.AddSeconds(2)), "allow three seconds for progress");
        Check(tracker.ObserveCommand(location, destination, now.AddSeconds(3)), "repeated stationary commands must detect the recorded CTM cancellation, independently of moving flags");
        Check(!tracker.ObserveCommand(location, destination, now.AddSeconds(4)), "recovery restarts the observation window");
        tracker.Reset();
        for (int i = 0; i < 10; i++)
            Check(!tracker.ObserveCommand(location.Add(i, 0, 0), destination, now.AddSeconds(i)), "walking progress must not trigger recovery");
        tracker.Reset();
        tracker.ObserveCommand(location, destination, now);
        Check(!tracker.ObserveCommand(location, destination, now.AddSeconds(10)), "intentional idle or combat pause must invalidate old commands");
        tracker.ObserveCommand(location, destination, now.AddSeconds(11));
        Check(!tracker.ObserveCommand(location, destination.Add(30, 0, 0), now.AddSeconds(12)), "new destination must start fresh");
        tracker.Reset();
        Check(!tracker.ObserveCommand(location, destination, now.AddSeconds(13)), "arrival or explicit navigation clear must reset evidence");
        var history = new LocalDetourAttemptHistory();
        Check(history.CanAttempt(1, location, now), "first obstruction can attempt a detour");
        history.Record(1, location, now);
        Check(!history.CanAttempt(1, location.Add(2, 0, 0), now.AddSeconds(10)), "landing then returning near the same obstruction must escalate instead of repeating detours");
        Check(history.CanAttempt(1, location.Add(20, 0, 0), now.AddSeconds(10)), "a separate obstruction may try its own detour");
        Check(history.CanAttempt(0, location, now.AddSeconds(10)), "another map must not inherit obstacle history");
        Check(history.CanAttempt(1, location, now.AddMinutes(2)), "temporary obstacle history must expire");
        for (int i = 0; i < 9; i++) history.Record(1, location.Add(i * 20, 0, 0), now);
        Check(history.CanAttempt(1, location, now.AddSeconds(1)), "bounded history must evict the oldest of more than eight departures");
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var navigator = new MeshNavigator();
        var mover = new RecordingMover();
        var previousMover = Navigator.PlayerMover;
        var previousAvoidance = Navigator.NavAvoidWaypointProvider;
        try
        {
            Navigator.PlayerMover = mover;
            Navigator.NavAvoidWaypointProvider = _ => new[] { location.Add(0, 10, 0) };
            typeof(MeshNavigator).GetField("_localConnectorTarget", flags)!.SetValue(navigator, location.Add(2, 0, 0));
            var result = typeof(MeshNavigator).GetMethod("ContinueLocalConnector", flags)!.Invoke(navigator, new object?[] { null, destination });
            Check(result == null && mover.Stops == 1 && mover.Moves == 0,
                "a newly required avoidance route must stop the active connector before issuing further local movement");
            Check((WoWPoint)typeof(MeshNavigator).GetField("_localConnectorTarget", flags)!.GetValue(navigator)! == WoWPoint.Zero,
                "hazard interruption must release the local connector so ordinary avoidance can resume");
        }
        finally
        {
            Navigator.NavAvoidWaypointProvider = previousAvoidance;
            Navigator.PlayerMover = previousMover;
        }
    }

    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException("Commanded movement regression: " + message); }

    private sealed class RecordingMover : IPlayerMover
    {
        internal int Stops, Moves;
        public void Move(Styx.WoWInternals.WoWMovement.MovementDirection direction) => Moves++;
        public void MoveTowards(WoWPoint location) => Moves++;
        public void MoveStop() => Stops++;
    }
}
