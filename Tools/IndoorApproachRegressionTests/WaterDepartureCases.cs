using CommonBehaviors.Actions;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Profiles;
using Styx.WoWInternals;
using TreeSharp;

// Actual ActionMoveToPoi and retained transition/runtime. Mesh construction and
// input are controlled boundaries here; MeshMoveRequestOwnershipRegressionTests
// independently checks that missing mesh evidence cannot authorize direct swimming.
internal static class WaterDepartureCases
{
    internal static void Run(Action<string, Action<ActionMoveToPoi>> test)
    {
        static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        static void Water()
        {
            World.Actor.Position = new(100, 10, -10); World.Actor.MountedValue = false;
            World.Actor.IsSwimming = true; World.Actor.Flags = 0x00200000u; World.Liquid = true;
            World.Target.Position = new(150, 10, 0); World.Target.Outdoors = true;
            BotPoi.Current.Position = World.Target.Position;
        }
        foreach (PoiType type in new[] { PoiType.QuestPickUp, PoiType.QuestTurnIn, PoiType.Buy, PoiType.Sell,
            PoiType.Repair, PoiType.Train, PoiType.Mail, PoiType.Fly, PoiType.InnKeeper })
            test("swimming departure reaches the existing owned mesh route/" + type, action =>
            {
                Water(); BotPoi.Current.Type = type;
                int mounts = 0; GroundTravelMount.Waiter = () => { mounts++; return false; };
                double now = 0; GroundTransitionRuntime.MonotonicClockOverride = () => now;
                try
                {
                    Check(action.Tick(null!) == RunStatus.Running && World.Walks.SequenceEqual(new[] { World.Target.Position }),
                        "selected land work never handed its swimming departure to the mesh navigator");
                    World.Actor.Position = new(104, 10, -9); now = .3;
                    Check(action.Tick(null!) == RunStatus.Running && World.Walks.Count == 2 && World.Stops == 0,
                        "continuing swimming movement was stopped or restarted instead of retaining its route");
                    Check(mounts == 0 && World.RawFlights.Count == 0 && World.ExteriorFlights.Count == 0
                        && World.Dismounts == 0 && World.Descents == 0 && World.Interactions.Count == 0,
                        "water departure attempted mounting, flight, dismount or premature interaction");
                    World.Actor.Position = World.Target.Position.Add(-1, 0, 0);
                    World.Actor.IsSwimming = false; World.Actor.Flags = 0; World.Liquid = false; now = .6;
                    Check(action.Tick(null!) == RunStatus.Success,
                        "later independently observed dry arrival did not release interaction readiness");
                    Check(GroundTransition.TryInteractWith(World.Target) && World.Interactions.Count == 1,
                        "dry final approach did not interact with its original recipient");
                }
                finally { GroundTransitionRuntime.MonotonicClockOverride = null; GroundTravelMount.Waiter = null; }
            });
        foreach (string state in new[] { "mounted", "flying", "falling", "transport", "vehicle", "root", "stun", "unknown" })
            test("swimming departure cannot borrow invalid movement admission/" + state, action =>
            {
                Water();
                switch (state)
                {
                    case "mounted": World.Actor.MountedValue = true; break;
                    case "flying": World.Actor.Flags |= 0x02000000u; break;
                    case "falling": World.Actor.Flags |= 0x1000u; break;
                    case "transport": World.Actor.Transport = 9; break;
                    case "vehicle": World.Actor.InVehicle = true; break;
                    case "root": World.Actor.Rooted = true; break;
                    case "stun": World.Actor.Stunned = true; break;
                    case "unknown": World.Actor.MovementKnown = false; break;
                }
                Check(action.Tick(null!) != RunStatus.Success && World.Walks.Count == 0
                    && World.ExteriorFlights.Count == 0 && World.Dismounts == 0 && World.Interactions.Count == 0,
                    "unsafe swimming state dispatched movement or acknowledged arrival");
            });
        foreach (string change in new[] { "dry", "mounted", "transport", "root", "casting", "poi", "profile", "run", "actor", "provider" })
            test("water route rechecks its final mesh entry/" + change, action =>
            {
                Water(); bool reached = false;
                World.Callback = stage =>
                {
                    if (stage != "mesh-prepare") return;
                    World.Callback = null; reached = true;
                    switch (change)
                    {
                        case "dry": World.Actor.IsSwimming = false; World.Actor.Flags = 0; break;
                        case "mounted": World.Actor.MountedValue = true; break;
                        case "transport": World.Actor.Transport = 9; break;
                        case "root": World.Actor.Rooted = true; break;
                        case "casting": World.Actor.IsCasting = true; break;
                        case "poi": BotPoi.CurrentGeneration++; break;
                        case "profile": ProfileManager.CurrentProfileSnapshot = new(); break;
                        case "run": TreeRoot.RunIdentity = new(); break;
                        case "actor": ObjectManager.Me = new() { Guid = 4, BaseAddress = 400 }; break;
                        case "provider": Navigator.NavigationProvider = new MeshNavigator(); break;
                    }
                };
                Check(action.Tick(null!) != RunStatus.Success && reached && World.Walks.Count == 0,
                    "late movement/owner change reached native water-route input");
            });
        test("nearby swimming player cannot claim ground interaction", action =>
        {
            Water(); World.Actor.Position = World.Target.Position.Add(-1, 0, 0);
            Check(action.Tick(null!) == RunStatus.Running && !GroundTransition.CanInteractWith(World.Target)
                && !GroundTransition.TryInteractWith(World.Target) && World.Interactions.Count == 0,
                "an in-range swimming pose bypassed ground interaction admission");
        });
        test("wet state at prepared interaction revokes dry admission", action =>
        {
            Water(); World.Actor.Position = World.Target.Position.Add(-1, 0, 0);
            World.Actor.IsSwimming = false; World.Actor.Flags = 0; World.Liquid = false;
            World.Callback = stage => { if (stage == "interaction-prepare") World.Actor.IsSwimming = true; };
            Check(!GroundTransition.TryInteractWith(World.Target) && World.Interactions.Count == 0,
                "final interaction used a dry observation after the actor entered water");
        });
        test("useful swimming displacement renews the travel deadline", action =>
        {
            Water(); World.Target.Position = new(2000, 10, 0); BotPoi.Current.Position = World.Target.Position;
            double now = 0; GroundTransitionRuntime.MonotonicClockOverride = () => now;
            try
            {
                for (int i = 0; i < 32; i++)
                {
                    now = i * 5; World.Actor.Position = new(100 + i * 4, 10, -10);
                    Check(action.Tick(null!) == RunStatus.Running && World.Walks.Count == i + 1,
                        "productive swimming hit an arbitrary trip deadline");
                }
                Check(!World.Diagnostics.Any(line => line.Contains("ground-transition-deadline")),
                    "useful water movement expired the final-landing budget");
            }
            finally { GroundTransitionRuntime.MonotonicClockOverride = null; }
        });
        test("stationary swimming remains bounded by observed progress", action =>
        {
            Water(); double now = 0; GroundTransitionRuntime.MonotonicClockOverride = () => now;
            try
            {
                for (int i = 0; i < 12; i++) { now = i * 4; action.Tick(null!); }
                Check(World.Walks.Count > 0 && World.Diagnostics.Any(line => line.Contains("no-observed-transition-progress")),
                    "failed water-route movement neither attempted the mesh nor reached bounded recovery");
            }
            finally { GroundTransitionRuntime.MonotonicClockOverride = null; }
        });
    }
}
