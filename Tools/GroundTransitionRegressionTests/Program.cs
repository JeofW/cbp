using Styx.Helpers;
using Styx.Logic.Pathing;
using Tripper.Navigation;

int passed = 0, total = 0;
var failures = new List<string>();
void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
void Case(string name, Action<Runtime, GroundTransitionMachine> action, GroundTransitionPurpose purpose = GroundTransitionPurpose.Combat)
{
    total++;
    var runtime = new Runtime(); var machine = new GroundTransitionMachine(purpose, runtime);
    try { action(runtime, machine); passed++; Console.WriteLine("PASS ground transition: " + name); }
    catch (Exception error) { failures.Add(name + ": " + error); Console.Error.WriteLine("FAIL ground transition: " + name + ": " + error.Message); }
}
void Pending(GroundTransitionMachine machine) => Check(machine.Tick() == GroundTransitionState.Pending, "request/observation was falsely completed");
void Air(Runtime runtime, float altitude = 80) => runtime.Motion = runtime.Motion with { Flying = true, Supported = false, Position = new(10, 10, altitude) };

Case("ground mount submission waits for observed removal", (r, m) =>
{
    Pending(m); Pending(m);
    Check(r.Dismounts == 1 && r.Walks == 0 && r.Flights == 0, "mounted routine advanced or repeated removal");
    r.Motion = r.Motion with { Mounted = false };
    Check(m.Tick() == GroundTransitionState.Ready && r.Dismounts == 1, "observed unmount did not acquire combat");
});
Case("transit points never become stop or dismount destinations", (r,m)=>
{
    r.Motion=r.Motion with { GroundTravel=true, InteractionReady=true };
    for(int i=0;i<8;i++){r.Time=i*.3;r.Motion=r.Motion with { Position=new WoWPoint(10+i*8,10,0) };Pending(m);}
    Check(r.Walks==8&&r.Dismounts==0&&r.Holds==0,"transit introduced stops or dismounts at intermediate points");
},(GroundTransitionPurpose)2);
Case("distant grounded travel retains a mount until final approach", (r,m)=>
{
    r.Motion=r.Motion with { GroundTravel=true };
    Pending(m);
    Check(r.Walks==1&&r.Dismounts==0,"a distant ground journey dismounted instead of retaining mounted mesh travel");
    r.Motion=r.Motion with { GroundTravel=false };r.Time=.3;Pending(m);
    Check(r.Dismounts==1,"final approach did not restore safe observed unmount handling");
},GroundTransitionPurpose.Interaction);
Case("useful mounted ground progress outlives the landing budget", (r,m)=>
{
    r.Motion=r.Motion with { GroundTravel=true };Pending(m);
    for(int i=1;i<=40;i++)
    {
        r.Time=i*10;r.Motion=r.Motion with { Position=new WoWPoint(10+i,10,0) };Pending(m);
    }
    Check(r.Holds==0&&r.Dismounts==0&&r.Resets==0,"active long-distance ground progress was interrupted by a flight-transition deadline");
},GroundTransitionPurpose.Interaction);
Case("landing stops descent input before distant mounted travel",(r,m)=>
{
    Air(r);Pending(m);r.Motion=r.Motion with { Position=new(50,10,7) };r.Time=.3;Pending(m);
    r.Motion=r.Motion with { Position=new(50,10,0),Flying=false,Supported=true,Descending=true,GroundTravel=true };
    r.Time=.6;Pending(m);
    Check(r.Walks==0&&r.Dismounts==0&&m.Phase=="landed","landing resumed ground travel before stopping retained descent input");
},GroundTransitionPurpose.Interaction);
Case("unsubmitted dismount may retry without repeating an effect", (r, m) =>
{
    r.AcceptDismount = false; Pending(m); Check(r.Dismounts == 0, "rejected command became submitted");
    r.AcceptDismount = true; Pending(m); Check(r.Dismounts == 1, "unsubmitted request poisoned acknowledgement state");
});
Case("fresh dismount reservation starts its own progress window", (r, m) =>
{
    r.Motion = r.Motion with { Immobilized = true };
    Pending(m);
    r.Time = 11.9;
    r.Motion = r.Motion with { Immobilized = false };
    Pending(m);
    Check(r.Dismounts == 1, "fresh supported request was not submitted");
    r.Time = 12.1;
    Check(m.Tick() == GroundTransitionState.Pending && r.Dismounts == 1,
        "fresh request inherited an older no-progress deadline");
});
Case("pending dismount survives lag without another effect", (r, m) =>
{
    Pending(m); r.Time = 13;
    Check(m.Tick() == GroundTransitionState.Unavailable && r.Dismounts == 1, "dismount timeout authorized another native effect");
    r.Time = 17; Check(m.Tick() == GroundTransitionState.Unavailable && r.Dismounts == 1, "recovery cooldown duplicated the timed-out request");
    r.Time = 19; Pending(m); Check(r.Dismounts == 2, "diagnosed timeout never allowed one fresh supported-ground request");
});
Case("terminal unavailable reprobes after bounded owner cooldown", (r, m) =>
{
    Air(r); r.Plan = null; r.Exhausted = true;
    Check(m.Tick() == GroundTransitionState.Unavailable, "terminal search did not become unavailable");
    r.Exhausted = false;
    r.Plan = new(new(50, 10, 0), new(50, 10, 4), AreaType.Ground,
        new GroundPath(true, "complete", new[] { new WoWPoint(50, 10, 0), new WoWPoint(80, 10, 0) },
            new[] { AreaType.Ground, AreaType.Ground }), true, "recovered-safe-approach");
    r.Time = 6;
    Check(m.Tick() == GroundTransitionState.Pending && r.Flights == 1,
        "terminal unavailable stayed sticky after bounded same-owner recovery window");
});
Case("authoritative forced unmount can finish while unavailable", (r, m) =>
{
    Air(r); r.Plan = null; r.Exhausted = true;
    Check(m.Tick() == GroundTransitionState.Unavailable, "terminal search did not become unavailable");
    r.Motion = r.Motion with { Mounted = false, Flying = false, Supported = true, Position = new WoWPoint(10, 10, 0) };
    r.Time = 1;
    Check(m.Tick() == GroundTransitionState.Ready && r.Dismounts == 0,
        "observed supported unmount was hidden behind terminal unavailable latch");
});
Case("high flying rider never dismounts", (r, m) =>
{
    Air(r); Pending(m); Check(r.Flights == 1 && r.Dismounts == 0 && r.Descents == 0, "airborne removal or unsupported descent");
});
Case("descent then supported landing then unmount acknowledgement", (r, m) =>
{
    Air(r); Pending(m);
    r.Motion = r.Motion with { Position = new(50, 10, 7) }; r.Time += .3; Pending(m);
    Check(r.Descents == 1 && r.Dismounts == 0, "landing dispatch was taken as ground");
    r.Motion = r.Motion with { Position = new(50, 10, 0), Flying = false, Supported = true, Descending = true }; r.Time += .3; Pending(m);
    Check(r.Dismounts == 0, "retained descent input was not stopped first");
    r.Motion = r.Motion with { Descending = false }; r.Time += .3; Pending(m);
    Check(r.Dismounts == 1, "supported grounded rider did not request removal");
    r.Motion = r.Motion with { Mounted = false }; r.Time += .3;
    Check(m.Tick() == GroundTransitionState.Ready, "owned mount removal never acquired combat");
});
Case("supported landing with a lagging flight flag never climbs back to its approach waypoint", (r, m) =>
{
    Air(r); Pending(m);
    r.Motion=r.Motion with { Position=new(50,10,7) };r.Time=.3;Pending(m);
    Check(r.Descents==1,"descent was not established");int flights=r.Flights;
    // Observed support is authoritative about the footprint. The client may
    // clear Flying one pulse later and drift beyond the original .75-yard column.
    r.Motion=r.Motion with { Position=new(50.9f,10,-.3f),Supported=true,Descending=true };
    for(int pulse=0;pulse<3;pulse++){r.Time+=.3;Pending(m);}
    Check(r.Flights==flights&&r.Dismounts==0,"supported descent bounced upward or dismounted before the flight flag cleared");
    r.Motion=r.Motion with { Flying=false,Descending=false };r.Time+=.3;Pending(m);
    r.Time+=.3;Pending(m);Check(r.Dismounts==1,"observed landing never released its single dismount request");
});
Case("forced removal needs no duplicate request", (r, m) =>
{
    Air(r); Pending(m); r.Motion = r.Motion with { Flying = false, Mounted = false, Supported = true, Position = new(50, 10, 0) };
    Check(m.Tick() == GroundTransitionState.Ready && r.Dismounts == 0, "server removal was duplicated");
});
Case("near ground flying remains a landing request", (r, m) =>
{
    Air(r, 1); r.Motion = r.Motion with { Position = new(50, 10, 1) }; Pending(m);
    Check(r.Descents == 1 && r.Dismounts == 0, "low altitude was mistaken for grounded state");
});
Case("loss of support never authorizes unmount", (r, m) =>
{
    r.Motion = r.Motion with { Supported = false }; Pending(m);
    Check(r.Dismounts == 0 && r.Flights == 0 && r.Descents == 0, "unsupported actor acquired ground permission");
});
foreach (string state in new[] { "falling", "water", "transport", "root-stun" })
    Case("mounted unsafe state/" + state, (r, m) =>
    {
        Air(r); r.Motion = state switch
        {
            "falling" => r.Motion with { Falling = true }, "water" => r.Motion with { Swimming = true },
            "transport" => r.Motion with { OnTransport = true }, _ => r.Motion with { Immobilized = true }
        };
        Pending(m); Check(r.Dismounts + r.Flights + r.Descents + r.Walks == 0, "unsafe transition dispatched movement or dismount");
    });
