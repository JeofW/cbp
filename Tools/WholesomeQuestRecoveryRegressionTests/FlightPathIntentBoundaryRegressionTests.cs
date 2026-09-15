using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

// Real flight selection/POI publication, reusing the descriptor-backed merchant
// fixture. The only controlled effect boundary is navigation-provider feasibility.
internal static class FlightPathIntentBoundaryRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticHidden = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly WoWPoint From = new(0, 0, 0), To = new(1100, 0, 0);
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string text) : base(text) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Flight intent-boundary tests require Windows x86.");
        var cases = new List<(string Name, Action<Fixture> Test)>
        {
            ("unchanged known origin publishes without consulting a provider", f => f.Stable(false)),
            ("unchanged unknown origin retains real merchant Update publication", f => f.Stable(true)),
            ("provider disabling flights prevents an obsolete publication", f => f.Reject(() => f.Settings.UseFlightPaths = false)),
            ("replacement settings instance cannot inherit old admission", f => f.Reject(() =>
            { var replacement = (CharacterSettings)RuntimeHelpers.GetUninitializedObject(typeof(CharacterSettings)); replacement.UseFlightPaths = true;
              typeof(CharacterSettings).GetProperty("Instance")!.SetValue(null, replacement); })),
            ("missing player after provider callback is denied without owner error", f => f.Reject(() => ObjectManager.Me = null)),
            ("same-address replacement player cannot inherit old admission", f => f.Reject(() => ObjectManager.Me = new LocalPlayer(f.Player.BaseAddress))),
            ("replacement network list cannot inherit old candidate selection", f => f.Reject(() => FlightPaths.XmlNodes = new List<XmlFlightNode>(FlightPaths.XmlNodes))),
            ("changed origin coordinates invalidate the captured candidate", f => f.Reject(() => f.Origin.Location = new WoWPoint(20, 30, 40))),
            ("changed origin master invalidates the captured Update candidate", f => f.Reject(() => f.Origin.MasterEntry = 77)),
            ("removed connection invalidates the captured candidate", f => f.Reject(() => f.Origin.Connections.Clear())),
            ("actual Reset during feasibility is not overwritten by old publication", f => f.Reject(() =>
            { if (typeof(Navigator).GetField("_meshNavigator", StaticHidden)!.GetValue(null) != null)
                  throw new InvalidOperationException("Offline Reset requires no mesh navigator");
              FlightPaths.Reset(); })),
            ("genuine nested known publication survives the obsolete outer call", f => f.Reject(f.PublishReplacement)),
            ("replacement service POI survives the obsolete outer call", f => f.Reject(() => BotPoi.Current = new BotPoi(new WoWPoint(7, 8, 9), PoiType.Repair))),
            ("invalidated selected merchant cannot authorize Update", f => f.Reject(() =>
                typeof(WoWObject).GetMethod("UpdateBaseAddress", Hidden)!.Invoke(f.Merchant, new object[] { 0U }))),
            ("ordinary provider failure propagates without changing intent", f => f.ProviderError()),
        };
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { using var fixture = new Fixture(); item.Test(fixture); passed++; Console.WriteLine("PASS flight intent boundary: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL flight intent boundary assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR flight intent boundary fixture/owner: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Flight intent-boundary scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual public owners and test-process merchant; controlled provider callbacks; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException($"Flight intent-boundary regressions: assertions={assertions}; unexpected={unexpected}");
    }

    private sealed class Probe : NavigationProvider
    {
        internal int Calls, Callbacks;
        internal Action? BeforeReturn;
        public override float PathPrecision { get; set; } = 5;
        public override bool CanNavigateFully(WoWPoint from, WoWPoint to)
        {
            Calls++;
            var callback = BeforeReturn; BeforeReturn = null;
            if (callback != null) { Callbacks++; callback(); }
            return true;
        }
        public override MoveResult MoveTo(WoWPoint point) => throw new InvalidOperationException("Unexpected movement");
        public override WoWPoint[] GeneratePath(WoWPoint from, WoWPoint to) => throw new InvalidOperationException("Unexpected path generation");
        public override bool AtLocation(WoWPoint first, WoWPoint second) => first == second;
    }
    private sealed class Intent
    {
        private readonly XmlFlightNode from = FlightPaths.TakingPathFrom, to = FlightPaths.TakingPathTo;
        private readonly FlightPathReason reason = FlightPaths.Reason;
        private readonly bool need = FlightPaths.NeedFlightPath;
        private readonly BotPoi poi = BotPoi.Current;
        internal bool Retained => ReferenceEquals(from, FlightPaths.TakingPathFrom) && ReferenceEquals(to, FlightPaths.TakingPathTo)
            && reason == FlightPaths.Reason && need == FlightPaths.NeedFlightPath && ReferenceEquals(poi, BotPoi.Current);
    }
    private sealed class Fixture : IDisposable
    {
        private readonly object source;
        private readonly Probe probe = new();
        internal readonly LocalPlayer Player;
        internal XmlFlightNode Origin => (XmlFlightNode)Get(source, "Origin");
        private XmlFlightNode Destination => (XmlFlightNode)Get(source, "Destination");
        internal CharacterSettings Settings => (CharacterSettings)Get(source, "Settings");
        internal WoWUnit Merchant => (WoWUnit)Get(source, "merchant");
        internal Fixture()
        {
            source = Activator.CreateInstance(typeof(FlightPathUpdateProviderRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
            Player = ObjectManager.Me;
            try { Navigator.NavigationProvider = probe; }
            catch { ((IDisposable)source).Dispose(); throw; }
        }
        internal void Stable(bool update)
        {
            if (update) Origin.MasterEntry = 0;
            Check(FlightPaths.SetFlightPathUsage(From, To, out var start, out var end), "valid unchanged selection was rejected");
            Check(ReferenceEquals(FlightPaths.TakingPathFrom, Origin) && ReferenceEquals(FlightPaths.TakingPathTo, Destination)
                && start == Origin.Location && end == Destination.Location && FlightPaths.Reason == (update ? FlightPathReason.Update : FlightPathReason.Use),
                "unchanged publication lost its endpoint/reason identity");
            Check(BotPoi.Current.Type == PoiType.Fly && (update ? BotPoi.Current.Guid == Merchant.Guid && probe.Calls > 0
                : BotPoi.Current.Entry == Origin.MasterEntry && probe.Calls == 0), "unchanged publication selected the wrong provider/POI");
        }
        internal void Reject(Action change)
        {
            Origin.MasterEntry = 0;
            Intent? replacement = null; Exception? callbackError = null;
            probe.BeforeReturn = () =>
            {
                try { change(); replacement = new Intent(); }
                catch (Exception error) { callbackError = error; throw; }
            };
            bool result = false; Exception? observed = null; var start = WoWPoint.Empty; var end = WoWPoint.Empty;
            try { result = FlightPaths.SetFlightPathUsage(From, To, out start, out end); }
            catch (Exception error) { observed = error; }
            if (callbackError != null) ExceptionDispatchInfo.Capture(callbackError).Throw();
            Check(probe.Callbacks == 1 && probe.Calls > 0 && replacement != null, "mutation callback was not reached exactly once");
            Check(observed == null && !result && Empty(start) && Empty(end) && replacement!.Retained,
                $"denied={!result}; empty={Empty(start) && Empty(end)}; replacement-retained={replacement!.Retained}; owner-error={observed?.GetType().Name ?? "none"}");
        }
        internal void PublishReplacement()
        {
            var origin = new XmlFlightNode(90, 20, "audit-nested-origin", 1, new WoWPoint(3, 4, 5));
            var destination = new XmlFlightNode(91, 20, "audit-nested-destination", 1, new WoWPoint(1050, 4, 5));
            origin.Connect(destination.Name); FlightPaths.XmlNodes = new List<XmlFlightNode> { origin, destination };
            if (!FlightPaths.SetFlightPathUsage(From, To, out var start, out var end)
                || !ReferenceEquals(FlightPaths.TakingPathFrom, origin) || !ReferenceEquals(FlightPaths.TakingPathTo, destination)
                || start != origin.Location || end != destination.Location || FlightPaths.Reason != FlightPathReason.Use)
                throw new InvalidOperationException("Genuine nested publication setup failed");
        }
        internal void ProviderError()
        {
            Origin.MasterEntry = 0; var retained = new Intent(); var signal = new InvalidOperationException("ordinary provider error");
            probe.BeforeReturn = () => throw signal; Exception? observed = null;
            try { FlightPaths.SetFlightPathUsage(From, To, out _, out _); } catch (Exception error) { observed = error; }
            Check(ReferenceEquals(observed, signal) && probe.Callbacks == 1 && probe.Calls == 1 && retained.Retained,
                "ordinary provider error was swallowed/replaced or changed intent");
        }
        public void Dispose()
        {
            ObjectManager.Me = Player;
            ((IDisposable)source).Dispose();
        }
    }
    private static object Get(object source, string name) => source.GetType().GetField(name, Hidden)?.GetValue(source)
        ?? source.GetType().GetProperty(name, Hidden)?.GetValue(source) ?? throw new InvalidOperationException("Missing retained fixture member: " + name);
    private static bool Empty(WoWPoint point) => float.IsNaN(point.X) && float.IsNaN(point.Y) && float.IsNaN(point.Z);
    private static void Check(bool condition, string text) { if (!condition) throw new AssertionFailure(text); }
}
