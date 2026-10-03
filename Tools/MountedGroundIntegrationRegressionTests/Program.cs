using Styx.Logic.BehaviorTree;
using Styx.Logic.Combat;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
using TreeAction = TreeSharp.Action;

var failures = new List<string>();
int total = 0, passed = 0, attacks = 0;

void Check(bool condition, string reason)
{
    if (!condition) throw new InvalidOperationException(reason);
}

void Case(string name, System.Action body)
{
    total++;
    try
    {
        World.Reset();
        // Reset the production mounted-escape sampler between cases through its
        // normal no-threat path. No transition state is acknowledged here.
        MountedCombatTransition.RequiresProtectiveHandoff(new WoWPoint(500, 0, 0));
        attacks = 0;
        body();
        passed++;
        Console.WriteLine("PASS " + name);
    }
    catch (Exception error)
    {
        failures.Add(name + ": " + error.Message);
        Console.WriteLine("FAIL " + name + ": " + error.Message);
    }
    finally
    {
        Styx.BotEvents.Stop();
    }
}

void GroundedMounted()
{
    World.Actor.Position = new WoWPoint(100, 10, 0);
    World.Actor.Flags = 0;
    World.Actor.Transport = 0;
    World.Actor.MountedValue = true;
    World.Actor.Shapeshift = Styx.ShapeshiftForm.Normal;
}

void AssertAttackBlocked(string stage)
{
    int before = attacks;
    Composite guard = MountedCombatTransition.GuardAction(new TreeAction(_ =>
    {
        attacks++;
        return RunStatus.Success;
    }));
    guard.Start(null!);
    Check(guard.Tick(null!) == RunStatus.Failure && attacks == before,
        stage + " admitted an ordinary attack before current ground+unmount observation");
}

void AssertAttackAllowed(string stage)
{
    int before = attacks;
    Composite guard = MountedCombatTransition.GuardAction(new TreeAction(_ =>
    {
        attacks++;
        return RunStatus.Success;
    }));
    guard.Start(null!);
    _ = guard.Tick(null!);
    Check(attacks == before + 1,
        stage + " did not execute an ordinary attack after current ground+unmount observation");
}

void AssertNoOrdinaryTravel(string stage)
{
    Check(World.RawFlights.Count == 0 && World.Walks.Count == 0,
        stage + " used an ordinary travel path during combat landing ownership");
}

void ReachLandingCommand(Func<GroundTransitionState> tick, string stage)
{
    for (int n = 0; n < 80 && World.ExteriorFlights.Count == 0 && World.Descents == 0; n++)
    {
        GroundTransitionState state = tick();
        Check(state == GroundTransitionState.Pending,
            stage + " terminated before a landing command with " + state
            + "; diagnostic=" + (GroundTransitionRuntime.LastObservation?.Phase ?? "none")
            + "/" + (GroundTransitionRuntime.LastObservation?.Reason ?? "none"));
    }
    Check(World.ExteriorFlights.Count + World.Descents > 0, stage + " never produced a validated landing command");
    Check(World.Dismounts == 0, stage + " requested dismount while still airborne");
    AssertNoOrdinaryTravel(stage);
    AssertAttackBlocked(stage);
}

void ObserveExteriorArrivalIfNeeded(Func<GroundTransitionState> tick, string stage)
{
    if (World.Descents != 0) return;
    Check(World.ExteriorFlights.Count == 1, stage + " expected one retained exterior approach");
    // Later client observation: the actor reached the commanded exterior waypoint.
    World.Actor.Position = World.ExteriorFlights[0];
    Check(tick() == GroundTransitionState.Pending && World.Descents == 1,
        stage + " did not request descent after observed exterior arrival");
    Check(World.Dismounts == 0, stage + " treated descent submission as landing acknowledgement");
}

void ObserveLandingAndUnmount(Func<GroundTransitionState> tick, string stage)
{
    var position = World.Actor.Position;
    // Later client observation: supported ground is visible, but the actor is
    // still mounted. This is deliberately separate from the descent command.
    World.Actor.Position = new WoWPoint(position.X, position.Y, 0);
    World.Actor.Flags = 0;
    Check(tick() == GroundTransitionState.Pending && World.Dismounts == 0,
        stage + " did not stop retained descent input before mount removal");
    Check(tick() == GroundTransitionState.Pending && World.Dismounts == 1,
        stage + " did not submit exactly one grounded dismount request");
    AssertNoOrdinaryTravel(stage + "/dismount-pending");
    AssertAttackBlocked(stage + "/dismount-pending");
    Check(tick() == GroundTransitionState.Pending && World.Dismounts == 1,
        stage + " repeated an unacknowledged dismount request");
    // Later client observation: mount removal is now visible.
    World.Actor.MountedValue = false;
    Check(tick() == GroundTransitionState.Ready, stage + " did not release combat after observed landing+unmount");
    AssertAttackAllowed(stage + "/ready");
}

