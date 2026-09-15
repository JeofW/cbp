using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using TreeSharp;

// Actual Composite and GroupComposite cleanup, including iterator-finally errors.
// These are stop-boundary tests, not claims of native movement or game acceptance.
internal static class QuestCleanupSignalRegressionTests
{
    private sealed class AssertionFailure : Exception
    { internal AssertionFailure(string message) : base(message) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Cleanup owner tests require Windows x86.");
        var cases = new List<(string Name, System.Action Test)>
        {
            ("one cancellation cleanup preserves the exact signal", () => LeafCase(new OperationCanceledException("cancel"), null)),
            ("ordinary handler before cancellation cannot mask the stop signal", () => HandlerOrder(false, false)),
            ("cancellation handler before ordinary failure preserves the stop signal", () => HandlerOrder(true, false)),
            ("ordinary handler before interruption preserves the stop signal", () => HandlerOrder(false, true)),
            ("cancellation cleanup survives an ordinary iterator disposal failure", () => DisposalOrder(false, false)),
            ("interruption cleanup survives an ordinary iterator disposal failure", () => DisposalOrder(false, true)),
            ("iterator cancellation supersedes an ordinary handler failure", () => DisposalOrder(true, false)),
            ("iterator interruption supersedes an ordinary handler failure", () => DisposalOrder(true, true)),
            ("ordinary sibling before cancellation cannot mask the stop signal", () => SiblingOrder(false, false)),
            ("cancellation sibling before ordinary failure preserves the stop signal", () => SiblingOrder(true, false)),
            ("ordinary sibling before interruption preserves the stop signal", () => SiblingOrder(false, true)),
            ("first cancellation remains authoritative before a later interruption", () => TwoSignals(false)),
            ("first interruption remains authoritative before a later cancellation", () => TwoSignals(true)),
            ("ordinary cleanup errors retain their first error and drain every owner", () => OrdinaryControl()),
        };
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { item.Test(); passed++; Console.WriteLine("PASS cleanup signal: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL cleanup signal assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR cleanup signal fixture/owner: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Cleanup signal scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual Composite/GroupComposite stop and iterator disposal; no game attached.");
        if (assertions + unexpected != 0)
            throw new InvalidOperationException($"Cleanup signal regressions: assertions={assertions}; unexpected={unexpected}");
    }

    private static Exception Signal(bool interruption) => interruption
        ? new ThreadInterruptedException("original interruption")
        : new OperationCanceledException("original cancellation");

    private static void LeafCase(Exception signal, Exception? dispose)
    {
        var leaf = new CleanupLeaf(new Exception?[] { signal }, dispose);
        VerifyStop(leaf, signal, leaf);
    }

    private static void HandlerOrder(bool signalFirst, bool interruption)
    {
        var signal = Signal(interruption); var ordinary = new InvalidOperationException("ordinary handler");
        var leaf = new CleanupLeaf(signalFirst ? new[] { signal, ordinary } : new[] { ordinary, signal });
        VerifyStop(leaf, signal, leaf);
    }

    private static void DisposalOrder(bool signalInDispose, bool interruption)
    {
        var signal = Signal(interruption); var ordinary = new InvalidOperationException("ordinary cleanup");
        var leaf = new CleanupLeaf(new[] { signalInDispose ? ordinary : signal }, signalInDispose ? signal : ordinary);
        VerifyStop(leaf, signal, leaf);
    }

    private static void SiblingOrder(bool signalFirst, bool interruption)
    {
        var signal = Signal(interruption); var ordinary = new InvalidOperationException("ordinary sibling");
        var a = new CleanupLeaf(new[] { signalFirst ? signal : ordinary });
        var b = new CleanupLeaf(new[] { signalFirst ? ordinary : signal });
        var last = new CleanupLeaf(new Exception?[] { null });
        VerifyStop(new RunningGroup(a, b, last), signal, a, b, last);
    }

    private static void TwoSignals(bool interruptionFirst)
    {
        var first = Signal(interruptionFirst); var second = Signal(!interruptionFirst);
        var a = new CleanupLeaf(new[] { first }); var b = new CleanupLeaf(new[] { second });
        VerifyStop(new RunningGroup(a, b), first, a, b);
    }

    private static void OrdinaryControl()
    {
        var first = new InvalidOperationException("first ordinary");
        var a = new CleanupLeaf(new Exception?[] { first, new ApplicationException("second ordinary"), null });
        VerifyStop(a, first, a);
    }

    private static void VerifyStop(Composite root, Exception expected, params CleanupLeaf[] leaves)
    {
        var context = new object(); Exception? actual = null;
        root.Start(context);
        Check(root.Tick(context) == RunStatus.Running && leaves.All(x => x.Starts == 1), "running owner setup failed");
        try { root.Stop(context); } catch (Exception error) { actual = error; }
        // All counters are checked before the idempotency check or teardown.
        Check(leaves.All(x => x.HandlersRun == x.HandlerCount && x.Disposals == 1), "cleanup short-circuited or disposed an owner more than once");
        Check(root.LastStatus != RunStatus.Running, "stopped root retained Running status");
        root.Stop(context);
        Check(leaves.All(x => x.HandlersRun == x.HandlerCount && x.Disposals == 1), "repeated Stop repeated cleanup");
        Check(ReferenceEquals(actual, expected), "original cleanup signal was masked/replaced by " + actual?.GetType().Name);
    }

    private sealed class CleanupLeaf : Composite
    {
        private readonly Exception?[] handlers;
        private readonly Exception? dispose;
        internal int Starts, HandlersRun, Disposals;
        internal int HandlerCount => handlers.Length;
        internal CleanupLeaf(Exception?[] handlers, Exception? dispose = null) { this.handlers = handlers; this.dispose = dispose; }
        protected override IEnumerable<RunStatus> Execute(object context)
        {
            Starts++;
            for (int i = handlers.Length - 1; i >= 0; i--)
                CleanupHandlers.Push(new Handler(this, context, handlers[i]));
            try { while (true) yield return RunStatus.Running; }
            finally { Disposals++; if (dispose != null) throw dispose; }
        }
        private sealed class Handler : CleanupHandler
        {
            private readonly CleanupLeaf leaf;
            private readonly Exception? error;
            internal Handler(CleanupLeaf leaf, object context, Exception? error) : base(leaf, context) { this.leaf = leaf; this.error = error; }
            protected override void DoCleanup(object context) { leaf.HandlersRun++; if (error != null) throw error; }
        }
    }

    private sealed class RunningGroup : GroupComposite
    {
        internal RunningGroup(params Composite[] children) : base(children) { }
        protected override IEnumerable<RunStatus> Execute(object context)
        {
            foreach (var child in Children) { child.Start(context); Check(child.Tick(context) == RunStatus.Running, "child did not start"); }
            while (true) yield return RunStatus.Running;
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new AssertionFailure(message); }
}
