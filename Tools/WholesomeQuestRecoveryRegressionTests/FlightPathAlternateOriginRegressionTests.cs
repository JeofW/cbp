using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.Pathing;
using Styx.Logic.POI;

// Actual cached-network admission and POI publication. Connectivity is not mesh
// reachability or optimal route cost: no navigation/taxi/Lua dispatch is invoked.
internal static class FlightPathAlternateOriginRegressionTests
{
    private static readonly WoWPoint From = new(0, 0, 0), To = new(1100, 0, 0);
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string message) : base(message) { } }
    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Alternate flight origin tests require Windows x86.");
        var cases = new List<(string Name, Action<Fixture> Test)>
        {
            ("ordinary nearest connected origin remains selected", f => f.Accept()),
            ("a closer disconnected origin cannot hide a usable cached connection", f => { f.Shadow(); f.Accept(); }),
            ("a closer unknown connection set cannot hide a usable cached connection", f => { f.Shadow().Connections = null!; f.Accept(); }),
            ("a closer self-only origin cannot hide a usable cached connection", f => { var n = f.Shadow(); n.Connect(n.Name); f.Accept(); }),
            ("a closer dangling connection cannot hide a usable cached connection", f => { f.Shadow().Connect("absent destination"); f.Accept(); }),
            ("a backwards-only origin cannot hide a forward cached connection", f =>
            {
                var n = f.Shadow(); var backwards = new XmlFlightNode(51, 20, "audit-backwards", 1, new WoWPoint(-1000, 0, 0));
                n.Connect(backwards.Name); FlightPaths.XmlNodes.Add(backwards); f.Accept();
            }),
            ("several disconnected origins do not terminate the candidate search", f =>
            { f.Shadow(); FlightPaths.XmlNodes.Add(new XmlFlightNode(52, 20, "second-disconnected", 1, new WoWPoint(2, 0, 0))); f.Accept(); }),
            ("the nearest valid origin is retained over a farther valid origin", f =>
            { var n = new XmlFlightNode(53, 20, "farther-valid", 1, new WoWPoint(100, 0, 0)); n.Connect(f.Destination.Name); FlightPaths.XmlNodes.Add(n); f.Accept(); }),
            ("origin distance retains elevation rather than treating floors as identical", f =>
            { var n = f.Shadow(); n.Location = new WoWPoint(0, 0, 100); n.Connect(f.Destination.Name); f.Accept(); }),
            ("another continent's closer connection is never borrowed", f =>
            { var n = f.Shadow(); n.Continent = 2; n.Connect(f.Destination.Name); f.Accept(); }),
            ("invalid closer geometry does not poison a valid alternative", f =>
            { var n = f.Shadow(); n.Location = WoWPoint.Empty; n.Connect(f.Destination.Name); f.Accept(); }),
            ("null cached nodes do not poison a valid alternative", f => { FlightPaths.XmlNodes.Insert(0, null!); f.Accept(); }),
            ("self links do not suppress a valid endpoint from the same origin", f => { f.Origin.Connect(f.Origin.Name); f.Accept(); }),
            ("the read-only query can find an alternative without publishing intent", f =>
            { f.Shadow(); Check(FlightPaths.HasKnownConnection(From, To), "read-only alternative was hidden"); f.Unchanged(); }),
            ("no connected alternative preserves the previous intent", f => { f.Shadow(); f.Origin.Connections.Clear(); f.Reject(); }),
            ("disabled flight setting remains a publication veto with an alternative", f =>
            { f.Shadow(); f.Settings.UseFlightPaths = false; f.Reject(); }),
            ("invalid destination evidence remains a veto across all origins", f =>
            { var n = f.Shadow(); n.Connect(f.Destination.Name); f.Destination.Location = WoWPoint.Empty; f.Reject(); }),
            ("candidate order is by distance rather than input insertion order", f =>
            { f.Shadow(); FlightPaths.XmlNodes.Reverse(); f.Accept(); }),
        };
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { using var fixture = new Fixture(); item.Test(fixture); passed++; Console.WriteLine("PASS flight-path alternate origin: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL flight-path alternate origin assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR flight-path alternate origin fixture/owner: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Flight-path alternate-origin scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual cached selection/publication; not native reachability or optimal-route proof.");
        if (assertions + unexpected != 0) throw new InvalidOperationException($"Flight-path alternate-origin regressions: assertions={assertions}; unexpected={unexpected}");
    }

    private sealed class Fixture : IDisposable
    {
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly object actual;
        private readonly XmlFlightNode previousFrom, previousTo;
        private readonly FlightPathReason previousReason;
        private readonly bool previousNeed;
        private readonly BotPoi previousPoi;
        internal Fixture()
        {
            actual = Activator.CreateInstance(typeof(FlightPathAdmissionRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
            previousFrom = FlightPaths.TakingPathFrom; previousTo = FlightPaths.TakingPathTo;
            previousReason = FlightPaths.Reason; previousNeed = FlightPaths.NeedFlightPath; previousPoi = BotPoi.Current;
        }
        internal XmlFlightNode Origin => (XmlFlightNode)actual.GetType().GetField("Origin", All)!.GetValue(actual)!;
        internal XmlFlightNode Destination => (XmlFlightNode)actual.GetType().GetField("Destination", All)!.GetValue(actual)!;
        internal CharacterSettings Settings => (CharacterSettings)actual.GetType().GetField("Settings", All)!.GetValue(actual)!;
        internal XmlFlightNode Shadow()
        {
            var node = new XmlFlightNode(50, 20, "audit-nearest-shadow", 1, new WoWPoint(1, 0, 0));
            FlightPaths.XmlNodes.Insert(0, node); return node;
        }
        internal void Accept()
        {
            Check(FlightPaths.SetFlightPathUsage(From, To, out var start, out var end), "usable cached connection was hidden by another origin");
            Check(ReferenceEquals(FlightPaths.TakingPathFrom, Origin) && ReferenceEquals(FlightPaths.TakingPathTo, Destination)
                && start == Origin.Location && end == Destination.Location, "incorrect endpoints or XYZ output were published");
            Check(FlightPaths.NeedFlightPath && FlightPaths.Reason == FlightPathReason.Use && BotPoi.Current.Type == PoiType.Fly
                && BotPoi.Current.Entry == Origin.MasterEntry && BotPoi.Current.Location == Origin.Location, "flight intent does not match selected origin");
        }
        internal void Reject()
        {
            Check(!FlightPaths.SetFlightPathUsage(From, To, out var start, out var end), "unusable alternative was published");
            Check(float.IsNaN(start.X) && float.IsNaN(start.Y) && float.IsNaN(start.Z)
                && float.IsNaN(end.X) && float.IsNaN(end.Y) && float.IsNaN(end.Z), "rejected query returned actionable coordinates");
            Unchanged();
        }
        internal void Unchanged() => Check(ReferenceEquals(FlightPaths.TakingPathFrom, previousFrom)
            && ReferenceEquals(FlightPaths.TakingPathTo, previousTo) && FlightPaths.Reason == previousReason
            && FlightPaths.NeedFlightPath == previousNeed && ReferenceEquals(BotPoi.Current, previousPoi), "read-only/rejected query mutated prior intent");
        public void Dispose() => ((IDisposable)actual).Dispose();
    }
    private static void Check(bool condition, string message) { if (!condition) throw new AssertionFailure(message); }
}
