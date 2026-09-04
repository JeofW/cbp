using Styx;
using Styx.Logic.POI;
using Styx.Logic.Questing.Recovery;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZygorProfileRecovery;

internal static class ZygorRecoveryRegressionTests
{
    public static void Run()
    {
        TestDeathClassificationBoundary();
        TestPreDeathAttributionExclusions();
        TestPoiAttributionRejectsUnrelatedWork();
        TestConcurrentLifecycleAndExactClaim();
        TestDeniedClaimYieldsWithoutClearingPoi();
        TestProgressUsesCoherentCountersAndResetsDeaths();
        TestThirdAttributableDeathReportsOneOwnedEpisode();
        TestDeathsOutsideWindowStartFreshEpisode();
        TestAcceptedAndStaleCleanupUseExactIdentity();
        TestNoProgressRequiresActiveTimeAndTwoClusters();
        TestAutomaticAbandonmentRemainsPolicyLocked();
    }

    private static void TestDeathClassificationBoundary()
    {
        ZygorDeathClassification first = ZygorProfileRecovery.ZygorProfileRecovery.ClassifyDeath(
            true, false, 1, false);
        ZygorDeathClassification second = ZygorProfileRecovery.ZygorProfileRecovery.ClassifyDeath(
            true, false, 2, false);
        ZygorDeathClassification third = ZygorProfileRecovery.ZygorProfileRecovery.ClassifyDeath(
            true, false, 3, false);

        Assert(first.Action == ZygorDeathAction.NormalRecovery && !first.IsFailureEpisode,
            "the first attributable death must leave normal corpse recovery in control");
        Assert(second.Action == ZygorDeathAction.RequestAlternateCluster && !second.IsFailureEpisode,
            "the second attributable death must request one alternate cluster without ending an episode");
        Assert(third.Action == ZygorDeathAction.ReportRepeatedDeaths
               && third.Reason == QuestFailureReason.RepeatedDeaths
               && third.IsFailureEpisode,
            "only the third attributable zero-progress death may become a RepeatedDeaths episode");

        Assert(ZygorProfileRecovery.ZygorProfileRecovery.ClassifyDeath(false, false, 3, false).Action == ZygorDeathAction.Ignore
               && ZygorProfileRecovery.ZygorProfileRecovery.ClassifyDeath(true, true, 3, false).Action == ZygorDeathAction.Ignore
               && !ZygorProfileRecovery.ZygorProfileRecovery.ClassifyDeath(true, false, 3, true).IsFailureEpisode,
            "unowned, excluded-POI, and progressed deaths must not become recovery failures");
    }

    private static void TestPreDeathAttributionExclusions()
    {
        bool Eligible(params bool[] values) =>
            ZygorProfileRecovery.ZygorProfileRecovery.CanCapturePreDeath(
                values[0], values[1], values[2], values[3], values[4], values[5],
                values[6], values[7], values[8], values[9], values[10]);

        bool[] valid = { true, true, true, true, false, false, false, false, false, true, true };
        Assert(Eligible(valid), "an exact owned objective in its active area and quest combat must be attributable");
        for (int index = 0; index <= 8; index++)
        {
            bool[] excluded = valid.ToArray();
            excluded[index] = !excluded[index];
            Assert(!Eligible(excluded), $"pre-death exclusion flag {index} must block attribution");
        }
        bool[] unrelatedCombat = valid.ToArray();
        unrelatedCombat[10] = false;
        Assert(!Eligible(unrelatedCombat), "unrelated combat must not be charged to the active quest");
        bool[] peaceful = valid.ToArray();
        peaceful[9] = false;
        peaceful[10] = false;
        Assert(Eligible(peaceful), "non-combat approach to an exact objective may remain attributable");
        bool[] proximityOnly = valid.ToArray();
        proximityOnly[0] = false;
        Assert(!Eligible(proximityOnly), "area proximity without exact objective execution is insufficient");
    }

