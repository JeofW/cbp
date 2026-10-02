using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Styx.WoWInternals;
using Styx.Logic.Pathing;

// Real24-byte aura records and sparse Spell table, complete current Flightor
// MoveTo with controlled mount/geometry/input boundaries. No native movement.
internal static class FlightorRawAuraLivenessRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        int passed = 0;
        foreach (string scenario in new[] { "unknown-unrelated", "known-crusader", "unreadable" })
        {
            using var fixture = (IDisposable)Activator.CreateInstance(typeof(AuraQueryCoverageRegressionTests)
                .GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
            bool unavailable = scenario == "unreadable";
            if (scenario == "known-crusader") fixture.GetType().GetMethod("Auras", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(fixture, new object[] { new[] { 61988, 32223 } });
            if (unavailable) fixture.GetType().GetMethod("BadRawPointer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(fixture, null);
            FlightorWaitContinuityRegressionTests.RunRawAura(name => ObjectManager.Me.HasAura(name), id => ObjectManager.Me.HasAura(id), unavailable);
            passed++;
        }
        foreach (int id in new[] { 33943, 33950, 40120, 40123, 1066, 1446, 61988 })
        {
            using var fixture = (IDisposable)Activator.CreateInstance(typeof(AuraQueryCoverageRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
            fixture.GetType().GetMethod("Auras", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(fixture, new object[] { new[] { 61988, id } });
            bool form = (bool)typeof(Flightor).GetMethod("HasTravelFormAura", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { ObjectManager.Me })!;
            if (form != (id != 61988)) throw new InvalidOperationException("Original form identity was lost or invented: " + id);
            passed++;
        }
        Console.WriteLine($"Flightor raw UNKNOWN liveness: {passed}/10; actual aura reader and full MoveTo, controlled geometry/input.");
    }
}
