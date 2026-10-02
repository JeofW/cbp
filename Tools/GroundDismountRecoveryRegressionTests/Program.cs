using Styx.Logic.Pathing;
using Styx.Logic.POI;

var failures = new List<string>();
int passed = 0, total = 0;
double now = 0;

void Check(bool condition, string reason)
{
    if (!condition) throw new InvalidOperationException(reason);
}

void GroundedMounted()
{
    World.Actor.Position = new WoWPoint(100, 10, 0);
    World.Actor.Flags = 0;
    World.Actor.Transport = 0;
    World.Actor.MountedValue = true;
    World.Actor.InVehicle = false;
}

void Case(string name, Action body)
{
    total++;
    try
    {
        World.Reset();
        GroundedMounted();
        now = 0;
        GroundTransitionRuntime.MonotonicClockOverride = () => now;
        body();
        passed++;
        Console.WriteLine("PASS ground dismount recovery: " + name);
    }
    catch (Exception error)
    {
        failures.Add(name + ": " + error.Message);
        Console.Error.WriteLine("FAIL ground dismount recovery: " + name + ": " + error.Message);
    }
    finally
    {
        Styx.BotEvents.Stop();
        GroundTransitionRuntime.MonotonicClockOverride = null;
    }
}

GroundTransitionRuntime Runtime() => new(GroundTransitionPurpose.Combat, World.Target.Position, World.Target, () => true);

Case("support observation cannot be borrowed after movement during preparation", () =>
{
    var runtime = Runtime();
    bool changed = false;
    World.Callback = stage =>
    {
        if (stage != "dismount-prepare") return;
        World.Callback = null; changed = true;
        World.Actor.Position = World.Actor.Position.Add(2, 0, 0);
        // Flags may still look grounded during a delayed movement observation;
        // the previous footprint no longer supports this actor position.
    };
    Check(runtime.Dismount() == GroundDismountState.Rejected && changed && World.Dismounts == 0,
        "dismount native admission reused support from a previous position");
});

Case("position observation cannot return admission after reentrant work replacement", () =>
{
    var runtime = Runtime();
    bool changed = false;
    World.Callback = stage =>
    {
        if (stage != "dismount-prepare") return;
        World.Callback = next =>
        {
            if (next != "location") return;
            World.Callback = null; changed = true;
            BotPoi.CurrentGeneration++;
        };
    };
    Check(runtime.Dismount() == GroundDismountState.Rejected && changed && World.Dismounts == 0,
        "late position observation returned its predecessor's work admission");
});

Case("known pre-entry rejection cannot poison successor actor-session", () =>
{
    World.DismountMode = "legacy-preentry-reject";
    var first = Runtime();
    Check(first.Dismount() == GroundDismountState.Rejected && World.DismountAttempts == 1 && World.Dismounts == 0,
        "controlled rejected dispatch did not reach the pre-entry reservation boundary");
    World.DismountMode = "success";
    var successor = Runtime();
    Check(successor.Dismount() == GroundDismountState.Submitted && World.DismountAttempts == 2 && World.Dismounts == 1,
        "rejected dispatch left a static pending lease that suppressed the successor request");
});

Case("submitted request survives target-owner replacement without duplicate", () =>
{
    World.DismountMode = "success";
    var first = Runtime();
    Check(first.Dismount() == GroundDismountState.Submitted && World.DismountAttempts == 1 && World.Dismounts == 1,
        "initial strict submission was not recorded");
    var successor = Runtime();
    Check(successor.Dismount() == GroundDismountState.Pending && World.DismountAttempts == 1 && World.Dismounts == 1,
        "same actor/session replacement duplicated an unacknowledged request");
});

Case("post-entry UNKNOWN client lease suppresses successor duplicate", () =>
{
    World.DismountMode = "ambiguous-postentry";
    var first = Runtime();
    Exception? caught = null;
    try { _ = first.Dismount(); }
    catch (Exception error) { caught = error; }
    Check(caught is Styx.Helpers.ObservationUnavailableException
        && World.DismountAttempts == 1 && World.Dismounts == 1,
        "controlled post-entry UNKNOWN did not record exactly one client effect");

    World.DismountMode = "success";
    Styx.Logic.POI.BotPoi.CurrentGeneration++;
    var successor = Runtime();
    Check(successor.Dismount() == GroundDismountState.Submitted
        && World.DismountAttempts == 2 && World.Dismounts == 1,
        "same actor/session successor did not inherit the client-side pending lease");
    Check(successor.Dismount() == GroundDismountState.Pending
        && World.DismountAttempts == 2 && World.Dismounts == 1,
        "managed lease armed from the client pending receipt did not suppress later retries");
});

