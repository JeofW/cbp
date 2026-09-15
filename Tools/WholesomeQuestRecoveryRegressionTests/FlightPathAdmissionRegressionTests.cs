using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

// Actual cached-flight admission and POI publication, using the existing
// test-process player/memory fixture. No route generation, mesh, taxi dispatch,
// Lua, installed-client file or original-client acceptance is exercised.
internal static class FlightPathAdmissionRegressionTests
{
    private static readonly WoWPoint From = new(0, 0, 0), To = new(1100, 0, 0);
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string text) : base(text) { } }
    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Flight admission tests require Windows x86.");
        var cases = new List<(string Name, Action<Fixture> Test)>
        {
            ("missing network rejects without replacing prior intent", f => { FlightPaths.XmlNodes = null!; f.Reject(); }),
            ("empty network rejects without replacing prior intent", f => { FlightPaths.XmlNodes.Clear(); f.Reject(); }),
            ("other-continent network rejects without replacing prior intent", f => { foreach (var n in FlightPaths.XmlNodes) n.Continent = 2; f.Reject(); }),
            ("disconnected origin rejects without replacing prior intent", f => { f.Origin.Connections.Clear(); f.Reject(); }),
            ("connection to an absent destination rejects prior-intent replacement", f => { f.Origin.Connections.Clear(); f.Origin.Connect("missing"); f.Reject(); }),
            ("backwards cached connection retains rejection and prior intent", f => { f.Destination.Location = new WoWPoint(-1000, 0, 0); f.Reject(); }),
            ("self-only cached connection does not publish a flight", f => { f.Origin.Connections.Clear(); f.Origin.Connect(f.Origin.Name); f.Reject(); }),
            ("valid directed cached connection publishes both endpoints and POI", f => f.Accept()),
            ("disabled flight setting preserves prior intent", f => { f.Settings.UseFlightPaths = false; f.Reject(); }),
            ("missing current player with known nodes rejects without an exception", f => { ObjectManager.Me = null; f.Reject(); }),
            ("unknown starting coordinate cannot publish cached work", f => f.Reject(WoWPoint.Empty, To)),
            ("unknown destination coordinate cannot publish cached work", f => f.Reject(From, WoWPoint.Empty)),
            ("infinite query coordinate cannot publish cached work", f => f.Reject(new WoWPoint(float.PositiveInfinity, 0, 0), To)),
            ("unknown origin elevation cannot publish cached work", f => { f.Origin.Location = new WoWPoint(10, 10, float.NaN); f.Reject(); }),
            ("unknown destination elevation cannot publish cached work", f => { f.Destination.Location = new WoWPoint(1000, 10, float.NaN); f.Reject(); }),
            ("infinite destination node cannot publish cached work", f => { f.Destination.Location = new WoWPoint(float.PositiveInfinity, 0, 0); f.Reject(); }),
            ("null-only cached node observation rejects without an exception", f => { FlightPaths.XmlNodes = new List<XmlFlightNode> { null! }; f.Reject(); }),
            ("unknown master without a nearby provider cannot overwrite prior intent", f =>
            { Check(ObjectManager.CachedUnits.Count == 0, "fixture requires an observed empty provider set"); f.Origin.MasterEntry = 0; f.Reject(); }),
            ("known-connection query preserves all publication state", f => { Check(FlightPaths.HasKnownConnection(From, To), "valid read-only control failed"); f.Unchanged(); }),
            ("known-connection query rejects invalid node geometry", f => { f.Destination.Location = WoWPoint.Empty; f.ReadOnlyReject(); }),
            ("known-connection query rejects invalid query geometry", f => f.ReadOnlyReject(From, WoWPoint.Empty)),
            ("known-connection query rejects missing player without mutation", f => { ObjectManager.Me = null; f.ReadOnlyReject(); }),
        };
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { using var fixture = new Fixture(); item.Test(fixture); passed++; Console.WriteLine("PASS flight-path admission: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL flight-path admission assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR flight-path admission fixture/owner: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Flight-path admission scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual cached connection and publication; no navigation/taxi dispatch or game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException($"Flight-path admission regressions: assertions={assertions}; unexpected={unexpected}");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly object world;
        private readonly CharacterSettings? previousSettings = CharacterSettings.Instance;
        private readonly List<XmlFlightNode>? previousNodes = FlightPaths.XmlNodes;
        private readonly XmlFlightNode? previousFrom = FlightPaths.TakingPathFrom, previousTo = FlightPaths.TakingPathTo;
        private readonly FlightPathReason previousReason = FlightPaths.Reason;
        private readonly bool previousNeed = FlightPaths.NeedFlightPath;
        private readonly BotPoi previousPoi = BotPoi.Current;
        internal readonly CharacterSettings Settings = (CharacterSettings)RuntimeHelpers.GetUninitializedObject(typeof(CharacterSettings));
        internal readonly XmlFlightNode Origin = new(42, 20, "audit-origin", 1, new WoWPoint(10, 10, 25));
        internal readonly XmlFlightNode Destination = new(43, 20, "audit-destination", 1, new WoWPoint(1000, 10, 300));
        private readonly XmlFlightNode retainedFrom = new("retained-origin", 1, new WoWPoint(-500, 0, 0));
        private readonly XmlFlightNode retainedTo = new("retained-destination", 1, new WoWPoint(-1000, 0, 0));
        private readonly BotPoi retainedPoi = new(new WoWPoint(-400, 0, 10), PoiType.Sell);
        internal Fixture()
        {
            world = Activator.CreateInstance(typeof(QuestPublicationRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
            try
            {
                Check(ObjectManager.Me != null && ObjectManager.Me.MapId == 1, "real player/map fixture is unavailable");
                typeof(CharacterSettings).GetProperty("Instance")!.SetValue(null, Settings);
                Settings.UseFlightPaths = true; Check(FlightPaths.CanTakeFlightPaths, "actual setting did not enable flights");
                Origin.Connect(Destination.Name); FlightPaths.XmlNodes = new List<XmlFlightNode> { Origin, Destination };
                FlightPaths.TakingPathFrom = retainedFrom; FlightPaths.TakingPathTo = retainedTo;
                FlightPaths.Reason = FlightPathReason.Use; FlightPaths.NeedFlightPath = true; BotPoi.Current = retainedPoi;
            }
            catch { Dispose(); throw; }
        }
        internal void Reject() => Reject(From, To);
        internal void Reject(WoWPoint from, WoWPoint to)
        {
            bool result = false; Exception? error = null; WoWPoint start = WoWPoint.Empty, end = WoWPoint.Empty;
            try { result = FlightPaths.SetFlightPathUsage(from, to, out start, out end); }
            catch (NullReferenceException caught) { error = caught; }
            bool empty = Empty(start) && Empty(end), retained = Retained();
            Check(error == null && !result && empty && retained,
                $"rejected={!result}; empty={empty}; prior-intent-retained={retained}; owner-error={error?.GetType().Name ?? "none"}");
        }
        internal void ReadOnlyReject() => ReadOnlyReject(From, To);
        internal void ReadOnlyReject(WoWPoint from, WoWPoint to)
        {
            bool result = FlightPaths.HasKnownConnection(from, to); bool retained = Retained();
            Check(!result && retained, $"read-only rejected={!result}; prior-intent-retained={retained}");
        }
        internal void Accept()
        {
            Check(FlightPaths.SetFlightPathUsage(From, To, out var start, out var end), "valid cached connection was rejected");
            Check(ReferenceEquals(FlightPaths.TakingPathFrom, Origin) && ReferenceEquals(FlightPaths.TakingPathTo, Destination)
                && start == Origin.Location && end == Destination.Location && FlightPaths.Reason == FlightPathReason.Use,
                "successful cached publication changed node identity or XYZ output");
            Check(BotPoi.Current.Type == PoiType.Fly && BotPoi.Current.Entry == Origin.MasterEntry
                && BotPoi.Current.Location == Origin.Location && FlightPaths.NeedFlightPath, "successful flight POI control failed");
        }
        private bool Retained() => ReferenceEquals(FlightPaths.TakingPathFrom, retainedFrom) && ReferenceEquals(FlightPaths.TakingPathTo, retainedTo)
            && FlightPaths.Reason == FlightPathReason.Use && FlightPaths.NeedFlightPath && ReferenceEquals(BotPoi.Current, retainedPoi);
        internal void Unchanged() => Check(Retained(), "read-only operation changed published intent");
        public void Dispose()
        {
            try
            {
                FlightPaths.XmlNodes = previousNodes!; FlightPaths.TakingPathFrom = previousFrom!; FlightPaths.TakingPathTo = previousTo!;
                FlightPaths.Reason = previousReason; FlightPaths.NeedFlightPath = previousNeed; BotPoi.Current = previousPoi;
                typeof(CharacterSettings).GetProperty("Instance")!.SetValue(null, previousSettings);
            }
            finally { ((IDisposable)world).Dispose(); }
        }
    }
    private static bool Empty(WoWPoint point) => float.IsNaN(point.X) && float.IsNaN(point.Y) && float.IsNaN(point.Z);
    private static void Check(bool condition, string text) { if (!condition) throw new AssertionFailure(text); }
}
