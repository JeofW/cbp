using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Bots.Quest.QuestOrder;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;

var utcNow = new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc);

try
{
    TestStagePriority();
    TestStageSpecificCooldown();
    TestEndpointCooldownIsolation();
    TestEndpointAttemptCap();
    TestEndpointAttemptCapCountsDistinctRecoveryKeys();
    TestOrdinaryWorkPrecedesHalfOpenProbe();
    TestNoWorkReturnsEarliestRetry();
    TestCandidateTieBreakers();
    TestValidatedGrindFallbackRequiresVettedPath();
    TestDatasetFingerprintIsDeterministic();
    TestSchedulerRetainsEligibleAlternatesAndReportsExactExclusions();
    TestSchedulerFallbackUsesOnlyCallerVettedPath();
    TestUnknownCompletionAuthorityDefersNegativePickupPaths();
    TestAuthoritativeCompletionsAreMarkedBeforeEvaluation();
    TestAncestorCorrectionUsesQuestData();
    TestIneligibleDescendantDoesNotTriggerAncestorCorrection();
    TestSchedulerEvaluatesEveryNarrowScope();
    TestEndpointClusteringKeepsFiveDistinctEightyYardCells();
    TestEndpointCapAppliesAcrossObjectiveStage();
    TestEvaluationDoesNotClaimAndActivationUsesExactKey();
    TestEndpointFailureEscalatesOnlyAfterEveryKnownCluster();
    TestNavigationFingerprintIncludesProviderAndMeshStamp();
    TestProductionCallerNoLongerNeedsBlacklistCompatibilityAdapters();
    TestOrdinaryObjectiveEndpointPrecedesHalfOpenAlternative();
    TestOrdinaryRelationPrecedesHalfOpenAlternative();
    TestCompletedObjectivesDoNotConsumeEndpointBudget();
    TestUnavailableObjectiveCountsDoNotAdvanceWork();
    TestScanExpansionPrecedesFallbackAndResets();
    TestProductionActivationClaimsOnceAndCoalescesRebuild();
    TestProgressReleaseAllowsSameBehaviorToClaimALaterGeneration();
    TestEndpointSafetyAndReachabilityPrecedeDistanceAndCap();
    TestSchedulerFiltersKnownNavigationUnsafePointsBeforeCap();
    TestLoadedSpawnsReceiveCachedLiveNavigationEnrichment();
    TestConfirmedEndpointPrecedesUnknownFallback();
    TestEmbeddedQuestPoiRequiresExactCurrentEndpoint();
    TestDeniedActivationLeavesUnownedPoiUntouched();
    TestOutcomePoiCleanupUsesTheReportedFailureScope();
    TestGuardedProfilePreservesScheduleOrderAndLiveState();
    TestGuardedProfileUsesOnlyApprovedAlternativesAndHotspots();
    TestGuardedProfileOmitsEndpointlessObjectives();
    TestGameObjectOnlyObjectiveResolvesUseObjectOverride();
    TestGameObjectItemCollectionRemainsCollectItemOverride();
    TestSchedulerReportsEveryEndpointlessObjectiveOmission();
    TestSchedulerDeduplicatesRelationRowsAndExactEndpoints();
    TestRefreshGateCoalescesConcurrentRequestsAndStopsCallbacks();
    TestLifecycleGateDoesNotGrowSubscriptionsAcrossRestarts();
    TestLifecycleResetDoesNotCarryPickupCyclesAcrossRestart();
    TestProgressMonitorCountsOnlyActiveWorkAndCoalescesOneStall();
    TestProgressMonitorScopesEndpointAndDeathFailures();
    TestProgressMonitorResetsForEachSameKeyOwnershipGeneration();
    TestProductionWorkSnapshotExcludesNonWorkAndRequiresExactOwner();
    TestDeathEventConsumesOnlyPreDeathOwnedQuestCombatSnapshot();
    TestAttemptOwnershipUsesTheExactGenerationToken();
    TestOwnedFailureBindsGenerationWithoutSyntheticSuccess();
    TestCompletedOwnedStageRequestsRefreshBeforeQuestOrderRunsOut();
    TestTimedIdleSuppressesOldQuestOrderUntilARebuildSelectsWork();
    TestPickupOutcomeCoalescingStillReportsTheFailureEpisode();
    TestPickupRecoveryRequiresThreeDistinctActiveOwnedCycles();
    Console.WriteLine("Wholesome scheduler recovery regression tests passed.");
}

catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    global::System.Environment.ExitCode = 1;
}

void TestRefreshGateCoalescesConcurrentRequestsAndStopsCallbacks()
{
    var gate = new RefreshGate();
    var accepted = 0;
    Parallel.For(0, 100, _ =>
    {
        if (gate.TryRequest())
            Interlocked.Increment(ref accepted);
    });

    Assert(accepted == 1 && gate.Begin(),
        "100 concurrent refresh requests must produce one pending main-thread refresh");
    accepted = 0;
    Parallel.For(0, 100, _ =>
    {
        if (gate.TryRequest())
            Interlocked.Increment(ref accepted);
    });
    Assert(accepted == 1,
        "requests arriving during a running refresh must coalesce into one follow-up latch");
    gate.Complete();
    Assert(gate.Begin(),
        "completion must promote the one running-state latch to a pending refresh");
    gate.TryRequest();
    gate.Stop();
    gate.Complete();
    Assert(!gate.TryRequest() && !gate.Begin(),
        "Stop must drop pending/rerun bits and win a race with a stale running callback's Complete");
    gate.Start();
    Assert(gate.TryRequest() && gate.Begin(),
        "a deliberate later bot start must reset the stopped refresh gate");
}

void TestLifecycleGateDoesNotGrowSubscriptionsAcrossRestarts()
{
    var gate = new RefreshGate();
    var lifecycle = new WholesomeLifecycleGate(gate);
    var active = 0;
    var maximumActive = 0;
    var subscriptions = 0;
    var removals = 0;

    for (var cycle = 0; cycle < 10; cycle++)
    {
        lifecycle.Start(() =>
        {
            subscriptions++;
            active++;
            maximumActive = Math.Max(maximumActive, active);
        });
        lifecycle.Start(() => throw new InvalidOperationException("duplicate subscription"));
        lifecycle.Stop(() => { removals++; active--; });
        lifecycle.Stop(() => throw new InvalidOperationException("duplicate removal"));
    }

    Assert(subscriptions == 10 && removals == 10 && active == 0 && maximumActive == 1,
        "repeated start/stop cycles must own exactly one event subscription set");
    Assert(lifecycle.IsStopped && !gate.TryRequest(),
        "lifecycle Stop must leave an explicit stopped state and cancel refresh work");
}

