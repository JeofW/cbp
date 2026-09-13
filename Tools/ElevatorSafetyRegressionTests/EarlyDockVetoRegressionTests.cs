using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Styx.Logic.Pathing;

internal static class EarlyDockVetoRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var failures = new List<string>();
        foreach (bool down in new[] { false, true })
        foreach (string interruption in new[] { "early-unsafe", "detached-unsupported", "detached-unsafe", "detached-safe" })
        {
            try
            {
                var controller = new ElevatorTransitController();
                float start = down ? 30 : 0, end = down ? 0 : 30;
                controller.Begin(11, 4171, new WoWPoint(0, 0, start), new WoWPoint(0, 0, end),
                    new WoWPoint(4, 0, start), new WoWPoint(4, 0, end));
                ElevatorTransitAction Observe(int ms, ulong attached = 11, bool ground = true, bool safe = true) =>
                    controller.Observe(new DateTime(2026,9,13,0,0,0,DateTimeKind.Utc).AddMilliseconds(ms),
                        ms == 0 ? controller.StartDock : controller.EndDock,
                        ms == 0 ? controller.StartDock : controller.EndDock, true, attached, false, ground, true, true, safe).Kind;
                void Expect(ElevatorTransitAction expected, ElevatorTransitAction actual)
                {
                    if (actual != expected) throw new InvalidOperationException($"expected {expected}, observed {actual}");
                }
                Expect(ElevatorTransitAction.Ride, Observe(0));
                Expect(ElevatorTransitAction.Ride, Observe(1000));
                int resumed;
                if (interruption == "early-unsafe")
                {
                    Expect(ElevatorTransitAction.Ride, Observe(1500, safe: false));
                    resumed = 1900;
                }
                else
                {
                    Expect(ElevatorTransitAction.MoveToExit, Observe(1800));
                    Expect(interruption == "detached-safe" ? ElevatorTransitAction.MoveToExit : ElevatorTransitAction.Wait,
                        Observe(1900, attached: 0, ground: interruption != "detached-unsupported", safe: interruption != "detached-unsafe"));
                    resumed = 2000;
                }
                Expect(ElevatorTransitAction.Ride, Observe(resumed));
                Expect(ElevatorTransitAction.MoveToExit, Observe(resumed + 800));
                Console.WriteLine($"PASS lift intermediate veto: down={down} {interruption}");
            }
            catch (Exception error)
            {
                string message = $"down={down} {interruption}: {error.Message}";
                failures.Add(message); Console.Error.WriteLine("FAIL lift intermediate veto: " + message);
            }
        }
        Console.WriteLine($"Lift intermediate vetoes: {8-failures.Count}/8; actual controller, no game attached.");
        if (failures.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
    }
}
