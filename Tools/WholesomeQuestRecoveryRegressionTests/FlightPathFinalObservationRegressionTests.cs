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

// Real SetFlightPathUsage with a descriptor-backed LocalPlayer subclass whose
// virtual Guid observation is armed only by actual POI publication logging.
// This is controlled managed callback evidence, not native frame atomicity.
internal static class FlightPathFinalObservationRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly WoWPoint From = new(0, 0, 0), To = new(1100, 0, 0);
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string message) : base(message) { } }
    private enum Replacement { None, Flight, Service, Reset }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Final observation tests require Windows x86.");
        var cases = new List<(string Name, Action<Fixture> Test)>();
        foreach (bool known in new[] { false, true })
        {
            cases.Add(($"{(known ? "known" : "merchant")}: stable virtual player observation preserves successful admission", f => f.Stable(known)));
            foreach (bool persistent in new[] { false, true })
                foreach (string kind in new[] { "cancellation", "interruption", "ordinary" })
                    cases.Add(($"{(known ? "known" : "merchant")}: {(persistent ? "persistent" : "one-shot")} final {kind} observation revokes unknown owned intent", f => f.Observe(known, Make(kind), persistent)));
        }
        foreach (string kind in new[] { "cancellation", "interruption", "ordinary" })
            foreach (Replacement replacement in new[] { Replacement.Flight, Replacement.Service })
                cases.Add(($"final {kind} observation cannot damage {replacement} replacement", f => f.Observe(true, Make(kind), false, replacement)));
        foreach (var pair in new[] { ("cancellation", "ordinary"), ("interruption", "cancellation"), ("ordinary", "interruption"), ("ordinary", "cancellation"), ("ordinary", "ordinary") })
            cases.Add(($"{pair.Item1} observation followed by {pair.Item2} cleanup retains first-stop priority", f => f.Observe(true, Make(pair.Item1), false, cleanup: Make(pair.Item2))));
        foreach (Replacement replacement in new[] { Replacement.Flight, Replacement.Service })
            cases.Add(($"cleanup-log {replacement} replacement survives final observation failure", f => f.Observe(true, Make("cancellation"), false, cleanupReplacement: replacement)));
        foreach (Replacement replacement in new[] { Replacement.Flight, Replacement.Service, Replacement.Reset })
            cases.Add(($"successful final getter cannot certify the old call after {replacement} replacement", f => f.Observe(true, null, false, replacement)));
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { using var fixture = new Fixture(); item.Test(fixture); passed++; Console.WriteLine("PASS final flight observation: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL final flight observation assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR final flight observation fixture/owner: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Final flight-observation scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual publication plus virtual observation boundary; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException($"Final flight-observation regressions: assertions={assertions}; unexpected={unexpected}");
    }
    private static Exception Make(string kind) => kind == "cancellation" ? new OperationCanceledException("final observation cancellation")
        : kind == "interruption" ? new ThreadInterruptedException("final observation interruption")
        : new InvalidOperationException("final observation ordinary error");
    private static bool Stop(Exception? error) => error is OperationCanceledException or ThreadInterruptedException;
    private sealed class ObservedPlayer : LocalPlayer
    {
        internal Func<ulong>? Read;
        internal ObservedPlayer(uint address) : base(address) { }
        public override ulong Guid => Read == null ? base.Guid : Read();
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
        private readonly LocalPlayer original;
        private readonly ObservedPlayer player;
        private readonly XmlFlightNode origin;
        private readonly ulong guid;
        internal Fixture()
        {
            source = Activator.CreateInstance(typeof(FlightPathMerchantOwnershipRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
            original = ObjectManager.Me; player = new ObservedPlayer(original.BaseAddress); guid = original.Guid;
            try
            {
                origin = (XmlFlightNode)Get(source, "Origin");
                ObjectManager.Me = player;
                if (!player.IsValid || player.Guid != guid || player.MapId != original.MapId)
                    throw new InvalidOperationException("Virtual-observation fixture did not retain the descriptor-backed player");
            }
            catch { Dispose(); throw; }
        }
        internal void Stable(bool known)
        {
            if (known) origin.MasterEntry = 90;
            Check(FlightPaths.SetFlightPathUsage(From, To, out var start, out var end) && start == origin.Location && !Empty(end)
                && FlightPaths.Reason == (known ? FlightPathReason.Use : FlightPathReason.Update) && BotPoi.Current.Type == PoiType.Fly,
                "stable descriptor-backed observation did not publish");
        }
        private void Replace(Replacement replacement)
        {
            player.Read = null;
            if (replacement == Replacement.Flight)
            {
                // Keep the exact same network references and values. A new
                // publication token, not changed route data, identifies this owner.
                if (!FlightPaths.SetFlightPathUsage(From, To, out _, out _))
                    throw new InvalidOperationException("The actual same-route nested publication was rejected");
            }
            else if (replacement == Replacement.Service) BotPoi.Current = new BotPoi(new WoWPoint(7, 8, 9), PoiType.Repair);
            else if (replacement == Replacement.Reset) FlightPaths.Reset();
        }
        internal void Observe(bool known, Exception? signal, bool persistent, Replacement replacement = Replacement.None,
            Exception? cleanup = null, Replacement cleanupReplacement = Replacement.None)
        {
            if (known) origin.MasterEntry = 90;
            bool priorNeed = FlightPaths.NeedFlightPath; int publications = 0, reads = 0, cleanups = 0; bool detached = false;
            Intent? newer = null; Exception? callbackError = null;
            Action<LogLevel, string>? publicationHandler = null, cleanupHandler = null;
            publicationHandler = (_, message) =>
            {
                if (!message.Contains("Changed POI to:", StringComparison.Ordinal) || BotPoi.Current.Type != PoiType.Fly) return;
                Logging.OnMessageLogged -= publicationHandler; publications++;
                player.Read = () =>
                {
                    reads++; if (!persistent) player.Read = null;
                    if (replacement != Replacement.None)
                    {
                        try { Replace(replacement); newer = new Intent(); }
                        catch (Exception error) { callbackError = error; throw; }
                    }
                    if (signal != null) ExceptionDispatchInfo.Capture(signal).Throw();
                    return guid;
                };
            };
            cleanupHandler = (_, message) =>
            {
                if (publications != 1 || !message.Contains("Cleared POI", StringComparison.Ordinal) || BotPoi.Current.Type != PoiType.None) return;
                Logging.OnMessageLogged -= cleanupHandler; cleanups++; detached = Revoked;
                if (cleanupReplacement != Replacement.None)
                {
                    try { Replace(cleanupReplacement); newer = new Intent(); }
                    catch (Exception error) { callbackError = error; throw; }
                }
                if (cleanup != null) ExceptionDispatchInfo.Capture(cleanup).Throw();
            };
            Logging.OnMessageLogged += publicationHandler; Logging.OnMessageLogged += cleanupHandler;
            bool accepted = false; Exception? caught = null; var start = WoWPoint.Empty; var end = WoWPoint.Empty;
            try { accepted = FlightPaths.SetFlightPathUsage(From, To, out start, out end); }
            catch (Exception error) { caught = error; }
            finally { player.Read = null; Logging.OnMessageLogged -= publicationHandler; Logging.OnMessageLogged -= cleanupHandler; }
            if (callbackError != null) throw new InvalidOperationException("Final-observation fixture callback failed", callbackError);
            Check(publications == 1 && reads == 1, $"publication={publications}; actual final reads={reads}; expected exactly one armed observation");
            Exception? expected = !Stop(signal) && Stop(cleanup) ? cleanup : signal;
            Check(ReferenceEquals(caught, expected), "original observation/first-stop exception identity changed");
            Check(!accepted && Empty(start) && Empty(end) && FlightPaths.NeedFlightPath == priorNeed, "obsolete or unknown final observation returned successful endpoints");
            if (replacement != Replacement.None || cleanupReplacement != Replacement.None)
                Check(newer != null && newer.Retained, "old call damaged the replacement created by an observation/cleanup callback");
            else Check(Revoked, "unavailable final observation left its own flight intent published");
            if (replacement == Replacement.None)
                Check(cleanups == 1 && detached, "unknown owned intent was not detached before cleanup logging");
            else Check(cleanups == (replacement == Replacement.Reset ? 1 : 0), "old call performed replacement cleanup");
        }
        public void Dispose() { player.Read = null; ObjectManager.Me = original; ((IDisposable)source).Dispose(); }
    }
    private static object Get(object source, string name) => source.GetType().GetField(name, Hidden)?.GetValue(source)
        ?? source.GetType().GetProperty(name, Hidden)?.GetValue(source) ?? throw new InvalidOperationException("Missing retained fixture member: " + name);
    private static bool Revoked => FlightPaths.TakingPathFrom == null && FlightPaths.TakingPathTo == null && FlightPaths.Reason == FlightPathReason.None && BotPoi.Current.Type == PoiType.None;
    private static bool Empty(WoWPoint point) => float.IsNaN(point.X) && float.IsNaN(point.Y) && float.IsNaN(point.Z);
    private static void Check(bool condition, string message) { if (!condition) throw new AssertionFailure(message); }
}