Case("dismount cancellation propagates and restores ambient owner", () =>
{
    World.DismountMode = "cancel";
    var first = Runtime();
    Exception? caught = null;
    try { _ = first.Dismount(); }
    catch (Exception error) { caught = error; }
    Check(caught is OperationCanceledException && World.DismountAttempts == 1 && World.Dismounts == 0,
        "cancellation was swallowed or converted into a dismount effect");
    Check(GroundDismountDispatchContext.Owner == null,
        "cancellation leaked the thread-local actor/session dispatch owner");
    World.DismountMode = "success";
    var successor = Runtime();
    Check(successor.Dismount() == GroundDismountState.Submitted && World.Dismounts == 1,
        "cancellation poisoned the successor actor/session request");
});

Case("reentrant successor inherits submitted lease without duplicate", () =>
{
    var first = Runtime();
    bool invoked = false;
    World.Callback = stage =>
    {
        if (stage != "dismount") return;
        World.Callback = null;
        invoked = true;
        var successor = Runtime();
        Check(successor.Dismount() == GroundDismountState.Pending && World.Dismounts == 1,
            "reentrant successor duplicated the already-submitted actor/session request");
    };
    Check(first.Dismount() == GroundDismountState.Submitted && invoked && World.Dismounts == 1,
        "initial request did not survive reentrant owner replacement");
    Check(GroundDismountDispatchContext.Owner == null,
        "reentrant dispatch did not restore the prior ambient owner");
});

Case("expired request enters cooldown before one fresh supported retry", () =>
{
    var runtime = Runtime();
    var machine = new GroundTransitionMachine(GroundTransitionPurpose.Combat, runtime);
    Check(machine.Tick() == GroundTransitionState.Pending && World.DismountAttempts == 1 && World.Dismounts == 1,
        "initial grounded request was not submitted exactly once");
    now = 11;
    Check(machine.Tick() == GroundTransitionState.Pending && World.DismountAttempts == 1,
        "pre-expiry pulse duplicated the pending request");
    now = 12;
    Check(machine.Tick() == GroundTransitionState.Unavailable && World.DismountAttempts == 1,
        "pending expiry did not become a diagnosed unavailable state before retry");
    now = 16;
    Check(machine.Tick() == GroundTransitionState.Unavailable && World.DismountAttempts == 1,
        "bounded recovery cooldown emitted an early retry");
    now = 18;
    Check(machine.Tick() == GroundTransitionState.Pending && World.DismountAttempts == 2 && World.Dismounts == 2,
        "post-cooldown current supported ground did not permit exactly one fresh request");
});

Case("replacement owner cannot reset actor-session recovery cooldown", () =>
{
    var firstRuntime = Runtime();
    var first = new GroundTransitionMachine(GroundTransitionPurpose.Combat, firstRuntime);
    Check(first.Tick() == GroundTransitionState.Pending && World.DismountAttempts == 1,
        "initial owner did not submit one request");
    now = 12;
    Check(first.Tick() == GroundTransitionState.Unavailable && World.DismountAttempts == 1,
        "initial owner did not diagnose the expired request");
    Styx.Logic.POI.BotPoi.CurrentGeneration++;
    now = 13;
    var successorRuntime = Runtime();
    var successor = new GroundTransitionMachine(GroundTransitionPurpose.Combat, successorRuntime);
    Check(successor.Tick() == GroundTransitionState.Unavailable && World.DismountAttempts == 1,
        "replacement owner bypassed the shared recovery cooldown");
    now = 18;
    Check(successor.Tick() == GroundTransitionState.Pending && World.DismountAttempts == 2 && World.Dismounts == 2,
        "replacement owner did not recover with exactly one fresh request after the inherited cooldown");
});

Case("observed forced unmount resolves active recovery cooldown", () =>
{
    var runtime = Runtime();
    var machine = new GroundTransitionMachine(GroundTransitionPurpose.Combat, runtime);
    Check(machine.Tick() == GroundTransitionState.Pending && World.DismountAttempts == 1,
        "initial request missing");
    now = 12;
    Check(machine.Tick() == GroundTransitionState.Unavailable && World.DismountAttempts == 1,
        "timeout did not enter recovery");
    World.Actor.MountedValue = false;
    now = 13;
    Check(machine.Tick() == GroundTransitionState.Ready && World.DismountAttempts == 1,
        "authoritative supported unmount stayed hidden behind recovery cooldown");
});

