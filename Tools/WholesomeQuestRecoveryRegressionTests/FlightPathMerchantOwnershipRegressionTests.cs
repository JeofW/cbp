using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

// Actual cached merchant, public flight setter and blacklist owners. Only the
// feasibility result/callback is controlled. Descriptor mutations explicitly
// refresh their managed cache; these tests do not establish native frame identity.
internal static class FlightPathMerchantOwnershipRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticHidden = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly WoWPoint From = new(0, 0, 0), To = new(1100, 0, 0);
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string text) : base(text) { } }
    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Flight merchant ownership tests require Windows x86.");
        var cases = new List<(string Name, Action<Fixture> Test)>
        {
            ("unchanged reachable merchant publishes once", f => f.Stable(true)),
            ("unchanged unreachable merchant is blacklisted without publishing", f => f.Stable(false)),
            ("observed merchant movement invalidates old reachability", f => f.Reject(true, f.MoveMerchant)),
            ("observed merchant entry change invalidates old reachability", f => f.Reject(true, f.ChangeEntry)),
            ("loss of flight-master flag invalidates old reachability", f => f.Reject(true, f.RemoveFlightFlag)),
            ("merchant removed from registry cannot authorize Update", f => f.Reject(true, () => f.RegistryChange(false))),
            ("same-address registry replacement cannot inherit merchant admission", f => f.Reject(true, () => f.RegistryChange(true))),
            ("Reset from an unsuccessful provider prevents stale blacklisting", f => f.Reject(false, FlightPaths.Reset)),
            ("nested publication from unsuccessful provider is retained without blacklist", f => f.Reject(false, f.PublishReplacement)),
            ("disabled settings after failed feasibility prevent stale blacklist", f => f.Reject(false, () => f.Settings.UseFlightPaths = false)),
            ("missing player after failed feasibility prevents stale blacklist", f => f.Reject(false, () => ObjectManager.Me = null)),
            ("provider replacement invalidates its failed feasibility", f => f.Reject(false, () => Navigator.NavigationProvider = new Probe())),
            ("network mutation after failed feasibility prevents stale blacklist", f => f.Reject(false, () => f.Origin.Connections.Clear())),
            ("Reset from the flight diagnostic prevents stale blacklist", f => f.LoggingReplacement(false, FlightPaths.Reset)),
            ("nested publication from flight diagnostic prevents stale blacklist", f => f.LoggingReplacement(false, f.PublishReplacement)),
            ("Reset from the actual blacklist diagnostic prevents its mutation", f => f.LoggingReplacement(true, FlightPaths.Reset)),
            ("nested publication from blacklist diagnostic prevents its mutation", f => f.LoggingReplacement(true, f.PublishReplacement)),
        };
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { using var fixture = new Fixture(); item.Test(fixture); passed++; Console.WriteLine("PASS flight merchant ownership: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL flight merchant ownership assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR flight merchant ownership fixture/owner: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Flight merchant-ownership scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual descriptors, registry, setter and blacklist; controlled feasibility; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException($"Flight merchant-ownership regressions: assertions={assertions}; unexpected={unexpected}");
    }
    private sealed class Probe : NavigationProvider
    {
        internal int Calls, Callbacks;
        internal bool Reachable = true;
        internal Action? BeforeReturn;
        public override float PathPrecision { get; set; } = 5;
        public override bool CanNavigateFully(WoWPoint from, WoWPoint to)
        {
            Calls++;
            var callback = BeforeReturn; BeforeReturn = null;
            if (callback != null) { Callbacks++; callback(); }
            return Reachable;
        }
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
        private readonly Probe provider = new();
        private readonly LocalPlayer player;
        private readonly WoWUnit merchant;
        private readonly ulong guid;
        private readonly Dictionary<ulong, DateTime> blacklist;
        private readonly bool hadBlacklist;
        private readonly DateTime priorBlacklist;
        internal XmlFlightNode Origin => (XmlFlightNode)Get(source, "Origin");
        internal CharacterSettings Settings => (CharacterSettings)Get(source, "Settings");
        internal Fixture()
        {
            source = Activator.CreateInstance(typeof(FlightPathUpdateProviderRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
            player = ObjectManager.Me; merchant = (WoWUnit)Get(source, "merchant"); guid = merchant.Guid;
            blacklist = (Dictionary<ulong, DateTime>)typeof(Blacklist).GetField("_blacklistedGuids", StaticHidden)!.GetValue(null)!;
            hadBlacklist = blacklist.TryGetValue(guid, out priorBlacklist);
            try
            {
                if (typeof(Navigator).GetField("_meshNavigator", StaticHidden)!.GetValue(null) != null)
                    throw new InvalidOperationException("Offline Reset requires no mesh navigator");
                Origin.MasterEntry = 0; Navigator.NavigationProvider = provider;
            }
            catch { Dispose(); throw; }
        }
        internal void Stable(bool reachable)
        {
            provider.Reachable = reachable; var retained = new Intent();
            bool result = FlightPaths.SetFlightPathUsage(From, To, out var start, out var end);
            Check(provider.Calls == 1 && provider.Callbacks == 0, "control did not use one actual provider observation");
            if (reachable)
                Check(result && start == Origin.Location && !Empty(end) && FlightPaths.Reason == FlightPathReason.Update
                    && BotPoi.Current.Guid == guid && !Blacklist.Contains(guid), "reachable control failed");
            else
                Check(!result && Empty(start) && Empty(end) && retained.Retained && Blacklist.Contains(guid), "ordinary unreachable control changed intent or skipped blacklist");
        }
        internal void Reject(bool reachable, Action change)
        {
            provider.Reachable = reachable; Intent? replacement = null; Exception? callbackError = null;
            provider.BeforeReturn = () => { try { change(); replacement = new Intent(); } catch (Exception e) { callbackError = e; throw; } };
            InvokeAndCheck(() => replacement, () => callbackError, () => provider.Callbacks);
        }
        private void InvokeAndCheck(Func<Intent?> replacement, Func<Exception?> callbackError, Func<int> callbacks)
        {
            bool result = false; Exception? observed = null; var start = WoWPoint.Empty; var end = WoWPoint.Empty;
            try { result = FlightPaths.SetFlightPathUsage(From, To, out start, out end); } catch (Exception e) { observed = e; }
            if (callbackError() is Exception failure) ExceptionDispatchInfo.Capture(failure).Throw();
            Check(callbacks() == 1 && provider.Calls == 1 && replacement() != null, "actual mutation boundary not reached exactly once");
            Check(observed == null && !result && Empty(start) && Empty(end) && replacement()!.Retained && !Blacklist.Contains(guid),
                $"denied={!result}; retained={replacement()!.Retained}; blacklist={Blacklist.Contains(guid)}; owner-error={observed?.GetType().Name ?? "none"}");
        }
        internal void LoggingReplacement(bool blacklistLog, Action change)
        {
            provider.Reachable = false; Intent? replacement = null; Exception? callbackError = null; int calls = 0;
            Action<LogLevel, string>? handler = null;
            handler = (_, message) =>
            {
                if (!message.Contains("Blacklisting ", StringComparison.Ordinal)
                    || (blacklistLog ? !message.Contains(guid.ToString("X16"), StringComparison.Ordinal) : !message.Contains("for 5 minutes", StringComparison.Ordinal))) return;
                Logging.OnMessageLogged -= handler; calls++;
                try { change(); replacement = new Intent(); } catch (Exception e) { callbackError = e; throw; }
            };
            Logging.OnMessageLogged += handler;
            try { InvokeAndCheck(() => replacement, () => callbackError, () => calls); }
            finally { Logging.OnMessageLogged -= handler; }
        }
        internal void MoveMerchant()
        {
            var point = new WoWPoint(80, 90, 100);
            Marshal.StructureToPtr(point, new IntPtr(unchecked((int)(merchant.BaseAddress + 1944))), false);
            EvictRead(merchant.BaseAddress + 1944);
            typeof(WoWUnit).GetField("_cachedLocation", Hidden)!.SetValue(merchant, null);
            CheckPlayerContext();
            if (merchant.Location != point) throw new InvalidOperationException("Merchant movement was not observed");
        }
        internal void ChangeEntry()
        {
            uint descriptor = merchant.BaseAddress + 4096;
            Marshal.WriteInt32(new IntPtr(unchecked((int)(descriptor + 12))), 54322);
            EvictRead(descriptor + 12);
            typeof(WoWObject).GetField("_cachedEntry", Hidden)!.SetValue(merchant, 0U);
            CheckPlayerContext();
            if (merchant.Entry != 54322) throw new InvalidOperationException("Merchant entry mutation was not observed");
        }
        internal void RemoveFlightFlag()
        {
            uint offset = (uint)typeof(FlightPathUpdateProviderRegressionTests).GetMethod("Field", StaticHidden)!.Invoke(null, new object[] { "NpcFlags" })!;
            Marshal.WriteInt32(new IntPtr(unchecked((int)(merchant.BaseAddress + 4096 + offset))), 0);
            EvictRead(merchant.BaseAddress + 4096 + offset);
            CheckPlayerContext();
            if (merchant.IsFlightMaster) throw new InvalidOperationException("Merchant flag mutation was not observed");
        }
        private void CheckPlayerContext()
        {
            if (!ReferenceEquals(ObjectManager.Me, player) || !player.IsValid || player.MapId != 1 || !Styx.StyxWoW.IsInWorld)
                throw new InvalidOperationException("Merchant mutation disturbed the retained player/world fixture");
        }
        private static void EvictRead(uint address)
        {
            // Evict only the changed test-process address. Clearing the whole
            // Memory cache would erase controlled client-global fixture bytes.
            var cache = (System.Threading.ThreadLocal<Dictionary<IntPtr, byte[]>>)ObjectManager.Wow!.GetType().GetField("_cache", Hidden)!.GetValue(ObjectManager.Wow)!;
            cache.Value!.Remove(new IntPtr(unchecked((int)address)));
        }
        internal void RegistryChange(bool replace)
        {
            var registry = (Dictionary<ulong, WoWObject>)typeof(ObjectManager).GetField("_objectList", StaticHidden)!.GetValue(null)!;
            var sync = typeof(ObjectManager).GetField("_updateLock", StaticHidden)!.GetValue(null)!;
            lock (sync) { if (replace) registry[guid] = new WoWUnit(merchant.BaseAddress); else registry.Remove(guid); }
            typeof(ObjectManager).GetMethod("ResetCaches", StaticHidden)!.Invoke(null, null);
            if (ReferenceEquals(ObjectManager.GetObjectByGuid<WoWObject>(guid), merchant)) throw new InvalidOperationException("Registry mutation failed");
        }
        internal void PublishReplacement()
        {
            var origin = new XmlFlightNode(90, 20, "audit-merchant-replacement-origin", 1, new WoWPoint(3, 4, 5));
            var destination = new XmlFlightNode(91, 20, "audit-merchant-replacement-destination", 1, new WoWPoint(1050, 4, 5));
            origin.Connect(destination.Name); FlightPaths.XmlNodes = new List<XmlFlightNode> { origin, destination };
            if (!FlightPaths.SetFlightPathUsage(From, To, out _, out _) || !ReferenceEquals(FlightPaths.TakingPathFrom, origin))
                throw new InvalidOperationException("Genuine replacement publication failed");
        }
        public void Dispose()
        {
            ObjectManager.Me = player;
            if (hadBlacklist) blacklist[guid] = priorBlacklist; else blacklist.Remove(guid);
            ((IDisposable)source).Dispose();
        }
    }
    private static object Get(object source, string name) => source.GetType().GetField(name, Hidden)?.GetValue(source)
        ?? source.GetType().GetProperty(name, Hidden)?.GetValue(source) ?? throw new InvalidOperationException("Missing retained fixture member: " + name);
    private static bool Empty(WoWPoint p) => float.IsNaN(p.X) && float.IsNaN(p.Y) && float.IsNaN(p.Z);
    private static void Check(bool condition, string message) { if (!condition) throw new AssertionFailure(message); }
}
