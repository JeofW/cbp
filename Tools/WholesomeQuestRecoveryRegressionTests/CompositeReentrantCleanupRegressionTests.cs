using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using TreeSharp;

// Actual Composite.Stop and GroupComposite cleanup across reentrant Start/Tick.
// A stopping lifetime must drain its own registrations, not a callback's replacement.
internal static class CompositeReentrantCleanupRegressionTests
{
    private sealed class AssertionFailure : Exception
    { internal AssertionFailure(string message) : base(message) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Reentrant cleanup tests require Windows x86.");
        var cases = new List<(string Name, System.Action Test)>
        {
            ("ordinary stop retains LIFO cleanup and exactly-once disposal", OrdinaryStop),
            ("handler-created unticked replacement remains startable", () => HandlerReplacement(false, null, null)),
            ("handler-started running replacement retains status and resources", () => HandlerReplacement(true, null, null)),
            ("ordinary old handler error cannot drain a replacement", () => HandlerReplacement(true, new InvalidOperationException("old handler"), null)),
            ("old handler cancellation preserves its identity and the replacement", () => HandlerReplacement(true, new OperationCanceledException("old cancellation"), null)),
            ("old handler interruption preserves its identity and the replacement", () => HandlerReplacement(true, new ThreadInterruptedException("old interruption"), null)),
            ("handler cancellation survives old iterator failure without stopping replacement", () => HandlerReplacement(true, new OperationCanceledException("first signal"), new ApplicationException("old iterator"))),
            ("iterator cancellation supersedes ordinary handler error without stopping replacement", () => HandlerReplacement(true, new InvalidOperationException("old handler"), new OperationCanceledException("iterator cancellation"))),
            ("iterator-created unticked replacement remains startable", () => IteratorReplacement(false)),
            ("iterator-started running replacement remains owned", () => IteratorReplacement(true)),
            ("group replacement retains its children and drains both old children", () => GroupReplacement(null)),
            ("group replacement survives an old child cancellation", () => GroupReplacement(new OperationCanceledException("old child cancellation"))),
            ("terminal replacement cannot drain the remaining old handler early", TerminalReplacement),
        };
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { item.Test(); passed++; Console.WriteLine("PASS reentrant cleanup: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL reentrant cleanup assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR reentrant cleanup fixture/owner: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Reentrant cleanup scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual Composite/GroupComposite lifetimes; no game attached.");
        if (assertions + unexpected != 0)
            throw new InvalidOperationException($"Reentrant cleanup regressions: assertions={assertions}; unexpected={unexpected}");
    }

    private static Exception? StopError(Composite owner, object context)
    { try { owner.Stop(context); return null; } catch (Exception error) { return error; } }

    private static void Begin(Composite owner, object context)
    { owner.Start(context); Check(owner.Tick(context) == RunStatus.Running, "initial owner did not run"); }

    private static void OrdinaryStop()
    {
        var leaf = new Leaf(); var context = new object(); Begin(leaf, context);
        Check(StopError(leaf, context) == null, "ordinary stop failed");
        Check(leaf.Events.SequenceEqual(new[] { "start1", "tick1", "top1", "tail1", "dispose1" }), "ordinary cleanup order changed");
        Check(leaf.LastStatus != RunStatus.Running && leaf.Starts == 1, "ordinary stop retained execution");
        leaf.Stop(context);
        Check(leaf.Top[1] == 1 && leaf.Tail[1] == 1 && leaf.Disposals[1] == 1, "ordinary stop repeated cleanup");
    }

    private static void HandlerReplacement(bool tickReplacement, Exception? handlerError, Exception? iteratorError)
    {
        var leaf = new Leaf { OldDisposeError = iteratorError }; var context = new object();
        leaf.OldTop = () =>
        {
            leaf.Start(context);
            if (tickReplacement) Check(leaf.Tick(context) == RunStatus.Running, "replacement failed to enter Running");
            if (handlerError != null) throw handlerError;
        };
        try
        {
            Begin(leaf, context);
            var actual = StopError(leaf, context);
            var expected = handlerError is OperationCanceledException || handlerError is ThreadInterruptedException
                ? handlerError : iteratorError ?? handlerError;
            Check(ReferenceEquals(actual, expected), "stop error identity changed: " + actual?.GetType().Name);
            VerifyOldAndContinueReplacement(leaf, context, tickReplacement);
        }
        finally { leaf.OldTop = null; StopError(leaf, context); }
    }

    private static void IteratorReplacement(bool tickReplacement)
    {
        var leaf = new Leaf(); var context = new object();
        leaf.OldFinally = () =>
        {
            leaf.Start(context);
            if (tickReplacement) Check(leaf.Tick(context) == RunStatus.Running, "iterator replacement did not run");
        };
        try
        {
            Begin(leaf, context);
            Check(StopError(leaf, context) == null, "iterator replacement stop failed");
            VerifyOldAndContinueReplacement(leaf, context, tickReplacement);
        }
        finally { leaf.OldFinally = null; StopError(leaf, context); }
    }