Case("ground mounted explicit pull waits for observed unmount", () =>
{
    GroundedMounted();
    using var owner = new MountedCombatTransition();
    Check(owner.TickExplicit(World.Target, () => true) == GroundTransitionState.Pending && World.Dismounts == 1,
        "explicit pull did not acquire one grounded dismount request");
    AssertNoOrdinaryTravel("explicit-ground");
    AssertAttackBlocked("explicit-ground");
    Check(owner.TickExplicit(World.Target, () => true) == GroundTransitionState.Pending && World.Dismounts == 1,
        "explicit pull repeated dismount before observation");
    World.Actor.MountedValue = false;
    Check(owner.TickExplicit(World.Target, () => true) == GroundTransitionState.Ready,
        "explicit pull did not become ready after observed unmount");
    AssertAttackAllowed("explicit-ground/ready");
});

Case("incidental ground aggro retains mount until separately observed forced removal", () =>
{
    GroundedMounted();
    World.Actor.Combat = true;
    World.Actor.HealthPercent = 30;
    using var owner = new MountedCombatTransition();
    Check(owner.TickCurrent(new WoWPoint(600, 0, 0)) == GroundTransitionState.Revoked && World.Dismounts == 0,
        "incidental aggro acquired voluntary ground removal");
    World.Target.Position = new WoWPoint(900, 900, 0);
    Check(owner.TickCurrent(new WoWPoint(-600, 0, 0)) == GroundTransitionState.Revoked && World.Dismounts == 0,
        "unrelated target/travel churn authorized dismount");
    AssertNoOrdinaryTravel("protective-ground");
    AssertAttackBlocked("protective-ground");
    // A later client update reports hostile mount removal; no bot command caused it.
    World.Actor.MountedValue = false;
    Check(owner.TickCurrent(new WoWPoint(-600, 0, 0)) == GroundTransitionState.Ready,
        "observed forced removal on supported ground did not release combat");
    Check(World.Dismounts == 0,"forced removal emitted a redundant dismount");
    AssertAttackAllowed("protective-ground/ready");
});

Case("ground forced removal is accepted without a dismount submission", () =>
{
    GroundedMounted();
    World.Actor.MountedValue = false;
    using var owner = new MountedCombatTransition();
    Check(owner.TickExplicit(World.Target, () => true) == GroundTransitionState.Ready && World.Dismounts == 0,
        "already-observed removal was resubmitted or delayed");
    AssertNoOrdinaryTravel("forced-ground-removal");
    AssertAttackAllowed("forced-ground-removal");
});

Case("airborne forced removal stays blocked until supported ground", () =>
{
    World.Actor.MountedValue = false;
    using var owner = new MountedCombatTransition();
    Check(owner.TickExplicit(World.Target, () => true) == GroundTransitionState.Pending,
        "airborne forced removal became ground-ready");
    Check(World.Dismounts + World.Descents + World.ExteriorFlights.Count == 0,
        "airborne forced removal invented a mount or landing effect");
    AssertAttackBlocked("airborne-forced-removal");
    World.Actor.Position = new WoWPoint(100, 10, 0);
    World.Actor.Flags = 0;
    Check(owner.TickExplicit(World.Target, () => true) == GroundTransitionState.Ready && World.Dismounts == 0,
        "supported forced removal was not observed as ready");
    AssertAttackAllowed("airborne-forced-removal/ready");
});

Case("flying explicit pull uses actual ground transition before attack", () =>
{
    using var owner = new MountedCombatTransition();
    GroundTransitionState Tick() => owner.TickExplicit(World.Target, () => true);
    ReachLandingCommand(Tick, "explicit-flying");
    ObserveExteriorArrivalIfNeeded(Tick, "explicit-flying");
    ObserveLandingAndUnmount(Tick, "explicit-flying");
});

Case("flying committed kill uses actual target and ground transition", () =>
{
    BotPoi.Current = new BotPoi { Type = PoiType.Kill, Guid = World.Target.Guid, Entry = World.Target.Entry, Object = World.Target, Position = World.Target.Position };
    BotPoi.CurrentGeneration++;
    using var owner = new MountedCombatTransition();
    GroundTransitionState Tick() => owner.TickCurrent(new WoWPoint(700, 0, 0));
    ReachLandingCommand(Tick, "committed-flying");
    ObserveExteriorArrivalIfNeeded(Tick, "committed-flying");
    ObserveLandingAndUnmount(Tick, "committed-flying");
});