    private static void TestPoiAttributionRejectsUnrelatedWork()
    {
        Assert(!ZygorProfileRecovery.ZygorProfileRecovery.IsExcludedOrUnrelatedPoi(
                PoiType.None, 0, 876, false)
               && !ZygorProfileRecovery.ZygorProfileRecovery.IsExcludedOrUnrelatedPoi(
                   PoiType.Hotspot, 0, 876, false)
               && !ZygorProfileRecovery.ZygorProfileRecovery.IsExcludedOrUnrelatedPoi(
                   PoiType.Quest, 876, 876, false)
               && !ZygorProfileRecovery.ZygorProfileRecovery.IsExcludedOrUnrelatedPoi(
                   PoiType.Kill, 3338, 876, true),
            "neutral, hotspot, exact quest, and proven objective-target POIs may remain attributable");

        foreach (PoiType type in new[]
        {
            PoiType.Buy, PoiType.Sell, PoiType.Repair, PoiType.Train, PoiType.Mail,
            PoiType.Fly, PoiType.InnKeeper, PoiType.Corpse, PoiType.QuestPickUp,
            PoiType.QuestTurnIn
        })
        {
            Assert(ZygorProfileRecovery.ZygorProfileRecovery.IsExcludedOrUnrelatedPoi(
                    type, 876, 876, true),
                $"utility/unrelated POI {type} must block quest-death attribution");
        }
        Assert(ZygorProfileRecovery.ZygorProfileRecovery.IsExcludedOrUnrelatedPoi(
                   PoiType.Quest, 875, 876, false)
               && ZygorProfileRecovery.ZygorProfileRecovery.IsExcludedOrUnrelatedPoi(
                   PoiType.Kill, 3338, 876, false)
               && ZygorProfileRecovery.ZygorProfileRecovery.IsExcludedOrUnrelatedPoi(
                   PoiType.Loot, 3338, 876, false),
            "wrong-quest and unproven combat/loot POIs must not be charged to proximity alone");
    }

    private static void TestConcurrentLifecycleAndExactClaim()
    {
        var runtime = new FakeZygorRuntime();
        object behavior = new();
        runtime.Execution = Execution(behavior, 876, 2, new[] { 1, 0, 4 });
        var plugin = new ZygorProfileRecoveryPlugin(runtime);

        Parallel.Invoke(plugin.OnEnable, plugin.OnEnable, plugin.OnEnable);
        plugin.Pulse();
        plugin.Pulse();
        Assert(runtime.EnsureCount == 1 && runtime.SubscribeCount == 1 && runtime.ClaimKeys.Count == 1,
            "concurrent/repeated enable and pulses must configure, subscribe, and claim the exact objective once");
        Assert(runtime.ClaimKeys[0].Equals(QuestRecoveryKey.ForObjective(876, 2)),
            "the Zygor adapter must claim the exact quest/objective key");

        Parallel.Invoke(plugin.OnDisable, plugin.OnDisable, plugin.OnDisable);
        Assert(runtime.UnsubscribeCount == 1 && runtime.AbandonAttemptCount == 1,
            "concurrent/repeated disable must unsubscribe and release exact ownership once");

        plugin.OnEnable();
        plugin.OnEnable();
        plugin.OnDisable();
        Assert(runtime.EnsureCount == 2 && runtime.SubscribeCount == 2 && runtime.UnsubscribeCount == 2,
            "re-enable must install exactly one fresh death subscription");
    }

    private static void TestDeniedClaimYieldsWithoutClearingPoi()
    {
        var runtime = new FakeZygorRuntime { ClaimAllowed = false };
        object behavior = new();
        object unrelatedPoi = new();
        runtime.CurrentBehavior = behavior;
        runtime.CurrentPoi = unrelatedPoi;
        runtime.Execution = Execution(behavior, 876, 0, new[] { 0, 0, 0 }, ownedPoi: null);
        var plugin = new ZygorProfileRecoveryPlugin(runtime);
        plugin.OnEnable();

        plugin.Pulse();
        plugin.Pulse();
        plugin.Pulse();

        Assert(runtime.ClaimKeys.Count == 1 && runtime.ContinueCount == 0,
            "a denied exact claim must become passive for that activation and yield to its existing owner");
        Assert(runtime.ClearCount == 0 && ReferenceEquals(runtime.CurrentPoi, unrelatedPoi),
            "a denied claim must never clear an unrelated POI");
        plugin.OnDisable();
    }

