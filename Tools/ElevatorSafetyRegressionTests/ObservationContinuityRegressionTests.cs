using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Styx;
using Styx.Logic.Pathing;

internal static class ObservationContinuityRegressionTests
{
    private static readonly DateTime Epoch = new(2026, 9, 13, 0, 0, 0, DateTimeKind.Utc);
    [ModuleInitializer]
    internal static void Run()
    {
        var failures = new List<string>();
        int scenarios = 0;
        void Case(string name, Action test)
        {
            scenarios++;
            try { test(); Console.WriteLine("PASS lift continuity: " + name); }
            catch (Exception error) { failures.Add(name + ": " + error.Message); Console.Error.WriteLine("FAIL lift continuity: " + failures[^1]); }
        }
        foreach (bool down in new[] { false, true })
        {
            string direction = down ? "down" : "up";
            Case(direction + " ordinary stable docking and supported exit still complete", () =>
            {
                var f = new Crossing(down); f.Riding();
                f.Expect(1800, ElevatorTransitAction.MoveToExit);
                f.Expect(1850, ElevatorTransitAction.Complete, attached: 0, player: f.Controller.ExitPoint);
            });
            Case(direction + " lost attachment invalidates exit dwell", () =>
            {
                var f = new Crossing(down); f.Riding();
                f.Expect(1500, ElevatorTransitAction.Wait, attached: 0);
                f.Expect(1900, ElevatorTransitAction.Ride);
                f.Expect(2700, ElevatorTransitAction.MoveToExit);
            });
            Case(direction + " wrong attachment invalidates exit dwell", () =>
            {
                var f = new Crossing(down); f.Riding();
                f.Expect(1500, ElevatorTransitAction.Wait, attached: 99);
                f.Expect(1900, ElevatorTransitAction.Ride);
                f.Expect(2700, ElevatorTransitAction.MoveToExit);
            });
            Case(direction + " falling while nominally attached never authorizes an exit", () =>
            {
                var f = new Crossing(down); f.Riding();
                f.Expect(1800, ElevatorTransitAction.Ride, falling: true);
                f.Expect(1900, ElevatorTransitAction.Ride);
                f.Expect(2700, ElevatorTransitAction.MoveToExit);
            });
            Case(direction + " unsafe exit must regain fresh dock dwell", () =>
            {
                var f = new Crossing(down); f.Riding();
                f.Expect(1800, ElevatorTransitAction.Ride, safe: false);
                f.Expect(1900, ElevatorTransitAction.Ride);
                f.Expect(2700, ElevatorTransitAction.MoveToExit);
            });
            Case(direction + " platform motion inside the dock radius revokes active exit", () =>
            {
                var f = new Crossing(down); f.Riding(); f.Expect(1800, ElevatorTransitAction.MoveToExit);
                var shifted = f.Controller.EndDock.Add(0.1f, 0, 0);
                f.Expect(1900, ElevatorTransitAction.Ride, platform: shifted);
                f.Expect(2700, ElevatorTransitAction.MoveToExit, platform: shifted);
            });
            Case(direction + " active exit safety revocation restarts dwell", () =>
            {
                var f = new Crossing(down); f.Riding(); f.Expect(1800, ElevatorTransitAction.MoveToExit);
                f.Expect(1900, ElevatorTransitAction.Wait, safe: false);
                f.Expect(2000, ElevatorTransitAction.Ride);
                f.Expect(2800, ElevatorTransitAction.MoveToExit);
            });
            Case(direction + " interrupted exit does not trust its old confirmation", () =>
            {
                var f = new Crossing(down); f.Riding(); f.Expect(1800, ElevatorTransitAction.MoveToExit);
                f.Expect(1900, ElevatorTransitAction.Wait, falling: true);
                f.Expect(2000, ElevatorTransitAction.Ride);
                f.Expect(2800, ElevatorTransitAction.MoveToExit);
            });
            Case(direction + " clock reversal within an existing dwell restarts observation", () =>
            {
                var f = new Crossing(down); f.Riding();
                f.Expect(1500, ElevatorTransitAction.Ride);
                f.Expect(1100, ElevatorTransitAction.Ride);
                f.Expect(1800, ElevatorTransitAction.Ride);
                f.Expect(1900, ElevatorTransitAction.MoveToExit);
            });
            Case(direction + " long gap at destination is not continuous stable observation", () =>
            {
                var f = new Crossing(down); f.Riding();
                f.Expect(6000, ElevatorTransitAction.Ride);
                f.Expect(6800, ElevatorTransitAction.MoveToExit);
            });
            Case(direction + " long gap during active exit revokes old authorization", () =>
            {
                var f = new Crossing(down); f.Riding(); f.Expect(1800, ElevatorTransitAction.MoveToExit);
                f.Expect(9000, ElevatorTransitAction.Ride);
                f.Expect(9800, ElevatorTransitAction.MoveToExit);
            });
            Case(direction + " missing live transform still reacquires fresh dwell", () =>
            {
                var f = new Crossing(down); f.Riding(); f.Expect(1800, ElevatorTransitAction.MoveToExit);
                f.Expect(1900, ElevatorTransitAction.Ride, live: false);
                f.Expect(2000, ElevatorTransitAction.Ride);
                f.Expect(2800, ElevatorTransitAction.MoveToExit);
            });
            Case(direction + " detached supported landing remains completable after an interruption", () =>
            {
                var f = new Crossing(down); f.Riding(); f.Expect(1800, ElevatorTransitAction.MoveToExit);
                f.Expect(1900, ElevatorTransitAction.Wait, falling: true);
                f.Expect(2000, ElevatorTransitAction.Complete, attached: 0, player: f.Controller.ExitPoint);
            });
            Case(direction + " long boarding gap starts a fresh confirmation", () =>
            {
                var f = new Crossing(down);
                f.Expect(0, ElevatorTransitAction.Wait, attached: 0, platform: f.Controller.StartDock, player: f.Controller.WaitPoint);
                f.Expect(10000, ElevatorTransitAction.Wait, attached: 0, platform: f.Controller.StartDock, player: f.Controller.WaitPoint);
                f.Expect(10800, ElevatorTransitAction.MoveToBoard, attached: 0, platform: f.Controller.StartDock, player: f.Controller.WaitPoint);
            });
        }
        Console.WriteLine($"Lift observation continuity: {scenarios-failures.Count}/{scenarios}; linked production controller, no game attached.");
        if (failures.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
    }

    private sealed class Crossing
    {
        internal ElevatorTransitController Controller { get; } = new();
        internal Crossing(bool down)
        {
            float start = down ? 30 : 0, end = down ? 0 : 30;
            Controller.Begin(11, 4171, new WoWPoint(0, 0, start), new WoWPoint(0, 0, end),
                new WoWPoint(4, 0, start), new WoWPoint(4, 0, end));
        }
        internal void Riding()
        {
            Expect(0, ElevatorTransitAction.Ride, player: Controller.StartDock, platform: Controller.StartDock);
            Expect(1000, ElevatorTransitAction.Ride);
        }
        internal void Expect(int ms, ElevatorTransitAction expected, ulong attached = 11,
            bool safe = true, bool falling = false, bool live = true, WoWPoint? player = null, WoWPoint? platform = null)
        {
            var action = Controller.Observe(Epoch.AddMilliseconds(ms), player ?? Controller.EndDock,
                platform ?? Controller.EndDock, live, attached, falling, true, true, true, safe);
            if (action.Kind != expected)
                throw new InvalidOperationException($"at {ms}ms expected {expected}, observed {action.Kind} in {Controller.StageName}");
        }
    }
}
