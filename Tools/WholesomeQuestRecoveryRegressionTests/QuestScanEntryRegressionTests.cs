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

// Actual Memory/world, vendor discovery, ScanAndRefresh, DoScan, RefreshGate and
// already-running execution owners. Only external memory/player observations and
// prior publication are controlled. No client, native executor or item mutation.
internal static class QuestScanEntryRegressionTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string PriorPath = "controlled-prior-scan-entry-profile.xml";
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string message) : base(message) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Scan entry owner tests require Windows x86.");
        var tests = new List<(string Name, System.Action Test)>
        {
            ("closed memory is unavailable at the actual world guard before identity capture", () => WithWorld((player, memory) =>
            {
                var scheduler = Scheduler(); Seed(scheduler); CloseMemory(memory);
                WorldUnavailable(scheduler, player);
                Check(player.NameReads == 0, "identity capture ran before the failed world guard");
            })),
            ("direct world-read failure revokes publication but retains item protection", () => WithWorld((player, memory) =>
            {
                var scheduler = Scheduler(); Seed(scheduler); CloseMemory(memory);
                WorldUnavailable(scheduler, player); Idle(scheduler);
                Check(scheduler.ActiveQuestIds.Contains(867), "early failure released conservative item protection");
            })),
            ("direct world-read failure stops an already running selected plan", () => WithWorld((player, memory) =>
            {
                var scheduler = Scheduler(); Seed(scheduler);
                WithRunningRoot(scheduler, (bot, root, child, context) =>
                {
                    CloseMemory(memory); WorldUnavailable(scheduler, player);
                    Stopped(root, child, context); Idle(scheduler);
                });
            })),
            ("direct world-read failure stops an already running grind plan", () => WithWorld((player, memory) =>
            {
                var scheduler = Scheduler(); Seed(scheduler, true);
                WithRunningRoot(scheduler, (bot, root, child, context) =>
                {
                    CloseMemory(memory); WorldUnavailable(scheduler, player);
                    Stopped(root, child, context); Idle(scheduler);
                });
            })),
            ("DoScan world guard failure cannot preserve running authorization", () => WithWorld((player, memory) =>
            {
                var scheduler = Scheduler(); Seed(scheduler);
                WithRunningRoot(scheduler, (bot, root, child, context) =>
                {
                    var lease = CurrentLease(bot); CloseMemory(memory);
                    Check(Equals(Invoke(bot, "DoScan", scheduler, lease), false), "ordinary world failure changed the DoScan return contract");
                    Check(player.NameReads == 0, "failed world guard entered scheduler identity capture");
                    Stopped(root, child, context); Idle(scheduler);
                });
            })),
            ("vendor failure is from actual discovery before scheduler entry", () => WithWorld((player, memory) =>
            {
                var scheduler = Scheduler(); Seed(scheduler); var bot = Bot(scheduler); EnableVendors(bot);
                var error = new InvalidOperationException("controlled vendor position failure"); player.PositionFailure = error;
                Invoke(bot, "DoScan", scheduler, CurrentLease(bot));
                VendorWasReached(player, error);
            })),
            ("ordinary vendor discovery failure stops a running selected plan", () => WithWorld((player, memory) =>
            {
                var scheduler = Scheduler(); Seed(scheduler);
                WithRunningRoot(scheduler, (bot, root, child, context) =>
                {
                    EnableVendors(bot); var error = new InvalidOperationException("controlled vendor failure"); player.PositionFailure = error;
                    Invoke(bot, "DoScan", scheduler, CurrentLease(bot)); VendorWasReached(player, error);
                    Stopped(root, child, context); Idle(scheduler);
                    Check(scheduler.ActiveQuestIds.Contains(867), "vendor failure released item protection");
                });
            })),
            ("ordinary vendor discovery failure stops a running grind plan", () => WithWorld((player, memory) =>
            {
                var scheduler = Scheduler(); Seed(scheduler, true);
                WithRunningRoot(scheduler, (bot, root, child, context) =>
                {
                    EnableVendors(bot); player.PositionFailure = new InvalidOperationException("controlled vendor failure");
                    Invoke(bot, "DoScan", scheduler, CurrentLease(bot)); Stopped(root, child, context); Idle(scheduler);
                });
            })),
            ("vendor interruption propagates unchanged and revokes prior permission", () => WithWorld((player, memory) =>
            {
                var scheduler = Scheduler(); Seed(scheduler); var bot = Bot(scheduler); EnableVendors(bot);
                var error = new ThreadInterruptedException("controlled vendor interruption"); player.PositionFailure = error;
                SameException(() => Invoke(bot, "DoScan", scheduler, CurrentLease(bot)), error, "VendorDataLoader.GetNearestVendors");
                Idle(scheduler);
            })),
            ("vendor cancellation propagates unchanged and revokes prior permission", () => WithWorld((player, memory) =>
            {
                var scheduler = Scheduler(); Seed(scheduler); var bot = Bot(scheduler); EnableVendors(bot);
                var error = new OperationCanceledException("controlled vendor cancellation"); player.PositionFailure = error;
                SameException(() => Invoke(bot, "DoScan", scheduler, CurrentLease(bot)), error, "VendorDataLoader.GetNearestVendors");
                Idle(scheduler);
            })),
            ("pending vendor cancellation releases its lease and leaves execution idle", () => WithWorld((player, memory) =>
            {
                var scheduler = Scheduler(); Seed(scheduler); var bot = Bot(scheduler); EnableVendors(bot); var gate = Gate(bot);
                gate.Start(); Check(gate.TryRequest(), "refresh request setup failed");
                var error = new OperationCanceledException("controlled pending vendor cancellation"); player.PositionFailure = error;
                SameException(() => Invoke(bot, "RunPendingRefresh"), error, "VendorDataLoader.GetNearestVendors");
                Check(gate.TryRequest() && gate.Begin().HasValue, "cancelled vendor discovery retained the running lease");
                Idle(scheduler);
            })),
            ("obsolete lease does not inspect unavailable memory or revoke replacement", () => WithWorld((player, memory) =>
            {
                var scheduler = Scheduler(); Seed(scheduler);
                WithRunningRoot(scheduler, (bot, root, child, context) =>
                {
                    var stale = CurrentLease(bot); var gate = Gate(bot); gate.Stop(); gate.Start();
                    Seed(scheduler); var replacement = scheduler.LastSchedule; CloseMemory(memory);
                    Check(Equals(Invoke(bot, "DoScan", scheduler, stale), false), "obsolete lease unexpectedly requested work");
                    Check(ReferenceEquals(replacement, scheduler.LastSchedule), "obsolete lease revoked replacement authorization");
                    Check(root.Tick(context) == RunStatus.Running && child.Ticks == 2, "obsolete lease disabled replacement execution");
                });
            })),
            ("vendor failure after generation replacement does not revoke the replacement", () => WithWorld((player, memory) =>
            {
                var scheduler = Scheduler(); Seed(scheduler);
                WithRunningRoot(scheduler, (bot, root, child, context) =>
                {
                    EnableVendors(bot); var lease = CurrentLease(bot); var gate = Gate(bot); QuestScheduleResult? replacement = null;
                    player.BeforePosition = () =>
                    {
                        player.BeforePosition = null; gate.Stop(); gate.Start();
                        Check(gate.TryRequest() && gate.Begin().HasValue, "replacement lease setup failed");
                        Seed(scheduler); replacement = scheduler.LastSchedule;
                    };
                    player.PositionFailure = new InvalidOperationException("old vendor lookup failed after replacement");
                    Invoke(bot, "DoScan", scheduler, lease);
                    Check(replacement != null && ReferenceEquals(replacement, scheduler.LastSchedule), "late old failure revoked replacement publication");
                    Check(root.Tick(context) == RunStatus.Running && child.Ticks == 2, "late old failure stopped replacement execution");
                });
            })),
            ("successful vendor observations still enter the actual scheduler", () => WithWorld((player, memory) =>
            {
                var scheduler = Scheduler(); Seed(scheduler); var bot = Bot(scheduler); EnableVendors(bot);
                var error = new OperationCanceledException("controlled identity boundary after vendor reads"); player.NameFailure = error;
                SameException(() => Invoke(bot, "DoScan", scheduler, CurrentLease(bot)), error, "QuestRecoveryRuntime.EnsureConfigured");
                Check(player.PositionReads == 3 && player.NameReads > 0, "normal three vendor lookups did not reach the scheduler");
                Idle(scheduler);
            })),
            ("normal selected work remains executable without a refresh", () => WithWorld((player, memory) =>
            {
                var scheduler = Scheduler(); Seed(scheduler);
                WithRunningRoot(scheduler, (bot, root, child, context) =>
                    Check(root.Tick(context) == RunStatus.Running && child.Ticks == 2, "normal selected work was disabled"));
            }))
        };
        int assertions = 0, unexpected = 0;
        foreach (var test in tests)
        {
            try { test.Test(); Console.WriteLine("PASS scan entry: " + test.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL scan entry assertion: " + test.Name + ": " + error); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR scan entry fixture/owner: " + test.Name + ": " + error); }
        }
        Console.WriteLine($"Scan entry scenarios: {tests.Count - assertions - unexpected}/{tests.Count}; assertions={assertions}; unexpected={unexpected}; actual world/vendor/scan/refresh/execution owners; no game attached.");
        if (assertions != 0 || unexpected != 0) throw new InvalidOperationException("Scan entry regressions: assertions=" + assertions + "; unexpected=" + unexpected);
    }

    private static void WithWorld(System.Action<ObservedPlayer, Memory> test)
    {
        var previousPlayer = ObjectManager.Me; var previousMemory = ObjectManager.Wow; var previousExecutor = ObjectManager.Executor;
        var memory = (Memory)RuntimeHelpers.GetUninitializedObject(typeof(Memory));
        var cache = new ThreadLocal<Dictionary<IntPtr, byte[]>>(() => new Dictionary<IntPtr, byte[]>());
        var enabled = new ThreadLocal<bool>(() => true);
        Set(memory, "_cache", cache); Set(memory, "_cacheEnabled", enabled); Set(memory, "_hProcess", new IntPtr(-2));
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
            Check(StyxWoW.IsInWorld && player.IsValid, "controlled external world setup is not valid");
            test(player, memory);
        }
        finally
        {
            ObjectManager.Me = previousPlayer; ObjectManager.Executor = previousExecutor;
            typeof(ObjectManager).GetProperty("Wow")!.SetValue(null, previousMemory);
            CloseMemory(memory); cache.Dispose(); enabled.Dispose();
        }
    }
    private sealed class ObservedPlayer : LocalPlayer
    {
        internal Exception? NameFailure; internal Exception? PositionFailure; internal System.Action? BeforePosition;
        internal int NameReads; internal int PositionReads;
        internal ObservedPlayer() : base(0x1000) { }
        public override string Name { get { NameReads++; if (NameFailure != null) throw NameFailure; return "W42-Scan-Entry-Fixture"; } }
        public override WoWPoint Location { get { PositionReads++; BeforePosition?.Invoke(); if (PositionFailure != null) throw PositionFailure; return new WoWPoint(10, 10, 10); } }
    }
    private static void CloseMemory(Memory memory) => Set(memory, "_hProcess", IntPtr.Zero);
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
    private static void EnableVendors(WholesomeAutoQuest bot)
    {
        var loader = new VendorDataLoader(); Set(loader, "_database", new VendorDatabase());
        Set(bot, "_vendorDataReady", true); Set(bot, "_vendorLoader", loader);
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
        protected override IEnumerable<RunStatus> Execute(object context) { while (true) { Ticks++; yield return RunStatus.Running; } }
        public override void Stop(object context) { Stops++; base.Stop(context); }
    }
    private static void Stopped(GroupComposite root, RunningChild child, object context) =>
        Check(root.Tick(context) == RunStatus.Failure && child.Ticks == 1 && child.Stops > 0, "early failed refresh authorized another child tick");
    private static void Idle(QuestScheduler scheduler)
    {
        Check(scheduler.LastSchedule.FallbackMode == QuestFallbackMode.TimedIdle && scheduler.LastSchedule.Selected.Count == 0, "early failed refresh retained execution permission");
        Check(scheduler.CurrentProfilePath == null && scheduler.LastQuestCount == 0, "early failed refresh retained prior publication");
        Check(scheduler.EarliestRetryUtc.HasValue, "early failed refresh has no bounded observation retry");
    }
    private static void VendorWasReached(ObservedPlayer player, Exception error)
    {
        Check(player.PositionReads == 1 && player.NameReads == 0, "failure did not precede scheduler identity capture");
        Check(error.StackTrace?.Contains("VendorDataLoader.GetNearestVendors", StringComparison.Ordinal) == true, "actual vendor discovery is absent from the failure stack");
    }
    private static void WorldUnavailable(QuestScheduler scheduler, ObservedPlayer player)
    {
        // Counterevidence to a broad early-read hypothesis: ObjectManager.IsInGame
        // already catches a failed Memory read and returns false. Preserve this control.
        Check(!StyxWoW.IsInWorld, "closed memory unexpectedly remained in-world");
        Check(!scheduler.ScanAndRefresh(player), "unavailable world unexpectedly produced a profile");
    }
    private static void SameException(System.Action call, Exception expected, string owner)
    {
        Exception? observed = null; try { call(); } catch (Exception error) { observed = error; }
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
