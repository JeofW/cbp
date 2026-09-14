using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Styx.Logic.Questing.Recovery;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
using WholesomeAQ;

internal static class QuestScanRevocationRegressionTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string PriorPath = "controlled-prior-profile-not-on-disk.xml";

    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, System.Action Test)>
        {
            ("unavailable actual player defers without a slot-read exception", () => WithoutPlayer(() =>
            {
                var scheduler = Scheduler();
                Check(!scheduler.ScanAndRefresh(new LocalPlayer(0)), "unavailable scan reported a built profile");
                Idle(scheduler);
            })),
            ("ScanAndBuildProfile alias also defers unavailable observations", () => WithoutPlayer(() =>
            {
                var scheduler = Scheduler();
                Check(!scheduler.ScanAndBuildProfile(new LocalPlayer(0)), "alias published unavailable work");
                Idle(scheduler);
            })),
            ("BuildForLogQuests alias also defers unavailable observations", () => WithoutPlayer(() =>
            {
                var scheduler = Scheduler();
                Check(!scheduler.BuildForLogQuests(new LocalPlayer(0)), "alias published unavailable log work");
                Idle(scheduler);
            })),
            ("null public argument preserves its exception but revokes prior publication", () => WithoutPlayer(() =>
            {
                var scheduler = Scheduler(); Seed(scheduler);
                bool rejected = false;
                try { scheduler.ScanAndRefresh(null!); }
                catch (ArgumentNullException error) { rejected = error.ParamName == "me"; }
                Check(rejected, "the existing null-argument contract changed");
                Idle(scheduler);
            })),
            ("unavailable scan clears a previously published path and accepted work set", () => WithoutPlayer(() =>
            {
                var scheduler = Scheduler(); Seed(scheduler);
                ObserveUnavailableScan(scheduler);
                Idle(scheduler);
            })),
            ("missing database clears prior publication instead of just returning false", () => WithoutPlayer(() =>
            {
                var scheduler = Scheduler(withDatabase: false); Seed(scheduler);
                Check(!scheduler.ScanAndRefresh(new LocalPlayer(0)), "missing database reported a profile");
                Idle(scheduler);
            })),
            ("actual scan revokes an already running quest root before another child tick", () => WithoutPlayer(() =>
            {
                var scheduler = Scheduler(); Seed(scheduler);
                WithRunningRoot(scheduler, (bot, child, root, context) =>
                {
                    ObserveUnavailableScan(scheduler);
                    Stopped(root, child, context); Idle(scheduler);
                });
            })),
            ("unknown observations revoke an already running validated grind fallback", () => WithoutPlayer(() =>
            {
                var scheduler = Scheduler(); Seed(scheduler, grind: true);
                WithRunningRoot(scheduler, (bot, child, root, context) =>
                {
                    ObserveUnavailableScan(scheduler, PriorPath);
                    Stopped(root, child, context); Idle(scheduler);
                });
            })),
            ("actual DoScan unavailable-world guard revokes the running plan", () => WithoutPlayer(() =>
            {
                var scheduler = Scheduler(); Seed(scheduler);
                WithRunningRoot(scheduler, (bot, child, root, context) =>
                {
                    InvokeDoScan(bot, scheduler, CurrentLease(bot));
                    Stopped(root, child, context); Idle(scheduler);
                });
            })),
            ("actual DoScan unavailable-data guard clears publication", () => WithoutPlayer(() =>
            {
                var scheduler = Scheduler(); Seed(scheduler);
                WithRunningRoot(scheduler, (bot, child, root, context) =>
                {
                    Set(bot, "_dataReady", false);
                    InvokeDoScan(bot, scheduler, CurrentLease(bot));
                    Stopped(root, child, context); Idle(scheduler);
                });
            })),
            ("repeated unavailable scans do not restore a revoked plan", () => WithoutPlayer(() =>
            {
                var scheduler = Scheduler(); Seed(scheduler);
                ObserveUnavailableScan(scheduler); ObserveUnavailableScan(scheduler);
                Idle(scheduler);
            })),
            ("stale refresh lease cannot revoke a newer generation plan", () => WithoutPlayer(() =>
            {
                var scheduler = Scheduler(); Seed(scheduler);
                WithRunningRoot(scheduler, (bot, child, root, context) =>
                {
                    RefreshLease stale = CurrentLease(bot);
                    var gate = Get<RefreshGate>(bot, "_refreshGate");
                    gate.Stop(); gate.Start();
                    Check(gate.TryRequest() && gate.Begin().HasValue, "replacement lease setup failed");
                    QuestScheduleResult published = scheduler.LastSchedule;
                    InvokeDoScan(bot, scheduler, stale);
                    Check(ReferenceEquals(published, scheduler.LastSchedule) && scheduler.CurrentProfilePath == PriorPath,
                        "stale callback changed the newer generation publication");
                    Check(root.Tick(context) == RunStatus.Running && child.Ticks == 2,
                        "stale callback interrupted a plan outside its refresh generation");
                });
            })),
            ("selected-work execution control runs until a real invalidating event", () => WithoutPlayer(() =>
            {
                var scheduler = Scheduler(); Seed(scheduler);
                WithRunningRoot(scheduler, (bot, child, root, context) =>
                    Check(root.Tick(context) == RunStatus.Running && child.Ticks == 2,
                        "ordinary selected work was unconditionally disabled"));
            }))
        };
        int failures = 0;
        foreach (var test in cases)
        {
            try { test.Test(); Console.WriteLine("PASS scan revocation: " + test.Name); }
            catch (Exception error) { failures++; Console.Error.WriteLine("FAIL scan revocation: " + test.Name + ": " + error); }
        }
        Console.WriteLine($"Scan revocation scenarios: {cases.Count - failures}/{cases.Count}; actual scheduler, DoScan and execution gate; seeded prior publication; no client or native dispatch.");
        if (failures != 0) throw new InvalidOperationException("Scan revocation regressions: " + failures);
    }

    private static QuestScheduler Scheduler(bool withDatabase = true)
    {
        var loader = new DataLoader();
        if (withDatabase) Set(loader, "_database", new QuestDatabase());
        return new QuestScheduler(loader, new ProfileBuilder(), new WholesomeAQSettings());
    }

    private static void Seed(QuestScheduler scheduler, bool grind = false)
    {
        var selected = new[] { new QuestWorkCandidate
        {
            QuestId = 867, Stage = QuestWorkStage.Objective,
            Recovery = new QuestRecoveryDecision { State = QuestRecoveryState.Eligible, MayAttempt = true }
        } };
        Set(scheduler, "<LastSchedule>k__BackingField", new QuestScheduleResult
        {
            Selected = grind ? Array.Empty<QuestWorkCandidate>() : selected,
            FallbackMode = grind ? QuestFallbackMode.ValidatedGrind : QuestFallbackMode.None,
            ValidatedGrindProfilePath = grind ? PriorPath : ""
        });
        Set(scheduler, "<CurrentProfilePath>k__BackingField", PriorPath);
        Set(scheduler, "<ActiveQuestIds>k__BackingField", new HashSet<int> { 867 });
        Set(scheduler, "<LastQuestCount>k__BackingField", 1);
    }

    private static void Idle(QuestScheduler scheduler)
    {
        Check(scheduler.LastSchedule.FallbackMode == QuestFallbackMode.TimedIdle && scheduler.LastSchedule.Selected.Count == 0,
            "unknown observation retained execution permission");
        Check(scheduler.CurrentProfilePath == null && scheduler.LastQuestCount == 0 &&
            (scheduler.ActiveQuestIds == null || scheduler.ActiveQuestIds.Count == 0), "stale publication metadata survived invalidation");
    }

    private static void ObserveUnavailableScan(QuestScheduler scheduler, string? grind = null)
    {
        // Keep the subsequent permission assertion reachable on the known failing baseline.
        // The diagnostic run separately records the real QuestLog.GetQuestIdAtIndex stack.
        try { Check(!scheduler.ScanAndRefresh(new LocalPlayer(0), grind!), "unknown owner published work"); }
        catch (NullReferenceException error) { Console.WriteLine("OBSERVED production unavailable-owner exception: " + error); }
    }

    private static void WithRunningRoot(QuestScheduler scheduler,
        System.Action<WholesomeAutoQuest, RunningChild, GroupComposite, object> test)
    {
        var bot = new WholesomeAutoQuest();
        Set(bot, "_stopped", false); Set(bot, "_initialized", true); Set(bot, "_dataReady", true); Set(bot, "_scheduler", scheduler);
        var root = (GroupComposite)bot.Root;
        var child = new RunningChild(); root.Children[0] = child;
        var context = new object(); root.Start(context);
        try
        {
            Check(root.Tick(context) == RunStatus.Running && child.Ticks == 1, "running-root fixture did not start selected work");
            test(bot, child, root, context);
        }
        finally { root.Stop(context); }
    }

    private static void Stopped(GroupComposite root, RunningChild child, object context)
    {
        Check(root.Tick(context) == RunStatus.Failure && child.Ticks == 1 && child.Stops > 0,
            "stale executing plan received another child tick after an unavailable observation");
    }

    private static RefreshLease CurrentLease(WholesomeAutoQuest bot)
    {
        var gate = Get<RefreshGate>(bot, "_refreshGate"); gate.Start();
        Check(gate.TryRequest(), "could not request controlled refresh lease");
        return gate.Begin() ?? throw new InvalidOperationException("could not acquire controlled refresh lease");
    }

    private static void InvokeDoScan(WholesomeAutoQuest bot, QuestScheduler scheduler, RefreshLease lease)
    {
        MethodInfo method = typeof(WholesomeAutoQuest).GetMethod("DoScan", PrivateInstance)
            ?? throw new InvalidOperationException("actual DoScan owner missing");
        method.Invoke(bot, new object[] { scheduler, lease });
    }

    private static void WithoutPlayer(System.Action test)
    {
        var previous = ObjectManager.Me; ObjectManager.Me = null;
        try { test(); } finally { ObjectManager.Me = previous; }
    }

    private static void Set(object owner, string name, object value)
    {
        var field = owner.GetType().GetField(name, PrivateInstance)
            ?? throw new InvalidOperationException("fixture field missing: " + name);
        field.SetValue(owner, value);
    }
    private static T Get<T>(object owner, string name) => (T)(owner.GetType().GetField(name, PrivateInstance)
        ?? throw new InvalidOperationException("fixture field missing: " + name)).GetValue(owner)!;
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class RunningChild : Composite
    {
        internal int Ticks { get; private set; }
        internal int Stops { get; private set; }
        protected override IEnumerable<RunStatus> Execute(object context)
        {
            while (true) { Ticks++; yield return RunStatus.Running; }
        }
        public override void Stop(object context) { Stops++; base.Stop(context); }
    }
}
