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

// Actual BotPoi.Clear -> FlightPaths.Reset -> Navigator.Clear owners. The mesh
// adapter supplies only a counted callback boundary; no native path/movement runs.
internal static class FlightResetOwnershipRegressionTests
{
    private const BindingFlags StaticHidden = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly WoWPoint From = new(0, 0, 0), To = new(1100, 0, 0);
    private enum Owner { FlightReset, PoiClear }
    private enum Boundary { Navigation, ClearedPoi, Reason }
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string text) : base(text) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Flight reset tests require Windows x86.");
        var cases = new List<(string Name, Action<Fixture> Test)>();
        foreach (Owner owner in Enum.GetValues<Owner>())
        {
            var selected = owner;
            foreach (PoiType type in new[] { PoiType.Fly, PoiType.Repair, PoiType.None })
            {
                var initial = type;
                cases.Add(($"{selected} stable {initial} cleanup is nonrecursive and keeps unrelated state", f => f.Stable(selected, initial)));
            }
            foreach (Boundary boundary in Enum.GetValues<Boundary>())
            {
                var stage = boundary;
                foreach (string replacement in new[] { "flight", "service" })
                {
                    var work = replacement;
                    cases.Add(($"{selected} preserves {work} published by {stage}", f => f.Observe(selected, stage, work)));
                }
                foreach (string signal in new[] { "ordinary", "cancellation", "interruption" })
                {
                    var kind = signal;
                    cases.Add(($"{selected} {stage} {kind} does not strand owned flight state", f => f.Observe(selected, stage, null, Make(kind))));
                }
            }
            cases.Add(($"{selected} navigation cancellation survives a later ordinary cleanup error", f =>
                f.Observe(selected, Boundary.Navigation, null, Make("cancellation"), Make("ordinary"))));
            cases.Add(($"{selected} navigation interruption wins over later cleanup cancellation", f =>
                f.Observe(selected, Boundary.Navigation, null, Make("interruption"), Make("cancellation"))));
            cases.Add(($"{selected} cleanup cancellation supersedes an ordinary navigation error", f =>
                f.Observe(selected, Boundary.Navigation, null, Make("ordinary"), Make("cancellation"))));
            cases.Add(($"{selected} replacement from a throwing navigation callback is retained", f =>
                f.Observe(selected, Boundary.Navigation, "flight", Make("cancellation"))));
            cases.Add(($"{selected} replacement from a throwing cleared-POI callback is retained", f =>
                f.Observe(selected, Boundary.ClearedPoi, "flight", Make("interruption"))));
            cases.Add(($"{selected} nested reset cannot be resumed by obsolete cleanup", f =>
                f.Observe(selected, Boundary.Navigation, "reset")));
        }
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { using var fixture = new Fixture(); item.Test(fixture); passed++; Console.WriteLine("PASS flight reset ownership: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL flight reset ownership assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR flight reset ownership fixture/owner: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Flight reset-ownership scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual reset/POI/navigation owners; controlled mesh callback; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException($"Flight reset-ownership regressions: assertions={assertions}; unexpected={unexpected}");
    }
    private static Exception Make(string kind) => kind == "cancellation" ? new OperationCanceledException("reset cancellation")
        : kind == "interruption" ? new ThreadInterruptedException("reset interruption") : new InvalidOperationException("reset ordinary error");
    private static bool Stop(Exception? error) => error is OperationCanceledException or ThreadInterruptedException;

    private sealed class ProbeMesh : MeshNavigator
    {
        internal int Clears, ReplacementClears;
        internal bool ReplacementPath;
        internal Action? Callback;
        public override bool Clear()
        {
            Clears++;
            if (ReplacementPath) { ReplacementClears++; ReplacementPath = false; }
            var callback = Callback; Callback = null;
            callback?.Invoke();
            return true;
        }
    }
    private sealed class Intent
    {
        private readonly object owner = OwnerField.GetValue(null)!;
        private readonly XmlFlightNode from = FlightPaths.TakingPathFrom, to = FlightPaths.TakingPathTo;
        private readonly FlightPathReason reason = FlightPaths.Reason;
        private readonly BotPoi poi = BotPoi.Current;
        internal bool Retained => ReferenceEquals(owner, OwnerField.GetValue(null)) &&
            ReferenceEquals(from, FlightPaths.TakingPathFrom) && ReferenceEquals(to, FlightPaths.TakingPathTo) &&
            reason == FlightPaths.Reason && ReferenceEquals(poi, BotPoi.Current);
    }
    private static readonly FieldInfo MeshField = typeof(Navigator).GetField("_meshNavigator", StaticHidden)!;
    private static readonly FieldInfo OwnerField = typeof(FlightPaths).GetField("_flightIntentOwner", StaticHidden)!;
    private sealed class Fixture : IDisposable
    {
        private readonly object source;
        private readonly object? oldMesh = MeshField.GetValue(null);
        private readonly object oldOwner = OwnerField.GetValue(null)!;
        private readonly ProbeMesh mesh = (ProbeMesh)RuntimeHelpers.GetUninitializedObject(typeof(ProbeMesh));
        private readonly List<XmlFlightNode> network;
        private readonly bool need;
        internal Fixture()
        {
            source = Activator.CreateInstance(typeof(FlightPathAdmissionRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
            try
            {
                if (ObjectManager.Executor != null) throw new InvalidOperationException("Reset tests require no native executor");
                MeshField.SetValue(null, mesh);
                if (!FlightPaths.SetFlightPathUsage(From, To, out _, out _)) throw new InvalidOperationException("Initial real flight publication failed");
                network = FlightPaths.XmlNodes; need = FlightPaths.NeedFlightPath;
            }
            catch { Dispose(); throw; }
        }
        private static bool Detached => FlightPaths.TakingPathFrom == null && FlightPaths.TakingPathTo == null && FlightPaths.Reason == FlightPathReason.None;
        private void Unrelated() => Check(ReferenceEquals(FlightPaths.XmlNodes, network) && FlightPaths.NeedFlightPath == need,
            "cleanup modified the network or unrelated NeedFlightPath");
        private static void Invoke(Owner owner)
        {
            if (owner == Owner.FlightReset) FlightPaths.Reset();
            else BotPoi.Clear("audit-owned-reset");
        }
        internal void Stable(Owner owner, PoiType initial)
        {
            if (initial != PoiType.Fly) BotPoi.Current = new BotPoi(new WoWPoint(7, 8, 9), initial);
            var priorPoi = BotPoi.Current;
            Invoke(owner); Unrelated();
            Check(Detached, "ordinary cleanup retained old endpoints/reason");
            Check(owner == Owner.PoiClear || initial == PoiType.Fly ? BotPoi.Current.Type == PoiType.None : ReferenceEquals(priorPoi, BotPoi.Current),
                "reset cleared an unrelated POI or failed to clear its own");
            Check(mesh.Clears == 1, "one cleanup recursively cleared navigation " + mesh.Clears + " times");
        }
        internal void Observe(Owner owner, Boundary boundary, string? replacement, Exception? signal = null, Exception? cleanup = null)
        {
            int callbacks = 0, cleanupCallbacks = 0; bool detachedAtCallback = false; Intent? newer = null; Exception? fixtureError = null;
            void Replace()
            {
                if (replacement == "flight")
                {
                    if (!FlightPaths.SetFlightPathUsage(From, To, out _, out _)) throw new InvalidOperationException("Replacement real flight publication failed");
                    mesh.ReplacementPath = true;
                }
                else if (replacement == "service")
                {
                    FlightPaths.TakingPathFrom = null; FlightPaths.TakingPathTo = null; FlightPaths.Reason = FlightPathReason.None;
                    BotPoi.Current = new BotPoi(new WoWPoint(70, 80, 90), PoiType.Repair);
                    mesh.ReplacementPath = true;
                }
                else if (replacement == "reset") FlightPaths.Reset();
                if (replacement != null) newer = new Intent();
            }
            void Trigger()
            {
                if (callbacks != 0) return;
                callbacks++; detachedAtCallback = Detached;
                try { Replace(); } catch (Exception error) { fixtureError = error; throw; }
                if (signal != null) ExceptionDispatchInfo.Capture(signal).Throw();
            }
            if (boundary == Boundary.Navigation) mesh.Callback = Trigger;
            Action<LogLevel, string> handler = (_, message) =>
            {
                bool reason = message.Contains("Cleared POI - Reason", StringComparison.Ordinal);
                bool cleared = !reason && message.Contains("Cleared POI", StringComparison.Ordinal) && BotPoi.Current.Type == PoiType.None;
                if ((boundary == Boundary.Reason && reason) || (boundary == Boundary.ClearedPoi && cleared)) Trigger();
                if (cleanup != null && boundary == Boundary.Navigation && callbacks == 1 && cleared && cleanupCallbacks == 0)
                { cleanupCallbacks++; ExceptionDispatchInfo.Capture(cleanup).Throw(); }
            };
            Logging.OnMessageLogged += handler; Exception? caught = null;
            try { Invoke(owner); } catch (Exception error) { caught = error; }
            finally { Logging.OnMessageLogged -= handler; mesh.Callback = null; }
            if (fixtureError != null) throw new InvalidOperationException("Reset fixture replacement failed", fixtureError);
            Unrelated();
            Check(callbacks == 1, "intended callback was not reached exactly once");
            var expected = !Stop(signal) && Stop(cleanup) ? cleanup : signal ?? cleanup;
            Check(ReferenceEquals(caught, expected), "cleanup changed exception/first-stop identity");
            Check(detachedAtCallback, "owned flight endpoints were not detached before a callback");
            if (replacement == null) Check(Detached && BotPoi.Current.Type == PoiType.None, "cleanup left owned stale flight state published");
            else Check(newer != null && newer.Retained, "obsolete cleanup overwrote replacement work");
            Check(mesh.ReplacementClears == 0, "obsolete cleanup cleared replacement navigation");
            Check(mesh.Clears == (replacement == "reset" ? 2 : 1), "recursive or resumed navigation cleanup: " + mesh.Clears);
            if (cleanup != null) Check(cleanupCallbacks == 1, "owned POI cleanup did not run after navigation failure");
        }
        public void Dispose()
        {
            MeshField.SetValue(null, oldMesh);
            try { ((IDisposable)source).Dispose(); }
            finally { OwnerField.SetValue(null, oldOwner); }
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new AssertionFailure(message); }
}
