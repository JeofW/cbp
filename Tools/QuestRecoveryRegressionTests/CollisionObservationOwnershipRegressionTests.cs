using Vector3 = Styx.Logic.Pathing.WoWPoint;
using System.Reflection;
using System.Runtime.CompilerServices;
using Styx.Helpers;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles;
using Styx.WoWInternals;

// Actual collision confirmation and MeshNavigator mutation owners. World hits
// and reentrant callbacks are explicit inputs; no real terrain is raycast here.
internal static class CollisionObservationOwnershipRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int cases, passed, assertions, unexpected;

    [ModuleInitializer]
    internal static void Run()
    {
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Case("nonfinite collision height " + invalid, () =>
            {
                var tracker = new LiveCollisionTracker();
                var point = new Vector3(12, 10, invalid);
                Check(!tracker.Observe(true, point, 2, 6) && !tracker.Observe(true, point, 2, 6),
                    "unknown intersection height authorized a collision");
            });
            Case("nonfinite collision distance " + invalid, () =>
            {
                var tracker = new LiveCollisionTracker();
                Check(!tracker.Observe(true, new Vector3(12, 10, 10), invalid, 6)
                    && !tracker.Observe(true, new Vector3(12, 10, 10), invalid, 6), "unknown hit distance became confirmed");
            });
            Case("nonfinite probe length " + invalid, () =>
            {
                var tracker = new LiveCollisionTracker();
                Check(!tracker.Observe(true, new Vector3(12, 10, 10), 2, invalid)
                    && !tracker.Observe(true, new Vector3(12, 10, 10), 2, invalid), "unknown probe length became confirmed");
            });
        }
        Case("different floors cannot corroborate one obstruction", () =>
        {
            var tracker = new LiveCollisionTracker();
            Check(!tracker.Observe(true, new Vector3(12, 10, 10), 2, 6), "first hit already confirmed");
            Check(!tracker.Observe(true, new Vector3(12, 10, 30), 2, 6), "another floor confirmed an old obstruction");
            Check(tracker.Observe(true, new Vector3(12, 10, 30), 2, 6), "two consistent upper-floor hits were rejected");
        });
        Case("no output point from a failed trace cannot confirm", () =>
        {
            var tracker = new LiveCollisionTracker();
            Check(!tracker.Observe(true, Vector3.Zero, 2, 6) && !tracker.Observe(true, Vector3.Zero, 2, 6),
                "empty native intersection output became a collision");
        });
        Case("nearby three-dimensional hits retain normal confirmation", () =>
        {
            var tracker = new LiveCollisionTracker();
            Check(!tracker.Observe(true, new Vector3(12, 10, 10), 2, 6)
                && tracker.Observe(true, new Vector3(12.3f, 10.2f, 10.2f), 2, 6), "normal repeated collision was lost");
        });
        Case("invalid observation breaks a confirmation sequence", () =>
        {
            var tracker = new LiveCollisionTracker();
            Check(!tracker.Observe(true, new Vector3(12, 10, 10), 2, 6), "first observation confirmed");
            Check(!tracker.Observe(true, new Vector3(12, 10, float.NaN), 2, 6), "invalid observation confirmed");
            Check(!tracker.Observe(true, new Vector3(12, 10, 10), 2, 6), "a hit before unknown state was borrowed");
            Check(tracker.Observe(true, new Vector3(12, 10, 10), 2, 6), "fresh repeat failed");
        });
        foreach (string change in new[] { "path", "player", "map", "mover", "provider", "stuck-handler" })
            Case("collision log callback replacement / " + change, () => MutationGuard(change));
        Case("unchanged collision retains bounded route recovery", () => MutationGuard(null));
        Case("collision observer cancellation propagates", () => ResetCancellation());
        Console.WriteLine($"Collision observation/ownership cases: {passed}/{cases}; assertions={assertions}; unexpected={unexpected}; actual tracker and MeshNavigator owner with controlled observations; no game.");
        if (assertions != 0 || unexpected != 0) throw new InvalidOperationException("Collision observation/ownership regression failures");
    }

    private static void MutationGuard(string? mutation)
    {
        using var actor = new RoutineActorFixture();
        var originalMover = Navigator.PlayerMover;
        var originalProvider = Navigator.NavigationProvider;
        var navigator = new MeshNavigator();
        var spy = new RecordingStuck();
        var originalPath = new[] { new WoWPoint(10, 10, 10), new WoWPoint(40, 10, 10) };
        var replacement = new[] { new WoWPoint(10, 10, 10), new WoWPoint(80, 20, 10) };
        var obstruction = new WoWPoint(12340 + cases * 10, -21111, 50);
        var previousSpots = BlackspotManager.Blackspots.ToArray();
        Set(navigator, "_stuckHandler", spy);
        navigator.OverrideCurrentPath(originalPath);
        bool callback = false;
        Action<LogLevel, string> onLog = (_, message) =>
        {
            if (callback || !message.Contains("Confirmed live collision")) return;
            callback = true;
            switch (mutation)
            {
                case "path": navigator.OverrideCurrentPath(replacement); break;
                case "player": ObjectManager.Me = null; break;
                case "map": SetMap(530); break;
                case "mover": Navigator.PlayerMover = new RecordingMover(); break;
                case "provider": Navigator.NavigationProvider = new MeshNavigator(); break;
                case "stuck-handler": Set(navigator, "_stuckHandler", new RecordingStuck()); break;
            }
        };
        try
        {
            Logging.OnMessageLogged += onLog;
            navigator.ApplyConfirmedLiveCollision(obstruction, 2f);
            Check(callback, "the actual collision logging boundary did not execute");
            if (mutation == null)
            {
                Check(!navigator.HasActivePath && spy.Resets == 1, "confirmed current collision did not retire its path once");
                Check(BlackspotManager.Blackspots.Any(spot => spot.Location == obstruction), "confirmed region is missing");
            }
            else
            {
                Check(navigator.CurrentPath.SequenceEqual(mutation == "path" ? replacement : originalPath),
                    "stale collision cleared a replacement or revoked route");
                Check(!BlackspotManager.Blackspots.Any(spot => spot.Location == obstruction), "revoked collision created a region");
                Check(spy.Resets == 0, "revoked collision reset an old recovery owner");
            }
        }
        finally
        {
            Logging.OnMessageLogged -= onLog;
            ObjectManager.Me = actor.Player;
            SetMap(1);
            Navigator.PlayerMover = originalMover;
            Navigator.NavigationProvider = originalProvider;
            foreach (var spot in BlackspotManager.Blackspots.Where(spot => !previousSpots.Contains(spot)).ToArray())
                BlackspotManager.RemoveBlackspot(spot);
        }
    }

    private static void ResetCancellation()
    {
        using var actor = new RoutineActorFixture();
        var navigator = new MeshNavigator();
        navigator.OverrideCurrentPath(new[] { new WoWPoint(10, 10, 10), new WoWPoint(40, 10, 10) });
        var spot = new WoWPoint(16001, -22001, 50);
        var saved = BlackspotManager.Blackspots.ToArray();
        Set(navigator, "_stuckHandler", new CancellingStuck());
        try
        {
            bool cancelled = false;
            try { navigator.ApplyConfirmedLiveCollision(spot, 2f); }
            catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "collision recovery swallowed cancellation");
        }
        finally
        {
            foreach (var region in BlackspotManager.Blackspots.Where(value => !saved.Contains(value)).ToArray())
                BlackspotManager.RemoveBlackspot(region);
        }
    }
    private static void SetMap(uint map)
    {
        var cache = (ThreadLocal<Dictionary<IntPtr, byte[]>>)typeof(GreenMagic.Memory).GetField("_cache", Hidden)!
            .GetValue(ObjectManager.Wow)!;
        cache.Value![new IntPtr(0xBD088C)] = BitConverter.GetBytes(map);
    }
    private static void Set(object owner, string field, object value) => owner.GetType().GetField(field, Hidden)!.SetValue(owner, value);
    private static void Case(string name, Action test)
    {
        cases++;
        try { test(); passed++; }
        catch (ExpectedFailure error) { assertions++; Console.WriteLine("FAIL collision ownership " + name + ": " + error.Message); }
        catch (Exception error) { unexpected++; Console.WriteLine("ERROR collision ownership " + name + ": " + error); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new ExpectedFailure(message); }
    private sealed class ExpectedFailure(string message) : Exception(message);
    private class RecordingStuck : StuckHandler
    {
        internal int Resets;
        public override bool IsStuck() => false;
        public override void Unstick() { }
        public override void Reset() { Resets++; }
    }
    private sealed class CancellingStuck : RecordingStuck
    {
        public override void Reset() => throw new OperationCanceledException("controlled collision cancellation");
    }
    private sealed class RecordingMover : IPlayerMover
    {
        public void Move(WoWMovement.MovementDirection direction) { }
        public void MoveTowards(WoWPoint point) { }
        public void MoveStop() { }
    }
}