    private static void VerifyOldAndContinueReplacement(Leaf leaf, object context, bool alreadyTicked)
    {
        // Assert before teardown; neither a later Stop nor restarting can hide loss.
        Check(leaf.Top[1] == 1 && leaf.Tail[1] == 1 && leaf.Disposals[1] == 1, "old lifetime was not drained exactly once");
        Check(leaf.Top[2] == 0 && leaf.Tail[2] == 0 && leaf.Disposals[2] == 0, "old Stop drained replacement resources");
        Check(leaf.Starts == (alreadyTicked ? 2 : 1), "unexpected replacement lifetime count");
        Check(alreadyTicked ? leaf.LastStatus == RunStatus.Running : leaf.LastStatus == null, "old Stop overwrote replacement status");
        Check(leaf.Tick(context) == RunStatus.Running && leaf.Starts == 2 && leaf.Ticks[2] == (alreadyTicked ? 2 : 1), "replacement iterator cannot continue");
        Check(StopError(leaf, context) == null, "replacement explicit Stop failed");
        leaf.Stop(context);
        Check(leaf.Top[2] == 1 && leaf.Tail[2] == 1 && leaf.Disposals[2] == 1, "replacement cleanup was missing or duplicated");
        Check(leaf.Top[1] == 1 && leaf.Tail[1] == 1 && leaf.Disposals[1] == 1, "old cleanup repeated during replacement stop");
    }

    private static void GroupReplacement(Exception? oldError)
    {
        var a = new Leaf(); var b = new Leaf(); var replacement = new Leaf();
        var group = new RunningGroup(a, b); var context = new object();
        a.OldTop = () =>
        {
            group.Children = new List<Composite> { replacement };
            replacement.Parent = group;
            group.Start(context);
            Check(group.Tick(context) == RunStatus.Running, "replacement group did not run");
            if (oldError != null) throw oldError;
        };
        try
        {
            Begin(group, context);
            Check(a.Starts == 1 && b.Starts == 1, "old group children did not run");
            var actual = StopError(group, context);
            Check(ReferenceEquals(actual, oldError), "old group error was replaced");
            Check(a.Disposals[1] == 1 && b.Disposals[1] == 1 && a.Top[1] == 1 && b.Top[1] == 1, "old group skipped a child");
            Check(replacement.Starts == 1 && replacement.Top[1] == 0 && replacement.Disposals[1] == 0, "old group cleanup stopped replacement child");
            Check(group.LastStatus == RunStatus.Running && group.Tick(context) == RunStatus.Running, "old group stop overwrote replacement status");
            Check(group.Starts == 2 && group.Disposals[1] == 1 && group.Disposals[2] == 0, "group iterator ownership changed");
            Check(StopError(group, context) == null, "replacement group Stop failed");
            group.Stop(context);
            Check(replacement.Top[1] == 1 && replacement.Tail[1] == 1 && replacement.Disposals[1] == 1 && group.Disposals[2] == 1, "replacement group cleanup repeated or missing");
        }
        finally { a.OldTop = null; StopError(group, context); StopError(a, context); StopError(b, context); StopError(replacement, context); }
    }

    private static void TerminalReplacement()
    {
        var leaf = new Leaf { SecondStatus = RunStatus.Success }; var context = new object();
        leaf.OldTop = () =>
        {
            leaf.Start(context);
            Check(leaf.Tick(context) == RunStatus.Success, "terminal replacement changed status");
            leaf.Events.Add("returned-from-replacement");
        };
        try
        {
            Begin(leaf, context);
            Check(StopError(leaf, context) == null, "terminal replacement cleanup failed");
            Check(leaf.Events.IndexOf("tail1") > leaf.Events.IndexOf("returned-from-replacement"), "replacement Stop drained an old registration inside the old callback");
            Check(leaf.LastStatus == RunStatus.Success && leaf.Starts == 2, "old Stop lost terminal replacement status");
            Check(leaf.Top[1] == 1 && leaf.Tail[1] == 1 && leaf.Disposals[1] == 1 && leaf.Top[2] == 1 && leaf.Tail[2] == 1 && leaf.Disposals[2] == 1, "terminal lifetime cleanup counts changed");
        }
        finally { leaf.OldTop = null; StopError(leaf, context); }
    }

    private sealed class Leaf : Composite
    {
        internal System.Action? OldTop, OldFinally;
        internal Exception? OldDisposeError;
        internal RunStatus SecondStatus = RunStatus.Running;
        internal int Starts;
        internal readonly int[] Top = new int[4], Tail = new int[4], Disposals = new int[4], Ticks = new int[4];
        internal readonly List<string> Events = new List<string>();
        protected override IEnumerable<RunStatus> Execute(object context)
        {
            int id = ++Starts;
            Check(id < Top.Length, "unexpected extra lifetime");
            Events.Add("start" + id);
            CleanupHandlers.Push(new Handler(this, context, () => { Tail[id]++; Events.Add("tail" + id); }));
            CleanupHandlers.Push(new Handler(this, context, () => { Top[id]++; Events.Add("top" + id); if (id == 1) OldTop?.Invoke(); }));
            try
            {
                while (true) { Ticks[id]++; Events.Add("tick" + id); yield return id == 2 ? SecondStatus : RunStatus.Running; }
            }
            finally
            {
                Disposals[id]++; Events.Add("dispose" + id);
                if (id == 1) { OldFinally?.Invoke(); if (OldDisposeError != null) throw OldDisposeError; }
            }
        }
        private sealed class Handler : CleanupHandler
        {
            private readonly System.Action callback;
            internal Handler(Leaf owner, object context, System.Action callback) : base(owner, context) { this.callback = callback; }
            protected override void DoCleanup(object context) => callback();
        }
    }

    private sealed class RunningGroup : GroupComposite
    {
        internal int Starts;
        internal readonly int[] Disposals = new int[4];
        internal RunningGroup(params Composite[] children) : base(children) { }
        protected override IEnumerable<RunStatus> Execute(object context)
        {
            int id = ++Starts;
            try
            {
                foreach (var child in Children.ToArray()) Begin(child, context);
                while (true) yield return RunStatus.Running;
            }
            finally { Disposals[id]++; }
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new AssertionFailure(message); }
}
