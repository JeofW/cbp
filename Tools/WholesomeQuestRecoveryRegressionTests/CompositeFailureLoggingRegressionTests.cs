using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Styx.Helpers;
using TreeSharp;

// Real Composite.Start/Tick, real Logging event dispatch and registered cleanup.
// Diagnostic callbacks must not prevent cleanup or hide a more authoritative stop.
internal static class CompositeFailureLoggingRegressionTests
{
    private sealed class AssertionFailure : Exception
    { internal AssertionFailure(string message) : base(message) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Failure/logging tests require Windows x86.");
        var cases = new List<(string Name, System.Action Test)>
        {
            ("ordinary tick fault retains Failure and drains cleanup", () => TickCase("ordinary", null, null, null)),
            ("ordinary diagnostic failure cannot skip fault cleanup", () => TickCase("ordinary", "ordinary", null, "log")),
            ("diagnostic cancellation propagates after fault cleanup", () => TickCase("ordinary", "cancel", null, "log")),
            ("diagnostic interruption propagates after fault cleanup", () => TickCase("ordinary", "interrupt", null, "log")),
            ("cleanup cancellation outranks ordinary diagnostic failure", () => TickCase("ordinary", "ordinary", "cancel", "cleanup")),
            ("cleanup interruption outranks ordinary diagnostic failure", () => TickCase("ordinary", "ordinary", "interrupt", "cleanup")),
            ("first diagnostic cancellation survives later cleanup interruption", () => TickCase("ordinary", "cancel", "interrupt", "log")),
            ("first diagnostic interruption survives later cleanup cancellation", () => TickCase("ordinary", "interrupt", "cancel", "log")),
            ("original tick cancellation survives cleanup and diagnostic faults", () => TickCase("cancel", "ordinary", "ordinary", "execute")),
            ("original tick interruption survives later diagnostic cancellation", () => TickCase("interrupt", "cancel", "ordinary", "execute")),
            ("ordinary Start factory fault drains already registered cleanup", () => FactoryCase("ordinary", null, null, "execute")),
            ("Start factory error is not replaced by an ordinary diagnostic fault", () => FactoryCase("ordinary", "ordinary", null, "execute")),
            ("Start diagnostic cancellation propagates after registered cleanup", () => FactoryCase("ordinary", "cancel", null, "log")),
            ("Start cleanup cancellation outranks the ordinary factory error", () => FactoryCase("ordinary", null, "cancel", "cleanup")),
            ("original Start cancellation remains authoritative", () => FactoryCase("cancel", "ordinary", "ordinary", "execute")),
            ("parent does not run fallback after diagnostic cancellation", () => TickCase("ordinary", "cancel", null, "log", true)),
            ("normal Running control does not report a fault or clean prematurely", () => NormalControl(RunStatus.Running)),
            ("normal Success control cleans once without diagnostics", () => NormalControl(RunStatus.Success)),
        };
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { item.Test(); passed++; Console.WriteLine("PASS failure logging: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL failure logging assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR failure logging fixture/owner: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Failure logging scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual Composite and Logging callbacks; no game attached.");
        if (assertions + unexpected != 0)
            throw new InvalidOperationException($"Failure logging regressions: assertions={assertions}; unexpected={unexpected}");
    }

    private static Exception? Error(string? kind) => kind switch
    {
        "ordinary" => new InvalidOperationException("ordinary controlled fault"),
        "cancel" => new OperationCanceledException("controlled cancellation"),
        "interrupt" => new ThreadInterruptedException("controlled interruption"),
        null => null,
        _ => throw new ArgumentException("Unknown fault kind")
    };

    private static void TickCase(string executeKind, string? logKind, string? cleanupKind, string? expectedOwner, bool parent = false)
    {
        var execute = Error(executeKind)!; var log = Error(logKind); var cleanup = Error(cleanupKind);
        var leaf = new FaultLeaf(execute, cleanup, false); int fallbacks = 0;
        Composite owner = parent ? new PrioritySelector(leaf, new TreeSharp.Action(_ => { fallbacks++; return RunStatus.Success; })) : leaf;
        var context = new object();
        using var diagnostic = new DiagnosticScope(log);
        try
        {
            owner.Start(context);
            Check(owner.Tick(context) == RunStatus.Running && leaf.Starts == 1, "running fault setup failed");
            Check(leaf.Handlers == 0 && leaf.Disposals == 0 && diagnostic.Calls == 0, "setup performed premature work");
            RunStatus? result = null; Exception? actual = null;
            try { result = owner.Tick(context); } catch (Exception error) { actual = error; }
            Check(leaf.Attempts == 1 && diagnostic.Calls == 1, "actual fault/diagnostic callback did not run exactly once");
            Check(leaf.Handlers == 1 && leaf.Disposals == 1, "fault reporting skipped or duplicated owned cleanup");
            Check(owner.LastStatus != RunStatus.Running && leaf.LastStatus != RunStatus.Running, "fault retained Running ownership");
            var expected = expectedOwner == "execute" ? execute : expectedOwner == "log" ? log : expectedOwner == "cleanup" ? cleanup : null;
            Check(ReferenceEquals(actual, expected), "fault signal identity was masked by " + actual?.GetType().Name);
            if (expected == null) Check(result == RunStatus.Failure, "ordinary fault no longer returns Failure");
            Check(fallbacks == 0, "parent fallback ran after a stop signal");
            owner.Stop(context); owner.Stop(context);
            Check(leaf.Handlers == 1 && leaf.Disposals == 1, "repeated Stop repeated fault cleanup");
        }
        finally { try { owner.Stop(context); } catch { } }
    }

