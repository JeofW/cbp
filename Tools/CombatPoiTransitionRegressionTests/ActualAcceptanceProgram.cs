using Levelbot.Actions.Combat;
using Styx.Logic.Combat;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using TreeSharp;

var failures = new List<string>();
int total = 0, passed = 0;

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
        MountedCombatTransition.RequiresProtectiveHandoff(new WoWPoint(500, 0, 0));
        body();
        passed++;
        Console.WriteLine("PASS actual combat-poi: " + name);
    }
    catch (Exception error)
    {
        failures.Add(name + ": " + error.Message);
        Console.WriteLine("FAIL actual combat-poi: " + name + ": " + error.Message);
    }
    finally { Styx.BotEvents.Stop(); }
}

void CommitKill()
{
    BotPoi.Current = new BotPoi(World.Target, PoiType.Kill);
    Check(ReferenceEquals(BotPoi.Current.AsObject, World.Target), "committed Kill did not retain the exact target wrapper");
}

void MoveTarget(float dx = 6, float dy = 1)
{
    World.Target.Position = new WoWPoint(World.Target.Position.X + dx, World.Target.Position.Y + dy, World.Target.Position.Z);
}

void ReachExteriorFlight(MountedCombatTransition owner, string stage)
{
    for (int pulse = 0; pulse < 80 && World.ExteriorFlights.Count == 0; pulse++)
    {
        GroundTransitionState state = owner.TickCurrent(BotPoi.Current.Location);
        Check(state == GroundTransitionState.Pending,
            stage + " ended before an owned flight command with " + state
            + "; phase=" + (GroundTransitionRuntime.LastObservation?.Phase ?? "none"));
    }
    Check(World.ExteriorFlights.Count == 1, stage + " never produced an owned exterior flight command");
}

void ReachDescending(MountedCombatTransition owner, string stage)
{
    ReachExteriorFlight(owner, stage);
    World.Actor.Position = World.ExteriorFlights[0];
    GroundTransitionState state = owner.TickCurrent(BotPoi.Current.Location);
    Check(state == GroundTransitionState.Pending && World.Descents == 1,
        stage + " did not reach retained descending phase; state=" + state + " descents=" + World.Descents);
}

Case("actual Location refresh revokes production context and exact active Flightor+mesh requests", () =>
{
    CommitKill();
    Flightor.PrimeControlledRoute();
    World.Mesh.PrimeControlledRoute();
    object? flight = Flightor.RequestIdentity;
    object mesh = World.Mesh.RequestIdentity;
    long generation = BotPoi.CurrentGeneration;
    long workGeneration = BotPoi.CurrentWorkGeneration;
    var context = new GroundTransitionContext(World.Target, World.Target.Location, false, () => true);

    MoveTarget();
    WoWPoint refreshed = BotPoi.Current.Location;

    Check(refreshed.Equals(World.Target.Position), "Location getter did not refresh the live target coordinate");
    Check(BotPoi.CurrentGeneration == generation + 1, "same target coordinate move did not advance the production route generation exactly once");
    Check(BotPoi.CurrentWorkGeneration == workGeneration, "same target coordinate move changed semantic work generation");
    Check(!context.Current, "production GroundTransitionContext survived its captured POI generation changing");
    Check(!ReferenceEquals(flight, Flightor.RequestIdentity), "exact production Flightor.InvalidateRouteContext did not rotate the active request");
    Check(!ReferenceEquals(mesh, World.Mesh.RequestIdentity), "exact production MeshNavigator.InvalidateRouteContext did not rotate the active request");
});

foreach (string mutation in new[] { "type", "guid", "entry", "wrapper-loss" })
    Case("actual semantic POI mutation advances work generation exactly once: " + mutation, () =>
    {
        CommitKill();
        var poi = BotPoi.Current;
        long routeBefore = BotPoi.CurrentGeneration;
        long workBefore = BotPoi.CurrentWorkGeneration;
        switch (mutation)
        {
            case "type": poi.Type = PoiType.Loot; break;
            case "guid": poi.Guid++; break;
            case "entry": poi.Entry++; break;
            case "wrapper-loss": World.Target.Invalidate(); break;
        }
        Check(BotPoi.CurrentGeneration == routeBefore + 1,
            mutation + " advanced route generation by " + (BotPoi.CurrentGeneration - routeBefore) + ", expected exactly 1");
        Check(BotPoi.CurrentWorkGeneration == workBefore + 1,
            mutation + " advanced work generation by " + (BotPoi.CurrentWorkGeneration - workBefore) + ", expected exactly 1");
    });

