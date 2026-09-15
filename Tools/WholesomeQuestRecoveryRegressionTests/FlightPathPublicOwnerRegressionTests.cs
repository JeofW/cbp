using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

// Actual standalone public flight owners, not a replica of their policies.
// Reuse the committed descriptor-backed fixture; control only feasibility and
// synchronous callbacks. No movement, native taxi, mesh or game is executed.
internal static class FlightPathPublicOwnerRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticHidden = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private enum Owner { Lookup, NearbyUpdate, UpdatePoi, LearnPoi }
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string message) : base(message) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Public flight owner tests require Windows x86.");
        var cases = new List<(string Name, Action<Fixture> Test)>();
        foreach (Owner owner in Enum.GetValues<Owner>())
        {
            cases.Add(($"{owner}: stable admitted merchant uses exactly one feasibility observation", f => f.Stable(owner)));
            cases.Add(($"{owner}: unavailable merchant retains existing intent", f => f.Missing(owner)));
            cases.Add(($"{owner}: stable unreachable merchant keeps ordinary blacklist behavior", f => f.Unreachable(owner)));
            foreach (string signal in new[] { "cancellation", "interruption", "ordinary error" })
                cases.Add(($"{owner}: provider {signal} keeps identity and prior state", f => f.ProviderError(owner, signal)));
            foreach (string mutation in new[] { "movement", "entry", "flag", "removed", "registry replacement", "player removed", "provider replaced", "reset", "nested flight", "service POI", "settings replaced" })
                cases.Add(($"{owner}: {mutation} invalidates the admitted merchant", f => f.Mutate(owner, mutation)));
            foreach (bool finalLog in new[] { false, true })
                cases.Add(($"{owner}: reset at {(finalLog ? "blacklist mutation" : "unreachable diagnostic")} prevents obsolete blacklisting", f => f.LoggingReset(owner, finalLog)));
        }
        cases.Add(("lookup remains independent of disabled flight settings", f => { f.Settings.UseFlightPaths = false; f.Settings.LearnFlightPaths = false; f.Stable(Owner.Lookup); }));
        cases.Add(("lookup does not require a cached network", f => { FlightPaths.XmlNodes = null!; f.Stable(Owner.Lookup); }));
        cases.Add(("explicit Learn POI retains learn-only compatibility", f => { f.Settings.UseFlightPaths = false; f.Settings.LearnFlightPaths = true; f.Stable(Owner.LearnPoi); }));
        cases.Add(("NearbyUpdate disabled setting prevents feasibility and state changes", f => f.Disabled()));
        cases.Add(("NearbyUpdate setting disabled during feasibility cannot set Update reason", f => f.Mutate(Owner.NearbyUpdate, "disabled")));
        cases.Add(("NearbyUpdate replaced network cannot authorize an obsolete Update", f => f.Mutate(Owner.NearbyUpdate, "network replaced")));
        cases.Add(("NearbyUpdate same network edited during feasibility is not the admitted observation", f => f.Mutate(Owner.NearbyUpdate, "network edited")));
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { using var fixture = new Fixture(); item.Test(fixture); passed++; Console.WriteLine("PASS public flight owner: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL public flight owner assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR public flight owner fixture/owner: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Public flight-owner scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual standalone lookup/update/POI owners; controlled feasibility; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException($"Public flight-owner regressions: assertions={assertions}; unexpected={unexpected}");
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
        public override MoveResult MoveTo(WoWPoint point) => throw new InvalidOperationException("Unexpected native movement");
        public override WoWPoint[] GeneratePath(WoWPoint from, WoWPoint to) => throw new InvalidOperationException("Unexpected route generation");
        public override bool AtLocation(WoWPoint a, WoWPoint b) => a == b;
    }

    private sealed class Intent
    {
        private readonly XmlFlightNode from = FlightPaths.TakingPathFrom, to = FlightPaths.TakingPathTo;
        private readonly FlightPathReason reason = FlightPaths.Reason;
        private readonly bool need = FlightPaths.NeedFlightPath;
        private readonly BotPoi poi = BotPoi.Current;
        internal bool EndpointsRetained => ReferenceEquals(from, FlightPaths.TakingPathFrom) && ReferenceEquals(to, FlightPaths.TakingPathTo) && need == FlightPaths.NeedFlightPath;
        internal bool PoiRetained => ReferenceEquals(poi, BotPoi.Current);
        internal bool Retained => EndpointsRetained && PoiRetained && reason == FlightPaths.Reason;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly object source;
        private readonly Probe probe = new();
        private readonly LocalPlayer player;
        internal readonly WoWUnit Merchant;
        internal CharacterSettings Settings => (CharacterSettings)Get(source, "Settings");
        private XmlFlightNode Origin => (XmlFlightNode)Get(source, "Origin");
        internal Fixture()
        {
            source = Activator.CreateInstance(typeof(FlightPathMerchantOwnershipRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
            player = ObjectManager.Me; Merchant = (WoWUnit)Get(source, "merchant");
            try
            {
                if (ObjectManager.Executor != null || typeof(Navigator).GetField("_meshNavigator", StaticHidden)!.GetValue(null) != null)
                    throw new InvalidOperationException("Public flight tests require no native executor/mesh navigator");
                Navigator.NavigationProvider = probe;
            }
            catch { Dispose(); throw; }
        }
        private void Prepare(Owner owner)
        {
            if (owner == Owner.UpdatePoi) FlightPaths.Reason = FlightPathReason.Update;
            if (owner == Owner.LearnPoi) FlightPaths.Reason = FlightPathReason.Learn;
        }
        private static object? Invoke(Owner owner)
        {
            if (owner == Owner.Lookup) return FlightPaths.NearestFlightMerchant;
            if (owner == Owner.NearbyUpdate) return FlightPaths.NeedNearbyUpdate();
            FlightPaths.SetPoi(); return null;
        }
        private static bool Denied(Owner owner, object? result) => owner == Owner.Lookup ? result == null : owner != Owner.NearbyUpdate || result is false;
        internal void Stable(Owner owner)
        {
            Prepare(owner); var retained = new Intent();
            object? result = Invoke(owner);
            Check(probe.Calls == 1 && probe.Callbacks == 0, "expected one admitted provider call, observed " + probe.Calls);
            if (owner == Owner.Lookup) Check(ReferenceEquals(result, Merchant) && retained.Retained, "lookup lost merchant identity or mutated intent");
            else if (owner == Owner.NearbyUpdate) Check(result is true && FlightPaths.Reason == FlightPathReason.Update && retained.EndpointsRetained && retained.PoiRetained, "ordinary nearby update failed");
            else Check(BotPoi.Current.Type == PoiType.Fly && BotPoi.Current.Guid == Merchant.Guid && BotPoi.Current.Entry == Merchant.Entry && BotPoi.Current.Location == Merchant.Location && retained.EndpointsRetained && FlightPaths.Reason == (owner == Owner.UpdatePoi ? FlightPathReason.Update : FlightPathReason.Learn), "ordinary explicit flight POI failed");
            Check(!Blacklist.Contains(Merchant.Guid), "reachable control was blacklisted");
        }
        internal void Missing(Owner owner)
        {
            Prepare(owner); Call("RegistryChange", false); var retained = new Intent();
            object? result = Invoke(owner);
            Check(Denied(owner, result) && probe.Calls == 0 && retained.Retained && !Blacklist.Contains(Merchant.Guid), "missing candidate probed, published or blacklisted");
        }
        internal void Unreachable(Owner owner)
        {
            Prepare(owner); probe.Reachable = false; var retained = new Intent();
            object? result = Invoke(owner);
            Check(Denied(owner, result) && probe.Calls == 1 && retained.Retained && Blacklist.Contains(Merchant.Guid), "stable unreachable compatibility changed");
        }
        internal void Disabled()
        {
            Settings.UseFlightPaths = false; Settings.LearnFlightPaths = false; var retained = new Intent();
            bool result = FlightPaths.NeedNearbyUpdate();
            Check(!result && probe.Calls == 0 && retained.Retained, "disabled nearby update consulted provider or mutated intent");
        }
        internal void ProviderError(Owner owner, string kind)
        {
            Prepare(owner); var retained = new Intent();
            Exception signal = kind == "cancellation" ? new OperationCanceledException("public flight provider stop")
                : kind == "interruption" ? new ThreadInterruptedException("public flight provider stop")
                : new InvalidOperationException("public flight provider error");
            probe.BeforeReturn = () => ExceptionDispatchInfo.Capture(signal).Throw();
            Exception? caught = null;
            try { Invoke(owner); } catch (Exception error) { caught = error; }
            Check(ReferenceEquals(caught, signal) && probe.Calls == 1 && probe.Callbacks == 1 && retained.Retained && !Blacklist.Contains(Merchant.Guid), "provider error lost identity, repeated a call or mutated state");
        }
        internal void Mutate(Owner owner, string mutation)
        {
            Prepare(owner); Intent? replacement = null; Exception? callbackError = null;
            probe.BeforeReturn = () =>
            {
                try
                {
                    switch (mutation)
                    {
                        case "movement": Call("MoveMerchant"); break;
                        case "entry": Call("ChangeEntry"); break;
                        case "flag": Call("RemoveFlightFlag"); break;
                        case "removed": Call("RegistryChange", false); break;
                        case "registry replacement": Call("RegistryChange", true); break;
                        case "player removed": ObjectManager.Me = null; break;
                        case "provider replaced": Navigator.NavigationProvider = new Probe(); break;
                        case "reset": FlightPaths.Reset(); break;
                        case "nested flight": Call("PublishReplacement"); break;
                        case "service POI": BotPoi.Current = new BotPoi(new WoWPoint(70, 80, 90), PoiType.Repair); break;
                        case "settings replaced":
                            var next = (CharacterSettings)RuntimeHelpers.GetUninitializedObject(typeof(CharacterSettings));
                            next.UseFlightPaths = Settings.UseFlightPaths; next.LearnFlightPaths = Settings.LearnFlightPaths;
                            typeof(CharacterSettings).GetProperty("Instance")!.SetValue(null, next); break;
                        case "disabled": Settings.UseFlightPaths = false; break;
                        case "network replaced": FlightPaths.XmlNodes = new List<XmlFlightNode>(); break;
                        case "network edited": Origin.UpdateLevel++; break;
                        default: throw new InvalidOperationException("Unknown test mutation: " + mutation);
                    }
                    replacement = new Intent();
                }
                catch (Exception error) { callbackError = error; throw; }
            };
            CheckRejected(owner, () => replacement, () => callbackError, () => probe.Callbacks);
        }
        internal void LoggingReset(Owner owner, bool finalLog)
        {
            Prepare(owner); probe.Reachable = false; Intent? replacement = null; Exception? callbackError = null; int callbacks = 0;
            Action<LogLevel, string>? handler = null;
            handler = (_, message) =>
            {
                if (!message.Contains("Blacklisting ", StringComparison.Ordinal) || (finalLog ? !message.Contains(Merchant.Guid.ToString("X16"), StringComparison.Ordinal) : !message.Contains("for 5 minutes", StringComparison.Ordinal))) return;
                Logging.OnMessageLogged -= handler; callbacks++;
                try { FlightPaths.Reset(); replacement = new Intent(); }
                catch (Exception error) { callbackError = error; throw; }
            };
            Logging.OnMessageLogged += handler;
            try { CheckRejected(owner, () => replacement, () => callbackError, () => callbacks); }
            finally { Logging.OnMessageLogged -= handler; }
        }
        private void CheckRejected(Owner owner, Func<Intent?> replacement, Func<Exception?> callbackError, Func<int> callbacks)
        {
            object? result = null; Exception? caught = null;
            try { result = Invoke(owner); } catch (Exception error) { caught = error; }
            if (callbackError() is Exception failure) ExceptionDispatchInfo.Capture(failure).Throw();
            Check(callbacks() == 1 && replacement() != null, "fixture did not reach the actual callback exactly once");
            Check(caught == null && Denied(owner, result) && replacement()!.Retained && probe.Calls == 1 && !Blacklist.Contains(Merchant.Guid),
                $"denied={Denied(owner, result)}; retained={replacement()!.Retained}; calls={probe.Calls}; blacklisted={Blacklist.Contains(Merchant.Guid)}; error={caught?.GetType().Name ?? "none"}");
        }
        private void Call(string name, params object[] args)
        {
            try { source.GetType().GetMethod(name, Hidden)!.Invoke(source, args); }
            catch (TargetInvocationException error) when (error.InnerException != null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        public void Dispose() { ObjectManager.Me = player; ((IDisposable)source).Dispose(); }
    }
    private static object Get(object source, string name) => source.GetType().GetField(name, Hidden)?.GetValue(source)
        ?? source.GetType().GetProperty(name, Hidden)?.GetValue(source) ?? throw new InvalidOperationException("Missing retained fixture member: " + name);
    private static void Check(bool condition, string message) { if (!condition) throw new AssertionFailure(message); }
}
