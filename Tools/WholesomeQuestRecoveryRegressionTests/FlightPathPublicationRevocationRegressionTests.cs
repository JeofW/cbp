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

// Actual flight publication and synchronous POI logging. Retained test-process
// descriptors/registry provide observations; no native dispatch or game attach.
internal static class FlightPathPublicationRevocationRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly WoWPoint From = new(0, 0, 0), To = new(1100, 0, 0);
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string text) : base(text) { } }
    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Flight publication revocation tests require Windows x86.");
        var cases = new List<(string Name, Action<Fixture> Test)>
        {
            ("unchanged known publication retains its endpoints", f => f.Stable(true)),
            ("unchanged merchant Update retains its endpoints", f => f.Stable(false)),
            ("disabled setting from the POI log revokes the owned flight", f => f.Revoke(() => f.Settings.UseFlightPaths = false)),
            ("missing player from the POI log revokes the owned flight", f => f.Revoke(() => ObjectManager.Me = null)),
            ("provider replacement from the POI log revokes the owned flight", f => f.Revoke(() => Navigator.NavigationProvider = new Probe())),
            ("network replacement from the POI log revokes the owned flight", f => f.Revoke(() => FlightPaths.XmlNodes = new List<XmlFlightNode>(FlightPaths.XmlNodes))),
            ("network removal from the POI log revokes the owned flight", f => f.Revoke(() => FlightPaths.XmlNodes.Clear())),
            ("connection mutation from the POI log revokes the owned flight", f => f.Revoke(() => f.Origin.Connections.Clear())),
            ("destination movement from the POI log revokes the owned flight", f => f.Revoke(() => f.Destination.Location = new WoWPoint(1050, 12, 34))),
            ("merchant movement from the POI log revokes the owned flight", f => f.Revoke(() => f.Mutate("MoveMerchant"))),
            ("merchant entry mutation from the POI log revokes the owned flight", f => f.Revoke(() => f.Mutate("ChangeEntry"))),
            ("merchant flag loss from the POI log revokes the owned flight", f => f.Revoke(() => f.Mutate("RemoveFlightFlag"))),
            ("merchant registry removal from the POI log revokes the owned flight", f => f.Revoke(() => f.Mutate("RegistryChange", false))),
            ("merchant registry replacement from the POI log revokes the owned flight", f => f.Revoke(() => f.Mutate("RegistryChange", true))),
            ("known flight also revokes after setting invalidation", f => { f.Origin.MasterEntry = 90; f.Revoke(() => f.Settings.UseFlightPaths = false); }),
            ("known flight also revokes after endpoint mutation", f => { f.Origin.MasterEntry = 90; f.Revoke(() => f.Destination.Location = new WoWPoint(1050, 12, 34)); }),
            ("nested publication in the POI log remains owned by its replacement", f => f.Preserve(f.PublishReplacement)),
            ("Reset in the POI log remains authoritative", f => f.Preserve(FlightPaths.Reset)),
            ("service replacement in the POI log remains untouched", f => f.Preserve(() => BotPoi.Current = new BotPoi(new WoWPoint(7, 8, 9), PoiType.Repair))),
            ("revocation detaches old endpoints before a cleanup-log nested flight", f => f.CleanupReplacement(true)),
            ("revocation cannot clear cleanup-log replacement service work", f => f.CleanupReplacement(false)),
        };
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { using var fixture = new Fixture(); item.Test(fixture); passed++; Console.WriteLine("PASS flight publication revocation: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL flight publication revocation assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR flight publication revocation fixture/owner: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Flight publication-revocation scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual publication and POI callbacks; test-process merchant; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException($"Flight publication-revocation regressions: assertions={assertions}; unexpected={unexpected}");
    }
    private sealed class Probe : NavigationProvider
    {
        public override float PathPrecision { get; set; } = 5;
        public override bool CanNavigateFully(WoWPoint from, WoWPoint to) => true;
        public override MoveResult MoveTo(WoWPoint point) => throw new InvalidOperationException("Unexpected movement");
        public override WoWPoint[] GeneratePath(WoWPoint from, WoWPoint to) => throw new InvalidOperationException("Unexpected path generation");
        public override bool AtLocation(WoWPoint a, WoWPoint b) => a == b;
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
        private readonly LocalPlayer player;
        private readonly bool previousNeed;
        internal readonly XmlFlightNode Origin, Destination;
        internal CharacterSettings Settings => (CharacterSettings)Get(source, "Settings");
        internal Fixture()
        {
            source = Activator.CreateInstance(typeof(FlightPathMerchantOwnershipRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
            player = ObjectManager.Me; previousNeed = FlightPaths.NeedFlightPath;
            try
            {
                Origin = (XmlFlightNode)Get(source, "Origin");
                Destination = FlightPaths.XmlNodes.Find(n => Origin.Connections.Contains(n.Name))
                    ?? throw new InvalidOperationException("Missing retained connected destination");
            }
            catch { Dispose(); throw; }
        }
        internal void Stable(bool known)
        {
            if (known) Origin.MasterEntry = 90;
            Check(FlightPaths.SetFlightPathUsage(From, To, out var start, out var end)
                && start == Origin.Location && end == Destination.Location
                && ReferenceEquals(FlightPaths.TakingPathFrom, Origin) && ReferenceEquals(FlightPaths.TakingPathTo, Destination)
                && FlightPaths.Reason == (known ? FlightPathReason.Use : FlightPathReason.Update)
                && BotPoi.Current.Type == PoiType.Fly && FlightPaths.NeedFlightPath == previousNeed,
                "ordinary completed publication was revoked or changed");
        }
        internal void Revoke(Action change) => Observe(change, false);
        internal void Preserve(Action change) => Observe(change, true);
        private void Observe(Action change, bool preserve)
        {
            int callbacks = 0; Exception? callbackError = null; Intent? replacement = null;
            Action<LogLevel, string>? handler = null;
            handler = (_, message) =>
            {
                if (!message.Contains("Changed POI to:", StringComparison.Ordinal) || BotPoi.Current.Type != PoiType.Fly) return;
                Logging.OnMessageLogged -= handler; callbacks++;
                try { change(); replacement = new Intent(); } catch (Exception error) { callbackError = error; throw; }
            };
            Logging.OnMessageLogged += handler;
            bool result = false; Exception? observed = null; var start = WoWPoint.Empty; var end = WoWPoint.Empty;
            try { result = FlightPaths.SetFlightPathUsage(From, To, out start, out end); } catch (Exception error) { observed = error; }
            finally { Logging.OnMessageLogged -= handler; }
            if (callbackError != null) ExceptionDispatchInfo.Capture(callbackError).Throw();
            Check(callbacks == 1 && replacement != null, "actual publication callback not reached exactly once");
            Check(observed == null && !result && Empty(start) && Empty(end), "invalidated publication still succeeded or threw: " + observed?.GetType().Name);
            if (preserve) Check(replacement!.Retained, "obsolete caller overwrote the replacement");
            else Check(Revoked && FlightPaths.NeedFlightPath == previousNeed, "invalid owned flight remains published after rejection");
        }
        internal void CleanupReplacement(bool flight)
        {
            int publications = 0, cleanups = 0; bool detached = false;
            Exception? callbackError = null; Intent? replacement = null;
            Action<LogLevel, string>? publishHandler = null, cleanupHandler = null;
            publishHandler = (_, message) =>
            {
                if (!message.Contains("Changed POI to:", StringComparison.Ordinal) || BotPoi.Current.Type != PoiType.Fly) return;
                Logging.OnMessageLogged -= publishHandler; publications++; Settings.UseFlightPaths = false;
            };
            cleanupHandler = (_, message) =>
            {
                if (publications != 1 || !message.Contains("Cleared POI", StringComparison.Ordinal) || BotPoi.Current.Type != PoiType.None) return;
                Logging.OnMessageLogged -= cleanupHandler; cleanups++; detached = Revoked;
                try
                {
                    if (flight) PublishReplacement();
                    else BotPoi.Current = new BotPoi(new WoWPoint(7, 8, 9), PoiType.Repair);
                    replacement = new Intent();
                }
                catch (Exception error) { callbackError = error; throw; }
            };
            Logging.OnMessageLogged += publishHandler; Logging.OnMessageLogged += cleanupHandler;
            bool result = false; Exception? observed = null; var start = WoWPoint.Empty; var end = WoWPoint.Empty;
            try { result = FlightPaths.SetFlightPathUsage(From, To, out start, out end); } catch (Exception error) { observed = error; }
            finally { Logging.OnMessageLogged -= publishHandler; Logging.OnMessageLogged -= cleanupHandler; }
            if (callbackError != null) ExceptionDispatchInfo.Capture(callbackError).Throw();
            Check(publications == 1 && cleanups == 1 && detached && replacement != null, "owned revocation/cleanup callback was not reached after detaching endpoints");
            Check(observed == null && !result && Empty(start) && Empty(end) && replacement!.Retained,
                "revocation resumed after the cleanup callback and damaged replacement work");
        }
        private static bool Revoked => FlightPaths.TakingPathFrom == null && FlightPaths.TakingPathTo == null
            && FlightPaths.Reason == FlightPathReason.None && BotPoi.Current.Type == PoiType.None;
        internal void Mutate(string name, params object[] args)
        {
            try { source.GetType().GetMethod(name, Hidden)!.Invoke(source, args); }
            catch (TargetInvocationException error) when (error.InnerException != null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); }
        }
        internal void PublishReplacement()
        {
            Settings.UseFlightPaths = true; ObjectManager.Me = player;
            Mutate("PublishReplacement");
        }
        public void Dispose() { ObjectManager.Me = player; ((IDisposable)source).Dispose(); }
    }
    private static object Get(object source, string name) => source.GetType().GetField(name, Hidden)?.GetValue(source)
        ?? source.GetType().GetProperty(name, Hidden)?.GetValue(source) ?? throw new InvalidOperationException("Missing retained fixture member: " + name);
    private static bool Empty(WoWPoint p) => float.IsNaN(p.X) && float.IsNaN(p.Y) && float.IsNaN(p.Z);
    private static void Check(bool condition, string message) { if (!condition) throw new AssertionFailure(message); }
}
