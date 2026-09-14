using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using GreenMagic;
using Styx;
using Styx.Logic.Pathing;
using Styx.Logic.Questing.Recovery;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
using WholesomeAQ;

// Test-only controlled external observations. QuestLog, Memory, ScanAndRefresh,
// DoScan, RefreshGate and the already-running execution gate are production owners.
// No game process is opened. No native executor or inventory mutation is used.
internal static class QuestScanFailureRegressionTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private const string PriorPath = "controlled-prior-scan-failure-profile.xml";
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string message) : base(message) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Scan failure owner tests require Windows x86.");
        var tests = new List<(string Name, System.Action Test)>
        {
            ("controlled host reads a valid owner and a genuinely empty raw log", () => WithWorld(player =>
            {
                Check(StyxWoW.IsInWorld && player.IsValid, "controlled owner is not valid at the actual world boundary");
                Check(player.QuestLog.GetAllQuests().Count == 0, "actual empty slot enumeration changed");
                for (uint slot = 0; slot < 25; slot++) Check(player.QuestLog.GetQuestId(slot) == 0, "fixture slot is not empty");
            })),
            ("direct scan preserves the actual identity-read exception", () => WithWorld(player =>
            {
                var scheduler = Scheduler(); Seed(scheduler);
                var error = new InvalidOperationException("controlled identity observation failed"); player.NameFailure = error;
                SameException(() => scheduler.ScanAndRefresh(player), error, "QuestRecoveryRuntime.EnsureConfigured");
            })),
            ("identity-read failure revokes prior permission and preserves item protection", () => WithWorld(player =>
            {
                var scheduler = Scheduler(); Seed(scheduler);
                var error = new InvalidOperationException("controlled identity observation failed"); player.NameFailure = error;
                SameException(() => scheduler.ScanAndRefresh(player), error, "QuestRecoveryRuntime.EnsureConfigured");
                Idle(scheduler);
                Check(scheduler.ActiveQuestIds.Contains(867), "failed scan released scheduled-item sale protection");
            })),
            ("failure after actual quest-log enumeration revokes prior publication", () => WithWorld(player =>
            {
                var scheduler = Scheduler(); Seed(scheduler);
                var error = new InvalidOperationException("controlled position observation failed"); player.PositionFailure = error;
                SameException(() => scheduler.ScanAndRefresh(player), error, "QuestScheduler.ScanAndRefresh");
                Check(player.PositionReads > 0, "scan did not reach the post-log position boundary"); Idle(scheduler);
            })),
            ("direct scan interruption propagates and revokes permission", () => WithWorld(player =>
            {
                var scheduler = Scheduler(); Seed(scheduler);
                var error = new ThreadInterruptedException("controlled scan interruption"); player.NameFailure = error;
                SameException(() => scheduler.ScanAndRefresh(player), error, "QuestRecoveryRuntime.EnsureConfigured"); Idle(scheduler);
            })),
            ("direct scan cancellation propagates and revokes permission", () => WithWorld(player =>
            {
                var scheduler = Scheduler(); Seed(scheduler);
                var error = new OperationCanceledException("controlled scan cancellation"); player.NameFailure = error;
                SameException(() => scheduler.ScanAndRefresh(player), error, "QuestRecoveryRuntime.EnsureConfigured"); Idle(scheduler);
            })),
            ("failed actual scan stops an already executing selected plan", () => WithWorld(player =>
            {
                var scheduler = Scheduler(); Seed(scheduler);
                WithRunningRoot(scheduler, (bot, root, child, context) =>
                {
                    var error = new InvalidOperationException("controlled scan failure"); player.NameFailure = error;
                    SameException(() => scheduler.ScanAndRefresh(player), error, "QuestRecoveryRuntime.EnsureConfigured");
                    Stopped(root, child, context); Idle(scheduler);
                });
            })),
            ("failed actual scan stops an already executing grind fallback", () => WithWorld(player =>
            {
                var scheduler = Scheduler(); Seed(scheduler, grind: true);
                WithRunningRoot(scheduler, (bot, root, child, context) =>
                {
                    var error = new InvalidOperationException("controlled scan failure"); player.NameFailure = error;
                    SameException(() => scheduler.ScanAndRefresh(player), error, "QuestRecoveryRuntime.EnsureConfigured");
                    Stopped(root, child, context); Idle(scheduler);
                });
            })),
            ("actual DoScan failure cannot leave its running child authorized", () => WithWorld(player =>
            {
                var scheduler = Scheduler(); Seed(scheduler);
                WithRunningRoot(scheduler, (bot, root, child, context) =>
                {
                    player.NameFailure = new InvalidOperationException("controlled DoScan failure");
                    var lease = CurrentLease(bot); Invoke(bot, "DoScan", scheduler, lease);
                    Check(player.NameReads > 0, "DoScan never entered the actual scan");
                    Stopped(root, child, context); Idle(scheduler);
                });
            })),
            ("actual DoScan does not swallow thread interruption", () => WithWorld(player =>
            {
                var scheduler = Scheduler(); Seed(scheduler); var bot = Bot(scheduler); var lease = CurrentLease(bot);
                var error = new ThreadInterruptedException("controlled DoScan interruption"); player.NameFailure = error;
                SameException(() => Invoke(bot, "DoScan", scheduler, lease), error, "QuestRecoveryRuntime.EnsureConfigured"); Idle(scheduler);
            })),
            ("actual DoScan does not swallow cancellation", () => WithWorld(player =>
            {
                var scheduler = Scheduler(); Seed(scheduler); var bot = Bot(scheduler); var lease = CurrentLease(bot);
                var error = new OperationCanceledException("controlled DoScan cancellation"); player.NameFailure = error;
                SameException(() => Invoke(bot, "DoScan", scheduler, lease), error, "QuestRecoveryRuntime.EnsureConfigured"); Idle(scheduler);
            })),
            ("pending refresh releases its running lease when cancellation propagates", () => WithWorld(player =>
            {
                var scheduler = Scheduler(); Seed(scheduler); var bot = Bot(scheduler); var gate = Gate(bot);
                gate.Start(); Check(gate.TryRequest(), "refresh setup was not accepted");
                var error = new OperationCanceledException("controlled pending refresh cancellation"); player.NameFailure = error;
                SameException(() => Invoke(bot, "RunPendingRefresh"), error, "QuestRecoveryRuntime.EnsureConfigured");
                Check(gate.TryRequest() && gate.Begin().HasValue, "cancelled refresh retained the running lease"); Idle(scheduler);
            })),
            ("failed old refresh does not revoke a replacement generation publication", () => WithWorld(player =>
            {
                var scheduler = Scheduler(); Seed(scheduler);
                WithRunningRoot(scheduler, (bot, root, child, context) =>
                {
                    var stale = CurrentLease(bot); var gate = Gate(bot); QuestScheduleResult? replacement = null;
                    player.BeforeName = () =>
                    {
                        player.BeforeName = null; gate.Stop(); gate.Start();
                        Check(gate.TryRequest() && gate.Begin().HasValue, "replacement refresh could not start");
                        Seed(scheduler); replacement = scheduler.LastSchedule;
                    };
                    player.NameFailure = new InvalidOperationException("old generation failed after replacement");
                    Invoke(bot, "DoScan", scheduler, stale);
                    Check(replacement != null && ReferenceEquals(replacement, scheduler.LastSchedule), "old failure revoked replacement publication");
                    Check(root.Tick(context) == RunStatus.Running && child.Ticks == 2, "old failure stopped replacement execution");
                });
            })),
            ("normal selected plan executes without a failing scan", () => WithWorld(player =>
            {
                var scheduler = Scheduler(); Seed(scheduler);
                WithRunningRoot(scheduler, (bot, root, child, context) =>
                    Check(root.Tick(context) == RunStatus.Running && child.Ticks == 2, "normal selected plan was disabled"));
            }))
        };
        int assertions = 0, unexpected = 0;
        foreach (var test in tests)
        {
            try { test.Test(); Console.WriteLine("PASS scan failure: " + test.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL scan failure assertion: " + test.Name + ": " + error); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR scan failure fixture/owner: " + test.Name + ": " + error); }
        }
        Console.WriteLine($"Scan failure scenarios: {tests.Count - assertions - unexpected}/{tests.Count}; assertions={assertions}; unexpected={unexpected}; actual host/scan/DoScan/execution owners; controlled cached external observations; no game attached.");
        Console.WriteLine("Scan failure loaded native-assembler assemblies: " + string.Join(", ",
            AppDomain.CurrentDomain.GetAssemblies().Where(assembly => assembly.GetName().Name?.Contains("fasm", StringComparison.OrdinalIgnoreCase) == true)
                .Select(assembly => assembly.FullName)));
        if (assertions != 0 || unexpected != 0) throw new InvalidOperationException("Scan failure regressions: assertions=" + assertions + "; unexpected=" + unexpected);
    }

    private static void WithWorld(System.Action<ObservedPlayer> test)
    {
        var previousPlayer = ObjectManager.Me; var previousMemory = ObjectManager.Wow; var previousExecutor = ObjectManager.Executor;
        // Do not JIT Memory's process/assembler constructor in an offline fixture.
        // Only its external read-cache/handle fields are controlled; actual Read methods
        // and all host/quest/scheduler owners below remain unchanged production code.
        var memory = (Memory)RuntimeHelpers.GetUninitializedObject(typeof(Memory));
        var cache = new ThreadLocal<Dictionary<IntPtr, byte[]>>(() => new Dictionary<IntPtr, byte[]>());
        var enabled = new ThreadLocal<bool>(() => false);
        typeof(Memory).GetField("_cache", Instance)!.SetValue(memory, cache);
        typeof(Memory).GetField("_cacheEnabled", Instance)!.SetValue(memory, enabled);
        // Current-thread pseudo-handle is NOT a process handle: an unseeded RPM cannot read a client.
        // Seed only the actual Memory read cache, not QuestLog or scheduler return values.
        typeof(Memory).GetField("_hProcess", Instance)!.SetValue(memory, new IntPtr(-2));
        enabled.Value = true;
        void Bytes(uint address, byte[] bytes) => cache.Value![new IntPtr(unchecked((int)address))] = bytes;
        void UInt(uint address, uint value) => Bytes(address, BitConverter.GetBytes(value));
        Bytes(0xBD0792, new byte[] { 1 }); UInt(0xB6A9E0, 0); UInt(0xBD088C, 1);
        UInt(0x1000 + 0x14, 4); UInt(0x1000 + 0xBC, 0); UInt(0x1000 + 8, 0x2000);
        for (uint slot = 0; slot < 25; slot++) UInt(0x2000 + (158 + slot * 5) * 4, 0);
        var player = new ObservedPlayer();
        try
        {
            typeof(ObjectManager).GetProperty("Wow")!.SetValue(null, memory);
            ObjectManager.Executor = null; ObjectManager.Me = player;
            test(player);
        }
        finally
        {
            ObjectManager.Me = previousPlayer; ObjectManager.Executor = previousExecutor;
            typeof(ObjectManager).GetProperty("Wow")!.SetValue(null, previousMemory);
            typeof(Memory).GetField("_hProcess", Instance)!.SetValue(memory, IntPtr.Zero);
            cache.Dispose(); enabled.Dispose();
        }
    }
    private sealed class ObservedPlayer : LocalPlayer
    {
        internal Exception? NameFailure; internal Exception? PositionFailure; internal System.Action? BeforeName;
        internal int NameReads; internal int PositionReads;
        internal ObservedPlayer() : base(0x1000) { }
        public override string Name { get { NameReads++; BeforeName?.Invoke(); if (NameFailure != null) throw NameFailure; return "W42-Scan-Fixture"; } }
        public override WoWPoint Location { get { PositionReads++; if (PositionFailure != null) throw PositionFailure; return new WoWPoint(10, 10, 10); } }
    }
    private static QuestScheduler Scheduler()
    {
        var loader = new DataLoader(); Set(loader, "_database", new QuestDatabase());
        return new QuestScheduler(loader, new ProfileBuilder(), new WholesomeAQSettings());
    }
    private static void Seed(QuestScheduler scheduler, bool grind = false)
    {
        Set(scheduler, "<LastSchedule>k__BackingField", new QuestScheduleResult
        {
            Selected = grind ? Array.Empty<QuestWorkCandidate>() : new[] { new QuestWorkCandidate { QuestId = 867, Stage = QuestWorkStage.Objective, Recovery = new QuestRecoveryDecision { State = QuestRecoveryState.Eligible, MayAttempt = true } } },
            FallbackMode = grind ? QuestFallbackMode.ValidatedGrind : QuestFallbackMode.None,
            ValidatedGrindProfilePath = grind ? PriorPath : ""
        });
        Set(scheduler, "<CurrentProfilePath>k__BackingField", PriorPath);
        Set(scheduler, "<ActiveQuestIds>k__BackingField", new HashSet<int> { 867 });
        Set(scheduler, "<LastQuestCount>k__BackingField", 1);
    }
    private static WholesomeAutoQuest Bot(QuestScheduler scheduler)
    {
        var bot = new WholesomeAutoQuest(); Set(bot, "_stopped", false); Set(bot, "_initialized", true); Set(bot, "_dataReady", true); Set(bot, "_scheduler", scheduler); return bot;
    }
    private static RefreshGate Gate(WholesomeAutoQuest bot) => (RefreshGate)typeof(WholesomeAutoQuest).GetField("_refreshGate", Instance)!.GetValue(bot)!;
    private static RefreshLease CurrentLease(WholesomeAutoQuest bot)
    {
        var gate = Gate(bot); gate.Start(); Check(gate.TryRequest(), "refresh request setup failed");
        return gate.Begin() ?? throw new AssertionFailure("refresh lease setup failed");
    }
    private static void WithRunningRoot(QuestScheduler scheduler, System.Action<WholesomeAutoQuest, GroupComposite, RunningChild, object> test)
    {
        var bot = Bot(scheduler); var root = (GroupComposite)bot.Root; root.Children.Clear(); var child = new RunningChild(); root.Children.Add(child);
        object context = new object(); root.Start(context);
        try { Check(root.Tick(context) == RunStatus.Running && child.Ticks == 1, "running child setup failed"); test(bot, root, child, context); }
        finally { root.Stop(context); Gate(bot).Stop(); }
    }
    private sealed class RunningChild : Composite
    {
        internal int Ticks; internal int Stops;
        protected override IEnumerable<RunStatus> Execute(object context)
        {
            while (true) { Ticks++; yield return RunStatus.Running; }
        }
        public override void Stop(object context) { Stops++; base.Stop(context); }
    }
    private static void Stopped(GroupComposite root, RunningChild child, object context) =>
        Check(root.Tick(context) == RunStatus.Failure && child.Ticks == 1 && child.Stops > 0, "failed scan authorized another child tick");
    private static void Idle(QuestScheduler scheduler)
    {
        Check(scheduler.LastSchedule.FallbackMode == QuestFallbackMode.TimedIdle && scheduler.LastSchedule.Selected.Count == 0, "failed scan retained execution permission");
        Check(scheduler.CurrentProfilePath == null && scheduler.LastQuestCount == 0, "failed scan retained prior publication");
        Check(scheduler.EarliestRetryUtc.HasValue, "failed scan has no bounded observation retry");
    }
    private static void SameException(System.Action call, Exception expected, string owner)
    {
        Exception? observed = null;
        try { call(); } catch (Exception error) { observed = error; }
        Check(ReferenceEquals(observed, expected), "expected exact production-propagated exception, observed " + observed);
        Check(observed!.StackTrace?.Contains(owner, StringComparison.Ordinal) == true, "expected production owner absent from exception stack: " + observed);
    }
    private static object? Invoke(object owner, string method, params object[] arguments)
    {
        try { return owner.GetType().GetMethod(method, Instance)!.Invoke(owner, arguments); }
        catch (TargetInvocationException error) when (error.InnerException != null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Instance)!.SetValue(owner, value);
    private static void Check(bool condition, string message) { if (!condition) throw new AssertionFailure(message); }
}