Case("full production landing should retain same committed target while exterior flight is active", () =>
{
    CommitKill();
    using var owner = new MountedCombatTransition();
    ReachExteriorFlight(owner, "moving-flight");
    object? flight = Flightor.RequestIdentity;
    long generation = BotPoi.CurrentGeneration;

    MoveTarget();
    _ = BotPoi.Current.Location;
    Check(BotPoi.CurrentGeneration == generation + 1, "same target move did not remain an ordinary route revision");
    GroundTransitionState state = owner.TickCurrent(BotPoi.Current.Location);
    Check(state == GroundTransitionState.Pending,
        "same committed target movement revoked the actual GroundTransition/Runtime owner during exterior flight: " + state
        + "; flightTokenRetained=" + ReferenceEquals(flight, Flightor.RequestIdentity));
});

Case("full production landing should retain same committed target while descending", () =>
{
    CommitKill();
    using var owner = new MountedCombatTransition();
    ReachDescending(owner, "moving-descent");
    int descents = World.Descents;
    MoveTarget(3, 0);
    _ = BotPoi.Current.Location;
    GroundTransitionState state = owner.TickCurrent(BotPoi.Current.Location);
    Check(state == GroundTransitionState.Pending,
        "same committed target movement revoked the actual descending owner: " + state);
    Check(World.Descents >= descents, "retained descent lost its already-owned input state");
});

Case("actual ActionPull should retain same explicit target across coordinate-only POI revision", () =>
{
    CommitKill();
    var action = new ActionPull();
    action.Start(null!);
    try
    {
        Check(action.Tick(null!) == RunStatus.Running && World.PullCalls == 0,
            "ActionPull did not enter a pending mounted transition");
        MoveTarget();
        _ = BotPoi.Current.Location;
        RunStatus state = action.Tick(null!);
        Check(state == RunStatus.Running && World.PullCalls == 0,
            "same explicit moving target revoked ActionPull before ground acknowledgement: " + state);
    }
    finally { action.Stop(null!); }
});

Case("actual ActionSetTarget should retain same explicit target across coordinate-only POI revision", () =>
{
    CommitKill();
    var action = new ActionSetTarget();
    action.Start(null!);
    try
    {
        Check(action.Tick(null!) == RunStatus.Running && World.NavigatorClears == 0,
            "ActionSetTarget did not enter a pending mounted transition");
        MoveTarget();
        _ = BotPoi.Current.Location;
        RunStatus state = action.Tick(null!);
        Check(state == RunStatus.Running && World.NavigatorClears == 0,
            "same explicit moving target revoked ActionSetTarget before ground acknowledgement: " + state);
    }
    finally { action.Stop(null!); }
});

foreach (string replacement in new[] { "poi", "target-base", "provider" })
    Case("full production landing still revokes semantic replacement: " + replacement, () =>
    {
        CommitKill();
        using var owner = new MountedCombatTransition();
        ReachExteriorFlight(owner, "replacement-" + replacement);
        switch (replacement)
        {
            case "poi": BotPoi.Current = new BotPoi(World.Target, PoiType.Kill); break;
            case "target-base": World.Target.BaseAddress++; break;
            case "provider": Navigator.NavigationProvider = new MeshNavigator(); break;
        }
        Check(owner.TickCurrent(BotPoi.Current.Location) == GroundTransitionState.Revoked,
            replacement + " inherited the old production landing owner");
    });

foreach(PoiType type in new[]{PoiType.QuestPickUp,PoiType.QuestTurnIn,PoiType.Repair,PoiType.Sell,PoiType.Train,PoiType.Buy})
    foreach(bool flying in new[]{false,true})
        Case("same moving NPC retains continuous "+type+" journey, flying="+flying,()=>
        {
            double clock=0;GroundTransitionRuntime.MonotonicClockOverride=()=>clock;
            try
            {
                World.Actor.Position=new(100,10,flying?40:0);World.Actor.Flags=flying?0x02000000u:0;
                World.Actor.MountedValue=true;World.Actor.IsMoving=true;
                World.Target.Position=new(700,10,0);World.Target.IsMoving=true;World.Target.Outdoors=true;
                BotPoi.Current=new BotPoi(World.Target,type);var poi=BotPoi.Current;
                using var journey=new GroundTransition(GroundTransitionPurpose.Interaction);
                GroundTransitionState Tick(){clock+=.3;return journey.Tick(BotPoi.Current.Location,World.Target,()=>ReferenceEquals(BotPoi.Current,poi));}
                Check(Tick()==GroundTransitionState.Pending,"initial moving-NPC travel was not admitted");
                int stops=World.Stops;int commands=World.Walks.Count+World.ExteriorFlights.Count;
                for(int pulse=0;pulse<6;pulse++)
                {
                    MoveTarget(0,-.75f);_=BotPoi.Current.Location;
                    Check(Tick()==GroundTransitionState.Pending,"same observed NPC coordinate update revoked its journey");
                }
                Check(World.Stops==stops,"coordinate-only NPC update stopped the owned journey");
                Check(World.Walks.Count+World.ExteriorFlights.Count>commands,"moving NPC stopped receiving updated movement");
                Check(World.Dismounts==0&&World.Descents==0,"distant moving NPC triggered premature landing/removal");
                World.Target.BaseAddress++;
                Check(Tick()==GroundTransitionState.Revoked,"semantic target replacement retained old travel authority");
            }
            finally { GroundTransitionRuntime.MonotonicClockOverride=null; }
        });

