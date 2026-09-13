using System.Runtime.CompilerServices;
using Styx.Logic.Pathing;
using Styx.WoWInternals;

internal static class ElevatorTransitRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        TestBoardingRequiresAStableDockAndSelectedAttachment();
        TestDepartingLiftCancelsBoarding();
        TestExitRequiresAStableDockAndSafeLanding();
        TestExitDockDepartureRequiresFreshConfirmation();
        TestUnsafeGroundCorridorsBlockBoardingAndExit();
    }

    private static void TestBoardingRequiresAStableDockAndSelectedAttachment()
    {
        var controller = CreateController(out DateTime start);
        var wait = new WoWPoint(4f, 0f, 100f);
        var topDock = new WoWPoint(0f, 0f, 100f);

        Assert(Observe(controller, start, wait, topDock, liveLocationAvailable: true,
                attachedTransportGuid: 0UL, isFalling: false).Kind == ElevatorTransitAction.Wait,
            "an arriving lift must not be boarded from one instantaneous dock sample");
        Assert(Observe(controller, start.AddMilliseconds(749), wait, topDock, true,
                0UL, false).Kind == ElevatorTransitAction.Wait,
            "the boarding dock dwell must last the full 750 milliseconds");

        ElevatorTransitDecision board = Observe(controller,
            start.AddMilliseconds(750), wait, topDock, true, 0UL, false);
        Assert(board.Kind == ElevatorTransitAction.MoveToBoard && board.Target == topDock,
            "a stable selected lift must be boarded at its live center");
        Assert(Observe(controller, start.AddMilliseconds(775), new WoWPoint(0.5f, 0f, 100f),
                topDock, true, 0UL, false).Kind == ElevatorTransitAction.MoveToBoard,
            "boarding must keep moving toward the platform after leaving the waiting point");

        Assert(Observe(controller, start.AddMilliseconds(800), wait, topDock, true,
                attachedTransportGuid: 22UL, isFalling: false).Kind == ElevatorTransitAction.Wait,
            "attachment to an adjacent lift must not satisfy the locked lift crossing");
        Assert(controller.SelectedTransportGuid == 11UL,
            "an adjacent transport must never replace the locked lift GUID");
        Assert(Observe(controller, start.AddMilliseconds(850), topDock, topDock, true,
                attachedTransportGuid: 11UL, isFalling: false).Kind == ElevatorTransitAction.Ride,
            "only attachment to the selected lift may begin the ride");
    }

    private static void TestDepartingLiftCancelsBoarding()
    {
        var controller = CreateController(out DateTime start);
        var wait = new WoWPoint(4f, 0f, 100f);
        var topDock = new WoWPoint(0f, 0f, 100f);
        Observe(controller, start, wait, topDock, true, 0UL, false);
        Observe(controller, start.AddMilliseconds(750), wait, topDock, true, 0UL, false);

        ElevatorTransitDecision cancelled = Observe(controller,
            start.AddMilliseconds(800), new WoWPoint(2f, 0f, 100f),
            new WoWPoint(0f, 0f, 96f), true, 0UL, false);

        Assert(cancelled.Kind == ElevatorTransitAction.MoveToWait && cancelled.Target == wait,
            "if the lift departs before attachment, boarding must retreat to the waiting point");

        controller = CreateController(out start);
        Observe(controller, start, wait, topDock, true, 0UL, false);
        Observe(controller, start.AddMilliseconds(750), wait, topDock, true, 0UL, false);
        Assert(Observe(controller, start.AddMilliseconds(800), new WoWPoint(2f, 0f, 100f),
                new WoWPoint(0f, 0f, 96f), true, 0UL, false,
                boardingPathSafe: false).Kind == ElevatorTransitAction.Wait,
            "boarding retreat must stop when the return corridor is no longer supported");
    }

    private static void TestExitRequiresAStableDockAndSafeLanding()
    {
        var controller = CreateController(out DateTime start);
        var wait = new WoWPoint(4f, 0f, 100f);
        var topDock = new WoWPoint(0f, 0f, 100f);
        var bottomDock = new WoWPoint(0f, 0f, 0f);
        var exit = new WoWPoint(4f, 0f, 0f);

        Observe(controller, start, wait, topDock, true, 0UL, false);
        Observe(controller, start.AddMilliseconds(750), wait, topDock, true, 0UL, false);
        Observe(controller, start.AddMilliseconds(800), topDock, topDock, true, 11UL, false);

        Assert(Observe(controller, start.AddSeconds(5), bottomDock, bottomDock, true,
                11UL, false).Kind == ElevatorTransitAction.Ride,
            "one destination-dock sample must not trigger an exit");
        Assert(Observe(controller, start.AddSeconds(5).AddMilliseconds(749), bottomDock, bottomDock,
                true, 11UL, false).Kind == ElevatorTransitAction.Ride,
            "the destination dock dwell must last the full 750 milliseconds");
        Assert(Observe(controller, start.AddSeconds(5).AddMilliseconds(750), bottomDock, bottomDock,
                true, 11UL, false).Kind == ElevatorTransitAction.MoveToExit,
            "a stable destination dock must move toward the destination-side landing");

        Assert(Observe(controller, start.AddSeconds(6), new WoWPoint(2f, 0f, -2f), bottomDock,
                true, 0UL, true).Kind == ElevatorTransitAction.Wait,
            "falling after detachment must stop exit movement");
        Assert(Observe(controller, start.AddSeconds(6), new WoWPoint(2f, 0f, -8f), bottomDock,
                true, 0UL, false).Kind == ElevatorTransitAction.Wait,
            "detachment at the wrong elevation must not chase a landing across a cliff");
        Assert(Observe(controller, start.AddSeconds(6), new WoWPoint(3.5f, 0f, 0f), bottomDock,
                true, 0UL, false, hasGroundSupport: false).Kind == ElevatorTransitAction.Wait,
            "a transient non-falling flag without ground support must not complete the crossing");
        Assert(Observe(controller, start.AddSeconds(6), new WoWPoint(0.5f, 0f, 0f), bottomDock,
                true, 0UL, false).Kind == ElevatorTransitAction.MoveToExit,
            "safe detachment must continue to the configured landing");
        Assert(Observe(controller, start.AddSeconds(6), new WoWPoint(3.5f, 0f, 0f), bottomDock,
                true, 0UL, false).Kind == ElevatorTransitAction.Complete,
            "the crossing completes only when grounded and within three yards of the landing");
    }

    private static void TestExitDockDepartureRequiresFreshConfirmation()
    {
        var controller = CreateRidingController(out DateTime start);
        var bottomDock = new WoWPoint(0f, 0f, 0f);

        Observe(controller, start, bottomDock, bottomDock, true, 11UL, false);
        Assert(Observe(controller, start.AddMilliseconds(750), bottomDock, bottomDock,
                true, 11UL, false).Kind == ElevatorTransitAction.MoveToExit,
            "fixture must authorize exit after one complete stable destination dwell");

        Assert(Observe(controller, start.AddMilliseconds(800), bottomDock,
                new WoWPoint(0f, 0f, 4f), true, 11UL, false).Kind == ElevatorTransitAction.Ride,
            "a departing lift must revoke the old exit authorization");
        Assert(Observe(controller, start.AddSeconds(2), bottomDock, bottomDock,
                true, 11UL, false).Kind == ElevatorTransitAction.Ride,
            "a returning lift must begin a fresh destination-dock observation");
        Assert(Observe(controller, start.AddSeconds(2).AddMilliseconds(749), bottomDock, bottomDock,
                true, 11UL, false).Kind == ElevatorTransitAction.Ride,
            "the stale exit dwell must not carry across a departure");
        Assert(Observe(controller, start.AddSeconds(2).AddMilliseconds(750), bottomDock, bottomDock,
                true, 11UL, false).Kind == ElevatorTransitAction.MoveToExit,
            "exit may resume only after a new complete stable dwell");
    }

    private static void TestUnsafeGroundCorridorsBlockBoardingAndExit()
    {
        var controller = CreateController(out DateTime start);
        var wait = new WoWPoint(4f, 0f, 100f);
        var topDock = new WoWPoint(0f, 0f, 100f);

        Observe(controller, start, wait, topDock, true, 0UL, false,
            boardingPathSafe: false);
        Assert(Observe(controller, start.AddMilliseconds(750), wait, topDock, true, 0UL, false,
                boardingPathSafe: false).Kind == ElevatorTransitAction.Wait,
            "a stable lift must not be boarded through an unproven approach corridor");

        controller = CreateRidingController(out start);
        var bottomDock = new WoWPoint(0f, 0f, 0f);
        Observe(controller, start, bottomDock, bottomDock, true, 11UL, false,
            exitPathSafe: false);
        Assert(Observe(controller, start.AddMilliseconds(750), bottomDock, bottomDock, true, 11UL,
                false, exitPathSafe: false).Kind == ElevatorTransitAction.Ride,
            "a stable destination dock must not be exited through an unproven landing corridor");

        // Earlier unsafe samples do not count toward a continuous safe dock dwell.
        Assert(Observe(controller, start.AddMilliseconds(800), bottomDock, bottomDock, true, 11UL,
                false, exitPathSafe: true).Kind == ElevatorTransitAction.Ride,
            "corridor recovery must begin fresh dock confirmation rather than reuse unsafe time");
        Assert(Observe(controller, start.AddMilliseconds(1549), bottomDock, bottomDock, true, 11UL,
                false, exitPathSafe: true).Kind == ElevatorTransitAction.Ride,
            "exit recovery must observe the full 750 millisecond safe dwell");
        Assert(Observe(controller, start.AddMilliseconds(1550), bottomDock, bottomDock, true, 11UL,
                false, exitPathSafe: true).Kind == ElevatorTransitAction.MoveToExit,
            "a fresh confirmed safe dock must remain able to authorize exit");
        Assert(Observe(controller, start.AddMilliseconds(1600), new WoWPoint(1f, 0f, 0f), bottomDock,
                true, 0UL, false, hasGroundSupport: true,
                exitPathSafe: false).Kind == ElevatorTransitAction.Wait,
            "ground support on a wrong ledge must not bypass a blocked exit corridor");
    }

    private static ElevatorTransitController CreateRidingController(out DateTime start)
    {
        var controller = CreateController(out start);
        var wait = new WoWPoint(4f, 0f, 100f);
        var topDock = new WoWPoint(0f, 0f, 100f);
        Observe(controller, start.AddSeconds(-1), wait, topDock, true, 0UL, false);
        Observe(controller, start.AddMilliseconds(-250), wait, topDock, true, 0UL, false);
        Observe(controller, start.AddMilliseconds(-200), topDock, topDock, true, 11UL, false);
        return controller;
    }

    private static ElevatorTransitDecision Observe(
        ElevatorTransitController controller,
        DateTime observedAtUtc,
        WoWPoint playerLocation,
        WoWPoint liveTransportLocation,
        bool liveLocationAvailable,
        ulong attachedTransportGuid,
        bool isFalling,
        bool hasGroundSupport = true,
        bool boardingPathSafe = true,
        bool exitPathSafe = true)
    {
        return controller.Observe(
            observedAtUtc,
            playerLocation,
            liveTransportLocation,
            liveLocationAvailable,
            attachedTransportGuid,
            isFalling,
            hasGroundSupport,
            boardingPathSafe,
            exitPathSafe);
    }

    private static ElevatorTransitController CreateController(out DateTime start)
    {
        start = new DateTime(2026, 9, 11, 8, 0, 0, DateTimeKind.Utc);
        var controller = new ElevatorTransitController();
        controller.Begin(
            selectedTransportGuid: 11UL,
            selectedTransportEntry: 11899U,
            startDock: new WoWPoint(0f, 0f, 100f),
            endDock: new WoWPoint(0f, 0f, 0f),
            waitPoint: new WoWPoint(4f, 0f, 100f),
            exitPoint: new WoWPoint(4f, 0f, 0f));
        return controller;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
