#nullable disable
using System;
using System.Collections.Generic;
using Harness;
using Levelbot.Actions.Combat;
using Styx;
using Styx.Logic;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Combat;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

internal static class Program
{
    private static int passed, failed, unexpected;

    private sealed class Failure(string message) : Exception(message);
    private sealed record Mutation(string Name, Action<WorldState> Apply);
    private sealed record WorldState(LocalPlayer Me, WoWUnit Target, BotPoi Poi);

    private static int Main()
    {
        Case("ordinary moving-target Location refresh still advances route revision and invalidates both route tokens", OrdinaryMovingTargetInvalidation);
        Case("committed combat landing retains Flightor and mesh tokens across same-target movement", CombatLeaseRetainsRouteTokens);
        Case("same committed target movement keeps mounted landing transition pending", SameTargetMoveKeepsPending);
        Case("same committed target movement while descending keeps landing transition pending", SameTargetDescendingKeepsPending);
        Case("ActionPull keeps the same explicit moving target transition pending", ActionPullMovingTargetKeepsPending);
        Case("ActionSetTarget keeps the same explicit moving target transition pending", ActionSetTargetMovingTargetKeepsPending);
        foreach (Mutation mutation in InvalidatingMutations())
            Case("combat landing revokes " + mutation.Name, () => InvalidatingMutationRevokes(mutation));
        Case("stale landing lease cannot reacquire after POI leaves and same wrapper returns", StalePoiEpochCannotReacquire);

        Console.WriteLine($"Combat POI transition scenarios: {passed}/{passed + failed + unexpected}; assertions={failed}; unexpected={unexpected}; linked actual BotPoi/MountedCombatTransition/pull/settarget + exact Navigator.InvalidatePoiRoute; controlled movement/native leaves; no host/navmesh/game loop.");
        return failed + unexpected == 0 ? 0 : 1;
    }

    private static void Case(string name, Func<bool> body)
    {
        try
        {
            Check(body(), name);
            passed++;
            Console.WriteLine("PASS combat-poi: " + name);
        }
        catch (Failure error)
        {
            failed++;
            Console.Error.WriteLine("FAIL combat-poi: " + error.Message);
        }
        catch (Exception error)
        {
            unexpected++;
            Console.Error.WriteLine("ERROR combat-poi: " + name + ": " + error);
        }
    }

    private static void Check(bool value, string reason)
    {
        if (!value) throw new Failure(reason);
    }

    private static WorldState World(bool descending = false)
    {
        Control.Reset();
        var me = new LocalPlayer
        {
            Guid = 1,
            BaseAddress = 100,
            Entry = 0,
            MapId = 530,
            IsAlive = true,
            Mounted = true,
            Location = new WoWPoint(0, 0, 80),
            ObservedMovementFlags = descending ? 0x02800000u : 0x02000000u,
            HealthPercent = 100,
        };
        var target = new WoWUnit
        {
            Guid = 2,
            BaseAddress = 200,
            Entry = 16863,
            IsAlive = true,
            Location = new WoWPoint(30, 0, 40),
            Name = "controlled moving target",
        };
        StyxWoW.Me = me;
        WoWMovement.ActiveMover = me;
        ObjectManager.Objects.Add(target);
        Targeting.Instance.FirstUnit = target;
        Targeting.Instance.TargetList.Add(target);
        me.CurrentTarget = target;
        var poi = new BotPoi(target, PoiType.Kill);
        BotPoi.Current = poi;
        Control.ResetRouteTokens();
        return new WorldState(me, target, poi);
    }

    private static WoWPoint MoveAndRefresh(WorldState world, float delta = 7)
    {
        world.Target.Location = new WoWPoint(world.Target.Location.X + delta, world.Target.Location.Y + 2, world.Target.Location.Z);
        return BotPoi.Current.Location;
    }

    private static bool OrdinaryMovingTargetInvalidation()
    {
        var world = World();
        long before = BotPoi.CurrentGeneration;
        object flight = Flightor.RequestIdentity;
        object mesh = Control.Mesh.RequestIdentity;
        WoWPoint refreshed = MoveAndRefresh(world);
        Check(refreshed.Equals(world.Target.Location), "Location getter did not publish the live target coordinate");
        Check(BotPoi.CurrentGeneration == before + 1, $"route revision changed by {BotPoi.CurrentGeneration - before}, expected exactly 1");
        Check(Control.FlightInvalidations == 1 && Control.MeshInvalidations == 1,
            $"expected one Flightor+mesh invalidation, saw flight={Control.FlightInvalidations}, mesh={Control.MeshInvalidations}");
        Check(!ReferenceEquals(flight, Flightor.RequestIdentity), "ordinary Flightor request token survived moving-target route invalidation");
        Check(!ReferenceEquals(mesh, Control.Mesh.RequestIdentity), "ordinary mesh request token survived moving-target route invalidation");
        return true;
    }

    private static bool CombatLeaseRetainsRouteTokens()
    {
        var world = World();
        using var transition = new MountedCombatTransition();
        Check(transition.TickCurrent(BotPoi.Current.Location) == GroundTransitionState.Pending, "initial committed landing did not become Pending");
        object flight = Flightor.RequestIdentity;
        object mesh = Control.Mesh.RequestIdentity;
        long before = BotPoi.CurrentGeneration;
        MoveAndRefresh(world);
        Check(BotPoi.CurrentGeneration == before + 1, "same-target movement did not remain visible as a route revision");
        bool flightRetained = ReferenceEquals(flight, Flightor.RequestIdentity);
        bool meshRetained = ReferenceEquals(mesh, Control.Mesh.RequestIdentity);
        Check(flightRetained && meshRetained,
            $"same-target movement rotated leased route token(s): flightRetained={flightRetained}, meshRetained={meshRetained}");
        return true;
    }