    private static void TestProgressUsesCoherentCountersAndResetsDeaths()
    {
        var runtime = new FakeZygorRuntime();
        object behavior = new();
        runtime.Execution = Execution(behavior, 876, 1, new[] { 2, 1, 7 });
        var plugin = new ZygorProfileRecoveryPlugin(runtime);
        plugin.OnEnable();
        plugin.Pulse();
        runtime.FireDeath();
        plugin.Pulse();
        runtime.FireDeath();

        runtime.Execution = Execution(behavior, 876, 1, new[] { 2, 1, 8 });
        plugin.Pulse();

        Assert(runtime.ProgressReports.Count == 1
               && runtime.ProgressReports[0].counts.SequenceEqual(new[] { 2, 1, 8 })
               && runtime.CapturedCounts.Last().SequenceEqual(new[] { 2, 1, 8 }),
            "an increase in a required-item slot must report the same coherent counter vector and context");

        plugin.Pulse();
        runtime.FireDeath();
        plugin.Pulse();
        runtime.FireDeath();
        plugin.Pulse();
        runtime.FireDeath();
        Assert(runtime.OwnedOutcomes.Count == 1
               && runtime.OwnedOutcomes[0].Reason == QuestFailureReason.RepeatedDeaths,
            "progress must reset the death episode so only three subsequent attributable deaths report failure");
        plugin.OnDisable();
    }

    private static void TestThirdAttributableDeathReportsOneOwnedEpisode()
    {
        var runtime = new FakeZygorRuntime();
        object behavior = new();
        runtime.Execution = Execution(behavior, 876, 3, new[] { 0, 0, 0 });
        var plugin = new ZygorProfileRecoveryPlugin(runtime);
        plugin.OnEnable();
        plugin.Pulse();

        runtime.FireDeath();
        plugin.Pulse();
        runtime.FireDeath();
        plugin.Pulse();
        Assert(runtime.AlternateClusterCount == 1 && runtime.OwnedOutcomes.Count == 0,
            "the second attributable death must issue exactly one alternate-cluster request");
        runtime.FireDeath();
        runtime.FireDeath();

        QuestAttemptOutcome outcome = runtime.OwnedOutcomes.Single();
        Assert(outcome.Key.Equals(QuestRecoveryKey.ForObjective(876, 3))
               && outcome.AttemptKey.Equals(outcome.Key)
               && outcome.AttemptGeneration == 41
               && outcome.Kind == QuestAttemptOutcomeKind.Failure
               && outcome.IsFailureEpisode
               && outcome.Reason == QuestFailureReason.RepeatedDeaths,
            "the third death must report one exact-generation RepeatedDeaths episode and never a private permanent count");
        plugin.OnDisable();
    }

    private static void TestDeathsOutsideWindowStartFreshEpisode()
    {
        var runtime = new FakeZygorRuntime();
        object behavior = new();
        runtime.Execution = Execution(behavior, 876, 3, new[] { 0 });
        var plugin = new ZygorProfileRecoveryPlugin(runtime);
        plugin.OnEnable();
        plugin.Pulse();

        runtime.FireDeath();
        runtime.Now = runtime.Now.AddMinutes(16);
        plugin.Pulse();
        runtime.FireDeath();
        plugin.Pulse();
        runtime.FireDeath();
        plugin.Pulse();

        Assert(runtime.OwnedOutcomes.Count == 0 && runtime.AlternateClusterCount == 1,
            "a death older than 15 minutes must expire before the new three-death episode is counted");
        plugin.OnDisable();
    }

