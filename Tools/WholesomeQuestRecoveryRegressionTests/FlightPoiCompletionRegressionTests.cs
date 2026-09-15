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

// Explicit SetPoi owns a publication too. Exercise its actual logging boundary
// with retained test-process merchant storage, not a replacement policy model.
internal static class FlightPoiCompletionRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string message) : base(message) { } }
    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Flight POI completion tests require Windows x86.");
        var cases = new List<(string Name, Action<Fixture> Test)>();
        foreach (FlightPathReason reason in new[] { FlightPathReason.Use, FlightPathReason.Update, FlightPathReason.Learn })
        {
            cases.Add(($"{reason}: stable explicit POI retains intended endpoints and reason", f => f.Stable(reason)));
            foreach (string mutation in new[] { "use setting", "learn setting", "player", "provider" })
                cases.Add(($"{reason}: final {mutation} invalidation revokes its owned publication", f => f.Observe(reason, mutation)));
            foreach (string mutation in reason == FlightPathReason.Use ? new[] { "node location", "node entry" } : new[] { "merchant location", "merchant entry", "merchant flag", "merchant removed", "merchant replaced" })
                cases.Add(($"{reason}: final {mutation} invalidation revokes its owned publication", f => f.Observe(reason, mutation)));
            foreach (string replacement in new[] { "flight", "service", "reset" })
                cases.Add(($"{reason}: final {replacement} replacement is never cleaned by the old call", f => f.Observe(reason, replacement, preserve: true)));
            foreach (string kind in new[] { "cancellation", "interruption", "ordinary" })
                foreach (bool candidate in new[] { false, true })
                    cases.Add(($"{reason}: {(candidate ? "candidate" : "settings")} invalidation plus {kind} still cleans", f => f.Observe(reason, candidate ? (reason == FlightPathReason.Use ? "node location" : "merchant location") : "use setting", Make(kind))));
            foreach (string replacement in new[] { "flight", "service" })
                cases.Add(($"{reason}: cleanup-log {replacement} replacement survives detached revocation", f => f.Observe(reason, "use setting", cleanupReplacement: replacement)));
            cases.Add(($"{reason}: first cancellation survives cleanup ordinary error", f => f.Observe(reason, "use setting", Make("cancellation"), cleanup: Make("ordinary"))));
            cases.Add(($"{reason}: cleanup interruption supersedes the original ordinary error", f => f.Observe(reason, "use setting", Make("ordinary"), cleanup: Make("interruption"))));
        }
        cases.Add(("explicit Use with no node retains prior state", f => f.MissingNode()));
        cases.Add(("explicit Learn remains valid with only learning enabled", f => { f.Settings.UseFlightPaths = false; f.Settings.LearnFlightPaths = true; f.Stable(FlightPathReason.Learn); }));
        cases.Add(("explicit Use rejects unknown X before publication", f => f.Invalid(new WoWPoint(float.NaN, 10, 25), 90)));
        cases.Add(("explicit Use rejects infinite Y before publication", f => f.Invalid(new WoWPoint(10, float.PositiveInfinity, 25), 90)));
        cases.Add(("explicit Use rejects unknown Z before publication", f => f.Invalid(new WoWPoint(10, 10, float.NaN), 90)));
        cases.Add(("explicit Use rejects an unknown master rather than inventing Update", f => f.Invalid(new WoWPoint(10, 10, 25), 0)));
        cases.Add(("explicit Use preserves finite negative world coordinates", f => { f.Origin.Location = new WoWPoint(-10, -20, -30); f.Stable(FlightPathReason.Use); }));
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { using var fixture = new Fixture(); item.Test(fixture); passed++; Console.WriteLine("PASS flight POI completion: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL flight POI completion assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR flight POI completion fixture/owner: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Flight POI-completion scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual explicit publication and callback cleanup; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException($"Flight POI-completion regressions: assertions={assertions}; unexpected={unexpected}");
    }
    private static Exception Make(string kind) => kind == "cancellation" ? new OperationCanceledException("POI publication cancellation")
        : kind == "interruption" ? new ThreadInterruptedException("POI publication interruption") : new InvalidOperationException("POI publication ordinary error");
    private static bool Stop(Exception? error) => error is OperationCanceledException or ThreadInterruptedException;
    private sealed class Probe : NavigationProvider
    {
        public override float PathPrecision { get; set; } = 5;
        public override bool CanNavigateFully(WoWPoint a, WoWPoint b) => true;
        public override MoveResult MoveTo(WoWPoint p) => throw new InvalidOperationException("Unexpected native movement");
        public override WoWPoint[] GeneratePath(WoWPoint a, WoWPoint b) => throw new InvalidOperationException("Unexpected path generation");
        public override bool AtLocation(WoWPoint a, WoWPoint b) => a == b;
    }
    private sealed class Intent
    {
        private readonly XmlFlightNode from = FlightPaths.TakingPathFrom, to = FlightPaths.TakingPathTo;
        private readonly FlightPathReason reason = FlightPaths.Reason;
        private readonly bool need = FlightPaths.NeedFlightPath;
        private readonly BotPoi poi = BotPoi.Current;
        internal bool EndpointsRetained => ReferenceEquals(from, FlightPaths.TakingPathFrom) && ReferenceEquals(to, FlightPaths.TakingPathTo) && need == FlightPaths.NeedFlightPath;
        internal bool Retained => EndpointsRetained && reason == FlightPaths.Reason && ReferenceEquals(poi, BotPoi.Current);
    }
    private sealed class Fixture : IDisposable
    {
        private readonly object source;
        private readonly LocalPlayer player;
        private readonly WoWUnit merchant;
        internal readonly XmlFlightNode Origin;
        internal CharacterSettings Settings => (CharacterSettings)Get(source, "Settings");
        internal Fixture()
        {
            source = Activator.CreateInstance(typeof(FlightPathMerchantOwnershipRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
            player = ObjectManager.Me;
            try { Origin = (XmlFlightNode)Get(source, "Origin"); merchant = (WoWUnit)Get(source, "merchant"); Origin.MasterEntry = 90; }
            catch { Dispose(); throw; }
        }
        private void Prepare(FlightPathReason reason)
        {
            FlightPaths.Reason = reason;
            if (reason == FlightPathReason.Learn) Settings.LearnFlightPaths = true;
        }
        private void Invoke(FlightPathReason reason) { if (reason == FlightPathReason.Use) FlightPaths.SetPoi(Origin); else FlightPaths.SetPoi(); }
        internal void Stable(FlightPathReason reason)
        {
            Prepare(reason); var previous = new Intent(); Invoke(reason);
            Check(previous.EndpointsRetained && FlightPaths.Reason == reason && BotPoi.Current.Type == PoiType.Fly
                && BotPoi.Current.Entry == (reason == FlightPathReason.Use ? Origin.MasterEntry : merchant.Entry)
                && BotPoi.Current.Location == (reason == FlightPathReason.Use ? Origin.Location : merchant.Location), "ordinary explicit POI publication changed its contract");
        }
        internal void MissingNode()
        {
            Prepare(FlightPathReason.Use); var previous = new Intent(); FlightPaths.SetPoi();
            Check(previous.Retained, "null-node Use changed prior state");
        }
        internal void Invalid(WoWPoint point, uint master)
        {
            Origin.Location = point; Origin.MasterEntry = master; Prepare(FlightPathReason.Use); var previous = new Intent(); int publications = 0;
            Action<LogLevel, string> handler = (_, message) => { if (message.Contains("Changed POI to:", StringComparison.Ordinal)) publications++; };
            Logging.OnMessageLogged += handler;
            try { FlightPaths.SetPoi(Origin); }
            finally { Logging.OnMessageLogged -= handler; }
            Check(previous.Retained && publications == 0, "invalid explicit node published a Fly POI");
        }
        private void Change(string mutation)
        {
            switch (mutation)
            {
                case "use setting": Settings.UseFlightPaths = !Settings.UseFlightPaths; break;
                case "learn setting": Settings.LearnFlightPaths = !Settings.LearnFlightPaths; break;
                case "player": ObjectManager.Me = null; break;
                case "provider": Navigator.NavigationProvider = new Probe(); break;
                case "node location": Origin.Location = new WoWPoint(70, 80, 90); break;
                case "node entry": Origin.MasterEntry++; break;
                case "merchant location": Call("MoveMerchant"); break;
                case "merchant entry": Call("ChangeEntry"); break;
                case "merchant flag": Call("RemoveFlightFlag"); break;
                case "merchant removed": Call("RegistryChange", false); break;
                case "merchant replaced": Call("RegistryChange", true); break;
                case "service": BotPoi.Current = new BotPoi(new WoWPoint(7, 8, 9), PoiType.Repair); break;
                case "reset": FlightPaths.Reset(); break;
                case "flight": Settings.UseFlightPaths = true; ObjectManager.Me = player; Call("PublishReplacement"); break;
                default: throw new InvalidOperationException("Unknown mutation: " + mutation);
            }
        }
        internal void Observe(FlightPathReason reason, string mutation, Exception? signal = null, bool preserve = false,
            string? cleanupReplacement = null, Exception? cleanup = null)
        {
            Prepare(reason); bool priorNeed = FlightPaths.NeedFlightPath; int publications = 0, cleanups = 0; bool detached = false;
            Intent? newer = null; Exception? fixtureError = null; Action<LogLevel, string>? publishHandler = null, cleanupHandler = null;
            publishHandler = (_, message) =>
            {
                if (!message.Contains("Changed POI to:", StringComparison.Ordinal) || BotPoi.Current.Type != PoiType.Fly) return;
                Logging.OnMessageLogged -= publishHandler; publications++;
                try { Change(mutation); if (preserve) newer = new Intent(); }
                catch (Exception error) { fixtureError = error; throw; }
                if (signal != null) ExceptionDispatchInfo.Capture(signal).Throw();
            };
            cleanupHandler = (_, message) =>
            {
                if (publications != 1 || !message.Contains("Cleared POI", StringComparison.Ordinal) || BotPoi.Current.Type != PoiType.None) return;
                Logging.OnMessageLogged -= cleanupHandler; cleanups++; detached = Revoked;
                if (cleanupReplacement != null)
                {
                    try { Change(cleanupReplacement); newer = new Intent(); }
                    catch (Exception error) { fixtureError = error; throw; }
                }
                if (cleanup != null) ExceptionDispatchInfo.Capture(cleanup).Throw();
            };
            Logging.OnMessageLogged += publishHandler; Logging.OnMessageLogged += cleanupHandler; Exception? caught = null;
            try { Invoke(reason); } catch (Exception error) { caught = error; }
            finally { Logging.OnMessageLogged -= publishHandler; Logging.OnMessageLogged -= cleanupHandler; }
            if (fixtureError != null) throw new InvalidOperationException("POI publication fixture callback failed", fixtureError);
            Check(publications == 1, "actual POI publication callback was not reached exactly once");
            Exception? expected = !Stop(signal) && Stop(cleanup) ? cleanup : signal ?? cleanup;
            Check(ReferenceEquals(caught, expected), "publication/cleanup first-stop identity was not retained");
            Check(FlightPaths.NeedFlightPath == priorNeed, "unrelated NeedFlightPath changed");
            if (preserve || cleanupReplacement != null) Check(newer != null && newer.Retained, "old SetPoi overwrote replacement work");
            else Check(Revoked, "invalidated explicit flight POI remained published");
            if (!preserve) Check(cleanups == 1 && detached, "owned publication was not detached before cleanup diagnostic");
            else Check(cleanups == (mutation == "reset" ? 1 : 0), "old SetPoi cleaned a replacement");
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
    private static bool Revoked => FlightPaths.TakingPathFrom == null && FlightPaths.TakingPathTo == null && FlightPaths.Reason == FlightPathReason.None && BotPoi.Current.Type == PoiType.None;
    private static void Check(bool condition, string message) { if (!condition) throw new AssertionFailure(message); }
}