    private static bool SameTargetMoveKeepsPending()
    {
        var world = World();
        using var transition = new MountedCombatTransition();
        Check(transition.TickCurrent(BotPoi.Current.Location) == GroundTransitionState.Pending, "initial committed landing did not become Pending");
        int cancels = Control.TransitionCancels;
        for (int move = 0; move < 3; move++)
        {
            ulong guid = world.Target.Guid;
            uint baseAddress = world.Target.BaseAddress;
            MoveAndRefresh(world, 3 + move);
            Check(ReferenceEquals(BotPoi.Current, world.Poi) && ReferenceEquals(BotPoi.Current.AsObject, world.Target)
                && world.Target.Guid == guid && world.Target.BaseAddress == baseAddress,
                "same-target coordinate refresh changed semantic target identity");
            GroundTransitionState state = transition.TickCurrent(BotPoi.Current.Location);
            Check(state == GroundTransitionState.Pending, $"same target move {move + 1} returned {state}, expected retained Pending");
            Check(Control.TransitionCancels == cancels, "same-target movement cancelled the retained ground owner");
        }
        world.Me.Mounted = false;
        world.Me.ObservedMovementFlags = 0;
        Check(transition.TickCurrent(BotPoi.Current.Location) == GroundTransitionState.Ready,
            "observed ground/unmounted acknowledgement did not release the retained combat transition");
        return true;
    }

    private static bool SameTargetDescendingKeepsPending()
    {
        var world = World(descending: true);
        using var transition = new MountedCombatTransition();
        Check(transition.TickCurrent(BotPoi.Current.Location) == GroundTransitionState.Pending, "descending committed landing did not start");
        MoveAndRefresh(world, 4);
        GroundTransitionState state = transition.TickCurrent(BotPoi.Current.Location);
        Check(state == GroundTransitionState.Pending, $"descending same-target move returned {state}, expected retained Pending");
        return true;
    }

    private static bool ActionPullMovingTargetKeepsPending()
    {
        var world = World();
        var action = new ActionPull();
        action.Start(null);
        try
        {
            Check(action.Tick(null) == RunStatus.Running, "initial ActionPull transition did not wait");
            Check(Control.PullCalls == 0, "ActionPull dispatched while mounted");
            MoveAndRefresh(world);
            RunStatus state = action.Tick(null);
            Check(state == RunStatus.Running, $"same explicit moving target made ActionPull return {state}");
            Check(Control.PullCalls == 0, "ActionPull dispatched before ground acknowledgement");
            return true;
        }
        finally { action.Stop(null); }
    }

    private static bool ActionSetTargetMovingTargetKeepsPending()
    {
        var world = World();
        var action = new ActionSetTarget();
        action.Start(null);
        try
        {
            Check(action.Tick(null) == RunStatus.Running, "initial ActionSetTarget transition did not wait");
            MoveAndRefresh(world);
            RunStatus state = action.Tick(null);
            Check(state == RunStatus.Running, $"same explicit moving target made ActionSetTarget return {state}");
            Check(Control.NavigatorClears == 0, "ActionSetTarget cleared navigation before ground acknowledgement");
            return true;
        }
        finally { action.Stop(null); }
    }

    private static IEnumerable<Mutation> InvalidatingMutations()
    {
        yield return new Mutation("different POI reference", world =>
        {
            BotPoi.Current = new BotPoi(world.Target, PoiType.Kill);
        });
        yield return new Mutation("POI entry mutation", world => world.Poi.Entry++);
        yield return new Mutation("POI GUID mutation", world => world.Poi.Guid++);
        yield return new Mutation("POI type mutation", world => world.Poi.Type = PoiType.Loot);
        yield return new Mutation("target wrapper replacement", world =>
        {
            var replacement = new WoWUnit
            {
                Guid = world.Target.Guid,
                BaseAddress = world.Target.BaseAddress,
                Entry = world.Target.Entry,
                IsAlive = true,
                Location = world.Target.Location,
                Name = "replacement wrapper",
            };
            world.Target.Invalidate();
            ObjectManager.Objects.Clear();
            ObjectManager.Objects.Add(replacement);
            world.Me.CurrentTarget = replacement;
            Targeting.Instance.FirstUnit = replacement;
        });
        yield return new Mutation("target base mutation", world => world.Target.BaseAddress++);
        yield return new Mutation("target GUID mutation", world => world.Target.Guid++);
        yield return new Mutation("target death", world => world.Target.IsAlive = false);
        yield return new Mutation("run replacement", world => TreeRoot.RunIdentity = new object());
        yield return new Mutation("provider replacement", world => Navigator.NavigationProvider = new object());
    }

    private static bool InvalidatingMutationRevokes(Mutation mutation)
    {
        var world = World();
        using var transition = new MountedCombatTransition();
        Check(transition.TickCurrent(BotPoi.Current.Location) == GroundTransitionState.Pending, "initial committed landing did not become Pending");
        mutation.Apply(world);
        return transition.TickCurrent(BotPoi.Current.Location) == GroundTransitionState.Revoked;
    }

    private static bool StalePoiEpochCannotReacquire()
    {
        var world = World();
        using var transition = new MountedCombatTransition();
        Check(transition.TickCurrent(BotPoi.Current.Location) == GroundTransitionState.Pending, "initial committed landing did not become Pending");
        var samePoi = world.Poi;
        BotPoi.Current = new BotPoi(new WoWPoint(100, 100, 10), PoiType.Repair);
        BotPoi.Current = samePoi;
        return transition.TickCurrent(BotPoi.Current.Location) == GroundTransitionState.Revoked;
    }
}
