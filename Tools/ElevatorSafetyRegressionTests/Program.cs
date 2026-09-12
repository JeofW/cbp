using Styx.Logic.Pathing;

// The linked module initializer also executes the five existing elevator regression scenarios.
var cases = new (string Name, Action Run)[]
{
    ("unsafe corridor revokes an active boarding decision", CorridorRevocation),
    ("corridor recovery requires a fresh dock dwell", () => FreshDwell("corridor")),
    ("ground-support recovery requires a fresh dock dwell", () => FreshDwell("ground")),
    ("falling recovery requires a fresh dock dwell", () => FreshDwell("falling")),
    ("wrong-transport recovery requires a fresh dock dwell", () => FreshDwell("transport")),
    ("selected attachment retains ride ownership", AttachedSelected)
};
int failed = 0;
foreach (var test in cases)
{
    try { test.Run(); Console.WriteLine($"PASS: {test.Name}"); }
    catch (Exception error) { failed++; Console.Error.WriteLine($"FAIL: {test.Name}: {error.Message}"); }
}
Console.WriteLine($"Existing elevator scenarios: 5 completed. New scenarios: {cases.Length - failed}/{cases.Length} passed. No game attached.");
return failed == 0 ? 0 : 1;

static ElevatorTransitController Boarding()
{
    var controller = new ElevatorTransitController();
    controller.Begin(11UL, 11899U, new WoWPoint(0, 0, 100), new WoWPoint(0, 0, 0),
        new WoWPoint(4, 0, 100), new WoWPoint(4, 0, 0));
    Equal(ElevatorTransitAction.Wait, Observe(controller, 0).Kind, "first dock sample");
    Equal(ElevatorTransitAction.MoveToBoard, Observe(controller, 750).Kind, "fixture must reach Boarding");
    return controller;
}

static ElevatorTransitDecision Observe(ElevatorTransitController controller, int milliseconds,
    bool corridor = true, bool ground = true, bool falling = false, ulong attached = 0)
{
    return controller.Observe(new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(milliseconds),
        new WoWPoint(4, 0, 100), new WoWPoint(0, 0, 100), true, attached, falling, ground, corridor, true);
}

static void CorridorRevocation()
{
    var controller = Boarding();
    Equal(ElevatorTransitAction.Wait, Observe(controller, 800, corridor: false).Kind,
        "boarding must stop when the corridor becomes unsupported");
    Equal(ElevatorTransitAction.Wait, Observe(controller, 5000, corridor: false).Kind,
        "continued uncertainty must never authorize movement");
}

static void FreshDwell(string loss)
{
    var controller = Boarding();
    Observe(controller, 800, corridor: loss != "corridor", ground: loss != "ground",
        falling: loss == "falling", attached: loss == "transport" ? 22UL : 0UL);
    Equal(ElevatorTransitAction.Wait, Observe(controller, 1600).Kind,
        "the old dock confirmation must be discarded after a safety interruption");
    Equal(ElevatorTransitAction.Wait, Observe(controller, 2349).Kind, "fresh dwell must last 750 ms");
    Equal(ElevatorTransitAction.MoveToBoard, Observe(controller, 2350).Kind,
        "safe boarding must recover after a full fresh dwell");
    if (controller.SelectedTransportGuid != 11UL) throw new InvalidOperationException("selected transport changed");
}

static void AttachedSelected()
{
    var controller = Boarding();
    Equal(ElevatorTransitAction.Ride, Observe(controller, 800, corridor: false, ground: false, attached: 11UL).Kind,
        "confirmed attachment transfers ownership to riding rather than ground boarding");
}

static void Equal(ElevatorTransitAction expected, ElevatorTransitAction actual, string message)
{
    if (expected != actual) throw new InvalidOperationException($"{message}; expected {expected}, observed {actual}");
}