foreach (string blocked in new[] { "taxi", "root", "stun" })
    Case("complete ground-unit state blocks dismount while " + blocked, () =>
    {
        switch (blocked)
        {
            case "taxi": World.Actor.OnTaxi = true; break;
            case "root": World.Actor.Rooted = true; break;
            case "stun": World.Actor.Stunned = true; break;
        }
        var runtime = Runtime();
        var machine = new GroundTransitionMachine(GroundTransitionPurpose.Combat, runtime);
        Check(machine.Tick() == GroundTransitionState.Pending && World.DismountAttempts == 0,
            "descriptor state admitted mount removal while " + blocked);
    });

Case("flight form remains mounted when legacy mounted bit is false", () =>
{
    World.Actor.MountedValue = false;
    World.Actor.Shapeshift = Styx.ShapeshiftForm.FlightForm;
    var runtime = Runtime();
    var machine = new GroundTransitionMachine(GroundTransitionPurpose.Combat, runtime);
    Check(machine.Tick() == GroundTransitionState.Pending && World.DismountAttempts == 1,
        "complete form observation fabricated an unmounted-ready state");
});

Case("ground-unit observation UNKNOWN propagates without a dismount", () =>
{
    var signal = new Styx.Helpers.ObservationUnavailableException("ground-unit-state", "controlled incomplete descriptor");
    World.ObservationError = signal;
    var runtime = Runtime();
    Exception? caught = null;
    try { _ = runtime.Observe(); }
    catch (Exception error) { caught = error; }
    Check(ReferenceEquals(caught, signal) && World.DismountAttempts == 0,
        "incomplete descriptor observation was defaulted, swallowed, or caused an effect");
});

Case("production Mount source reserves only after strict exact receipt", () =>
{
    string? root = null;
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        if (File.Exists(Path.Combine(directory.FullName, "CopilotBuddy.csproj"))) { root = directory.FullName; break; }
    Check(root != null, "tracked checkout root unavailable");
    string source = File.ReadAllText(Path.Combine(root!, "Styx", "Logic", "Mount.cs"));
    int start = source.IndexOf("internal static bool TryDismountOwned", StringComparison.Ordinal);
    int end = source.IndexOf("private static bool CanRemoveMount", start, StringComparison.Ordinal);
    Check(start >= 0 && end > start, "production TryDismountOwned source boundary changed");
    string body = source.Substring(start, end - start);
    int strict = body.IndexOf("Lua.GetObservedReturnValues", StringComparison.Ordinal);
    int exact = body.IndexOf("receipt.Count != 1 || receipt[0] != GroundDismountReceipt", StringComparison.Ordinal);
    int reserve = body.IndexOf("submitted?.Invoke()", StringComparison.Ordinal);
    Check(strict >= 0 && exact > strict && reserve > exact,
        "pending reservation is not ordered after strict Lua dispatch and exact local receipt validation");
    Check(body.IndexOf("Lua.DoString", StringComparison.Ordinal) < 0,
        "legacy swallow-all Lua dispatch remains in the owned dismount path");
    Check(body.IndexOf("WorldQueryObservation.ReadGroundUnitState", StringComparison.Ordinal) >= 0,
        "production dismount admission still depends on legacy false-default mount/form descriptors");
    Check(body.IndexOf("state.MountDisplayId == mountDisplayId", StringComparison.Ordinal) >= 0,
        "production dismount admission does not retain the exact observed mount identity");
    int clientLease = body.IndexOf("_G.CopilotBuddy_GroundDismountLease={schema='cb-ground-dismount-v1',owner=owner,startedAt=now,untilAt=now+ttl}", StringComparison.Ordinal);
    int action = body.IndexOf("+ action +", StringComparison.Ordinal);
    Check(clientLease >= 0 && action > clientLease,
        "client actor/session lease is not installed immediately before the dismount action");
});

Console.WriteLine($"Ground dismount recovery: {passed}/{total}; linked actual runtime/context/machine/geometry/query, controlled dismount leaf; no live Lua/client effect proof.");
if (failures.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
