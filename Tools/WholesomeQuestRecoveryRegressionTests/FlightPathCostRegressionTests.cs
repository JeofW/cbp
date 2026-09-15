using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Styx.Logic;
using Styx.Logic.Pathing;

// Actual FlightPaths cost owners, not a replacement estimator. Full-travel cases
// deliberately start/end at the flight nodes, so neither side invokes navigation.
// This checks numerical evidence only: no meshes, taxi dispatch, Lua or live game.
internal static class FlightPathCostRegressionTests
{
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string text) : base(text) { } }
    private static readonly WoWPoint Origin = new(0, 0, 0);
    private static readonly WoWPoint Near = new(19.6f, 0, 0);
    private static WoWPoint[] Segment => new[] { Origin, new WoWPoint(3, 4, 12) };

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Travel-cost owner tests require Windows x86.");
        var cases = new List<(string Name, Action Test)>
        {
            ("missing ground path remains unknown", () => Unknown(() => FlightPaths.GetRunPathTime(null!, 7))),
            ("empty ground path remains unknown", () => Unknown(() => FlightPaths.GetRunPathTime(Array.Empty<WoWPoint>(), 7))),
            ("one finite stationary point has zero cost", () => Equal(() => FlightPaths.GetRunPathTime(new[] { Origin }, 7), 0)),
            ("repeated finite stationary points have zero cost", () => Equal(() => FlightPaths.GetRunPathTime(new[] { Origin, Origin }, 7), 0)),
            ("ground distance retains all three coordinates", () => Equal(() => FlightPaths.GetRunPathTime(Segment, 7), 2)),
            ("ground segments accumulate before rounding", () => Equal(() => FlightPaths.GetRunPathTime(new[] { Origin, new WoWPoint(3, 4, 0), new WoWPoint(3, 4, 12) }, 2), 9)),
            ("negative finite world coordinates remain valid", () => Equal(() => FlightPaths.GetRunPathTime(new[] { new WoWPoint(-3, -4, 0), Origin }, 2), 3)),
            ("large finite ground cost remains positive and usable", () => Equal(() => FlightPaths.GetRunPathTime(new[] { Origin, new WoWPoint(1000000, 0, 0) }, 10), 100000)),
            ("zero speed does not produce a negative ground time", () => Unknown(() => FlightPaths.GetRunPathTime(Segment, 0))),
            ("negative speed is unknown rather than negative cost", () => Unknown(() => FlightPaths.GetRunPathTime(Segment, -7))),
            ("NaN speed is unknown rather than an integer conversion", () => Unknown(() => FlightPaths.GetRunPathTime(Segment, float.NaN))),
            ("positive infinite speed is not a free route", () => Unknown(() => FlightPaths.GetRunPathTime(Segment, float.PositiveInfinity))),
            ("negative infinite speed is not a free route", () => Unknown(() => FlightPaths.GetRunPathTime(Segment, float.NegativeInfinity))),
            ("tiny positive speed cannot overflow into a cheap route", () => Unknown(() => FlightPaths.GetRunPathTime(Segment, float.Epsilon))),
            ("invalid single point is not a known stationary route", () => Unknown(() => FlightPaths.GetRunPathTime(new[] { WoWPoint.Empty }, 7))),
            ("invalid first point makes ground cost unknown", () => Unknown(() => FlightPaths.GetRunPathTime(new[] { WoWPoint.Empty, Origin }, 7))),
            ("invalid last elevation makes ground cost unknown", () => Unknown(() => FlightPaths.GetRunPathTime(new[] { Origin, new WoWPoint(3, 4, float.NaN) }, 7))),
            ("infinite single point is not a known stationary route", () => Unknown(() => FlightPaths.GetRunPathTime(new[] { new WoWPoint(float.PositiveInfinity, 0, 0) }, 7))),
            ("infinite intermediate geometry makes ground cost unknown", () => Unknown(() => FlightPaths.GetRunPathTime(new[] { Origin, new WoWPoint(0, float.NegativeInfinity, 0), Origin }, 7))),
            ("finite coordinates with overflowing segment length remain unknown", () => Unknown(() => FlightPaths.GetRunPathTime(new[] { Origin, new WoWPoint(float.MaxValue / 4, 0, 0) }, 7))),
            ("seconds beyond the integer estimator domain remain unknown", () => Unknown(() => FlightPaths.GetRunPathTime(new[] { Origin, new WoWPoint(1000000, 0, 0) }, 0.00001f))),
            ("ordinary airborne estimate retains the existing winding factor", () => Equal(() => FlightPaths.GetFlightPathTime(Origin, Near), 2)),
            ("finite airborne estimate remains the retained planar approximation", () => Equal(() => FlightPaths.GetFlightPathTime(Origin, new WoWPoint(0, 0, 100)), 0)),
            ("missing airborne endpoint does not produce negative time", () => Unknown(() => FlightPaths.GetFlightPathTime(Origin, WoWPoint.Empty))),
            ("infinite airborne endpoint does not produce negative time", () => Unknown(() => FlightPaths.GetFlightPathTime(Origin, new WoWPoint(float.PositiveInfinity, 0, 0)))),
            ("unknown airborne elevation is not valid endpoint evidence", () => Unknown(() => FlightPaths.GetFlightPathTime(Origin, new WoWPoint(19.6f, 0, float.NaN)))),
            ("full cost with no ground legs preserves valid flight time", () => Equal(() => FlightPaths.GetFullTravelTime(Origin, Near, Origin, Near, 7), 2)),
            ("full cost cannot bypass invalid zero speed through empty ground legs", () => Unknown(() => FlightPaths.GetFullTravelTime(Origin, Near, Origin, Near, 0))),
            ("full cost cannot bypass unknown speed through empty ground legs", () => Unknown(() => FlightPaths.GetFullTravelTime(Origin, Near, Origin, Near, float.NaN))),
            ("full cost cannot bypass infinite speed through empty ground legs", () => Unknown(() => FlightPaths.GetFullTravelTime(Origin, Near, Origin, Near, float.PositiveInfinity))),
            ("full cost preserves unknown airborne overflow", () =>
            { var far = new WoWPoint(float.MaxValue / 4, 0, 0); Unknown(() => FlightPaths.GetFullTravelTime(Origin, far, Origin, far, 7)); }),
        };
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { item.Test(); passed++; Console.WriteLine("PASS flight-path cost: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL flight-path cost assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR flight-path cost fixture/owner: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Flight-path cost scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual cost owners; no route probing, taxi dispatch or game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException($"Flight-path cost regressions: assertions={assertions}; unexpected={unexpected}");
    }

    private static void Unknown(Func<TimeSpan> action)
    {
        TimeSpan result;
        try { result = action(); }
        catch (ArgumentOutOfRangeException error) { throw new AssertionFailure("unknown input escaped as a range error: " + error.GetType().Name); }
        catch (OverflowException error) { throw new AssertionFailure("unknown input escaped as arithmetic overflow: " + error.GetType().Name); }
        if (result != TimeSpan.MaxValue) throw new AssertionFailure("expected unknown cost; got " + result);
    }
    private static void Equal(Func<TimeSpan> action, int seconds)
    {
        TimeSpan result = action();
        if (result != TimeSpan.FromSeconds(seconds)) throw new AssertionFailure("expected " + seconds + " seconds; got " + result);
    }
}
