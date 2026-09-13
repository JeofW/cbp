using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Styx;
using Styx.Helpers;
using Styx.Logic.BehaviorTree;
using Styx.Logic.POI;
using Styx.WoWInternals;
using TreeSharp;
using TestAction = System.Action;

internal static class WorkerOwnershipRegressionTests
{
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly FieldInfo Worker = typeof(TreeRoot).GetField("_workerThread", PrivateStatic)!;
    private static readonly FieldInfo State = typeof(TreeRoot).GetField("<State>k__BackingField", PrivateStatic)!;
    private static readonly FieldInfo CurrentBot = typeof(BotManager).GetField("_current", PrivateStatic)!;
    private static readonly FieldInfo Started = typeof(BotEvents).GetField("_onBotStarted", PrivateStatic)!;
    private static readonly FieldInfo Stopped = typeof(BotEvents).GetField("_onBotStopped", PrivateStatic)!;
    private static readonly object StateLock = typeof(TreeRoot).GetField("_stateLock", PrivateStatic)!.GetValue(null)!;
    private static readonly Func<TestAction, string, bool, bool> SafeAction =
        (Func<TestAction, string, bool, bool>)typeof(TreeRoot).GetMethod("SafeAction", PrivateStatic)!
            .CreateDelegate(typeof(Func<TestAction, string, bool, bool>));
    private static readonly TestAction WorkerBody =
        (TestAction)typeof(TreeRoot).GetMethod("WorkerThread", PrivateStatic)!.CreateDelegate(typeof(TestAction));

