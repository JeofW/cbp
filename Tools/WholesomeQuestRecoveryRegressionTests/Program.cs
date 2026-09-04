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
    TestDataLoaderPublishesAndInvalidatesDependencyAuthority();
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
    TestSchedulerOmissionsPersistImmediateRecoveryQuarantine();
    TestSchedulerZeroRowsPersistWithoutRebuildChurn();
    TestSchedulerDeduplicatesRelationRowsAndExactEndpoints();
    TestRefreshGateCoalescesConcurrentRequestsAndStopsCallbacks();
    TestRefreshLeaseFencesTheEntireSchedulerRun();
    TestRefreshApplyIsAtomicWithLifecycleTransitions();
    TestLifecycleGateDoesNotGrowSubscriptionsAcrossRestarts();
    TestLifecycleGateCannotSubscribeAfterConcurrentStop();
    TestStopAbandonsOwnedAttemptBeforeClearingLifecycleState();
    TestManualExclusionClearsMatchingLocalOwnership();
    TestLifecycleResetDoesNotCarryPickupCyclesAcrossRestart();
    TestProgressMonitorCountsOnlyActiveWorkAndCoalescesOneStall();
    TestProgressMonitorRequestsAlternateAndFailsBoundedlyWithOneCluster();
    TestProductionUnavailableAlternatePreservesAttemptUntilBoundedFailure();
    TestProgressMonitorScopesEndpointAndDeathFailures();
    TestProgressMonitorResetsForEachSameKeyOwnershipGeneration();
    TestProductionWorkSnapshotExcludesNonWorkAndRequiresExactOwner();
    TestDeathEventConsumesOnlyPreDeathOwnedQuestCombatSnapshot();
    TestAttemptOwnershipUsesTheExactGenerationToken();
    TestBehaviorTransitionFinalizesExactPriorGeneration();
    TestOwnedFailureBindsGenerationWithoutSyntheticSuccess();
    TestFinalEndpointAndStageFailuresReportInOneOwnedSequence();
    TestRejectedGeneratedBatchRecoversWithoutThrowingOrApplyingEpisode();
    TestRejectedGeneratedBatchDoesNotFallThroughToObservations();
    TestCompletedOwnedStageRequestsRefreshBeforeQuestOrderRunsOut();
    TestTimedIdleSuppressesOldQuestOrderUntilARebuildSelectsWork();
    TestPickupOutcomeCoalescingStillReportsTheFailureEpisode();
    TestPickupRecoveryRequiresThreeDistinctActiveOwnedCycles();
    TestRecoveryStatusFormattingCoversStatesReasonsAndRetryConditions();
    TestRecoveryActionsUseManagerSemanticsAndPreserveCompleted();
    TestManualQuestIdEditorDiffsOnlyManualRecords();
    TestRecoveryActionsAreDisabledWithoutSelection();
    TestRecoveryGridRefreshesFromManagerSnapshotsOnItsUiThread();
    TestLegacyMigrationIsVisibleOnceAndNeverMutatesLegacyFiles();
    TestCombinedAutomaticManualUiActionsRemainQuestWideAndScoped();
    TestConfigurationBeforeStartLoadsPersistedRecoveryWithoutStartingLifecycle();
    TestPreStartConfigurationMutationsPersistAcrossFreshManagers();
    TestRecoveryFlushFailureIsVisibleAndRetryable();
    TestStaleRecoveryUiActionsRefreshWithoutPersistingDirtyState();
    TestRecoveryActionFlushCountTracksActualMutations();
    TestMarkPermanentRequiresTheSelectedAutomaticState();
    TestMarkPermanentUiRacesDoNotMutateOrFlush();
    TestConfigurationWithoutCharacterIsReadOnlyAndContainsErrors();
    Console.WriteLine("Wholesome scheduler recovery regression tests passed.");
}

catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    global::System.Environment.ExitCode = 1;
}

void TestRecoveryStatusFormattingCoversStatesReasonsAndRetryConditions()
{
    var cooldown = utcNow.AddMinutes(15);
    var halfOpen = utcNow.AddHours(6);
    var records = new[]
    {
        RecoveryRecord(3001, QuestRecoveryState.Eligible, QuestFailureReason.None),
        RecoveryRecord(3002, QuestRecoveryState.Attempting, QuestFailureReason.InteractionTimedOut),
        RecoveryRecord(3003, QuestRecoveryState.CoolingDown, QuestFailureReason.NoObjectiveProgress, cooldownUntilUtc: cooldown),
        RecoveryRecord(3004, QuestRecoveryState.HalfOpen, QuestFailureReason.EndpointUnreachable),
        RecoveryRecord(3005, QuestRecoveryState.Quarantined, QuestFailureReason.LegacyUnknown, nextHalfOpenUtc: halfOpen),
        RecoveryRecord(3006, QuestRecoveryState.ManualBlacklist, QuestFailureReason.UserExcluded),
        RecoveryRecord(3007, QuestRecoveryState.Completed, QuestFailureReason.None)
    };

    var rows = RecoveryStatusFormatter.CreateRows(records);
    Assert(rows.Count == records.Length,
        "every recovery state must remain visible in the diagnostic snapshot");
    foreach (var row in rows)
    {
        Assert(row.DisplayText.Contains($"quest={row.QuestId}", StringComparison.Ordinal)
               && row.DisplayText.Contains($"stage={row.Stage}", StringComparison.Ordinal)
               && row.DisplayText.Contains($"state={row.State}", StringComparison.Ordinal)
               && row.DisplayText.Contains($"reason={row.Reason}", StringComparison.Ordinal)
               && row.DisplayText.Contains($"episode={row.Episode}", StringComparison.Ordinal)
               && row.DisplayText.Contains(row.RetryOrReset, StringComparison.Ordinal),
            "each status row must include quest, stage, state, reason, episode, and its retry/reset condition");
    }

    Assert(rows.Single(row => row.QuestId == 3003).RetryOrReset == "cooldown until 2026-09-03T00:15:00.0000000Z",
        "cooling rows must display the exact UTC cooldown expiry");
    Assert(rows.Single(row => row.QuestId == 3005).RetryOrReset ==
           "half-open after 2026-09-03T06:00:00.0000000Z; reset on live chain, level, dataset, or NPC evidence, or Retry now",
        "legacy quarantine must name its time-based probe and explicit non-authorship reset trigger");
    Assert(rows.Single(row => row.QuestId == 3007).RetryOrReset == "no reset (completed terminal)",
        "completed rows must explain their terminal state");

    var everyReason = Enum.GetValues<QuestFailureReason>()
        .Select((reason, index) => RecoveryRecord((uint)(3100 + index), QuestRecoveryState.Quarantined, reason))
        .ToArray();
    var reasonRows = RecoveryStatusFormatter.CreateRows(everyReason);
    Assert(reasonRows.Select(row => row.Reason).SequenceEqual(Enum.GetValues<QuestFailureReason>())
           && reasonRows.All(row => row.DisplayText.Contains($"reason={row.Reason}", StringComparison.Ordinal))
           && reasonRows.All(row => row.RetryOrReset.Contains("reset", StringComparison.OrdinalIgnoreCase)),
        "all finite recovery reasons must be visible with an explicit reset condition");
}