Case("no safe landing terminates the owned search", (r, m) =>
{
    Air(r); r.Plan = null; r.Exhausted = true;
    Check(m.Tick() == GroundTransitionState.Unavailable && r.Flights + r.Dismounts + r.Descents == 0, "fabricated a landing point");
});
Case("incremental search is pending", (r, m) =>
{
    Air(r); var plan = r.Plan; r.Plan = null; Pending(m); Check(r.Flights == 0, "search inputs became a route");
    r.Plan = plan; r.Time += .3; Pending(m); Check(r.Flights == 1, "validated search result not acquired");
});
Case("dynamic collision invalidates approach before dispatch", (r, m) =>
{
    Air(r); r.ValidPlan = false; Pending(m); Check(r.Flights + r.Descents + r.Dismounts == 0 && r.Resets == 1, "stale collision plan dispatched");
});
Case("repeated invalidation becomes bounded UNKNOWN", (r, m) =>
{
    Air(r); r.ValidPlan = false; Pending(m); Pending(m);
    Check(m.Tick() == GroundTransitionState.Unavailable && r.Flights == 0, "unbounded candidate invalidation loop");
});
Case("no displacement revokes/replans instead of pushing a wall", (r, m) =>
{
    Air(r); Pending(m); r.Time = 13; Pending(m); Check(r.Resets == 1, "movement submission masked a wall stall");
    r.Time = 26; Pending(m); r.Time = 39;
    Check(m.Tick() == GroundTransitionState.Unavailable, "wall retries were not bounded");
});
Case("real displacement keeps a useful detour alive", (r, m) =>
{
    Air(r); Pending(m);
    for (int n = 1; n <= 15; n++) { r.Time = n * 2; r.Motion = r.Motion with { Position = new(10 - n, 10, 80) }; Pending(m); }
    Check(r.Resets == 0, "detour progress was mistaken for a stall because final-target distance increased");
});
Case("arrival is never inferred from a long-running command", (r, m) =>
{
    Air(r); Pending(m); r.Time = 121;
    Check(m.Tick() == GroundTransitionState.Unavailable && r.Dismounts == 0, "timer became a landing acknowledgement");
});
Case("unmounted foot approach uses mesh and current interaction", (r, m) =>
{
    r.Motion = r.Motion with { Mounted = false }; Pending(m); Check(r.Walks == 1 && r.Flights == 0, "foot handoff remounted or skipped route");
    r.Motion = r.Motion with { InteractionReady = true }; r.Time += .3;
    Check(m.Tick() == GroundTransitionState.Ready, "observed interaction approach never completed");
}, GroundTransitionPurpose.Interaction);
Case("exterior flight plan never means indoor interaction ready", (r, m) =>
{
    Air(r); Pending(m); Check(r.LastFlight == r.Plan!.AirWaypoint && r.LastFlight != r.Target, "indoor final coordinate was dispatched as a flight waypoint");
    Check(m.Phase == "exterior-approach", "exterior dispatch was falsely promoted to ground arrival");
}, GroundTransitionPurpose.Interaction);
Case("covered landing never becomes a lateral flight destination", (r, m) =>
{
    Air(r); r.Plan = r.Plan! with { OpenColumn = false };
    Pending(m);
    Check(r.Flights == 0 && r.Descents == 0 && r.Resets == 1, "covered landing plan commanded flight through a roof or wall");
});
Case("grounded flight selection is retained until landing handoff", (r, m) =>
{
    r.Motion = r.Motion with { Mounted = false, PreferFlight = true }; Pending(m); Check(r.Flights == 1 && r.Walks == 0, "long travel flight selection lost");
}, GroundTransitionPurpose.Interaction);
Case("final exterior landing keeps its ground leg despite a distant indoor destination", (r,m)=>
{
    Air(r);Pending(m);
    r.Motion=r.Motion with { Position=new(50,10,4) };r.Time=.3;Pending(m);
    Check(r.Descents==1,"final exterior landing did not begin");
    r.Motion=r.Motion with { Position=new(50,10,0),Flying=false,Supported=true,Descending=true,PreferFlight=true };
    r.Time=.6;Pending(m);
    r.Motion=r.Motion with { Descending=false };r.Time=.9;Pending(m);
    Check(r.Dismounts==1&&r.Flights==1,"final ground approach restarted flight instead of awaiting supported unmount");
    r.Motion=r.Motion with { Mounted=false };r.Time=1.2;Pending(m);
    Check(r.Walks==1&&r.Flights==1,"distant indoor endpoint caused takeoff/landing oscillation");
},GroundTransitionPurpose.Interaction);
Case("pending flight search does not commit a partial batch to ground travel", (r, m) =>
{
    r.Motion = r.Motion with { Mounted = false, PreferFlight = true };
    r.Plan = null;
    Pending(m);
    Check(r.Walks == 0 && r.Flights == 0 && r.Resets == 0,
        "an incomplete flight-search batch was discarded and committed to ground travel");

    // A later candidate from the same bounded search can prove safe flight.
    r.Plan = new(new(50, 10, 0), new(50, 10, 4), AreaType.Ground,
        new GroundPath(true, "complete", new[] { new WoWPoint(50, 10, 0), new WoWPoint(80, 10, 0) },
            new[] { AreaType.Ground, AreaType.Ground }), true, "later-candidate-proves-flight");
    r.Time += .3;
    Pending(m);
    Check(r.Walks == 0 && r.Flights == 1,
        "a later valid candidate could not preserve the selected flying mount");
}, GroundTransitionPurpose.Interaction);
Case("exhausted grounded flight search retains useful ground fallback", (r,m)=>
{
    r.Motion=r.Motion with { Mounted=false, PreferFlight=true };r.Plan=null;r.Exhausted=true;
    Pending(m);Check(r.Walks==1&&r.Flights==0,"exhausted flight planning parked supported ground travel");
    r.Time=.3;Pending(m);Check(r.Walks==2,"ground fallback was not retained");
},GroundTransitionPurpose.Interaction);
foreach(bool mounted in new[]{false,true})
Case("ground flight planning has a bounded batch budget / mounted="+mounted, (r,m)=>
{
    r.Motion=r.Motion with { Mounted=mounted, PreferFlight=true };r.Plan=null;r.Exhausted=false;
    for(int i=0;i<8;i++){r.Time=i*.21;Pending(m);Check(r.Walks==0&&r.Dismounts==0,"incomplete planning discarded flight preference prematurely");}
    r.Time=1.9;Pending(m);Check(r.Walks==1&&r.Dismounts==0,"planning budget failed to yield a supported ground fallback");
},GroundTransitionPurpose.Interaction);
Case("ground flight planning has a monotonic time budget", (r,m)=>
{
    r.Motion=r.Motion with { Mounted=false, PreferFlight=true };r.Plan=null;r.Exhausted=false;
    Pending(m);Check(r.Walks==0,"initial incomplete search selected ground prematurely");
    r.Time=2.1;Pending(m);Check(r.Walks==1,"elapsed flight planning budget parked the actor");
},GroundTransitionPurpose.Interaction);
foreach (string boundary in new[] { "observe", "search", "validate", "hold", "fly", "descend", "dismount", "walk", "report" })
    Case("owner replacement at " + boundary, (r, m) =>
    {
        if (boundary is "search" or "validate" or "fly" or "descend") Air(r);
        if (boundary == "descend") r.Motion = r.Motion with { Position = new(50, 10, 7) };
        if (boundary == "walk") r.Motion = r.Motion with { Mounted = false };
        r.After = name => { if (name == boundary) r.IsCurrent = false; };
        Check(m.Tick() == GroundTransitionState.Revoked, "stale owner returned completion/pending authority");
        int effects = r.Dismounts + r.Flights + r.Descents + r.Walks;
        Check(m.Tick() == GroundTransitionState.Revoked && effects == r.Dismounts + r.Flights + r.Descents + r.Walks, "revoked owner dispatched again");
    }, boundary == "walk" ? GroundTransitionPurpose.Interaction : GroundTransitionPurpose.Combat);
