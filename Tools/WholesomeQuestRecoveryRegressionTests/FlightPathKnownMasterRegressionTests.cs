using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.WoWInternals;

// Extend actual cached-origin admission, not a replacement route algorithm.
// The retained fixture provides a real test-process player and an observed empty
// flight-merchant set. These tests do not invoke navigation, taxi dispatch or Lua.
internal static class FlightPathKnownMasterRegressionTests
{
    private static readonly WoWPoint From = new(0, 0, 0), To = new(1100, 0, 0);
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string text) : base(text) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Known flight-origin tests require Windows x86.");
        var cases = new List<(string Name, Action<Fixture> Test)>
        {
            ("a known origin remains a read-only connection", f => f.Query(true)),
            ("a known origin retains ordinary publication and XYZ endpoints", f => f.Publish(true)),
            ("a nearer connected origin with unknown master does not hide a known connection", f =>
            { f.UnknownOrigin(1); f.Query(true); }),
            ("an unknown nearer origin without a merchant does not prevent known-origin publication", f =>
            { f.UnknownOrigin(1); f.Publish(true); }),
            ("several connected unknown masters do not terminate the read-only search", f =>
            { f.UnknownOrigin(1); f.UnknownOrigin(2); f.Query(true); }),
            ("input insertion order cannot make an unknown master hide publication", f =>
            { f.UnknownOrigin(1); FlightPaths.XmlNodes.Reverse(); f.Publish(true); }),
            ("an unknown farther master does not displace the nearer known origin", f =>
            { f.UnknownOrigin(100); f.Publish(true); }),
            ("unknown masters alone do not become a known connection", f =>
            { f.UnknownOrigin(1); f.Origin.MasterEntry = 0; f.Query(false); }),
            ("unknown masters alone without a merchant preserve prior publication", f =>
            { f.UnknownOrigin(1); f.Origin.MasterEntry = 0; f.Publish(false); }),
            ("a disconnected known origin cannot borrow an unknown origin's connection", f =>
            { f.UnknownOrigin(1); f.Origin.Connections.Clear(); f.Query(false); }),
            ("disabled flight setting still vetoes publication with a known alternative", f =>
            { f.UnknownOrigin(1); f.Settings.UseFlightPaths = false; f.Publish(false); }),
            ("missing player still vetoes the read-only query with a known alternative", f =>
            { f.UnknownOrigin(1); ObjectManager.Me = null; f.Query(false); }),
        };
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { using var fixture = new Fixture(); item.Test(fixture); passed++; Console.WriteLine("PASS flight-path known master: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL flight-path known master assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR flight-path known master fixture/owner: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Flight-path known-master scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual cached selection/publication; no navigation, taxi dispatch or game attached.");
        if (assertions + unexpected != 0)
            throw new InvalidOperationException($"Flight-path known-master regressions: assertions={assertions}; unexpected={unexpected}");
    }

    private sealed class Fixture : IDisposable
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly object source;
        private readonly XmlFlightNode previousFrom, previousTo;
        private readonly FlightPathReason previousReason;
        private readonly bool previousNeed;
        private readonly BotPoi previousPoi;

        internal Fixture()
        {
            var type = typeof(FlightPathAdmissionRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Retained flight admission fixture is missing.");
            source = Activator.CreateInstance(type, true)!;
            previousFrom = FlightPaths.TakingPathFrom; previousTo = FlightPaths.TakingPathTo;
            previousReason = FlightPaths.Reason; previousNeed = FlightPaths.NeedFlightPath; previousPoi = BotPoi.Current;
            try
            {
                if (ObjectManager.CachedUnits.Count != 0)
                    throw new InvalidOperationException("Fixture requires an observed empty flight-merchant set.");
            }
            catch { ((IDisposable)source).Dispose(); throw; }
        }
        internal XmlFlightNode Origin => (XmlFlightNode)source.GetType().GetField("Origin", Fields)!.GetValue(source)!;
        private XmlFlightNode Destination => (XmlFlightNode)source.GetType().GetField("Destination", Fields)!.GetValue(source)!;
        internal CharacterSettings Settings => (CharacterSettings)source.GetType().GetField("Settings", Fields)!.GetValue(source)!;

        internal void UnknownOrigin(int x)
        {
            var node = new XmlFlightNode(0, 20, "audit-unknown-master-" + x, 1, new WoWPoint(x, 0, 0));
            node.Connect(Destination.Name);
            FlightPaths.XmlNodes.Insert(0, node);
        }
        internal void Query(bool expected)
        {
            bool result = FlightPaths.HasKnownConnection(From, To);
            AssertUnchanged();
            Check(result == expected, $"expected known connection={expected}; actual={result}");
        }
        internal void Publish(bool expected)
        {
            bool result = FlightPaths.SetFlightPathUsage(From, To, out var start, out var end);
            if (!expected)
            {
                AssertUnchanged();
                Check(!result && Empty(start) && Empty(end), "rejected selection returned success or actionable coordinates");
                return;
            }
            Check(result, "connected known alternative was rejected because a nearer master was unknown");
            Check(ReferenceEquals(FlightPaths.TakingPathFrom, Origin) && ReferenceEquals(FlightPaths.TakingPathTo, Destination)
                && start == Origin.Location && end == Destination.Location, "publication chose a different origin/destination or changed XYZ");
            Check(FlightPaths.Reason == FlightPathReason.Use && FlightPaths.NeedFlightPath
                && BotPoi.Current.Type == PoiType.Fly && BotPoi.Current.Entry == Origin.MasterEntry
                && BotPoi.Current.Location == Origin.Location, "published POI and flight intent disagree with the selected known master");
        }
        private void AssertUnchanged() => Check(ReferenceEquals(FlightPaths.TakingPathFrom, previousFrom)
            && ReferenceEquals(FlightPaths.TakingPathTo, previousTo) && FlightPaths.Reason == previousReason
            && FlightPaths.NeedFlightPath == previousNeed && ReferenceEquals(BotPoi.Current, previousPoi),
            "read-only/rejected candidate changed the previously published intent");
        public void Dispose() => ((IDisposable)source).Dispose();
    }
    private static bool Empty(WoWPoint point) => float.IsNaN(point.X) && float.IsNaN(point.Y) && float.IsNaN(point.Z);
    private static void Check(bool condition, string message) { if (!condition) throw new AssertionFailure(message); }
}