void TestRecoveryActionsUseManagerSemanticsAndPreserveCompleted()
{
    string root = Path.Combine(Path.GetTempPath(), $"wholesome-actions-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        var clock = new TestRecoveryClock(utcNow);
        var manager = new QuestRecoveryManager(clock);
        manager.Configure(new QuestRecoveryEnvironment(root, "Jeof", "Lordaeron", "dataset", "core", "nav"));
        var automaticKey = QuestRecoveryKey.ForQuestStage(3201, QuestRecoveryStage.Objective);
        var owner = manager.TryBeginAttempt(automaticKey, new QuestRecoveryContext());
        manager.Report(
            QuestAttemptOutcome.Failure(
                automaticKey,
                automaticKey,
                owner.AttemptGeneration,
                QuestFailureReason.NoObjectiveProgress,
                "stalled"),
            new QuestRecoveryContext());
        var logs = new List<string>();
        var controller = new RecoverySettingsController(manager, logs.Add);

        var automatic = controller.Refresh().Single(row => row.Key.Equals(automaticKey));
        controller.RetryNow(automatic);
        Assert(manager.GetEntries().Single(record => record.Key.Equals(automaticKey)).State == QuestRecoveryState.HalfOpen,
            "Retry now must create half-open eligibility for the selected automatic exclusion");
        var probe = manager.TryBeginAttempt(automaticKey, new QuestRecoveryContext());
        Assert(probe.MayAttempt && probe.State == QuestRecoveryState.Attempting
               && !manager.TryBeginAttempt(automaticKey, new QuestRecoveryContext()).MayAttempt,
            "Retry now must permit exactly one owned half-open probe");
        manager.AbandonAttempt(automaticKey, probe.AttemptGeneration);
        CreateCoolingRecord(manager, automaticKey, QuestFailureReason.NoObjectiveProgress);

        automatic = controller.Refresh().Single(row => row.Key.Equals(automaticKey));
        controller.MarkPermanent(automatic);
        Assert(manager.GetEntries().Any(record => record.Key.QuestId == 3201
               && record.State == QuestRecoveryState.ManualBlacklist
               && record.Reason == QuestFailureReason.UserExcluded),
            "Mark permanent must use the manager's ManualBlacklist state");
        var manual = controller.Refresh().Single(row => row.QuestId == 3201
            && row.State == QuestRecoveryState.ManualBlacklist);
        controller.ClearExclusion(manual);
        Assert(manager.GetEntries().All(record => record.Key.QuestId != 3201
               || record.State != QuestRecoveryState.ManualBlacklist),
            "Clear exclusion must remove a manual exclusion");

        var secondKey = QuestRecoveryKey.ForQuestStage(3202, QuestRecoveryStage.Pickup);
        var secondOwner = manager.TryBeginAttempt(secondKey, new QuestRecoveryContext());
        manager.Report(
            QuestAttemptOutcome.Failure(
                secondKey,
                secondKey,
                secondOwner.AttemptGeneration,
                QuestFailureReason.PickupTargetNotOffered,
                "not offered"),
            new QuestRecoveryContext());
        controller.ClearExclusion(controller.Refresh().Single(row => row.Key.Equals(secondKey)));
        Assert(manager.GetEntries().All(record => !record.Key.Equals(secondKey)),
            "Clear exclusion must remove the selected automatic exclusion rather than creating a probe");

        manager.MarkCompleted(3203);
        var completed = controller.Refresh().Single(row => row.QuestId == 3203);
        controller.RetryNow(completed);
        controller.MarkPermanent(completed);
        controller.ClearExclusion(completed);
        Assert(manager.GetEntries().Single(record => record.Key.QuestId == 3203).State == QuestRecoveryState.Completed,
            "retry, permanent, and clear UI actions must preserve Completed as terminal");
        Assert(logs.Count == 7 && logs.All(line =>
                   line.Contains("quest=", StringComparison.Ordinal)
                   && line.Contains("stage=", StringComparison.Ordinal)
                   && line.Contains("reason=", StringComparison.Ordinal)),
            "every explicit recovery action, including a completed no-op, must be logged with quest, stage, and reason");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

void TestManualQuestIdEditorDiffsOnlyManualRecords()
{
    string root = Path.Combine(Path.GetTempPath(), $"wholesome-manual-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        var manager = new QuestRecoveryManager(new TestRecoveryClock(utcNow));
        manager.Configure(new QuestRecoveryEnvironment(root, "Jeof", "Lordaeron", "dataset", "core", "nav"));
        manager.SetManualBlacklist(3301, true);
        manager.SetManualBlacklist(3302, true);
        var automaticKey = QuestRecoveryKey.ForQuestStage(3399, QuestRecoveryStage.Pickup);
        var owner = manager.TryBeginAttempt(automaticKey, new QuestRecoveryContext());
        manager.Report(
            QuestAttemptOutcome.Failure(
                automaticKey,
                automaticKey,
                owner.AttemptGeneration,
                QuestFailureReason.PickupTargetNotOffered,
                "automatic"),
            new QuestRecoveryContext());

        var controller = new RecoverySettingsController(manager, _ => { });
        Assert(controller.ManualQuestIdsText() == "3301,3302",
            "the manual textbox must be populated only from ManualBlacklist manager records");
        controller.ApplyManualQuestIds("3302, 3303, invalid, 0, 3303");
        var manualIds = manager.GetEntries()
            .Where(record => record.State == QuestRecoveryState.ManualBlacklist)
            .Select(record => record.Key.QuestId)
            .OrderBy(id => id)
            .ToArray();
        Assert(manualIds.SequenceEqual(new uint[] { 3302, 3303 })
               && manager.GetEntries().Any(record => record.Key.Equals(automaticKey)
                   && record.State == QuestRecoveryState.CoolingDown),
            "applying the manual textbox must diff added/removed manual records without changing automatic records");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

void TestRecoveryActionsAreDisabledWithoutSelection()
{
    var none = RecoveryActionAvailability.For(null);
    Assert(!none.CanRetryNow && !none.CanMarkPermanent && !none.CanClearExclusion,
        "all recovery buttons must be disabled when no row is selected");
    var completed = RecoveryActionAvailability.For(
        RecoveryStatusFormatter.CreateRows(new[]
        {
            RecoveryRecord(3401, QuestRecoveryState.Completed, QuestFailureReason.None)
        }).Single());
    Assert(!completed.CanRetryNow && !completed.CanMarkPermanent && !completed.CanClearExclusion,
        "completed terminal rows must not expose mutating recovery buttons");
}

void TestRecoveryGridRefreshesFromManagerSnapshotsOnItsUiThread()
{
    string root = Path.Combine(Path.GetTempPath(), $"wholesome-ui-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        var manager = new QuestRecoveryManager(new TestRecoveryClock(utcNow));
        manager.Configure(new QuestRecoveryEnvironment(root, "Jeof", "Lordaeron", "dataset", "core", "nav"));
        manager.SetManualBlacklist(3501, true);
        Exception? failure = null;
        var finished = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new SettingsForm(new WholesomeAQSettings(), _ => { }, recoveryManager: manager);
                form.CreateControl();
                var grid = (System.Windows.Forms.DataGridView)form.Controls.Find("recoveryGrid", true).Single();
                var retry = (System.Windows.Forms.Button)form.Controls.Find("retryRecoveryButton", true).Single();
                var permanent = (System.Windows.Forms.Button)form.Controls.Find("markPermanentButton", true).Single();
                var clear = (System.Windows.Forms.Button)form.Controls.Find("clearExclusionButton", true).Single();
                Assert(grid.ReadOnly && grid.Rows.Count == 1,
                    "the recovery grid must be read-only and initialized from a manager snapshot");
                Assert(!retry.Enabled && !permanent.Enabled && !clear.Enabled,
                    "the actual recovery buttons must start disabled with no selected row");

                manager.SetManualBlacklist(3502, true);
                var worker = new Thread(form.RefreshRecoveryEntries);
                worker.Start();
                while (worker.IsAlive)
                {
                    System.Windows.Forms.Application.DoEvents();
                    Thread.Yield();
                }
                worker.Join();
                System.Windows.Forms.Application.DoEvents();
                Assert(grid.Rows.Count == 2,
                    "a background refresh request must marshal the manager snapshot onto the form's UI thread");
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                finished.Set();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert(finished.Wait(TimeSpan.FromSeconds(10)), "the recovery UI thread must finish without deadlock");
        thread.Join();
        if (failure != null)
            throw failure;
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

QuestRecoveryRecord RecoveryRecord(
    uint questId,
    QuestRecoveryState state,
    QuestFailureReason reason,
    DateTime? cooldownUntilUtc = null,
    DateTime? nextHalfOpenUtc = null) => new()
{
    Key = QuestRecoveryKey.ForQuestStage(questId, QuestRecoveryStage.Objective),
    State = state,
    Reason = reason,
    EpisodeCount = 2,
    CooldownUntilUtc = cooldownUntilUtc,
    NextHalfOpenUtc = nextHalfOpenUtc
};

void TestLegacyMigrationIsVisibleOnceAndNeverMutatesLegacyFiles()
{
    string root = Path.Combine(Path.GetTempPath(), $"wholesome-legacy-{Guid.NewGuid():N}");
    string legacyDirectory = Path.Combine(root, "WholesomeAutoQuest", "Jeof-Lordaeron");
    string legacyPath = Path.Combine(legacyDirectory, "quest_blacklist.txt");
    string backupPath = Path.Combine(legacyDirectory, "quest_blacklist.legacy.bak");
    Directory.CreateDirectory(legacyDirectory);
    File.WriteAllText(legacyPath, "867,875");
    File.WriteAllText(backupPath, "historical backup must survive");
    byte[] legacyBefore = File.ReadAllBytes(legacyPath);
    byte[] backupBefore = File.ReadAllBytes(backupPath);
    try
    {
        var firstLogs = new List<string>();
        var manager = new QuestRecoveryManager(new TestRecoveryClock(utcNow), firstLogs.Add);
        var environment = new QuestRecoveryEnvironment(root, "Jeof", "Lordaeron", "dataset", "core", "nav");
        manager.Configure(environment);
        var rows = RecoveryStatusFormatter.CreateRows(manager.GetEntries());
        Assert(rows.Select(row => row.QuestId).SequenceEqual(new uint[] { 867, 875 })
               && rows.All(row => row.State == QuestRecoveryState.Quarantined
                   && row.Reason == QuestFailureReason.LegacyUnknown)
               && rows.All(row => row.DisplayText.Contains("LegacyUnknown", StringComparison.Ordinal)
                   && !row.DisplayText.Contains("user-authored", StringComparison.OrdinalIgnoreCase)),
            "migrated IDs must be visibly labelled LegacyUnknown without claiming user authorship");

        string expectedMigration = $"Legacy migration: backup='{backupPath}', imported=2.";
        Assert(firstLogs.Count(line => line == expectedMigration) == 1,
            "the first successful migration must log the exact backup path and imported count once");

        var controller = new RecoverySettingsController(manager, _ => { });
        controller.ClearExclusion(rows[0]);
        controller.MarkPermanent(rows[1]);
        manager.Flush();
        Assert(File.Exists(legacyPath)
               && File.ReadAllBytes(legacyPath).SequenceEqual(legacyBefore)
               && File.Exists(backupPath)
               && File.ReadAllBytes(backupPath).SequenceEqual(backupBefore),
            "manual and automatic recovery actions must never write, delete, or overwrite legacy evidence files");

        var secondLogs = new List<string>();
        var reloaded = new QuestRecoveryManager(new TestRecoveryClock(utcNow), secondLogs.Add);
        reloaded.Configure(environment);
        Assert(secondLogs.All(line => !line.StartsWith("Legacy migration:", StringComparison.Ordinal)),
            "the completed migration must never be logged again on later manager configuration");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

void TestCombinedAutomaticManualUiActionsRemainQuestWideAndScoped()
{
    string root = Path.Combine(Path.GetTempPath(), $"wholesome-combined-actions-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        var manager = new QuestRecoveryManager(new TestRecoveryClock(utcNow));
        manager.Configure(new QuestRecoveryEnvironment(root, "Jeof", "Lordaeron", "dataset", "core", "nav"));
        var selectedKey = QuestRecoveryKey.ForQuestStage(3601, QuestRecoveryStage.Objective);
        var sameQuestOtherStage = QuestRecoveryKey.ForQuestStage(3601, QuestRecoveryStage.TurnIn);
        var unrelatedKey = QuestRecoveryKey.ForQuestStage(3602, QuestRecoveryStage.Pickup);
        CreateCoolingRecord(manager, selectedKey, QuestFailureReason.NoObjectiveProgress);
        CreateCoolingRecord(manager, sameQuestOtherStage, QuestFailureReason.TurnInTargetNotOffered);
        CreateCoolingRecord(manager, unrelatedKey, QuestFailureReason.PickupTargetNotOffered);
        manager.MarkCompleted(3603);

        Exception? failure = null;
        var finished = new ManualResetEventSlim();
        var logs = new List<string>();
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new SettingsForm(new WholesomeAQSettings(), logs.Add, recoveryManager: manager);
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                var grid = (System.Windows.Forms.DataGridView)form.Controls.Find("recoveryGrid", true).Single();
                var retry = (System.Windows.Forms.Button)form.Controls.Find("retryRecoveryButton", true).Single();
                var permanent = (System.Windows.Forms.Button)form.Controls.Find("markPermanentButton", true).Single();
                var clear = (System.Windows.Forms.Button)form.Controls.Find("clearExclusionButton", true).Single();
                var selected = grid.Rows.Cast<System.Windows.Forms.DataGridViewRow>()
                    .Single(row => row.Tag is RecoveryStatusRow status && status.Key.Equals(selectedKey));
                selected.Selected = true;
                System.Windows.Forms.Application.DoEvents();
                Assert(retry.Enabled && permanent.Enabled && clear.Enabled,
                    "an automatic exclusion row must initially expose retry, permanent, and clear actions");

                permanent.PerformClick();
                System.Windows.Forms.Application.DoEvents();
                var afterMark = grid.SelectedRows.Cast<System.Windows.Forms.DataGridViewRow>()
                    .Select(row => row.Tag as RecoveryStatusRow)
                    .Single();
                Assert(afterMark != null && afterMark.Key.Equals(selectedKey)
                       && manager.GetEntries().Any(record => record.Key.QuestId == 3601
                           && record.State == QuestRecoveryState.ManualBlacklist),
                    "marking an automatic row permanent must retain a useful selection and install the canonical quest-wide manual terminal");
                Assert(!retry.Enabled && !permanent.Enabled && clear.Enabled,
                    "a same-quest manual terminal must disable retry/duplicate permanent actions even while the automatic row remains selected");
                retry.PerformClick();
                Assert(manager.GetEntries().Single(record => record.Key.Equals(selectedKey)).State == QuestRecoveryState.CoolingDown,
                    "disabled Retry now must not silently turn the selected automatic exclusion into a half-open probe");

                clear.PerformClick();
                System.Windows.Forms.Application.DoEvents();
                var remaining = manager.GetEntries();
                Assert(remaining.All(record => record.Key.QuestId != 3601
                           || record.State != QuestRecoveryState.ManualBlacklist)
                       && remaining.All(record => !record.Key.Equals(selectedKey))
                       && remaining.Any(record => record.Key.Equals(sameQuestOtherStage)
                           && record.State == QuestRecoveryState.CoolingDown)
                       && remaining.Any(record => record.Key.Equals(unrelatedKey)
                           && record.State == QuestRecoveryState.CoolingDown)
                       && remaining.Single(record => record.Key.QuestId == 3603).State == QuestRecoveryState.Completed,
                    "Clear exclusion must atomically remove the quest-wide manual terminal and selected automatic row while preserving other stage, quest, and Completed records");
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                finished.Set();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert(finished.Wait(TimeSpan.FromSeconds(10)), "the combined action UI regression must finish without deadlock");
        thread.Join();
        if (failure != null)
            throw failure;
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

void CreateCoolingRecord(
    QuestRecoveryManager manager,
    QuestRecoveryKey key,
    QuestFailureReason reason)
{
    var owner = manager.TryBeginAttempt(key, new QuestRecoveryContext());
    manager.Report(
        QuestAttemptOutcome.Failure(key, key, owner.AttemptGeneration, reason, "test failure"),
        new QuestRecoveryContext());
}

void CreateQuarantinedRecord(
    QuestRecoveryManager manager,
    TestRecoveryClock clock,
    QuestRecoveryKey key,
    QuestFailureReason reason)
{
    CreateCoolingRecord(manager, key, reason);
    clock.Advance(TimeSpan.FromMinutes(31));
    CreateCoolingRecord(manager, key, reason);
    clock.Advance(TimeSpan.FromMinutes(61));
    CreateCoolingRecord(manager, key, reason);
}

void TestConfigurationBeforeStartLoadsPersistedRecoveryWithoutStartingLifecycle()
{
    string root = Path.Combine(Path.GetTempPath(), $"wholesome-prestart-{Guid.NewGuid():N}");
    string dataPath = Path.Combine(root, "quest_data.json");
    Directory.CreateDirectory(root);
    File.WriteAllText(dataPath, "{\"Quests\":[]}");
    var environment = new QuestRecoveryEnvironment(root, "Jeof", "Lordaeron", "old-dataset", "core", "old-nav");
    try
    {
        var writer = new QuestRecoveryManager(new TestRecoveryClock(utcNow));
        writer.Configure(environment);
        writer.SetManualBlacklist(3701, true);
        writer.Flush();

        var manager = new QuestRecoveryManager(new TestRecoveryClock(utcNow));
        var bot = new WholesomeAutoQuest();
        var logs = new List<string>();
        var result = bot.EnsureRecoveryConfigured(
            manager,
            new DataLoader(dataPath),
            () => "deterministic-nav",
            (dataset, navigation) => new QuestRecoveryEnvironment(
                root, "Jeof", "Lordaeron", dataset, "core", navigation),
            logs.Add);

        Assert(result.IsAvailable && result.DataReady
               && result.DatasetFingerprint != "unknown"
               && manager.GetEntries().Single().State == QuestRecoveryState.ManualBlacklist,
            "configuration before Start must load the deterministic data fingerprint and persisted recovery store");
        var lifecycle = (WholesomeLifecycleGate)typeof(WholesomeAutoQuest)
            .GetField("_lifecycle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(bot)!;
        var refresh = (RefreshGate)typeof(WholesomeAutoQuest)
            .GetField("_refreshGate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(bot)!;
        Assert(lifecycle.IsStopped && !refresh.Begin().HasValue,
            "opening configuration before Start must not start bot lifecycle handlers or queue a recovery refresh");

        Exception? failure = null;
        var finished = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new SettingsForm(
                    new WholesomeAQSettings(),
                    logs.Add,
                    recoveryManager: manager,
                    recoveryAvailable: result.IsAvailable,
                    recoveryUnavailableReason: result.Status);
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                var grid = (System.Windows.Forms.DataGridView)form.Controls.Find("recoveryGrid", true).Single();
                var manual = (System.Windows.Forms.TextBox)form.Controls.Find("manualQuestIdsTextBox", true).Single();
                Assert(grid.Rows.Count == 1 && manual.Text == "3701" && !manual.ReadOnly,
                    "the pre-Start configuration form must display persisted rows and permit manual editing once configured");
                manual.Text = "3701,3702";
                var save = form.Controls.Cast<System.Windows.Forms.Control>()
                    .OfType<System.Windows.Forms.Button>()
                    .Single(button => button.Text == "Save");
                save.PerformClick();
                System.Windows.Forms.Application.DoEvents();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                finished.Set();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert(finished.Wait(TimeSpan.FromSeconds(10)), "the pre-Start configuration UI must finish without deadlock");
        thread.Join();
        if (failure != null)
            throw failure;
        Assert(manager.GetEntries()
                .Where(record => record.State == QuestRecoveryState.ManualBlacklist)
                .Select(record => record.Key.QuestId)
                .OrderBy(id => id)
                .SequenceEqual(new uint[] { 3701, 3702 })
               && lifecycle.IsStopped && !refresh.Begin().HasValue,
            "manual save must work before Start without changing bot lifecycle or timer state");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

void TestPreStartConfigurationMutationsPersistAcrossFreshManagers()
{
    string root = Path.Combine(Path.GetTempPath(), $"wholesome-prestart-persistence-{Guid.NewGuid():N}");
    string dataPath = Path.Combine(root, "quest_data.json");
    Directory.CreateDirectory(root);
    File.WriteAllText(dataPath, "{\"Quests\":[]}");
    var environment = new QuestRecoveryEnvironment(root, "Jeof", "Lordaeron", "dataset", "core", "nav");
    var retryKey = QuestRecoveryKey.ForQuestStage(3721, QuestRecoveryStage.Objective);
    var permanentKey = QuestRecoveryKey.ForQuestStage(3722, QuestRecoveryStage.TurnIn);
    var clearKey = QuestRecoveryKey.ForQuestStage(3723, QuestRecoveryStage.Pickup);
    try
    {
        var seed = new QuestRecoveryManager(new TestRecoveryClock(utcNow));
        seed.Configure(environment);
        seed.SetManualBlacklist(3711, true);
        seed.SetManualBlacklist(3712, true);
        CreateCoolingRecord(seed, retryKey, QuestFailureReason.NoObjectiveProgress);
        CreateCoolingRecord(seed, permanentKey, QuestFailureReason.TurnInTargetNotOffered);
        CreateCoolingRecord(seed, clearKey, QuestFailureReason.PickupTargetNotOffered);
        seed.MarkCompleted(3799);
        seed.Flush();

        (QuestRecoveryManager manager, WholesomeRecoveryConfigurationResult result) LoadFresh()
        {
            var manager = new QuestRecoveryManager(new TestRecoveryClock(utcNow));
            var bot = new WholesomeAutoQuest();
            var result = bot.EnsureRecoveryConfigured(
                manager,
                new DataLoader(dataPath),
                () => "deterministic-nav",
                (dataset, navigation) => new QuestRecoveryEnvironment(
                    root, "Jeof", "Lordaeron", dataset, "core", navigation),
                _ => { });
            Assert(result.IsAvailable, "the production pre-Start configuration seam must configure the persisted store");
            return (manager, result);
        }

        void UseForm(
            QuestRecoveryManager manager,
            WholesomeRecoveryConfigurationResult result,
            Action<System.Windows.Forms.Form> mutate)
        {
            Exception? failure = null;
            var finished = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                try
                {
                    using var form = new SettingsForm(
                        new WholesomeAQSettings(),
                        _ => { },
                        recoveryManager: manager,
                        recoveryAvailable: result.IsAvailable,
                        recoveryUnavailableReason: result.Status);
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    mutate(form);
                    System.Windows.Forms.Application.DoEvents();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    finished.Set();
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert(finished.Wait(TimeSpan.FromSeconds(10)),
                "the pre-Start persistence UI regression must finish without deadlock");
            thread.Join();
            if (failure != null)
                throw failure;
        }

        var loaded = LoadFresh();
        UseForm(loaded.manager, loaded.result, form =>
        {
            var manual = (System.Windows.Forms.TextBox)form.Controls.Find("manualQuestIdsTextBox", true).Single();
            manual.Text = "3712,3713";
            form.Controls.Cast<System.Windows.Forms.Control>()
                .OfType<System.Windows.Forms.Button>()
                .Single(button => button.Text == "Save")
                .PerformClick();
        });

        loaded = LoadFresh();
        Assert(loaded.manager.GetEntries()
                .Where(record => record.State == QuestRecoveryState.ManualBlacklist)
                .Select(record => record.Key.QuestId)
                .OrderBy(id => id)
                .SequenceEqual(new uint[] { 3712, 3713 }),
            "a pre-Start textbox Save containing both removal and addition must survive a fresh manager reload");
        Assert(loaded.manager.GetEntries().Single(record => record.Key.QuestId == 3799).State == QuestRecoveryState.Completed,
            "manual Save persistence must preserve Completed terminal records");

        UseForm(loaded.manager, loaded.result, form =>
        {
            SelectRecoveryRow(form, retryKey);
            ((System.Windows.Forms.Button)form.Controls.Find("retryRecoveryButton", true).Single()).PerformClick();
        });
        loaded = LoadFresh();
        Assert(loaded.manager.GetEntries().Single(record => record.Key.Equals(retryKey)).State == QuestRecoveryState.HalfOpen,
            "Retry now must persist its single half-open eligibility before Start");

        UseForm(loaded.manager, loaded.result, form =>
        {
            SelectRecoveryRow(form, permanentKey);
            ((System.Windows.Forms.Button)form.Controls.Find("markPermanentButton", true).Single()).PerformClick();
        });
        loaded = LoadFresh();
        Assert(loaded.manager.GetEntries().Any(record => record.Key.QuestId == permanentKey.QuestId
                   && record.State == QuestRecoveryState.ManualBlacklist)
               && loaded.manager.GetEntries().Any(record => record.Key.Equals(permanentKey)
                   && record.State == QuestRecoveryState.CoolingDown),
            "Mark permanent must persist the quest-wide terminal while retaining the selected automatic record");

        UseForm(loaded.manager, loaded.result, form =>
        {
            SelectRecoveryRow(form, permanentKey);
            var retry = (System.Windows.Forms.Button)form.Controls.Find("retryRecoveryButton", true).Single();
            var clear = (System.Windows.Forms.Button)form.Controls.Find("clearExclusionButton", true).Single();
            Assert(!retry.Enabled && clear.Enabled,
                "the reloaded automatic row must still expose quest-wide terminal action semantics");
            clear.PerformClick();
        });
        loaded = LoadFresh();
        Assert(loaded.manager.GetEntries().All(record => record.Key.QuestId != permanentKey.QuestId),
            "Clear exclusion must persist removal of both the same-quest manual terminal and selected automatic record");

        UseForm(loaded.manager, loaded.result, form =>
        {
            SelectRecoveryRow(form, clearKey);
            ((System.Windows.Forms.Button)form.Controls.Find("clearExclusionButton", true).Single()).PerformClick();
        });
        loaded = LoadFresh();
        Assert(loaded.manager.GetEntries().All(record => !record.Key.Equals(clearKey))
               && loaded.manager.GetEntries().Single(record => record.Key.QuestId == 3799).State == QuestRecoveryState.Completed,
            "an explicit automatic Clear must survive reload without changing Completed terminal state");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

void SelectRecoveryRow(System.Windows.Forms.Form form, QuestRecoveryKey key)
{
    var grid = (System.Windows.Forms.DataGridView)form.Controls.Find("recoveryGrid", true).Single();
    var row = grid.Rows.Cast<System.Windows.Forms.DataGridViewRow>()
        .Single(candidate => candidate.Tag is RecoveryStatusRow status && status.Key.Equals(key));
    grid.ClearSelection();
    row.Selected = true;
    System.Windows.Forms.Application.DoEvents();
}

void TestRecoveryFlushFailureIsVisibleAndRetryable()
{
    string root = Path.Combine(Path.GetTempPath(), $"wholesome-flush-failure-{Guid.NewGuid():N}");
    string dataPath = Path.Combine(root, "quest_data.json");
    string storePath = Path.Combine(root, "QuestRecovery", "Jeof-Lordaeron", "quest-recovery.json");
    string temporaryPath = storePath + ".tmp";
    Directory.CreateDirectory(root);
    File.WriteAllText(dataPath, "{\"Quests\":[]}");
    try
    {
        var logs = new List<string>();
        var manager = new QuestRecoveryManager(new TestRecoveryClock(utcNow), logs.Add);
        var bot = new WholesomeAutoQuest();
        var result = bot.EnsureRecoveryConfigured(
            manager,
            new DataLoader(dataPath),
            () => "deterministic-nav",
            (dataset, navigation) => new QuestRecoveryEnvironment(
                root, "Jeof", "Lordaeron", dataset, "core", navigation),
            logs.Add);
        Directory.CreateDirectory(Path.GetDirectoryName(temporaryPath)!);
        Directory.CreateDirectory(temporaryPath);

        Exception? failure = null;
        var finished = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new SettingsForm(
                    new WholesomeAQSettings(),
                    logs.Add,
                    recoveryManager: manager,
                    recoveryAvailable: result.IsAvailable,
                    recoveryUnavailableReason: result.Status);
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                var manual = (System.Windows.Forms.TextBox)form.Controls.Find("manualQuestIdsTextBox", true).Single();
                var status = (System.Windows.Forms.Label)form.Controls.Find("recoveryAvailabilityLabel", true).Single();
                var save = form.Controls.Cast<System.Windows.Forms.Control>()
                    .OfType<System.Windows.Forms.Button>()
                    .Single(button => button.Text == "Save");
                manual.Text = "3731";
                save.PerformClick();
                System.Windows.Forms.Application.DoEvents();
                Assert(form.Visible
                       && status.Text.Contains("not persisted", StringComparison.OrdinalIgnoreCase)
                       && logs.Any(line => line.Contains("persistence failed", StringComparison.OrdinalIgnoreCase)),
                    "a failed recovery Save flush must remain visibly open and must not claim persistence");

                Directory.Delete(temporaryPath);
                save.PerformClick();
                System.Windows.Forms.Application.DoEvents();
                Assert(!form.Visible,
                    "retrying Save after the persistence target recovers must flush the manager's retained dirty state");
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                finished.Set();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert(finished.Wait(TimeSpan.FromSeconds(10)), "the recovery flush failure UI regression must finish without deadlock");
        thread.Join();
        if (failure != null)
            throw failure;

        var reloaded = new QuestRecoveryManager(new TestRecoveryClock(utcNow));
        reloaded.Configure(new QuestRecoveryEnvironment(
            root, "Jeof", "Lordaeron", result.DatasetFingerprint, "core", result.NavigationFingerprint));
        Assert(reloaded.GetEntries().Single(record => record.Key.QuestId == 3731).State == QuestRecoveryState.ManualBlacklist,
            "a failed flush must keep state dirty so a later Save can persist it without another textbox diff");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

void TestStaleRecoveryUiActionsRefreshWithoutPersistingDirtyState()
{
    string root = Path.Combine(Path.GetTempPath(), $"wholesome-stale-actions-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    var environment = new QuestRecoveryEnvironment(root, "Jeof", "Lordaeron", "dataset", "core", "nav");
    var retryKey = QuestRecoveryKey.ForQuestStage(3741, QuestRecoveryStage.Objective);
    var clearKey = QuestRecoveryKey.ForQuestStage(3742, QuestRecoveryStage.TurnIn);
    try
    {
        var seed = new QuestRecoveryManager(new TestRecoveryClock(utcNow));
        seed.Configure(environment);
        CreateCoolingRecord(seed, retryKey, QuestFailureReason.NoObjectiveProgress);
        CreateCoolingRecord(seed, clearKey, QuestFailureReason.TurnInTargetNotOffered);
        seed.MarkCompleted(3799);
        seed.Flush();

        void RunStaleAction(QuestRecoveryKey selectedKey, uint unrelatedQuestId, string buttonName, string actionName)
        {
            var manager = new QuestRecoveryManager(new TestRecoveryClock(utcNow));
            manager.Configure(environment);
            var logs = new List<string>();
            int recoveryChanged = 0;
            Exception? failure = null;
            var finished = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                try
                {
                    using var form = new SettingsForm(
                        new WholesomeAQSettings(),
                        logs.Add,
                        recoveryChanged: () => recoveryChanged++,
                        recoveryManager: manager);
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    SelectRecoveryRow(form, selectedKey);

                    manager.ClearExclusion(selectedKey);
                    manager.SetManualBlacklist(unrelatedQuestId, true);
                    ((System.Windows.Forms.Button)form.Controls.Find(buttonName, true).Single()).PerformClick();
                    System.Windows.Forms.Application.DoEvents();

                    var grid = (System.Windows.Forms.DataGridView)form.Controls.Find("recoveryGrid", true).Single();
                    Assert(recoveryChanged == 0
                           && logs.Any(line => line.Contains(actionName, StringComparison.Ordinal)
                               && (line.Contains("unavailable", StringComparison.Ordinal)
                                   || line.Contains("made no change", StringComparison.Ordinal)))
                           && grid.Rows.Cast<System.Windows.Forms.DataGridViewRow>()
                               .All(row => row.Tag is not RecoveryStatusRow status || !status.Key.Equals(selectedKey)),
                        "a stale action must report no change and refresh current status without signaling a mutation");
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    finished.Set();
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert(finished.Wait(TimeSpan.FromSeconds(10)), "the stale recovery action UI regression must finish without deadlock");
            thread.Join();
            if (failure != null)
                throw failure;

            var reloaded = new QuestRecoveryManager(new TestRecoveryClock(utcNow));
            reloaded.Configure(environment);
            Assert(reloaded.GetEntries().Any(record => record.Key.Equals(selectedKey))
                   && reloaded.GetEntries().All(record => record.Key.QuestId != unrelatedQuestId)
                   && reloaded.GetEntries().Single(record => record.Key.QuestId == 3799).State == QuestRecoveryState.Completed,
                "a stale action must not flush its no-op or unrelated dirty state, and must preserve Completed");
        }

        RunStaleAction(retryKey, 3751, "retryRecoveryButton", "Retry now");
        RunStaleAction(clearKey, 3752, "clearExclusionButton", "Clear exclusion");

        var raced = new QuestRecoveryManager(new TestRecoveryClock(utcNow));
        raced.Configure(environment);
        var raceLogs = new List<string>();
        int raceChanged = 0;
        Exception? raceFailure = null;
        var raceFinished = new ManualResetEventSlim();
        var raceThread = new Thread(() =>
        {
            try
            {
                using var form = new SettingsForm(
                    new WholesomeAQSettings(),
                    raceLogs.Add,
                    recoveryChanged: () => raceChanged++,
                    recoveryManager: raced);
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                SelectRecoveryRow(form, retryKey);
                raced.SetManualBlacklist(retryKey.QuestId, true);
                ((System.Windows.Forms.Button)form.Controls.Find("retryRecoveryButton", true).Single()).PerformClick();
                System.Windows.Forms.Application.DoEvents();
                var retry = (System.Windows.Forms.Button)form.Controls.Find("retryRecoveryButton", true).Single();
                Assert(raceChanged == 0
                       && raceLogs.Any(line => line.Contains("Retry now unavailable", StringComparison.Ordinal))
                       && !retry.Enabled,
                    "an availability race must refresh to the current terminal state without flushing or signaling success");
            }
            catch (Exception ex)
            {
                raceFailure = ex;
            }
            finally
            {
                raceFinished.Set();
            }
        });
        raceThread.SetApartmentState(ApartmentState.STA);
        raceThread.Start();
        Assert(raceFinished.Wait(TimeSpan.FromSeconds(10)), "the recovery availability race UI regression must finish without deadlock");
        raceThread.Join();
        if (raceFailure != null)
            throw raceFailure;

        var raceReloaded = new QuestRecoveryManager(new TestRecoveryClock(utcNow));
        raceReloaded.Configure(environment);
        Assert(raceReloaded.GetEntries().All(record => record.State != QuestRecoveryState.ManualBlacklist),
            "an availability race no-op must not persist its dirty terminal change");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

void TestRecoveryActionFlushCountTracksActualMutations()
{
    string root = Path.Combine(Path.GetTempPath(), $"wholesome-action-flush-count-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    var environment = new QuestRecoveryEnvironment(root, "Jeof", "Lordaeron", "dataset", "core", "nav");
    var retryKey = QuestRecoveryKey.ForQuestStage(3761, QuestRecoveryStage.Objective);
    var clearKey = QuestRecoveryKey.ForQuestStage(3762, QuestRecoveryStage.TurnIn);
    var markKey = QuestRecoveryKey.ForQuestStage(3763, QuestRecoveryStage.Pickup);
    try
    {
        var seed = new QuestRecoveryManager(new TestRecoveryClock(utcNow));
        seed.Configure(environment);
        CreateCoolingRecord(seed, retryKey, QuestFailureReason.NoObjectiveProgress);
        CreateCoolingRecord(seed, clearKey, QuestFailureReason.TurnInTargetNotOffered);
        CreateCoolingRecord(seed, markKey, QuestFailureReason.PickupTargetNotOffered);
        seed.MarkCompleted(3799);
        seed.Flush();

        (QuestRecoveryManager manager, RecoverySettingsController controller, Func<int> flushCount) LoadCounting()
        {
            var manager = new QuestRecoveryManager(new TestRecoveryClock(utcNow));
            manager.Configure(environment);
            int count = 0;
            var controller = new RecoverySettingsController(
                manager,
                _ => { },
                () =>
                {
                    count++;
                    return manager.TryFlush();
                });
            return (manager, controller, () => count);
        }

        var staleRetry = LoadCounting();
        var staleRetryRow = staleRetry.controller.Refresh().Single(row => row.Key.Equals(retryKey));
        staleRetry.manager.ClearExclusion(retryKey);
        staleRetry.manager.SetManualBlacklist(3771, true);
        Assert(!staleRetry.controller.RetryNow(staleRetryRow) && staleRetry.flushCount() == 0,
            "a stale Retry now must perform zero flush attempts");
        var afterStaleRetry = new QuestRecoveryManager(new TestRecoveryClock(utcNow));
        afterStaleRetry.Configure(environment);
        Assert(afterStaleRetry.GetEntries().Any(record => record.Key.Equals(retryKey))
               && afterStaleRetry.GetEntries().All(record => record.Key.QuestId != 3771),
            "a stale Retry now must leave unrelated dirty state unpersisted");

        var successfulRetry = LoadCounting();
        var retryRow = successfulRetry.controller.Refresh().Single(row => row.Key.Equals(retryKey));
        Assert(successfulRetry.controller.RetryNow(retryRow) && successfulRetry.flushCount() == 1,
            "a successful Retry now mutation must perform exactly one flush attempt");

        var staleClear = LoadCounting();
        var staleClearRow = staleClear.controller.Refresh().Single(row => row.Key.Equals(clearKey));
        staleClear.manager.ClearExclusion(clearKey);
        staleClear.manager.SetManualBlacklist(3772, true);
        Assert(!staleClear.controller.ClearExclusion(staleClearRow) && staleClear.flushCount() == 0,
            "a stale Clear exclusion must perform zero flush attempts");

        var successfulClear = LoadCounting();
        var clearRow = successfulClear.controller.Refresh().Single(row => row.Key.Equals(clearKey));
        Assert(successfulClear.controller.ClearExclusion(clearRow) && successfulClear.flushCount() == 1,
            "a successful Clear exclusion mutation must perform exactly one flush attempt");

        var racedMark = LoadCounting();
        var racedMarkRow = racedMark.controller.Refresh().Single(row => row.Key.Equals(markKey));
        racedMark.manager.MarkCompleted(markKey.QuestId);
        racedMark.manager.SetManualBlacklist(3773, true);
        Assert(!racedMark.controller.MarkPermanent(racedMarkRow) && racedMark.flushCount() == 0,
            "a Mark permanent availability race with Completed must perform zero flush attempts");

        var successfulMark = LoadCounting();
        var markRow = successfulMark.controller.Refresh().Single(row => row.Key.Equals(markKey));
        Assert(successfulMark.controller.MarkPermanent(markRow) && successfulMark.flushCount() == 1,
            "a successful Mark permanent mutation must perform exactly one flush attempt");

        var completed = LoadCounting();
        var completedRow = completed.controller.Refresh().Single(row => row.QuestId == 3799);
        Assert(!completed.controller.RetryNow(completedRow)
               && !completed.controller.MarkPermanent(completedRow)
               && !completed.controller.ClearExclusion(completedRow)
               && completed.flushCount() == 0,
            "Completed recovery actions must remain terminal and perform zero flush attempts");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

void TestMarkPermanentRequiresTheSelectedAutomaticState()
{
    string root = Path.Combine(Path.GetTempPath(), $"wholesome-conditional-mark-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        (QuestRecoveryManager manager, RecoverySettingsController controller, QuestRecoveryKey key, Func<int> flushCount)
            CreateAutomatic(uint questId, QuestRecoveryState state = QuestRecoveryState.CoolingDown)
        {
            var clock = new TestRecoveryClock(utcNow);
            var manager = new QuestRecoveryManager(clock);
            manager.Configure(new QuestRecoveryEnvironment(root, $"Jeof-{questId}", "Lordaeron", "dataset", "core", "nav"));
            var key = QuestRecoveryKey.ForQuestStage(questId, QuestRecoveryStage.Objective);
            if (state == QuestRecoveryState.Quarantined)
                CreateQuarantinedRecord(manager, clock, key, QuestFailureReason.NoObjectiveProgress);
            else
                CreateCoolingRecord(manager, key, QuestFailureReason.NoObjectiveProgress);
            int count = 0;
            var controller = new RecoverySettingsController(
                manager,
                _ => { },
                () =>
                {
                    count++;
                    return manager.TryFlush();
                });
            return (manager, controller, key, () => count);
        }

        var removed = CreateAutomatic(3781);
        var removedRow = removed.controller.Refresh().Single(row => row.Key.Equals(removed.key));
        removed.manager.ClearExclusion(removed.key);
        Assert(!removed.controller.ActionsFor(removedRow).CanMarkPermanent
               && !removed.controller.MarkPermanent(removedRow)
               && removed.flushCount() == 0
               && removed.manager.GetEntries().All(record => record.Key.QuestId != removed.key.QuestId),
            "a selected automatic row removed before Mark permanent must be unavailable and perform no mutation or flush");

        var eligible = CreateAutomatic(3782);
        var eligibleRow = eligible.controller.Refresh().Single(row => row.Key.Equals(eligible.key));
        eligible.manager.RetryNow(eligible.key);
        var probe = eligible.manager.TryBeginAttempt(eligible.key, new QuestRecoveryContext());
        eligible.manager.AbandonAttempt(eligible.key, probe.AttemptGeneration);
        Assert(!eligible.controller.ActionsFor(eligibleRow).CanMarkPermanent
               && !eligible.controller.MarkPermanent(eligibleRow)
               && eligible.flushCount() == 0
               && eligible.manager.GetEntries().Single(record => record.Key.Equals(eligible.key)).State == QuestRecoveryState.Eligible,
            "a selected automatic row that becomes Eligible before Mark permanent must perform no mutation or flush");

        var completed = CreateAutomatic(3783, QuestRecoveryState.Quarantined);
        var completedRow = completed.controller.Refresh().Single(row => row.Key.Equals(completed.key));
        completed.manager.MarkCompleted(completed.key.QuestId);
        Assert(!completed.controller.ActionsFor(completedRow).CanMarkPermanent
               && !completed.controller.MarkPermanent(completedRow)
               && completed.flushCount() == 0
               && completed.manager.GetEntries().All(record => record.State == QuestRecoveryState.Completed),
            "a selected automatic row completed before Mark permanent must remain terminal with no flush");

        var changed = CreateAutomatic(3784, QuestRecoveryState.Quarantined);
        var changedRow = changed.controller.Refresh().Single(row => row.Key.Equals(changed.key));
        changed.manager.RetryNow(changed.key);
        Assert(!changed.controller.ActionsFor(changedRow).CanMarkPermanent
               && !changed.controller.MarkPermanent(changedRow)
               && changed.flushCount() == 0
               && changed.manager.GetEntries().Single(record => record.Key.Equals(changed.key)).State == QuestRecoveryState.HalfOpen,
            "a selected automatic row whose state changes before Mark permanent must perform no mutation or flush");

        var unchanged = CreateAutomatic(3785, QuestRecoveryState.Quarantined);
        var unchangedRow = unchanged.controller.Refresh().Single(row => row.Key.Equals(unchanged.key));
        Assert(unchanged.controller.MarkPermanent(unchangedRow)
               && unchanged.flushCount() == 1
               && unchanged.manager.GetEntries().Count(record =>
                   record.Key.QuestId == unchanged.key.QuestId &&
                   record.State == QuestRecoveryState.ManualBlacklist) == 1,
            "an unchanged selected automatic row must install one quest-wide manual terminal and flush exactly once");

        var direct = CreateAutomatic(3786);
        var directRow = direct.controller.Refresh().Single(row => row.Key.Equals(direct.key));
        Assert(!direct.manager.TrySetManualBlacklistIfCurrentAutomatic(
                   direct.key,
                   QuestRecoveryState.Quarantined,
                   directRow.AttemptGeneration)
               && direct.manager.TrySetManualBlacklistIfCurrentAutomatic(
                   direct.key,
                   QuestRecoveryState.CoolingDown,
                   directRow.AttemptGeneration)
               && !direct.manager.TrySetManualBlacklistIfCurrentAutomatic(
                   direct.key,
                   QuestRecoveryState.CoolingDown,
                   directRow.AttemptGeneration),
            "the manager conditional API must atomically require the exact key, expected automatic state, and stable attempt generation");

        var aba = CreateAutomatic(3791);
        var abaRow = aba.controller.Refresh().Single(row => row.Key.Equals(aba.key));
        aba.manager.RetryNow(aba.key);
        var nextAttempt = aba.manager.TryBeginAttempt(aba.key, new QuestRecoveryContext());
        aba.manager.Report(
            QuestAttemptOutcome.Failure(
                aba.key,
                aba.key,
                nextAttempt.AttemptGeneration,
                QuestFailureReason.NoObjectiveProgress,
                "same-state ABA"),
            new QuestRecoveryContext());
        Assert(aba.manager.GetEntries().Single(record => record.Key.Equals(aba.key)).State == abaRow.State
               && !aba.manager.TrySetManualBlacklistIfCurrentAutomatic(
                   aba.key,
                   abaRow.State,
                   abaRow.AttemptGeneration),
            "a same-key automatic record that returns to the selected state in a later attempt generation must reject the stale row");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

void TestMarkPermanentUiRacesDoNotMutateOrFlush()
{
    string root = Path.Combine(Path.GetTempPath(), $"wholesome-mark-ui-races-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        void Run(
            uint questId,
            QuestRecoveryState initialState,
            Action<QuestRecoveryManager, QuestRecoveryKey> race,
            QuestRecoveryState expectedStateAfter,
            bool rowRemains)
        {
            var clock = new TestRecoveryClock(utcNow);
            var manager = new QuestRecoveryManager(clock);
            manager.Configure(new QuestRecoveryEnvironment(root, $"Jeof-{questId}", "Lordaeron", "dataset", "core", "nav"));
            var key = QuestRecoveryKey.ForQuestStage(questId, QuestRecoveryStage.Objective);
            if (initialState == QuestRecoveryState.Quarantined)
                CreateQuarantinedRecord(manager, clock, key, QuestFailureReason.NoObjectiveProgress);
            else
                CreateCoolingRecord(manager, key, QuestFailureReason.NoObjectiveProgress);
            manager.Flush();
            int changed = 0;
            var logs = new List<string>();
            Exception? failure = null;
            var finished = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                try
                {
                    using var form = new SettingsForm(
                        new WholesomeAQSettings(),
                        logs.Add,
                        recoveryChanged: () => changed++,
                        recoveryManager: manager);
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    SelectRecoveryRow(form, key);
                    var mark = (System.Windows.Forms.Button)form.Controls.Find("markPermanentButton", true).Single();
                    Assert(mark.Enabled, "a current automatic exclusion must expose Mark permanent before the race");

                    race(manager, key);
                    mark.PerformClick();
                    System.Windows.Forms.Application.DoEvents();

                    var rows = ((System.Windows.Forms.DataGridView)form.Controls.Find("recoveryGrid", true).Single())
                        .Rows.Cast<System.Windows.Forms.DataGridViewRow>()
                        .Select(row => row.Tag as RecoveryStatusRow)
                        .Where(row => row != null)
                        .ToArray();
                    Assert(changed == 0
                           && logs.Any(line => line.Contains("Mark permanent", StringComparison.Ordinal)
                               && (line.Contains("unavailable", StringComparison.Ordinal)
                                   || line.Contains("made no change", StringComparison.Ordinal)))
                           && rows.Any(row => row!.Key.Equals(key)) == rowRemains
                           && manager.GetEntries().All(record => record.State != QuestRecoveryState.ManualBlacklist)
                           && (!rowRemains || manager.GetEntries().Single(record => record.Key.Equals(key)).State == expectedStateAfter),
                        "a stale Mark permanent click must refresh current rows without callback or manual mutation");
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    finished.Set();
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert(finished.Wait(TimeSpan.FromSeconds(10)), "the Mark permanent UI race must finish without deadlock");
            thread.Join();
            if (failure != null)
                throw failure;
        }

        Run(3787, QuestRecoveryState.CoolingDown, (manager, key) => manager.ClearExclusion(key), QuestRecoveryState.Eligible, rowRemains: false);
        Run(3788, QuestRecoveryState.CoolingDown, (manager, key) =>
        {
            manager.RetryNow(key);
            var probe = manager.TryBeginAttempt(key, new QuestRecoveryContext());
            manager.AbandonAttempt(key, probe.AttemptGeneration);
        }, QuestRecoveryState.Eligible, rowRemains: true);
        Run(3789, QuestRecoveryState.Quarantined, (manager, key) => manager.MarkCompleted(key.QuestId), QuestRecoveryState.Completed, rowRemains: true);
        Run(3790, QuestRecoveryState.Quarantined, (manager, key) => manager.RetryNow(key), QuestRecoveryState.HalfOpen, rowRemains: true);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

void TestConfigurationWithoutCharacterIsReadOnlyAndContainsErrors()
{
    string root = Path.Combine(Path.GetTempPath(), $"wholesome-no-character-{Guid.NewGuid():N}");
    string dataPath = Path.Combine(root, "quest_data.json");
    Directory.CreateDirectory(root);
    File.WriteAllText(dataPath, "{\"Quests\":[]}");
    try
    {
        var manager = new QuestRecoveryManager(new TestRecoveryClock(utcNow));
        var bot = new WholesomeAutoQuest();
        var logs = new List<string>();
        var result = bot.EnsureRecoveryConfigured(
            manager,
            new DataLoader(dataPath),
            () => "deterministic-nav",
            (_, _) => null,
            logs.Add);
        Assert(!result.IsAvailable
               && result.Status == "Quest recovery is unavailable until a character and realm are loaded."
               && logs.Any(line => line.Contains(result.Status, StringComparison.Ordinal)),
            "missing live identity must be contained and reported as an unavailable configuration state");

        Exception? failure = null;
        var finished = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new SettingsForm(
                    new WholesomeAQSettings(),
                    logs.Add,
                    recoveryManager: manager,
                    recoveryAvailable: result.IsAvailable,
                    recoveryUnavailableReason: result.Status);
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                var manual = (System.Windows.Forms.TextBox)form.Controls.Find("manualQuestIdsTextBox", true).Single();
                var retry = (System.Windows.Forms.Button)form.Controls.Find("retryRecoveryButton", true).Single();
                var permanent = (System.Windows.Forms.Button)form.Controls.Find("markPermanentButton", true).Single();
                var clear = (System.Windows.Forms.Button)form.Controls.Find("clearExclusionButton", true).Single();
                var unavailable = (System.Windows.Forms.Label)form.Controls.Find("recoveryAvailabilityLabel", true).Single();
                Assert(manual.ReadOnly && !retry.Enabled && !permanent.Enabled && !clear.Enabled
                       && unavailable.Text == result.Status,
                    "without a live character, recovery controls must be visibly read-only/disabled rather than throwing");
                var save = form.Controls.Cast<System.Windows.Forms.Control>()
                    .OfType<System.Windows.Forms.Button>()
                    .Single(button => button.Text == "Save");
                save.PerformClick();
                System.Windows.Forms.Application.DoEvents();
                Assert(logs.All(line => !line.StartsWith("Quest recovery settings save failed:", StringComparison.Ordinal)),
                    "the missing-character read-only Save path must not attempt a recovery flush");
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                finished.Set();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert(finished.Wait(TimeSpan.FromSeconds(10)), "the unavailable configuration UI must finish without deadlock");
        thread.Join();
        if (failure != null)
            throw failure;
        Assert(manager.GetEntries().Count == 0,
            "saving ordinary bot settings while recovery is unavailable must not access or mutate an unconfigured manager");
        var failingResult = bot.EnsureRecoveryConfigured(
            manager,
            new DataLoader(dataPath),
            () => throw new IOException("navigation unavailable"),
            (_, _) => throw new InvalidOperationException("identity unavailable"),
            logs.Add);
        Assert(!failingResult.IsAvailable
               && logs.Any(line => line.Contains("identity unavailable", StringComparison.Ordinal)),
            "configuration initialization exceptions must be contained and logged meaningfully for the user");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
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

    var firstLease = gate.Begin();
    Assert(accepted == 1 && firstLease.HasValue,
        "100 concurrent refresh requests must produce one pending main-thread refresh");
    accepted = 0;
    Parallel.For(0, 100, _ =>
    {
        if (gate.TryRequest())
            Interlocked.Increment(ref accepted);
    });
    Assert(accepted == 1,
        "requests arriving during a running refresh must coalesce into one follow-up latch");
    gate.Complete(firstLease.GetValueOrDefault());
    var followupLease = gate.Begin();
    Assert(followupLease.HasValue,
        "completion must promote the one running-state latch to a pending refresh");
    gate.TryRequest();
    gate.Stop();
    gate.Complete(followupLease.GetValueOrDefault());
    Assert(!gate.TryRequest() && !gate.Begin().HasValue,
        "Stop must drop pending/rerun bits and win a race with a stale running callback's Complete");
    gate.Start();
    Assert(gate.TryRequest(), "a deliberate later bot start must accept one refresh request");
    var restartedLease = gate.Begin();
    Assert(restartedLease.HasValue,
        "a deliberate later bot start must reset the stopped refresh gate");
    Assert(gate.TryRequest(), "the restarted run must record its own one-bit follow-up latch");
    gate.Complete(followupLease.GetValueOrDefault());
    Assert(!gate.Begin().HasValue,
        "an old-epoch Complete after a restarted Begin must not alter the new running state or latch");
    gate.Complete(restartedLease.GetValueOrDefault());
    Assert(gate.Begin().HasValue,
        "the matching restarted lease must promote exactly its own latched follow-up");
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
        lifecycle.Start(
            () =>
            {
                subscriptions++;
                active++;
                maximumActive = Math.Max(maximumActive, active);
            },
            () => throw new InvalidOperationException("unexpected compensation"));
        lifecycle.Start(
            () => throw new InvalidOperationException("duplicate subscription"),
            () => throw new InvalidOperationException("duplicate compensation"));
        lifecycle.Stop(() => { removals++; active--; });
        lifecycle.Stop(() => throw new InvalidOperationException("duplicate removal"));
    }

    Assert(subscriptions == 10 && removals == 10 && active == 0 && maximumActive == 1,
        "repeated start/stop cycles must own exactly one event subscription set");
    Assert(lifecycle.IsStopped && !gate.TryRequest(),
        "lifecycle Stop must leave an explicit stopped state and cancel refresh work");
}

void TestLifecycleGateCannotSubscribeAfterConcurrentStop()
{
    for (var cycle = 0; cycle < 25; cycle++)
    {
        var lifecycle = new WholesomeLifecycleGate(new RefreshGate());
        var subscriptionSync = new object();
        var subscribeEntered = new ManualResetEventSlim();
        var allowSubscribe = new ManualResetEventSlim();
        var handlerInstalled = false;

        var start = Task.Run(() => lifecycle.Start(
            () =>
            {
                subscribeEntered.Set();
                allowSubscribe.Wait();
                lock (subscriptionSync)
                    handlerInstalled = true;
            },
            () =>
            {
                lock (subscriptionSync)
                    handlerInstalled = false;
            }));
        Assert(subscribeEntered.Wait(TimeSpan.FromSeconds(5)),
            "the lifecycle race fixture must reach the in-flight subscription action");
        var stop = Task.Run(() => lifecycle.Stop(() =>
        {
            lock (subscriptionSync)
                handlerInstalled = false;
        }));
        Assert(stop.Wait(TimeSpan.FromSeconds(5)),
            "Stop must not wait on or invoke callbacks while holding the lifecycle state lock");
        allowSubscribe.Set();
        Assert(start.Wait(TimeSpan.FromSeconds(5)),
            "the in-flight Start must finish after its subscription action is released");
        lock (subscriptionSync)
            Assert(lifecycle.IsStopped && !handlerInstalled,
                "an in-flight Start must compensate its stale subscription after concurrent Stop");
    }

    var startedLifecycle = new WholesomeLifecycleGate(new RefreshGate());
    var subscriptions = 0;
    Parallel.For(0, 100, _ => startedLifecycle.Start(
        () => Interlocked.Increment(ref subscriptions),
        () => Interlocked.Decrement(ref subscriptions)));
    Assert(!startedLifecycle.IsStopped && subscriptions == 1,
        "concurrent Start calls must finish with exactly one owned handler");
    var removals = 0;
    Parallel.For(0, 100, _ => startedLifecycle.Stop(() => Interlocked.Increment(ref removals)));
    Assert(startedLifecycle.IsStopped && removals == 1,
        "concurrent Stop calls must finish with zero handlers and exactly one removal");
}

void TestRefreshLeaseFencesTheEntireSchedulerRun()
{
    var gate = new RefreshGate();
    Assert(gate.TryRequest(), "the fence fixture must queue an old refresh");
    var oldLease = gate.Begin();
    Assert(oldLease.HasValue, "the fence fixture must begin the old refresh");
    gate.Stop();
    gate.Start();
    Assert(gate.TryRequest(), "restart must queue a new-epoch refresh");
    var newLease = gate.Begin();
    Assert(newLease.HasValue, "restart must begin the new-epoch refresh");

    int scans = 0;
    int applies = 0;
    bool oldRunAgain = WholesomeAutoQuest.RunLeaseFencedRefresh(
        gate,
        oldLease.GetValueOrDefault(),
        () => { scans++; return true; },
        () => applies++);
    Assert(!oldRunAgain && scans == 0 && applies == 0
           && !gate.TryRequest(oldLease.GetValueOrDefault()),
        "an old callback must not scan a newly assigned scheduler, apply a profile, or latch a new-epoch refresh");
    Assert(gate.IsCurrent(newLease.GetValueOrDefault()),
        "the rejected old callback must leave the new refresh lease unchanged");

    gate.Complete(newLease.GetValueOrDefault());
    Assert(gate.TryRequest(), "the post-scan fixture must queue a current refresh");
    var scanningLease = gate.Begin();
    RefreshLease restartedLease = default;
    bool postScanRunAgain = WholesomeAutoQuest.RunLeaseFencedRefresh(
        gate,
        scanningLease.GetValueOrDefault(),
        () =>
        {
            scans++;
            gate.Stop();
            gate.Start();
            gate.TryRequest();
            restartedLease = gate.Begin().GetValueOrDefault();
            return true;
        },
        () => applies++);
    Assert(!postScanRunAgain && scans == 1 && applies == 0
           && gate.IsCurrent(restartedLease)
           && !gate.TryRequest(scanningLease.GetValueOrDefault()),
        "Stop/Start during scan must fence profile apply and the old runAgain request");
    Assert(gate.TryRequest(),
        "the old runAgain path must not consume the restarted run's one-bit latch");
}

void TestRefreshApplyIsAtomicWithLifecycleTransitions()
{
    var gate = new RefreshGate();
    Assert(gate.TryRequest(), "the apply-race fixture must queue a refresh");
    var lease = gate.Begin();
    Assert(lease.HasValue, "the apply-race fixture must begin a refresh");

    var postScanReached = new ManualResetEventSlim();
    var allowApplyAttempt = new ManualResetEventSlim();
    var applies = 0;
    var stoppedBeforeApply = Task.Run(() =>
    {
        postScanReached.Set();
        allowApplyAttempt.Wait();
        return gate.TryApply(lease.GetValueOrDefault(), () => Interlocked.Increment(ref applies));
    });
    Assert(postScanReached.Wait(TimeSpan.FromSeconds(5)),
        "the apply-race fixture must reach the boundary after scan and before apply");
    gate.Stop();
    allowApplyAttempt.Set();
    Assert(stoppedBeforeApply.Wait(TimeSpan.FromSeconds(5))
           && !stoppedBeforeApply.Result
           && applies == 0,
        "Stop between scan and lease-aware apply must win without running the stale side effect");

    gate.Start();
    Assert(gate.TryRequest(), "restart must queue a fresh apply-race refresh");
    var restartedLease = gate.Begin();
    Assert(restartedLease.HasValue, "restart must begin a fresh apply-race refresh");
    var applyEntered = new ManualResetEventSlim();
    var releaseApply = new ManualResetEventSlim();
    var stopStarted = new ManualResetEventSlim();
    var startStarted = new ManualResetEventSlim();
    var reentrantRequest = false;
    var authorizedApply = Task.Run(() => gate.TryApply(
        restartedLease.GetValueOrDefault(),
        () =>
        {
            applyEntered.Set();
            reentrantRequest = gate.TryRequest(restartedLease.GetValueOrDefault());
            releaseApply.Wait();
            Interlocked.Increment(ref applies);
        }));
    Assert(applyEntered.Wait(TimeSpan.FromSeconds(5)),
        "the concurrent lifecycle fixture must enter the authorized apply action");
    var stop = Task.Run(() =>
    {
        stopStarted.Set();
        gate.Stop();
    });
    Assert(stopStarted.Wait(TimeSpan.FromSeconds(5)) && !stop.Wait(TimeSpan.FromMilliseconds(100)),
        "Stop must wait for an already-authorized apply to finish before advancing the lifecycle epoch");
    var start = Task.Run(() =>
    {
        startStarted.Set();
        gate.Start();
    });
    Assert(startStarted.Wait(TimeSpan.FromSeconds(5)) && !start.Wait(TimeSpan.FromMilliseconds(100)),
        "Start must share the same synchronization boundary as an already-authorized apply");
    releaseApply.Set();
    Assert(authorizedApply.Wait(TimeSpan.FromSeconds(5))
           && stop.Wait(TimeSpan.FromSeconds(5))
           && start.Wait(TimeSpan.FromSeconds(5))
           && authorizedApply.Result
           && reentrantRequest
           && applies == 1
           && !gate.TryApply(restartedLease.GetValueOrDefault(), () => Interlocked.Increment(ref applies)),
        "authorized apply, reentrant gate calls, and concurrent Stop/Start must finish without deadlock or permit an old-lease side effect afterward");

    var lifecycleGate = new RefreshGate();
    var lifecycle = new WholesomeLifecycleGate(lifecycleGate);
    Assert(lifecycle.Start(() => { }, () => { })
           && lifecycleGate.TryRequest(),
        "the lifecycle reentrancy fixture must start and queue a refresh");
    var lifecycleLease = lifecycleGate.Begin();
    Assert(lifecycleLease.HasValue,
        "the lifecycle reentrancy fixture must begin a refresh");
    var lifecycleApplyEntered = new ManualResetEventSlim();
    var allowReentrantStop = new ManualResetEventSlim();
    var concurrentStopStarted = new ManualResetEventSlim();
    var lifecycleApply = Task.Run(() => lifecycleGate.TryApply(
        lifecycleLease.GetValueOrDefault(),
        () =>
        {
            lifecycleApplyEntered.Set();
            allowReentrantStop.Wait();
            lifecycle.Stop(() => { });
        }));
    Assert(lifecycleApplyEntered.Wait(TimeSpan.FromSeconds(5)),
        "the lifecycle reentrancy fixture must enter the apply callback");
    var concurrentLifecycleStop = Task.Run(() =>
    {
        concurrentStopStarted.Set();
        lifecycle.Stop(() => { });
    });
    Assert(concurrentStopStarted.Wait(TimeSpan.FromSeconds(5))
           && !concurrentLifecycleStop.Wait(TimeSpan.FromMilliseconds(100)),
        "a concurrent lifecycle Stop must wait while apply remains authorized");
    allowReentrantStop.Set();
    Assert(lifecycleApply.Wait(TimeSpan.FromSeconds(5))
           && concurrentLifecycleStop.Wait(TimeSpan.FromSeconds(5))
           && lifecycleApply.Result
           && lifecycle.IsStopped,
        "a refresh apply callback that reenters lifecycle Stop must not deadlock with a concurrent Stop");
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
    monitor!.Observe(key, 12, 1, Tagged(observation, 1), active: true);
    monitor.Observe(key, 12, 2, Tagged(observation, 2), active: true);
    var third = Tagged(failure, 3);
    Assert(monitor.Observe(key, 12, 3, third, active: true) == third,
        "the lifecycle fixture must reach a reported pickup failure before restart");

    bot.ResetRecoveryLifecycleState();
    Assert(monitor.Observe(key, 12, 4, Tagged(failure, 4), active: true) == null,
        "Stop/Start reset must prevent the same pickup from inheriting target, generation, token, count, outcome, or reported state");
}

void TestStopAbandonsOwnedAttemptBeforeClearingLifecycleState()
{
    var ownership = new WholesomeAttemptOwnership();
    var owner = new object();
    var key = QuestRecoveryKey.ForQuestStage(866, QuestRecoveryStage.Objective);
    ownership.Begin(owner, key, new QuestRecoveryDecision
    {
        State = QuestRecoveryState.Attempting,
        MayAttempt = true,
        AttemptGeneration = 41
    });
    var calls = new List<(QuestRecoveryKey Key, long Generation)>();

    Assert(WholesomeAutoQuest.AbandonOwnedAttempt(
               ownership,
               (ownedKey, generation) =>
               {
                   calls.Add((ownedKey, generation));
                   return ownership.TryGet(owner, out _, out var stillOwned) && stillOwned == generation;
               })
           && calls.Count == 1
           && calls[0].Key.Equals(key)
           && calls[0].Generation == 41,
        "Stop must abandon the exact manager generation while local ownership is still available");
    Assert(ownership.TryGet(owner, out _, out _),
        "the neutral manager release must happen before Stop clears local lifecycle state");
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

void TestProgressMonitorRequestsAlternateAndFailsBoundedlyWithOneCluster()
{
    var clock = new TestRecoveryClock(utcNow);
    var monitor = new WholesomeProgressMonitor(clock);
    var key = QuestRecoveryKey.ForQuestStage(9867, QuestRecoveryStage.Objective);
    var onlyCluster = QuestRecoveryKey.ForEndpoint(
        9867, QuestRecoveryStage.Navigation, 1, "cell:0:0");
    monitor.Sample(WorkSample(key, new[] { 0 }, onlyCluster, active: true));

    QuestProgressUpdate update = new();
    bool requestedAlternate = false;
    for (int second = 2; second <= 8 * 60; second += 2)
    {
        clock.Advance(TimeSpan.FromSeconds(2));
        update = monitor.Sample(WorkSample(key, new[] { 0 }, onlyCluster, active: true));
        requestedAlternate |= update.RequestAlternateCluster;
    }

    Assert(requestedAlternate,
        "production monitor output must request one alternate target cluster after four active minutes");
    Assert(update.Outcomes.Any(outcome =>
            outcome.Key.Equals(key) && outcome.Reason == QuestFailureReason.NoObjectiveProgress &&
            outcome.IsFailureEpisode),
        "one unavailable or unchanged cluster must still end in one bounded eight-minute failure episode");
}

void TestProductionUnavailableAlternatePreservesAttemptUntilBoundedFailure()
{
    var root = Path.Combine(Path.GetTempPath(), "wholesome-one-cluster-production-" + Guid.NewGuid().ToString("N"));
    var manager = QuestRecoveryManager.Instance;
    try
    {
        manager.Configure(new QuestRecoveryEnvironment(
            root, "Wholesome", "OneCluster", "data-v1", "core-v1", "nav-v1"));
        var clock = new TestRecoveryClock(utcNow);
        var monitor = new WholesomeProgressMonitor(clock);
        var key = QuestRecoveryKey.ForQuestStage(9868, QuestRecoveryStage.Objective);
        var onlyCluster = QuestRecoveryKey.ForEndpoint(9868, QuestRecoveryStage.Navigation, 1, "cell:0:0");
        QuestRecoveryDecision owner = manager.TryBeginAttempt(key, new QuestRecoveryContext());
        var behavior = new ForcedQuestObjective(TestQuestObjective.Create(9868, WoWPoint.Zero));
        var bot = new WholesomeAutoQuest();
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var ownership = (WholesomeAttemptOwnership?)typeof(WholesomeAutoQuest)
            .GetField("_attemptOwnership", flags)?.GetValue(bot);
        var gate = (RefreshGate?)typeof(WholesomeAutoQuest)
            .GetField("_refreshGate", flags)?.GetValue(bot);
        var stopped = typeof(WholesomeAutoQuest).GetField("_stopped", flags);
        var process = typeof(WholesomeAutoQuest).GetMethod("ProcessProgressUpdate", flags);
        Assert(ownership != null && gate != null && stopped != null && process != null,
            "the one-cluster regression must invoke production request handling");
        ownership!.Begin(behavior, key, owner);
        stopped!.SetValue(bot, false);

        QuestWorkSample Sample() => WorkSample(
            key, new[] { 0 }, onlyCluster, active: true, attemptGeneration: owner.AttemptGeneration);
        monitor.Sample(Sample());
        QuestProgressUpdate fourMinutes = new();
        for (int second = 2; second <= 4 * 60; second += 2)
        {
            clock.Advance(TimeSpan.FromSeconds(2));
            fourMinutes = monitor.Sample(Sample());
        }
        Assert(fourMinutes.RequestAlternateCluster,
            "the live production attempt must ask for its alternate at four active minutes");
        process!.Invoke(bot, new object[] { behavior, Sample(), fourMinutes });
        Assert(!gate!.Begin().HasValue
               && ownership.TryGet(behavior, out QuestRecoveryKey retainedKey, out long retainedGeneration)
               && retainedKey.Equals(key) && retainedGeneration == owner.AttemptGeneration,
            "an unavailable alternate must not queue a profile rebuild or replace the live attempt generation");

        QuestProgressUpdate eightMinutes = new();
        for (int second = 4 * 60 + 2; second <= 8 * 60; second += 2)
        {
            clock.Advance(TimeSpan.FromSeconds(2));
            eightMinutes = monitor.Sample(Sample());
        }
        Assert(eightMinutes.Outcomes.Any(outcome => outcome.IsFailureEpisode && outcome.Key.Equals(key)),
            "the same monitor attempt must reach its bounded failure at eight active minutes");
        process.Invoke(bot, new object[] { behavior, Sample(), eightMinutes });
        QuestRecoveryRecord failed = manager.GetEntries().Single(record => record.Key.Equals(key));
        Assert(failed.EpisodeCount == 1
               && failed.AttemptGeneration == owner.AttemptGeneration
               && !ownership.TryGet(behavior, out _, out _),
            "production handling must report and release the exact generation that requested the unavailable alternate");
    }
    finally
    {
        manager.Flush();
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
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

void TestBehaviorTransitionFinalizesExactPriorGeneration()
{
    var root = Path.Combine(Path.GetTempPath(), "wholesome-transition-" + Guid.NewGuid().ToString("N"));
    try
    {
        var manager = new QuestRecoveryManager(new TestRecoveryClock(utcNow));
        manager.Configure(new QuestRecoveryEnvironment(root, "Wholesome", "Realm", "data", "core", "nav"));
        var local = new WholesomeAttemptOwnership();
        var ownerA = new object();
        var ownerB = new object();
        var key = QuestRecoveryKey.ForQuestStage(9868, QuestRecoveryStage.Objective);
        QuestRecoveryDecision claimA = manager.TryBeginAttempt(key, new QuestRecoveryContext());
        local.Begin(ownerA, key, claimA);

        Assert(WholesomeAutoQuest.FinalizeChangedOwner(
                   local, ownerB,
                   (ownedKey, generation) => manager.AbandonAttempt(ownedKey, generation))
               && !manager.OwnsAttempt(key, claimA.AttemptGeneration),
            "a real behavior transition must finalize the exact prior manager generation before replacement");

        QuestRecoveryDecision claimB = manager.TryBeginAttempt(key, new QuestRecoveryContext());
        local.Begin(ownerB, key, claimB);
        Assert(!WholesomeAutoQuest.FinalizeChangedOwner(
                   local, ownerB,
                   (ownedKey, generation) => manager.AbandonAttempt(ownedKey, generation))
               && manager.OwnsAttempt(key, claimB.AttemptGeneration),
            "an unchanged owner and stale transition callback must not release the newer generation");

        var staleLocal = new WholesomeAttemptOwnership();
        var staleOwner = new object();
        var replacementBehavior = new object();
        var staleKey = QuestRecoveryKey.ForObjective(9869, 0);
        QuestRecoveryDecision staleClaim = manager.TryBeginAttempt(staleKey, new QuestRecoveryContext());
        staleLocal.Begin(staleOwner, staleKey, staleClaim);
        Assert(manager.AbandonAttempt(staleKey, staleClaim.AttemptGeneration),
            "stale transition fixture must release owner A before acquiring owner B");
        QuestRecoveryDecision replacement = manager.TryBeginAttempt(staleKey, new QuestRecoveryContext());
        Assert(!WholesomeAutoQuest.FinalizeChangedOwner(
                   staleLocal, replacementBehavior,
                   (ownedKey, generation) => manager.AbandonAttempt(ownedKey, generation))
               && manager.OwnsAttempt(staleKey, replacement.AttemptGeneration),
            "a delayed behavior-transition finalizer must release only its stale local token and preserve manager owner B");
    }
    finally
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
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

void TestFinalEndpointAndStageFailuresReportInOneOwnedSequence()
{
    var ownership = new WholesomeAttemptOwnership();
    var owner = new object();
    var stage = QuestRecoveryKey.ForQuestStage(868, QuestRecoveryStage.Objective);
    var endpoint = QuestRecoveryKey.ForEndpoint(868, QuestRecoveryStage.Navigation, 1, "cell:8:9");
    ownership.Begin(owner, stage, new QuestRecoveryDecision
    {
        State = QuestRecoveryState.Attempting,
        MayAttempt = true,
        AttemptGeneration = 61
    });
    IReadOnlyList<QuestAttemptOutcome> reported = Array.Empty<QuestAttemptOutcome>();
    bool ownedDuringBatch = false;

    Assert(WholesomeAutoQuest.ReportOwnedFailures(
               ownership,
               owner,
               new[]
               {
                   QuestAttemptOutcome.Failure(endpoint, QuestFailureReason.PathGenerationFailed, "endpoint failed"),
                   QuestAttemptOutcome.Failure(stage, QuestFailureReason.NoNavigableHotspot, "stage exhausted")
               },
               (IReadOnlyList<QuestAttemptOutcome> bound, out IReadOnlyList<QuestRecoveryDecision> accepted) =>
               {
                   reported = bound;
                   ownedDuringBatch = ownership.TryGet(owner, out _, out var generation) && generation == 61;
                   accepted = new[]
                   {
                       new QuestRecoveryDecision { State = QuestRecoveryState.CoolingDown },
                       new QuestRecoveryDecision { State = QuestRecoveryState.CoolingDown }
                   };
                   return true;
               },
               out var decisions)
           && ownedDuringBatch
           && reported.Count == 2
           && reported[0].Key.Equals(endpoint)
           && reported[1].Key.Equals(stage)
           && reported.All(item => item.AttemptKey?.Equals(stage) == true && item.AttemptGeneration == 61)
           && decisions.Count == 2
           && !ownership.TryGet(owner, out _, out _),
        "the final endpoint failure must be applied first under stage ownership, followed by the stage failure that releases it");
}

void TestRejectedGeneratedBatchRecoversWithoutThrowingOrApplyingEpisode()
{
    var root = Path.Combine(Path.GetTempPath(), "wholesome-generated-race-" + Guid.NewGuid().ToString("N"));
    try
    {
        void RunRace(bool contendEndpoint)
        {
            var manager = new QuestRecoveryManager(new TestRecoveryClock(utcNow));
            manager.Configure(new QuestRecoveryEnvironment(
                Path.Combine(root, contendEndpoint ? "endpoint" : "stop"),
                "Wholesome",
                "Realm",
                "data-v1",
                "core-v1",
                "nav-v1"));
            var stage = QuestRecoveryKey.ForQuestStage(869, QuestRecoveryStage.Objective);
            var endpoint = QuestRecoveryKey.ForEndpoint(869, QuestRecoveryStage.Navigation, 1, "cell:9:10");
            var source = manager.TryBeginAttempt(stage, new QuestRecoveryContext());
            if (contendEndpoint)
                Assert(manager.TryBeginAttempt(endpoint, new QuestRecoveryContext()).MayAttempt,
                    "the batch contention fixture must independently own its endpoint");

            var ownership = new WholesomeAttemptOwnership();
            var behavior = new object();
            ownership.Begin(behavior, stage, source);
            var outcomes = new[]
            {
                QuestAttemptOutcome.Failure(endpoint, QuestFailureReason.PathGenerationFailed, "endpoint failed"),
                QuestAttemptOutcome.Failure(stage, QuestFailureReason.NoNavigableHotspot, "stage exhausted")
            };
            Assert(!WholesomeAutoQuest.ReportOwnedFailures(
                       ownership,
                       behavior,
                       outcomes,
                       (IReadOnlyList<QuestAttemptOutcome> bound, out IReadOnlyList<QuestRecoveryDecision> decisions) =>
                       {
                           if (!contendEndpoint)
                               manager.AbandonAttempt(stage, source.AttemptGeneration);
                           return manager.TryReportGeneratedFailures(bound, new QuestRecoveryContext(), out decisions);
                       },
                       out var rejected)
                   && rejected.Count == 0
                   && ownership.TryGet(behavior, out _, out var retainedGeneration)
                   && retainedGeneration == source.AttemptGeneration,
                "Stop or independently owned target contention must be a non-throwing rejection that retains local ownership for recovery");

            var releases = 0;
            var refreshes = 0;
            Assert(WholesomeAutoQuest.RecoverRejectedOwnedFailures(
                       ownership,
                       behavior,
                       (key, generation) => manager.AbandonAttempt(key, generation),
                       () => releases++,
                       () => refreshes++)
                   && releases == 1
                   && refreshes == 1
                   && !ownership.TryGet(behavior, out _, out _)
                   && !manager.OwnsAttempt(stage, source.AttemptGeneration),
                "rejected production work must abandon the exact source, clear local activation, and request one rebuild");
            Assert(manager.GetEntries().Where(record => record.Key.Equals(stage))
                       .All(record => record.EpisodeCount == 0)
                   && (!contendEndpoint || manager.GetEntries().Any(record =>
                       record.Key.Equals(endpoint) && record.State == QuestRecoveryState.Attempting)),
                "a rejected batch must not falsely apply an episode or overwrite the contended endpoint owner");
        }

        RunRace(contendEndpoint: false);
        RunRace(contendEndpoint: true);
    }
    finally
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}

void TestRejectedGeneratedBatchDoesNotFallThroughToObservations()
{
    var root = Path.Combine(Path.GetTempPath(), "wholesome-generated-processing-" + Guid.NewGuid().ToString("N"));
    var manager = QuestRecoveryManager.Instance;
    try
    {
        var environment = new QuestRecoveryEnvironment(
            root,
            "Wholesome",
            "Processing",
            "data-v1",
            "core-v1",
            "nav-v1");
        manager.Configure(environment);
        var stage = QuestRecoveryKey.ForQuestStage(872, QuestRecoveryStage.Objective);
        var endpoint = QuestRecoveryKey.ForEndpoint(872, QuestRecoveryStage.Navigation, 1, "cell:11:12");
        var source = manager.TryBeginAttempt(stage, new QuestRecoveryContext());
        var target = manager.TryBeginAttempt(endpoint, new QuestRecoveryContext());
        Assert(source.MayAttempt && target.MayAttempt,
            "the production-processing fixture must own both the source and contended target independently");
        manager.Flush();

        var behavior = new ForcedQuestObjective(TestQuestObjective.Create(872, WoWPoint.Zero));
        var bot = new WholesomeAutoQuest();
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var ownership = (WholesomeAttemptOwnership?)typeof(WholesomeAutoQuest)
            .GetField("_attemptOwnership", flags)?.GetValue(bot);
        var gate = (RefreshGate?)typeof(WholesomeAutoQuest)
            .GetField("_refreshGate", flags)?.GetValue(bot);
        var stopped = typeof(WholesomeAutoQuest).GetField("_stopped", flags);
        var process = typeof(WholesomeAutoQuest).GetMethod("ProcessProgressUpdate", flags);
        Assert(ownership != null && gate != null && stopped != null && process != null,
            "the regression must invoke the actual production ProcessProgressUpdate path");
        ownership!.Begin(behavior, stage, source);
        stopped!.SetValue(bot, false);
        var sample = WorkSample(stage, new[] { 0 }, endpoint, active: true, attemptGeneration: source.AttemptGeneration);
        var update = new QuestProgressUpdate
        {
            Outcomes = new[]
            {
                QuestAttemptOutcome.Failure(endpoint, QuestFailureReason.PathGenerationFailed, "rejected endpoint"),
                QuestAttemptOutcome.Failure(stage, QuestFailureReason.NoNavigableHotspot, "rejected stage")
            }
        };

        process!.Invoke(bot, new object[] { behavior, sample, update });

        var sourceAfter = manager.GetEntries().Single(record => record.Key.Equals(stage));
        var targetAfter = manager.GetEntries().Single(record => record.Key.Equals(endpoint));
        Assert(sourceAfter.State == QuestRecoveryState.Eligible
               && sourceAfter.AttemptGeneration == source.AttemptGeneration
               && sourceAfter.EpisodeCount == 0
               && sourceAfter.Evidence.Count == 0
               && targetAfter.State == QuestRecoveryState.Attempting
               && targetAfter.AttemptGeneration == target.AttemptGeneration
               && targetAfter.EpisodeCount == 0
               && targetAfter.Evidence.Count == 0
               && !ownership.TryGet(behavior, out _, out _),
            "a rejected generated batch must stop processing after neutral source release and must not report either failure as an observation");
        var refreshLease = gate!.Begin();
        Assert(refreshLease.HasValue && !gate.Begin().HasValue,
            "a rejected production batch must queue exactly one rebuild");
        gate.Complete(refreshLease.GetValueOrDefault());
        manager.Flush();

        var reloaded = new QuestRecoveryManager(new TestRecoveryClock(utcNow));
        reloaded.Configure(environment);
        var persistedSource = reloaded.GetEntries().Single(record => record.Key.Equals(stage));
        var persistedTarget = reloaded.GetEntries().Single(record => record.Key.Equals(endpoint));
        Assert(persistedSource.State == QuestRecoveryState.Eligible
               && persistedSource.Evidence.Count == 0
               && persistedTarget.State == QuestRecoveryState.HalfOpen
               && persistedTarget.Evidence.Count == 0,
            "rejected-batch persistence must contain only the neutral source clear and ordinary stale-attempt recovery, never rejected evidence");
    }
    finally
    {
        manager.Flush();
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}

void TestManualExclusionClearsMatchingLocalOwnership()
{
    var ownership = new WholesomeAttemptOwnership();
    var behavior = new object();
    var key = QuestRecoveryKey.ForQuestStage(870, QuestRecoveryStage.Pickup);
    ownership.Begin(behavior, key, new QuestRecoveryDecision
    {
        State = QuestRecoveryState.Attempting,
        MayAttempt = true,
        AttemptGeneration = 71
    });
    object released = null!;
    long retained = 0;
    Assert(!WholesomeAutoQuest.ReleaseManuallyExcludedOwnership(
               ownership,
               871,
               owner => released = owner)
           && ownership.TryGet(behavior, out _, out retained) && retained == 71,
        "manual exclusion for another quest must not clear the exact local owner");
    Assert(WholesomeAutoQuest.ReleaseManuallyExcludedOwnership(
               ownership,
               870,
               owner => released = owner)
           && ReferenceEquals(released, behavior)
           && !ownership.TryGet(behavior, out _, out _),
        "installing a same-quest manual terminal must clear its stale local activation exactly once");
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

    Assert(monitor.Observe(key, 7, interactionCycle: 1, Tagged(observation, 1), active: false) == null
           && monitor.Observe(key, 7, interactionCycle: 2, Tagged(failure, 2), active: false) == null,
        "excluded pickup cycles must not count toward failure");
    Assert(monitor.Observe(key, 7, interactionCycle: 3, Tagged(observation, 2), active: true) == null,
        "a persisted observation from an earlier interaction must not consume the current cycle");
    var cycle3 = Tagged(observation, 3);
    Assert(monitor.Observe(key, 7, interactionCycle: 3, cycle3, active: true) == cycle3
           && monitor.Observe(key, 7, interactionCycle: 3, cycle3, active: true) == null,
        "one exact tagged active interaction result may report once but duplicate pulses must not count");
    var cycle4 = Tagged(observation, 4);
    Assert(monitor.Observe(key, 7, interactionCycle: 4, cycle4, active: true) == cycle4,
        "the second distinct active cycle must remain an observation");
    var cycle5 = Tagged(failure, 5);
    Assert(monitor.Observe(key, 7, interactionCycle: 5, cycle5, active: true) == cycle5,
        "only the third distinct active interaction cycle may report pickup failure");

    monitor.Reset();
    Assert(monitor.Observe(key, 7, interactionCycle: 6, Tagged(failure, 6), active: true) == null,
        "lifecycle reset must discard pickup target, generation, interaction token/count, and failure state");
    Assert(typeof(ForcedQuestPickUp).GetProperty("InteractionCycleId") != null,
        "production pickup behavior must expose its existing real interaction-cycle token read-only");
}

QuestAttemptOutcome Tagged(QuestAttemptOutcome outcome, long interactionCycleId) => new()
{
    Key = outcome.Key,
    Kind = outcome.Kind,
    Reason = outcome.Reason,
    IsFailureEpisode = outcome.IsFailureEpisode,
    Evidence = outcome.Evidence,
    ObservedQuestId = outcome.ObservedQuestId,
    OfferedQuestIds = outcome.OfferedQuestIds,
    ObjectiveCounts = outcome.ObjectiveCounts,
    InteractionCycleId = interactionCycleId
};

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

void TestDataLoaderPublishesAndInvalidatesDependencyAuthority()
{
    var directory = Path.Combine(Path.GetTempPath(), $"wholesome-dependencies-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        string path = Path.Combine(directory, "quest_data.json");
        File.WriteAllText(path,
            "{\"Quests\":[{\"Id\":867,\"Name\":\"Ancestor\"},{\"Id\":875,\"Name\":\"Dependent\",\"PrevQuestID\":867}]," +
            "\"QuestGivers\":[],\"QuestEnders\":[],\"CreatureSpawns\":{},\"GameObjectSpawns\":{}}");
        Assert(new DataLoader(path).Load() != null
               && QuestPrerequisiteAuthority.DetermineFromPublishedDependencies(
                   867, new uint[] { 875 }) == QuestPrerequisiteStatus.Active,
            "the real data loader must publish reverse prerequisite authority for active dependents");

        Assert(new DataLoader(Path.Combine(directory, "missing.json")).Load() == null
               && QuestPrerequisiteAuthority.DetermineFromPublishedDependencies(
                   867, new uint[] { 875 }) == QuestPrerequisiteStatus.Unknown,
            "a missing database must invalidate published authority and fail closed");
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

void TestSchedulerOmissionsPersistImmediateRecoveryQuarantine()
{
    var root = Path.Combine(Path.GetTempPath(), "wholesome-omission-" + Guid.NewGuid().ToString("N"));
    try
    {
        var manager = new QuestRecoveryManager(new TestRecoveryClock(utcNow));
        manager.Configure(new QuestRecoveryEnvironment(root, "Wholesome", "Realm", "data", "core", "nav"));
        var db = SchedulerDatabase();
        db.CreatureSpawns.Remove("2000");
        QuestRecoveryContext context = new()
        {
            DatasetVersion = "data",
            CoreVersion = "core",
            NavigationFingerprint = "nav"
        };
        QuestScheduler.MaterializeSchedule(
            db,
            Snapshot(new[] { Accepted(867, false) }, Array.Empty<uint>(), authoritative: false),
            key => manager.Evaluate(key, context),
            10,
            500,
            7,
            reportDataFailure: outcome => manager.Report(outcome, context));

        QuestRecoveryRecord record = manager.GetEntries().Single(item =>
            item.Key.Equals(QuestRecoveryKey.ForObjective(867, 0)));
        Assert(record.State == QuestRecoveryState.Quarantined
               && record.Reason == QuestFailureReason.InvalidQuestData
               && record.Evidence.Single().Text == "scheduler:no-known-hotspots",
            "an endpointless objective omission must become a stable manager-backed data quarantine");

        int evidenceCount = record.Evidence.Count;
        QuestScheduler.MaterializeSchedule(
            db,
            Snapshot(new[] { Accepted(867, false) }, Array.Empty<uint>(), authoritative: false),
            key => manager.Evaluate(key, context),
            10,
            500,
            7,
            reportDataFailure: outcome => manager.Report(outcome, context));
        Assert(manager.GetEntries().Single(item => item.Key.Equals(record.Key)).Evidence.Count == evidenceCount,
            "a persisted data quarantine must prevent the same omission from reappearing on every rebuild");

        var unsupportedDb = SchedulerDatabase();
        unsupportedDb.Quests.Add(new QuestEntry
        {
            Id = 868,
            Name = "Unsupported objective",
            MinLevel = 1,
            QuestLevel = 20,
            Objectives = { new QuestObjective { Index = 0, Type = ObjectiveType.KillMob, MobId = 0, KillCount = 1 } }
        });
        QuestScheduler.MaterializeSchedule(
            unsupportedDb,
            Snapshot(new[] { Accepted(868, false) }, Array.Empty<uint>(), authoritative: false),
            key => manager.Evaluate(key, context), 10, 500, 7,
            reportDataFailure: outcome => manager.Report(outcome, context));
        Assert(manager.GetRecord(QuestRecoveryKey.ForObjective(868, 0))?.Reason ==
               QuestFailureReason.UnsupportedObjective,
            "an unsupported objective must persist immediate core/data quarantine instead of silently rebuilding");

        var relationDb = SchedulerDatabase();
        relationDb.Quests.Add(new QuestEntry
        {
            Id = 869,
            Name = "Missing ender",
            MinLevel = 1,
            QuestLevel = 20,
            Objectives = { new QuestObjective { Index = 0, Type = ObjectiveType.TurnInOnly } }
        });
        relationDb.QuestEnders.Add(new QuestEnderEntry { QuestId = 869, EnderId = 5000 });
        QuestScheduler.MaterializeSchedule(
            relationDb,
            Snapshot(new[] { Accepted(869, true) }, Array.Empty<uint>(), authoritative: false),
            key => manager.Evaluate(key, context), 10, 500, 7,
            reportDataFailure: outcome => manager.Report(outcome, context));
        Assert(manager.GetRecord(QuestRecoveryKey.ForNpc(869, QuestRecoveryStage.TurnIn, 5000))?.Evidence
                .Any(item => item.Text == "scheduler:no-relation-spawns") == true,
            "a missing relation spawn must persist its canonical narrow data failure");

        var assessedDb = SchedulerDatabase();
        assessedDb.Quests.Add(new QuestEntry
        {
            Id = 871,
            Name = "Unassessed objective",
            MinLevel = 1,
            QuestLevel = 20,
            Objectives = { new QuestObjective { Index = 0, Type = ObjectiveType.KillMob, MobId = 2002, KillCount = 1 } }
        });
        assessedDb.CreatureSpawns["2002"] = new() { new SpawnPoint { Map = 1, X = 2, Y = 2 } };
        QuestScheduler.MaterializeSchedule(
            assessedDb,
            Snapshot(new[] { Accepted(871, false) }, Array.Empty<uint>(), authoritative: false),
            key => manager.Evaluate(key, context), 10, 500, 7,
            navigationAssessment: _ => new SpawnNavigationAssessment { IsKnownReachable = false },
            reportDataFailure: outcome => manager.Report(outcome, context));
        Assert(manager.GetRecord(QuestRecoveryKey.ForObjective(871, 0))?.Evidence
                .Any(item => item.Text == "scheduler:no-assessed-hotspots") == true,
            "an objective with no assessed navigable hotspot must persist its canonical data failure");

        QuestScheduler.MaterializeSchedule(
            SchedulerDatabase(),
            Snapshot(new[] { Accepted(870, false) }, Array.Empty<uint>(), authoritative: false),
            key => manager.Evaluate(key, context), 10, 500, 7,
            reportDataFailure: outcome => manager.Report(outcome, context));
        Assert(manager.GetRecord(QuestRecoveryKey.ForQuestStage(870, QuestRecoveryStage.Objective))?.Evidence
                .Any(item => item.Text == "scheduler:accepted-quest-missing") == true,
            "an accepted quest missing from the database must persist a stable stage-level data quarantine");
    }
    finally
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}

void TestSchedulerZeroRowsPersistWithoutRebuildChurn()
{
    var root = Path.Combine(Path.GetTempPath(), "wholesome-zero-rows-" + Guid.NewGuid().ToString("N"));
    var environment = new QuestRecoveryEnvironment(root, "Wholesome", "ZeroRows", "data", "core", "nav");
    QuestRecoveryContext context = new()
    {
        DatasetVersion = "data",
        CoreVersion = "core",
        NavigationFingerprint = "nav"
    };
    try
    {
        var db = new QuestDatabase();
        db.Quests.Add(new QuestEntry { Id = 881, Name = "Objective rows missing", MinLevel = 1, QuestLevel = 20 });
        db.Quests.Add(new QuestEntry { Id = 882, Name = "Ender rows missing", MinLevel = 1, QuestLevel = 20 });
        var manager = new QuestRecoveryManager(new TestRecoveryClock(utcNow));
        manager.Configure(environment);
        void Build(QuestRecoveryManager current) => QuestScheduler.MaterializeSchedule(
            db,
            Snapshot(new[] { Accepted(881, false), Accepted(882, true) }, Array.Empty<uint>(), authoritative: false),
            key => current.Evaluate(key, context), 10, 500, 7,
            reportDataFailure: outcome => current.Report(outcome, context));

        Build(manager);
        var objectiveKey = QuestRecoveryKey.ForQuestStage(881, QuestRecoveryStage.Objective);
        var turnInKey = QuestRecoveryKey.ForQuestStage(882, QuestRecoveryStage.TurnIn);
        QuestRecoveryRecord objective = manager.GetEntries().Single(record => record.Key.Equals(objectiveKey));
        QuestRecoveryRecord turnIn = manager.GetEntries().Single(record => record.Key.Equals(turnInKey));
        Assert(objective.State == QuestRecoveryState.Quarantined
               && objective.Reason == QuestFailureReason.InvalidQuestData
               && objective.Evidence.Single().Text == "scheduler:no-objective-rows"
               && turnIn.State == QuestRecoveryState.Quarantined
               && turnIn.Reason == QuestFailureReason.InvalidQuestData
               && turnIn.Evidence.Single().Text == "scheduler:no-ender-relations",
            "accepted work with no objective or ender rows must become stable stage-level data quarantines");
        manager.Flush();

        var reloaded = new QuestRecoveryManager(new TestRecoveryClock(utcNow.AddMinutes(1)));
        reloaded.Configure(environment);
        Build(reloaded);
        Assert(reloaded.GetRecord(objectiveKey)?.Evidence.Count == 1
               && reloaded.GetRecord(turnInKey)?.Evidence.Count == 1,
            "persisted zero-row quarantines must not churn duplicate evidence on scheduler rebuild");

        reloaded.SetManualBlacklist(883, true);
        reloaded.MarkCompleted(884);
        db.Quests.Add(new QuestEntry { Id = 883, Name = "Manual zero objective" });
        db.Quests.Add(new QuestEntry { Id = 884, Name = "Completed zero objective" });
        QuestScheduler.MaterializeSchedule(
            db,
            Snapshot(new[] { Accepted(883, false), Accepted(884, false) }, Array.Empty<uint>(), authoritative: false),
            key => reloaded.Evaluate(key, context), 10, 500, 7,
            reportDataFailure: outcome => reloaded.Report(outcome, context));
        Assert(!reloaded.GetEntries().Any(record =>
                   record.Key.Equals(QuestRecoveryKey.ForQuestStage(883, QuestRecoveryStage.Objective)))
               && !reloaded.GetEntries().Any(record =>
                   record.Key.Equals(QuestRecoveryKey.ForQuestStage(884, QuestRecoveryStage.Objective))),
            "manual and completed terminal precedence must suppress zero-row automatic quarantine reports");
    }
    finally
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
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