foreach (Exception error in new Exception[] { new OperationCanceledException("cancel"), new ThreadInterruptedException("stop"), new ObservationUnavailableException("world", "missing") })
    Case("observation signal preserved/" + error.GetType().Name, (r, m) =>
    {
        r.Error = error; Exception? caught = null; try { m.Tick(); } catch (Exception actual) { caught = actual; }
        Check(ReferenceEquals(caught, error) && r.Dismounts + r.Flights + r.Descents + r.Walks == 0, "observation/control signal became ordinary completion");
    });

Console.WriteLine($"Ground transition owner: {passed}/{total}; actual production state machine; controlled observation/effect leaves; no native geometry or live traversal proof.");
if (failures.Count != 0) throw new InvalidOperationException(string.Join("\n", failures));

internal sealed class Runtime : IGroundTransitionRuntime
{
    internal bool IsCurrent = true, AcceptDismount = true, ValidPlan = true, Exhausted;
    internal double Time;
    internal int Flights, Descents, Dismounts, Walks, Resets, Holds;
    internal double DismountAt = double.NaN, RecoveryUntil = double.NegativeInfinity;
    internal Action<string>? After;
    internal Exception? Error;
    internal WoWPoint LastFlight, Target = new(80, 10, 0);
    internal GroundMotion Motion = new(new WoWPoint(10, 10, 0), true, false, false, false, false, false, true, false, false, false);
    internal GroundApproachPlan? Plan = new(new(50, 10, 0), new(50, 10, 4), AreaType.Ground,
        new GroundPath(true, "complete", new[] { new WoWPoint(50, 10, 0), new WoWPoint(60, 15, 0), new WoWPoint(80, 10, 0) },
            new[] { AreaType.Ground, AreaType.KnownBuilding }), true, "controlled-observed-exterior");
    public bool Current => IsCurrent;
    public double Now => Time;
    public bool SearchExhausted => Exhausted;
    public GroundMotion Observe() { if (Error != null) throw Error; var value = Motion; After?.Invoke("observe"); return value; }
    public GroundApproachPlan? Search() { var value = Plan; After?.Invoke("search"); return value; }
    public bool Validate(GroundApproachPlan plan) { bool value = ValidPlan; After?.Invoke("validate"); return value; }
    public void ResetSearch() { Resets++; }
    public void Hold() { Holds++; After?.Invoke("hold"); }
    public void Fly(GroundApproachPlan plan) { Flights++; LastFlight = plan.AirWaypoint; After?.Invoke("fly"); }
    public void Descend(GroundApproachPlan plan) { Descents++; After?.Invoke("descend"); }
    public GroundDismountState Dismount()
    {
        if (!double.IsNaN(DismountAt))
        {
            if (Time < DismountAt) throw new ObservationUnavailableException("dismount", "controlled monotonic clock moved backward");
            if (Time - DismountAt < 12) { After?.Invoke("dismount"); return GroundDismountState.Pending; }
            DismountAt = double.NaN; After?.Invoke("dismount"); return GroundDismountState.Expired;
        }
        if (!AcceptDismount) { After?.Invoke("dismount"); return GroundDismountState.Rejected; }
        Dismounts++; DismountAt = Time; After?.Invoke("dismount"); return GroundDismountState.Submitted;
    }
    public void Walk() { Walks++; After?.Invoke("walk"); }
    public bool RecoveryDeferred(double now) => now < RecoveryUntil;
    public void DeferRecovery(double now) { if (now >= RecoveryUntil) RecoveryUntil = now + 5; }
    public void Report(string phase, string reason, GroundMotion? observation, GroundApproachPlan? plan) { After?.Invoke("report"); }
}