void TestLifecycleResetDoesNotCarryPickupCyclesAcrossRestart()
{
    var bot = new WholesomeAutoQuest();
    var field = typeof(WholesomeAutoQuest).GetField(
        "_pickupMonitor",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    var monitor = (WholesomePickupMonitor?)field?.GetValue(bot);
    Assert(monitor != null, "the production bot must own one pickup lifecycle tracker");
    var key = QuestRecoveryKey.ForNpc(867, QuestRecoveryStage.Pickup, 1001);
    var observation = QuestAttemptOutcome.Observation(
        key, QuestFailureReason.PickupWrongQuestShown, "wrong quest");
    var failure = QuestAttemptOutcome.Failure(
        key, QuestFailureReason.PickupWrongQuestShown, "wrong quest");
    monitor!.Observe(key, 12, 1, observation, active: true);
    monitor.Observe(key, 12, 2, observation, active: true);
    Assert(monitor.Observe(key, 12, 3, failure, active: true) == failure,
        "the lifecycle fixture must reach a reported pickup failure before restart");

    bot.ResetRecoveryLifecycleState();
    Assert(monitor.Observe(key, 12, 4, failure, active: true) == null,
        "Stop/Start reset must prevent the same pickup from inheriting target, generation, token, count, outcome, or reported state");
}

void TestProgressMonitorCountsOnlyActiveWorkAndCoalescesOneStall()
{
    var clock = new TestRecoveryClock(utcNow);
    var monitor = new WholesomeProgressMonitor(clock);
    var key = QuestRecoveryKey.ForQuestStage(867, QuestRecoveryStage.Objective);
    var clusterA = QuestRecoveryKey.ForEndpoint(867, QuestRecoveryStage.Navigation, 1, "cell:0:0");
    var clusterB = QuestRecoveryKey.ForEndpoint(867, QuestRecoveryStage.Navigation, 1, "cell:1:0");
    QuestProgressUpdate Accrue(QuestWorkSample sample, TimeSpan duration)
    {
        QuestProgressUpdate update = new();
        for (var elapsed = TimeSpan.Zero; elapsed < duration; elapsed += TimeSpan.FromSeconds(2))
        {
            clock.Advance(TimeSpan.FromSeconds(2));
            update = monitor.Sample(sample);
        }
        return update;
    }

    monitor.Sample(WorkSample(key, new[] { 0 }, clusterA, active: true));
    Accrue(WorkSample(key, new[] { 0 }, clusterA, active: true), TimeSpan.FromMinutes(4));
    clock.Advance(TimeSpan.FromHours(1));
    monitor.Sample(WorkSample(key, new[] { 0 }, clusterA, active: false));
    clock.Advance(TimeSpan.FromHours(1));
    monitor.Sample(WorkSample(key, new[] { 0 }, clusterB, active: true));
    var failed = Accrue(WorkSample(key, new[] { 0 }, clusterB, active: true), TimeSpan.FromMinutes(4));

    Assert(failed.Outcomes.Count == 1
           && failed.Outcomes[0].Reason == QuestFailureReason.NoObjectiveProgress
           && failed.Outcomes[0].IsFailureEpisode,
        "eight active work minutes across two clusters must create one no-progress failure episode while paused time is excluded");
    var repeated = monitor.Sample(WorkSample(key, new[] { 0 }, clusterB, active: true));
    Assert(repeated.Outcomes.Count == 1 && !repeated.Outcomes[0].IsFailureEpisode,
        "repeated samples from the same continuous stall must remain non-episode observations");

    var progressed = monitor.Sample(WorkSample(key, new[] { 1 }, clusterB, active: true));
    Assert(progressed.MadeProgress && progressed.ObjectiveCounts.SequenceEqual(new[] { 1 }),
        "an objective counter or item gain must report progress and reset stall/death escalation");

    monitor.Reset();
    monitor.Sample(WorkSample(key, new[] { 0 }, clusterA, active: true));
    clock.Advance(TimeSpan.FromHours(1));
    var afterMissingPulses = monitor.Sample(WorkSample(key, new[] { 0 }, clusterB, active: true));
    Assert(afterMissingPulses.Outcomes.Count == 0,
        "a long interval with no pulse samples must not be charged as active quest-work time");
}

void TestProgressMonitorScopesEndpointAndDeathFailures()
{
    var clock = new TestRecoveryClock(utcNow);
    var monitor = new WholesomeProgressMonitor(clock);
    var key = QuestRecoveryKey.ForQuestStage(867, QuestRecoveryStage.Objective);
    var endpointA = QuestRecoveryKey.ForEndpoint(867, QuestRecoveryStage.Navigation, 1, "cell:0:0");
    var endpointB = QuestRecoveryKey.ForEndpoint(867, QuestRecoveryStage.Navigation, 1, "cell:1:0");
    var known = new[] { endpointA, endpointB };

    var excludedProbe = monitor.Sample(WorkSample(
        key, new[] { 0 }, endpointA, active: false,
        endpointPathFailed: true, knownEndpoints: known,
        allHotspotsUnavailable: true));
    Assert(excludedProbe.Outcomes.Count == 0,
        "path and hotspot probes captured outside active quest work must not create recovery failures");

    var firstPath = monitor.Sample(WorkSample(
        key, new[] { 0 }, endpointA, active: true,
        endpointPathFailed: true, knownEndpoints: known));
    Assert(firstPath.Outcomes.Count == 1
           && firstPath.Outcomes[0].Key.Equals(endpointA)
           && firstPath.Outcomes[0].Reason == QuestFailureReason.PathGenerationFailed
           && firstPath.Outcomes[0].IsFailureEpisode,
        "one failed path must cool only its exact endpoint");
    var repeatedPath = monitor.Sample(WorkSample(
        key, new[] { 0 }, endpointA, active: true,
        endpointPathFailed: true, knownEndpoints: known));
    Assert(repeatedPath.Outcomes.Count == 1 && !repeatedPath.Outcomes[0].IsFailureEpisode,
        "the same endpoint-path stall must not become another failure episode");
    var lastPath = monitor.Sample(WorkSample(
        key, new[] { 0 }, endpointB, active: true,
        endpointPathFailed: true, knownEndpoints: known));
    Assert(lastPath.Outcomes.Any(outcome => outcome.Key.Equals(endpointB)
                                            && outcome.Reason == QuestFailureReason.PathGenerationFailed)
           && lastPath.Outcomes.Any(outcome => outcome.Key.Equals(key)
                                               && outcome.Reason == QuestFailureReason.NoNavigableHotspot),
        "stage failure is permitted only after every known generated hotspot cluster has failed");

    monitor.Reset();
    var emptyArea = monitor.Sample(WorkSample(
        key, new[] { 0 }, endpointA, active: true,
        allHotspotsUnavailable: true));
    Assert(emptyArea.Outcomes.Count == 1
           && emptyArea.Outcomes[0].Key.Equals(key)
           && emptyArea.Outcomes[0].Reason == QuestFailureReason.NoNavigableHotspot
           && emptyArea.Outcomes[0].IsFailureEpisode,
        "an active generated area with no available hotspots must fail only the exact objective stage");

    monitor.Reset();
    var deathSample = WorkSample(key, new[] { 0 }, endpointA, active: false, deathAttributable: true);
    Assert(monitor.RecordDeath(deathSample).Outcomes.Count == 0,
        "the first attributable death must not end an episode");
    clock.Advance(TimeSpan.FromMinutes(5));
    Assert(monitor.RecordDeath(deathSample).Outcomes.Count == 0,
        "the second attributable death must not end an episode");
    clock.Advance(TimeSpan.FromMinutes(5));
    var third = monitor.RecordDeath(deathSample);
    Assert(third.Outcomes.Count == 1
           && third.Outcomes[0].Key.Equals(key)
           && third.Outcomes[0].Reason == QuestFailureReason.RepeatedDeaths
           && third.Outcomes[0].IsFailureEpisode,
        "the third attributable death inside 15 minutes must fail only the exact objective stage");
    Assert(monitor.RecordDeath(deathSample).Outcomes.Single().IsFailureEpisode == false,
        "additional deaths in the same no-progress episode must be observations");
}

void TestProgressMonitorResetsForEachSameKeyOwnershipGeneration()
{
    var root = Path.Combine(Path.GetTempPath(), "wholesome-generation-reset-" + Guid.NewGuid().ToString("N"));
    try
    {
        var clock = new TestRecoveryClock(utcNow);
        var manager = new QuestRecoveryManager(clock);
        manager.Configure(new QuestRecoveryEnvironment(root, "Wholesome", "Realm", "data-v1", "core-v1", "nav-v1"));
        var monitor = new WholesomeProgressMonitor(clock);
        var key = QuestRecoveryKey.ForQuestStage(990, QuestRecoveryStage.Objective);

        for (var expectedEpisode = 1; expectedEpisode <= 3; expectedEpisode++)
        {
            var owner = manager.TryBeginAttempt(key, new QuestRecoveryContext());
            Assert(owner.MayAttempt && owner.State == QuestRecoveryState.Attempting,
                "each elapsed cooldown must permit one same-key HalfOpen ownership generation");
            var update = monitor.Sample(WorkSample(
                key,
                new[] { 0 },
                cluster: null!,
                active: true,
                allHotspotsUnavailable: true,
                attemptGeneration: owner.AttemptGeneration));
            var failure = update.Outcomes.Single();
            Assert(failure.IsFailureEpisode,
                "a new ownership generation on the same key must begin a fresh monitor failure episode");
            manager.Report(
                QuestAttemptOutcome.Failure(
                    failure.Key,
                    key,
                    owner.AttemptGeneration,
                    failure.Reason,
                    failure.Evidence),
                new QuestRecoveryContext());
            Assert(manager.GetEntries().Single().EpisodeCount == expectedEpisode,
                "same-key HalfOpen failures must retain core escalation across fresh monitor generations");
            clock.Advance(expectedEpisode == 1 ? TimeSpan.FromMinutes(31) : TimeSpan.FromMinutes(61));
        }

        Assert(manager.GetEntries().Single().State == QuestRecoveryState.Quarantined,
            "the third exact-generation failure must quarantine without a manual monitor reset");
    }
    finally
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}

void TestProductionWorkSnapshotExcludesNonWorkAndRequiresExactOwner()
{
    var objective = new ForcedQuestObjective(TestQuestObjective.Create(867, new WoWPoint(80, 0, 0)));
    var exact = QuestRecoveryKey.ForQuestStage(867, QuestRecoveryStage.Objective);
    var other = QuestRecoveryKey.ForQuestStage(876, QuestRecoveryStage.Objective);
    var questPoi = new BotPoi(new WoWPoint(80, 0, 0), PoiType.Quest) { Entry = 867 };

    QuestWorkSample Map(
        QuestRecoveryKey owner,
        PoiType poiType = PoiType.Quest,
        bool inWorld = true,
        bool dead = false,
        bool ghost = false,
        bool taxi = false,
        bool transport = false,
        bool resting = false,
        bool paused = false,
        bool combat = false,
        bool questCombat = false) => WholesomeAutoQuest.CreateWorkSample(
            objective,
            owner,
            poiType == PoiType.Quest ? questPoi : new BotPoi(new WoWPoint(80, 0, 0), poiType),
            inWorld,
            dead,
            ghost,
            taxi,
            transport,
            resting,
            paused,
            combat,
            questCombat,
            new[] { 0 },
            exact,
            new[] { exact },
            endpointPathFailed: false,
            allHotspotsUnavailable: false,
            deathAttributable: true);

    Assert(Map(exact).IsActiveWork,
        "the production mapper must attribute active time to the exact objective behavior quest and stage");
    Assert(!Map(other).IsActiveWork
           && !Map(exact, PoiType.Buy).IsActiveWork
           && !Map(exact, PoiType.Sell).IsActiveWork
           && !Map(exact, PoiType.Repair).IsActiveWork
           && !Map(exact, PoiType.Mail).IsActiveWork
           && !Map(exact, PoiType.Train).IsActiveWork
           && !Map(exact, inWorld: false).IsActiveWork
           && !Map(exact, dead: true).IsActiveWork
           && !Map(exact, ghost: true).IsActiveWork
           && !Map(exact, taxi: true).IsActiveWork
           && !Map(exact, transport: true).IsActiveWork
           && !Map(exact, resting: true).IsActiveWork
           && !Map(exact, paused: true).IsActiveWork
           && !Map(exact, combat: true, questCombat: false).IsActiveWork,
        "loading, death/ghost, taxi/transport, vendor/repair/mail/trainer, rest, pause, unrelated combat, and another quest must not accrue work time");
    Assert(Map(exact, combat: true, questCombat: true).IsActiveWork,
        "combat proven attributable to the exact objective may accrue active work time");
    Assert(Map(exact, combat: true, questCombat: true).CombatOwnedByQuest
           && !Map(exact, combat: true, questCombat: false).CombatOwnedByQuest
           && !Map(other, combat: true, questCombat: true).CombatOwnedByQuest,
        "the production mapper must retain exact quest-combat ownership for pre-death attribution");
}

void TestDeathEventConsumesOnlyPreDeathOwnedQuestCombatSnapshot()
{
    var clock = new TestRecoveryClock(utcNow);
    var monitor = new WholesomeDeathMonitor(clock);
    var owner = new object();
    var key = QuestRecoveryKey.ForQuestStage(867, QuestRecoveryStage.Objective);
    var activeQuestCombat = WorkSample(
        key,
        new[] { 0 },
        QuestRecoveryKey.ForEndpoint(867, QuestRecoveryStage.Navigation, 1, "cell:0:0"),
        active: true,
        combatOwnedByQuest: true,
        attemptGeneration: 31);

    monitor.Capture(owner, key, 31, activeQuestCombat);
    Assert(monitor.TryRecordDeath(owner, key, 31, out var death)
           && death.DeathAttributable
           && death.Key.Equals(key)
           && death.AttemptGeneration == 31,
        "the death event must consume the exact pre-death active owned quest-combat snapshot after live POI state clears");
    Assert(!monitor.TryRecordDeath(owner, key, 31, out _),
        "one pre-death snapshot must count at most one death event");

    monitor.Capture(owner, key, 31, WorkSample(
        key, new[] { 0 }, cluster: null!, active: false,
        combatOwnedByQuest: true, attemptGeneration: 31));
    Assert(!monitor.TryRecordDeath(owner, key, 31, out _),
        "loading, rest, pause, death, or any other inactive state must not count merely because the character is nearby");
    monitor.Capture(owner, key, 31, WorkSample(
        key, new[] { 0 }, cluster: null!, active: true,
        combatOwnedByQuest: false, attemptGeneration: 31));
    Assert(!monitor.TryRecordDeath(owner, key, 31, out _),
        "unrelated combat must not count as an attributable death");

    monitor.Capture(owner, key, 31, activeQuestCombat);
    Assert(!monitor.TryRecordDeath(new object(), key, 31, out _),
        "a replaced behavior must not consume another behavior's death attribution");
    monitor.Capture(owner, key, 31, activeQuestCombat);
    Assert(!monitor.TryRecordDeath(owner, key, 32, out _),
        "a newer ownership generation must reject a stale pre-death snapshot");
    monitor.Capture(owner, key, 31, activeQuestCombat);
    clock.Advance(TimeSpan.FromSeconds(11));
    Assert(!monitor.TryRecordDeath(owner, key, 31, out _),
        "a stale nearby snapshot must not be treated as causal death evidence");
}

void TestAttemptOwnershipUsesTheExactGenerationToken()
{
    var ownership = new WholesomeAttemptOwnership();
    var owner = new object();
    var key = QuestRecoveryKey.ForQuestStage(867, QuestRecoveryStage.Objective);
    ownership.Begin(owner, key, new QuestRecoveryDecision
    {
        State = QuestRecoveryState.Attempting,
        MayAttempt = true,
        AttemptGeneration = 42
    });

    Assert(!ownership.TryComplete(new object(), "stale behavior", out _),
        "a replaced or unrelated behavior must not release another activation's ownership");
    Assert(ownership.TryComplete(owner, "objective progressed", out var success)
           && success.Kind == QuestAttemptOutcomeKind.Success
           && success.Key.Equals(key)
           && success.AttemptGeneration == 42,
        "successful Wholesome outcomes must carry the exact generation returned by TryBeginAttempt");
    Assert(!ownership.TryComplete(owner, "duplicate callback", out _),
        "a duplicate completion callback must be rejected after ownership is released");
}

void TestOwnedFailureBindsGenerationWithoutSyntheticSuccess()
{
    var ownership = new WholesomeAttemptOwnership();
    var owner = new object();
    var stageKey = QuestRecoveryKey.ForQuestStage(867, QuestRecoveryStage.Objective);
    var endpointKey = QuestRecoveryKey.ForEndpoint(867, QuestRecoveryStage.Navigation, 1, "cell:2:3");
    ownership.Begin(owner, stageKey, new QuestRecoveryDecision
    {
        State = QuestRecoveryState.Attempting,
        MayAttempt = true,
        AttemptGeneration = 51
    });
    var endpointFailure = QuestAttemptOutcome.Failure(
        endpointKey,
        QuestFailureReason.PathGenerationFailed,
        "no path to exact endpoint");

    Assert(ownership.TryBind(owner, endpointFailure, out var bound)
           && bound.Kind == QuestAttemptOutcomeKind.Failure
           && bound.Key.Equals(endpointKey)
           && bound.AttemptKey?.Equals(stageKey) == true
           && bound.AttemptGeneration == 51,
        "Wholesome must bind a failure directly to the exact active generation without emitting Success first");
    Assert(ownership.TryGet(owner, out _, out var stillOwnedGeneration)
           && stillOwnedGeneration == 51,
        "local ownership must remain until the manager atomically reports the generated failure");
    Assert(!ownership.Release(owner, 50)
           && ownership.Release(owner, 51)
           && !ownership.TryGet(owner, out _, out _),
        "local ownership must be removed only by the matching generation after manager Report returns");

    ownership.Begin(owner, stageKey, new QuestRecoveryDecision
    {
        State = QuestRecoveryState.Attempting,
        MayAttempt = true,
        AttemptGeneration = 52
    });
    var reports = new List<QuestAttemptOutcome>();
    var ownedDuringReport = false;
    Assert(WholesomeAutoQuest.ReportOwnedFailure(
               ownership,
               owner,
               endpointFailure,
               reported =>
               {
                   reports.Add(reported);
                   ownedDuringReport = ownership.TryGet(owner, out _, out var generation) && generation == 52;
                   return new QuestRecoveryDecision { State = QuestRecoveryState.CoolingDown };
               })
           && reports.Count == 1
           && reports[0].Kind == QuestAttemptOutcomeKind.Failure
           && ownedDuringReport
           && !ownership.TryGet(owner, out _, out _),
        "production failure reporting must call the manager once while local ownership remains, then release locally");
}

void TestProgressReleaseAllowsSameBehaviorToClaimALaterGeneration()
{
    var scheduler = new QuestScheduler(
        new DataLoader(Path.Combine(Path.GetTempPath(), "missing-wholesome-data.json")),
        new ProfileBuilder(Path.Combine(Path.GetTempPath(), "wholesome-progress-release.xml")),
        new WholesomeAQSettings());
    var behavior = new ForcedQuestObjective(TestQuestObjective.Create(867, new WoWPoint(80, 0, 0)));
    var calls = 0;
    QuestRecoveryDecision Begin(QuestRecoveryKey _) => new()
    {
        State = QuestRecoveryState.Attempting,
        MayAttempt = true,
        AttemptGeneration = ++calls
    };

    scheduler.ObserveActivation(behavior, Begin, _ => { }, () => { });
    scheduler.ReleaseActivation(behavior);
    scheduler.ObserveActivation(behavior, Begin, _ => { }, () => { });
    Assert(calls == 2,
        "objective progress must release the activation cache so the same live behavior can own a later exact attempt generation");
}

void TestCompletedOwnedStageRequestsRefreshBeforeQuestOrderRunsOut()
{
    var pickup = new ForcedQuestPickUp(867, "Pickup", 1001, "Giver", WoWPoint.Zero, null);
    var pickupKey = QuestRecoveryKey.ForNpc(867, QuestRecoveryStage.Pickup, 1001);
    var turnIn = new ForcedQuestTurnIn(867, "Turn in", 1002, "Ender", WoWPoint.Zero);
    var turnInKey = QuestRecoveryKey.ForNpc(867, QuestRecoveryStage.TurnIn, 1002);
    var objective = new ForcedQuestObjective(TestQuestObjective.Create(867, WoWPoint.Zero));
    var objectiveKey = QuestRecoveryKey.ForQuestStage(867, QuestRecoveryStage.Objective);

    Assert(WholesomeAutoQuest.IsCompletedOwnedStage(pickup, pickupKey, questAccepted: true, authoritativeCompleted: false, behaviorDone: true)
           && !WholesomeAutoQuest.IsCompletedOwnedStage(pickup, pickupKey, questAccepted: false, authoritativeCompleted: false, behaviorDone: true)
           && WholesomeAutoQuest.IsCompletedOwnedStage(turnIn, turnInKey, questAccepted: false, authoritativeCompleted: true, behaviorDone: true)
           && WholesomeAutoQuest.IsCompletedOwnedStage(objective, objectiveKey, questAccepted: true, authoritativeCompleted: false, behaviorDone: true),
        "a completed exact pickup, objective, or turn-in must queue its replacement profile before the current guarded order runs out");
    Assert(!WholesomeAutoQuest.IsCompletedOwnedStage(pickup, objectiveKey, questAccepted: true, authoritativeCompleted: false, behaviorDone: true),
        "stage completion must never be attributed through another quest-stage key");
}

void TestTimedIdleSuppressesOldQuestOrderUntilARebuildSelectsWork()
{
    var timedIdle = new QuestScheduleResult
    {
        FallbackMode = QuestFallbackMode.TimedIdle,
        EarliestRetryUtc = utcNow.AddMinutes(10)
    };
    var expanding = new QuestScheduleResult { FallbackMode = QuestFallbackMode.None };
    var selected = new QuestScheduleResult
    {
        Selected = new[] { Candidate(867, QuestWorkStage.Objective, 10, eligible: true) }
    };

    Assert(!WholesomeAutoQuest.ShouldExecuteQuestRoot(stopped: false, timedIdle)
           && !WholesomeAutoQuest.ShouldExecuteQuestRoot(stopped: false, expanding)
           && WholesomeAutoQuest.ShouldExecuteQuestRoot(stopped: false, selected)
           && !WholesomeAutoQuest.ShouldExecuteQuestRoot(stopped: true, selected),
        "no eligible work must suppress the stale generated quest order until a coalesced rebuild selects work");
}

void TestPickupOutcomeCoalescingStillReportsTheFailureEpisode()
{
    var key = QuestRecoveryKey.ForNpc(867, QuestRecoveryStage.Pickup, 1001);
    var observation = QuestAttemptOutcome.Observation(
        key, QuestFailureReason.PickupWrongQuestShown, "same dialog evidence");
    var repeatedObservation = QuestAttemptOutcome.Observation(
        key, QuestFailureReason.PickupWrongQuestShown, "same dialog evidence");
    var failure = QuestAttemptOutcome.Failure(
        key, QuestFailureReason.PickupWrongQuestShown, "same dialog evidence");

    Assert(WholesomeAutoQuest.PickupOutcomeFingerprint(observation) ==
           WholesomeAutoQuest.PickupOutcomeFingerprint(repeatedObservation),
        "repeated samples of one pickup mismatch must coalesce");
    Assert(WholesomeAutoQuest.PickupOutcomeFingerprint(observation) !=
           WholesomeAutoQuest.PickupOutcomeFingerprint(failure),
        "the third-cycle failure episode must not be hidden by an earlier observation with identical dialog evidence");
}

void TestPickupRecoveryRequiresThreeDistinctActiveOwnedCycles()
{
    var monitor = new WholesomePickupMonitor();
    var pickup = new ForcedQuestPickUp(867, "Pickup", 1001, "Giver", new WoWPoint(20, 30, 0), null);
    var key = QuestRecoveryKey.ForNpc(867, QuestRecoveryStage.Pickup, 1001);
    var other = QuestRecoveryKey.ForNpc(876, QuestRecoveryStage.Pickup, 1002);
    var poi = new BotPoi(new WoWPoint(20, 30, 0), PoiType.QuestPickUp) { Entry = 1001 };
    var observation = QuestAttemptOutcome.Observation(
        key, QuestFailureReason.PickupWrongQuestShown, "wrong quest dialog");
    var failure = QuestAttemptOutcome.Failure(
        key, QuestFailureReason.PickupWrongQuestShown, "wrong quest dialog");

    bool Active(
        QuestRecoveryKey owner,
        long generation = 7,
        bool managerOwnsAttempt = true,
        bool inWorld = true,
        bool dead = false,
        bool resting = false,
        bool paused = false,
        bool combat = false,
        PoiType poiType = PoiType.QuestPickUp) => WholesomeAutoQuest.IsPickupRecoveryActive(
            pickup,
            owner,
            generation,
            managerOwnsAttempt,
            poiType == PoiType.QuestPickUp ? poi : new BotPoi(WoWPoint.Zero, poiType),
            inWorld,
            dead,
            ghost: false,
            onTaxi: false,
            onTransport: false,
            resting,
            paused,
            combat);

    Assert(Active(key)
           && !Active(other)
           && !Active(key, generation: 0)
           && !Active(key, managerOwnsAttempt: false)
           && !Active(key, inWorld: false)
           && !Active(key, dead: true)
           && !Active(key, resting: true)
           && !Active(key, paused: true)
           && !Active(key, combat: true)
           && !Active(key, poiType: PoiType.Repair),
        "pickup recovery evidence must require exact active ownership and exclude loading, cooling/denied, death, rest, pause, combat, other quests, and non-pickup work");

    Assert(monitor.Observe(key, 7, interactionCycle: 1, observation, active: false) == null
           && monitor.Observe(key, 7, interactionCycle: 2, failure, active: false) == null,
        "excluded pickup cycles must not count toward failure");
    Assert(monitor.Observe(key, 7, interactionCycle: 3, observation, active: true) == observation
           && monitor.Observe(key, 7, interactionCycle: 3, observation, active: true) == null,
        "one real active interaction cycle may report one observation but duplicate pulses must not count");
    Assert(monitor.Observe(key, 7, interactionCycle: 4, observation, active: true) == observation,
        "the second distinct active cycle must remain an observation");
    Assert(monitor.Observe(key, 7, interactionCycle: 5, failure, active: true) == failure,
        "only the third distinct active interaction cycle may report pickup failure");

    monitor.Reset();
    Assert(monitor.Observe(key, 7, interactionCycle: 6, failure, active: true) == null,
        "lifecycle reset must discard pickup target, generation, interaction token/count, and failure state");
    Assert(typeof(ForcedQuestPickUp).GetProperty("InteractionCycleId") != null,
        "production pickup behavior must expose its existing real interaction-cycle token read-only");
}

QuestWorkSample WorkSample(
    QuestRecoveryKey key,
    IReadOnlyList<int> counts,
    QuestRecoveryKey cluster,
    bool active,
    bool endpointPathFailed = false,
    IReadOnlyList<QuestRecoveryKey>? knownEndpoints = null,
    bool allHotspotsUnavailable = false,
    bool deathAttributable = false,
    bool combatOwnedByQuest = false,
    long attemptGeneration = 1) => new()
{
    Key = key,
    AttemptGeneration = attemptGeneration,
    ObjectiveCounts = counts,
    ClusterKey = cluster,
    IsActiveWork = active,
    EndpointPathFailed = endpointPathFailed,
    KnownEndpointKeys = knownEndpoints ?? Array.Empty<QuestRecoveryKey>(),
    AllHotspotsUnavailable = allHotspotsUnavailable,
    DeathAttributable = deathAttributable,
    CombatOwnedByQuest = combatOwnedByQuest
};

void TestProductionCallerNoLongerNeedsBlacklistCompatibilityAdapters()
{
    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    var sync = typeof(QuestScheduler).GetMethod("SyncBlacklist", flags);
    var giver = typeof(QuestScheduler).GetMethod("BlacklistQuestGiver", flags);

    Assert(typeof(WholesomeAutoQuest).Assembly == typeof(QuestScheduler).Assembly,
        "the production-linked regression must compile the actual bot caller with the scheduler");
    Assert(sync == null && giver == null,
        "Task 4 must remove temporary blacklist compatibility adapters after converting the real caller to scoped outcomes");
}

void TestOrdinaryObjectiveEndpointPrecedesHalfOpenAlternative()
{
    var db = SchedulerDatabase();
    var nearHalfOpen = QuestScheduler.EndpointKey(
        867, QuestRecoveryStage.Navigation, new SpawnPoint { Map = 1, X = 1, Y = 1 });
    var result = QuestScheduler.MaterializeSchedule(
        db,
        Snapshot(new[] { Accepted(867, completed: false) }, Array.Empty<uint>()),
        key => key.Equals(nearHalfOpen) ? HalfOpen() : Eligible(),
        10,
        500,
        7);

    var selected = result.Selected.Single(candidate => candidate.QuestId == 867);
    var objective = result.Plan.Single(entry => entry.Quest.Id == 867);
    Assert(selected.Stage == QuestWorkStage.Objective,
        "an ordinary objective endpoint must keep the whole quest ordinary when another endpoint is half-open");
    Assert(objective.Hotspots.All(point => point.X >= 80),
        "half-open objective endpoints may be retained only when the candidate has no ordinary endpoint path");
}

void TestOrdinaryRelationPrecedesHalfOpenAlternative()
{
    var db = SchedulerDatabase();
    var result = QuestScheduler.MaterializeSchedule(
        db,
        Snapshot(Array.Empty<QuestSchedulerAcceptedQuest>(), Array.Empty<uint>()),
        key => key.Scope == QuestRecoveryScope.NpcRelation && key.NpcEntry == 1001
            ? HalfOpen()
            : Eligible(),
        10,
        500,
        7);

    var selected = result.Selected.Single(candidate => candidate.QuestId == 876);
    Assert(selected.Stage == QuestWorkStage.Pickup,
        "an ordinary giver relation must keep pickup ordinary when another giver is half-open");
    var giverPlan = result.Plan.Single(entry => entry.Giver?.GiverId == 1002);
    Assert(result.Plan.All(entry => entry.Giver?.GiverId != 1001)
           && giverPlan.Hotspots.Single().X == 20,
        "a half-open giver may be retained only when no ordinary giver path exists for the candidate");
}

void TestCompletedObjectivesDoNotConsumeEndpointBudget()
{
    var db = SchedulerDatabase();
    var quest = db.Quests.Single(entry => entry.Id == 867);
    quest.Objectives.Add(new QuestObjective
    {
        Index = 1,
        Type = ObjectiveType.KillMob,
        MobId = 2002,
        KillCount = 1
    });
    db.CreatureSpawns["2000"] = Enumerable.Range(0, 6)
        .Select(index => new SpawnPoint { Map = 1, X = index * 80 + 1, Y = 1 })
        .ToList();
    db.CreatureSpawns["2002"] = new List<SpawnPoint>
    {
        new() { Map = 1, X = 501, Y = 41 }
    };

    var result = QuestScheduler.MaterializeSchedule(
        db,
        Snapshot(new[] { AcceptedWithCounts(867, 2, 0) }, Array.Empty<uint>()),
        _ => Eligible(),
        10,
        1000,
        7);

    var objectivePlan = result.Plan.Where(entry => entry.Quest.Id == 867).ToArray();
    Assert(objectivePlan.Length == 1
           && objectivePlan[0].ObjectiveIndex == 1
           && objectivePlan[0].Hotspots.Single().X == 501,
        "completed objective clusters must be skipped before incomplete work consumes the five-key endpoint budget");
}

void TestUnavailableObjectiveCountsDoNotAdvanceWork()
{
    var result = QuestScheduler.MaterializeSchedule(
        SchedulerDatabase(),
        Snapshot(new[]
        {
            new QuestSchedulerAcceptedQuest
            {
                QuestId = 867,
                IsCompleted = false,
                ObjectiveCounts = Array.Empty<int>()
            }
        }, Array.Empty<uint>()),
        _ => Eligible(),
        10,
        500,
        7);

    Assert(result.Plan.Any(entry => entry.Quest.Id == 867 && entry.ObjectiveIndex == 0),
        "unavailable live objective counts must conservatively retain work instead of falsely advancing it");
}

void TestScanExpansionPrecedesFallbackAndResets()
{
    var settings = new WholesomeAQSettings
    {
        ScanStartDistance = 100,
        ScanStep = 100,
        ScanMaxDistance = 300
    };
    var scheduler = new QuestScheduler(
        new DataLoader(Path.Combine(Path.GetTempPath(), "missing-wholesome-data.json")),
        new ProfileBuilder(Path.Combine(Path.GetTempPath(), "wholesome-scan-expansion.xml")),
        settings);
    var noSelection = new QuestScheduleResult
    {
        FallbackMode = QuestFallbackMode.TimedIdle,
        EarliestRetryUtc = utcNow.AddMinutes(5),
        Status = "no work"
    };

    var first = scheduler.ApplyScanExpansionBeforeFallback(noSelection);
    var firstThreshold = scheduler.ScanThreshold;
    var second = scheduler.ApplyScanExpansionBeforeFallback(noSelection);
    var secondThreshold = scheduler.ScanThreshold;
    var atMaximum = scheduler.ApplyScanExpansionBeforeFallback(noSelection);
    Assert(first.FallbackMode == QuestFallbackMode.None && firstThreshold == 200,
        "the first empty scan must suppress fallback and advance by ScanStep");
    Assert(second.FallbackMode == QuestFallbackMode.None && secondThreshold == 300,
        "each pre-maximum empty scan must suppress fallback while expanding");
    Assert(atMaximum.FallbackMode == QuestFallbackMode.TimedIdle
           && atMaximum.EarliestRetryUtc == noSelection.EarliestRetryUtc,
        "fallback is allowed only after a scan has already run at ScanMaxDistance");

    var selected = new QuestScheduleResult
    {
        Selected = new[] { Candidate(867, QuestWorkStage.Objective, 10, eligible: true) }
    };
    scheduler.ApplyScanExpansionBeforeFallback(selected);
    Assert(scheduler.ScanThreshold == settings.ScanStartDistance,
        "successful selection must reset the scan threshold to its configured start");
    scheduler.ApplyScanExpansionBeforeFallback(noSelection);
    scheduler.Reset();
    Assert(scheduler.ScanThreshold == settings.ScanStartDistance,
        "scheduler lifecycle reset must also restore the configured start threshold");
}

void TestProductionActivationClaimsOnceAndCoalescesRebuild()
{
    var scheduler = new QuestScheduler(
        new DataLoader(Path.Combine(Path.GetTempPath(), "missing-wholesome-data.json")),
        new ProfileBuilder(Path.Combine(Path.GetTempPath(), "wholesome-activation.xml")),
        new WholesomeAQSettings());
    var pickup = new ForcedQuestPickUp(867, "Pickup", 1001, "Giver", WoWPoint.Zero, null);
    var beginCalls = 0;
    var rebuilds = 0;
    var cleared = new List<QuestRecoveryKey>();
    Func<QuestRecoveryKey, QuestRecoveryDecision> deny = key =>
    {
        beginCalls++;
        Assert(key.Equals(QuestRecoveryKey.ForNpc(867, QuestRecoveryStage.Pickup, 1001)),
            "the production activation path must claim the exact pickup relation key");
        return Cooling(utcNow.AddMinutes(2));
    };

    scheduler.ObserveActivation(pickup, deny, cleared.Add, () => rebuilds++);
    scheduler.ObserveActivation(pickup, deny, cleared.Add, () => rebuilds++);
    Assert(beginCalls == 1 && cleared.Count == 1 && rebuilds == 1,
        "one active behavior must claim once across repeated pulses and request one rebuild when denied");

    var replacement = new ForcedQuestPickUp(867, "Pickup", 1001, "Giver", WoWPoint.Zero, null);
    scheduler.ObserveActivation(replacement, deny, cleared.Add, () => rebuilds++);
    Assert(beginCalls == 2 && cleared.Count == 2 && rebuilds == 1,
        "a replacement activation may retry the exact claim while rebuild requests remain coalesced");

    scheduler.ApplyScanExpansionBeforeFallback(new QuestScheduleResult());
    var accepted = new ForcedQuestPickUp(876, "Pickup 2", 1002, "Giver 2", WoWPoint.Zero, null);
    scheduler.ObserveActivation(
        accepted,
        key =>
        {
            beginCalls++;
            return new QuestRecoveryDecision
            {
                State = QuestRecoveryState.Attempting,
                MayAttempt = true,
                AttemptGeneration = 1
            };
        },
        cleared.Add,
        () => rebuilds++);
    scheduler.ObserveActivation(accepted, deny, cleared.Add, () => rebuilds++);
    Assert(beginCalls == 3 && cleared.Count == 2 && rebuilds == 1,
        "a won activation claim must not clear its POI, rebuild, or claim again on later pulses");
}

void TestEndpointSafetyAndReachabilityPrecedeDistanceAndCap()
{
    var endpoints = new[]
    {
        RankedEndpoint("near-unsafe", distance: 1, safety: 100, knownReachable: true, knownSafe: false),
        RankedEndpoint("near-unreachable", distance: 2, safety: 100, knownReachable: false, knownSafe: true),
        RankedEndpoint("near-low-a", distance: 3, safety: 1, knownReachable: true, knownSafe: true),
        RankedEndpoint("near-low-b", distance: 4, safety: 1, knownReachable: true, knownSafe: true),
        RankedEndpoint("near-low-c", distance: 5, safety: 1, knownReachable: true, knownSafe: true),
        RankedEndpoint("near-low-d", distance: 6, safety: 1, knownReachable: true, knownSafe: true),
        RankedEndpoint("near-low-e", distance: 7, safety: 1, knownReachable: true, knownSafe: true),
        RankedEndpoint("far-safe", distance: 100, safety: 10, knownReachable: true, knownSafe: true)
    };

    var selected = QuestSchedulingPolicy.Select(endpoints, maximum: 5);
    Assert(selected.Count == 5
           && selected[0].Key.Endpoint == "far-safe"
           && selected.All(endpoint => endpoint.Key.Endpoint != "near-unsafe")
           && selected.All(endpoint => endpoint.Key.Endpoint != "near-unreachable")
           && selected.Any(endpoint => endpoint.Key.Endpoint == "near-low-a")
           && selected.All(endpoint => endpoint.IsKnownReachable == true && endpoint.IsKnownSafe == true),
        "known unreachable endpoints must be filtered and safety must outrank distance before the distinct five-key cap");
}

void TestSchedulerFiltersKnownNavigationUnsafePointsBeforeCap()
{
    var db = SchedulerDatabase();
    db.CreatureSpawns["2000"] = Enumerable.Range(0, 7)
        .Select(index => new SpawnPoint
        {
            Map = 1,
            X = index * 80 + 1,
            Y = 1,
            SafetyScore = index == 6 ? 10 : 1
        })
        .ToList();

    var result = QuestScheduler.MaterializeSchedule(
        db,
        Snapshot(new[] { Accepted(867, completed: false) }, Array.Empty<uint>()),
        _ => Eligible(),
        10,
        1000,
        7,
        isKnownUnsafe: point => point.X == 1);
    var hotspots = result.Plan.Single(entry => entry.Quest.Id == 867).Hotspots;

    Assert(hotspots.Count == 5
           && hotspots.All(point => point.X != 1)
           && hotspots.Any(point => point.X == 481),
        "the scheduler must remove known navigation-unsafe points before safety ordering and the five-key cap");
}

void TestLoadedSpawnsReceiveCachedLiveNavigationEnrichment()
{
    var path = Path.Combine(Path.GetTempPath(), $"wholesome-live-nav-{Guid.NewGuid():N}.json");
    File.WriteAllText(path, """
        {
          "Quests": [{
            "Id": 867,
            "Name": "Loaded objective",
            "MinLevel": 1,
            "QuestLevel": 20,
            "Objectives": [{ "Index": 0, "Type": "KillMob", "MobId": 2000, "KillCount": 1 }]
          }],
          "CreatureSpawns": {
            "2000": [
              { "Map": 1, "X": 1,   "Y": 1, "Z": 0 },
              { "Map": 1, "X": 10,  "Y": 2, "Z": 0 },
              { "Map": 1, "X": 81,  "Y": 1, "Z": 0 },
              { "Map": 1, "X": 161, "Y": 1, "Z": 0 },
              { "Map": 1, "X": 241, "Y": 1, "Z": 0 },
              { "Map": 1, "X": 321, "Y": 1, "Z": 0 },
              { "Map": 1, "X": 481, "Y": 1, "Z": 0 },
              { "Map": 1, "X": 561, "Y": 1, "Z": 0 }
            ]
          }
        }
        """);

    var previousProvider = Navigator.NavigationProvider;
    var provider = new TestNavigationProvider(destination =>
    {
        if (destination.X >= 560)
            throw new InvalidOperationException("provider unavailable");
        if (destination.X >= 160 && destination.X < 400)
            return null;
        return destination.X + 10;
    });
    try
    {
        var loader = new DataLoader(path);
        var db = loader.Load();
        Assert(db != null && db.CreatureSpawns["2000"].All(point =>
                point.IsKnownReachable == null && point.IsKnownSafe == null),
            "unannotated loaded spawn records must remain unknown until live navigation enrichment");

        Navigator.NavigationProvider = provider;
        var enrichmentCalls = 0;
        var result = QuestScheduler.MaterializeSchedule(
            db!,
            Snapshot(new[] { Accepted(867, completed: false) }, Array.Empty<uint>()),
            _ => Eligible(),
            10,
            1000,
            7,
            navigationAssessment: point =>
            {
                enrichmentCalls++;
                return QuestScheduler.AssessNavigation(
                    point,
                    new WoWPoint(0, 0, 0),
                    location => location.X < 160);
            });
        var hotspots = result.Plan.Single(entry => entry.Quest.Id == 867).Hotspots;
        var providerException = QuestScheduler.AssessNavigation(
            new SpawnPoint { Map = 1, X = 561, Y = 1 },
            new WoWPoint(0, 0, 0),
            _ => false);

        Assert(enrichmentCalls == 7,
            "live enrichment must run once per distinct quantized cluster per scan, not once per spawn record or comparator call");
        Assert(hotspots.Any(point => point.X == 481)
               && hotspots.All(point => point.X >= 400),
            "five nearer live-unsafe or unreachable clusters must not hide a farther confirmed safe and reachable cluster");
        Assert(providerException.IsKnownReachable == null
               && providerException.IsKnownSafe == null,
            "a navigation-provider exception must remain unknown instead of being labeled safe or reachable");
    }
    finally
    {
        Navigator.NavigationProvider = previousProvider;
        File.Delete(path);
    }
}

void TestConfirmedEndpointPrecedesUnknownFallback()
{
    var endpoints = Enumerable.Range(1, 5)
        .Select(index => new QuestEndpointCandidate
        {
            Key = QuestRecoveryKey.ForEndpoint(867, QuestRecoveryStage.Navigation, 1, $"unknown-{index}"),
            Point = new SpawnPoint { Map = 1, X = index },
            Distance = index,
            IsKnownReachable = null,
            IsKnownSafe = null,
            SafetyScore = 100,
            Recovery = Eligible()
        })
        .Append(new QuestEndpointCandidate
        {
            Key = QuestRecoveryKey.ForEndpoint(867, QuestRecoveryStage.Navigation, 1, "confirmed"),
            Point = new SpawnPoint { Map = 1, X = 100 },
            Distance = 100,
            IsKnownReachable = true,
            IsKnownSafe = true,
            SafetyScore = 1,
            Recovery = Eligible()
        });

    var selected = QuestSchedulingPolicy.Select(endpoints, maximum: 5);
    Assert(selected.Count == 5
           && selected[0].Key.Endpoint == "confirmed"
           && selected.Count(endpoint => endpoint.Key.Endpoint.StartsWith("unknown-", StringComparison.Ordinal)) == 4,
        "confirmed safe and reachable endpoints must precede unknown fallback endpoints before the five-key cap");
}

void TestEmbeddedQuestPoiRequiresExactCurrentEndpoint()
{
    var currentLocation = new WoWPoint(20, 30, 0);
    var staleLocation = new WoWPoint(220, 330, 0);
    var pickup = new ForcedQuestPickUp(867, "Pickup", 1001, "Giver", currentLocation, null);
    var pickupKey = QuestRecoveryKey.ForNpc(867, QuestRecoveryStage.Pickup, 1001);
    var matchingPickup = new BotPoi(new Styx.Logic.Profiles.Quest.PickUpNode(
        currentLocation, 1001, "Giver", null, 867, "Pickup"));
    var stalePickup = new BotPoi(new Styx.Logic.Profiles.Quest.PickUpNode(
        staleLocation, 1001, "Giver", null, 867, "Pickup"));
    var clearCalls = 0;

    Assert(WholesomeAutoQuest.TryClearDeniedRecoveryPoi(
               pickup, pickupKey, pickup, matchingPickup, () => clearCalls++)
           && clearCalls == 1,
        "a denied pickup claim must clear an embedded pickup node at the exact current endpoint");
    Assert(!WholesomeAutoQuest.TryClearDeniedRecoveryPoi(
               pickup, pickupKey, pickup, stalePickup, () => clearCalls++)
           && stalePickup.AsPickUp != null
           && clearCalls == 1,
        "an embedded pickup node for the same quest and NPC at a stale alternate endpoint must not be cleared");

    var turnIn = new ForcedQuestTurnIn(867, "Turn in", 1002, "Ender", currentLocation);
    var turnInKey = QuestRecoveryKey.ForNpc(867, QuestRecoveryStage.TurnIn, 1002);
    var matchingTurnIn = new BotPoi(new Styx.Logic.Profiles.Quest.TurnInNode(
        currentLocation, 1002, "Ender", null, 867, "Turn in"));
    var staleTurnIn = new BotPoi(new Styx.Logic.Profiles.Quest.TurnInNode(
        staleLocation, 1002, "Ender", null, 867, "Turn in"));
    Assert(WholesomeAutoQuest.TryClearDeniedRecoveryPoi(
               turnIn, turnInKey, turnIn, matchingTurnIn, () => clearCalls++)
           && clearCalls == 2,
        "a denied turn-in claim must clear an embedded turn-in node at the exact current endpoint");
    Assert(!WholesomeAutoQuest.TryClearDeniedRecoveryPoi(
               turnIn, turnInKey, turnIn, staleTurnIn, () => clearCalls++)
           && staleTurnIn.AsTurnIn != null
           && clearCalls == 2,
        "an embedded turn-in node for the same quest and NPC at a stale alternate endpoint must not be cleared");
}

void TestDeniedActivationLeavesUnownedPoiUntouched()
{
    var location = new WoWPoint(20, 30, 0);
    var behavior = new ForcedQuestPickUp(867, "Pickup", 1001, "Giver", location, null);
    var key = QuestRecoveryKey.ForNpc(867, QuestRecoveryStage.Pickup, 1001);
    var matching = new BotPoi(new Styx.Logic.Profiles.Quest.PickUpNode(
        location, 1001, "Giver", null, 867, "Pickup"));
    var clearCalls = 0;

    var combat = new BotPoi(location, PoiType.Kill) { Entry = 1001 };
    var vendor = new BotPoi(location, PoiType.Repair) { Entry = 1001 };
    var staleEndpoint = new BotPoi(new WoWPoint(500, 500, 0), PoiType.QuestPickUp) { Entry = 1001 };
    var staleBehavior = new ForcedQuestPickUp(867, "Pickup", 1001, "Giver", location, null);
    Assert(!WholesomeAutoQuest.TryClearDeniedRecoveryPoi(behavior, key, behavior, combat, () => clearCalls++)
           && !WholesomeAutoQuest.TryClearDeniedRecoveryPoi(behavior, key, behavior, vendor, () => clearCalls++)
           && !WholesomeAutoQuest.TryClearDeniedRecoveryPoi(behavior, key, behavior, staleEndpoint, () => clearCalls++)
           && !WholesomeAutoQuest.TryClearDeniedRecoveryPoi(behavior, key, staleBehavior, matching, () => clearCalls++)
           && clearCalls == 0,
        "denied claims must not clear combat, vendor, stale-endpoint, or no-longer-current behavior POIs");

    var objective = new ForcedQuestObjective(TestQuestObjective.Create(867, location));
    var objectiveKey = QuestRecoveryKey.ForQuestStage(867, QuestRecoveryStage.Objective);
    var coincidentHotspot = new BotPoi(location, PoiType.Hotspot);
    Assert(!WholesomeAutoQuest.TryClearDeniedRecoveryPoi(
               objective, objectiveKey, objective, coincidentHotspot, () => clearCalls++)
           && clearCalls == 0,
        "an untagged generic objective hotspot must not be cleared even when it is coincident with the objective location");
}

void TestOutcomePoiCleanupUsesTheReportedFailureScope()
{
    var location = new WoWPoint(20, 30, 0);
    var objective = new ForcedQuestObjective(TestQuestObjective.Create(867, location));
    var stageKey = QuestRecoveryKey.ForQuestStage(867, QuestRecoveryStage.Objective);
    var endpointKey = QuestRecoveryKey.ForEndpoint(867, QuestRecoveryStage.Navigation, 1, "cell:0:0");
    var questPoi = new BotPoi(location, PoiType.Quest) { Entry = 867 };
    var clears = 0;

    Assert(!WholesomeAutoQuest.TryClearRecoveryOutcomePoi(
               objective, endpointKey, objective, questPoi, () => clears++)
           && clears == 0,
        "an endpoint-only path failure must not clear a broader objective-stage POI");
    Assert(WholesomeAutoQuest.TryClearRecoveryOutcomePoi(
               objective, stageKey, objective, questPoi, () => clears++)
           && clears == 1,
        "an exact objective-stage failure may clear its proven quest-owned POI");
}

void TestStagePriority()
{
    var result = QuestSchedulingPolicy.Select(new[]
    {
        Candidate(100, QuestWorkStage.Pickup, distance: 10, eligible: true),
        Candidate(200, QuestWorkStage.Objective, distance: 200, eligible: true),
        Candidate(300, QuestWorkStage.TurnIn, distance: 500, eligible: true)
    }, maximum: 10, utcNow);

    Assert(result.Selected.Select(x => x.QuestId).SequenceEqual(new uint[] { 300, 200, 100 }),
        "turn-in and accepted objective work must rank before pickup");
}

void TestStageSpecificCooldown()
{
    var pickupRetry = utcNow.AddMinutes(15);
    var result = QuestSchedulingPolicy.Select(new[]
    {
        Candidate(867, QuestWorkStage.Pickup, distance: 10, eligible: false, pickupRetry),
        Candidate(867, QuestWorkStage.Objective, distance: 100, eligible: true)
    }, maximum: 10, utcNow);

    Assert(result.Selected.Count == 1
           && result.Selected[0].QuestId == 867
           && result.Selected[0].Stage == QuestWorkStage.Objective,
        "cooling a pickup stage must leave the same quest's accepted objective stage eligible");
    Assert(result.EarliestRetryUtc == pickupRetry,
        "an excluded stage's retry time must remain visible while other work proceeds");
}

void TestEndpointCooldownIsolation()
{
    var selected = QuestSchedulingPolicy.Select(new[]
    {
        Endpoint(867, "giver-a", distance: 5, eligible: false),
        Endpoint(867, "giver-b", distance: 25, eligible: true)
    }, maximum: 5);

    Assert(selected.Count == 1 && selected[0].Key.Endpoint == "giver-b",
        "cooling one endpoint must leave another endpoint selectable");
}

void TestEndpointAttemptCap()
{
    var selected = QuestSchedulingPolicy.Select(
        Enumerable.Range(1, 7)
            .Select(index => Endpoint(867, $"endpoint-{index}", index, eligible: true)),
        maximum: 20);

    Assert(selected.Count == 5
           && selected.Select(x => x.Key.Endpoint).SequenceEqual(new[]
           {
               "endpoint-1", "endpoint-2", "endpoint-3", "endpoint-4", "endpoint-5"
           }),
        "one scheduling episode must try no more than the five nearest eligible endpoints");
}

void TestEndpointAttemptCapCountsDistinctRecoveryKeys()
{
    var selected = QuestSchedulingPolicy.Select(new[]
    {
        Endpoint(867, "duplicate", distance: 1, eligible: true),
        Endpoint(867, "duplicate", distance: 2, eligible: true),
        Endpoint(867, "duplicate", distance: 3, eligible: true),
        Endpoint(867, "duplicate", distance: 4, eligible: true),
        Endpoint(867, "duplicate", distance: 5, eligible: true),
        Endpoint(867, "alternative-a", distance: 6, eligible: true),
        Endpoint(867, "alternative-b", distance: 7, eligible: true)
    }, maximum: 5);

    Assert(selected.Select(candidate => candidate.Key.Endpoint).SequenceEqual(new[]
           {
               "duplicate", "alternative-a", "alternative-b"
           })
           && selected.Select(candidate => candidate.Distance).SequenceEqual(new[] { 1d, 6d, 7d }),
        "the endpoint cap must count exact recovery keys once and retain the first sorted row");
}

void TestOrdinaryWorkPrecedesHalfOpenProbe()
{
    var result = QuestSchedulingPolicy.Select(new[]
    {
        Candidate(100, QuestWorkStage.Pickup, distance: 500, eligible: true),
        Candidate(200, QuestWorkStage.HalfOpen, distance: 1, eligible: true)
    }, maximum: 10, utcNow);

    Assert(result.Selected.Count == 1
           && result.Selected[0].QuestId == 100
           && result.Selected[0].Stage == QuestWorkStage.Pickup,
        "ordinary eligible work must win over a due half-open probe");
}

void TestNoWorkReturnsEarliestRetry()
{
    var earliest = utcNow.AddMinutes(10);
    var result = QuestSchedulingPolicy.Select(new[]
    {
        Candidate(100, QuestWorkStage.Pickup, distance: 10, eligible: false, utcNow.AddMinutes(15)),
        Candidate(200, QuestWorkStage.Objective, distance: 20, eligible: false, earliest)
    }, maximum: 10, utcNow);

    Assert(result.Selected.Count == 0
           && result.FallbackMode == QuestFallbackMode.TimedIdle
           && result.EarliestRetryUtc == earliest,
        "no eligible candidates must produce timed idle until the earliest retry, not a restart request");
}

void TestCandidateTieBreakers()
{
    var result = QuestSchedulingPolicy.Select(new[]
    {
        Candidate(400, QuestWorkStage.Objective, distance: 5, eligible: true, safety: 1, chain: 50),
        Candidate(300, QuestWorkStage.Objective, distance: 50, eligible: true, safety: 2, chain: 1),
        Candidate(200, QuestWorkStage.Objective, distance: 20, eligible: true, safety: 2, chain: 10),
        Candidate(100, QuestWorkStage.Objective, distance: 20, eligible: true, safety: 2, chain: 10)
    }, maximum: 10, utcNow);

    Assert(result.Selected.Select(x => x.QuestId).SequenceEqual(new uint[] { 100, 200, 300, 400 }),
        "equal-stage work must sort by safety, chain value, distance, then quest ID");
}

void TestValidatedGrindFallbackRequiresVettedPath()
{
    var validated = QuestSchedulingPolicy.Select(
        Array.Empty<QuestWorkCandidate>(), maximum: 10, utcNow, "Profiles/vetted.xml");
    var whitespace = QuestSchedulingPolicy.Select(
        Array.Empty<QuestWorkCandidate>(), maximum: 10, utcNow, "   ");

    Assert(validated.FallbackMode == QuestFallbackMode.ValidatedGrind
           && validated.ValidatedGrindProfilePath == "Profiles/vetted.xml",
        "a caller-vetted grind profile may be returned when quest work is unavailable");
    Assert(whitespace.FallbackMode == QuestFallbackMode.TimedIdle
           && string.IsNullOrEmpty(whitespace.ValidatedGrindProfilePath),
        "a blank grind profile path must never be treated as a validated fallback");
}

void TestDatasetFingerprintIsDeterministic()
{
    var directory = Path.Combine(Path.GetTempPath(), $"wholesome-dataset-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var path = Path.Combine(directory, "quest_data.json");
        File.WriteAllText(path, "{\"Quests\":[],\"QuestGivers\":[],\"QuestEnders\":[],\"CreatureSpawns\":{},\"GameObjectSpawns\":{}}");
        var stamp = new DateTime(2026, 9, 3, 1, 2, 3, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, stamp);

        var first = new DataLoader(path);
        var second = new DataLoader(path);
        Assert(first.Load() != null && second.Load() != null, "the controlled quest datasets must load");
        Assert(first.DatasetFingerprint == second.DatasetFingerprint
               && first.DatasetFingerprint != "unknown",
            "equal ordered path/length/timestamp metadata must produce one deterministic dataset fingerprint");

        File.SetLastWriteTimeUtc(path, stamp.AddSeconds(1));
        var changed = new DataLoader(path);
        changed.Load();
        Assert(changed.DatasetFingerprint != first.DatasetFingerprint,
            "changing dataset metadata must change the dataset fingerprint");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

void TestSchedulerRetainsEligibleAlternatesAndReportsExactExclusions()
{
    var giverRetry = utcNow.AddMinutes(15);
    var clusterRetry = utcNow.AddMinutes(10);
    var db = SchedulerDatabase();
    var snapshot = Snapshot(
        accepted: new[] { Accepted(867, completed: false) },
        completed: Array.Empty<uint>());
    var cooledCluster = QuestScheduler.EndpointKey(
        867, QuestRecoveryStage.Navigation, new SpawnPoint { Map = 1, X = 1, Y = 1 });

    var result = QuestScheduler.MaterializeSchedule(
        db,
        snapshot,
        key => key.Scope == QuestRecoveryScope.NpcRelation && key.NpcEntry == 1001
            ? Cooling(giverRetry)
            : key.Equals(cooledCluster) ? Cooling(clusterRetry) : Eligible(),
        maximum: 10,
        scanThreshold: 500,
        minQuestLevelOffset: 7);

    Assert(result.Selected.Select(candidate => candidate.QuestId)
            .SequenceEqual(new uint[] { 867, 876 }),
        "accepted objective work must precede a new pickup");
    Assert(result.Plan.Any(entry => entry.Quest.Id == 876 && entry.Giver?.GiverId == 1002)
           && result.Plan.All(entry => entry.Quest.Id != 876 || entry.Giver?.GiverId != 1001),
        "cooling one giver relation must retain the eligible alternate giver");

    var objective = result.Plan.Single(entry =>
        entry.Quest.Id == 867 && entry.Stage == QuestWorkStage.Objective);
    Assert(objective.Hotspots.Any(point => point.X == 161)
           && objective.Hotspots.All(point => point.X >= 80),
        "cooling one objective cluster must retain the eligible alternate cluster only");
    Assert(result.EarliestRetryUtc == clusterRetry,
        "the schedule must expose the earliest retry across exact excluded scopes");
    Assert(result.Status.Contains("scope=NpcRelation;npc=1001;retry=2026-09-03T00:15:00.0000000Z", StringComparison.Ordinal)
           && result.Status.Contains($"scope=Endpoint;endpoint={cooledCluster.Endpoint};retry=2026-09-03T00:10:00.0000000Z", StringComparison.Ordinal),
        "the schedule status must identify each exact excluded scope and retry time");
}

void TestSchedulerFallbackUsesOnlyCallerVettedPath()
{
    var db = SchedulerDatabase();
    var snapshot = Snapshot(accepted: Array.Empty<QuestSchedulerAcceptedQuest>(), completed: Array.Empty<uint>());
    var cooling = new Func<QuestRecoveryKey, QuestRecoveryDecision>(_ => Cooling(utcNow.AddMinutes(12)));

    var vetted = QuestScheduler.MaterializeSchedule(
        db, snapshot, cooling, 10, 500, 7, "Profiles/vetted-map-level-safe.xml");
    var none = QuestScheduler.MaterializeSchedule(db, snapshot, cooling, 10, 500, 7);

    Assert(vetted.FallbackMode == QuestFallbackMode.ValidatedGrind
           && vetted.ValidatedGrindProfilePath == "Profiles/vetted-map-level-safe.xml",
        "a caller-vetted same-map/level/safety profile must be preserved exactly");
    Assert(none.FallbackMode == QuestFallbackMode.TimedIdle
           && string.IsNullOrEmpty(none.ValidatedGrindProfilePath)
           && none.EarliestRetryUtc == utcNow.AddMinutes(12),
        "without a caller-vetted profile the scheduler must timed-idle at the earliest retry, never discover by filename");
}

void TestUnknownCompletionAuthorityDefersNegativePickupPaths()
{
    var result = QuestScheduler.MaterializeSchedule(
        SchedulerDatabase(),
        Snapshot(
            accepted: new[] { Accepted(867, completed: false) },
            completed: Array.Empty<uint>(),
            authoritative: false),
        _ => Eligible(),
        10,
        500,
        7);

    Assert(result.Selected.Select(candidate => candidate.QuestId).SequenceEqual(new uint[] { 867 })
           && result.Status.Contains("completion-authority=unknown", StringComparison.Ordinal),
        "unknown completion authority must allow accepted work but defer new pickup and prerequisite-negative paths");
}

void TestAuthoritativeCompletionsAreMarkedBeforeEvaluation()
{
    var marked = new HashSet<uint>();
    QuestScheduler.MaterializeSchedule(
        SchedulerDatabase(),
        Snapshot(Array.Empty<QuestSchedulerAcceptedQuest>(), new uint[] { 42, 43 }),
        key =>
        {
            Assert(marked.SetEquals(new uint[] { 42, 43 }),
                "authoritative completions must be marked before any candidate recovery evaluation");
            return Eligible();
        },
        10,
        500,
        7,
        markCompleted: questId => marked.Add(questId));

    Assert(marked.SetEquals(new uint[] { 42, 43 }),
        "every authoritative completed quest must be passed to MarkCompleted");
}

void TestAncestorCorrectionUsesQuestData()
{
    var db = SchedulerDatabase();
    db.Quests.Add(new QuestEntry
    {
        Id = 990,
        Name = "Descendant",
        MinLevel = 1,
        QuestLevel = 20,
        PrevQuestID = 867,
        Objectives = { new QuestObjective { Index = 0, Type = ObjectiveType.KillMob, MobId = 2990, KillCount = 1 } }
    });
    db.QuestGivers.Add(new QuestGiverEntry { QuestId = 990, GiverId = 1990, GiverName = "Descendant Giver" });
    db.CreatureSpawns["1990"] = new List<SpawnPoint> { new() { Map = 1, X = 20, Y = 0 } };

    var messages = new List<string>();
    var result = QuestScheduler.MaterializeSchedule(
        db,
        Snapshot(new[] { Accepted(867, completed: false) }, Array.Empty<uint>()),
        _ => Eligible(),
        10,
        500,
        7,
        log: messages.Add);

    Assert(result.Selected.Any(candidate =>
               candidate.QuestId == 867 && candidate.Stage == QuestWorkStage.AncestorCorrection)
           && result.Selected.All(candidate => candidate.QuestId != 990),
        "an incomplete accepted ancestor must replace its requested descendant pickup with an ancestor correction");
    Assert(messages.Any(message => message.Contains("ancestor=867", StringComparison.Ordinal)
                                   && message.Contains("descendant=990", StringComparison.Ordinal)),
        "ancestor correction diagnostics must identify both data-derived quest IDs");
}

void TestIneligibleDescendantDoesNotTriggerAncestorCorrection()
{
    var db = SchedulerDatabase();
    db.Quests.Add(new QuestEntry
    {
        Id = 991,
        Name = "Too-high descendant",
        MinLevel = 80,
        QuestLevel = 80,
        PrevQuestID = 867,
        Objectives = { new QuestObjective { Index = 0, Type = ObjectiveType.KillMob, MobId = 2991, KillCount = 1 } }
    });
    db.QuestGivers.Add(new QuestGiverEntry { QuestId = 991, GiverId = 1991, GiverName = "High-level giver" });
    db.CreatureSpawns["1991"] = new List<SpawnPoint> { new() { Map = 1, X = 20, Y = 0 } };

    var result = QuestScheduler.MaterializeSchedule(
        db,
        Snapshot(new[] { Accepted(867, completed: false) }, Array.Empty<uint>()),
        _ => Eligible(),
        10,
        500,
        7);

    Assert(result.Selected.Any(candidate =>
               candidate.QuestId == 867 && candidate.Stage == QuestWorkStage.Objective)
           && result.Selected.All(candidate => candidate.Stage != QuestWorkStage.AncestorCorrection),
        "a descendant that cannot be requested at the current level must not redirect accepted work");
}

void TestSchedulerEvaluatesEveryNarrowScope()
{
    var db = SchedulerDatabase();
    db.Quests.Add(new QuestEntry
    {
        Id = 868,
        Name = "Complete accepted",
        MinLevel = 1,
        QuestLevel = 20,
        Objectives = { new QuestObjective { Index = 0, Type = ObjectiveType.TurnInOnly } }
    });
    db.QuestEnders.Add(new QuestEnderEntry { QuestId = 868, EnderId = 3001, EnderName = "Ender" });
    db.CreatureSpawns["3001"] = new List<SpawnPoint> { new() { Map = 1, X = 30, Y = 0 } };
    var evaluated = new HashSet<QuestRecoveryKey>();

    QuestScheduler.MaterializeSchedule(
        db,
        Snapshot(new[] { Accepted(867, false), Accepted(868, true) }, Array.Empty<uint>()),
        key => { evaluated.Add(key); return Eligible(); },
        10,
        500,
        7);

    Assert(evaluated.Any(key => key.QuestId == 876 && key.Scope == QuestRecoveryScope.QuestStage && key.Stage == QuestRecoveryStage.Pickup)
           && evaluated.Any(key => key.QuestId == 867 && key.Scope == QuestRecoveryScope.Objective)
           && evaluated.Any(key => key.QuestId == 868 && key.Scope == QuestRecoveryScope.QuestStage && key.Stage == QuestRecoveryStage.TurnIn)
           && evaluated.Any(key => key.Scope == QuestRecoveryScope.NpcRelation)
           && evaluated.Any(key => key.Scope == QuestRecoveryScope.Endpoint),
        "pickup, objective, turn-in, NPC relation, and endpoint candidates must each use their narrow recovery keys");
}

void TestEndpointClusteringKeepsFiveDistinctEightyYardCells()
{
    var db = SchedulerDatabase();
    db.CreatureSpawns["2000"] = new List<SpawnPoint>
    {
        new() { Map = 1, X = 1, Y = 1 },
        new() { Map = 1, X = 79, Y = 79 },
        new() { Map = 1, X = 81, Y = 1 },
        new() { Map = 1, X = 161, Y = 1 },
        new() { Map = 1, X = 241, Y = 1 },
        new() { Map = 1, X = 321, Y = 1 },
        new() { Map = 1, X = 401, Y = 1 }
    };
    var endpoints = new HashSet<QuestRecoveryKey>();
    var result = QuestScheduler.MaterializeSchedule(
        db,
        Snapshot(new[] { Accepted(867, false) }, Array.Empty<uint>()),
        key => { if (key.Scope == QuestRecoveryScope.Endpoint && key.QuestId == 867) endpoints.Add(key); return Eligible(); },
        10,
        1000,
        7);

    var objective = result.Plan.Single(entry => entry.Quest.Id == 867 && entry.Stage == QuestWorkStage.Objective);
    Assert(endpoints.Count == 6,
        "points in one 80-yard map cell must share one endpoint recovery key");
    Assert(objective.Hotspots.Count == 6
           && objective.Hotspots.Count(point => point.X < 80) == 2
           && objective.Hotspots.All(point => point.X < 401),
        "the scheduler must retain every point in the five nearest distinct eligible endpoint clusters");
}

void TestEndpointCapAppliesAcrossObjectiveStage()
{
    var db = SchedulerDatabase();
    db.Quests.Single(quest => quest.Id == 867).Objectives.Add(
        new QuestObjective { Index = 1, Type = ObjectiveType.KillMob, MobId = 2002, KillCount = 1 });
    db.CreatureSpawns["2000"] = Enumerable.Range(0, 4)
        .Select(index => new SpawnPoint { Map = 1, X = index * 80 + 1, Y = 1 })
        .ToList();
    db.CreatureSpawns["2002"] = Enumerable.Range(4, 4)
        .Select(index => new SpawnPoint { Map = 1, X = index * 80 + 1, Y = 1 })
        .ToList();

    var result = QuestScheduler.MaterializeSchedule(
        db,
        Snapshot(new[] { Accepted(867, false) }, Array.Empty<uint>()),
        _ => Eligible(),
        10,
        1000,
        7);
    var endpointKeys = result.Plan
        .Where(entry => entry.Quest.Id == 867 && entry.Stage == QuestWorkStage.Objective)
        .SelectMany(entry => entry.Hotspots)
        .Select(point => QuestScheduler.EndpointKey(867, QuestRecoveryStage.Navigation, point))
        .Distinct()
        .ToArray();

    Assert(endpointKeys.Length == 5,
        "one objective-stage episode must retain no more than five distinct endpoint keys across all objectives");
}

void TestEvaluationDoesNotClaimAndActivationUsesExactKey()
{
    var beginCalls = 0;
    var schedule = QuestScheduler.MaterializeSchedule(
        SchedulerDatabase(),
        Snapshot(new[] { Accepted(867, false) }, Array.Empty<uint>()),
        _ => Eligible(),
        10,
        500,
        7);
    Assert(schedule.Selected.All(candidate =>
               candidate.Recovery.State == QuestRecoveryState.Eligible
               && candidate.Recovery.AttemptGeneration == 0),
        "candidate evaluation must preserve non-owning recovery decisions and never claim attempt execution");

    var exact = QuestRecoveryKey.ForObjective(867, 0);
    QuestRecoveryKey? cleared = null;
    var rebuilds = 0;
    var decision = QuestScheduler.BeginActivation(
        exact,
        key => { beginCalls++; Assert(key.Equals(exact), "activation must claim the exact selected key"); return Cooling(utcNow.AddMinutes(3)); },
        key => cleared = key,
        () => rebuilds++);

    Assert(!decision.MayAttempt && beginCalls == 1 && cleared?.Equals(exact) == true && rebuilds == 1,
        "a lost first-activation claim must clear only that stage key and request one rebuild");
}

void TestEndpointFailureEscalatesOnlyAfterEveryKnownCluster()
{
    var a = QuestRecoveryKey.ForEndpoint(867, QuestRecoveryStage.Navigation, 1, "cell:0:0");
    var b = QuestRecoveryKey.ForEndpoint(867, QuestRecoveryStage.Navigation, 1, "cell:1:0");
    var outcomes = new List<QuestAttemptOutcome>();

    QuestScheduler.ReportEndpointUnreachable(
        a, QuestRecoveryStage.Objective, new[] { a, b }, Array.Empty<QuestRecoveryKey>(), outcomes.Add);
    Assert(outcomes.Count == 1
           && outcomes[0].Key.Equals(a)
           && outcomes[0].Reason == QuestFailureReason.EndpointUnreachable,
        "one endpoint failure must remain scoped to that exact endpoint while another cluster remains");

    outcomes.Clear();
    QuestScheduler.ReportEndpointUnreachable(
        b, QuestRecoveryStage.Objective, new[] { a, b }, new[] { a }, outcomes.Add);
    Assert(outcomes.Count == 2
           && outcomes[0].Key.Equals(b)
           && outcomes[1].Key.Equals(QuestRecoveryKey.ForQuestStage(867, QuestRecoveryStage.Objective)),
        "quest-stage failure may be reported only after every known cluster is excluded or tried");
}

void TestNavigationFingerprintIncludesProviderAndMeshStamp()
{
    var directory = Path.Combine(Path.GetTempPath(), $"wholesome-mesh-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var stamp = new DateTime(2026, 9, 3, 4, 5, 6, DateTimeKind.Utc);
        Directory.SetLastWriteTimeUtc(directory, stamp);
        var first = QuestScheduler.CreateNavigationProviderFingerprint(typeof(QuestScheduler), directory);
        var second = QuestScheduler.CreateNavigationProviderFingerprint(typeof(QuestScheduler), directory);
        Directory.SetLastWriteTimeUtc(directory, stamp.AddSeconds(1));
        var changed = QuestScheduler.CreateNavigationProviderFingerprint(typeof(QuestScheduler), directory);

        Assert(first == second && first != "unknown",
            "provider type/assembly and mesh-root stamp must produce a deterministic navigation fingerprint");
        Assert(changed != first, "changing the mesh-root stamp must change the navigation fingerprint");
    }
    finally
    {
        Directory.Delete(directory);
    }
}

void TestGuardedProfilePreservesScheduleOrderAndLiveState()
{
    var db = ProfileDatabase();
    var objective = db.Quests.Single(quest => quest.Id == 867);
    var pickup = db.Quests.Single(quest => quest.Id == 876);
    var turnIn = db.Quests.Single(quest => quest.Id == 868);
    var plan = new QuestPlanEntry[]
    {
        new()
        {
            Quest = objective,
            Stage = QuestWorkStage.Objective,
            ObjectiveIndex = 0,
            Hotspots = new[] { new SpawnPoint { Map = 1, X = 161, Y = 2, Z = 3 } }
        },
        new()
        {
            Quest = pickup,
            Stage = QuestWorkStage.Pickup,
            Giver = db.QuestGivers.Single(giver => giver.GiverId == 1002),
            Hotspots = new[] { new SpawnPoint { Map = 1, X = 20, Y = 0, Z = 0 } }
        },
        new()
        {
            Quest = turnIn,
            Stage = QuestWorkStage.TurnIn,
            Ender = db.QuestEnders.Single(ender => ender.EnderId == 3002),
            Hotspots = new[] { new SpawnPoint { Map = 1, X = 30, Y = 0, Z = 0 } }
        }
    };

    string xml = BuildProfileFromPlan(new ProfileBuilder(), plan, db);
    var document = XDocument.Parse(xml);
    var groups = document.Root!.Element("QuestOrder")!.Elements("If").ToArray();

    Assert(groups.Select(group => (string?)group.Elements().Single().Attribute("QuestId"))
            .SequenceEqual(new[] { "867", "876", "868" }),
        "accepted objective and turn-in work must retain reviewed schedule order instead of waiting behind pickups");
    Assert((string?)groups[0].Attribute("Condition") == "HasQuest(867) && !IsQuestCompleted(867)"
           && groups[0].Elements().Single().Name.LocalName == "Objective"
           && (string?)groups[0].Elements().Single().Attribute("Index") == "0",
        "objective work must use the exact accepted-incomplete live-state guard");
    Assert((string?)groups[1].Attribute("Condition") == "!HasQuest(876) && !IsQuestCompleted(876)"
           && groups[1].Elements().Single().Name.LocalName == "PickUp"
           && (string?)groups[1].Elements().Single().Attribute("X") == "20",
        "pickup work must use the exact not-accepted and not-completed live-state guard");
    Assert((string?)groups[2].Attribute("Condition") == "HasQuest(868) && IsQuestCompleted(868)"
           && groups[2].Elements().Single().Name.LocalName == "TurnIn"
           && (string?)groups[2].Elements().Single().Attribute("TurnInId") == "3002"
           && (string?)groups[2].Elements().Single().Attribute("X") == "30",
        "turn-in work must use the exact accepted-complete live-state guard");
    Assert(xml.Contains("HasQuest(867) &amp;&amp; !IsQuestCompleted(867)", StringComparison.Ordinal),
        "guard conditions must be escaped by XML serialization");
}

void TestGuardedProfileUsesOnlyApprovedAlternativesAndHotspots()
{
    var db = ProfileDatabase();
    var pickup = db.Quests.Single(quest => quest.Id == 876);
    var objective = db.Quests.Single(quest => quest.Id == 867);
    var approvedB = db.QuestGivers.Single(giver => giver.GiverId == 1002);
    var approvedC = db.QuestGivers.Single(giver => giver.GiverId == 1003);
    var plan = new QuestPlanEntry[]
    {
        new()
        {
            Quest = pickup,
            Stage = QuestWorkStage.Pickup,
            Giver = approvedB,
            Hotspots = new[]
            {
                new SpawnPoint { Map = 1, X = 20, Y = 1, Z = 2 },
                new SpawnPoint { Map = 1, X = 21, Y = 3, Z = 4 }
            }
        },
        new()
        {
            Quest = pickup,
            Stage = QuestWorkStage.Pickup,
            Giver = approvedC,
            Hotspots = new[] { new SpawnPoint { Map = 1, X = 30 } }
        },
        new()
        {
            Quest = objective,
            Stage = QuestWorkStage.Objective,
            ObjectiveIndex = 0,
            Hotspots = new[]
            {
                new SpawnPoint { Map = 1, X = 161, Y = 2, Z = 3 },
                new SpawnPoint { Map = 1, X = 241, Y = 4, Z = 5 }
            }
        }
    };

    var document = XDocument.Parse(BuildProfileFromPlan(new ProfileBuilder(), plan, db));
    var pickups = document.Root!.Element("QuestOrder")!.Elements("If")
        .SelectMany(group => group.Elements("PickUp"))
        .ToArray();
    var hotspots = document.Root!.Elements("Quest")
        .Where(quest => (string?)quest.Attribute("Id") == "867")
        .SelectMany(quest => quest.Elements("Objective"))
        .SelectMany(node => node.Element("Hotspots")!.Elements("Hotspot"))
        .Select(node => (string?)node.Attribute("X"))
        .ToArray();

    Assert(pickups.Select(node => (string?)node.Attribute("GiverId"))
            .SequenceEqual(new[] { "1002", "1002", "1003" })
           && pickups.Select(node => (string?)node.Attribute("X"))
               .SequenceEqual(new[] { "20", "21", "30" }),
        "the builder must preserve every distinct approved giver endpoint in deterministic plan order");
    Assert(pickups.All(node => (string?)node.Attribute("GiverId") != "1001"),
        "the builder must never fall back to a database-global cooled giver");
    Assert(hotspots.SequenceEqual(new[] { "161", "241" }),
        "objective definitions must contain only eligible plan hotspots in deterministic order");
}

void TestGuardedProfileOmitsEndpointlessObjectives()
{
    var db = ProfileDatabase();
    var plan = new QuestPlanEntry[]
    {
        new()
        {
            Quest = db.Quests.Single(quest => quest.Id == 867),
            Stage = QuestWorkStage.Objective,
            ObjectiveIndex = 0,
            Hotspots = Array.Empty<SpawnPoint>()
        }
    };
    var builder = new ProfileBuilder();
    var document = XDocument.Parse(BuildProfileFromPlan(builder, plan, db));

    Assert(!document.Descendants("Objective").Any(),
        "an objective with no approved endpoint must be omitted instead of emitting empty hotspots");
}

void TestGameObjectOnlyObjectiveResolvesUseObjectOverride()
{
    var quest = new QuestEntry
    {
        Id = 498,
        Name = "The Rescue",
        Objectives =
        {
            new QuestObjective
            {
                Index = 0,
                Type = ObjectiveType.CollectFromGameObject,
                ItemId = 0,
                GameObjectId = 1721,
                GameObjectName = "Locked ball and chain",
                CollectCount = 1
            }
        }
    };
    var plan = new[]
    {
        new QuestPlanEntry
        {
            Quest = quest,
            Stage = QuestWorkStage.Objective,
            ObjectiveIndex = 0,
            Hotspots = new[] { new SpawnPoint { Map = 0, X = -1262, Y = -1211, Z = 38 } }
        }
    };

    var document = XDocument.Parse(new ProfileBuilder().BuildProfileXml(
        plan, new QuestDatabase(), "Hillsbrad", "Tester", 30));
    XElement definition = document.Root!.Elements("Quest").Single();
    XElement order = document.Root.Element("QuestOrder")!.Element("If")!.Element("Objective")!;
    var resolved = Styx.Logic.Profiles.Quest.QuestInfo.FromXML(definition).FindUseGameObject(1721);

    Assert((string?)definition.Element("Objective")!.Attribute("Type") == "UseObject"
           && (string?)definition.Element("Objective")!.Attribute("ObjectId") == "1721"
           && (string?)definition.Element("Objective")!.Attribute("UseCount") == "1",
        "an ItemId-zero game-object objective must use the live UseObject override schema");
    Assert((string?)order.Attribute("Type") == "UseObject"
           && (string?)order.Attribute("ObjectId") == "1721"
           && (string?)order.Attribute("UseCount") == "1",
        "the guarded order node must resolve the live game-object objective by GameObjectId");
    Assert(resolved?.OverridedHotspots?.Count == 1
           && resolved.OverridedHotspots[0].X == -1262,
        "UseGameObjectObjective resolution must receive only the scheduler-approved override hotspot");
}

void TestGameObjectItemCollectionRemainsCollectItemOverride()
{
    var quest = new QuestEntry
    {
        Id = 2950,
        Name = "Nogg's Ring Redo",
        Objectives =
        {
            new QuestObjective
            {
                Index = 1,
                Type = ObjectiveType.CollectFromGameObject,
                ItemId = 1206,
                GameObjectId = 1736,
                GameObjectName = "Shipment of Iron",
                CollectCount = 1
            }
        }
    };
    var plan = new[]
    {
        new QuestPlanEntry
        {
            Quest = quest,
            Stage = QuestWorkStage.Objective,
            ObjectiveIndex = 1,
            Hotspots = new[] { new SpawnPoint { Map = 1, X = 101, Y = 102, Z = 103 } }
        }
    };

    var document = XDocument.Parse(new ProfileBuilder().BuildProfileXml(
        plan, new QuestDatabase(), "Orgrimmar", "Tester", 35));
    XElement definition = document.Root!.Elements("Quest").Single();
    XElement order = document.Root.Element("QuestOrder")!.Element("If")!.Element("Objective")!;
    var resolved = Styx.Logic.Profiles.Quest.QuestInfo.FromXML(definition).FindCollectItem(1206);

    Assert((string?)definition.Element("Objective")!.Attribute("Type") == "CollectItem"
           && (string?)definition.Element("Objective")!.Attribute("ItemId") == "1206"
           && resolved?.OverridedCollectFrom?.ContainsGameObject(1736) == true,
        "a game object that supplies an item must retain CollectItem override behavior");
    Assert((string?)order.Attribute("Type") == "CollectItem"
           && (string?)order.Attribute("ItemId") == "1206",
        "the guarded order node must keep actual item collection keyed by ItemId");
    Assert(resolved?.OverridedHotspots?.Count == 1
           && resolved.OverridedHotspots[0].X == 101,
        "item collection must still use only the scheduler-approved hotspot");
}

void TestSchedulerReportsEveryEndpointlessObjectiveOmission()
{
    var noKnownDb = SchedulerDatabase();
    noKnownDb.CreatureSpawns.Remove("2000");
    var noKnown = QuestScheduler.MaterializeSchedule(
        noKnownDb,
        Snapshot(new[] { Accepted(867, false) }, Array.Empty<uint>(), authoritative: false),
        _ => Eligible(),
        10,
        500,
        7);
    Assert(noKnown.Status.Contains(
            "excluded quest=867;stage=Objective;objective=0;reason=no-known-hotspots;retry=context-change",
            StringComparison.Ordinal),
        "an objective with no known in-range endpoint must report its exact scheduler omission");

    var noAssessed = QuestScheduler.MaterializeSchedule(
        SchedulerDatabase(),
        Snapshot(new[] { Accepted(867, false) }, Array.Empty<uint>(), authoritative: false),
        _ => Eligible(),
        10,
        500,
        7,
        navigationAssessment: _ => new SpawnNavigationAssessment { IsKnownReachable = false });
    Assert(noAssessed.Status.Contains(
            "excluded quest=867;stage=Objective;objective=0;reason=no-assessed-hotspots;retry=context-change",
            StringComparison.Ordinal),
        "an objective whose known endpoints all fail assessment must report that exact scheduler omission");

    DateTime retry = utcNow.AddMinutes(10);
    var noSelected = QuestScheduler.MaterializeSchedule(
        SchedulerDatabase(),
        Snapshot(new[] { Accepted(867, false) }, Array.Empty<uint>(), authoritative: false),
        key => key.Scope == QuestRecoveryScope.Endpoint ? Cooling(retry) : Eligible(),
        10,
        500,
        7);
    Assert(noSelected.Status.Contains(
            "excluded quest=867;stage=Objective;objective=0;reason=no-selected-hotspots;retry=2026-09-03T00:10:00.0000000Z",
            StringComparison.Ordinal),
        "an objective whose assessed endpoints are all recovery-excluded must report its exact retry");
}

void TestSchedulerDeduplicatesRelationRowsAndExactEndpoints()
{
    var db = SchedulerDatabase();
    db.QuestGivers.Add(new QuestGiverEntry
    {
        QuestId = 876,
        GiverId = 1002,
        GiverName = "Duplicate giver row"
    });
    db.CreatureSpawns["1002"] = new List<SpawnPoint>
    {
        new() { Map = 1, X = 20, Y = 0, Z = 0 },
        new() { Map = 1, X = 20, Y = 0, Z = 0 },
        new() { Map = 1, X = 101, Y = 0, Z = 0 }
    };
    db.Quests.Add(new QuestEntry
    {
        Id = 868,
        Name = "Completed work",
        Objectives = { new QuestObjective { Index = 0, Type = ObjectiveType.TurnInOnly } }
    });
    db.QuestEnders.Add(new QuestEnderEntry
    {
        QuestId = 868,
        EnderId = 3001,
        EnderName = "Approved ender"
    });
    db.QuestEnders.Add(new QuestEnderEntry
    {
        QuestId = 868,
        EnderId = 3001,
        EnderName = "Duplicate ender row"
    });
    db.CreatureSpawns["3001"] = new List<SpawnPoint>
    {
        new() { Map = 1, X = 30, Y = 0, Z = 0 },
        new() { Map = 1, X = 30, Y = 0, Z = 0 },
        new() { Map = 1, X = 111, Y = 0, Z = 0 }
    };

    var result = QuestScheduler.MaterializeSchedule(
        db,
        Snapshot(new[] { Accepted(868, true) }, Array.Empty<uint>()),
        _ => Eligible(),
        10,
        500,
        7);
    var giver = result.Plan.Where(entry => entry.Giver?.GiverId == 1002).ToArray();
    var ender = result.Plan.Where(entry => entry.Ender?.EnderId == 3001).ToArray();

    Assert(giver.Length == 1
           && giver[0].Hotspots.Select(point => point.X).SequenceEqual(new[] { 20d, 101d }),
        "duplicate giver rows and exact endpoints must collapse while genuine alternate spawns remain");
    Assert(ender.Length == 1
           && ender[0].Hotspots.Select(point => point.X).SequenceEqual(new[] { 30d, 111d }),
        "duplicate ender rows and exact endpoints must collapse while genuine alternate spawns remain");

    var profile = XDocument.Parse(new ProfileBuilder().BuildProfileXml(
        result.Plan, db, "Test", "Tester", 20));
    Assert(profile.Descendants("If").Count(group => group.Elements("PickUp")
               .Any(node => (string?)node.Attribute("GiverId") == "1002")) == 1
           && profile.Descendants("If").Count(group => group.Elements("TurnIn")
               .Any(node => (string?)node.Attribute("TurnInId") == "3001")) == 1,
        "deduplicated relations must emit one guarded group per genuine scheduler plan entry");
}

string BuildProfileFromPlan(
    ProfileBuilder builder,
    IReadOnlyList<QuestPlanEntry> plan,
    QuestDatabase db)
{
    return builder.BuildProfileXml(plan, db, "Test & Zone", "Tester", 20);
}

QuestDatabase ProfileDatabase() => new()
{
    Quests = new List<QuestEntry>
    {
        new()
        {
            Id = 867,
            Name = "Accepted & ready",
            Objectives =
            {
                new QuestObjective
                {
                    Index = 0,
                    Type = ObjectiveType.KillMob,
                    MobId = 2000,
                    KillCount = 2
                }
            }
        },
        new() { Id = 876, Name = "New work" },
        new() { Id = 868, Name = "Completed work" }
    },
    QuestGivers = new List<QuestGiverEntry>
    {
        new() { QuestId = 876, GiverId = 1001, GiverName = "Cooled giver" },
        new() { QuestId = 876, GiverId = 1002, GiverName = "Approved giver B" },
        new() { QuestId = 876, GiverId = 1003, GiverName = "Approved giver C" }
    },
    QuestEnders = new List<QuestEnderEntry>
    {
        new() { QuestId = 868, EnderId = 3001, EnderName = "Cooled ender" },
        new() { QuestId = 868, EnderId = 3002, EnderName = "Approved ender" }
    },
    CreatureSpawns = new Dictionary<string, List<SpawnPoint>>
    {
        ["2000"] = new()
        {
            new() { Map = 1, X = 1, Y = 1, Z = 1 },
            new() { Map = 1, X = 81, Y = 1, Z = 1 }
        }
    }
};

QuestDatabase SchedulerDatabase() => new()
{
    Quests = new List<QuestEntry>
    {
        new()
        {
            Id = 867,
            Name = "Accepted work",
            MinLevel = 1,
            QuestLevel = 20,
            Objectives = { new QuestObjective { Index = 0, Type = ObjectiveType.KillMob, MobId = 2000, KillCount = 2 } }
        },
        new()
        {
            Id = 876,
            Name = "New work",
            MinLevel = 1,
            QuestLevel = 20,
            Objectives = { new QuestObjective { Index = 0, Type = ObjectiveType.KillMob, MobId = 2001, KillCount = 2 } }
        }
    },
    QuestGivers = new List<QuestGiverEntry>
    {
        new() { QuestId = 876, GiverId = 1001, GiverName = "Giver A" },
        new() { QuestId = 876, GiverId = 1002, GiverName = "Giver B" }
    },
    CreatureSpawns = new Dictionary<string, List<SpawnPoint>>
    {
        ["1001"] = new() { new() { Map = 1, X = 10, Y = 0 } },
        ["1002"] = new() { new() { Map = 1, X = 20, Y = 0 } },
        ["2000"] = new()
        {
            new() { Map = 1, X = 1, Y = 1 },
            new() { Map = 1, X = 10, Y = 10 },
            new() { Map = 1, X = 161, Y = 1 }
        }
    }
};

QuestSchedulerSnapshot Snapshot(
    IReadOnlyList<QuestSchedulerAcceptedQuest> accepted,
    IReadOnlyCollection<uint> completed,
    bool authoritative = true) => new()
{
    UtcNow = utcNow,
    PlayerLevel = 20,
    PlayerRaceId = 1,
    MapId = 1,
    X = 0,
    Y = 0,
    HasAuthoritativeCompletions = authoritative,
    CompletedQuestIds = completed,
    AcceptedQuests = accepted
};

QuestSchedulerAcceptedQuest Accepted(uint id, bool completed) => new()
{
    QuestId = id,
    IsCompleted = completed,
    ObjectiveCounts = new[] { completed ? 1 : 0 }
};

QuestSchedulerAcceptedQuest AcceptedWithCounts(uint id, params int[] counts) => new()
{
    QuestId = id,
    IsCompleted = false,
    ObjectiveCounts = counts
};

QuestRecoveryDecision Eligible() => new()
{
    State = QuestRecoveryState.Eligible,
    MayAttempt = true,
    Status = "eligible"
};

QuestRecoveryDecision Cooling(DateTime retry) => new()
{
    State = QuestRecoveryState.CoolingDown,
    MayAttempt = false,
    RetryUtc = retry,
    Status = "cooling down"
};

QuestRecoveryDecision HalfOpen() => new()
{
    State = QuestRecoveryState.HalfOpen,
    MayAttempt = true,
    Status = "half-open probe"
};

QuestWorkCandidate Candidate(
    uint questId,
    QuestWorkStage stage,
    double distance,
    bool eligible,
    DateTime? retryUtc = null,
    int safety = 0,
    int chain = 0) =>
    new()
    {
        QuestId = questId,
        Stage = stage,
        Distance = distance,
        ChainValue = chain,
        SafetyScore = safety,
        Recovery = Decision(stage, eligible, retryUtc)
    };

QuestEndpointCandidate Endpoint(uint questId, string endpoint, double distance, bool eligible) =>
    new()
    {
        Key = QuestRecoveryKey.ForEndpoint(questId, QuestRecoveryStage.Navigation, 1, endpoint),
        Point = new SpawnPoint { X = distance, Map = 1 },
        Distance = distance,
        Recovery = Decision(QuestWorkStage.Objective, eligible)
    };

QuestEndpointCandidate RankedEndpoint(
    string endpoint,
    double distance,
    int safety,
    bool knownReachable,
    bool knownSafe) => new()
{
    Key = QuestRecoveryKey.ForEndpoint(867, QuestRecoveryStage.Navigation, 1, endpoint),
    Point = new SpawnPoint { X = distance, Map = 1 },
    Distance = distance,
    SafetyScore = safety,
    IsKnownReachable = knownReachable,
    IsKnownSafe = knownSafe,
    Recovery = Eligible()
};

QuestRecoveryDecision Decision(QuestWorkStage stage, bool eligible, DateTime? retryUtc = null) =>
    new()
    {
        State = stage == QuestWorkStage.HalfOpen
            ? QuestRecoveryState.HalfOpen
            : eligible ? QuestRecoveryState.Eligible : QuestRecoveryState.CoolingDown,
        MayAttempt = eligible,
        RetryUtc = retryUtc,
        Status = eligible ? "eligible" : "cooling down"
    };

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

sealed class TestNavigationProvider : NavigationProvider
{
    private readonly Func<WoWPoint, float?> _pathDistance;

    public TestNavigationProvider(Func<WoWPoint, float?> pathDistance)
    {
        _pathDistance = pathDistance;
    }

    public override float PathPrecision { get; set; } = 2;

    public override MoveResult MoveTo(WoWPoint location) => MoveResult.Moved;

    public override WoWPoint[] GeneratePath(WoWPoint from, WoWPoint to) =>
        _pathDistance(to).HasValue ? new[] { to } : Array.Empty<WoWPoint>();

    public override bool AtLocation(WoWPoint point1, WoWPoint point2) => point1.Distance(point2) <= PathPrecision;

    public override float? PathDistance(WoWPoint from, WoWPoint to, float maxDistance = float.MaxValue) =>
        _pathDistance(to);
}

sealed class TestRecoveryClock : IQuestRecoveryClock
{
    public TestRecoveryClock(DateTime utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTime UtcNow { get; private set; }

    public void Advance(TimeSpan amount) => UtcNow = UtcNow.Add(amount);
}

sealed class TestQuestObjective : Bots.Quest.Objectives.QuestObjective
{
    private TestQuestObjective() : base(null!, null!, null!)
    {
    }

    public WoWPoint Location { get; private set; }

    public static TestQuestObjective Create(uint questId, WoWPoint location)
    {
        var objective = (TestQuestObjective)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(TestQuestObjective));
        var questField = typeof(Bots.Quest.Objectives.QuestObjective).GetField(
            "<Quest>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        questField!.SetValue(objective, new TestPlayerQuest(questId));
        objective.Location = location;
        return objective;
    }

    public override bool IsCompleted => false;

    public override bool CanComplete => true;

    public override TreeSharp.Composite CreateBranch() => null!;

    public override WoWPoint GetObjectiveLocation() => Location;
}

sealed class TestPlayerQuest : Styx.Logic.Questing.PlayerQuest
{
    public TestPlayerQuest(uint questId)
        : base(new Styx.WoWInternals.WoWCache.WoWCache.QuestCacheEntry { Id = questId })
    {
    }
}