Case("flying incidental threat waits for actual mount loss and supported ground", () =>
{
    World.Actor.Combat = true;
    World.Actor.HealthPercent = 30;
    using var owner = new MountedCombatTransition();
    GroundTransitionState Tick() => owner.TickCurrent(new WoWPoint(900, -400, 0));
    for(int i=0;i<5;i++)Check(Tick()==GroundTransitionState.Revoked,"incidental flying threat acquired a landing owner");
    AssertAttackBlocked("incidental-flying");
    World.Actor.MountedValue=false;
    Check(Tick()==GroundTransitionState.Pending,"airborne mount loss admitted combat");
    World.Actor.Flags=0x3000u;
    Check(Tick()==GroundTransitionState.Pending,"falling after mount loss admitted combat");
    World.Actor.Position=new WoWPoint(100,10,0);World.Actor.Flags=0;
    Check(Tick()==GroundTransitionState.Ready,"supported forced removal failed to release combat");
    Check(World.Dismounts+World.Descents+World.ExteriorFlights.Count==0,"incidental travel submitted a voluntary landing effect");
    AssertAttackAllowed("forced-flying/ready");
});

Case("flying pet-only aggro cannot land the rider", () =>
{
    World.Actor.Combat = false;
    World.Actor.HealthPercent = 30;
    World.Actor.Pet = new WoWUnit { Guid = 30, BaseAddress = 300, Entry = 400, Position = new WoWPoint(101, 10, 0), Combat = true, IsAlive = true };
    using var owner = new MountedCombatTransition();
    GroundTransitionState Tick() => owner.TickCurrent(new WoWPoint(900, 400, 0));
    Check(Tick()==GroundTransitionState.Revoked,"pet-only aggro acquired mounted landing");
    World.Actor.MountedValue=false;World.Actor.Flags=0x3000u;
    Check(Tick()==GroundTransitionState.Pending,"forced mount loss did not retain falling protection");
    World.Actor.Position=new WoWPoint(100,10,0);World.Actor.Flags=0;
    Check(Tick()==GroundTransitionState.Ready,"exact pet threat could not resume after supported forced removal");
    Check(World.Dismounts+World.Descents+World.ExteriorFlights.Count==0,"pet combat created an unsolicited landing effect");
});

foreach (string change in new[] { "actor", "run", "target", "poi", "caller-context" })
    Case("original integration owner rejects " + change + " replacement", () =>
    {
        GroundedMounted();
        bool admitted = true;
        using var owner = new MountedCombatTransition();
        Check(owner.TickExplicit(World.Target, () => admitted) == GroundTransitionState.Pending && World.Dismounts == 1,
            "initial explicit owner did not acquire its pending removal");
        switch (change)
        {
            case "actor": ObjectManager.Me = new LocalPlayer { Guid = 9, BaseAddress = 900, MapId = World.Actor.MapId, Position = World.Actor.Position, IsAlive = true }; break;
            case "run": TreeRoot.RunIdentity = new object(); break;
            case "target": World.Target.Guid++; break;
            case "poi": BotPoi.CurrentGeneration++; break;
            case "caller-context": admitted = false; break;
        }
        Check(owner.TickExplicit(World.Target, () => admitted) == GroundTransitionState.Revoked && World.Dismounts == 1,
            "replacement inherited explicit transition authority");
        AssertNoOrdinaryTravel("replacement-" + change);
    });

Case("target death cancels explicit transition before unmount acknowledgement", () =>
{
    GroundedMounted();
    using var owner = new MountedCombatTransition();
    Check(owner.TickExplicit(World.Target, () => true) == GroundTransitionState.Pending && World.Dismounts == 1, "initial target transition missing");
    World.Target.IsAlive = false;
    Check(owner.TickExplicit(World.Target, () => true) == GroundTransitionState.Revoked && World.Dismounts == 1,
        "dead target retained transition authority");
});

Case("caller-observed evade cancels explicit transition", () =>
{
    GroundedMounted();
    bool evaded = false;
    using var owner = new MountedCombatTransition();
    Check(owner.TickExplicit(World.Target, () => !evaded) == GroundTransitionState.Pending && World.Dismounts == 1, "initial evade-owned transition missing");
    evaded = true;
    Check(owner.TickExplicit(World.Target, () => !evaded) == GroundTransitionState.Revoked && World.Dismounts == 1,
        "caller-revoked evade target retained transition authority");
});

