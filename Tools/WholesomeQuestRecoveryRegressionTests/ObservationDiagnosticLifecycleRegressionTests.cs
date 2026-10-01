using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using Styx.Helpers;

// Real shared coalescer and Logging event dispatch. Only sink failures and the
// last-emission clock boundary are controlled. No sleep, game, or native dispatch.
internal static class ObservationDiagnosticLifecycleRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static readonly Type Owner = typeof(Logging).Assembly.GetType("Styx.Helpers.ObservationFailureDiagnostics", true)!;
    private static readonly Action<ObservationUnavailableException, string> Report =
        (Action<ObservationUnavailableException, string>)Owner.GetMethod("Report", Hidden)!
            .CreateDelegate(typeof(Action<ObservationUnavailableException, string>));
    private static readonly Action Begin = (Action)Owner.GetMethod("BeginSession", Hidden)!.CreateDelegate(typeof(Action));
    private static readonly Action End = (Action)Owner.GetMethod("EndSession", Hidden)!.CreateDelegate(typeof(Action));
    private sealed class Failure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, Action<Fixture> Test)>
        {
            ("first observation retains owner, reason and timestamps", f =>
            {
                f.Observe(); string line = f.Messages.Single();
                Check(line.Contains("consumer=quest-owner") && line.Contains("count=1 ") && line.Contains("61988")
                    && line.Contains("first=") && line.Contains("last=") && line.Contains("status=UNKNOWN"), "first diagnostic is incomplete");
            }),
            ("one thousand failures emit first and final count", f =>
            {
                for (int i = 0; i < 1000; i++) f.Observe();
                Check(f.Messages.Count == 1, "repeated errors flooded the ordinary interval"); End();
                Check(f.Messages.Count == 2 && f.Messages[1].Contains("count=1000 "), "final suppressed count was lost");
                Check(Token(f.Messages[0], "first") == Token(f.Messages[1], "first"), "first occurrence shifted to the latest failure");
            }),
            ("a new run resets suppression without losing the prior summary", f =>
            {
                f.Observe(); f.Observe(); Begin(); f.Observe();
                Check(f.Messages.Count == 3 && f.Messages[1].Contains("count=2 ") && f.Messages[2].Contains("count=1 "), "run boundaries lost/reset the wrong count");
                Check(Token(f.Messages[0], "session") != Token(f.Messages[2], "session"), "new run retained the old diagnostic identity");
            }),
            ("independent consumers retain independent counts", f =>
            {
                f.Observe(); f.Observe(); f.Observe("singular-owner"); End();
                Check(f.Messages.Count == 3 && f.Messages.Last().Contains("consumer=quest-owner count=2 "), "consumer counts were conflated");
            }),
            ("expiry emits accumulated evidence without waiting", f =>
            {
                f.Observe(); f.Observe();
                object entry = Entries().Values.Cast<object>().Single();
                entry.GetType().GetField("LastEmission", Hidden)!.SetValue(entry, Stopwatch.GetTimestamp() - 31L * Stopwatch.Frequency);
                f.Observe();
                Check(f.Messages.Count == 2 && f.Messages[1].Contains("count=3 "), "expired interval did not retain the accumulated count");
            }),
            ("high-cardinality failures have bounded storage and output", f =>
            {
                for (int i = 0; i < 2000; i++) Report(new ObservationUnavailableException("auras", "metadata-" + i), "owner-" + i);
                Check(Entries().Count <= 65 && f.Messages.Count <= 65, "corrupt IDs caused unbounded first-occurrence storage or logs");
                long observed = Entries().Values.Cast<object>().Sum(entry => (long)entry.GetType().GetField("Count", Hidden)!.GetValue(entry)!);
                Check(observed == 2000, "overflow discarded failures"); End();
                Check(f.Messages.Last().Contains("consumer=overflow") && f.Messages.Last().Contains("owner-1999"), "overflow lost its last owner");
            }),
            ("reentrant log observers cannot deadlock or lose failures", f =>
            {
                bool reentered = false;
                f.Sink = _ => { if (!reentered) { reentered = true; f.Observe(); } };
                f.Observe(); f.Sink = null; End();
                Check(f.Messages.Count == 2 && f.Messages.Last().Contains("count=2 "), "reentrant reporting lost the nested observation");
            }),
            ("ordinary log subscriber failure cannot terminate observation handling", f =>
            {
                f.Sink = _ => throw new InvalidOperationException("controlled broken UI sink"); f.Observe();
                f.Sink = null; f.Observe(); End();
                Check(f.Messages.Last().Contains("count=2 "), "sink failure poisoned subsequent reporting");
            }),
            ("external text cannot create multiline diagnostic storms", f =>
            {
                Report(new ObservationUnavailableException("auras", new string('x', 5000) + "\nforged log"), new string('c', 300));
                Check(f.Messages.Single().Length < 1400 && !f.Messages.Single().Contains('\n'), "untrusted diagnostic text was not bounded");
            })
        };
        foreach (bool wrapped in new[] { false, true })
        foreach (bool interrupted in new[] { false, true })
        {
            bool reflection = wrapped, stop = interrupted;
            cases.Add(($"diagnostic sink preserves cancellation wrapped={reflection} interruption={stop}", f =>
            {
                Exception signal = stop ? new ThreadInterruptedException("controlled sink Stop") : new OperationCanceledException("controlled sink cancellation");
                f.Sink = _ => { if (reflection) throw new TargetInvocationException(signal); throw signal; };
                Exception? observed = null;
                try { f.Observe(); } catch (Exception error) { observed = error; }
                f.Sink = null;
                Check(ReferenceEquals(observed, signal), "diagnostic fallback swallowed or replaced a cancellation signal");
            }));
        }
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var test in cases)
        {
            try { using var fixture = new Fixture(); test.Test(fixture); passed++; }
            catch (Failure error) { assertions++; Console.Error.WriteLine("FAIL observation diagnostics: " + test.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR observation diagnostics: " + test.Name + ": " + error); }
        }
        Console.WriteLine($"Observation diagnostic lifecycle: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual coalescer/logging owners; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Observation diagnostic lifecycle regressions");
    }
    private sealed class Fixture : IDisposable
    {
        private readonly IDisposable world;
        private readonly Logging.LogMessageDelegate listener;
        internal readonly List<string> Messages = new();
        internal Logging.LogMessageDelegate? Sink;
        internal Fixture()
        {
            world = (IDisposable)Activator.CreateInstance(typeof(AuraWorkerLivenessRegressionTests)
                .GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
            Begin();
            listener = batch => { foreach (var row in batch) Messages.Add(row.Message); Sink?.Invoke(batch); };
            Logging.OnLogMessage += listener;
        }
        internal void Observe(string consumer = "quest-owner") => Report(new ObservationUnavailableException("auras", "active aura 61988 metadata unavailable"), consumer);
        public void Dispose() { Sink = null; End(); Logging.OnLogMessage -= listener; world.Dispose(); }
    }
    private static IDictionary Entries() => (IDictionary)Owner.GetField("Entries", Hidden)!.GetValue(null)!;
    private static string Token(string text, string name) => Regex.Match(text, "(?:^| )" + name + "=([^ ]+)").Groups[1].Value;
    private static void Check(bool value, string message) { if (!value) throw new Failure(message); }
}