    private static void TestAcceptedAndStaleCleanupUseExactIdentity()
    {
        var acceptedRuntime = new FakeZygorRuntime();
        object acceptedBehavior = new();
        object acceptedPoi = new();
        acceptedRuntime.CurrentBehavior = acceptedBehavior;
        acceptedRuntime.CurrentPoi = acceptedPoi;
        acceptedRuntime.Execution = Execution(acceptedBehavior, 876, 0, new[] { 0 }, acceptedPoi);
        var accepted = new ZygorProfileRecoveryPlugin(acceptedRuntime);
        accepted.OnEnable();
        accepted.Pulse();
        FireThreeDeaths(accepted, acceptedRuntime);
        accepted.Pulse();
        Assert(acceptedRuntime.ContinueCount == 1 && acceptedRuntime.ClearCount == 1,
            "an accepted failure may dispose the exact behavior and clear its exact captured POI once");
        accepted.OnDisable();

        var staleRuntime = new FakeZygorRuntime();
        object staleBehavior = new();
        object stalePoi = new();
        object winnerBehavior = new();
        object winnerPoi = stalePoi;
        staleRuntime.CurrentBehavior = staleBehavior;
        staleRuntime.CurrentPoi = stalePoi;
        staleRuntime.Execution = Execution(staleBehavior, 876, 0, new[] { 0 }, stalePoi);
        staleRuntime.OnOwnedOutcome = _ =>
        {
            staleRuntime.CurrentBehavior = winnerBehavior;
            staleRuntime.CurrentPoi = winnerPoi;
            staleRuntime.Execution = Execution(winnerBehavior, 876, 0, new[] { 0 }, winnerPoi);
        };
        var stale = new ZygorProfileRecoveryPlugin(staleRuntime);
        stale.OnEnable();
        stale.Pulse();
        FireThreeDeaths(stale, staleRuntime);
        stale.Pulse();
        Assert(staleRuntime.ClearCount == 0 && ReferenceEquals(staleRuntime.CurrentPoi, winnerPoi),
            "an accepted but delayed outcome must not clear a newer/reused same-quest POI");
        stale.OnDisable();

        var rejectedRuntime = new FakeZygorRuntime { OwnedOutcomeAccepted = false };
        object rejectedBehavior = new();
        object rejectedPoi = new();
        rejectedRuntime.CurrentBehavior = rejectedBehavior;
        rejectedRuntime.CurrentPoi = rejectedPoi;
        rejectedRuntime.Execution = Execution(rejectedBehavior, 876, 0, new[] { 0 }, rejectedPoi);
        var rejected = new ZygorProfileRecoveryPlugin(rejectedRuntime);
        rejected.OnEnable();
        rejected.Pulse();
        FireThreeDeaths(rejected, rejectedRuntime);
        rejected.Pulse();
        Assert(rejectedRuntime.ContinueCount == 0 && rejectedRuntime.ClearCount == 0
               && ReferenceEquals(rejectedRuntime.CurrentPoi, rejectedPoi),
            "a rejected/stale manager result must not clean up its behavior or POI");
        rejected.OnDisable();
    }

    private static void TestNoProgressRequiresActiveTimeAndTwoClusters()
    {
        var runtime = new FakeZygorRuntime();
        object behavior = new();
        runtime.Execution = Execution(behavior, 876, 1, new[] { 0 }, cluster: "cluster-a");
        var plugin = new ZygorProfileRecoveryPlugin(runtime);
        plugin.OnEnable();
        plugin.Pulse();

        AdvanceActive(plugin, runtime, TimeSpan.FromMinutes(4));
        plugin.Pulse();
        Assert(runtime.AlternateClusterCount == 1 && runtime.OwnedOutcomes.Count == 0,
            "four active minutes must request one alternate cluster without ending the episode");

        runtime.Execution = Execution(behavior, 876, 1, new[] { 0 }, cluster: "cluster-b", active: false);
        AdvanceActive(plugin, runtime, TimeSpan.FromMinutes(3), active: false);
        Assert(runtime.OwnedOutcomes.Count == 0,
            "vendor/rest/pause or other inactive time must not advance the no-progress clock");

        runtime.Execution = Execution(behavior, 876, 1, new[] { 0 }, cluster: "cluster-b");
        plugin.Pulse();
        AdvanceActive(plugin, runtime, TimeSpan.FromMinutes(4));
        Assert(runtime.OwnedOutcomes.Count == 1
               && runtime.OwnedOutcomes[0].Reason == QuestFailureReason.NoObjectiveProgress,
            "eight active minutes across two clusters must report one owned no-progress episode");
        plugin.OnDisable();
    }

