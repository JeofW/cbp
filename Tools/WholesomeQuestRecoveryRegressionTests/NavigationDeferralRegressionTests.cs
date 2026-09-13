using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Styx.Logic.Pathing;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;

internal static class NavigationDeferralRegressionTests
{
    private static readonly QuestRecoveryKey Key = QuestRecoveryKey.ForQuestStage(867, QuestRecoveryStage.Objective);
    private static readonly QuestRecoveryKey Endpoint = QuestRecoveryKey.ForEndpoint(867, QuestRecoveryStage.Navigation, 1, "fixture-floor");
    private static readonly QuestRecoveryContext Context = new QuestRecoveryContext
    {
        PlayerLevel = 33, DatasetVersion = "fixture", CoreVersion = "fixture", NavigationFingerprint = "fixture", ObjectiveCounts = new[] { 0 }
    };

    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, Action Run)>();
        foreach (var reason in new[] { RouteFailureReason.SearchResourceLimit, RouteFailureReason.VerticalAccessUnresolved, RouteFailureReason.PartialPath, RouteFailureReason.PathSearchFailed })
        {
            var captured = reason;
            cases.Add(("unresolved " + reason + " defers instead of failing a quest", () => MonitorDefers(captured)));
        }
        cases.AddRange(new (string, Action)[]
        {
            ("an inactive objective never issues a deferral", Inactive),
            ("real objective progress takes precedence over old navigation evidence", Progress),
            ("legacy endpoint failure still has its existing bounded outcome", LegacyFailure),
            ("owned deferral has a fixed thirty-second retry and no failure counts", OwnedDeferral),
            ("duplicate deferral cannot slide a deadline", Duplicate),
            ("stale generations cannot defer a replacement attempt", StaleGeneration),
            ("manual and completed terminal states cannot be replaced by deferral", TerminalAuthority),
            ("deferral survives persistence without inventing a failure episode", Persist),
            ("one hundred deferrals do not consume the rolling failure budget", Repeated),
            ("a real failure after deferrals is still the first failure episode", SubsequentFailure),
            ("pickup and turn-in attempts use the same non-quarantining contract", OtherStages)
        });
        var failures = new List<string>();
        foreach (var test in cases)
        {
            try { test.Run(); Console.WriteLine("PASS navigation deferral: " + test.Name); }
            catch (Exception error)
            {
                while (error is TargetInvocationException wrapped && wrapped.InnerException != null) error = wrapped.InnerException;
                failures.Add(test.Name + ": " + error.Message);
                Console.Error.WriteLine("FAIL navigation deferral: " + failures[failures.Count - 1]);
            }
        }
        Console.WriteLine($"Navigation deferral scenarios: {cases.Count - failures.Count}/{cases.Count}; real monitor/manager, no game attached.");
        if (failures.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
    }

    private static QuestWorkSample Work(RouteFailureReason reason, bool active = true, int count = 0)
    {
        var sample = new QuestWorkSample
        {
            Key = Key, AttemptGeneration = 1, ClusterKey = Endpoint,
            KnownEndpointKeys = new[] { Endpoint }, ObjectiveCounts = new[] { count },
            IsActiveWork = active, EndpointPathFailed = true
        };
        // On the old consumer the same fresh failed movement is all it can see.
        // On the repair add the actual typed owner evidence; do not replace its policy.
        typeof(QuestWorkSample).GetProperty("NavigationFailure")?.SetValue(sample, reason);
        return sample;
    }

    private static bool IsDeferred(QuestProgressUpdate update) =>
        typeof(QuestProgressUpdate).GetProperty("NavigationDeferred")?.GetValue(update) is true;

    private static void MonitorDefers(RouteFailureReason reason)
    {
        var clock = new Clock(); var monitor = new WholesomeProgressMonitor(clock);
        QuestProgressUpdate result = monitor.Sample(Work(reason));
        Check(!result.Outcomes.Any(outcome => outcome.IsFailureEpisode), "an incomplete search is not evidence that this quest or every hotspot is invalid");
        Check(IsDeferred(result) && !result.RequestAlternateCluster, "release the stage through one deferral owner, not hotspot failure plus another recovery owner");
    }

    private static void Inactive()
    {
        var monitor = new WholesomeProgressMonitor(new Clock());
        var result = monitor.Sample(Work(RouteFailureReason.SearchResourceLimit, active: false));
        Check(!IsDeferred(result) && result.Outcomes.Count == 0, "transport/combat/pause suspension must not become failure or deferral");
    }

    private static void Progress()
    {
        var monitor = new WholesomeProgressMonitor(new Clock());
        monitor.Sample(Work(RouteFailureReason.SearchResourceLimit));
        var result = monitor.Sample(Work(RouteFailureReason.SearchResourceLimit, count: 1));
        Check(result.MadeProgress && !IsDeferred(result) && result.Outcomes.Count == 0, "current progress overrides an older failed movement result");
    }

    private static void LegacyFailure()
    {
        var result = new WholesomeProgressMonitor(new Clock()).Sample(Work(RouteFailureReason.None));
        Check(!IsDeferred(result) && result.Outcomes.Any(outcome => outcome.IsFailureEpisode && outcome.Key.Equals(Key)), "do not silently disable all existing endpoint recovery");
    }

    private static QuestRecoveryReportResult Defer(QuestRecoveryManager manager, QuestRecoveryKey key, long generation) =>
        (QuestRecoveryReportResult)(typeof(QuestRecoveryManager).GetMethod("TryDeferNavigationAttempt",
            new[] { typeof(QuestRecoveryKey), typeof(long), typeof(QuestRecoveryContext), typeof(string) })
            ?? throw new InvalidOperationException("The exact-generation non-escalating deferral owner is missing."))
            .Invoke(manager, new object[] { key, generation, Context, "Fixture: search evidence incomplete; no endpoint verdict." })!;

    private static void OwnedDeferral()
    {
        using var f = new Fixture(); var begun = f.Manager.TryBeginAttempt(Key, Context);
        var result = Defer(f.Manager, Key, begun.AttemptGeneration);
        Check(result.Accepted && !result.Decision.MayAttempt && result.Decision.RetryUtc == f.Clock.UtcNow.AddSeconds(30), "a deferral must acquire a fixed bounded retry");
        var record = f.Manager.GetRecord(Key)!;
        Check(record.State == QuestRecoveryState.CoolingDown && record.EpisodeCount == 0 && record.AttemptCountInEpisode == 0 && record.FirstFailureUtc == null && record.LastFailureUtc == null,
            "do not turn deferred navigation into a persisted failure episode");
        Check(!f.Manager.OwnsAttempt(Key, begun.AttemptGeneration), "deferral must release active core ownership");
        f.Clock.UtcNow = result.Decision.RetryUtc!.Value;
        Check(f.Manager.TryBeginAttempt(Key, Context).MayAttempt, "retry must be eligible exactly at its deadline");
    }

    private static void Duplicate()
    {
        using var f = new Fixture(); long generation = f.Manager.TryBeginAttempt(Key, Context).AttemptGeneration;
        DateTime deadline = Defer(f.Manager, Key, generation).Decision.RetryUtc!.Value;
        f.Clock.UtcNow = f.Clock.UtcNow.AddSeconds(10);
        Check(!Defer(f.Manager, Key, generation).Accepted && f.Manager.GetRecord(Key)!.CooldownUntilUtc == deadline,
            "a duplicate observer no longer owns the released attempt and cannot renew the delay");
    }

    private static void StaleGeneration()
    {
        using var f = new Fixture(); long old = f.Manager.TryBeginAttempt(Key, Context).AttemptGeneration;
        f.Manager.AbandonAttempt(Key, old);
        long current = f.Manager.TryBeginAttempt(Key, Context).AttemptGeneration;
        Check(!Defer(f.Manager, Key, old).Accepted && f.Manager.OwnsAttempt(Key, current), "stale evidence must not cancel a new attempt");
        Check(!Defer(f.Manager, Key, 0).Accepted, "missing ownership is never permission");
    }

    private static void TerminalAuthority()
    {
        foreach (bool completed in new[] { false, true })
        {
            using var f = new Fixture(); long generation = f.Manager.TryBeginAttempt(Key, Context).AttemptGeneration;
            if (completed) f.Manager.MarkCompleted(Key.QuestId); else f.Manager.SetManualBlacklist(Key.QuestId, true);
            Check(!Defer(f.Manager, Key, generation).Accepted, "terminal authority must dominate an old navigation owner");
            Check(f.Manager.Evaluate(Key, Context).State == (completed ? QuestRecoveryState.Completed : QuestRecoveryState.ManualBlacklist), "terminal state changed");
        }
    }

    private static void Persist()
    {
        using var f = new Fixture(); long generation = f.Manager.TryBeginAttempt(Key, Context).AttemptGeneration;
        DateTime until = Defer(f.Manager, Key, generation).Decision.RetryUtc!.Value;
        f.Manager.Flush(); var loaded = new QuestRecoveryManager(f.Clock); loaded.Configure(f.Environment);
        Check(loaded.GetRecord(Key)!.EpisodeCount == 0 && loaded.Evaluate(Key, Context).RetryUtc == until,
            "reload must retain the bounded delay without reconstructing an active/failing attempt");
    }

    private static void Repeated()
    {
        using var f = new Fixture();
        for (int i = 0; i < 100; i++)
        {
            var attempt = f.Manager.TryBeginAttempt(Key, Context);
            Check(attempt.MayAttempt, "navigation deferrals consumed the rolling failure budget at iteration " + i);
            var result = Defer(f.Manager, Key, attempt.AttemptGeneration);
            Check(result.Accepted && f.Manager.GetRecord(Key)!.EpisodeCount == 0, "deferrals escalated into quarantine");
            f.Clock.UtcNow = result.Decision.RetryUtc!.Value;
        }
    }

    private static void SubsequentFailure()
    {
        using var f = new Fixture(); long first = f.Manager.TryBeginAttempt(Key, Context).AttemptGeneration;
        f.Clock.UtcNow = Defer(f.Manager, Key, first).Decision.RetryUtc!.Value;
        long next = f.Manager.TryBeginAttempt(Key, Context).AttemptGeneration;
        f.Manager.Report(QuestAttemptOutcome.Failure(Key, Key, next, QuestFailureReason.NoObjectiveProgress, "Real subsequent fixture failure"), Context);
        Check(f.Manager.GetRecord(Key)!.EpisodeCount == 1, "a real failure must not be suppressed or treated as the hundredth deferral");
    }

    private static void OtherStages()
    {
        foreach (QuestRecoveryStage stage in new[] { QuestRecoveryStage.Pickup, QuestRecoveryStage.TurnIn })
        {
            using var f = new Fixture(); var key = QuestRecoveryKey.ForQuestStage(867, stage);
            long generation = f.Manager.TryBeginAttempt(key, Context).AttemptGeneration;
            Check(Defer(f.Manager, key, generation).Accepted && f.Manager.GetRecord(key)!.EpisodeCount == 0,
                "shared deferral contract must retain stage identity");
        }
    }

    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private sealed class Clock : IQuestRecoveryClock { public DateTime UtcNow { get; set; } = new DateTime(2026, 9, 13, 0, 0, 0, DateTimeKind.Utc); }
    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "cb-navigation-deferral-" + Guid.NewGuid().ToString("N"));
        internal readonly Clock Clock = new Clock();
        internal QuestRecoveryManager Manager { get; }
        internal QuestRecoveryEnvironment Environment { get; }
        internal Fixture()
        {
            Environment = new QuestRecoveryEnvironment(_root, "Fixture", "Offline", "fixture", "fixture", "fixture");
            Manager = new QuestRecoveryManager(Clock); Manager.Configure(Environment);
        }
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }
}
