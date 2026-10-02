using Styx.Helpers;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Profiles;
using Styx.WoWInternals;

// Real runtime, state machine, context and geometry; controlled observations and
// final native movement leaves. Changes below represent later observed client
// states, never automatic acknowledgements inferred from a submitted command.
internal static class GroundRuntimeCases
{
    internal static void Run(Action<string, Action> test)
    {
        static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        static GroundTransition Owner() => new(GroundTransitionPurpose.Combat);
        static GroundTransitionState Tick(GroundTransition owner) => owner.Tick(World.Target.Position, World.Target, () => true);
        static void Grounded() { World.Actor.Position = new(100, 10, 0); World.Actor.Flags = 0; }
        static int Effects() => World.RawFlights.Count + World.ExteriorFlights.Count + World.Descents + World.Dismounts + World.Walks.Count;

        test("actual combat runtime acknowledges each distinct mount lifetime", () =>
        {
            Grounded();
            using (var first = Owner())
            {
                Check(Tick(first) == GroundTransitionState.Pending && World.Dismounts == 1, "first mount removal was not requested");
                Check(Tick(first) == GroundTransitionState.Pending && World.Dismounts == 1, "request was retried without an observation");
                World.Actor.MountedValue = false;
                Check(Tick(first) == GroundTransitionState.Ready, "first observed removal did not release combat");
            }
            World.Actor.MountedValue = true;
            using var second = Owner();
            Check(Tick(second) == GroundTransitionState.Pending && World.Dismounts == 2,
                "acknowledged prior mount lifetime suppressed a new mount-removal request");
            World.Actor.MountedValue = false;
            Check(Tick(second) == GroundTransitionState.Ready, "second observed mount removal did not release combat");
        });
        test("unacknowledged dismount survives target and POI replacement without duplication", () =>
        {
            Grounded(); using var first = Owner();
            Check(Tick(first) == GroundTransitionState.Pending && World.Dismounts == 1, "initial request missing");
            BotPoi.CurrentGeneration++; World.Target.Guid++; first.Cancel();
            using var second = Owner();
            Check(Tick(second) == GroundTransitionState.Pending && World.Dismounts == 1,
                "replacement work repeated an unacknowledged actor/session dismount");
            World.ObservationError = new ObservationUnavailableException("mount", "controlled unknown");
            Check(Tick(second) == GroundTransitionState.Pending && World.Dismounts == 1, "UNKNOWN mount acknowledged or repeated the request");
            World.ObservationError = null; World.Actor.MountedValue = false;
            Check(Tick(second) == GroundTransitionState.Ready, "valid later removal did not acknowledge the pending request");
        });
        test("grounded forced removal needs no owned dismount request", () =>
        {
            Grounded(); World.Actor.MountedValue = false; using var owner = Owner();
            Check(Tick(owner) == GroundTransitionState.Ready && World.Dismounts == 0, "forced removal repeated dismount or delayed ground combat");
        });
        test("airborne forced removal remains pending until supported grounded state", () =>
        {
            World.Actor.MountedValue = false; using var owner = Owner();
            Check(Tick(owner) == GroundTransitionState.Pending && Effects() == 0, "airborne unmount became ground combat permission");
            Grounded(); Check(Tick(owner) == GroundTransitionState.Ready && World.Dismounts == 0, "supported forced removal was not recognized");
        });
        test("flying combat executes exterior descent and dismount acknowledgement in order", () =>
        {
            using var owner = Owner();
            for (int n = 0; n < 50 && World.ExteriorFlights.Count == 0; n++)
                Check(Tick(owner) == GroundTransitionState.Pending, "airborne combat owner terminated before exterior approach");
            Check(World.ExteriorFlights.Count == 1 && World.RawFlights.Count == 0 && World.Dismounts == 0,
                "airborne combat used raw travel or unsafe dismount");
            World.Actor.Position = World.ExteriorFlights[0];
            Check(Tick(owner) == GroundTransitionState.Pending && World.Descents == 1 && World.Dismounts == 0,
                "descent dispatch was treated as supported landing");
            World.Actor.Position = new(World.Actor.Position.X, World.Actor.Position.Y, 0); World.Actor.Flags = 0;
            Check(Tick(owner) == GroundTransitionState.Pending && World.Dismounts == 0, "retained descent input was not stopped first");
            Check(Tick(owner) == GroundTransitionState.Pending && World.Dismounts == 1, "landed mount did not request removal");
            World.Actor.MountedValue = false;
            Check(Tick(owner) == GroundTransitionState.Ready && World.Walks.Count == 0,
                "combat did not acquire the acknowledged actor or borrowed quest ground travel");
        });
        foreach (string unavailable in new[] { "water", "missing-support", "missing-mesh", "wrong-floor", "falling", "root", "stun", "transport", "unknown-mount" })
            test("actual combat runtime unsafe/unknown state " + unavailable, () =>
            {
                if (unavailable == "water") World.Liquid = true;
                if (unavailable == "missing-support") World.MissingSupport = true;
                if (unavailable == "missing-mesh") Navigator.IsNavigatorLoaded = false;
                if (unavailable == "wrong-floor") World.MissingMesh = true;
                if (unavailable == "falling") World.Actor.Flags = 0x1000;
                if (unavailable == "root") World.Actor.Rooted = true;
                if (unavailable == "stun") World.Actor.Stunned = true;
                if (unavailable == "transport") World.Actor.Transport = 8;
                if (unavailable == "unknown-mount") World.ObservationError = new ObservationUnavailableException("mount", "unknown");
                using var owner = Owner();
                for (int i = 0; i < 90; i++)
                    Check(Tick(owner) is GroundTransitionState.Pending or GroundTransitionState.Unavailable, "unsafe observation became attack permission");
                Check(Effects() == 0, "unsafe or unproven landing dispatched a movement/dismount effect");
            });
        foreach (string change in new[] { "actor", "map", "memory", "process", "executor", "profile", "provider", "poi", "run", "subject", "dead", "stop" })
            test("retained actual combat runtime revokes " + change, () =>
            {
                Grounded(); using var owner = Owner();
                Check(Tick(owner) == GroundTransitionState.Pending && World.Dismounts == 1, "initial combat handoff did not acquire a request");
                switch (change)
                {
                    case "actor": ObjectManager.Me = new() { Guid = 9, BaseAddress = 900, MountedValue = false }; break;
                    case "map": World.Actor.MapId++; break;
                    case "memory": ObjectManager.Wow = new(); ObjectManager.Executor!.Memory = ObjectManager.Wow; break;
                    case "process": ObjectManager.Wow!.ProcessId++; break;
                    case "executor": ObjectManager.Executor = new() { Memory = ObjectManager.Wow! }; break;
                    case "profile": ProfileManager.CurrentProfileSnapshot = new(); break;
                    case "provider": Navigator.NavigationProvider = new MeshNavigator(); break;
                    case "poi": BotPoi.CurrentGeneration++; break;
                    case "run": TreeRoot.RunIdentity = new(); break;
                    case "subject": World.Target.Guid++; break;
                    case "dead": World.Actor.IsAlive = false; break;
                    case "stop": TreeRoot.IsRunning = false; Styx.BotEvents.Stop(); break;
                }
                World.Actor.MountedValue = false;
                Check(Tick(owner) == GroundTransitionState.Revoked && World.Dismounts == 1,
                    "retained owner accepted a replacement as an acknowledged combat actor");
            });
        test("actual runtime cleanup cannot stop or clear a reentrant successor", () =>
        {
            using var first = Owner();
            for (int n = 0; n < 50 && World.ExteriorFlights.Count == 0; n++) Tick(first);
            Check(World.ExteriorFlights.Count == 1, "first exterior approach was not acquired");
            using var successor = Owner(); bool invoked = false;
            World.Callback = stage =>
            {
                if (stage != "flight-release") return;
                World.Callback = null; invoked = true; Grounded(); World.Actor.MountedValue = false;
                Check(Tick(successor) == GroundTransitionState.Ready, "reentrant successor could not acquire ground readiness");
            };
            first.Cancel();
            Check(invoked && Tick(successor) == GroundTransitionState.Ready, "old cleanup destroyed the successor's runtime");
        });
    }
}
