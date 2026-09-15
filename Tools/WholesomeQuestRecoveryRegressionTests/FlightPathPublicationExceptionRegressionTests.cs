using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.POI;
using Styx.WoWInternals;

// Test-only continuation at 651071e1. Actual public admission and actual logger
// events; reuse unchanged descriptor-backed fixtures. No native/game effects.
// LOCAL, UNCOMPILED, UNEXECUTED until an exact-source Windows run proves otherwise.
internal static class FlightPathPublicationExceptionRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly WoWPoint From = new(0, 0, 0), To = new(1100, 0, 0);
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string text) : base(text) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Flight publication exception tests require Windows x86.");
        var cases = new List<(string Name, Action<Fixture> Test)>
        {
            ("ordinary known publication remains successful", f => f.Stable(true)),
            ("ordinary merchant Update remains successful", f => f.Stable(false)),
        };
        var signals = new List<(string Name, Func<Exception> Make)>
        {
            ("cancellation", () => new OperationCanceledException("publication stop")),
            ("interruption", () => new ThreadInterruptedException("publication stop")),
            ("ordinary error", () => new InvalidOperationException("publication diagnostic failure")),
        };
        foreach (var signal in signals)
        {
            cases.Add(("known-setting invalidation followed by " + signal.Name,
                f => { f.Origin.MasterEntry = 90; f.Observe(() => f.Settings.UseFlightPaths = false, signal.Make()); }));
            cases.Add(("Update-setting invalidation followed by " + signal.Name,
                f => f.Observe(() => f.Settings.UseFlightPaths = false, signal.Make())));
            cases.Add(("merchant movement followed by " + signal.Name,
                f => f.Observe(() => f.MutateMerchant(), signal.Make())));
            cases.Add(("modern log-event invalidation followed by " + signal.Name,
                f => f.Observe(() => f.Settings.UseFlightPaths = false, signal.Make(), modern: true)));
            cases.Add(("nested flight survives subsequent " + signal.Name,
                f => f.Observe(f.PublishReplacement, signal.Make(), preserve: true)));
            cases.Add(("replacement service survives subsequent " + signal.Name,
                f => f.Observe(() => BotPoi.Current = new BotPoi(new WoWPoint(7, 8, 9), PoiType.Repair), signal.Make(), preserve: true)));
        }
        cases.Add(("first cancellation survives an ordinary cleanup diagnostic error",
            f => f.Observe(() => f.Settings.UseFlightPaths = false, new OperationCanceledException("first stop"),
                cleanupSignal: new InvalidOperationException("cleanup diagnostic"))));
        cases.Add(("first interruption survives a later cleanup cancellation",
            f => f.Observe(() => f.Settings.UseFlightPaths = false, new ThreadInterruptedException("first stop"),
                cleanupSignal: new OperationCanceledException("later stop"))));
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { using var fixture = new Fixture(); item.Test(fixture); passed++; Console.WriteLine("PASS flight publication exception: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL flight publication exception assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR flight publication exception fixture/owner: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Flight publication-exception scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual publication/logger owners; unchanged test-process fixture; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException($"Flight publication-exception regressions: assertions={assertions}; unexpected={unexpected}");
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
        internal XmlFlightNode Origin => (XmlFlightNode)Get(source, "Origin");
        internal CharacterSettings Settings => (CharacterSettings)Get(source, "Settings");
        internal Fixture()
        {
            source = Activator.CreateInstance(typeof(FlightPathPublicationRevocationRegressionTests)
                .GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
        }
        internal void Stable(bool known) => Invoke("Stable", known);
        internal void MutateMerchant() => Invoke("Mutate", "MoveMerchant", Array.Empty<object>());
        internal void PublishReplacement() => Invoke("PublishReplacement");

        internal void Observe(Action change, Exception signal, bool modern = false, bool preserve = false, Exception? cleanupSignal = null)
        {
            bool priorNeed = FlightPaths.NeedFlightPath;
            int publications = 0, cleanups = 0; bool cleanupDetached = false;
            Exception? fixtureError = null; Intent? replacement = null;
            Action<LogLevel, string>? legacyHandler = null, cleanupHandler = null;
            Logging.LogMessageDelegate? modernHandler = null;
            void RemovePublicationHandlers()
            {
                Logging.OnMessageLogged -= legacyHandler;
                Logging.OnLogMessage -= modernHandler;
            }
            void AtPublication()
            {
                RemovePublicationHandlers(); publications++;
                try { change(); if (preserve) replacement = new Intent(); }
                catch (Exception error) { fixtureError = error; throw; }
                ExceptionDispatchInfo.Capture(signal).Throw();
            }
            legacyHandler = (_, message) =>
            {
                if (message.Contains("Changed POI to:", StringComparison.Ordinal) && BotPoi.Current.Type == PoiType.Fly)
                    AtPublication();
            };
            modernHandler = messages =>
            {
                if (messages.Any(m => m.Message.Contains("Changed POI to:", StringComparison.Ordinal)) && BotPoi.Current.Type == PoiType.Fly)
                    AtPublication();
            };
            cleanupHandler = (_, message) =>
            {
                if (publications != 1 || !message.Contains("Cleared POI", StringComparison.Ordinal) || BotPoi.Current.Type != PoiType.None) return;
                Logging.OnMessageLogged -= cleanupHandler; cleanups++; cleanupDetached = Revoked;
                if (cleanupSignal != null) ExceptionDispatchInfo.Capture(cleanupSignal).Throw();
            };
            if (modern) Logging.OnLogMessage += modernHandler; else Logging.OnMessageLogged += legacyHandler;
            Logging.OnMessageLogged += cleanupHandler;
            bool result = false; Exception? observed = null;
            var start = WoWPoint.Empty; var end = WoWPoint.Empty;
            try { result = FlightPaths.SetFlightPathUsage(From, To, out start, out end); }
            catch (Exception error) { observed = error; }
            finally { RemovePublicationHandlers(); Logging.OnMessageLogged -= cleanupHandler; }
            if (fixtureError != null) ExceptionDispatchInfo.Capture(fixtureError).Throw();
            Check(publications == 1, "actual publication log was not reached exactly once");
            Check(ReferenceEquals(observed, signal), "original diagnostic/stop exception was swallowed, wrapped, or replaced");
            Check(!result && Empty(start) && Empty(end), "exceptional publication returned successful endpoints");
            Check(FlightPaths.NeedFlightPath == priorNeed, "unrelated NeedFlightPath state changed");
            if (preserve)
                Check(replacement != null && replacement.Retained && cleanups == 0, "old failing admission damaged replacement ownership");
            else
                Check(Revoked, "invalidated flight intent remained published after its diagnostic threw");
            if (cleanupSignal != null)
                Check(cleanups == 1 && cleanupDetached, "cleanup diagnostic was not reached after detaching the owned invalid intent");
        }
        private void Invoke(string name, params object[] args)
        {
            try { source.GetType().GetMethod(name, Hidden)!.Invoke(source, args); }
            catch (TargetInvocationException error) when (error.InnerException != null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); }
        }
        public void Dispose() => ((IDisposable)source).Dispose();
    }
    private static object Get(object source, string name) => source.GetType().GetField(name, Hidden)?.GetValue(source)
        ?? source.GetType().GetProperty(name, Hidden)?.GetValue(source) ?? throw new InvalidOperationException("Missing retained fixture member: " + name);
    private static bool Revoked => FlightPaths.TakingPathFrom == null && FlightPaths.TakingPathTo == null
        && FlightPaths.Reason == FlightPathReason.None && BotPoi.Current.Type == PoiType.None;
    private static bool Empty(WoWPoint p) => float.IsNaN(p.X) && float.IsNaN(p.Y) && float.IsNaN(p.Z);
    private static void Check(bool condition, string message) { if (!condition) throw new AssertionFailure(message); }
}
