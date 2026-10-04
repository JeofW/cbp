using CommonBehaviors.Actions;
using Styx.Helpers;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.WoWInternals.World;
using TreeSharp;

// Actual action, runtime, geometry and transition machine. Collision responses
// are analytic observations; absent ground never supplies landing permission.
internal static class FlightCorridorCases
{
    internal static void Run(Action<string, Action<ActionMoveToPoi>> test)
    {
        static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        static void Arrange()
        {
            World.Actor.Position = new(22.376114f, 4930.0576f, 136.09196f);
            World.Target.Position = new(99.9843f, 5188.04f, 21.1605f);
            World.Target.Outdoors = true;
            BotPoi.Current.Position = World.Target.Position;
        }
        static void Advance(ActionMoveToPoi action, int count)
        {
            for (int i = 0; i < count && World.ExteriorFlights.Count == 0; i++) action.Tick(null!);
        }
        foreach (string surface in new[] { "missing-mesh", "water", "canopy" })
            test("airborne local corridor does not require landing below: " + surface, action =>
            {
                Arrange(); World.MissingMesh = true;
                World.CollisionOverride = (line, flags) =>
                {
                    bool liquid = (flags & GameWorld.CGWorldFrameHitFlags.HitTestLiquid) != 0;
                    float ground = surface == "canopy" ? 90 : 0;
                    if ((surface != "water" && !liquid || surface == "water" && liquid)
                        && line.Start.Z >= ground && line.End.Z <= ground)
                        return (true, new(line.Start.X, line.Start.Y, ground));
                    return (false, WoWPoint.Empty);
                };
                Advance(action, 4);
                Check(World.ExteriorFlights.Count > 0, "clear air above unavailable landing geometry never produced forward travel");
                Check(World.ExteriorFlights[0].Z >= World.Actor.Position.Z - .01f
                    && World.ExteriorFlights[0].Distance2DSqr(World.Target.Position) < World.Actor.Position.Distance2DSqr(World.Target.Position),
                    "cruise progress guessed a lower floor or moved away from the destination");
                Check(World.Dismounts == 0 && World.Descents == 0 && World.Walks.Count == 0,
                    "clear airborne corridor fabricated a landing or ground handoff");
                Check(World.Rays.Any(ray => ray.Line.Start.Distance2DSqr(ray.Line.End) > 1),
                    "airborne travel omitted its swept corridor collision observations");
                using var record = System.Text.Json.JsonDocument.Parse(World.Diagnostics.Last()["[GroundTransition] ".Length..]);
                Check(record.RootElement.GetProperty("Landing").ValueKind == System.Text.Json.JsonValueKind.Null,
                    "an aerial waypoint was reported as an observed landing");
            });
        foreach (string veto in new[] { "blocked", "unknown", "owner", "grounded" })
            test("air corridor admission preserves " + veto, action =>
            {
                Arrange(); World.MissingMesh = true; World.MissingSupport = true;
                if (veto == "grounded") World.Actor.Flags = 0;
                World.CollisionOverride = (line, _) =>
                {
                    if (line.Start.Distance2DSqr(line.End) <= 1) return (false, WoWPoint.Empty);
                    if (veto == "unknown") throw new ObservationUnavailableException("air-corridor", "controlled unavailable trace");
                    if (veto == "owner") TreeRoot.RunIdentity = new();
                    return veto == "blocked" ? (true, (line.Start + line.End) / 2) : (false, WoWPoint.Empty);
                };
                Advance(action, 12);
                Check(World.ExteriorFlights.Count == 0 && World.Dismounts == 0 && World.Descents == 0,
                    "unobserved or replaced air corridor authorized movement");
            });
        test("nearby destination still needs final landing proof", action =>
        {
            Arrange(); World.Target.Position = World.Actor.Position.Add(60, 0, -110);
            BotPoi.Current.Position = World.Target.Position; World.MissingMesh = true; World.MissingSupport = true;
            Advance(action, 16);
            Check(World.ExteriorFlights.Count == 0 && World.Dismounts == 0 && World.Descents == 0,
                "near-destination corridor substituted for landing and onward-route observations");
        });
        test("cruise follows the safe route around an excluded settlement", action =>
        {
            World.Actor.Position = new(100, 10, 100); World.Target.Position = new(800, 10, 0);
            World.Target.Outdoors = true; BotPoi.Current.Position = World.Target.Position;
            World.MissingMesh = true; World.MissingSupport = true;
            World.AerialGoal = (_, _) => new(130, 70, 100);
            World.AerialSegment = (from, to) => to.X <= 139 || to.Y >= 50;
            Advance(action, 8);
            Check(World.ExteriorFlights.Count > 0 && World.ExteriorFlights[0].Y > 10
                && World.AerialSegment(World.Actor.Position, World.ExteriorFlights[0]),
                "local flight planning ignored its settlement detour and flew toward the original endpoint");
        });
        test("late settlement exclusion revokes an observed local air corridor", action =>
        {
            Arrange(); World.MissingMesh = true; World.MissingSupport = true;
            World.AerialSegment = (_, _) => false;
            Advance(action, 8);
            Check(World.ExteriorFlights.Count == 0, "collision-clear air ignored a forbidden route segment");
        });
        test("blocked preferred takeoff retains foot departure until flight geometry is reviewed", action =>
        {
            double now = 0;
            GroundTransitionRuntime.MonotonicClockOverride = () => now;
            try
            {
                World.Actor.Position = new(205, 15, 0); World.Actor.Flags = 0; World.Actor.MountedValue = false;
                World.Target.Position = new(1000, 15, 0); World.Target.Outdoors = true; BotPoi.Current.Position = World.Target.Position;
                World.PreferFlight = true; World.FlightCostRequiresStop = true;
                int groundRequests = 0;
                GroundTravelMount.Waiter = () => { groundRequests++; return false; };
                action.Tick(null!);
                Check(World.Walks.Count > 0 && groundRequests == 0, "blocked takeoff did not begin a foot departure");
                World.Actor.Position = new(225, 15, 0); World.Actor.IsMoving = true; now += 2;
                action.Tick(null!);
                Check(groundRequests == 0, "temporary flight-cost review abandoned flight intent and selected a ground mount");
                // The healthy acknowledgement arrives before the separate
                // two-second stop timeout (whose fallback has its own cases).
                World.Actor.IsMoving = false; now += .25;
                Advance(action, 8);
                Check(World.ExteriorFlights.Count > 0 && groundRequests == 0,
                    "open departure failed to acquire the preferred flight without a ground-mount detour");
            }
            finally { GroundTravelMount.Waiter = null; GroundTransitionRuntime.MonotonicClockOverride = null; }
        });
    }
}