    private static void FactoryCase(string executeKind, string? logKind, string? cleanupKind, string expectedOwner)
    {
        var execute = Error(executeKind)!; var log = Error(logKind); var cleanup = Error(cleanupKind);
        var leaf = new FaultLeaf(execute, cleanup, true); var context = new object();
        using var diagnostic = new DiagnosticScope(log);
        try
        {
            Exception? actual = null;
            try { leaf.Start(context); } catch (Exception error) { actual = error; }
            Check(leaf.Starts == 1 && leaf.Attempts == 1 && diagnostic.Calls == 1, "actual factory/diagnostic callback was not exercised");
            Check(leaf.Handlers == 1 && leaf.Disposals == 0, "Start failure retained an already registered cleanup owner");
            Check(leaf.LastStatus != RunStatus.Running, "failed Start retained Running state");
            var expected = expectedOwner == "execute" ? execute : expectedOwner == "log" ? log : cleanup;
            Check(ReferenceEquals(actual, expected), "Start fault identity was masked by " + actual?.GetType().Name);
            leaf.Stop(context); leaf.Stop(context);
            Check(leaf.Handlers == 1, "failed factory cleanup repeated");
        }
        finally { try { leaf.Stop(context); } catch { } }
    }

    private static void NormalControl(RunStatus nextStatus)
    {
        var leaf = new FaultLeaf(null, null, false) { NextStatus = nextStatus }; var context = new object();
        using var diagnostic = new DiagnosticScope(new InvalidOperationException("must not report"));
        try
        {
            leaf.Start(context); Check(leaf.Tick(context) == RunStatus.Running, "normal setup failed");
            Check(leaf.Tick(context) == nextStatus && diagnostic.Calls == 0, "normal status reported a fault");
            Check(leaf.Handlers == (nextStatus == RunStatus.Running ? 0 : 1), "normal cleanup timing changed");
            leaf.Stop(context); leaf.Stop(context);
            Check(leaf.Handlers == 1 && leaf.Disposals == 1, "normal cleanup repeated or missing");
        }
        finally { try { leaf.Stop(context); } catch { } }
    }

    private sealed class FaultLeaf : Composite
    {
        private readonly Exception? execute, cleanup;
        private readonly bool factory;
        internal int Starts, Attempts, Handlers, Disposals;
        internal RunStatus NextStatus = RunStatus.Running;
        internal FaultLeaf(Exception? execute, Exception? cleanup, bool factory) { this.execute = execute; this.cleanup = cleanup; this.factory = factory; }
        protected override IEnumerable<RunStatus> Execute(object context)
        {
            Starts++;
            CleanupHandlers.Push(new Handler(this, context));
            if (factory) { Attempts++; throw execute!; }
            return Enumerate();
        }
        private IEnumerable<RunStatus> Enumerate()
        {
            try
            {
                yield return RunStatus.Running;
                Attempts++;
                if (execute != null) throw execute;
                while (true) yield return NextStatus;
            }
            finally { Disposals++; }
        }
        private sealed class Handler : CleanupHandler
        {
            private readonly FaultLeaf leaf;
            internal Handler(FaultLeaf leaf, object context) : base(leaf, context) { this.leaf = leaf; }
            protected override void DoCleanup(object context) { leaf.Handlers++; if (leaf.cleanup != null) throw leaf.cleanup; }
        }
    }

    private sealed class DiagnosticScope : IDisposable
    {
        private readonly FieldInfo field;
        private readonly object? previous;
        private readonly bool fileLogging;
        internal int Calls;
        internal DiagnosticScope(Exception? error)
        {
            field = typeof(Logging).GetField("OnLogMessage", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("Actual logging event backing field missing");
            previous = field.GetValue(null); fileLogging = Logging.FileLogging;
            field.SetValue(null, null); Logging.FileLogging = false;
            Logging.OnLogMessage += _ => { Calls++; if (error != null) throw error; };
        }
        public void Dispose() { field.SetValue(null, previous); Logging.FileLogging = fileLogging; }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new AssertionFailure(message); }
}