    private static void TestAutomaticAbandonmentRemainsPolicyLocked()
    {
        var runtime = new FakeZygorRuntime
        {
            ReportState = QuestRecoveryState.Quarantined,
            AbandonSnapshot = new QuestAbandonmentLiveSnapshot
            {
                IsAccepted = true,
                IsCompleted = false,
                StateIsCertain = true,
                HasObjectiveProgress = false,
                FreeQuestLogSlots = 2
            }
        };
        object behavior = new();
        runtime.Execution = Execution(behavior, 876, 0, new[] { 0 });
        var plugin = new ZygorProfileRecoveryPlugin(runtime);
        plugin.OnEnable();
        plugin.Pulse();
        FireThreeDeaths(plugin, runtime);

        Assert(runtime.AbandonmentCalls == 1
               && runtime.ClientMutations.SequenceEqual(new[] { "persist", "abandon" }),
            "quarantine abandonment must use the locked policy boundary and persist before client action");
        plugin.OnDisable();

        runtime = new FakeZygorRuntime
        {
            ReportState = QuestRecoveryState.Quarantined,
            AbandonSnapshot = new QuestAbandonmentLiveSnapshot
            {
                IsAccepted = true,
                IsCompleted = false,
                StateIsCertain = true,
                HasObjectiveProgress = false,
                FreeQuestLogSlots = 3
            }
        };
        behavior = new object();
        runtime.Execution = Execution(behavior, 876, 0, new[] { 0 });
        plugin = new ZygorProfileRecoveryPlugin(runtime);
        plugin.OnEnable();
        plugin.Pulse();
        FireThreeDeaths(plugin, runtime);
        Assert(runtime.AbandonmentCalls == 1 && runtime.ClientMutations.Count == 0,
            "a retained quest outside <=2-slot pressure must perform no abandon mutation");
        plugin.OnDisable();
    }

    private static void FireThreeDeaths(ZygorProfileRecoveryPlugin plugin, FakeZygorRuntime runtime)
    {
        runtime.FireDeath();
        plugin.Pulse();
        runtime.FireDeath();
        plugin.Pulse();
        runtime.FireDeath();
    }

    private static void AdvanceActive(
        ZygorProfileRecoveryPlugin plugin,
        FakeZygorRuntime runtime,
        TimeSpan duration,
        bool active = true)
    {
        int samples = (int)(duration.TotalSeconds / 5);
        for (int index = 0; index < samples; index++)
        {
            runtime.Now = runtime.Now.AddSeconds(5);
            if (runtime.Execution != null)
                runtime.Execution = runtime.Execution.WithActive(active);
            plugin.Pulse();
        }
    }