Case("cleared committed POI cancels current transition", () =>
{
    GroundedMounted();
    BotPoi.Current = new BotPoi { Type = PoiType.Kill, Guid = World.Target.Guid, Entry = World.Target.Entry, Object = World.Target, Position = World.Target.Position };
    BotPoi.CurrentGeneration++;
    using var owner = new MountedCombatTransition();
    Check(owner.TickCurrent(World.Target.Position) == GroundTransitionState.Pending && World.Dismounts == 1, "initial committed transition missing");
    BotPoi.Current = new BotPoi { Type = PoiType.None };
    BotPoi.CurrentGeneration++;
    Check(owner.TickCurrent(World.Target.Position) == GroundTransitionState.Revoked && World.Dismounts == 1,
        "cleared kill POI retained transition authority");
});

Case("pet replacement revokes falling threat ownership after forced removal", () =>
{
    GroundedMounted();
    World.Actor.MountedValue=false;World.Actor.Flags=0x3000u;World.Actor.Position=new WoWPoint(100,10,8);
    World.Actor.HealthPercent = 30;
    var original = new WoWUnit { Guid = 30, BaseAddress = 300, Entry = 400, Combat = true, IsAlive = true };
    World.Actor.Pet = original;
    using var owner = new MountedCombatTransition();
    Check(owner.TickCurrent(new WoWPoint(500, 0, 0)) == GroundTransitionState.Pending && World.Dismounts == 0,
        "pet-only threat did not retain falling protection");
    World.Actor.Pet = new WoWUnit { Guid = original.Guid, BaseAddress = original.BaseAddress, Entry = original.Entry, Combat = true, IsAlive = true };
    Check(owner.TickCurrent(new WoWPoint(500, 0, 0)) == GroundTransitionState.Revoked && World.Dismounts == 0,
        "same-numbers replacement pet inherited exact threat ownership");
    Check(owner.TickCurrent(new WoWPoint(500, 0, 0)) == GroundTransitionState.Pending && World.Dismounts == 0,
        "successor pet lifetime lost falling protection or invented a dismount");
    World.Actor.Position=new WoWPoint(100,10,0);World.Actor.Flags=0;
    Check(owner.TickCurrent(new WoWPoint(500, 0, 0)) == GroundTransitionState.Ready,
        "successor pet lifetime did not accept later observed removal");
});

Case("explicit target replacement does not duplicate unacknowledged dismount", () =>
{
    GroundedMounted();
    var replacement = new WoWUnit { Guid = 31, BaseAddress = 310, Entry = 71, Position = World.Target.Position, IsAlive = true };
    using var owner = new MountedCombatTransition();
    Check(owner.TickExplicit(World.Target, () => true) == GroundTransitionState.Pending && World.Dismounts == 1, "initial target request missing");
    Check(owner.TickExplicit(replacement, () => true) == GroundTransitionState.Revoked && World.Dismounts == 1,
        "target replacement was not detached");
    Check(owner.TickExplicit(replacement, () => true) == GroundTransitionState.Pending && World.Dismounts == 1,
        "replacement duplicated unacknowledged removal");
    World.Actor.MountedValue = false;
    Check(owner.TickExplicit(replacement, () => true) == GroundTransitionState.Ready,
        "replacement did not accept later observed removal");
});

Case("reentrant cleanup cannot clobber mounted-ground successor", () =>
{
    using var first = new MountedCombatTransition();
    GroundTransitionState FirstTick() => first.TickExplicit(World.Target, () => true);
    ReachLandingCommand(FirstTick, "reentrant-first");

    var successorSubject = new WoWUnit { Guid = 44, BaseAddress = 440, Entry = 72, Position = new WoWPoint(120, 10, 0), IsAlive = true };
    using var successor = new MountedCombatTransition();
    bool invoked = false;
    World.Callback = stage =>
    {
        if (stage != "flight-release") return;
        World.Callback = null;
        invoked = true;
        World.Actor.Position = new WoWPoint(100, 10, 0);
        World.Actor.Flags = 0;
        World.Actor.MountedValue = false;
        Check(successor.TickExplicit(successorSubject, () => true) == GroundTransitionState.Ready,
            "successor could not acquire observed ground state during predecessor cleanup");
    };
    first.Cancel();
    Check(invoked, "predecessor cleanup did not reach retained flight release boundary");
    Check(successor.TickExplicit(successorSubject, () => true) == GroundTransitionState.Ready,
        "predecessor cleanup clobbered reentrant successor state");
});

Console.WriteLine($"Mounted-ground integration: {passed}/{total}; linked actual MountedCombatTransition, GroundTransition/context/runtime/machine/geometry/query and TreeSharp guard; controlled client/native leaves only; no live game/native traversal proof.");
if (failures.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