foreach (PoiType type in new[] { PoiType.QuestPickUp, PoiType.QuestTurnIn, PoiType.Repair, PoiType.Sell, PoiType.Train, PoiType.Buy })
    foreach (string moving in new[] { "actor", "npc", "both" })
        Case("moving final approach submits the same NPC: " + type + "/" + moving, () =>
        {
            double clock = 0;
            GroundTransitionRuntime.MonotonicClockOverride = () => clock;
            try
            {
                World.Actor.Position = new(100, 10, 0);
                World.Actor.Flags = 0;
                World.Actor.MountedValue = false;
                World.Actor.IsMoving = moving != "npc";
                World.Target.Position = new(100, 13, 0);
                World.Target.IsMoving = moving != "actor";
                World.Target.Outdoors = true;
                BotPoi.Current = new BotPoi(World.Target, type);
                var poi = BotPoi.Current;
                var subject = World.Target;
                int observations = 0;
                // Position changes occur inside native observation/dispatch boundaries,
                // rather than only between ticks. A stop acknowledgement is supplied
                // explicitly by the controlled client; the request alone is not proof.
                World.Callback = stage =>
                {
                    if (stage == "stop") World.Actor.IsMoving = false;
                    if (stage is not ("vehicle" or "sight" or "trace" or "interaction-prepare")) return;
                    observations++;
                    if (World.Actor.IsMoving) World.Actor.Position = World.Actor.Position.Add(0, .02f, 0);
                    if (World.Target.IsMoving) World.Target.Position = World.Target.Position.Add(0, .02f, 0);
                };
                bool Current() => ReferenceEquals(BotPoi.Current, poi) && ReferenceEquals(BotPoi.Current.AsObject, subject);
                using var journey = new GroundTransition(GroundTransitionPurpose.Interaction);
                GroundTransitionState state = GroundTransitionState.Pending;
                for (int pulse = 0; pulse < 8 && state != GroundTransitionState.Ready; pulse++)
                {
                    clock += .3;
                    state = journey.Tick(BotPoi.Current.Location, subject, Current);
                }
                Check(observations > 0, "motion fixture did not reach a native observation");
                Check(state == GroundTransitionState.Ready, "in-range walking NPC never reached interaction readiness: " + state);
                Check(!World.Actor.IsMoving, "interaction readiness preceded observed stop acknowledgement");
                Check(GroundTransition.TryInteractWith(subject, Current), "walking NPC lost final native interaction admission");
                Check(World.Interactions.SequenceEqual(new[] { subject.Guid }), "interaction missed or changed its captured NPC recipient");
                Check(World.Stops <= 1 && World.Dismounts == 0, "final approach oscillated stops or issued unnecessary removal");
            }
            finally { World.Callback = null; GroundTransitionRuntime.MonotonicClockOverride = null; }
        });

foreach (string change in new[] { "range", "sight", "target-base", "poi", "moving-actor" })
    Case("final NPC interaction rejects changed native admission: " + change, () =>
    {
        World.Actor.Position = new(100, 10, 0); World.Actor.Flags = 0; World.Actor.MountedValue = false;
        World.Target.Position = new(100, 13, 0); World.Target.Outdoors = true;
        var subject = World.Target; var poi = BotPoi.Current;
        bool Current() => ReferenceEquals(BotPoi.Current, poi) && ReferenceEquals(BotPoi.Current.AsObject, subject);
        if (change == "sight") World.Sight = false;
        if (change == "moving-actor") World.Actor.IsMoving = true;
        World.Callback = stage =>
        {
            if (stage != "interaction-prepare") return;
            if (change == "range") subject.Position = new(100, 40, 0);
            if (change == "target-base") subject.BaseAddress++;
            if (change == "poi") BotPoi.Current = new BotPoi(subject, PoiType.QuestTurnIn);
        };
        Check(!GroundTransition.TryInteractWith(subject, Current) && World.Interactions.Count == 0,
            "changed interaction admission dispatched to the NPC: " + change);
    });

Console.WriteLine($"Actual combat-POI acceptance: {passed}/{total}; failures={failures.Count}; production BotPoi+MCT+GroundTransition/Context/Runtime/Machine+Mesh request observation; exact extracted Navigator/Flightor/Mesh invalidators; controlled native/movement leaves.");
foreach (string failure in failures) Console.WriteLine("  " + failure);
return failures.Count == 0 ? 0 : 1;