    private static ZygorObjectiveExecution Execution(
        object behavior,
        uint questId,
        int objectiveIndex,
        IReadOnlyList<int> counts,
        object ownedPoi = null,
        string cluster = "cluster-a",
        bool active = true) => new(
            behavior,
            QuestRecoveryKey.ForObjective(questId, objectiveIndex),
            "Test quest",
            counts,
            ownedPoi,
            cluster,
            active,
            active);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class FakeZygorRuntime : ZygorRecoveryRuntime
    {
        private BotEvents.Player.PlayerDiedDelegate _deathHandler;

        public DateTime Now { get; set; } = new(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);
        public bool ClaimAllowed { get; set; } = true;
        public bool OwnedOutcomeAccepted { get; set; } = true;
        public QuestRecoveryState ReportState { get; set; } = QuestRecoveryState.CoolingDown;
        public bool ResilientProfileLoaded { get; set; } = true;
        public bool BotRunning { get; set; } = true;
        public object CurrentBehavior { get; set; }
        public object CurrentPoi { get; set; }
        public ZygorObjectiveExecution Execution { get; set; }
        public QuestAbandonmentLiveSnapshot AbandonSnapshot { get; set; }
        public int EnsureCount { get; private set; }
        public int SubscribeCount { get; private set; }
        public int UnsubscribeCount { get; private set; }
        public int AbandonAttemptCount { get; private set; }
        public int AlternateClusterCount { get; private set; }
        public int ContinueCount { get; private set; }
        public int ClearCount { get; private set; }
        public int AbandonmentCalls { get; private set; }
        public List<QuestRecoveryKey> ClaimKeys { get; } = new();
        public List<IReadOnlyList<int>> CapturedCounts { get; } = new();
        public List<(QuestRecoveryKey key, IReadOnlyList<int> counts)> ProgressReports { get; } = new();
        public List<QuestAttemptOutcome> OwnedOutcomes { get; } = new();
        public List<string> ClientMutations { get; } = new();
        public Action<QuestAttemptOutcome> OnOwnedOutcome { get; set; }

        public override DateTime UtcNow => Now;
        public override bool IsResilientProfileLoaded => ResilientProfileLoaded;
        public override bool IsBotRunning => BotRunning;
        public override void EnsureConfigured() => EnsureCount++;
        public override void SubscribePlayerDied(BotEvents.Player.PlayerDiedDelegate handler)
        {
            SubscribeCount++;
            _deathHandler += handler;
        }
        public override void UnsubscribePlayerDied(BotEvents.Player.PlayerDiedDelegate handler)
        {
            UnsubscribeCount++;
            _deathHandler -= handler;
        }
        public void FireDeath() => _deathHandler?.Invoke();
        public override ZygorObjectiveExecution CaptureObjectiveExecution() =>
            CurrentBehavior == null && Execution != null && ContinueCount > 0 ? null : Execution;
        public override QuestRecoveryContext CaptureContext(IReadOnlyList<int> counts)
        {
            int[] copy = (counts ?? Array.Empty<int>()).ToArray();
            CapturedCounts.Add(copy);
            return new QuestRecoveryContext { PlayerLevel = 34, ObjectiveCounts = copy };
        }
        public override QuestRecoveryDecision TryBeginAttempt(QuestRecoveryKey key, QuestRecoveryContext context)
        {
            ClaimKeys.Add(key);
            return new QuestRecoveryDecision
            {
                MayAttempt = ClaimAllowed,
                State = ClaimAllowed ? QuestRecoveryState.Attempting : QuestRecoveryState.CoolingDown,
                AttemptGeneration = ClaimAllowed ? 41 : 0
            };
        }
        public override bool OwnsAttempt(QuestRecoveryKey key, long generation) =>
            ClaimAllowed && generation == 41 && OwnedOutcomes.Count == 0;
        public override bool AbandonAttempt(QuestRecoveryKey key, long generation)
        {
            AbandonAttemptCount++;
            return true;
        }
        public override void ReportProgress(
            QuestRecoveryKey key,
            IReadOnlyList<int> counts,
            QuestRecoveryContext context) => ProgressReports.Add((key, counts.ToArray()));
        public override QuestRecoveryReportResult TryReportOwnedOutcome(
            QuestAttemptOutcome outcome,
            QuestRecoveryContext context)
        {
            OwnedOutcomes.Add(outcome);
            OnOwnedOutcome?.Invoke(outcome);
            return new QuestRecoveryReportResult
            {
                Accepted = OwnedOutcomeAccepted,
                Decision = new QuestRecoveryDecision { State = ReportState }
            };
        }
        public override bool RequestAlternateCluster(ZygorObjectiveExecution execution)
        {
            if (!ReferenceEquals(CurrentBehavior ?? execution.Behavior, execution.Behavior))
                return false;
            AlternateClusterCount++;
            return true;
        }
        public override bool ContinueObjective(
            ZygorObjectiveExecution execution,
            object capturedPoi,
            string reason)
        {
            if (CurrentBehavior != null && !ReferenceEquals(CurrentBehavior, execution.Behavior))
                return false;
            ContinueCount++;
            if (capturedPoi != null && ReferenceEquals(CurrentPoi, capturedPoi))
            {
                ClearCount++;
                CurrentPoi = null;
            }
            CurrentBehavior = null;
            return true;
        }
        public override QuestAbandonmentDecision TryExecuteAutomaticAbandonment(
            QuestRecoveryKey key,
            Func<QuestAbandonmentLiveSnapshot> recapture)
        {
            AbandonmentCalls++;
            QuestAbandonmentLiveSnapshot live = recapture();
            QuestAbandonmentDecision decision = QuestAbandonmentPolicy.Evaluate(new QuestAbandonmentContext
            {
                IsAccepted = live.IsAccepted,
                IsCompleted = live.IsCompleted,
                StateIsCertain = live.StateIsCertain,
                HasObjectiveProgress = live.HasObjectiveProgress,
                FreeQuestLogSlots = live.FreeQuestLogSlots,
                RecoveryState = QuestRecoveryState.Quarantined,
                Reason = QuestFailureReason.RepeatedDeaths
            });
            if (decision.MayAbandon)
            {
                ClientMutations.Add("persist");
                ClientMutations.Add("abandon");
            }
            return decision;
        }
        public override QuestAbandonmentLiveSnapshot CaptureAbandonmentSnapshot(uint questId) =>
            AbandonSnapshot ?? new QuestAbandonmentLiveSnapshot { FreeQuestLogSlots = 25 };
    }
}
