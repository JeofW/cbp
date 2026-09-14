using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Bots.Quest.Actions;
using Bots.Quest.QuestOrder;
using Styx.Logic.Profiles.Quest;
using TreeSharp;
using Action = System.Action;
using QuestOrder = Bots.Quest.QuestOrder.QuestOrder;

// Actual ForcedBehaviorExecutor and TreeSharp iterators, with a controlled terminal
// ForcedBehavior. This group does not simulate game actions or claim live acceptance.
// All lifetime assertions run BEFORE fixture teardown explicitly stops test leaves.
internal static class QuestExecutorOwnershipRegressionTests
{
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string text) : base(text) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Executor ownership tests require Windows x86.");
        var tests = new List<(string Name, Action Test)>
        {
            ("unchanged running owner continues without premature cleanup", () => With(c =>
            { c.Start(); Check(c.Tick() == RunStatus.Running && c.Body.Ticks == 2 && c.Body.Cleanups == 0, "fresh owner did not continue"); })),
            ("executor stop drains its unregistered nested branch once", () => With(c =>
            { c.Start(); c.Executor.Stop(c.Context); c.Executor.Stop(c.Context); Check(c.Body.Cleanups == 1, "nested cleanup was skipped or repeated"); })),
            ("parent selector stop reaches the actual nested branch", () => With(c =>
            { c.UseParent(); c.Start(); c.Root.Stop(c.Context); Check(c.Body.Cleanups == 1, "parent stop orphaned the forced branch"); })),
            ("running deferral stops effects and yields a terminal cycle", () => With(c =>
            {
                c.Start(); c.Behavior.Deferred = true;
                Check(c.Tick() == RunStatus.Failure && c.Body.Ticks == 1 && c.Body.Cleanups == 1,
                    "deferred continuation ticked or retained its running branch");
                Check(c.Order.Nodes.Count == 1 && ReferenceEquals(c.Order.CurrentBehavior, c.Behavior)
                    && c.Behavior.Disposals == 0, "temporary deferral advanced or disposed the retained behavior");
            })),
            ("initial deferral yields without starting an effect", () => With(c =>
            {
                c.Behavior.Deferred = true; c.Root.Start(c.Context);
                Check(c.Tick() == RunStatus.Failure && c.Body.Starts == 0 && c.Body.Ticks == 0,
                    "initial deferral retained an endless running selection");
            })),
            ("completion during a running branch stops effects and advances once", () => With(c =>
            {
                c.Start(); c.Behavior.Done = true; c.Tick();
                Check(c.Body.Ticks == 1 && c.Body.Cleanups == 1, "completed owner ticked its old effect");
                c.Root.Stop(c.Context); c.Root.Start(c.Context); c.Tick();
                Check(c.Order.Nodes.Count == 0 && c.Behavior.Disposals == 1, "completed owner was not advanced exactly once");
            })),
            ("between-tick replacement is not ticked or stopped by old owner", () => With(c =>
            {
                c.Start(); var next = c.Replacement(); c.Order.CurrentBehavior = next;
                Check(c.Tick() == RunStatus.Failure && c.Body.Ticks == 1 && c.Body.Cleanups == 1,
                    "old branch lifetime was not closed on replacement");
                Untouched(next); Check(ReferenceEquals(c.Order.CurrentBehavior, next), "replacement owner was overwritten");
            })),
            ("OnTick replacement cannot redirect branch start", () => With(c =>
            {
                var next = c.Replacement(); c.Behavior.OnTickAction = () => c.Order.CurrentBehavior = next;
                c.Root.Start(c.Context); Check(c.Tick() == RunStatus.Failure, "obsolete OnTick continued");
                Untouched(next); Check(c.Body.Starts == 0, "obsolete body started after replacement");
            })),
            ("replacement inside branch tick stops only the original branch", () => With(c =>
            {
                var next = c.Replacement(); c.Body.Effect = () => c.Order.CurrentBehavior = next;
                c.Root.Start(c.Context); Check(c.Tick() == RunStatus.Failure && c.Body.Ticks == 1 && c.Body.Cleanups == 1,
                    "reentrant replacement retained the obsolete branch");
                Untouched(next);
            })),
            ("replacement inside old cleanup is not stopped or used for status", () => With(c =>
            {
                c.Start(); var next = c.Replacement(); c.Body.OnCleanup = () => c.Order.CurrentBehavior = next;
                c.Body.Finish = true;
                Check(c.Tick() == RunStatus.Failure && c.Body.Cleanups == 1,
                    "post-cleanup replacement was not recognized as a changed owner");
                Untouched(next); Check(ReferenceEquals(c.Order.CurrentBehavior, next), "cleanup replacement was lost");
            })),
            ("deferral observation replacement does not call replacement OnTick", () => With(c =>
            {
                var next = c.Replacement(); c.Behavior.ObserveDeferral = () => c.Order.CurrentBehavior = next;
                c.Root.Start(c.Context); Check(c.Tick() == RunStatus.Failure, "obsolete observation retained execution");
                Untouched(next); Check(c.Body.Starts == 0, "obsolete observation started its branch");
            })),
            ("completion disposal cannot clear or advance a replacement order", () => With(c =>
            {
                var next = c.Replacement(); var nodes = new OrderNodeCollection { new CheckpointNode(2) };
                c.Behavior.Done = true;
                c.Behavior.OnDispose = () => { c.Order.CurrentBehavior = next; c.Order.Nodes = nodes; };
                c.Root.Start(c.Context); c.Tick();
                Check(ReferenceEquals(c.Order.CurrentBehavior, next) && ReferenceEquals(c.Order.Nodes, nodes)
                    && nodes.Count == 1 && c.Behavior.Disposals == 1, "old completion mutated replacement state");
                Untouched(next);
            })),
            ("node collection replacement invalidates a running branch", () => With(c =>
            {
                c.Start(); var nodes = new OrderNodeCollection { new CheckpointNode(2) }; c.Order.Nodes = nodes;
                Check(c.Tick() == RunStatus.Failure && c.Body.Ticks == 1 && c.Body.Cleanups == 1
                    && nodes.Count == 1, "old continuation borrowed a replacement node collection");
            })),
            ("executor restart drains the previous iterator before replacing it", () => With(c =>
            {
                c.Start(); c.Executor.Start(c.Context);
                Check(c.Body.Cleanups == 1, "restart abandoned the old nested iterator");
                Check(c.Tick() == RunStatus.Running && c.Body.Starts == 2 && c.Body.Ticks == 2,
                    "restart did not create a new owned branch lifetime");
                c.Executor.Stop(c.Context); Check(c.Body.Cleanups == 2, "restart cleanup was not one per lifetime");
            })),
            ("normal branch success retains status and cleans once", () => With(c =>
            {
                c.Start(); c.Body.Finish = true;
                Check(c.Tick() == RunStatus.Success && c.Body.Ticks == 1 && c.Body.Cleanups == 1
                    && c.Order.Nodes.Count == 1 && c.Behavior.Disposals == 0, "normal success contract changed");
            })),
            ("ordinary branch failure cleans without advancing", () => With(c =>
            {
                c.Body.Fail = true; c.Root.Start(c.Context);
                Check(c.Tick() == RunStatus.Failure && c.Body.Cleanups == 1 && c.Order.Nodes.Count == 1,
                    "ordinary terminal failure changed completion ownership");
            })),
            ("child interruption propagates exactly without fallback", () => Cancellation(new ThreadInterruptedException("owned interruption"), false, false)),
            ("child cancellation propagates exactly without fallback", () => Cancellation(new OperationCanceledException("owned cancellation"), false, false)),
            ("executor OnTick cancellation propagates exactly without fallback", () => With(c =>
            {
                c.UseParent(); var signal = new OperationCanceledException("OnTick cancellation");
                c.Behavior.OnTickAction = () => throw signal; c.Root.Start(c.Context);
                ExpectSignal(() => c.Tick(), signal); Check(c.Fallback.Ticks == 0 && c.Body.Starts == 0,
                    "OnTick cancellation authorized fallback or branch start");
            })),
            ("branch Start cancellation propagates exactly without fallback", () => With(c =>
            {
                c.UseParent(); var signal = new OperationCanceledException("Start cancellation");
                c.Body.OnStartAction = () => throw signal; c.Root.Start(c.Context);
                ExpectSignal(() => c.Tick(), signal); Check(c.Fallback.Ticks == 0, "Start cancellation authorized fallback");
            })),
            ("child cancellation survives an ordinary cleanup failure", () => Cancellation(new OperationCanceledException("cancel with cleanup"), true, false)),
            ("child interruption survives an ordinary cleanup failure", () => Cancellation(new ThreadInterruptedException("interrupt with cleanup"), true, false)),
            ("executor cancellation survives nested branch cleanup failure", () => Cancellation(new OperationCanceledException("defer observation cancellation"), true, true))
        };
        int assertions = 0, unexpected = 0;
        foreach (var test in tests)
        {
            try { test.Test(); Console.WriteLine("PASS executor ownership: " + test.Name); }
            catch (AssertionFailure e) { assertions++; Console.Error.WriteLine("FAIL executor ownership assertion: " + test.Name + ": " + e); }
            catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR executor ownership fixture/owner: " + test.Name + ": " + e); }
        }
        Console.WriteLine($"Executor ownership scenarios: {tests.Count - assertions - unexpected}/{tests.Count}; assertions={assertions}; unexpected={unexpected}; actual executor/iterators; controlled terminal branch; no game attached.");
        if (assertions != 0 || unexpected != 0)
            throw new InvalidOperationException("Executor ownership regressions: assertions=" + assertions + "; unexpected=" + unexpected);
    }

    private static void Cancellation(Exception signal, bool cleanupFailure, bool atObservation) => With(c =>
    {
        c.UseParent(); c.Start();
        if (cleanupFailure) c.Body.OnCleanup = () => throw new InvalidOperationException("controlled cleanup failure");
        if (atObservation) c.Behavior.ObserveDeferral = () => throw signal;
        else c.Body.Effect = () => throw signal;
        ExpectSignal(() => c.Tick(), signal);
        Check(c.Fallback.Ticks == 0 && c.Body.Cleanups == 1, "stop signal authorized fallback or skipped owned cleanup");
    });
    private static void ExpectSignal(Action action, Exception signal)
    {
        Exception? observed = null;
        try { action(); } catch (Exception error) { observed = error; }
        Check(ReferenceEquals(observed, signal), "exact stop signal was swallowed or replaced: " + observed?.GetType().Name);
    }
    private static void Untouched(ObservedBehavior next) => Check(next.Body.Starts == 0 && next.Body.Ticks == 0
        && next.Body.StopCalls == 0 && next.Disposals == 0 && next.OnTicks == 0, "obsolete executor touched replacement behavior");
    private sealed class ObservedBehavior : ForcedBehavior
    {
        internal readonly EffectLeaf Body = new();
        internal bool Deferred, Done;
        internal int Disposals, OnTicks;
        internal Action? OnTickAction, ObserveDeferral, OnDispose;
        public override bool IsDone => Done;
        public override bool IsExecutionDeferred { get { ObserveDeferral?.Invoke(); return Deferred; } }
        public override void OnTick() { OnTicks++; OnTickAction?.Invoke(); }
        public override void Dispose() { Disposals++; OnDispose?.Invoke(); }
        protected override Composite CreateBehavior() => Body;
    }
    private sealed class EffectLeaf : Composite
    {
        internal int Starts, Ticks, StopCalls, Cleanups;
        internal bool Finish, Fail;
        internal Action? Effect, OnCleanup, OnStartAction;
        public override void Start(object context) { Starts++; OnStartAction?.Invoke(); base.Start(context); }
        public override void Stop(object context) { StopCalls++; base.Stop(context); }
        protected override IEnumerable<RunStatus> Execute(object context)
        {
            CleanupHandlers.Push(new CountCleanup(this, context));
            while (!Finish)
            {
                Ticks++; Effect?.Invoke();
                if (Fail) { yield return RunStatus.Failure; yield break; }
                yield return RunStatus.Running;
            }
            yield return RunStatus.Success;
        }
        private sealed class CountCleanup : CleanupHandler
        {
            internal CountCleanup(EffectLeaf owner, object context) : base(owner, context) { }
            protected override void DoCleanup(object context) { var leaf = (EffectLeaf)Owner; leaf.Cleanups++; leaf.OnCleanup?.Invoke(); }
        }
    }
    private sealed class FallbackLeaf : Composite
    {
        internal int Ticks;
        protected override IEnumerable<RunStatus> Execute(object context) { Ticks++; yield return RunStatus.Success; }
    }
    private sealed class Case : IDisposable
    {
        private readonly QuestOrder? previous = QuestOrder.Instance;
        private readonly List<ObservedBehavior> owned = new();
        internal readonly QuestOrder Order;
        internal readonly ForcedBehaviorExecutor Executor;
        internal readonly ObservedBehavior Behavior = new();
        internal readonly FallbackLeaf Fallback = new();
        internal readonly object Context = new();
        internal Composite Root;
        internal EffectLeaf Body => Behavior.Body;
        internal Case()
        {
            Order = new QuestOrder(new OrderNodeCollection { new CheckpointNode(1) });
            Order.CurrentBehavior = Behavior; owned.Add(Behavior);
            Executor = new ForcedBehaviorExecutor(Order); Root = Executor;
        }
        internal ObservedBehavior Replacement() { var behavior = new ObservedBehavior(); owned.Add(behavior); return behavior; }
        internal void UseParent() => Root = new PrioritySelector(Executor, Fallback);
        internal RunStatus Tick() => Root.Tick(Context);
        internal void Start()
        {
            Root.Start(Context); Check(Tick() == RunStatus.Running && Body.Ticks == 1 && Body.Starts == 1,
                "actual executor did not reach its initial controlled effect");
        }
        public void Dispose()
        {
            // Teardown cannot serve as a production cleanup oracle. All assertions above
            // have already executed; disable deliberately throwing callbacks, then restore.
            foreach (var behavior in owned) behavior.Body.OnCleanup = null;
            try { Root.Stop(Context); foreach (var behavior in owned) behavior.Body.Stop(Context); }
            finally { typeof(QuestOrder).GetProperty(nameof(QuestOrder.Instance))!.SetValue(null, previous); }
        }
    }
    private static void With(Action<Case> test) { using var c = new Case(); test(c); }
    private static void Check(bool condition, string message) { if (!condition) throw new AssertionFailure(message); }
}
