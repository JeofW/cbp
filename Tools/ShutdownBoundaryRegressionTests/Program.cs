using System.Collections.Concurrent;
using System.Reflection;
using Styx;
using Styx.Helpers;
using Styx.Logic.BehaviorTree;
using Styx.Logic.POI;
using Styx.WoWInternals;
using TreeSharp;
using Call = System.Action;

if (ObjectManager.Me != null || ObjectManager.Wow != null || ObjectManager.Executor != null)
    throw new InvalidOperationException("Shutdown fixtures require an unattached process.");
var cases = new (string Name, System.Action<Fixture> Run)[]
{
    ("cancellation before worker entry still releases initialized bot and tree", f =>
    {
        f.Set(Thread.CurrentThread, TreeRootState.Stopping);
        Fixture.WorkerBody();
        Check(TreeRoot.State == TreeRootState.Stopped && f.Bot.Stops == 1 && f.Bot.Tree.Stops == 1
            && f.Stopped == 1 && f.Started == 0, "pre-entry cancellation must retain exactly one teardown owner");
    }),
    ("stale worker entry cannot stop a different live owner", f =>
    {
        var old = f.Hold(); f.Set(old, TreeRootState.Running);
        Fixture.WorkerBody();
        Check(TreeRoot.State == TreeRootState.Running && ReferenceEquals(Fixture.Worker.GetValue(null), old)
            && f.Bot.Stops == 0 && f.Stopped == 0, "a stale entry must not publish Stopped or clean another owner's state");
    }),
    ("interrupted entry lock still tears down the initialized run", f =>
    {
        Thread? worker = null;
        using var entered = new ManualResetEventSlim();
        lock (Fixture.StateLock)
        {
            worker = f.Spawn(() => { entered.Set(); Fixture.WorkerBody(); }, start: false);
            f.Set(worker, TreeRootState.Starting); worker.Start();
            Check(entered.Wait(5000), "entry thread must start");
            Check(SpinWait.SpinUntil(() => (worker.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0, 5000), "entry must reach the managed state lock");
            Fixture.State.SetValue(null, TreeRootState.Stopping);
            worker.Interrupt();
        }
        Check(worker.Join(5000) && f.Errors.IsEmpty && f.Bot.Stops == 1 && f.Stopped == 1
            && TreeRoot.State == TreeRootState.Stopped, "interrupting initial lock acquisition must not escape worker cleanup");
    }),
    ("throwing stop subscriber cannot prevent the wakeup", f =>
    {
        using var waiting = new ManualResetEventSlim();
        using var woke = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var thread = f.Spawn(() => { waiting.Set(); try { release.Wait(); } catch (ThreadInterruptedException) { woke.Set(); } });
        Check(waiting.Wait(5000), "fixture wait must start");
        f.Set(thread, TreeRootState.Running);
        var expected = new InvalidOperationException("stop subscriber fixture");
        Fixture.StopRequested.SetValue(null, new BotEvents.OnBotStopDelegate(_ => throw expected));
        Exception? caught = null;
        try
        {
            try { TreeRoot.Stop("shutdown fixture"); } catch (Exception error) { caught = error; }
            Check(ReferenceEquals(caught, expected), "retain the existing stop-subscriber exception contract");
            Check(woke.Wait(5000) && thread.Join(5000), "stop must wake the owned managed wait even if a subscriber throws");
        }
        finally { release.Set(); thread.Join(5000); }
    }),
    ("stopping an already exited worker is harmless", f =>
    {
        var thread = f.Spawn(() => { }); Check(thread.Join(5000), "fixture worker must exit");
        f.Set(thread, TreeRootState.Running);
        TreeRoot.Stop("already exited fixture");
        Check(TreeRoot.State == TreeRootState.Stopping, "a dead wakeup target must not turn normal stop into an exception");
    }),
    ("cleanup drains all handlers after an ordinary failure", _ =>
    {
        var seen = new List<int>(); var expected = new InvalidOperationException("cleanup stack fixture");
        var root = new StackProbe(i => { seen.Add(i); if (i == 2) throw expected; });
        root.Start(null!); Check(root.Tick(null!) == RunStatus.Running, "fixture must own cleanup");
        Exception? caught = Capture(() => root.Stop(null!));
        Check(ReferenceEquals(caught, expected) && seen.SequenceEqual(new[] { 2, 1, 0 }), "drain LIFO cleanup without hiding the original failure");
        root.Stop(null!); Check(seen.Count == 3 && root.LastStatus == RunStatus.Failure, "cleanup must be idempotent even after failure");
    }),
    ("cleanup interruption takes precedence without skipping handlers", _ =>
    {
        var seen = new List<int>(); var interrupted = new ThreadInterruptedException("cleanup stop fixture");
        var root = new StackProbe(i => { seen.Add(i); if (i == 2) throw new InvalidOperationException("first cleanup failure"); if (i == 1) throw interrupted; });
        root.Start(null!); root.Tick(null!);
        Check(ReferenceEquals(Capture(() => root.Stop(null!)), interrupted) && seen.SequenceEqual(new[] { 2, 1, 0 }),
            "cancellation must reach the run owner after every owned cleanup was attempted");
    }),
    ("group teardown visits later children after an earlier failure", _ =>
    {
        var seen = new List<int>(); var expected = new InvalidOperationException("child stop fixture");
        var root = new Sequence(Enumerable.Range(0, 3).Select(i => (Composite)new ChildProbe(() => { seen.Add(i); if (i == 0) throw expected; })).ToArray());
        root.Start(null!);
        Check(ReferenceEquals(Capture(() => root.Stop(null!)), expected) && seen.SequenceEqual(new[] { 0, 1, 2 }), "one child must not prevent teardown of its siblings");
        root.Stop(null!); Check(seen.Count == 3, "group cleanup should not replay after failure");
    }),
    ("ordinary successful cleanup preserves LIFO and success", _ =>
    {
        var seen = new List<int>(); var root = new StackProbe(seen.Add);
        root.Start(null!); root.Tick(null!); root.LastStatus = RunStatus.Success; root.Stop(null!);
        Check(seen.SequenceEqual(new[] { 2, 1, 0 }) && root.LastStatus == RunStatus.Success, "successful cleanup contract must remain unchanged");
    })
};
var failures = new List<string>();
foreach (var test in cases)
{
    try { using var fixture = new Fixture(); test.Run(fixture); Console.WriteLine("PASS shutdown: " + test.Name); }
    catch (Exception error) { failures.Add(test.Name + ": " + error.GetType().Name + " " + error.Message); Console.Error.WriteLine("FAIL shutdown: " + failures[^1]); }
}
Console.WriteLine($"Shutdown boundaries: {cases.Length - failures.Count}/{cases.Length}; actual production owners; no game attached.");
return failures.Count == 0 ? 0 : 1;
static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
static Exception? Capture(Call call) { try { call(); return null; } catch (Exception error) { return error; } }

sealed class StackProbe(System.Action<int> cleanup) : Composite
{
    protected override IEnumerable<RunStatus> Execute(object context)
    {
        for (int i = 0; i < 3; i++) CleanupHandlers.Push(new Handler(this, context, i, cleanup));
        yield return RunStatus.Running;
    }
    sealed class Handler(Composite owner, object context, int number, System.Action<int> cleanup) : CleanupHandler(owner, context)
    { protected override void DoCleanup(object context) => cleanup(number); }
}
sealed class ChildProbe(Call stop) : Composite
{
    public override void Stop(object context) { try { stop(); } finally { base.Stop(context); } }
}
sealed class Fixture : IDisposable
{
    const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;
    internal static readonly FieldInfo Worker = typeof(TreeRoot).GetField("_workerThread", Static)!;
    internal static readonly FieldInfo State = typeof(TreeRoot).GetField("<State>k__BackingField", Static)!;
    internal static readonly object StateLock = typeof(TreeRoot).GetField("_stateLock", Static)!.GetValue(null)!;
    internal static readonly FieldInfo StopRequested = typeof(BotEvents).GetField("_onBotStopRequested", Static)!;
    internal static readonly Call WorkerBody = (Call)typeof(TreeRoot).GetMethod("WorkerThread", Static)!.CreateDelegate(typeof(Call));
    readonly FieldInfo _current = typeof(BotManager).GetField("_current", Static)!;
    readonly FieldInfo _started = typeof(BotEvents).GetField("_onBotStarted", Static)!;
    readonly FieldInfo _stopped = typeof(BotEvents).GetField("_onBotStopped", Static)!;
    readonly Dictionary<FieldInfo, object?> _original = new();
    readonly List<Thread> _threads = new();
    readonly List<ManualResetEventSlim> _releases = new();
    readonly BotPoi _poi = BotPoi.Current;
    readonly bool _fileLogging = Logging.FileLogging;
    internal readonly ConcurrentQueue<Exception> Errors = new();
    internal readonly FixtureBot Bot = new();
    internal int Started, Stopped;
    internal Fixture()
    {
        if (Worker.GetValue(null) is Thread active && active.IsAlive) throw new InvalidOperationException("No real worker may run during fixtures.");
        foreach (var field in new[] { Worker, State, StopRequested, _current, _started, _stopped }) _original[field] = field.GetValue(null);
        Set(null, TreeRootState.Stopped); _current.SetValue(null, Bot); StopRequested.SetValue(null, null);
        _started.SetValue(null, new BotEvents.OnBotStartDelegate(_ => Started++));
        _stopped.SetValue(null, new BotEvents.OnBotStopDelegate(_ => Stopped++));
        Logging.FileLogging = false;
    }
    internal void Set(Thread? thread, TreeRootState state) { Worker.SetValue(null, thread); State.SetValue(null, state); }
    internal Thread Spawn(Call body, bool start = true)
    {
        var thread = new Thread(() => { try { body(); } catch (Exception error) { Errors.Enqueue(error); } }) { IsBackground = true };
        _threads.Add(thread); if (start) thread.Start(); return thread;
    }
    internal Thread Hold()
    {
        var release = new ManualResetEventSlim(); _releases.Add(release);
        return Spawn(() => release.Wait());
    }
    public void Dispose()
    {
        foreach (var release in _releases) release.Set();
        foreach (var thread in _threads) if (!thread.Join(5000)) throw new InvalidOperationException("Fixture did not terminate; do not restore live state.");
        foreach (var release in _releases) release.Dispose();
        foreach (var saved in _original) saved.Key.SetValue(null, saved.Value);
        Logging.FileLogging = _fileLogging; BotPoi.Current = _poi;
    }
}
sealed class FixtureBot : BotBase
{
    public override string Name => "Shutdown fixture";
    internal readonly CountingTree Tree = new();
    public override Composite Root => Tree;
    public override PulseFlags PulseFlags => (PulseFlags)0;
    internal int Stops;
    public override void Stop() => Stops++;
}
sealed class CountingTree : Composite
{
    internal int Stops;
    public override void Stop(object context) { Stops++; base.Stop(context); }
}
