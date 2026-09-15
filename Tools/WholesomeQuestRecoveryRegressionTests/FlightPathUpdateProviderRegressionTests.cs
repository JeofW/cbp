using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using Styx;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

// Real cached selection and merchant filtering, with a test-process WoWUnit.
// Only route feasibility is controlled. No native route, movement, taxi or Lua call.
internal static class FlightPathUpdateProviderRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags StaticHidden = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly WoWPoint From = new(0, 0, 0), To = new(1100, 0, 0);
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string text) : base(text) { } }
    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Flight provider tests require Windows x86.");
        var cases = new List<(string Name, Action<Fixture> Test)>
        {
            ("an unknown master retains the reachable merchant Update path", f => { f.Origin.MasterEntry = 0; f.Update(f.Origin); }),
            ("a nearer unknown origin retains Update even with a known alternative", f => f.Update(f.AddUnknown())),
            ("a known origin does not consult the merchant route provider", f => f.Known()),
            ("unknown-only read query remains false despite a nearby merchant", f => { f.Origin.MasterEntry = 0; f.Query(false); }),
            ("known-only read query never probes the merchant route provider", f => f.Query(true)),
            ("disabled flight selection does not consult the merchant provider", f => { f.Settings.UseFlightPaths = false; f.Reject(); }),
            ("merchant feasibility cancellation propagates without publishing", f => f.Cancel(new OperationCanceledException("provider stop"))),
            ("merchant feasibility interruption propagates without publishing", f => f.Cancel(new ThreadInterruptedException("provider stop"))),
        };
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { using var fixture = new Fixture(); item.Test(fixture); passed++; Console.WriteLine("PASS flight update provider: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL flight update provider assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR flight update provider fixture/owner: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Flight update-provider scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual merchant descriptors and public owners; controlled route feasibility; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException($"Flight update-provider regressions: assertions={assertions}; unexpected={unexpected}");
    }
    private sealed class ProbeNavigation : NavigationProvider
    {
        internal int Calls;
        internal Exception? Signal;
        public override float PathPrecision { get; set; } = 5;
        public override bool CanNavigateFully(WoWPoint from, WoWPoint to)
        { Calls++; if (Signal != null) ExceptionDispatchInfo.Capture(Signal).Throw(); return true; }
        public override MoveResult MoveTo(WoWPoint point) => throw new InvalidOperationException("Unexpected movement");
        public override WoWPoint[] GeneratePath(WoWPoint from, WoWPoint to) => throw new InvalidOperationException("Unexpected route generation");
        public override bool AtLocation(WoWPoint first, WoWPoint second) => first == second;
    }
    private sealed class Fixture : IDisposable
    {
        private readonly object source;
        private readonly NavigationProvider? previousProvider;
        private readonly ProbeNavigation provider = new();
        private readonly Dictionary<ulong, WoWObject> registry;
        private readonly object registryLock;
        private readonly WoWObject? previousMerchant;
        private readonly bool hadMerchant;
        private const ulong MerchantGuid = 768512340001UL;
        private IntPtr storage;
        private WoWUnit merchant = null!;
        private bool installed;
        private readonly XmlFlightNode priorFrom, priorTo;
        private readonly FlightPathReason priorReason;
        private readonly bool priorNeed;
        private readonly BotPoi priorPoi;
        internal XmlFlightNode Origin => (XmlFlightNode)Get(source, "Origin");
        private XmlFlightNode Destination => (XmlFlightNode)Get(source, "Destination");
        internal CharacterSettings Settings => (CharacterSettings)Get(source, "Settings");
        internal Fixture()
        {
            source = Activator.CreateInstance(typeof(FlightPathAdmissionRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
            previousProvider = Navigator.NavigationProvider;
            registry = (Dictionary<ulong, WoWObject>)typeof(ObjectManager).GetField("_objectList", StaticHidden)!.GetValue(null)!;
            registryLock = typeof(ObjectManager).GetField("_updateLock", StaticHidden)!.GetValue(null)!;
            lock (registryLock) hadMerchant = registry.TryGetValue(MerchantGuid, out previousMerchant);
            priorFrom = FlightPaths.TakingPathFrom; priorTo = FlightPaths.TakingPathTo;
            priorReason = FlightPaths.Reason; priorNeed = FlightPaths.NeedFlightPath; priorPoi = BotPoi.Current;
            try
            {
                if (ObjectManager.Executor != null) throw new InvalidOperationException("Test requires no native executor");
                storage = Marshal.AllocHGlobal(8192); Marshal.Copy(new byte[8192], 0, storage, 8192);
                uint address = unchecked((uint)storage.ToInt32()), descriptor = address + 4096;
                Write(address + 8, descriptor); Write(address + 20, 3); Write64(address + 48, MerchantGuid); Write64(descriptor, MerchantGuid);
                Write(descriptor + 12, 54321); Write(descriptor + Field("NpcFlags"), (uint)UnitNPCFlags.Flightmaster);
                Write(descriptor + Field("Health"), 100);
                Marshal.StructureToPtr(new WoWPoint(5, 6, 7), new IntPtr(unchecked((int)(address + 1944))), false);
                merchant = new WoWUnit(address);
                typeof(WoWObject).GetField("_cachedName", Hidden)!.SetValue(merchant, "Audit flight merchant");
                lock (registryLock) registry[MerchantGuid] = merchant;
                installed = true; ResetCaches(); Navigator.NavigationProvider = provider;
                if (!merchant.IsValid || !merchant.IsFlightMaster || merchant.IsHostile || merchant.Entry != 54321
                    || merchant.Guid != MerchantGuid || merchant.Location != new WoWPoint(5, 6, 7)
                    || !ObjectManager.CachedUnits.Contains(merchant) || Blacklist.Contains(merchant))
                    throw new InvalidOperationException("Real merchant descriptor/registry setup failed");
                if (!ReferenceEquals(FlightPaths.NearestFlightMerchant, merchant) || provider.Calls == 0)
                    throw new InvalidOperationException("Actual merchant lookup did not reach the counted provider");
                provider.Calls = 0;
            }
            catch { Dispose(); throw; }
        }
        internal XmlFlightNode AddUnknown()
        {
            var node = new XmlFlightNode(0, 20, "audit-update-origin", 1, new WoWPoint(1, 0, 0));
            node.Connect(Destination.Name); FlightPaths.XmlNodes.Insert(0, node); return node;
        }
        internal void Update(XmlFlightNode expected)
        {
            Check(FlightPaths.SetFlightPathUsage(From, To, out var start, out var end), "Update candidate was rejected");
            Check(ReferenceEquals(FlightPaths.TakingPathFrom, expected) && ReferenceEquals(FlightPaths.TakingPathTo, Destination)
                && start == expected.Location && end == Destination.Location && FlightPaths.Reason == FlightPathReason.Update,
                "Update publication changed the selected endpoints or reason");
            Check(provider.Calls > 0 && FlightPaths.NeedFlightPath == priorNeed && BotPoi.Current.Type == PoiType.Fly
                && BotPoi.Current.Guid == MerchantGuid && BotPoi.Current.Entry == merchant.Entry && BotPoi.Current.Location == merchant.Location,
                "Actual Update POI/provider identity was not retained");
        }
        internal void Known()
        {
            Check(FlightPaths.SetFlightPathUsage(From, To, out var start, out var end), "Known-origin control rejected");
            Check(provider.Calls == 0 && FlightPaths.Reason == FlightPathReason.Use && start == Origin.Location
                && end == Destination.Location && BotPoi.Current.Entry == Origin.MasterEntry, "Known-origin control borrowed Update provider");
        }
        internal void Query(bool expected)
        { bool result = FlightPaths.HasKnownConnection(From, To); Unchanged(); Check(result == expected && provider.Calls == 0, "Read query changed scope or consulted route provider"); }
        internal void Reject()
        { bool result = FlightPaths.SetFlightPathUsage(From, To, out _, out _); Unchanged(); Check(!result && provider.Calls == 0, "Disabled selection called provider or published"); }
        internal void Cancel(Exception signal)
        {
            Origin.MasterEntry = 0; provider.Signal = signal; Exception? observed = null;
            try { FlightPaths.SetFlightPathUsage(From, To, out _, out _); } catch (Exception error) { observed = error; }
            Unchanged(); Check(ReferenceEquals(observed, signal) && provider.Calls == 1, "Provider stop signal was masked, replaced or not invoked");
        }
        private void Unchanged() => Check(ReferenceEquals(FlightPaths.TakingPathFrom, priorFrom) && ReferenceEquals(FlightPaths.TakingPathTo, priorTo)
            && FlightPaths.Reason == priorReason && FlightPaths.NeedFlightPath == priorNeed && ReferenceEquals(BotPoi.Current, priorPoi), "Read/rejected/cancelled selection changed prior intent");
        public void Dispose()
        {
            try
            {
                if (installed)
                {
                    lock (registryLock) { if (hadMerchant) registry[MerchantGuid] = previousMerchant!; else registry.Remove(MerchantGuid); }
                    installed = false; ResetCaches();
                }
                Navigator.NavigationProvider = previousProvider;
            }
            finally
            {
                try { ((IDisposable)source).Dispose(); }
                finally { if (storage != IntPtr.Zero) { Marshal.FreeHGlobal(storage); storage = IntPtr.Zero; } }
            }
        }
    }
    private static object Get(object source, string name) => source.GetType().GetField(name, Hidden)!.GetValue(source)!;
    private static uint Field(string name) => Convert.ToUInt32(Enum.Parse(typeof(WoWUnit).Assembly.GetTypes().Single(t => t.IsEnum && t.Name == "UnitFields"), name)) * 4;
    private static void Write(uint address, uint value) => Marshal.WriteInt32(new IntPtr(unchecked((int)address)), unchecked((int)value));
    private static void Write64(uint address, ulong value) { Write(address, (uint)value); Write(address + 4, (uint)(value >> 32)); }
    private static void ResetCaches() => typeof(ObjectManager).GetMethod("ResetCaches", StaticHidden)!.Invoke(null, null);
    private static void Check(bool condition, string text) { if (!condition) throw new AssertionFailure(text); }
}