    [ModuleInitializer]
    internal static void Run()
    {
        // Run thread/lifecycle fixtures once, in the core process, not again during
        // the separate runtime-compiler compatibility process.
        if (Environment.GetCommandLineArgs().Contains("--routine-compatibility")) return;
        if (ObjectManager.Me != null || ObjectManager.Wow != null || ObjectManager.Executor != null)
            throw new InvalidOperationException("Worker ownership tests must not attach to a game.");
        var cases = new (string Name, System.Action<Fixture> Run)[]
        {
            ("status-returning actions preserve interruption", f =>
            {
                var signal = new ThreadInterruptedException("status action");
                Interrupted(() => Tick(new TreeSharp.Action((ActionDelegate)(_ => throw signal))), signal);
                f.NoExceptionLog();
            }),
            ("void actions preserve interruption", f =>
            {
                var signal = new ThreadInterruptedException("void action");
                Interrupted(() => Tick(new TreeSharp.Action((ActionSucceedDelegate)(_ => throw signal))), signal);
                f.NoExceptionLog();
            }),
            ("interrupted priority branches cannot execute fallback", f =>
            {
                int fallback = 0;
                var signal = new ThreadInterruptedException("priority");
                var root = new PrioritySelector(new TreeSharp.Action((ActionDelegate)(_ => throw signal)),
                    new TreeSharp.Action(_ => { fallback++; return RunStatus.Success; }));
                Interrupted(() => Tick(root), signal);
                Check(fallback == 0, "interruption must not be downgraded to a retryable child failure");
                f.NoExceptionLog();
            }),
            ("interrupted sequences cannot execute later actions", f =>
            {
                int later = 0;
                var signal = new ThreadInterruptedException("sequence");
                var root = new Sequence(new TreeSharp.Action((ActionDelegate)(_ => throw signal)),
                    new TreeSharp.Action(_ => { later++; return RunStatus.Success; }));
                Interrupted(() => Tick(root), signal);
                Check(later == 0, "no sequence continuation is authorized after interruption");
                f.NoExceptionLog();
            }),
            ("decorator predicates preserve interruption", f =>
            {
                var signal = new ThreadInterruptedException("predicate");
                Interrupted(() => Tick(new Decorator(_ => throw signal,
                    new TreeSharp.Action(_ => RunStatus.Success))), signal);
                f.NoExceptionLog();
            }),
            ("context changes preserve interruption", f =>
            {
                var signal = new ThreadInterruptedException("context");
                Interrupted(() => Tick(new PrioritySelector((ContextChangeHandler)(_ => throw signal),
                    new TreeSharp.Action(_ => RunStatus.Success))), signal);
                f.NoExceptionLog();
            }),
            ("cleanup runs on interruption and the tree can restart", f =>
            {
                var signal = new ThreadInterruptedException("cleanup");
                bool fail = true;
                var root = new CleanupProbe(() => fail ? throw signal : RunStatus.Success, false);
                Interrupted(() => Tick(root), signal);
                Check(root.Cleanups == 1 && root.LastStatus == RunStatus.Failure, "interrupted action must release its owned cleanup exactly once");
                fail = false;
                Check(Tick(root) == RunStatus.Success && root.Cleanups == 2, "a new Start must not reuse the interrupted iterator");
                f.NoExceptionLog();
            }),
            ("cleanup errors cannot replace the stop signal", f =>
            {
                var signal = new ThreadInterruptedException("primary stop");
                var child = new CleanupProbe(() => throw signal, true);
                int fallback = 0;
                var root = new PrioritySelector(child,
                    new TreeSharp.Action(_ => { fallback++; return RunStatus.Success; }));
                Interrupted(() => Tick(root), signal);
                Check(child.Cleanups == 1 && fallback == 0, "a cleanup error must not turn interruption into permission for another action");
            }),
            ("start-time interruption is not logged as a behavior error", f =>
            {
                var signal = new ThreadInterruptedException("start");
                Interrupted(() => new StartFailure(signal).Start(null!), signal);
                f.NoExceptionLog();
            }),
            ("SafeAction preserves interruption", f =>
            {
                var signal = new ThreadInterruptedException("tick owner");
                Interrupted(() => SafeAction(() => throw signal, "fixture", false), signal);
                f.NoExceptionLog();
            }),
            ("nested interrupted behavior reaches its tick owner", f =>
            {
                var signal = new ThreadInterruptedException("nested");
                int fallback = 0;
                var root = new PrioritySelector(new Sequence(new Decorator(_ => true,
                    new TreeSharp.Action((ActionDelegate)(_ => throw signal)))),
                    new TreeSharp.Action(_ => { fallback++; return RunStatus.Success; }));
                Interrupted(() => SafeAction(() => Tick(root), "nested fixture", false), signal);
                Check(fallback == 0, "all enclosing composites must retain interruption ownership");
                f.NoExceptionLog();
            }),
            ("ordinary behavior errors still permit fallback", f =>
            {
                int fallback = 0;
                var root = new PrioritySelector(new TreeSharp.Action((ActionDelegate)(_ => throw new InvalidOperationException("ordinary fixture"))),
                    new TreeSharp.Action(_ => { fallback++; return RunStatus.Success; }));
                Check(Tick(root) == RunStatus.Success && fallback == 1, "ordinary errors retain existing failure/fallback behavior");
                Check(f.Messages.Any(message => message.Contains("ordinary fixture")), "ordinary errors must remain diagnosable");
            }),
            ("SafeAction retains ordinary error and success contracts", f =>
            {
                Check(!SafeAction(() => throw new InvalidOperationException("safe fixture"), "fixture", false), "ordinary errors must return false");
                Check(SafeAction(() => { }, "fixture", false), "successful actions must return true");
                Check(f.Messages.Any(message => message.Contains("safe fixture")), "ordinary errors must still be logged");
            }),
            ("real managed-wait interruption prevents fallback", RealThreadInterruption),
            ("a timed-out prior worker remains the stopping owner", f =>
            {
                var old = f.HoldWorker();
                f.Set(old.Thread, TreeRootState.Stopping);
                TreeRoot.Start(); // Exercise the real five-second bounded join once.
                Check(old.Thread.IsAlive && ReferenceEquals(Worker.GetValue(null), old.Thread)
                    && TreeRoot.State == TreeRootState.Stopping,
                    "timing out is not proof of termination and must not force Stopped");
                Check(!f.EnteredClientStartup, "a live prior worker must block client startup");
            }),
            ("a stopping worker cannot restart itself", f =>
            {
                f.Set(Thread.CurrentThread, TreeRootState.Stopping);
                TreeRoot.Start();
                Check(TreeRoot.State == TreeRootState.Stopping && !f.EnteredClientStartup,
                    "self-restart must not overwrite the executing worker's state");
            }),
            ("a stale Stopped flag cannot authorize a second live worker", f =>
            {
                var old = f.HoldWorker();
                f.Set(old.Thread, TreeRootState.Stopped);
                TreeRoot.Start();
                Check(!f.EnteredClientStartup && ReferenceEquals(Worker.GetValue(null), old.Thread),
                    "worker liveness is a separate ownership check from the lifecycle enum");
            }),
            ("an exited worker allows normal startup validation", f =>
            {
                var old = f.HoldWorker();
                old.Release.Set();
                Check(old.Thread.Join(5000), "fixture worker must exit");
                f.Set(old.Thread, TreeRootState.Stopping);
                TreeRoot.Start();
                Check(f.EnteredClientStartup && TreeRoot.State == TreeRootState.Stopped,
                    "completed stop should reach normal unavailable-client validation, not remain stuck");
            }),
            ("interruption while joining does not unbalance the state monitor", f =>
            {
                var old = f.HoldWorker();
                f.Set(old.Thread, TreeRootState.Stopping);
                Exception? observed = null;
                var caller = f.Spawn(() => { try { TreeRoot.Start(); } catch (Exception error) { observed = error; } });
                WaitForManagedWait(caller);
                caller.Interrupt();
                Check(caller.Join(5000), "interrupted startup must return");
                Check(observed is ThreadInterruptedException,
                    "a failed join must reacquire the monitor before leaving the lock, preserving the original interruption");
                Check(Monitor.TryEnter(StateLock, 1000), "state monitor must remain usable");
                Monitor.Exit(StateLock);
                Check(TreeRoot.State == TreeRootState.Stopping, "interrupted wait must not publish Stopped");
            }),
            ("a delayed restart cannot clobber a newer owner", f =>
            {
                var old = f.HoldWorker();
                var replacement = f.HoldWorker();
                f.Set(old.Thread, TreeRootState.Stopping);
                Exception? observed = null;
                var caller = f.Spawn(() => { try { TreeRoot.Start(); } catch (Exception error) { observed = error; } });
                WaitForManagedWait(caller);
                lock (StateLock)
                {
                    old.Release.Set();
                    Check(old.Thread.Join(5000), "old fixture worker must exit");
                    f.Set(replacement.Thread, TreeRootState.Running);
                }
                Check(caller.Join(5000) && observed == null, "delayed startup must finish without an error");
                Check(TreeRoot.State == TreeRootState.Running && ReferenceEquals(Worker.GetValue(null), replacement.Thread)
                    && !f.EnteredClientStartup, "reacquisition must recheck identity and state before changing either");
            }),
            ("interrupted started callbacks still execute worker cleanup", f =>
            {
                f.Set(Thread.CurrentThread, TreeRootState.Starting);
                Started.SetValue(null, new BotEvents.OnBotStartDelegate(_ => throw new ThreadInterruptedException("started fixture")));
                WorkerBody();
                Check(TreeRoot.State == TreeRootState.Stopped && f.Bot.Stops == 1 && f.StoppedEvents == 1,
                    "started-event dispatch must be inside the worker try/finally lifecycle");
                f.NoExceptionLog();
            }),
            ("ordinary started-callback errors still execute worker cleanup", f =>
            {
                f.Set(Thread.CurrentThread, TreeRootState.Starting);
                Started.SetValue(null, new BotEvents.OnBotStartDelegate(_ => throw new InvalidOperationException("started ordinary fixture")));
                WorkerBody();
                Check(TreeRoot.State == TreeRootState.Stopped && f.Bot.Stops == 1 && f.StoppedEvents == 1,
                    "a failed started callback must not leave the worker permanently Running");
                Check(f.Messages.Any(message => message.Contains("started ordinary fixture")), "ordinary startup failure must remain visible");
            })
        };
        var failures = new List<string>();
        foreach (var test in cases)
        {
            try
            {
                using var fixture = new Fixture();
                test.Run(fixture);
                Console.WriteLine("PASS worker ownership: " + test.Name);
            }
            catch (Exception error)
            {
                failures.Add(test.Name + ": " + error.GetType().Name + " " + error.Message);
                Console.Error.WriteLine("FAIL worker ownership: " + failures[^1]);
            }
        }
        Console.WriteLine($"Worker ownership scenarios: {cases.Length - failures.Count}/{cases.Length}; real threads and production tree/start methods; no game attached.");
        if (failures.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
    }

    private static void RealThreadInterruption(Fixture fixture)
    {
        using var entered = new ManualResetEventSlim();
        Exception? observed = null;
        int fallback = 0;
        var caller = fixture.Spawn(() =>
        {
            var root = new PrioritySelector(new TreeSharp.Action(_ =>
            {
                entered.Set();
                Thread.Sleep(Timeout.Infinite);
                return RunStatus.Success;
            }), new TreeSharp.Action(_ => { fallback++; return RunStatus.Success; }));
            try { SafeAction(() => Tick(root), "actual interrupted wait", false); }
            catch (Exception error) { observed = error; }
        });
        Check(entered.Wait(5000), "fixture must reach the real managed wait");
        caller.Interrupt();
        Check(caller.Join(5000) && observed is ThreadInterruptedException && fallback == 0,
            "an actual OS-delivered Thread.Interrupt must unwind instead of selecting recovery");
        fixture.NoExceptionLog();
    }
    private static void WaitForManagedWait(Thread thread) => Check(
        SpinWait.SpinUntil(() => (thread.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0, 5000),
        "fixture startup must reach its managed join");
    private static RunStatus Tick(Composite root)
    {
        root.Start(null!);
        try { return root.Tick(null!); }
        finally { root.Stop(null!); }
    }
    private static void Interrupted(TestAction action, ThreadInterruptedException expected)
    {
        try { action(); }
        catch (ThreadInterruptedException error)
        {
            Check(ReferenceEquals(expected, error), "preserve the original stop signal and stack");
            return;
        }
        throw new InvalidOperationException("interruption was swallowed instead of reaching the run owner");
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class StartFailure(Exception signal) : Composite
    {
        protected override IEnumerable<RunStatus> Execute(object context) => throw signal;
    }
    private sealed class CleanupProbe(Func<RunStatus> run, bool throwOnCleanup) : Composite
    {
        internal int Cleanups;
        private readonly bool _throwOnCleanup = throwOnCleanup;
        protected override IEnumerable<RunStatus> Execute(object context)
        {
            CleanupHandlers.Push(new ProbeCleanup(this, context));
            yield return run();
        }
        private sealed class ProbeCleanup(CleanupProbe owner, object context) : CleanupHandler(owner, context)
        {
            protected override void DoCleanup(object context)
            {
                owner.Cleanups++;
                if (owner._throwOnCleanup) throw new InvalidOperationException("cleanup fixture");
            }
        }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly object? _worker = Worker.GetValue(null);
        private readonly object? _state = State.GetValue(null);
        private readonly object? _bot = CurrentBot.GetValue(null);
        private readonly object? _started = Started.GetValue(null);
        private readonly object? _stopped = Stopped.GetValue(null);
        private readonly BotPoi _poi = BotPoi.Current;
        private readonly bool _fileLogging = Logging.FileLogging;
        private readonly Logging.LogMessageDelegate _listener;
        private readonly List<Thread> _threads = new();
        private readonly List<ManualResetEventSlim> _releases = new();
        internal readonly ConcurrentQueue<string> Messages = new();
        internal readonly FixtureBot Bot = new();
        internal int StoppedEvents;
        internal bool EnteredClientStartup => Messages.Any(message => message.Contains("cannot be started while not ingame"));
        internal Fixture()
        {
            if (_worker is Thread thread && thread.IsAlive) throw new InvalidOperationException("No real worker may be active during this fixture.");
            CurrentBot.SetValue(null, Bot);
            Started.SetValue(null, null);
            Stopped.SetValue(null, new BotEvents.OnBotStopDelegate(_ => StoppedEvents++));
            Set(null, TreeRootState.Stopped);
            Logging.FileLogging = false;
            _listener = batch => { foreach (var message in batch) Messages.Enqueue(message.Message); };
            Logging.OnLogMessage += _listener;
        }
        internal void Set(Thread? worker, TreeRootState state) { Worker.SetValue(null, worker); State.SetValue(null, state); }
        internal Thread Spawn(TestAction body)
        {
            var thread = new Thread(() => body()) { IsBackground = true };
            _threads.Add(thread); thread.Start(); return thread;
        }
        internal (Thread Thread, ManualResetEventSlim Release) HoldWorker()
        {
            var release = new ManualResetEventSlim();
            _releases.Add(release);
            using var entered = new ManualResetEventSlim();
            var thread = Spawn(() => { entered.Set(); release.Wait(); });
            Check(entered.Wait(5000), "fixture worker must start");
            return (thread, release);
        }
        internal void NoExceptionLog() => Check(!Messages.Any(message => message.Contains("Exception", StringComparison.OrdinalIgnoreCase)),
            "normal stop interruption must not be logged as an ordinary failure");
        public void Dispose()
        {
            foreach (var release in _releases) release.Set();
            foreach (var thread in _threads)
                if (!thread.Join(6000)) throw new InvalidOperationException("Fixture thread did not terminate; do not restore shared state under it.");
            foreach (var release in _releases) release.Dispose();
            Logging.OnLogMessage -= _listener;
            Logging.FileLogging = _fileLogging;
            Worker.SetValue(null, _worker); State.SetValue(null, _state); CurrentBot.SetValue(null, _bot);
            Started.SetValue(null, _started); Stopped.SetValue(null, _stopped);
            BotPoi.Current = _poi;
        }
    }
    private sealed class FixtureBot : BotBase
    {
        public override string Name => "Worker ownership fixture";
        public override Composite Root { get; } = new TreeSharp.Action(_ => RunStatus.Success);
        public override PulseFlags PulseFlags => (PulseFlags)0;
        internal int Stops;
        public override void Stop() => Stops++;
    }
}
