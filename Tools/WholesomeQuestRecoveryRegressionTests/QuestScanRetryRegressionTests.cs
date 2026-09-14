using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using WholesomeAQ;

internal static class QuestScanRetryRegressionTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, System.Action Test)>
        {
            ("unavailable direct scan schedules a bounded retry", () => WithoutPlayer(() =>
            {
                var scheduler = Scheduler(); DateTime before = DateTime.UtcNow;
                Check(!scheduler.ScanAndRefresh(new LocalPlayer(0)), "unavailable scan published work");
                RetryDeadline(scheduler, before, DateTime.UtcNow);
            })),
            ("missing data schedules observation retry without authorizing work", () => WithoutPlayer(() =>
            {
                var scheduler = new QuestScheduler(new DataLoader(), new ProfileBuilder(), new WholesomeAQSettings());
                DateTime before = DateTime.UtcNow;
                Check(!scheduler.ScanAndRefresh(new LocalPlayer(0)), "missing data published work");
                RetryDeadline(scheduler, before, DateTime.UtcNow);
            })),
            ("actual DoScan skipped-world path schedules a bounded retry", () => WithoutPlayer(() =>
            {
                var scheduler = Scheduler(); var bot = Bot(scheduler); var gate = Gate(bot);
                Check(gate.TryRequest(), "lease setup failed");
                var lease = gate.Begin() ?? throw new InvalidOperationException("lease missing");
                DateTime before = DateTime.UtcNow;
                Invoke(bot, "DoScan", scheduler, lease); gate.Complete(lease);
                RetryDeadline(scheduler, before, DateTime.UtcNow);
            })),
            ("future retry does not request an immediate repeated scan", () => WithoutPlayer(() =>
            {
                var scheduler = Scheduler(); SetDeadline(scheduler, DateTime.UtcNow.AddHours(1));
                var bot = Bot(scheduler);
                Invoke(bot, "MaybeRequestTimedRetry");
                Check(!Gate(bot).Begin().HasValue, "future deadline caused a retry storm");
            })),
            ("due retries coalesce through the actual refresh gate", () => WithoutPlayer(() =>
            {
                var scheduler = Scheduler(); SetDeadline(scheduler, DateTime.UtcNow.AddMinutes(-1));
                var bot = Bot(scheduler); var gate = Gate(bot);
                Invoke(bot, "MaybeRequestTimedRetry"); Invoke(bot, "MaybeRequestTimedRetry");
                var lease = gate.Begin(); Check(lease.HasValue, "due deadline did not request a new scan");
                Check(!gate.Begin().HasValue, "two scans entered concurrently");
                gate.Complete(lease.Value);
                Check(!gate.Begin().HasValue, "duplicate pending ticks produced an extra scan");
            })),
            ("stopped bot does not revive a due retry", () => WithoutPlayer(() =>
            {
                var scheduler = Scheduler(); SetDeadline(scheduler, DateTime.UtcNow.AddMinutes(-1));
                var bot = Bot(scheduler); Set(bot, "_stopped", true);
                Invoke(bot, "MaybeRequestTimedRetry");
                Check(!Gate(bot).Begin().HasValue, "a stopped bot accepted retry work");
            }))
        };
        int failed = 0;
        foreach (var test in cases)
        {
            try { test.Test(); Console.WriteLine("PASS scan retry: " + test.Name); }
            catch (Exception error) { failed++; Console.Error.WriteLine("FAIL scan retry: " + test.Name + ": " + error); }
        }
        Console.WriteLine($"Scan retry scenarios: {cases.Count - failed}/{cases.Count}; actual scheduler and retry owner; controlled deadlines; no attached game.");
        if (failed != 0) throw new InvalidOperationException("Scan retry regressions: " + failed);
    }

    private static QuestScheduler Scheduler()
    {
        var loader = new DataLoader(); Set(loader, "_database", new QuestDatabase());
        return new QuestScheduler(loader, new ProfileBuilder(), new WholesomeAQSettings());
    }

    private static WholesomeAutoQuest Bot(QuestScheduler scheduler)
    {
        var bot = new WholesomeAutoQuest();
        Set(bot, "_stopped", false); Set(bot, "_initialized", true); Set(bot, "_dataReady", true); Set(bot, "_scheduler", scheduler);
        Gate(bot).Start(); return bot;
    }

    private static void RetryDeadline(QuestScheduler scheduler, DateTime before, DateTime after)
    {
        var deadline = scheduler.EarliestRetryUtc;
        Check(deadline.HasValue && deadline.Value >= before.AddSeconds(10) && deadline.Value <= after.AddSeconds(10),
            "unknown observations did not retain the existing ten-second scan cooldown as a retry deadline");
        Check(scheduler.LastSchedule.FallbackMode == QuestFallbackMode.TimedIdle && scheduler.LastSchedule.Selected.Count == 0,
            "a retry deadline authorized execution before a successful observation");
    }

    private static void SetDeadline(QuestScheduler scheduler, DateTime deadline) =>
        Set(scheduler, "<LastSchedule>k__BackingField", new QuestScheduleResult
        {
            FallbackMode = QuestFallbackMode.TimedIdle,
            EarliestRetryUtc = deadline
        });

    private static RefreshGate Gate(WholesomeAutoQuest bot) =>
        (RefreshGate)Field(bot, "_refreshGate").GetValue(bot)!;

    private static FieldInfo Field(object owner, string name) => owner.GetType().GetField(name, PrivateInstance)
        ?? throw new InvalidOperationException("fixture field missing: " + name);
    private static void Set(object owner, string name, object value) => Field(owner, name).SetValue(owner, value);
    private static void Invoke(object owner, string method, params object[] arguments) =>
        (owner.GetType().GetMethod(method, PrivateInstance) ?? throw new InvalidOperationException("owner method missing: " + method))
        .Invoke(owner, arguments);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void WithoutPlayer(System.Action test)
    {
        var previous = ObjectManager.Me; ObjectManager.Me = null;
        try { test(); } finally { ObjectManager.Me = previous; }
    }
}
