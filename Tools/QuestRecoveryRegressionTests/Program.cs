using Styx.Logic.Questing.Recovery;
using Styx.Logic.Questing;
using Styx.Logic.Profiles.Quest;
using Bots.Quest.QuestOrder;
using Bots.Quest.Actions;
using Styx.Helpers;
using Styx.Logic.Inventory;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using System.Globalization;

if (args.Contains("--routine-compatibility"))
{
    try { RoutineCompilationRegression.Run(); }
    catch (Exception ex) { Console.Error.WriteLine(ex.Message); Environment.ExitCode = 1; }
    return;
}

var now = new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);
var testRoot = Path.Combine(AppContext.BaseDirectory, "quest-recovery-test-data");
ResetDirectory(testRoot);

try
{
    var resolver = typeof(ForcedBehaviorExecutor).GetMethod("ResolveQuestObjectiveIndex",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
    Assert(resolver != null, "objective resolution must validate explicit item identity before trusting an index");
    var eggObjectives = new List<Quest.QuestObjective>
    {
        new(0, 5058, 12, null, Quest.QuestObjectiveType.CollectItem),
        new(1, 5059, 1, null, Quest.QuestObjectiveType.CollectItem)
    };
    int ResolveEgg(string xml) => (int)resolver!.Invoke(null, new object[] {
        ObjectiveNode.FromXml(System.Xml.Linq.XElement.Parse(xml)), eggObjectives })!;
    Assert(ResolveEgg("<Objective QuestId='868' Type='CollectItem' ItemId='5058' Index='1'/>") == 0,
        "Egg Hunt must select twelve eggs, not the already-carried Digging Claw");
    Assert(ResolveEgg("<Objective QuestId='868' Type='CollectItem' ItemId='9999' Index='1'/>") == -1,
        "an unknown explicit item must not silently select a different objective");
    Assert(ResolveEgg("<Objective QuestId='868' Type='CollectItem' Index='1'/>") == 1,
        "legacy index-only objectives must remain supported");
    UpstreamMergeRegression.Run();
    TestCompletedQuestTraversalStopsAtInvalidPointers();
    TestCompletedQuestTraversalStopsAtRepeatedPointer();
    TestCompletedQuestTraversalStopsAtReaderFailure();
    TestCompletedQuestTraversalCapsAtTenThousandNodes();
    TestCompletedQuestTraversalDeduplicatesQuestIds();
    TestCompletedQuestLuaChunksParseDeterministically();
    TestCompletedQuestCacheInvalidatesAcrossIdentityChanges();
    TestCompletedQuestCacheSnapshotsAreStable();
    TestCompletedQuestCacheSerializesConcurrentRefreshes();
    TestCompletedQuestCacheDiscardsRefreshWhenIdentityChanges();
    TestQuestOrderDoesNotMutateWithoutAuthoritativeCompletion();
    TestProfileManagerPublishesSameMetadataReplacement();
    TestQuestingCompletedQuestIdsAreSafeWithoutClient();
    TestQuestManagerObsoleteGuidanceShowsCompilableTryCall();
    TestStuckDetectionUsesActualMovementSpeedAndDisplacement();
    TestMeshNavigatorPulseDoesNotStreamTiles();
    TestQuestTravelSuppressesOpportunisticTargeting();
    TestExclusiveForcedBehaviorSuppressesServicePreemption();
    TestTrainerTravelRequiresEfficientRoute();
    TestQuestTurnInDoesNotPreemptCombatPoi();
    TestQuestCompletionAuthorityIsTriState();
    TestEquipmentFingerprintPreservesSlotOrderAndDurabilityClass();
    TestProfileCompletionExpressionsPreserveUnknown();
    TestCompileBatchCompletionExpressionsPreserveUnknown();
    TestCompletionEvaluationScopesNestAndRecoverFromExceptions();
    TestForcedConditionBehaviorsDeferUnknown();
    TestCompletionDependentActionGatesDeferUnknown();
    TestQuestAbandonmentInputGuards();
    TestQuestAbandonmentPrerequisiteGuards();
    TestPrerequisiteAuthorityReverseScansDependencies();
    TestPrerequisiteAuthorityTraversesCompleteGraphSafely();
    TestPrerequisiteAuthorityHonorsTraversalBoundary();
    TestQuestAbandonmentSlotMatrix();
    TestQuestAbandonmentStateMatrix();
    TestQuestAbandonmentReasonMatrix();
    TestManagerAutomaticAbandonmentIsAtomic(Path.Combine(testRoot, "abandon-atomic"), now);
    TestAutomaticAbandonmentRejectsContextResetAndPersistsIntent(
        Path.Combine(testRoot, "abandon-intent"), now);
    TestHistoricalProgressBlocksAbandonmentAcrossReload(
        Path.Combine(testRoot, "abandon-history"), now);
    TestProgressPersistsPerCounterMaximum(Path.Combine(testRoot, "progress-maximum"), now);
    TestFailureContextPersistsPerCounterMaximum(Path.Combine(testRoot, "failure-progress-maximum"), now);
    TestOwnedProgressReleasesHistoricalEqualAttempt(
        Path.Combine(testRoot, "owned-progress-history"), now);
    TestOwnedProgressRejectsUnprovenStaleAndTerminalReports(
        Path.Combine(testRoot, "owned-progress-rejection"), now);
    TestOwnedProgressPreservesConsumedItemHistory(
        Path.Combine(testRoot, "owned-progress-consumed"), now);
    TestQuestRelationParserUsesInvariantCulture();
    TestQuestRelationParserRetainsValidSiblings();
    TestQuestRelationParserRejectsZeroEntry();
    TestQuestRelationParserRejectsNegativeAndOverflowEntries();
    TestQuestRelationParserTrimsAndPreservesDuplicatesInOrder();
    TestQuestRelationParserHandlesNullAndEmptyInput();
    TestQuestRelationParserRejectsNonFiniteOrMalformedCoordinates();
    RunPolicyRegressions(now);
    TestStoreRoundTripAndAtomicReplacement(Path.Combine(testRoot, "store"), now);
    TestCorruptStoreQuarantine(Path.Combine(testRoot, "corrupt"));
    TestStoreDirectOpenDistinguishesAbsenceAndAccess(Path.Combine(testRoot, "store-open"));
    TestValidStoreIoFailureIsSurfaced(Path.Combine(testRoot, "store-io"));
    TestUnsupportedSchemaIsPreserved(Path.Combine(testRoot, "unsupported-schema"));
    TestPersistenceAndIdentityIsolation(Path.Combine(testRoot, "manager"), now);
    TestPersistenceFailureCanRetry(Path.Combine(testRoot, "persistence-retry"), now);
    TestIdentitySwitchRequiresSuccessfulFlush(Path.Combine(testRoot, "switch-flush"), now);
    TestIdentitySwitchLoadIsTransactional(Path.Combine(testRoot, "switch-load"), now);
    TestPersistedAttemptingRecoversOneProbe(Path.Combine(testRoot, "stale-attempt"), now);
    TestCollisionSafeIdentityPaths(Path.Combine(testRoot, "identity-paths"), now);
    TestSuperscriptDeviceIdentityPaths(Path.Combine(testRoot, "superscript-paths"), now);
    TestProductionDiagnosticsReachBotLogger(Path.Combine(testRoot, "production-logging"));
    TestManagerRollingBudget(Path.Combine(testRoot, "rolling-budget"), now);
    TestGeneratedFailureBatchCountsOneRollingEpisode(Path.Combine(testRoot, "generated-batch-budget"), now);
    TestLegacyMigrationIsIdempotent(Path.Combine(testRoot, "migration"), now);
    TestLegacyCrossStageMaterialization(Path.Combine(testRoot, "legacy-cross-stage"), now);
    TestLegacyRekeyKeepsEvidenceSourcesDistinct(Path.Combine(testRoot, "legacy-evidence-source"), now);
    TestEvidenceCycleSeparatesFailureProgressReset(Path.Combine(testRoot, "evidence-cycle-reset"), now);
    TestEvidenceCycleAdvancesAtEpisodeZero(Path.Combine(testRoot, "evidence-cycle-zero"), now);
    TestLegacyEvidenceWithoutSourceKeyIsUnknown(Path.Combine(testRoot, "evidence-legacy-source-key"), now);
    TestEvidenceCoalescingRespectsEpisode(Path.Combine(testRoot, "evidence-episodes"), now);
    TestConcurrentReportingAndAttemptOwnership(Path.Combine(testRoot, "concurrency"), now);
    TestSuccessfulAttemptReleasesOwnership(Path.Combine(testRoot, "success-release"), now);
    TestStaleSuccessCannotReleaseNewOwner(Path.Combine(testRoot, "success-generation"), now);
    TestIncompleteRedirectRequiresExactGeneration(Path.Combine(testRoot, "redirect-generation"), now);
    TestOwnedRedirectApiRejectsNonRedirectOutcomesWithoutMutation(
        Path.Combine(testRoot, "redirect-api-misuse"), now);
    TestOwnedTerminalOutcomesRequireExactCurrentGeneration(Path.Combine(testRoot, "terminal-generation"), now);
    TestOwnedFailureRequiresExactGenerationAndReleasesAtomically(Path.Combine(testRoot, "failure-generation"), now);
    TestOwnedEndpointFailurePreservesStageHistory(Path.Combine(testRoot, "failure-endpoint-owner"), now);
    TestGeneratedFailureAuthorityAndAtomicStageSequence(Path.Combine(testRoot, "failure-authority"), now);
    TestPickupAttemptAuthorizesNarrowAdapterFailures(Path.Combine(testRoot, "pickup-adapter-authority"), now);
    TestTurnInAttemptAuthorizesNarrowAdapterFailures(Path.Combine(testRoot, "turnin-adapter-authority"), now);
    TestNeutralAbandonReleasesOnlyExactAttemptGeneration(Path.Combine(testRoot, "neutral-abandon"), now);
    TestRetryNowRejectsPriorOwnerSuccess(Path.Combine(testRoot, "success-retry-now"), now);
    TestIdentitySwitchCannotReuseOwnership(Path.Combine(testRoot, "success-identity"), now);
    TestManualBlacklistReplacementCannotReuseOwnership(Path.Combine(testRoot, "success-manual"), now);
    TestManualBlacklistNormalizesEveryOwnedScope(Path.Combine(testRoot, "manual-owned-scopes"), now);
    TestManualBlacklistPreservesCanonicalPickupAutomatic(Path.Combine(testRoot, "manual-pickup-preserve"), now);
    TestClearExclusionCombinesOverlayAndSelectedAutomatic(Path.Combine(testRoot, "combined-clear"), now);
    TestLegacyCanonicalManualBlacklistMigrates(Path.Combine(testRoot, "legacy-manual-migrate"), now);
    TestQuestWideTerminalPrecedenceAndAutomaticRestoration(Path.Combine(testRoot, "terminal-precedence"), now);
    TestCompletedRecordsSurviveManualBlacklistToggleAndCompaction(Path.Combine(testRoot, "manual-completed"), now);
    TestMarkCompletedPreservesManualOverlay(Path.Combine(testRoot, "complete-overlay"), now);
    TestDeathResetRequiresDirectionalEquipmentImprovement(now);
    TestDeathResetRecognizesEquippedReplacementAcrossReload(
        Path.Combine(testRoot, "death-equipment-reload"), now);
    TestOwnershipFenceSurvivesManualReleaseReload(Path.Combine(testRoot, "success-empty-reload"), now);
    TestSuccessfulHalfOpenClearsEscalation(Path.Combine(testRoot, "success-half-open"), now);
    TestSuccessCannotReopenTerminalStates(Path.Combine(testRoot, "success-terminal"), now);
    Console.WriteLine("Quest recovery regression tests passed.");
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    global::System.Environment.ExitCode = 1;
}
finally
{
    if (Directory.Exists(testRoot))
    {
        Directory.Delete(testRoot, recursive: true);
    }
}

static void TestAutomaticAbandonmentRejectsContextResetAndPersistsIntent(
    string settingsRoot,
    DateTime now)
{
    var clock = new FixedClock(now);
    var environment = CreateEnvironment(settingsRoot, "Jeof", "Lordaeron");
    var manager = new QuestRecoveryManager(clock);
    var key = QuestRecoveryKey.ForQuestStage(9801, QuestRecoveryStage.Objective);
    manager.Configure(environment);
    QuestRecoveryDecision owner = manager.TryBeginAttempt(key, Context());
    Assert(owner.MayAttempt, "fixture must acquire the abandonment failure episode");
    manager.TryReportOwnedOutcome(
        QuestAttemptOutcome.Failure(
            key, key, owner.AttemptGeneration,
            QuestFailureReason.InvalidQuestData, "invalid objective data"),
        Context());

    int resetActions = 0;
    QuestAbandonmentDecision resetDenied = manager.TryExecuteAutomaticAbandonment(
        key,
        () => new QuestAbandonmentLiveSnapshot
        {
            IsAccepted = true,
            StateIsCertain = true,
            PrerequisiteStatus = QuestPrerequisiteStatus.NotActive,
            FreeQuestLogSlots = 1,
            RecoveryContext = new QuestRecoveryContext
            {
                PlayerLevel = 34,
                EquipmentFingerprint = "100:healthy",
                EquipmentHealthKnown = true,
                CriticalEquipmentCount = 0,
                DatasetVersion = "quest-data-v2",
                CoreVersion = "core-v1",
                NavigationFingerprint = "nav-v1"
            }
        },
        () => resetActions++);
    Assert(!resetDenied.MayAbandon && resetActions == 0,
        "a relevant context reset must reopen a probe instead of abandoning stale quarantine evidence");

    QuestAbandonmentDecision allowed = manager.TryExecuteAutomaticAbandonment(
        key,
        () => new QuestAbandonmentLiveSnapshot
        {
            IsAccepted = true,
            StateIsCertain = true,
            PrerequisiteStatus = QuestPrerequisiteStatus.NotActive,
            FreeQuestLogSlots = 1,
            RecoveryContext = Context()
        },
        () => { });
    Assert(allowed.MayAbandon, "unchanged authoritative quarantine may abandon under pressure");

    var reloaded = new QuestRecoveryManager(clock);
    reloaded.Configure(environment);
    QuestRecoveryRecord record = reloaded.GetEntries().Single(item => item.Key.Equals(key));
    Assert(record.AbandonmentStatus == QuestAbandonmentStatus.Succeeded
           && record.AbandonmentReason.Contains("Automatic abandonment permitted", StringComparison.Ordinal),
        "the abandonment intent and terminal success must be durable across a fresh manager reload");

    string budgetRoot = settingsRoot + "-budget";
    var budgetEnvironment = CreateEnvironment(budgetRoot, "Jeof", "Lordaeron");
    string budgetPath = Path.Combine(
        budgetRoot, "QuestRecovery", "Jeof-Lordaeron", "quest-recovery.json");
    new QuestRecoveryStore(budgetPath).Save(new QuestRecoveryDocument
    {
        CharacterName = "Jeof",
        RealmName = "Lordaeron",
        RollingFailureUtc = Enumerable.Range(0, 6)
            .Select(index => now.AddMinutes(-index - 1)).ToArray(),
        Records = new[]
        {
            new QuestRecoveryRecord
            {
                Key = key,
                State = QuestRecoveryState.Quarantined,
                Reason = QuestFailureReason.InvalidQuestData,
                DatasetVersion = "quest-data-v1",
                CoreVersion = "core-v1",
                NavigationFingerprint = "nav-v1"
            }
        }
    });
    var budgetManager = new QuestRecoveryManager(clock);
    budgetManager.Configure(budgetEnvironment);
    int budgetActions = 0;
    QuestAbandonmentDecision budgetDenied = budgetManager.TryExecuteAutomaticAbandonment(
        key,
        () => new QuestAbandonmentLiveSnapshot
        {
            IsAccepted = true,
            StateIsCertain = true,
            PrerequisiteStatus = QuestPrerequisiteStatus.NotActive,
            FreeQuestLogSlots = 1,
            RecoveryContext = new QuestRecoveryContext
            {
                DatasetVersion = "quest-data-v2",
                CoreVersion = "core-v1",
                NavigationFingerprint = "nav-v1"
            }
        },
        () => budgetActions++);
    Assert(!budgetDenied.MayAbandon && budgetActions == 0,
        "a context reset whose half-open probe is denied by the rolling budget must retain the quest");
}

static void TestMarkCompletedPreservesManualOverlay(string settingsRoot, DateTime now)
{
    var environment = CreateEnvironment(settingsRoot, "Jeof", "Lordaeron");
    var manager = new QuestRecoveryManager(new FixedClock(now));
    manager.Configure(environment);
    var objective = QuestRecoveryKey.ForObjective(9802, 0);
    manager.Report(
        QuestAttemptOutcome.Failure(objective, QuestFailureReason.InvalidQuestData, "bad data"),
        Context());
    manager.SetManualBlacklist(9802, true);
    manager.MarkCompleted(9802);
    manager.Flush();

    var reloaded = new QuestRecoveryManager(new FixedClock(now));
    reloaded.Configure(environment);
    QuestRecoveryRecord[] records = reloaded.GetEntries().Where(item => item.Key.QuestId == 9802).ToArray();
    Assert(records.Any(item => item.State == QuestRecoveryState.ManualBlacklist)
           && records.Any(item => item.State == QuestRecoveryState.Completed)
           && records.Where(item => item.State != QuestRecoveryState.ManualBlacklist)
               .All(item => item.State == QuestRecoveryState.Completed),
        "completion must preserve the manual overlay while completing every non-manual record");
}

static void TestDeathResetRequiresDirectionalEquipmentImprovement(DateTime now)
{
    var key = QuestRecoveryKey.ForObjective(9803, 0);
    var healthy = new QuestRecoveryContext
    {
        EquipmentFingerprint = "100:healthy|200:healthy",
        EquipmentHealthKnown = true,
        CriticalEquipmentCount = 0,
        EquipmentEntries = new uint[] { 100, 200 }
    };
    QuestRecoveryRecord healthyFailure = QuestRecoveryPolicy.ApplyFailure(
        QuestRecoveryRecord.Create(key), QuestFailureReason.RepeatedDeaths, healthy, now);
    healthyFailure = QuestRecoveryPolicy.ApplyFailure(healthyFailure,
        QuestFailureReason.RepeatedDeaths, healthy, now.AddHours(1));
    healthyFailure = QuestRecoveryPolicy.ApplyFailure(healthyFailure,
        QuestFailureReason.RepeatedDeaths, healthy, now.AddHours(2));
    QuestRecoveryDecision degraded = QuestRecoveryPolicy.Evaluate(
        healthyFailure,
        new QuestRecoveryContext
        {
            EquipmentFingerprint = "100:critical|200:healthy",
            EquipmentHealthKnown = true,
            CriticalEquipmentCount = 1
        },
        0,
        now.AddHours(3));
    Assert(!degraded.MayAttempt,
        "healthy-to-critical equipment change must not reopen a death quarantine");

    var critical = new QuestRecoveryContext
    {
        EquipmentFingerprint = "100:critical|200:healthy",
        EquipmentHealthKnown = true,
        CriticalEquipmentCount = 1,
        EquipmentEntries = new uint[] { 100, 200 }
    };
    QuestRecoveryRecord criticalFailure = QuestRecoveryPolicy.ApplyFailure(
        QuestRecoveryRecord.Create(key), QuestFailureReason.RepeatedDeaths, critical, now);
    criticalFailure = QuestRecoveryPolicy.ApplyFailure(criticalFailure,
        QuestFailureReason.RepeatedDeaths, critical, now.AddHours(1));
    criticalFailure = QuestRecoveryPolicy.ApplyFailure(criticalFailure,
        QuestFailureReason.RepeatedDeaths, critical, now.AddHours(2));
    QuestRecoveryDecision repaired = QuestRecoveryPolicy.Evaluate(
        criticalFailure, healthy, 0, now.AddHours(3));
    Assert(repaired.MayAttempt && repaired.State == QuestRecoveryState.HalfOpen,
        "critical-to-healthy equipment improvement may reopen one death-quarantine probe");

    var removedCriticalItem = new QuestRecoveryContext
    {
        EquipmentFingerprint = "200:healthy",
        EquipmentHealthKnown = true,
        CriticalEquipmentCount = 0,
        EquipmentEntries = new uint[] { 200 }
    };
    Assert(!QuestRecoveryPolicy.Evaluate(
            criticalFailure, removedCriticalItem, 0, now.AddHours(3)).MayAttempt,
        "unequipping the only critical item must not masquerade as an equipment repair");

    QuestRecoveryRecord legacy = new()
    {
        Key = criticalFailure.Key,
        State = criticalFailure.State,
        Reason = criticalFailure.Reason,
        FirstFailureUtc = criticalFailure.FirstFailureUtc,
        LastFailureUtc = criticalFailure.LastFailureUtc,
        NextHalfOpenUtc = criticalFailure.NextHalfOpenUtc,
        EpisodeCount = criticalFailure.EpisodeCount,
        PlayerLevelAtFailure = criticalFailure.PlayerLevelAtFailure,
        EquipmentFingerprint = criticalFailure.EquipmentFingerprint,
        EquipmentHealthKnown = false,
        CriticalEquipmentCount = 0
    };
    Assert(!QuestRecoveryPolicy.Evaluate(legacy, healthy, 0, now.AddHours(3)).MayAttempt,
        "legacy equipment fingerprints without comparable health data must migrate conservatively");
}

static void TestDeathResetRecognizesEquippedReplacementAcrossReload(string settingsRoot, DateTime now)
{
    var environment = CreateEnvironment(settingsRoot, "Jeof", "Lordaeron");
    var key = QuestRecoveryKey.ForObjective(9804, 0);
    var baseline = new QuestRecoveryContext
    {
        EquipmentFingerprint = "100:critical|200:healthy",
        EquipmentHealthKnown = true,
        CriticalEquipmentCount = 1,
        EquipmentEntries = new uint[] { 100, 200 }
    };
    var clock = new FixedClock(now);
    var manager = new QuestRecoveryManager(clock);
    manager.Configure(environment);
    for (int episode = 0; episode < 3; episode++)
    {
        QuestRecoveryDecision owner = manager.TryBeginAttempt(key, baseline);
        Assert(owner.MayAttempt, "the death replacement fixture must own each bounded failure episode");
        manager.TryReportOwnedOutcome(QuestAttemptOutcome.Failure(
            key, key, owner.AttemptGeneration, QuestFailureReason.RepeatedDeaths,
            $"death {episode + 1}"), baseline);
        clock.UtcNow = clock.UtcNow.AddMinutes(61);
    }
    manager.Flush();

    var reloaded = new QuestRecoveryManager(new FixedClock(clock.UtcNow));
    reloaded.Configure(environment);
    var replacement = new QuestRecoveryContext
    {
        EquipmentFingerprint = "300:critical|200:healthy",
        EquipmentHealthKnown = true,
        CriticalEquipmentCount = 1,
        EquipmentEntries = new uint[] { 200, 300 }
    };
    Assert(reloaded.Evaluate(key, replacement).MayAttempt,
        "replacing an equipped item must reopen one controlled death probe even when critical count is unchanged");

    var removed = new QuestRecoveryContext
    {
        EquipmentFingerprint = "100:critical",
        EquipmentHealthKnown = true,
        CriticalEquipmentCount = 1,
        EquipmentEntries = new uint[] { 100 }
    };
    Assert(!reloaded.Evaluate(key, removed).MayAttempt,
        "removing or unequipping an item must not masquerade as a capability improvement");

    var removedCriticalItem = new QuestRecoveryContext
    {
        EquipmentFingerprint = "200:healthy",
        EquipmentHealthKnown = true,
        CriticalEquipmentCount = 0,
        EquipmentEntries = new uint[] { 200 }
    };
    Assert(!reloaded.Evaluate(key, removedCriticalItem).MayAttempt,
        "unequipping the only critical item must not masquerade as an equipment repair");

    var added = new QuestRecoveryContext
    {
        EquipmentFingerprint = "100:critical|200:healthy|300:healthy",
        EquipmentHealthKnown = true,
        CriticalEquipmentCount = 1,
        EquipmentEntries = new uint[] { 100, 200, 300 }
    };
    Assert(reloaded.Evaluate(key, added).MayAttempt,
        "adding an equipped item without worsening critical damage must count as capability improvement");

    var degraded = new QuestRecoveryContext
    {
        EquipmentFingerprint = "100:critical|200:critical",
        EquipmentHealthKnown = true,
        CriticalEquipmentCount = 2,
        EquipmentEntries = new uint[] { 100, 200 }
    };
    Assert(!reloaded.Evaluate(key, degraded).MayAttempt,
        "the same equipped entries becoming more critical must not count as improvement");

    var repaired = new QuestRecoveryContext
    {
        EquipmentFingerprint = "100:healthy|200:healthy",
        EquipmentHealthKnown = true,
        CriticalEquipmentCount = 0,
        EquipmentEntries = new uint[] { 100, 200 }
    };
    Assert(reloaded.Evaluate(key, repaired).MayAttempt,
        "repairing the same equipped entries must remain a material improvement");
}

static void TestQuestAbandonmentInputGuards()
{
    var denials = new (string Name, QuestAbandonmentContext Context, string Reason)[]
    {
        ("not accepted", AbandonmentContext(isAccepted: false),
            "Automatic abandonment denied: quest is not accepted."),
        ("completed", AbandonmentContext(isCompleted: true),
            "Automatic abandonment denied: quest is completed."),
        ("uncertain", AbandonmentContext(stateIsCertain: false),
            "Automatic abandonment denied: accepted/completed/progress state is uncertain."),
        ("progressed", AbandonmentContext(hasObjectiveProgress: true),
            "Automatic abandonment denied: quest has objective progress.")
    };

    foreach (var denial in denials)
    {
        var decision = QuestAbandonmentPolicy.Evaluate(denial.Context);
        Assert(!decision.MayAbandon && decision.Reason == denial.Reason,
            $"{denial.Name} must deny automatic abandonment with its precise guard reason");
    }
}

static void TestQuestAbandonmentPrerequisiteGuards()
{
    Assert(QuestPrerequisiteAuthority.Determine(
               relationDataAvailable: false,
               nextQuestId: 0,
               nextQuestIsAccepted: false,
               activeGuideContainsNextQuest: false) == QuestPrerequisiteStatus.Unknown
           && QuestPrerequisiteAuthority.Determine(
               relationDataAvailable: true,
               nextQuestId: 456,
               nextQuestIsAccepted: false,
               activeGuideContainsNextQuest: true) == QuestPrerequisiteStatus.Active
           && QuestPrerequisiteAuthority.Determine(
               relationDataAvailable: true,
               nextQuestId: 0,
               nextQuestIsAccepted: false,
               activeGuideContainsNextQuest: false) == QuestPrerequisiteStatus.NotActive,
        "prerequisite authority must distinguish missing relation data, an active guide chain, and a proven terminal quest");

    QuestAbandonmentDecision unknown = QuestAbandonmentPolicy.Evaluate(
        AbandonmentContext(prerequisiteStatus: QuestPrerequisiteStatus.Unknown));
    QuestAbandonmentDecision active = QuestAbandonmentPolicy.Evaluate(
        AbandonmentContext(prerequisiteStatus: QuestPrerequisiteStatus.Active));
    QuestAbandonmentDecision safe = QuestAbandonmentPolicy.Evaluate(
        AbandonmentContext(prerequisiteStatus: QuestPrerequisiteStatus.NotActive));

    Assert(!unknown.MayAbandon
           && unknown.Reason ==
               "Automatic abandonment denied: active prerequisite status is uncertain.",
        "missing prerequisite authority must conservatively deny abandonment with a precise reason");
    Assert(!active.MayAbandon
           && active.Reason ==
               "Automatic abandonment denied: quest is an active prerequisite for the current guide or quest chain.",
        "an active prerequisite must deny abandonment with a precise reason");
    Assert(safe.MayAbandon,
        "certain proof that the quest is not an active prerequisite must preserve the guarded allow path");
}

static void TestPrerequisiteAuthorityReverseScansDependencies()
{
    Assert(QuestPrerequisiteAuthority.DetermineFromDependencies(
            867,
            new[]
            {
                new QuestDependencyEvidence(875, 867, isActive: true, isAuthoritative: true)
            }) == QuestPrerequisiteStatus.Active,
        "an active dependent must protect its prerequisite even when the prerequisite advertises NextQuestId=0");
    Assert(QuestPrerequisiteAuthority.DetermineFromDependencies(
            867,
            new[]
            {
                new QuestDependencyEvidence(875, 0, isActive: true, isAuthoritative: false)
            }) == QuestPrerequisiteStatus.Unknown,
        "unknown active profile/database dependency data must fail closed");
    Assert(QuestPrerequisiteAuthority.DetermineFromDependencies(
            867,
            new[]
            {
                new QuestDependencyEvidence(875, 900, isActive: false, isAuthoritative: true)
            }) == QuestPrerequisiteStatus.NotActive,
        "complete authoritative reverse dependency information may prove no active dependent");

    QuestPrerequisiteAuthority.PublishAuthoritativeDependencies(new[]
    {
        new QuestDependencyEvidence(875, 867, isActive: false, isAuthoritative: true)
    });
    Assert(QuestPrerequisiteAuthority.DetermineFromPublishedDependencies(
               867, new uint[] { 875 }) == QuestPrerequisiteStatus.Active
           && QuestPrerequisiteAuthority.DetermineFromPublishedDependencies(
               867, Array.Empty<uint>()) == QuestPrerequisiteStatus.NotActive,
        "the published database graph must reverse-scan current active quest IDs and protect a NextQuestId=0 ancestor");
    QuestPrerequisiteAuthority.ClearPublishedDependencyAuthority();
    Assert(QuestPrerequisiteAuthority.DetermineFromPublishedDependencies(
               867, Array.Empty<uint>()) == QuestPrerequisiteStatus.Unknown,
        "missing or invalidated dependency data must fail closed instead of proving no dependent");
}

static void TestPrerequisiteAuthorityTraversesCompleteGraphSafely()
{
    QuestPrerequisiteAuthority.ClearPublishedDependencyAuthority();
    QuestPrerequisiteAuthority.PublishAuthoritativeDependencies(Array.Empty<QuestDependencyEvidence>());
    Assert(QuestPrerequisiteAuthority.DetermineFromPublishedDependencies(
               867, Array.Empty<uint>()) == QuestPrerequisiteStatus.Unknown,
        "an empty dependency publication must not be treated as authoritative proof");

    QuestPrerequisiteAuthority.PublishAuthoritativeDependencies(new[]
    {
        new QuestDependencyEvidence(875, 867, isActive: false, isAuthoritative: true),
        new QuestDependencyEvidence(900, 875, isActive: false, isAuthoritative: true),
        new QuestDependencyEvidence(867, 900, isActive: false, isAuthoritative: true)
    });
    Assert(QuestPrerequisiteAuthority.DetermineFromPublishedDependencies(
               867, new uint[] { 900 }) == QuestPrerequisiteStatus.Active,
        "a transitive active descendant must protect every ancestor, including through a cycle");
    Assert(QuestPrerequisiteAuthority.DetermineFromPublishedDependencies(
               867, new uint[] { 999999 }) == QuestPrerequisiteStatus.Unknown,
        "an active quest absent from the published graph must fail closed");
    Assert(QuestPrerequisiteAuthority.DetermineFromPublishedDependencies(
               867, Array.Empty<uint>()) == QuestPrerequisiteStatus.NotActive,
        "a finite cycle with no active descendant must terminate and prove no active prerequisite");

    QuestPrerequisiteAuthority.ClearPublishedDependencyAuthority();
    Assert(QuestPrerequisiteAuthority.DetermineFromPublishedDependencies(
               867, new uint[] { 900 }) == QuestPrerequisiteStatus.Unknown,
        "an unpublished graph must fail closed");
}

static void TestPrerequisiteAuthorityHonorsTraversalBoundary()
{
    QuestPrerequisiteAuthority.PublishAuthoritativeDependencies(new[]
    {
        new QuestDependencyEvidence(2, 1, isActive: false, isAuthoritative: true)
    });
    Assert(QuestPrerequisiteAuthority.DetermineFromPublishedDependencies(
               1, new uint[] { 2 }) == QuestPrerequisiteStatus.Active,
        "a direct active dependent must remain detectable within the traversal bound");

    QuestDependencyEvidence[] exactlyBounded = Enumerable.Range(1, 4096)
        .Select(id => new QuestDependencyEvidence(
            unchecked((uint)(id + 1)), unchecked((uint)id), false, true))
        .ToArray();
    QuestPrerequisiteAuthority.PublishAuthoritativeDependencies(exactlyBounded);
    Assert(QuestPrerequisiteAuthority.DetermineFromPublishedDependencies(
               1, new uint[] { 4097 }) == QuestPrerequisiteStatus.Active,
        "a transitive active dependent at exactly 4,096 examined edges must remain authoritative");

    QuestDependencyEvidence[] beyondBound = Enumerable.Range(1, 4097)
        .Select(id => new QuestDependencyEvidence(
            unchecked((uint)(id + 1)), unchecked((uint)id), false, true))
        .ToArray();
    QuestPrerequisiteAuthority.PublishAuthoritativeDependencies(beyondBound);
    Assert(QuestPrerequisiteAuthority.DetermineFromPublishedDependencies(
               1, new uint[] { 4098 }) == QuestPrerequisiteStatus.Unknown,
        "a dependency requiring more than 4,096 examined edges must fail closed instead of guessing inactive");
    QuestPrerequisiteAuthority.ClearPublishedDependencyAuthority();
}

static void TestManagerAutomaticAbandonmentIsAtomic(string settingsRoot, DateTime now)
{
    var key = QuestRecoveryKey.ForQuestStage(1230, QuestRecoveryStage.TurnIn);
    string storePath = Path.Combine(
        settingsRoot, "QuestRecovery", "Jeof-Lordaeron", "quest-recovery.json");
    new QuestRecoveryStore(storePath).Save(new QuestRecoveryDocument
    {
        CharacterName = "Jeof",
        RealmName = "Lordaeron",
        Records = new[]
        {
            new QuestRecoveryRecord
            {
                Key = key,
                State = QuestRecoveryState.Quarantined,
                Reason = QuestFailureReason.NoObjectiveProgress,
                EpisodeCount = 3,
                AttemptGeneration = 41
            }
        }
    });
    var diagnostics = new List<string>();
    var manager = new QuestRecoveryManager(new FixedClock(now), diagnostics.Add);
    manager.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));
    manager.Report(
        QuestAttemptOutcome.Observation(key, QuestFailureReason.NoObjectiveProgress, "must be durable first"),
        Context());

    int abandonCount = 0;
    bool persistenceObservedInsideAction = false;
    QuestAbandonmentDecision allowed = manager.TryExecuteAutomaticAbandonment(
        key,
        () => new QuestAbandonmentLiveSnapshot
        {
            IsAccepted = true,
            IsCompleted = false,
            StateIsCertain = true,
            HasObjectiveProgress = false,
            PrerequisiteStatus = QuestPrerequisiteStatus.NotActive,
            FreeQuestLogSlots = 2
        },
        () =>
        {
            abandonCount++;
            persistenceObservedInsideAction = new QuestRecoveryStore(storePath).Load()
                .Records.Single(item => item.Key.Equals(key))
                .Evidence.Any(item => item.Text == "must be durable first");
        });
    Assert(allowed.MayAbandon
           && abandonCount == 1
           && persistenceObservedInsideAction,
        "automatic abandonment must persist current recovery state before invoking the action");

    QuestAbandonmentDecision actionFailure = manager.TryExecuteAutomaticAbandonment(
        key,
        SafeAbandonmentSnapshot,
        () => throw new InvalidOperationException("abandon executor boom"));
    Assert(!actionFailure.MayAbandon
           && actionFailure.Reason.Contains("abandon executor boom", StringComparison.Ordinal)
           && diagnostics.Any(message =>
               message.Contains("automatic abandonment action failed", StringComparison.OrdinalIgnoreCase)
               && message.Contains("InvalidOperationException", StringComparison.Ordinal)
               && message.Contains("abandon executor boom", StringComparison.Ordinal)),
        "an abandon executor exception must return a structured denial and emit diagnostics instead of escaping the manager");
    var failureReload = new QuestRecoveryManager(new FixedClock(now));
    failureReload.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));
    Assert(failureReload.GetRecord(key)?.AbandonmentStatus == QuestAbandonmentStatus.Failed
           && failureReload.GetRecord(key)?.AbandonmentReason.Contains(
               "abandon executor boom", StringComparison.Ordinal) == true,
        "a failed client abandonment action must persist its explicit terminal failure reason");

    int recaptureActions = 0;
    QuestAbandonmentDecision completed = manager.TryExecuteAutomaticAbandonment(
        key,
        () => new QuestAbandonmentLiveSnapshot
        {
            IsAccepted = true,
            IsCompleted = true,
            StateIsCertain = true,
            HasObjectiveProgress = false,
            PrerequisiteStatus = QuestPrerequisiteStatus.NotActive,
            FreeQuestLogSlots = 1
        },
        () => recaptureActions++);
    QuestAbandonmentDecision progressed = manager.TryExecuteAutomaticAbandonment(
        key,
        () => new QuestAbandonmentLiveSnapshot
        {
            IsAccepted = true,
            IsCompleted = false,
            StateIsCertain = true,
            HasObjectiveProgress = true,
            PrerequisiteStatus = QuestPrerequisiteStatus.NotActive,
            FreeQuestLogSlots = 1
        },
        () => recaptureActions++);
    Assert(!completed.MayAbandon
           && !progressed.MayAbandon
           && recaptureActions == 0,
        "fresh completion or objective progress must cancel an earlier safe-looking abandonment state");

    string manualRoot = Path.Combine(settingsRoot, "manual-wins");
    string manualPath = Path.Combine(
        manualRoot, "QuestRecovery", "Jeof-Lordaeron", "quest-recovery.json");
    new QuestRecoveryStore(manualPath).Save(new QuestRecoveryDocument
    {
        CharacterName = "Jeof",
        RealmName = "Lordaeron",
        Records = new[]
        {
            new QuestRecoveryRecord
            {
                Key = key,
                State = QuestRecoveryState.Quarantined,
                Reason = QuestFailureReason.NoObjectiveProgress
            }
        }
    });
    var manualManager = new QuestRecoveryManager(new FixedClock(now));
    manualManager.Configure(CreateEnvironment(manualRoot, "Jeof", "Lordaeron"));
    int manualActions = 0;
    using var manualRace = new Barrier(2);
    using var manualWon = new ManualResetEventSlim();
    Task manualWinner = Task.Run(() =>
    {
        manualRace.SignalAndWait();
        manualManager.SetManualBlacklist(key.QuestId, true);
        manualWon.Set();
    });
    Task<QuestAbandonmentDecision> manualAuthorization = Task.Run(() =>
    {
        manualRace.SignalAndWait();
        manualWon.Wait();
        return manualManager.TryExecuteAutomaticAbandonment(
            key, SafeAbandonmentSnapshot, () => manualActions++);
    });
    manualWinner.GetAwaiter().GetResult();
    QuestAbandonmentDecision manualDenied = manualAuthorization.GetAwaiter().GetResult();
    Assert(!manualDenied.MayAbandon && manualActions == 0,
        "a concurrent manual terminal that wins before authorization must suppress the abandon action");

    string completedRoot = Path.Combine(settingsRoot, "completed-wins");
    string completedPath = Path.Combine(
        completedRoot, "QuestRecovery", "Jeof-Lordaeron", "quest-recovery.json");
    new QuestRecoveryStore(completedPath).Save(new QuestRecoveryDocument
    {
        CharacterName = "Jeof",
        RealmName = "Lordaeron",
        Records = new[]
        {
            new QuestRecoveryRecord
            {
                Key = key,
                State = QuestRecoveryState.Quarantined,
                Reason = QuestFailureReason.NoObjectiveProgress
            }
        }
    });
    var completedManager = new QuestRecoveryManager(new FixedClock(now));
    completedManager.Configure(CreateEnvironment(completedRoot, "Jeof", "Lordaeron"));
    int completedActions = 0;
    using var completedRace = new Barrier(2);
    using var completedWon = new ManualResetEventSlim();
    Task completedWinner = Task.Run(() =>
    {
        completedRace.SignalAndWait();
        completedManager.MarkCompleted(key.QuestId);
        completedWon.Set();
    });
    Task<QuestAbandonmentDecision> completedAuthorization = Task.Run(() =>
    {
        completedRace.SignalAndWait();
        completedWon.Wait();
        return completedManager.TryExecuteAutomaticAbandonment(
            key, SafeAbandonmentSnapshot, () => completedActions++);
    });
    completedWinner.GetAwaiter().GetResult();
    QuestAbandonmentDecision completedDenied = completedAuthorization.GetAwaiter().GetResult();
    Assert(!completedDenied.MayAbandon && completedActions == 0,
        "a concurrent completed terminal that wins before authorization must suppress the abandon action");

    string serializedRoot = Path.Combine(settingsRoot, "serialized-race");
    string serializedPath = Path.Combine(
        serializedRoot, "QuestRecovery", "Jeof-Lordaeron", "quest-recovery.json");
    new QuestRecoveryStore(serializedPath).Save(new QuestRecoveryDocument
    {
        CharacterName = "Jeof",
        RealmName = "Lordaeron",
        Records = new[]
        {
            new QuestRecoveryRecord
            {
                Key = key,
                State = QuestRecoveryState.Quarantined,
                Reason = QuestFailureReason.NoObjectiveProgress
            }
        }
    });
    var serializedManager = new QuestRecoveryManager(new FixedClock(now));
    serializedManager.Configure(CreateEnvironment(serializedRoot, "Jeof", "Lordaeron"));
    using var captureEntered = new ManualResetEventSlim();
    using var releaseCapture = new ManualResetEventSlim();
    using var manualStarted = new ManualResetEventSlim();
    using var manualFinished = new ManualResetEventSlim();
    int serializedActions = 0;
    Task<QuestAbandonmentDecision> authorization = Task.Run(() =>
        serializedManager.TryExecuteAutomaticAbandonment(
            key,
            () =>
            {
                captureEntered.Set();
                releaseCapture.Wait();
                return SafeAbandonmentSnapshot();
            },
            () => serializedActions++));
    captureEntered.Wait();
    Task manualWaiter = Task.Run(() =>
    {
        manualStarted.Set();
        serializedManager.SetManualBlacklist(key.QuestId, true);
        manualFinished.Set();
    });
    manualStarted.Wait();
    Assert(!manualFinished.Wait(TimeSpan.FromMilliseconds(100)),
        "terminal mutation must not enter while abandonment authorization owns the manager lock");
    releaseCapture.Set();
    QuestAbandonmentDecision serialized = authorization.GetAwaiter().GetResult();
    manualWaiter.GetAwaiter().GetResult();
    Assert(serialized.MayAbandon
           && serializedActions == 1
           && serializedManager.GetRecord(key)?.State == QuestRecoveryState.ManualBlacklist,
        "authorization and terminal mutation must serialize so exactly the manager-lock winner acts first");

    string brokenRoot = Path.Combine(settingsRoot, "broken-store");
    Directory.CreateDirectory(Path.GetDirectoryName(brokenRoot)!);
    File.WriteAllText(brokenRoot, "blocks recovery persistence");
    var brokenClock = new FixedClock(now);
    var brokenManager = new QuestRecoveryManager(brokenClock);
    brokenManager.Configure(CreateEnvironment(brokenRoot, "Jeof", "Lordaeron"));
    brokenManager.Report(
        QuestAttemptOutcome.Failure(key, QuestFailureReason.NoObjectiveProgress, "failure one"),
        Context());
    brokenClock.UtcNow = now.AddMinutes(31);
    brokenManager.Report(
        QuestAttemptOutcome.Failure(key, QuestFailureReason.NoObjectiveProgress, "failure two"),
        Context());
    brokenClock.UtcNow = now.AddMinutes(92);
    brokenManager.Report(
        QuestAttemptOutcome.Failure(key, QuestFailureReason.NoObjectiveProgress, "failure three"),
        Context());
    int brokenActions = 0;
    QuestAbandonmentDecision unpersisted = brokenManager.TryExecuteAutomaticAbandonment(
        key, SafeAbandonmentSnapshot, () => brokenActions++);
    Assert(!unpersisted.MayAbandon && brokenActions == 0,
        "persistence failure must withhold the external abandon action");
}

static QuestAbandonmentLiveSnapshot SafeAbandonmentSnapshot() => new()
{
    IsAccepted = true,
    IsCompleted = false,
    StateIsCertain = true,
    HasObjectiveProgress = false,
    PrerequisiteStatus = QuestPrerequisiteStatus.NotActive,
    FreeQuestLogSlots = 2
};

static void TestHistoricalProgressBlocksAbandonmentAcrossReload(
    string settingsRoot, DateTime now)
{
    QuestRecoveryKey objective = QuestRecoveryKey.ForObjective(1231, 0);
    QuestRecoveryKey turnIn = QuestRecoveryKey.ForQuestStage(1231, QuestRecoveryStage.TurnIn);
    string storePath = Path.Combine(
        settingsRoot, "QuestRecovery", "Jeof-Lordaeron", "quest-recovery.json");
    new QuestRecoveryStore(storePath).Save(new QuestRecoveryDocument
    {
        CharacterName = "Jeof",
        RealmName = "Lordaeron",
        Records = new[]
        {
            new QuestRecoveryRecord
            {
                Key = objective,
                State = QuestRecoveryState.Eligible,
                ObjectiveCounts = new[] { 0, 4 }
            },
            new QuestRecoveryRecord
            {
                Key = turnIn,
                State = QuestRecoveryState.Quarantined,
                Reason = QuestFailureReason.NoObjectiveProgress,
                EpisodeCount = 3
            }
        }
    });

    var manager = new QuestRecoveryManager(new FixedClock(now));
    manager.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));
    int actions = 0;
    QuestAbandonmentDecision denied = manager.TryExecuteAutomaticAbandonment(
        turnIn,
        () => new QuestAbandonmentLiveSnapshot
        {
            IsAccepted = true,
            IsCompleted = false,
            StateIsCertain = true,
            HasObjectiveProgress = false,
            PrerequisiteStatus = QuestPrerequisiteStatus.NotActive,
            FreeQuestLogSlots = 2
        },
        () => actions++);

    Assert(!denied.MayAbandon
           && denied.Reason == "Automatic abandonment denied: quest has objective progress."
           && actions == 0,
        "persisted progress on another exact key must survive reload and block quest-wide abandonment when live counters return to zero");
}

static void TestProgressPersistsPerCounterMaximum(string settingsRoot, DateTime now)
{
    QuestRecoveryKey key = QuestRecoveryKey.ForObjective(1232, 0);
    var manager = new QuestRecoveryManager(new FixedClock(now));
    manager.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));
    manager.ReportProgress(key, new[] { 5, 0 }, Context());
    manager.ReportProgress(key, new[] { 0, 1 }, Context());
    manager.Flush();

    var reloaded = new QuestRecoveryManager(new FixedClock(now.AddMinutes(1)));
    reloaded.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));
    Assert(reloaded.GetRecord(key)?.ObjectiveCounts.SequenceEqual(new[] { 5, 1 }) == true,
        "progress persistence must retain the historical maximum of every objective/item counter across reload");
}

static void TestFailureContextPersistsPerCounterMaximum(string settingsRoot, DateTime now)
{
    QuestRecoveryKey key = QuestRecoveryKey.ForObjective(1233, 0);
    var manager = new QuestRecoveryManager(new FixedClock(now));
    manager.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));
    manager.ReportProgress(key, new[] { 5, 0 }, Context());
    QuestRecoveryDecision retry = manager.TryBeginAttempt(key, Context());
    Assert(retry.MayAttempt && retry.AttemptGeneration > 0,
        "failure-context history setup must acquire the exact objective lease");
    manager.Report(
        QuestAttemptOutcome.Failure(
            key,
            key,
            retry.AttemptGeneration,
            QuestFailureReason.NoObjectiveProgress,
            "consumed intermediate item"),
        Context(objectiveCounts: new[] { 0, 2 }));
    manager.Flush();

    var reloaded = new QuestRecoveryManager(new FixedClock(now.AddMinutes(1)));
    reloaded.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));
    Assert(reloaded.GetRecord(key)?.ObjectiveCounts.SequenceEqual(new[] { 5, 2 }) == true,
        "failure snapshots must retain per-counter historical maxima when live required-item counters return to zero");
}

static void TestOwnedProgressReleasesHistoricalEqualAttempt(
    string settingsRoot, DateTime now)
{
    QuestRecoveryKey objective = QuestRecoveryKey.ForObjective(1234, 1);
    QuestRecoveryKey turnIn = QuestRecoveryKey.ForQuestStage(1234, QuestRecoveryStage.TurnIn);
    string storePath = Path.Combine(
        settingsRoot, "QuestRecovery", "Jeof-Lordaeron", "quest-recovery.json");
    new QuestRecoveryStore(storePath).Save(new QuestRecoveryDocument
    {
        CharacterName = "Jeof",
        RealmName = "Lordaeron",
        Records = new[]
        {
            new QuestRecoveryRecord
            {
                Key = objective,
                State = QuestRecoveryState.Eligible,
                Reason = QuestFailureReason.RepeatedDeaths,
                EpisodeCount = 2,
                AttemptCountInEpisode = 2,
                DeathCountInEpisode = 2,
                RecoveryCycleId = 7,
                ObjectiveCounts = new[] { 0, 1 }
            },
            new QuestRecoveryRecord
            {
                Key = turnIn,
                State = QuestRecoveryState.Quarantined,
                Reason = QuestFailureReason.NoObjectiveProgress,
                EpisodeCount = 3
            }
        }
    });

    var manager = new QuestRecoveryManager(new FixedClock(now));
    manager.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));
    QuestRecoveryDecision owner = manager.TryBeginAttempt(objective, Context());
    QuestRecoveryReportResult accepted = manager.TryReportOwnedProgress(
        objective,
        owner.AttemptGeneration,
        new[] { 0, 0 },
        new[] { 0, 1 },
        "required item reacquired during this owned attempt",
        Context(objectiveCounts: new[] { 0, 1 }));

    QuestRecoveryRecord progressed = manager.GetRecord(objective)!;
    Assert(accepted.Accepted
           && progressed.State == QuestRecoveryState.Eligible
           && progressed.Reason == QuestFailureReason.None
           && progressed.EpisodeCount == 0
           && progressed.AttemptCountInEpisode == 0
           && progressed.DeathCountInEpisode == 0
           && progressed.RecoveryCycleId == 8
           && progressed.LastProgressUtc == now
           && progressed.ObjectiveCounts.SequenceEqual(new[] { 0, 1 })
           && progressed.Evidence.Any(value =>
               value.Text == "required item reacquired during this owned attempt"),
        "owned live progress equal to an all-time maximum must release ownership, reset escalation, retain history, and persist evidence");

    QuestRecoveryDecision next = manager.TryBeginAttempt(objective, Context());
    Assert(next.MayAttempt && next.AttemptGeneration > owner.AttemptGeneration,
        "accepted owned progress must release the exact lease so the next attempt can claim it");
    manager.AbandonAttempt(objective, next.AttemptGeneration);
    manager.Flush();

    var reloaded = new QuestRecoveryManager(new FixedClock(now.AddMinutes(1)));
    reloaded.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));
    int abandonActions = 0;
    QuestAbandonmentDecision abandonment = reloaded.TryExecuteAutomaticAbandonment(
        turnIn,
        SafeAbandonmentSnapshot,
        () => abandonActions++);
    Assert(!abandonment.MayAbandon
           && abandonment.Reason == "Automatic abandonment denied: quest has objective progress."
           && abandonActions == 0
           && reloaded.GetRecord(objective)?.ObjectiveCounts.SequenceEqual(new[] { 0, 1 }) == true
           && reloaded.GetRecord(objective)?.Evidence.Any(value =>
               value.Text == "required item reacquired during this owned attempt") == true,
        "accepted owned progress must survive reload and continue blocking abandonment");
}

static void TestOwnedProgressRejectsUnprovenStaleAndTerminalReports(
    string settingsRoot, DateTime now)
{
    QuestRecoveryKey key = QuestRecoveryKey.ForObjective(1235, 0);
    var manager = new QuestRecoveryManager(new FixedClock(now));
    manager.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));
    QuestRecoveryDecision ownerA = manager.TryBeginAttempt(key, Context());

    QuestRecoveryReportResult unproven = manager.TryReportOwnedProgress(
        key,
        ownerA.AttemptGeneration,
        new[] { 0, 1 },
        new[] { 0, 1 },
        "no live increase",
        Context(objectiveCounts: new[] { 0, 1 }));
    QuestRecoveryRecord afterUnproven = manager.GetRecord(key)!;
    Assert(!unproven.Accepted
           && afterUnproven.State == QuestRecoveryState.Attempting
           && afterUnproven.AttemptGeneration == ownerA.AttemptGeneration
           && afterUnproven.ObjectiveCounts.Count == 0
           && afterUnproven.Evidence.Count == 0,
        "a no-increase report must be rejected without releasing or mutating its owner");

    QuestRecoveryReportResult nonPositive = manager.TryReportOwnedProgress(
        key,
        0,
        new[] { 0, 0 },
        new[] { 1, 0 },
        "invalid zero generation",
        Context(objectiveCounts: new[] { 1, 0 }));
    QuestRecoveryRecord afterNonPositive = manager.GetRecord(key)!;
    Assert(!nonPositive.Accepted
           && afterNonPositive.State == QuestRecoveryState.Attempting
           && afterNonPositive.AttemptGeneration == ownerA.AttemptGeneration
           && afterNonPositive.ObjectiveCounts.Count == 0
           && afterNonPositive.Evidence.Count == 0,
        "a non-positive generation must reject proven counters with zero owner mutation");

    Assert(manager.AbandonAttempt(key, ownerA.AttemptGeneration),
        "stale owned-progress setup must release owner A explicitly");
    QuestRecoveryDecision ownerB = manager.TryBeginAttempt(key, Context());
    QuestRecoveryReportResult stale = manager.TryReportOwnedProgress(
        key,
        ownerA.AttemptGeneration,
        new[] { 0, 0 },
        new[] { 1, 0 },
        "delayed owner A progress",
        Context(objectiveCounts: new[] { 1, 0 }));
    QuestRecoveryRecord afterStale = manager.GetRecord(key)!;
    Assert(!stale.Accepted
           && afterStale.State == QuestRecoveryState.Attempting
           && afterStale.AttemptGeneration == ownerB.AttemptGeneration
           && afterStale.ObjectiveCounts.Count == 0
           && afterStale.Evidence.Count == 0,
        "stale owner A progress must not release or mutate newer owner B");

    manager.MarkCompleted(key.QuestId);
    QuestRecoveryRecord terminalBefore = manager.GetRecord(key)!;
    QuestRecoveryReportResult terminal = manager.TryReportOwnedProgress(
        key,
        ownerB.AttemptGeneration,
        new[] { 0 },
        new[] { 1 },
        "late progress after completion",
        Context(objectiveCounts: new[] { 1 }));
    QuestRecoveryRecord terminalAfter = manager.GetRecord(key)!;
    Assert(!terminal.Accepted
           && terminalAfter.State == QuestRecoveryState.Completed
           && terminalAfter.AttemptGeneration == terminalBefore.AttemptGeneration
           && terminalAfter.ObjectiveCounts.SequenceEqual(terminalBefore.ObjectiveCounts)
           && terminalAfter.Evidence.SequenceEqual(terminalBefore.Evidence),
        "quest-wide terminal state must reject owned progress with zero record mutation");
}

static void TestOwnedProgressPreservesConsumedItemHistory(
    string settingsRoot, DateTime now)
{
    QuestRecoveryKey key = QuestRecoveryKey.ForObjective(1236, 2);
    var manager = new QuestRecoveryManager(new FixedClock(now));
    manager.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));
    manager.ReportProgress(key, new[] { 5, 1 }, Context());
    QuestRecoveryDecision owner = manager.TryBeginAttempt(key, Context());

    QuestRecoveryReportResult accepted = manager.TryReportOwnedProgress(
        key,
        owner.AttemptGeneration,
        new[] { 1, 1 },
        new[] { 2 },
        "first counter increased while required item was consumed",
        Context(objectiveCounts: new[] { 2 }));

    Assert(accepted.Accepted
           && manager.GetRecord(key)?.State == QuestRecoveryState.Eligible
           && manager.GetRecord(key)?.ObjectiveCounts.SequenceEqual(new[] { 5, 1 }) == true,
        "a vector shrink with one proven live increase must release ownership without erasing any historical maximum");
}

static void TestQuestAbandonmentSlotMatrix()
{
    var cases = new (int FreeSlots, bool MayAbandon, string Reason)[]
    {
        (-1, false, "Automatic abandonment denied: free quest-log slot count -1 is invalid."),
        (0, true, "Automatic abandonment permitted: accepted, incomplete, certain, zero-progress quest is automatically quarantined with 0 free quest-log slots."),
        (1, true, "Automatic abandonment permitted: accepted, incomplete, certain, zero-progress quest is automatically quarantined with 1 free quest-log slots."),
        (2, true, "Automatic abandonment permitted: accepted, incomplete, certain, zero-progress quest is automatically quarantined with 2 free quest-log slots."),
        (3, false, "Automatic abandonment denied: quest log has 3 free slots; pressure requires 2 or fewer.")
    };

    foreach (var item in cases)
    {
        var decision = QuestAbandonmentPolicy.Evaluate(
            AbandonmentContext(freeQuestLogSlots: item.FreeSlots));
        Assert(decision.MayAbandon == item.MayAbandon && decision.Reason == item.Reason,
            $"the {item.FreeSlots}-free-slot boundary must return its exact abandonment decision");
    }
}

static void TestQuestAbandonmentStateMatrix()
{
    var cases = new (QuestRecoveryState State, bool MayAbandon, string Reason)[]
    {
        (QuestRecoveryState.Eligible, false,
            "Automatic abandonment denied: recovery state Eligible is not Quarantined."),
        (QuestRecoveryState.Attempting, false,
            "Automatic abandonment denied: recovery state Attempting is not Quarantined."),
        (QuestRecoveryState.CoolingDown, false,
            "Automatic abandonment denied: recovery state CoolingDown is not Quarantined."),
        (QuestRecoveryState.HalfOpen, false,
            "Automatic abandonment denied: recovery state HalfOpen is not Quarantined."),
        (QuestRecoveryState.Quarantined, true,
            "Automatic abandonment permitted: accepted, incomplete, certain, zero-progress quest is automatically quarantined with 2 free quest-log slots."),
        (QuestRecoveryState.ManualBlacklist, false,
            "Automatic abandonment denied: quest is manually blacklisted."),
        (QuestRecoveryState.Completed, false,
            "Automatic abandonment denied: recovery state Completed is not Quarantined."),
        ((QuestRecoveryState)999, false,
            "Automatic abandonment denied: recovery state 999 is unknown.")
    };

    foreach (var item in cases)
    {
        var decision = QuestAbandonmentPolicy.Evaluate(
            AbandonmentContext(recoveryState: item.State));
        Assert(decision.MayAbandon == item.MayAbandon && decision.Reason == item.Reason,
            $"recovery state {(int)item.State} must return its exact abandonment decision");
    }
}

static void TestQuestAbandonmentReasonMatrix()
{
    var automaticReasons = new[]
    {
        QuestFailureReason.PickupTargetNotOffered,
        QuestFailureReason.PickupWrongQuestShown,
        QuestFailureReason.NpcNotFoundInWorld,
        QuestFailureReason.NpcMissingFromDatabase,
        QuestFailureReason.InteractionTimedOut,
        QuestFailureReason.PathGenerationFailed,
        QuestFailureReason.EndpointUnreachable,
        QuestFailureReason.NoNavigableHotspot,
        QuestFailureReason.NoObjectiveTargetsObserved,
        QuestFailureReason.NoObjectiveProgress,
        QuestFailureReason.RepeatedDeaths,
        QuestFailureReason.TurnInTargetNotOffered,
        QuestFailureReason.UnsupportedObjective,
        QuestFailureReason.InvalidQuestData,
        QuestFailureReason.InternalBehaviorError,
        QuestFailureReason.LegacyUnknown
    };

    foreach (var reason in automaticReasons)
    {
        var decision = QuestAbandonmentPolicy.Evaluate(AbandonmentContext(reason: reason));
        Assert(decision.MayAbandon,
            $"automatic quarantine reason {reason} must remain eligible for guarded abandonment");
    }

    var denials = new (QuestFailureReason Reason, string Diagnostic)[]
    {
        (QuestFailureReason.None,
            "Automatic abandonment denied: quarantine has no automatic failure reason."),
        (QuestFailureReason.TurnInQuestIncomplete,
            "Automatic abandonment denied: TurnInQuestIncomplete is a scheduler redirect, not a quarantine failure reason."),
        (QuestFailureReason.UserExcluded,
            "Automatic abandonment denied: quest is manually blacklisted."),
        ((QuestFailureReason)999,
            "Automatic abandonment denied: quarantine failure reason 999 is unknown or unsupported.")
    };

    foreach (var item in denials)
    {
        var decision = QuestAbandonmentPolicy.Evaluate(AbandonmentContext(reason: item.Reason));
        Assert(!decision.MayAbandon && decision.Reason == item.Diagnostic,
            $"quarantine reason {(int)item.Reason} must return its exact denial diagnostic");
    }
}

static QuestAbandonmentContext AbandonmentContext(
    bool isAccepted = true,
    bool isCompleted = false,
    bool stateIsCertain = true,
    bool hasObjectiveProgress = false,
    QuestPrerequisiteStatus prerequisiteStatus = QuestPrerequisiteStatus.NotActive,
    int freeQuestLogSlots = 2,
    QuestRecoveryState recoveryState = QuestRecoveryState.Quarantined,
    QuestFailureReason reason = QuestFailureReason.NoObjectiveProgress) => new()
{
    IsAccepted = isAccepted,
    IsCompleted = isCompleted,
    StateIsCertain = stateIsCertain,
    HasObjectiveProgress = hasObjectiveProgress,
    PrerequisiteStatus = prerequisiteStatus,
    FreeQuestLogSlots = freeQuestLogSlots,
    RecoveryState = recoveryState,
    Reason = reason
};

static void TestQuestRelationParserUsesInvariantCulture()
{
    var previousCulture = CultureInfo.CurrentCulture;
    try
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        var parsed = QuestRelationParser.Parse(
            "3338,-437.2,-3192.1,91.6;3391,-483.0,-3104.0,92.0");

        Assert(parsed.Relations.Count == 2 && parsed.Diagnostics.Count == 0,
            "two invariant-culture relations must parse without diagnostics under a comma-decimal current culture");
        Assert(parsed.Relations[0].Entry == 3338
               && parsed.Relations[0].X == -437.2
               && parsed.Relations[0].Y == -3192.1
               && parsed.Relations[0].Z == 91.6,
            "the first relation must preserve its literal entry and coordinates");
        Assert(parsed.Relations[1].Entry == 3391
               && parsed.Relations[1].X == -483.0
               && parsed.Relations[1].Y == -3104.0
               && parsed.Relations[1].Z == 92.0,
            "the second relation must preserve its literal entry and coordinates");
    }
    finally
    {
        CultureInfo.CurrentCulture = previousCulture;
    }
}

static void TestQuestRelationParserRetainsValidSiblings()
{
    var parsed = QuestRelationParser.Parse(
        "3338,-437.2,-3192.1,91.6;not-a-relation;3391,-483.0,-3104.0,92.0");

    Assert(parsed.Relations.Count == 2
           && parsed.Relations[0].Entry == 3338
           && parsed.Relations[1].Entry == 3391,
        "one malformed segment must not discard either valid sibling");
    Assert(parsed.Diagnostics.Count == 1
           && parsed.Diagnostics[0] == "Segment 2 ('not-a-relation') rejected: expected entry,x,y,z.",
        "a malformed segment must produce one deterministic diagnostic");
}

static void TestQuestRelationParserRejectsZeroEntry()
{
    var parsed = QuestRelationParser.Parse("0,1,2,3;3391,-483.0,-3104.0,92.0");

    Assert(parsed.Relations.Count == 1 && parsed.Relations[0].Entry == 3391,
        "entry zero must be rejected while a valid sibling survives");
    Assert(parsed.Diagnostics.Count == 1
           && parsed.Diagnostics[0] == "Segment 1 ('0,1,2,3') rejected: entry must be a positive integer.",
        "entry zero must produce the deterministic positive-entry diagnostic");
}

static void TestQuestRelationParserRejectsNegativeAndOverflowEntries()
{
    var parsed = QuestRelationParser.Parse(
        "-1,1,2,3;4294967296,4,5,6;3391,-483.0,-3104.0,92.0");

    Assert(parsed.Relations.Count == 1 && parsed.Relations[0].Entry == 3391,
        "negative and uint-overflow entries must be rejected without discarding a valid sibling");
    Assert(parsed.Diagnostics.Count == 2
           && parsed.Diagnostics[0] == "Segment 1 ('-1,1,2,3') rejected: entry must be a positive integer."
           && parsed.Diagnostics[1] == "Segment 2 ('4294967296,4,5,6') rejected: entry must be a positive integer.",
        "negative and overflow entries must produce ordered deterministic diagnostics");
}

static void TestQuestRelationParserTrimsAndPreservesDuplicatesInOrder()
{
    var parsed = QuestRelationParser.Parse(
        " 3338 , -437.2 , -3192.1 , 91.6 ; 3338,-1,-2,-3 ; 3391,4,5,6 ");

    Assert(parsed.Diagnostics.Count == 0 && parsed.Relations.Count == 3,
        "trimmed valid segments, including duplicates, must parse without diagnostics");
    Assert(parsed.Relations[0].Entry == 3338 && parsed.Relations[0].X == -437.2
           && parsed.Relations[1].Entry == 3338 && parsed.Relations[1].X == -1
           && parsed.Relations[2].Entry == 3391 && parsed.Relations[2].X == 4,
        "relation order and duplicate entries must be preserved exactly");
}

static void TestQuestRelationParserHandlesNullAndEmptyInput()
{
    foreach (var input in new string?[] { null, "", "   " })
    {
        var parsed = QuestRelationParser.Parse(input);
        Assert(parsed.Relations.Count == 0 && parsed.Diagnostics.Count == 0,
            "null, empty, and whitespace-only relation input must return an empty result");
    }
}

static void TestQuestRelationParserRejectsNonFiniteOrMalformedCoordinates()
{
    var parsed = QuestRelationParser.Parse(
        "4000,bad,1,2;4001,NaN,1,2;4002,1,Infinity,2;4003,1,2,-Infinity");

    Assert(parsed.Relations.Count == 0,
        "invalid or non-finite coordinates must never fabricate a relation at 0,0,0");
    Assert(parsed.Diagnostics.Count == 4
           && parsed.Diagnostics[0] == "Segment 1 ('4000,bad,1,2') rejected: coordinates must be finite invariant-culture numbers."
           && parsed.Diagnostics[1] == "Segment 2 ('4001,NaN,1,2') rejected: coordinates must be finite invariant-culture numbers."
           && parsed.Diagnostics[2] == "Segment 3 ('4002,1,Infinity,2') rejected: coordinates must be finite invariant-culture numbers."
           && parsed.Diagnostics[3] == "Segment 4 ('4003,1,2,-Infinity') rejected: coordinates must be finite invariant-culture numbers.",
        "coordinate rejections must preserve input order and deterministic diagnostics");
}

static void TestEquipmentFingerprintPreservesSlotOrderAndDurabilityClass()
{
    var slotOrdered = QuestRecoveryRuntime.CreateEquipmentFingerprint(new[]
    {
        (Entry: 500u, MaxDurability: 100.0, DurabilityPercent: 19.9),
        (Entry: 100u, MaxDurability: 100.0, DurabilityPercent: 87.0),
        (Entry: 300u, MaxDurability: 0.0, DurabilityPercent: 0.0)
    });
    var sameDurabilityClasses = QuestRecoveryRuntime.CreateEquipmentFingerprint(new[]
    {
        (Entry: 500u, MaxDurability: 100.0, DurabilityPercent: 1.0),
        (Entry: 100u, MaxDurability: 100.0, DurabilityPercent: 20.0),
        (Entry: 300u, MaxDurability: 0.0, DurabilityPercent: 99.0)
    });
    var differentSlotOrder = QuestRecoveryRuntime.CreateEquipmentFingerprint(new[]
    {
        (Entry: 100u, MaxDurability: 100.0, DurabilityPercent: 87.0),
        (Entry: 500u, MaxDurability: 100.0, DurabilityPercent: 19.9),
        (Entry: 300u, MaxDurability: 0.0, DurabilityPercent: 0.0)
    });

    Assert(slotOrdered == "500:critical|100:healthy|300:healthy",
        "equipment fingerprints must preserve the equipped-slot enumeration order instead of sorting item entries");
    Assert(slotOrdered == sameDurabilityClasses,
        "equipment fingerprints must encode only whether repairable durability is below the critical threshold");
    Assert(differentSlotOrder == "100:healthy|500:critical|300:healthy"
           && differentSlotOrder != slotOrdered,
        "changing equipment slot order must change the equipment fingerprint even when the same items remain equipped");
}

static void TestQuestCompletionAuthorityIsTriState()
{
    var acceptedSnapshot = QuestLog.ResolveQuestCompletionSnapshot(
        accepted: true, acceptedCompleted: false, cacheValid: false, cachedCompleted: true);
    Assert(acceptedSnapshot.IsAccepted
           && acceptedSnapshot.State == QuestCompletionState.KnownIncomplete,
        "one accepted-quest snapshot must provide both acceptance and live incomplete authority without consulting failed cache data");

    Assert(QuestLog.ResolveQuestCompletionState(
               accepted: true, acceptedCompleted: true, cacheValid: false, cachedCompleted: false)
           == QuestCompletionState.KnownComplete,
        "an accepted completed quest must be authoritative even when the historical cache failed");
    Assert(QuestLog.ResolveQuestCompletionState(
               accepted: true, acceptedCompleted: false, cacheValid: false, cachedCompleted: true)
           == QuestCompletionState.KnownIncomplete,
        "an accepted incomplete quest must outrank historical completion data");
    Assert(QuestLog.ResolveQuestCompletionState(
               accepted: false, acceptedCompleted: false, cacheValid: true, cachedCompleted: true)
           == QuestCompletionState.KnownComplete,
        "a valid current-identity cache may prove a non-accepted quest complete");
    Assert(QuestLog.ResolveQuestCompletionState(
               accepted: false, acceptedCompleted: false, cacheValid: true, cachedCompleted: false)
           == QuestCompletionState.KnownIncomplete,
        "a valid current-identity cache may prove a non-accepted quest incomplete");
    Assert(QuestLog.ResolveQuestCompletionState(
               accepted: false, acceptedCompleted: false, cacheValid: false, cachedCompleted: false)
           == QuestCompletionState.Unknown,
        "a non-accepted quest with no valid current-identity cache must remain unknown");
}

static void TestProfileCompletionExpressionsPreserveUnknown()
{
    Assert(EvaluateProfileCondition("IsQuestCompleted(867)", QuestCompletionState.Unknown)
           == QuestConditionEvaluationState.Unknown,
        "a positive IsQuestCompleted profile expression must remain unknown");
    Assert(EvaluateProfileCondition("!IsQuestCompleted(867)", QuestCompletionState.Unknown)
           == QuestConditionEvaluationState.Unknown,
        "negation must not collapse unknown completion into true");
    Assert(EvaluateProfileCondition("(!IsQuestCompleted(867)) && (1 == 1)", QuestCompletionState.Unknown)
           == QuestConditionEvaluationState.Unknown,
        "a Roslyn combined expression must preserve unknown completion");

    Assert(EvaluateProfileCondition("IsQuestCompleted(867)", QuestCompletionState.KnownComplete)
           == QuestConditionEvaluationState.True,
        "known-complete must satisfy a positive completion expression");
    Assert(EvaluateProfileCondition("!IsQuestCompleted(867)", QuestCompletionState.KnownComplete)
           == QuestConditionEvaluationState.False,
        "known-complete must fail a negative completion expression");
    Assert(EvaluateProfileCondition("(!IsQuestCompleted(867)) && (1 == 1)", QuestCompletionState.KnownIncomplete)
           == QuestConditionEvaluationState.True,
        "known-incomplete must satisfy a combined negative completion expression");
}

static void TestCompileBatchCompletionExpressionsPreserveUnknown()
{
    var batch = new CompileBatch();
    var expression = DelayCompiledExpression.Condition("!IsQuestCompleted(867)");
    batch.AddExpression(expression);
    Assert(batch.Compile(), "the completion expression must compile through CompileBatch");

    Assert(QuestConditionEvaluation.Evaluate(
               expression.CallableExpression,
               _ => QuestCompletionState.Unknown)
           == QuestConditionEvaluationState.Unknown,
        "CompileBatch negation must not turn unknown completion into true");
    Assert(QuestConditionEvaluation.Evaluate(
               expression.CallableExpression,
               _ => QuestCompletionState.KnownIncomplete)
           == QuestConditionEvaluationState.True,
        "CompileBatch must retain known-incomplete behavior");
}

static void TestCompletionEvaluationScopesNestAndRecoverFromExceptions()
{
    var nested = QuestConditionEvaluation.Evaluate(
        () =>
        {
            Assert(QuestConditionEvaluation.Evaluate(
                       () => ProfileHelperFunctions.IsQuestCompleted(867),
                       _ => QuestCompletionState.Unknown)
                   == QuestConditionEvaluationState.Unknown,
                "the nested evaluation must observe unknown completion");
            return true;
        },
        _ => QuestCompletionState.KnownComplete);
    Assert(nested == QuestConditionEvaluationState.Unknown,
        "nested unknown completion must propagate to the enclosing condition evaluation");

    AssertThrows<InvalidOperationException>(() => QuestConditionEvaluation.Evaluate(
            () => throw new InvalidOperationException("scope probe"),
            _ => QuestCompletionState.Unknown),
        "condition exceptions must propagate after evaluation cleanup");
    Assert(QuestConditionEvaluation.Evaluate(() => true) == QuestConditionEvaluationState.True,
        "an exception must not leak unknown state into the next evaluation");
}

static void TestCompletionDependentActionGatesDeferUnknown()
{
    Assert(QuestNodeCompletionPolicy.ForPickup(QuestCompletionState.Unknown, accepted: false)
           == QuestNodeCompletionAction.Defer,
        "pickup must defer when neither live acceptance nor completion authority is available");
    Assert(QuestNodeCompletionPolicy.ForTurnIn(QuestCompletionState.Unknown, accepted: false)
           == QuestNodeCompletionAction.Defer,
        "turn-in nodes must defer rather than skip when completion is unknown");
    Assert(QuestNodeCompletionPolicy.ForObjective(QuestCompletionState.Unknown, accepted: false)
           == QuestNodeCompletionAction.Defer,
        "objective nodes must defer rather than skip when completion is unknown");
    Assert(QuestNodeCompletionPolicy.ForObjective(QuestCompletionState.Unknown, accepted: true)
           == QuestNodeCompletionAction.Execute,
        "an accepted objective must execute from live quest-log authority when the historical completion cache is unavailable");
    Assert(QuestNodeCompletionPolicy.ForPickup(QuestCompletionState.KnownComplete, accepted: false)
           == QuestNodeCompletionAction.Skip,
        "known-complete pickup work may be skipped");
    Assert(QuestNodeCompletionPolicy.ForPickup(QuestCompletionState.KnownIncomplete, accepted: false)
           == QuestNodeCompletionAction.Execute,
        "known-incomplete pickup work may execute");
    Assert(QuestNodeCompletionPolicy.ForTurnIn(QuestCompletionState.KnownIncomplete, accepted: false)
           == QuestNodeCompletionAction.Skip,
        "known-incomplete unaccepted turn-in work may be skipped");
    Assert(QuestNodeCompletionPolicy.ForObjective(QuestCompletionState.KnownIncomplete, accepted: false)
           == QuestNodeCompletionAction.Skip,
        "known-incomplete unaccepted objective work may be skipped");
    Assert(QuestNodeCompletionPolicy.ForTurnIn(QuestCompletionState.KnownIncomplete, accepted: true)
           == QuestNodeCompletionAction.Execute,
        "accepted turn-in work must execute using live quest-log authority");
    Assert(QuestNodeCompletionPolicy.ForObjective(QuestCompletionState.KnownComplete, accepted: true)
           == QuestNodeCompletionAction.Skip,
        "an accepted completed quest must skip objective work but remain available for turn-in");
}

static void TestProfileManagerPublishesSameMetadataReplacement()
{
    var managerType = typeof(Styx.Logic.Profiles.ProfileManager);
    var currentField = managerType.GetField(
        "_currentProfile",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
    var setter = managerType.GetProperty(
        "CurrentProfile",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)!
        .GetSetMethod(nonPublic: true)!;
    object? original = currentField.GetValue(null);
    var first = new Styx.Logic.Profiles.Profile { Name = "Same profile", MinLevel = 1, MaxLevel = 80 };
    var replacement = new Styx.Logic.Profiles.Profile { Name = "Same profile", MinLevel = 1, MaxLevel = 80 };
    var replacementEvents = 0;

    void OnLoaded(Styx.BotEvents.Profile.NewProfileLoadedEventArgs args)
    {
        if (ReferenceEquals(args.OldProfile, first) && ReferenceEquals(args.NewProfile, replacement))
            replacementEvents++;
    }

    Styx.BotEvents.Profile.OnNewProfileLoaded += OnLoaded;
    try
    {
        setter.Invoke(null, new object?[] { first });
        setter.Invoke(null, new object?[] { replacement });
    }
    finally
    {
        Styx.BotEvents.Profile.OnNewProfileLoaded -= OnLoaded;
        currentField.SetValue(null, original);
    }

    Assert(replacementEvents == 1,
        "a newly parsed profile must publish a reload event even when its name and level range match the prior profile");
}

static void TestForcedConditionBehaviorsDeferUnknown()
{
    var positive = ConditionHelper.ParseConditionString("IsQuestCompleted(867)");
    var negative = ConditionHelper.ParseConditionString("!IsQuestCompleted(867)");
    Assert(positive != null && negative != null,
        "forced behavior completion fixtures must compile");

    var positiveIf = new ForcedIf(new IfNode(
        positive!,
        Array.Empty<OrderNode>(),
        new Else(Array.Empty<OrderNode>())));
    positiveIf.OnStart();
    Assert(!positiveIf.IsDone,
        "ForcedIf must remain pending instead of scheduling its else body for unknown positive completion");
    var ifContext = new object();
    positiveIf.Branch.Start(ifContext);
    Assert(positiveIf.Branch.Tick(ifContext) == TreeSharp.RunStatus.Running &&
           positiveIf.Branch.Tick(ifContext) == TreeSharp.RunStatus.Running,
        "ForcedIf must remain a valid running iterator across repeated unknown-completion ticks");
    positiveIf.Branch.Stop(ifContext);

    var negativeIf = new ForcedIf(new IfNode(
        negative!,
        Array.Empty<OrderNode>(),
        new Else(Array.Empty<OrderNode>())));
    negativeIf.OnStart();
    Assert(!negativeIf.IsDone,
        "ForcedIf must remain pending instead of scheduling either branch for unknown negated completion");

    var grind = new ForcedGrindTo(new GrindToNode(-1f, negative!));
    grind.OnStart();
    Assert(grind.IsExecutionDeferred && !grind.IsDone,
        "ForcedGrindTo must defer initialization and keep the node running under unknown completion");

    var loop = new ForcedWhile(new WhileNode(negative!, Array.Empty<OrderNode>()));
    var context = new object();
    loop.Branch.Start(context);
    Assert(loop.Branch.Tick(context) == TreeSharp.RunStatus.Running &&
           loop.Branch.Tick(context) == TreeSharp.RunStatus.Running && !loop.IsDone,
        "ForcedWhile must keep a valid running iterator without scheduling or completing its body while completion is unknown");
    loop.Branch.Stop(context);

    var deferredOrder = new QuestOrder(new OrderNodeCollection(new OrderNode[]
    {
        new GrindToNode(-1f, negative!)
    }));
    var executor = new ForcedBehaviorExecutor(deferredOrder);
    executor.Start(context);
    Assert(executor.Tick(context) == TreeSharp.RunStatus.Running &&
           executor.Tick(context) == TreeSharp.RunStatus.Running,
        "ForcedBehaviorExecutor must remain a valid running iterator while its behavior is deferred");
    executor.Stop(context);
}

static QuestConditionEvaluationState EvaluateProfileCondition(
    string expression,
    QuestCompletionState completionState)
{
    var condition = ConditionHelper.ParseConditionString(expression);
    Assert(condition != null, $"profile condition '{expression}' must compile");
    return QuestConditionEvaluation.Evaluate(condition!, _ => completionState);
}

static void TestCompletedQuestTraversalStopsAtInvalidPointers()
{
    var nullReaderCalls = 0;
    Assert(QuestLog.TryTraverseCompletedQuestNodes(0, address =>
    {
        nullReaderCalls++;
        return (0U, 0U);
    }, out var nullIds), "a null completed-quest head must be a valid empty traversal");
    Assert(nullReaderCalls == 0 && nullIds.Count == 0,
        "a null completed-quest head must not be read");

    var oddReaderCalls = 0;
    Assert(!QuestLog.TryTraverseCompletedQuestNodes(0x1001, address =>
    {
        oddReaderCalls++;
        return (0U, 0U);
    }, out _), "an odd completed-quest address must fail traversal");
    Assert(oddReaderCalls == 0, "an odd completed-quest address must not be read");

    var unalignedReaderCalls = 0;
    Assert(!QuestLog.TryTraverseCompletedQuestNodes(0x1002, address =>
    {
        unalignedReaderCalls++;
        return (0U, 0U);
    }, out _), "a non-DWORD-aligned completed-quest address must fail traversal");
    Assert(unalignedReaderCalls == 0,
        "a non-DWORD-aligned completed-quest address must not be read in the 32-bit client model");
}

static void TestCompletedQuestTraversalStopsAtRepeatedPointer()
{
    var readerCalls = 0;
    Assert(!QuestLog.TryTraverseCompletedQuestNodes(0x1000, address =>
    {
        readerCalls++;
        return address == 0x1000 ? (0x1004U, 867U) : (0x1000U, 875U);
    }, out var ids), "a repeated completed-quest node must fail traversal");
    Assert(readerCalls == 2, "a repeated completed-quest node must stop before rereading the cycle");
    Assert(ids.SequenceEqual(new uint[] { 867, 875 }),
        "nodes read before a repeated pointer must retain their quest IDs");
}

static void TestCompletedQuestTraversalStopsAtReaderFailure()
{
    var readerCalls = 0;
    Assert(!QuestLog.TryTraverseCompletedQuestNodes(0x1000, address =>
    {
        readerCalls++;
        throw new InvalidOperationException("simulated memory read failure");
    }, out var ids), "a completed-quest reader failure must fail traversal");
    Assert(readerCalls == 1 && ids.Count == 0,
        "a completed-quest reader failure must stop without retrying the same node");
}

static void TestCompletedQuestTraversalCapsAtTenThousandNodes()
{
    var readerCalls = 0;
    Assert(QuestLog.TryTraverseCompletedQuestNodes(0x1000, address =>
    {
        readerCalls++;
        return readerCalls == 10000 ? (0U, (uint)readerCalls) : (address + 4U, (uint)readerCalls);
    }, out var cappedIds), "a completed-quest traversal ending at ten thousand nodes must succeed");
    Assert(readerCalls == 10000 && cappedIds.Count == 10000,
        "a completed-quest traversal must read no more than ten thousand nodes");

    readerCalls = 0;
    Assert(!QuestLog.TryTraverseCompletedQuestNodes(0x1000, address =>
    {
        readerCalls++;
        return (address + 4U, (uint)readerCalls);
    }, out _), "a completed-quest traversal exceeding ten thousand nodes must fail");
    Assert(readerCalls == 10000,
        "a completed-quest traversal exceeding the bound must stop after ten thousand reads");
}

static void TestCompletedQuestTraversalDeduplicatesQuestIds()
{
    Assert(QuestLog.TryTraverseCompletedQuestNodes(0x1000, address => address switch
    {
        0x1000 => (0x1004U, 867U),
        0x1004 => (0x1008U, 867U),
        0x1008 => (0U, 875U),
        _ => throw new InvalidOperationException("unexpected completed-quest node")
    }, out var ids), "a finite completed-quest traversal must succeed");
    Assert(ids.SequenceEqual(new uint[] { 867, 875 }),
        "a completed-quest traversal must return each non-zero quest ID once in encounter order");
}

static void TestCompletedQuestLuaChunksParseDeterministically()
{
    Assert(QuestLog.TryParseCompletedQuestIdChunks(
            new[] { "1,2,2,", "4921,99999," },
            out var parsed)
           && parsed.SequenceEqual(new uint[] { 1, 2, 4921, 99999 }),
        "Lua completed-quest chunks must parse, deduplicate, and preserve deterministic numeric order");
    Assert(QuestLog.TryParseCompletedQuestIdChunks(new[] { "" }, out var empty)
           && empty.Count == 0,
        "an authoritative empty Lua completed-quest table must remain distinguishable from a failed Lua call");
    Assert(!QuestLog.TryParseCompletedQuestIdChunks(new[] { "1,not-a-quest," }, out _),
        "malformed Lua completed-quest output must not become authoritative");
}

static void TestCompletedQuestCacheInvalidatesAcrossIdentityChanges()
{
    Assert(QuestLog.TryGetAuthoritativeCompletedQuestsForIdentity(
        "Alpha", "Lordaeron", () => new List<uint> { 867 }, out var alphaIds),
        "a successful completed-quest refresh must be authoritative for its identity");
    Assert(alphaIds.SequenceEqual(new uint[] { 867 }),
        "a successful completed-quest refresh must return its identity's IDs");

    Assert(!QuestLog.TryGetAuthoritativeCompletedQuestsForIdentity(
        "Bravo", "Lordaeron", () => null, out var bravoIds),
        "a refresh failure for a new identity must not be authoritative");
    Assert(bravoIds.Count == 0,
        "a refresh failure for a new identity must not expose the previous identity's IDs");
    Assert(QuestLog.GetCompletedQuestCacheStatusForIdentity("Bravo", "Lordaeron")
        == CompletedQuestCacheStatus.RefreshFailed,
        "a failed refresh must expose RefreshFailed for the current identity");
    Assert(QuestLog.GetCompletedQuestCacheStatusForIdentity(null, "Lordaeron")
        == CompletedQuestCacheStatus.Unknown,
        "an unusable identity must clear completed-quest authority");
}

static void TestCompletedQuestCacheSnapshotsAreStable()
{
    Assert(QuestLog.TryGetAuthoritativeCompletedQuestsForIdentity(
        "Charlie", "Lordaeron", () => new List<uint> { 875 }, out var firstSnapshot),
        "a successful completed-quest refresh must provide a snapshot");
    Assert(QuestLog.TryGetAuthoritativeCompletedQuestsForIdentity(
        "Delta", "Lordaeron", () => new List<uint> { 876 }, out var secondSnapshot),
        "a later identity's successful completed-quest refresh must be authoritative");
    Assert(firstSnapshot.SequenceEqual(new uint[] { 875 }),
        "a completed-quest snapshot must not change when the shared cache is replaced");
    Assert(secondSnapshot.SequenceEqual(new uint[] { 876 }),
        "a later completed-quest snapshot must contain only its own identity's IDs");
}

static void TestCompletedQuestCacheSerializesConcurrentRefreshes()
{
    var refreshCalls = 0;
    var start = new ManualResetEventSlim(false);
    var ready = new CountdownEvent(2);

    Task<bool> first = Task.Run(() =>
    {
        ready.Signal();
        start.Wait();
        return QuestLog.TryGetAuthoritativeCompletedQuestsForIdentity(
            "Echo", "Lordaeron", () =>
            {
                Interlocked.Increment(ref refreshCalls);
                Thread.Sleep(100);
                return new List<uint> { 867 };
            }, out _);
    });
    Task<bool> second = Task.Run(() =>
    {
        ready.Signal();
        start.Wait();
        return QuestLog.TryGetAuthoritativeCompletedQuestsForIdentity(
            "Echo", "Lordaeron", () =>
            {
                Interlocked.Increment(ref refreshCalls);
                return new List<uint> { 875 };
            }, out _);
    });

    Assert(ready.Wait(TimeSpan.FromSeconds(5)), "concurrent completed-quest readers must start together");
    start.Set();
    Assert(Task.WaitAll(new Task[] { first, second }, TimeSpan.FromSeconds(5)),
        "concurrent completed-quest readers must complete without deadlock");
    Assert(first.Result && second.Result && refreshCalls == 1,
        "concurrent completed-quest readers must share one refresh attempt");
}

static void TestCompletedQuestCacheDiscardsRefreshWhenIdentityChanges()
{
    var identityReads = 0;
    Assert(!QuestLog.TryGetAuthoritativeCompletedQuestsForIdentityProvider(
        () => ++identityReads == 1 ? ("Foxtrot", "Lordaeron") : ("Golf", "Lordaeron"),
        () => new List<uint> { 867 },
        out var completedQuestIds),
        "a refresh that crosses an identity change must not be authoritative");
    Assert(completedQuestIds.Count == 0,
        "a refresh that crosses an identity change must discard the candidate IDs");
    Assert(QuestLog.GetCompletedQuestCacheStatusForIdentity("Golf", "Lordaeron")
        == CompletedQuestCacheStatus.Unknown,
        "an identity change during refresh must leave the new identity unrefreshed");
}

static void TestQuestOrderDoesNotMutateWithoutAuthoritativeCompletion()
{
    var nodes = new OrderNodeCollection
    {
        new CheckpointNode(1),
        new CheckpointNode(2)
    };
    var order = new QuestOrder(nodes);

    Assert(!order.TryUpdateNodesWithAuthoritativeCompletedQuests(false, new HashSet<uint>(), 2),
        "quest order must reject an unavailable completed-quest authority");
    Assert(nodes.Count == 2 && nodes[0] is CheckpointNode && nodes[1] is CheckpointNode,
        "quest order must not advance checkpoints when completion authority is unavailable");
}

static void TestQuestingCompletedQuestIdsAreSafeWithoutClient()
{
#pragma warning disable CS0618
    var completedQuestIds = Questing.GetCompletedQuestIDs();
#pragma warning restore CS0618
    Assert(completedQuestIds != null && completedQuestIds.Count == 0,
        "the legacy completed-quest API must return an empty copied set without a client authority");
}

static void TestQuestManagerObsoleteGuidanceShowsCompilableTryCall()
{
    var method = typeof(Bots.Quest.QuestManager).GetMethod(nameof(Bots.Quest.QuestManager.GetCompletedQuests));
    var obsolete = method == null
        ? null
        : Attribute.GetCustomAttribute(method, typeof(ObsoleteAttribute)) as ObsoleteAttribute;

    Assert(obsolete?.Message == "Use ObjectManager.Me.QuestLog.TryGetAuthoritativeCompletedQuests(out var ids) instead.",
        "the QuestManager obsolete guidance must show the Try API's required out argument");
}

static void RunPolicyRegressions(DateTime now)
{
    var defaultContext = new QuestRecoveryContext();
    var key = QuestRecoveryKey.ForQuestStage(867, QuestRecoveryStage.Pickup);
    var record = QuestRecoveryRecord.Create(key);

    var first = QuestRecoveryPolicy.ApplyFailure(record, QuestFailureReason.PickupTargetNotOffered, defaultContext, now);
    Assert(first.State == QuestRecoveryState.CoolingDown, "first pickup failure must cool down");
    Assert(first.CooldownUntilUtc == now.AddMinutes(15), "pickup cooldown must be 15 minutes");

    var observation = QuestRecoveryPolicy.ApplyFailure(first, QuestFailureReason.PickupTargetNotOffered, defaultContext, now.AddMinutes(1));
    Assert(observation.EpisodeCount == 1, "repeated observations must retain the same episode");

    var second = QuestRecoveryPolicy.ApplyFailure(first, QuestFailureReason.PickupTargetNotOffered, defaultContext, now.AddMinutes(16));
    Assert(second.CooldownUntilUtc == now.AddMinutes(76), "second episode must cool for 60 minutes");

    var third = QuestRecoveryPolicy.ApplyFailure(second, QuestFailureReason.PickupTargetNotOffered, defaultContext, now.AddMinutes(77));
    Assert(third.State == QuestRecoveryState.Quarantined, "third episode must quarantine");
    Assert(third.NextHalfOpenUtc == now.AddMinutes(77).AddHours(6), "quarantine probe must wait six hours");

    var failedProbe = QuestRecoveryPolicy.ApplyFailure(
        third,
        QuestFailureReason.PickupTargetNotOffered,
        defaultContext,
        third.NextHalfOpenUtc!.Value);
    Assert(failedProbe.State == QuestRecoveryState.Quarantined, "failed half-open probe must return to quarantine");
    Assert(failedProbe.NextHalfOpenUtc == third.NextHalfOpenUtc!.Value.AddHours(6), "failed half-open probe must wait another six hours");

    var objectiveKey = QuestRecoveryKey.ForObjective(867, 0);
    var objectiveFailure = QuestRecoveryPolicy.ApplyFailure(
        QuestRecoveryRecord.Create(objectiveKey),
        QuestFailureReason.RepeatedDeaths,
        defaultContext,
        now);
    var progressed = QuestRecoveryPolicy.ApplyProgress(objectiveFailure, new[] { 1, 0 }, now.AddMinutes(1));
    Assert(progressed.State == QuestRecoveryState.Eligible, "objective progress must reopen objective work");
    Assert(progressed.EpisodeCount == 0 && progressed.DeathCountInEpisode == 0, "objective progress must clear objective and death escalation");
    Assert(progressed.ObjectiveCounts.SequenceEqual(new[] { 1, 0 }), "objective counts must be retained after progress");

    var noProgress = QuestRecoveryPolicy.ApplyProgress(progressed, new[] { 0, 0 }, now.AddMinutes(2));
    Assert(noProgress.LastProgressUtc == progressed.LastProgressUtc,
        "unchanged objective counts must not update the progress timestamp");
    Assert(noProgress.ObjectiveCounts.SequenceEqual(progressed.ObjectiveCounts),
        "unchanged objective counts must not replace stored progress");

    var manualObjective = new QuestRecoveryRecord { Key = objectiveKey, State = QuestRecoveryState.ManualBlacklist };
    var completedObjective = new QuestRecoveryRecord { Key = objectiveKey, State = QuestRecoveryState.Completed };
    Assert(QuestRecoveryPolicy.ApplyProgress(manualObjective, new[] { 1 }, now).State == QuestRecoveryState.ManualBlacklist,
        "objective progress must not reopen a manual blacklist");
    Assert(QuestRecoveryPolicy.ApplyProgress(completedObjective, new[] { 1 }, now).State == QuestRecoveryState.Completed,
        "objective progress must not reopen a completed record");

    AssertImmediateDataQuarantine(QuestFailureReason.InvalidQuestData, now);
    AssertImmediateDataQuarantine(QuestFailureReason.UnsupportedObjective, now.AddDays(1));

    var failedEndpoint = QuestRecoveryPolicy.ApplyFailure(
        QuestRecoveryRecord.Create(QuestRecoveryKey.ForEndpoint(867, QuestRecoveryStage.Navigation, 1, "10,20")),
        QuestFailureReason.PathGenerationFailed,
        defaultContext,
        now);
    var alternativeEndpoint = QuestRecoveryKey.ForEndpoint(867, QuestRecoveryStage.Navigation, 1, "30,40");
    var matchingEndpoint = QuestRecoveryKey.ForEndpoint(867, QuestRecoveryStage.Navigation, 1, "10,20");
    Assert(QuestRecoveryPolicy.Evaluate(QuestRecoveryRecord.Create(alternativeEndpoint), new QuestRecoveryContext(), 0, now).MayAttempt,
        "endpoint failure must leave another endpoint eligible");
    Assert(failedEndpoint.Key.Equals(matchingEndpoint), "equal endpoint keys must compare equal by value");
    Assert(matchingEndpoint.Equals(failedEndpoint.Key), "endpoint key equality must be symmetric");
    Assert(!failedEndpoint.Key.Equals(alternativeEndpoint), "distinct endpoints must compare unequal by value");
    Assert(failedEndpoint.CooldownUntilUtc == now.AddMinutes(10), "endpoint path generation failure must cool only that endpoint for ten minutes");

    var navigationStageFailure = QuestRecoveryPolicy.ApplyFailure(
        QuestRecoveryRecord.Create(QuestRecoveryKey.ForQuestStage(867, QuestRecoveryStage.Navigation)),
        QuestFailureReason.PathGenerationFailed,
        defaultContext,
        now);
    Assert(navigationStageFailure.CooldownUntilUtc == now.AddMinutes(30),
        "quest-stage navigation failure must use the thirty-minute cooldown");

    var manual = new QuestRecoveryRecord
    {
        Key = key,
        State = QuestRecoveryState.ManualBlacklist,
        DatasetVersion = "old-data",
        CoreVersion = "old-core",
        NavigationFingerprint = "old-nav"
    };
    var changedContext = new QuestRecoveryContext
    {
        DatasetVersion = "new-data",
        CoreVersion = "new-core",
        NavigationFingerprint = "new-nav",
        PlayerLevel = 80,
        EquipmentFingerprint = "new-gear"
    };
    Assert(!QuestRecoveryPolicy.Evaluate(manual, changedContext, 0, now).MayAttempt, "manual blacklist must not reopen");

    var futureQuarantine = new QuestRecoveryRecord
    {
        Key = key,
        State = QuestRecoveryState.Quarantined,
        Reason = QuestFailureReason.PickupTargetNotOffered,
        NextHalfOpenUtc = now.AddHours(1),
        DatasetVersion = "old-data",
        CoreVersion = "old-core",
        NavigationFingerprint = "old-nav"
    };
    Assert(!QuestRecoveryPolicy.Evaluate(futureQuarantine, changedContext, 0, now).MayAttempt,
        "irrelevant context changes must not reopen pickup quarantine");

    var dueQuarantine = new QuestRecoveryRecord
    {
        Key = key,
        State = QuestRecoveryState.Quarantined,
        Reason = QuestFailureReason.PickupTargetNotOffered,
        NextHalfOpenUtc = now
    };
    Assert(QuestRecoveryPolicy.Evaluate(dueQuarantine, new QuestRecoveryContext(), 5, now).MayAttempt,
        "a due half-open probe must be allowed below the rolling-hour budget");
    Assert(!QuestRecoveryPolicy.Evaluate(dueQuarantine, new QuestRecoveryContext(), 6, now).MayAttempt,
        "only six failed episodes fit the rolling-hour budget");

    var redirect = QuestRecoveryPolicy.ApplyFailure(
        QuestRecoveryRecord.Create(QuestRecoveryKey.ForQuestStage(867, QuestRecoveryStage.TurnIn)),
        QuestFailureReason.TurnInQuestIncomplete,
        defaultContext,
        now);
    Assert(redirect.EpisodeCount == 0 && redirect.State == QuestRecoveryState.Eligible,
        "incomplete turn-in must redirect without a failure episode");
}

static void TestStoreRoundTripAndAtomicReplacement(string root, DateTime now)
{
    Directory.CreateDirectory(root);
    var path = Path.Combine(root, "quest-recovery.json");
    var key = QuestRecoveryKey.ForEndpoint(867, QuestRecoveryStage.Navigation, 1, "10,20");
    var document = new QuestRecoveryDocument
    {
        SchemaVersion = 1,
        CharacterName = "Jeof",
        RealmName = "Lordaeron",
        RollingFailureUtc = new[] { now.AddMinutes(-30), now },
        Records = new[]
        {
            new QuestRecoveryRecord
            {
                Key = key,
                State = QuestRecoveryState.Quarantined,
                Reason = QuestFailureReason.EndpointUnreachable,
                FirstFailureUtc = now.AddHours(-2),
                LastFailureUtc = now,
                CooldownUntilUtc = now.AddMinutes(15),
                NextHalfOpenUtc = now.AddHours(6),
                EpisodeCount = 3,
                AttemptCountInEpisode = 2,
                DeathCountInEpisode = 1,
                LastProgressUtc = now.AddDays(-1),
                ObjectiveCounts = new[] { 2, 3 },
                PlayerLevelAtFailure = 35,
                EquipmentFingerprint = "100:healthy|200:critical",
                DatasetVersion = "quest-data-v1",
                CoreVersion = "core-v1",
                NavigationFingerprint = "nav-v1",
                RecoveryCycleId = 7,
                Evidence = new[]
                {
                    new QuestRecoveryEvidence
                    {
                        ObservedUtc = now,
                        Reason = QuestFailureReason.EndpointUnreachable,
                        Text = "unreachable",
                        EpisodeCount = 3,
                        RecoveryCycleId = 7,
                        SourceKey = key
                    }
                }
            }
        }
    };

    var store = new QuestRecoveryStore(path);
    store.Save(document);
    store.Save(document);
    var loaded = store.Load();
    var loadedRecord = loaded.Records.Single();

    Assert(loaded.SchemaVersion == 1 && loaded.CharacterName == "Jeof" && loaded.RealmName == "Lordaeron",
        "store must round-trip document identity and schema");
    Assert(loaded.RollingFailureUtc.SequenceEqual(document.RollingFailureUtc), "store must round-trip rolling failures");
    Assert(loadedRecord.Key.Equals(key), "store must round-trip the complete recovery key");
    Assert(loadedRecord.State == QuestRecoveryState.Quarantined && loadedRecord.Reason == QuestFailureReason.EndpointUnreachable,
        "store must round-trip state and reason");
    Assert(loadedRecord.FirstFailureUtc == now.AddHours(-2) && loadedRecord.LastFailureUtc == now,
        "store must round-trip failure timestamps");
    Assert(loadedRecord.CooldownUntilUtc == now.AddMinutes(15) && loadedRecord.NextHalfOpenUtc == now.AddHours(6),
        "store must round-trip retry timestamps");
    Assert(loadedRecord.EpisodeCount == 3 && loadedRecord.AttemptCountInEpisode == 2 && loadedRecord.DeathCountInEpisode == 1,
        "store must round-trip episode counters");
    Assert(loadedRecord.LastProgressUtc == now.AddDays(-1) && loadedRecord.ObjectiveCounts.SequenceEqual(new[] { 2, 3 }),
        "store must round-trip progress");
    Assert(loadedRecord.PlayerLevelAtFailure == 35 && loadedRecord.EquipmentFingerprint == "100:healthy|200:critical",
        "store must round-trip combat context");
    Assert(loadedRecord.DatasetVersion == "quest-data-v1" && loadedRecord.CoreVersion == "core-v1" && loadedRecord.NavigationFingerprint == "nav-v1",
        "store must round-trip version context");
    Assert(loadedRecord.RecoveryCycleId == 7, "store must round-trip the recovery cycle identity");
    Assert(loadedRecord.Evidence.Single().Text == "unreachable" &&
           loadedRecord.Evidence.Single().EpisodeCount == 3 &&
           loadedRecord.Evidence.Single().RecoveryCycleId == 7 &&
           loadedRecord.Evidence.Single().SourceKey?.Equals(key) == true,
        "store must round-trip evidence and its nullable identity markers, including the source key");
    Assert(!File.Exists(path + ".tmp"), "an atomic save must not leave its temporary file behind");
}

static void TestCorruptStoreQuarantine(string root)
{
    Directory.CreateDirectory(root);
    var path = Path.Combine(root, "quest-recovery.json");
    File.WriteAllText(path, "{not-json");
    var messages = new List<string>();

    var loaded = new QuestRecoveryStore(path, messages.Add).Load();

    Assert(loaded.Records.Count == 0, "a corrupt store must load as empty state");
    Assert(!File.Exists(path), "the corrupt file must be moved away from the live store path");
    Assert(Directory.GetFiles(root, "quest-recovery.corrupt-*.json").Length == 1,
        "the corrupt store must be quarantined with a timestamped name");
    Assert(messages.Count == 1 && messages[0].Contains("Json", StringComparison.OrdinalIgnoreCase),
        "corrupt-store diagnostics must include the parse exception");
}

static void TestStoreDirectOpenDistinguishesAbsenceAndAccess(string root)
{
    Directory.CreateDirectory(root);
    var messages = new List<string>();
    var missingFile = new QuestRecoveryStore(
        Path.Combine(root, "quest-recovery.json"), messages.Add).Load();
    var missingDirectory = new QuestRecoveryStore(
        Path.Combine(root, "missing", "quest-recovery.json"), messages.Add).Load();
    Assert(missingFile.Records.Count == 0 && missingDirectory.Records.Count == 0 && messages.Count == 0,
        "directly opened missing files and directories must be treated as clean absence");

    AssertThrows<UnauthorizedAccessException>(
        () => new QuestRecoveryStore(root, messages.Add).Load(),
        "a present directory opened as a store file must surface its access failure instead of looking absent");
    Assert(messages.Any(message => message.Contains("live data was left in place", StringComparison.Ordinal)),
        "a non-absence open failure must be logged while preserving the target");
    Assert(Directory.Exists(root), "an access failure must not move or overwrite the live target");
}

static void TestValidStoreIoFailureIsSurfaced(string root)
{
    Directory.CreateDirectory(root);
    var path = Path.Combine(root, "quest-recovery.json");
    var store = new QuestRecoveryStore(path);
    store.Save(new QuestRecoveryDocument { CharacterName = "Jeof", RealmName = "Lordaeron" });

    using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    {
        AssertThrows<IOException>(() => store.Load(),
            "a sharing violation must be surfaced instead of producing empty overwriteable state");
    }

    Assert(File.Exists(path), "an I/O load failure must leave the valid live store in place");
    Assert(Directory.GetFiles(root, "quest-recovery.corrupt-*.json").Length == 0,
        "an I/O load failure must not mislabel valid data as malformed JSON");
}

static void TestUnsupportedSchemaIsPreserved(string root)
{
    Directory.CreateDirectory(root);
    var path = Path.Combine(root, "quest-recovery.json");
    var store = new QuestRecoveryStore(path);
    store.Save(new QuestRecoveryDocument
    {
        SchemaVersion = 2,
        CharacterName = "Jeof",
        RealmName = "Lordaeron"
    });

    AssertThrows<NotSupportedException>(() => store.Load(),
        "an unsupported quest recovery schema must be rejected explicitly");
    Assert(File.Exists(path), "an unsupported-schema store must be preserved in place");
    Assert(Directory.GetFiles(root, "quest-recovery.corrupt-*.json").Length == 0,
        "an unsupported schema must not be quarantined as malformed JSON");
}

static void TestPersistenceAndIdentityIsolation(string settingsRoot, DateTime now)
{
    var clock = new FixedClock(now);
    var environment = CreateEnvironment(settingsRoot, "Jeof", "Lordaeron");
    var manager = new QuestRecoveryManager(clock);
    manager.Configure(environment);
    var key = QuestRecoveryKey.ForQuestStage(867, QuestRecoveryStage.Pickup);

    var first = manager.Report(
        QuestAttemptOutcome.Failure(key, QuestFailureReason.PickupTargetNotOffered, "not offered"),
        Context());
    manager.Flush();

    Assert(first.State == QuestRecoveryState.CoolingDown, "a reported failed episode must apply recovery policy");
    Assert(manager.GetEntries().Single().EpisodeCount == 1, "one failure outcome must count one episode");
    var jsonPath = Path.Combine(settingsRoot, "QuestRecovery", "Jeof-Lordaeron", "quest-recovery.json");
    Assert(File.Exists(jsonPath), "JSON path is wrong");

    var reloaded = new QuestRecoveryManager(clock);
    reloaded.Configure(environment);
    Assert(reloaded.GetEntries().Single().EpisodeCount == 1, "state must survive reload");

    clock.UtcNow = now.AddMinutes(16);
    var beforeEvaluate = reloaded.GetEntries().Single();
    var due = reloaded.Evaluate(key, Context());
    var afterEvaluate = reloaded.GetEntries().Single();
    Assert(due.MayAttempt && due.State == QuestRecoveryState.HalfOpen, "elapsed cooldown must evaluate as half-open");
    Assert(beforeEvaluate.State == QuestRecoveryState.CoolingDown && afterEvaluate.State == QuestRecoveryState.CoolingDown,
        "Evaluate must be read-only");

    reloaded.Configure(new QuestRecoveryEnvironment(
        settingsRoot, "Jeof", "Lordaeron", "unknown", "unknown", "unknown"));
    var richerKey = QuestRecoveryKey.ForQuestStage(875, QuestRecoveryStage.Pickup);
    reloaded.Report(
        QuestAttemptOutcome.Failure(richerKey, QuestFailureReason.PickupTargetNotOffered, "not offered"),
        new QuestRecoveryContext());
    var richerRecord = reloaded.GetEntries().Single(entry => entry.Key.Equals(richerKey));
    Assert(richerRecord.DatasetVersion == "quest-data-v1" && richerRecord.CoreVersion == "core-v1" && richerRecord.NavigationFingerprint == "nav-v1",
        "unknown configuration and context must not downgrade richer fingerprints");

    var objectiveKey = QuestRecoveryKey.ForObjective(900, 0);
    reloaded.Report(
        QuestAttemptOutcome.Failure(objectiveKey, QuestFailureReason.NoObjectiveProgress, "stalled"),
        Context());
    reloaded.ReportProgress(objectiveKey, new[] { 1 }, Context());
    var progressed = reloaded.GetEntries().Single(entry => entry.Key.Equals(objectiveKey));
    Assert(progressed.State == QuestRecoveryState.Eligible && progressed.EpisodeCount == 0,
        "manager progress reporting must reset objective escalation");

    var retryKey = QuestRecoveryKey.ForQuestStage(901, QuestRecoveryStage.Pickup);
    reloaded.Report(
        QuestAttemptOutcome.Failure(retryKey, QuestFailureReason.PickupTargetNotOffered, "not offered"),
        Context());
    reloaded.RetryNow(retryKey);
    Assert(reloaded.Evaluate(retryKey, Context()).MayAttempt,
        "retry-now must permit one controlled probe of a non-terminal record");

    reloaded.MarkCompleted(867);
    reloaded.Flush();
    reloaded.Report(
        QuestAttemptOutcome.Failure(QuestRecoveryKey.ForObjective(867, 0), QuestFailureReason.NoObjectiveProgress, "late failure"),
        Context());
    Assert(reloaded.GetEntries().Where(entry => entry.Key.QuestId == 867).All(entry => entry.State == QuestRecoveryState.Completed),
        "a later failure must not reopen a completed quest");
    Assert(!reloaded.Evaluate(QuestRecoveryKey.ForObjective(867, 1), Context()).MayAttempt,
        "completion must deny every key for the quest");

    reloaded.SetManualBlacklist(875, blacklisted: true);
    reloaded.RetryNow(richerKey);
    Assert(!reloaded.Evaluate(QuestRecoveryKey.ForObjective(875, 0), Context()).MayAttempt,
        "manual blacklist must deny every key and ignore retry-now controls");

    reloaded.Configure(CreateEnvironment(settingsRoot, "Other", "Lordaeron"));
    Assert(reloaded.GetEntries().Count == 0, "identity change must not reuse another character's recovery state");
    reloaded.Configure(environment);
    Assert(reloaded.GetEntries().Any(entry => entry.Key.QuestId == 867 && entry.State == QuestRecoveryState.Completed),
        "returning to an identity must load its persisted completion state");
    Assert(!reloaded.Evaluate(QuestRecoveryKey.ForObjective(875, 0), Context()).MayAttempt,
        "manual blacklist must persist across identity changes and reloads");
}

static void TestLegacyMigrationIsIdempotent(string settingsRoot, DateTime now)
{
    var legacyDirectory = Path.Combine(settingsRoot, "WholesomeAutoQuest", "Jeof-Lordaeron");
    Directory.CreateDirectory(legacyDirectory);
    var legacyPath = Path.Combine(legacyDirectory, "quest_blacklist.txt");
    var backupPath = Path.Combine(legacyDirectory, "quest_blacklist.legacy.bak");
    File.WriteAllText(legacyPath, "867,875");
    var clock = new FixedClock(now);

    var manager = new QuestRecoveryManager(clock);
    manager.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));

    Assert(manager.GetEntries().Select(entry => entry.Key.QuestId).OrderBy(id => id).SequenceEqual(new uint[] { 867, 875 }),
        "legacy migration must import every comma-separated ID once");
    Assert(manager.GetEntries().All(entry => entry.State == QuestRecoveryState.Quarantined && entry.Reason == QuestFailureReason.LegacyUnknown),
        "legacy IDs must import as LegacyUnknown quarantines");
    Assert(File.Exists(backupPath), "legacy migration must create a backup");
    var originalBackup = File.ReadAllBytes(backupPath);
    Assert(!manager.Evaluate(QuestRecoveryKey.ForObjective(875, 0), Context(playerLevel: 0)).MayAttempt,
        "a legacy quarantine must cover every stage of its quest");
    var liveEvidence = new QuestAttemptOutcome
    {
        Key = QuestRecoveryKey.ForObjective(875, 0),
        Reason = QuestFailureReason.LegacyUnknown,
        IsFailureEpisode = false,
        Evidence = "live dialog offered legacy quest",
        ObservedQuestId = 867,
        OfferedQuestIds = new uint[] { 867 }
    };
    var probe = manager.Report(liveEvidence, Context(playerLevel: 0));
    Assert(probe.MayAttempt && probe.State == QuestRecoveryState.HalfOpen,
        "live quest evidence must permit one controlled probe of a legacy quarantine");
    Assert(manager.GetEntries().Single(entry => entry.Key.QuestId == 875).EpisodeCount == 0,
        "live legacy evidence must not count as a failed episode");
    Assert(manager.GetEntries().Count == 2,
        "cross-stage legacy reconciliation must update the imported record instead of creating a duplicate");

    File.WriteAllText(legacyPath, "999");
    var reloaded = new QuestRecoveryManager(clock);
    reloaded.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));

    Assert(File.ReadAllBytes(backupPath).SequenceEqual(originalBackup), "later migration must never overwrite the legacy backup bytes");
    Assert(reloaded.GetEntries().All(entry => entry.Key.QuestId != 999), "a migration marker must prevent later legacy re-import");
}

static void TestLegacyCrossStageMaterialization(string settingsRoot, DateTime now)
{
    var legacyDirectory = Path.Combine(settingsRoot, "WholesomeAutoQuest", "Jeof-Lordaeron");
    Directory.CreateDirectory(legacyDirectory);
    File.WriteAllText(Path.Combine(legacyDirectory, "quest_blacklist.txt"), "867,875,876");
    var environment = CreateEnvironment(settingsRoot, "Jeof", "Lordaeron");
    var manager = new QuestRecoveryManager(new FixedClock(now));
    manager.Configure(environment);

    var objectiveKey = QuestRecoveryKey.ForObjective(867, 0);
    manager.Report(
        QuestAttemptOutcome.Failure(objectiveKey, QuestFailureReason.RepeatedDeaths, "objective death"),
        Context());
    var objective = manager.GetEntries().Single(entry => entry.Key.QuestId == 867);
    Assert(objective.Key.Equals(objectiveKey) && objective.CooldownUntilUtc == now.AddMinutes(30),
        "cross-stage Report must re-key legacy control and apply Objective cooldown semantics");

    var navigationKey = QuestRecoveryKey.ForQuestStage(875, QuestRecoveryStage.Navigation);
    manager.Report(
        QuestAttemptOutcome.Failure(navigationKey, QuestFailureReason.PathGenerationFailed, "no path"),
        Context());
    var navigation = manager.GetEntries().Single(entry => entry.Key.QuestId == 875);
    Assert(navigation.Key.Equals(navigationKey) && navigation.CooldownUntilUtc == now.AddMinutes(30),
        "cross-stage Report must re-key legacy control and apply Navigation cooldown semantics");

    var progressedKey = QuestRecoveryKey.ForObjective(876, 1);
    manager.ReportProgress(progressedKey, new[] { 0, 1 }, Context());
    var progressed = manager.GetEntries().Single(entry => entry.Key.QuestId == 876);
    Assert(progressed.Key.Equals(progressedKey) && progressed.State == QuestRecoveryState.Eligible,
        "cross-stage progress must materialize an Objective record and apply Objective progress semantics");
    Assert(manager.GetEntries().Select(entry => entry.Key).Distinct().Count() == manager.GetEntries().Count,
        "legacy materialization must not retain duplicate embedded keys");

    manager.Flush();
    var reloaded = new QuestRecoveryManager(new FixedClock(now));
    reloaded.Configure(environment);
    var entries = reloaded.GetEntries();
    Assert(entries.Count == 3 && entries.Select(entry => entry.Key).Distinct().Count() == entries.Count,
        "cross-stage records must reload without duplicate embedded keys");
    Assert(entries.Single(entry => entry.Key.QuestId == 867).Key.Equals(objectiveKey),
        "Objective materialization must survive flush and reload");
    Assert(entries.Single(entry => entry.Key.QuestId == 875).Key.Equals(navigationKey),
        "Navigation materialization must survive flush and reload");
    Assert(entries.Single(entry => entry.Key.QuestId == 876).Key.Equals(progressedKey),
        "Objective progress materialization must survive flush and reload");
}

static void TestLegacyRekeyKeepsEvidenceSourcesDistinct(string settingsRoot, DateTime now)
{
    var legacyDirectory = Path.Combine(settingsRoot, "WholesomeAutoQuest", "Jeof-Lordaeron");
    Directory.CreateDirectory(legacyDirectory);
    File.WriteAllText(Path.Combine(legacyDirectory, "quest_blacklist.txt"), "867");
    var manager = new QuestRecoveryManager(new FixedClock(now));
    manager.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));

    var pickupKey = QuestRecoveryKey.ForQuestStage(867, QuestRecoveryStage.Pickup);
    manager.Report(
        QuestAttemptOutcome.Observation(pickupKey, QuestFailureReason.NpcNotFoundInWorld, "same pulse"),
        Context());

    var objectiveKey = QuestRecoveryKey.ForObjective(867, 0);
    manager.Report(
        QuestAttemptOutcome.Observation(objectiveKey, QuestFailureReason.NpcNotFoundInWorld, "same pulse"),
        Context());

    var evidence = manager.GetEntries().Single().Evidence;
    Assert(evidence.Count(item => item.Text == "same pulse") == 2,
        "a Pickup observation must not coalesce with an identical Objective observation after legacy re-keying");
    Assert(evidence.Where(item => item.Text == "same pulse").Select(item => item.SourceKey)
            .SequenceEqual(new QuestRecoveryKey?[] { pickupKey, objectiveKey }),
        "each new evidence sample must retain the containing record key that produced it");
}

static void TestPersistenceFailureCanRetry(string settingsRoot, DateTime now)
{
    Directory.CreateDirectory(Path.GetDirectoryName(settingsRoot)!);
    File.WriteAllText(settingsRoot, "blocks directory creation");
    var messages = new List<string>();
    var manager = new QuestRecoveryManager(new FixedClock(now), messages.Add);
    var environment = CreateEnvironment(settingsRoot, "Jeof", "Lordaeron");
    manager.Configure(environment);
    var key = QuestRecoveryKey.ForQuestStage(867, QuestRecoveryStage.Pickup);
    manager.Report(
        QuestAttemptOutcome.Failure(key, QuestFailureReason.PickupTargetNotOffered, "not offered"),
        Context());

    manager.Flush();

    Assert(manager.GetEntries().Single().EpisodeCount == 1,
        "a failed persistence attempt must leave the in-memory decision intact");
    Assert(messages.Any(message =>
            message.StartsWith("Quest recovery persistence failed:", StringComparison.Ordinal) &&
            message.Contains("Exception", StringComparison.Ordinal)),
        "a persistence failure must log the full exception");

    File.Delete(settingsRoot);
    Directory.CreateDirectory(settingsRoot);
    manager.Flush();
    var reloaded = new QuestRecoveryManager(new FixedClock(now));
    reloaded.Configure(environment);
    Assert(reloaded.GetEntries().Single().EpisodeCount == 1,
        "a later flush must retry and persist the retained decision");
}

static void TestIdentitySwitchRequiresSuccessfulFlush(string root, DateTime now)
{
    var oldRoot = Path.Combine(root, "old");
    var newRoot = Path.Combine(root, "new");
    Directory.CreateDirectory(oldRoot);
    var oldEnvironment = CreateEnvironment(oldRoot, "Jeof", "Lordaeron");
    var manager = new QuestRecoveryManager(new FixedClock(now));
    var key = QuestRecoveryKey.ForQuestStage(867, QuestRecoveryStage.Pickup);
    manager.Configure(oldEnvironment);
    manager.Report(
        QuestAttemptOutcome.Failure(key, QuestFailureReason.PickupTargetNotOffered, "not offered"),
        Context());

    Directory.Delete(oldRoot, recursive: true);
    File.WriteAllText(oldRoot, "blocks dirty-state persistence");
    AssertThrows<InvalidOperationException>(
        () => manager.Configure(CreateEnvironment(newRoot, "Alt", "Lordaeron")),
        "an identity switch must fail when the old dirty state cannot be persisted");
    Assert(manager.GetEntries().Single().Key.Equals(key),
        "a failed identity switch must retain the old in-memory records");

    File.Delete(oldRoot);
    Directory.CreateDirectory(oldRoot);
    manager.Flush();
    var reloaded = new QuestRecoveryManager(new FixedClock(now));
    reloaded.Configure(oldEnvironment);
    Assert(reloaded.GetEntries().Single().EpisodeCount == 1,
        "a later Flush retry must persist dirty state retained after a failed identity switch");
}

static void TestIdentitySwitchLoadIsTransactional(string root, DateTime now)
{
    var oldRoot = Path.Combine(root, "old");
    var newRoot = Path.Combine(root, "new");
    var oldEnvironment = CreateEnvironment(oldRoot, "Jeof", "Lordaeron");
    var newEnvironment = CreateEnvironment(newRoot, "Alt", "Lordaeron");
    var manager = new QuestRecoveryManager(new FixedClock(now));
    var firstKey = QuestRecoveryKey.ForQuestStage(867, QuestRecoveryStage.Pickup);
    manager.Configure(oldEnvironment);
    manager.Report(
        QuestAttemptOutcome.Failure(firstKey, QuestFailureReason.PickupTargetNotOffered, "not offered"),
        Context());
    manager.Flush();

    var newPath = Path.Combine(newRoot, "QuestRecovery", "Alt-Lordaeron", "quest-recovery.json");
    new QuestRecoveryStore(newPath).Save(new QuestRecoveryDocument
    {
        CharacterName = "Alt",
        RealmName = "Lordaeron"
    });
    using (new FileStream(newPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    {
        AssertThrows<IOException>(() => manager.Configure(newEnvironment),
            "a candidate identity load failure must be surfaced");
    }

    var secondKey = QuestRecoveryKey.ForQuestStage(868, QuestRecoveryStage.Pickup);
    manager.Report(
        QuestAttemptOutcome.Failure(secondKey, QuestFailureReason.PickupTargetNotOffered, "not offered"),
        Context());
    manager.Flush();

    var oldReload = new QuestRecoveryManager(new FixedClock(now));
    oldReload.Configure(oldEnvironment);
    Assert(oldReload.GetEntries().Select(entry => entry.Key.QuestId).OrderBy(id => id)
            .SequenceEqual(new uint[] { 867, 868 }),
        "a failed candidate load must leave the old environment, store, and records active");
    var newReload = new QuestRecoveryManager(new FixedClock(now));
    newReload.Configure(newEnvironment);
    Assert(newReload.GetEntries().Count == 0,
        "a failed candidate load must not overwrite the candidate identity with old records");
}

static void TestPersistedAttemptingRecoversOneProbe(string root, DateTime now)
{
    var environment = CreateEnvironment(root, "Jeof", "Lordaeron");
    var path = Path.Combine(root, "QuestRecovery", "Jeof-Lordaeron", "quest-recovery.json");
    var key = QuestRecoveryKey.ForQuestStage(867, QuestRecoveryStage.Pickup);
    new QuestRecoveryStore(path).Save(new QuestRecoveryDocument
    {
        CharacterName = "Jeof",
        RealmName = "Lordaeron",
        Records = new[]
        {
            new QuestRecoveryRecord { Key = key, State = QuestRecoveryState.Attempting }
        }
    });

    var manager = new QuestRecoveryManager(new FixedClock(now));
    manager.Configure(environment);
    Assert(manager.GetEntries().Single().State == QuestRecoveryState.HalfOpen,
        "a persisted Attempting state must restart as a recoverable one-probe state");
    manager.Flush();
    Assert(new QuestRecoveryStore(path).Load().Records.Single().State == QuestRecoveryState.HalfOpen,
        "normalizing stale attempt ownership must mark the manager dirty for persistence");

    var decisions = new QuestRecoveryDecision[256];
    Parallel.For(0, decisions.Length, index => decisions[index] = manager.TryBeginAttempt(key, Context()));
    Assert(decisions.Count(decision => decision.MayAttempt) == 1,
        "exactly one concurrent caller may acquire recovered stale-attempt ownership");

    var budgetRoot = Path.Combine(root, "budget");
    var budgetPath = Path.Combine(budgetRoot, "QuestRecovery", "Jeof-Lordaeron", "quest-recovery.json");
    new QuestRecoveryStore(budgetPath).Save(new QuestRecoveryDocument
    {
        CharacterName = "Jeof",
        RealmName = "Lordaeron",
        RollingFailureUtc = Enumerable.Range(0, 6).Select(index => now.AddMinutes(-index)).ToArray(),
        Records = new[]
        {
            new QuestRecoveryRecord { Key = key, State = QuestRecoveryState.Attempting }
        }
    });
    var budgetManager = new QuestRecoveryManager(new FixedClock(now));
    budgetManager.Configure(CreateEnvironment(budgetRoot, "Jeof", "Lordaeron"));
    var denied = new QuestRecoveryDecision[32];
    Parallel.For(0, denied.Length, index => denied[index] = budgetManager.TryBeginAttempt(key, Context()));
    Assert(denied.All(decision => !decision.MayAttempt),
        "stale-attempt recovery must still enforce the rolling-hour retry budget");
}

static void TestCollisionSafeIdentityPaths(string root, DateTime now)
{
    var first = new QuestRecoveryManager(new FixedClock(now));
    first.Configure(CreateEnvironment(root, "A-B", "C"));
    first.Report(
        QuestAttemptOutcome.Failure(
            QuestRecoveryKey.ForQuestStage(867, QuestRecoveryStage.Pickup),
            QuestFailureReason.PickupTargetNotOffered,
            "first identity"),
        Context());
    first.Flush();

    var second = new QuestRecoveryManager(new FixedClock(now));
    second.Configure(CreateEnvironment(root, "A", "B-C"));
    second.Report(
        QuestAttemptOutcome.Failure(
            QuestRecoveryKey.ForQuestStage(875, QuestRecoveryStage.Pickup),
            QuestFailureReason.PickupTargetNotOffered,
            "second identity"),
        Context());
    second.Flush();

    var identityRoot = Path.Combine(root, "QuestRecovery");
    var directories = Directory.GetDirectories(identityRoot);
    Assert(directories.Length == 2,
        "separator-bearing character and realm names must not collide into one identity path");
    var documents = directories
        .Select(directory => new QuestRecoveryStore(Path.Combine(directory, "quest-recovery.json")).Load())
        .ToArray();
    Assert(documents.Any(document => document.CharacterName == "A-B" && document.RealmName == "C") &&
           documents.Any(document => document.CharacterName == "A" && document.RealmName == "B-C"),
        "identity documents must retain the raw character and realm names");

    var hazardRoot = Path.Combine(root, "hazards");
    var hazardIdentities = new[]
    {
        (Character: "A%B", Realm: "Realm"),
        (Character: "A%0025B", Realm: "Realm"),
        (Character: "A/B\u0001", Realm: "Realm"),
        (Character: "CON", Realm: "Realm. "),
        (Character: "CON", Realm: "Realm")
    };
    foreach (var identity in hazardIdentities)
    {
        var manager = new QuestRecoveryManager(new FixedClock(now));
        manager.Configure(CreateEnvironment(hazardRoot, identity.Character, identity.Realm));
        manager.Report(
            QuestAttemptOutcome.Failure(
                QuestRecoveryKey.ForQuestStage(900, QuestRecoveryStage.Pickup),
                QuestFailureReason.PickupTargetNotOffered,
                "hazard identity"),
            Context());
        manager.Flush();
    }

    Assert(Directory.GetDirectories(Path.Combine(hazardRoot, "QuestRecovery")).Length == hazardIdentities.Length,
        "percent, invalid/control, trailing aliases, and reserved device names must map to distinct safe paths");

    var ordinaryRoot = Path.Combine(root, "ordinary");
    var ordinary = new QuestRecoveryManager(new FixedClock(now));
    ordinary.Configure(CreateEnvironment(ordinaryRoot, "Jeof", "Lordaeron"));
    ordinary.Report(
        QuestAttemptOutcome.Failure(
            QuestRecoveryKey.ForQuestStage(999, QuestRecoveryStage.Pickup),
            QuestFailureReason.PickupTargetNotOffered,
            "ordinary identity"),
        Context());
    ordinary.Flush();
    Assert(File.Exists(Path.Combine(ordinaryRoot, "QuestRecovery", "Jeof-Lordaeron", "quest-recovery.json")),
        "ordinary simple identities must retain the required Jeof-Lordaeron path");
    var caseReload = new QuestRecoveryManager(new FixedClock(now));
    caseReload.Configure(CreateEnvironment(ordinaryRoot, "JEOF", "lordaeron"));
    Assert(caseReload.GetEntries().Single().Key.QuestId == 999,
        "identity casing must follow Windows case-insensitive path semantics");
}

static void TestProductionDiagnosticsReachBotLogger(string root)
{
    var messages = new List<string>();
    var previousFileLogging = Logging.FileLogging;
    Action<LogLevel, string> handler = (_, message) => messages.Add(message);
    Logging.FileLogging = false;
    Logging.OnMessageLogged += handler;
    try
    {
        QuestRecoveryManager.Instance.Configure(CreateEnvironment(root, "Logger", "Lordaeron"));
        QuestRecoveryManager.Instance.Report(
            QuestAttemptOutcome.Failure(
                QuestRecoveryKey.ForQuestStage(1234, QuestRecoveryStage.Pickup),
                QuestFailureReason.PickupTargetNotOffered,
                "diagnostic probe"),
            Context());
    }
    finally
    {
        Logging.OnMessageLogged -= handler;
        Logging.FileLogging = previousFileLogging;
    }

    Assert(messages.Any(message => message.Contains("[QuestRecovery]", StringComparison.Ordinal) &&
                                   message.Contains("quest=1234", StringComparison.Ordinal)),
        "the production singleton must emit recovery transitions through the bot logger with a concise prefix");
}

static void TestSuperscriptDeviceIdentityPaths(string root, DateTime now)
{
    var reservedIdentities = new[]
    {
        (Raw: "COM¹", Encoded: "%0043OM¹-Realm", Literal: "%0043OM¹"),
        (Raw: "COM²", Encoded: "%0043OM²-Realm", Literal: "%0043OM²"),
        (Raw: "COM³", Encoded: "%0043OM³-Realm", Literal: "%0043OM³"),
        (Raw: "LPT¹", Encoded: "%004CPT¹-Realm", Literal: "%004CPT¹"),
        (Raw: "LPT²", Encoded: "%004CPT²-Realm", Literal: "%004CPT²"),
        (Raw: "LPT³", Encoded: "%004CPT³-Realm", Literal: "%004CPT³")
    };

    foreach (var identity in reservedIdentities)
    {
        var manager = new QuestRecoveryManager(new FixedClock(now));
        manager.Configure(CreateEnvironment(root, identity.Raw, "Realm"));
        manager.Report(
            QuestAttemptOutcome.Failure(
                QuestRecoveryKey.ForQuestStage(867, QuestRecoveryStage.Pickup),
                QuestFailureReason.PickupTargetNotOffered,
                identity.Raw),
            Context());
        manager.Flush();
        Assert(File.Exists(Path.Combine(root, "QuestRecovery", identity.Encoded, "quest-recovery.json")),
            $"the reserved device identity {identity.Raw} must be encoded to a safe file path");

        var literalEscape = new QuestRecoveryManager(new FixedClock(now));
        literalEscape.Configure(CreateEnvironment(root, identity.Literal, "Realm"));
        literalEscape.Report(
            QuestAttemptOutcome.Failure(
                QuestRecoveryKey.ForQuestStage(875, QuestRecoveryStage.Pickup),
                QuestFailureReason.PickupTargetNotOffered,
                "literal escape"),
            Context());
        literalEscape.Flush();
    }

    var directories = Directory.GetDirectories(Path.Combine(root, "QuestRecovery"));
    Assert(directories.Length == reservedIdentities.Length * 2,
        "superscript device names and literal escape-like identities must not collide");
    var rawNames = directories
        .Select(directory => new QuestRecoveryStore(Path.Combine(directory, "quest-recovery.json")).Load().CharacterName)
        .ToArray();
    Assert(reservedIdentities.All(identity => rawNames.Contains(identity.Raw) &&
                                                rawNames.Contains(identity.Literal)),
        "superscript identity documents must preserve both raw device and literal escape-like names");
}

static void TestManagerRollingBudget(string settingsRoot, DateTime now)
{
    var clock = new FixedClock(now);
    var manager = new QuestRecoveryManager(clock);
    manager.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));
    for (uint questId = 1000; questId < 1006; questId++)
    {
        manager.Report(
            QuestAttemptOutcome.Failure(
                QuestRecoveryKey.ForQuestStage(questId, QuestRecoveryStage.Pickup),
                QuestFailureReason.PickupTargetNotOffered,
                "not offered"),
            Context());
    }

    var probeKey = QuestRecoveryKey.ForQuestStage(1000, QuestRecoveryStage.Pickup);
    manager.RetryNow(probeKey);
    Assert(!manager.Evaluate(probeKey, Context()).MayAttempt,
        "six manager-recorded episodes must exhaust the rolling-hour half-open budget");
    Assert(manager.Evaluate(probeKey, Context()).RetryUtc == now.AddHours(1),
        "budget exhaustion must expose its expiry so the scheduler can wake without a context change");
    clock.UtcNow = now.AddMinutes(1);
    manager.Report(QuestAttemptOutcome.Failure(
        QuestRecoveryKey.ForQuestStage(1006, QuestRecoveryStage.Pickup),
        QuestFailureReason.PickupTargetNotOffered, "seventh episode"), Context());
    clock.UtcNow = now.AddHours(1).AddTicks(-1);
    Assert(!manager.Evaluate(probeKey, Context()).MayAttempt,
        "the rolling budget must remain blocked before enough episodes expire");
    clock.UtcNow = now.AddHours(1);
    Assert(manager.Evaluate(probeKey, Context()).MayAttempt,
        "the timed probe must become available at the advertised budget expiry");
}

static void TestGeneratedFailureBatchCountsOneRollingEpisode(string settingsRoot, DateTime now)
{
    var manager = new QuestRecoveryManager(new FixedClock(now));
    manager.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));

    for (uint questId = 1100; questId < 1103; questId++)
    {
        var stage = QuestRecoveryKey.ForQuestStage(questId, QuestRecoveryStage.Pickup);
        var relation = QuestRecoveryKey.ForNpc(questId, QuestRecoveryStage.Pickup, 3000 + questId);
        var owner = manager.TryBeginAttempt(stage, Context());
        Assert(owner.MayAttempt, "the generated-batch fixture must acquire its source attempt");
        Assert(manager.TryReportGeneratedFailures(
            new[]
            {
                QuestAttemptOutcome.Failure(
                    relation, stage, owner.AttemptGeneration,
                    QuestFailureReason.PickupTargetNotOffered, "relation exhausted"),
                QuestAttemptOutcome.Failure(
                    stage, stage, owner.AttemptGeneration,
                    QuestFailureReason.PickupTargetNotOffered, "stage exhausted")
            },
            Context(),
            out var decisions) && decisions.Count == 2,
            "one generated failure batch must apply both record-local failures");
    }

    for (uint questId = 1200; questId < 1202; questId++)
    {
        manager.Report(
            QuestAttemptOutcome.Failure(
                QuestRecoveryKey.ForQuestStage(questId, QuestRecoveryStage.Pickup),
                QuestFailureReason.PickupTargetNotOffered,
                "independent episode"),
            Context());
    }

    var probe = QuestRecoveryKey.ForQuestStage(1100, QuestRecoveryStage.Pickup);
    manager.RetryNow(probe);
    Assert(manager.Evaluate(probe, Context()).MayAttempt,
        "three two-record generated batches plus two failures must consume five, not eight, rolling episodes");

    manager.Report(
        QuestAttemptOutcome.Failure(
            QuestRecoveryKey.ForQuestStage(1202, QuestRecoveryStage.Pickup),
            QuestFailureReason.PickupTargetNotOffered,
            "sixth independent episode"),
        Context());
    Assert(!manager.Evaluate(probe, Context()).MayAttempt,
        "the sixth outer failure episode must exhaust the rolling-hour budget");
}

static void TestEvidenceCoalescingRespectsEpisode(string settingsRoot, DateTime now)
{
    var clock = new FixedClock(now);
    var environment = CreateEnvironment(settingsRoot, "Jeof", "Lordaeron");
    var manager = new QuestRecoveryManager(clock);
    var key = QuestRecoveryKey.ForQuestStage(867, QuestRecoveryStage.Pickup);
    manager.Configure(environment);
    manager.Report(
        QuestAttemptOutcome.Observation(key, QuestFailureReason.NpcNotFoundInWorld, "pulse A"),
        Context());
    manager.Report(
        QuestAttemptOutcome.Failure(key, QuestFailureReason.PickupTargetNotOffered, "failed episode"),
        Context());
    manager.Report(
        QuestAttemptOutcome.Observation(key, QuestFailureReason.NpcNotFoundInWorld, "pulse A"),
        Context());

    Assert(manager.GetEntries().Single().Evidence.Count(evidence => evidence.Text == "pulse A") == 2,
        "identical observations from different active episodes must remain distinct");

    manager.Flush();
    var reloaded = new QuestRecoveryManager(clock);
    reloaded.Configure(environment);
    reloaded.Report(
        QuestAttemptOutcome.Observation(key, QuestFailureReason.NpcNotFoundInWorld, "pulse B"),
        Context());
    reloaded.Report(
        QuestAttemptOutcome.Observation(key, QuestFailureReason.NpcNotFoundInWorld, "pulse A"),
        Context());
    var persisted = reloaded.GetEntries().Single();
    Assert(persisted.Evidence.Count(evidence => evidence.Text == "pulse A") == 2,
        "the persisted episode marker must coalesce non-consecutive evidence after reload");
    Assert(persisted.EpisodeCount == 1 &&
           persisted.Evidence.Count(evidence => evidence.Text == "failed episode") == 1,
        "coalescing must not collapse or create actual failure episodes");
}

static void TestEvidenceCycleSeparatesFailureProgressReset(string settingsRoot, DateTime now)
{
    var manager = new QuestRecoveryManager(new FixedClock(now));
    var key = QuestRecoveryKey.ForObjective(867, 0);
    manager.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));
    manager.Report(
        QuestAttemptOutcome.Observation(key, QuestFailureReason.NoObjectiveProgress, "pulse A"),
        Context());
    manager.Report(
        QuestAttemptOutcome.Failure(key, QuestFailureReason.RepeatedDeaths, "failed episode"),
        Context());
    manager.ReportProgress(key, new[] { 1 }, Context());
    manager.Report(
        QuestAttemptOutcome.Observation(key, QuestFailureReason.NoObjectiveProgress, "pulse A"),
        Context());

    var record = manager.GetEntries().Single();
    Assert(record.EpisodeCount == 0 &&
           record.Evidence.Count(evidence => evidence.Text == "pulse A") == 2,
        "observation A after failure and real progress reset must be distinct from the prior cycle's A");
    Assert(record.RecoveryCycleId == 1 &&
           record.Evidence.Where(evidence => evidence.Text == "pulse A")
               .Select(evidence => evidence.RecoveryCycleId)
               .SequenceEqual(new long?[] { 0, 1 }),
        "a real progress reset must advance and stamp a new recovery cycle");
    manager.Flush();
    var reloaded = new QuestRecoveryManager(new FixedClock(now));
    reloaded.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));
    Assert(reloaded.GetEntries().Single().RecoveryCycleId == 1 &&
           reloaded.GetEntries().Single().Evidence.Last().RecoveryCycleId == 1,
        "the recovery cycle identity must survive flush and reload");

    reloaded.MarkCompleted(867);
    reloaded.Flush();
    var completedReload = new QuestRecoveryManager(new FixedClock(now));
    completedReload.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));
    Assert(completedReload.GetEntries().Single().State == QuestRecoveryState.Completed &&
           completedReload.GetEntries().Single().RecoveryCycleId == 1,
        "completed-record compaction must preserve the latest recovery cycle identity");
}

static void TestEvidenceCycleAdvancesAtEpisodeZero(string settingsRoot, DateTime now)
{
    var manager = new QuestRecoveryManager(new FixedClock(now));
    var key = QuestRecoveryKey.ForObjective(867, 0);
    manager.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));
    manager.Report(
        QuestAttemptOutcome.Observation(key, QuestFailureReason.NoObjectiveProgress, "pulse A"),
        Context());
    manager.ReportProgress(key, new[] { 1 }, Context());
    var progressed = manager.GetEntries().Single();
    manager.ReportProgress(key, new[] { 1 }, Context());
    var unchanged = manager.GetEntries().Single();
    manager.Report(
        QuestAttemptOutcome.Observation(key, QuestFailureReason.NoObjectiveProgress, "pulse A"),
        Context());

    Assert(progressed.RecoveryCycleId == 1 && unchanged.RecoveryCycleId == 1,
        "progress at episode zero must advance the cycle once and no-increase progress must not advance it");
    Assert(manager.GetEntries().Single().Evidence.Count(evidence => evidence.Text == "pulse A") == 2,
        "genuine objective progress at episode zero must begin a distinct evidence cycle");

    var fresh = new QuestRecoveryManager(new FixedClock(now));
    fresh.Configure(CreateEnvironment(Path.Combine(settingsRoot, "fresh"), "Jeof", "Lordaeron"));
    fresh.ReportProgress(key, new[] { 1 }, Context());
    Assert(fresh.GetEntries().Single().RecoveryCycleId == 1,
        "first observed objective progress must establish the first recovery cycle");
}

static void TestLegacyEvidenceWithoutSourceKeyIsUnknown(string settingsRoot, DateTime now)
{
    var path = Path.Combine(settingsRoot, "QuestRecovery", "Jeof-Lordaeron", "quest-recovery.json");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path,
        """
        {
          "SchemaVersion": 1,
          "CharacterName": "Jeof",
          "RealmName": "Lordaeron",
          "Records": [
            {
              "Key": {
                "QuestId": 867,
                "Stage": 2,
                "Scope": 3,
                "ObjectiveIndex": 0
              },
              "Evidence": [
                {
                  "ObservedUtc": "2026-09-02T12:00:00Z",
                  "Reason": 10,
                  "Text": "legacy pulse",
                  "EpisodeCount": 0,
                  "RecoveryCycleId": 0
                }
              ]
            }
          ]
        }
        """);

    var manager = new QuestRecoveryManager(new FixedClock(now));
    var key = QuestRecoveryKey.ForObjective(867, 0);
    manager.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));
    manager.Report(
        QuestAttemptOutcome.Observation(key, QuestFailureReason.NoObjectiveProgress, "legacy pulse"),
        Context());

    var evidence = manager.GetEntries().Single().Evidence;
    Assert(evidence.Count(item => item.Text == "legacy pulse") == 2,
        "schema-1 evidence without a source key must be unknown and must not coalesce with a current sample");
    Assert(evidence[0].EpisodeCount == 0 && evidence[0].RecoveryCycleId == 0 && evidence[0].SourceKey is null &&
           evidence[1].EpisodeCount == 0 && evidence[1].RecoveryCycleId == 0 &&
           evidence[1].SourceKey?.Equals(key) == true,
        "a missing schema-1 source key must deserialize as unknown while new samples are authoritative");
}

static void TestConcurrentReportingAndAttemptOwnership(string settingsRoot, DateTime now)
{
    var clock = new FixedClock(now);
    var manager = new QuestRecoveryManager(clock);
    manager.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));
    var observedKey = QuestRecoveryKey.ForQuestStage(867, QuestRecoveryStage.Pickup);

    Parallel.For(0, 1_000, _ =>
        manager.Report(
            QuestAttemptOutcome.Observation(observedKey, QuestFailureReason.NpcNotFoundInWorld, "same pulse"),
            Context()));

    var observed = manager.GetEntries().Single();
    Assert(observed.EpisodeCount == 0, "timer observations must not create failure episodes");
    Assert(observed.Reason == QuestFailureReason.None,
        "timer observations must not replace the active episode's failure reason");
    Assert(observed.Evidence.Count == 1,
        "identical concurrent non-failure pulse evidence must coalesce into one record");
    manager.Report(
        QuestAttemptOutcome.Observation(observedKey, QuestFailureReason.NpcNotFoundInWorld, "different pulse"),
        Context());
    manager.Report(
        QuestAttemptOutcome.Observation(observedKey, QuestFailureReason.NpcNotFoundInWorld, "same pulse"),
        Context());
    observed = manager.GetEntries().Single();
    Assert(observed.Evidence.Count == 2 &&
           observed.Evidence.Select(evidence => evidence.Text).OrderBy(text => text)
               .SequenceEqual(new[] { "different pulse", "same pulse" }) &&
           observed.Evidence.All(evidence => evidence.SourceKey?.Equals(observedKey) == true),
        "non-consecutive A,B,A observations must coalesce by key, cycle, episode, reason, and evidence");
    Parallel.For(0, 20, index =>
        manager.Report(
            QuestAttemptOutcome.Observation(observedKey, QuestFailureReason.NpcNotFoundInWorld, $"distinct-{index}"),
            Context()));
    observed = manager.GetEntries().Single();
    Assert(observed.Evidence.Count == 10 && observed.Evidence.Select(evidence => evidence.Text).Distinct().Count() == 10,
        "distinct concurrent evidence must append while remaining bounded to ten records");

    var attemptKey = QuestRecoveryKey.ForQuestStage(875, QuestRecoveryStage.Pickup);
    var decisions = new QuestRecoveryDecision[256];
    Parallel.For(0, decisions.Length, index => decisions[index] = manager.TryBeginAttempt(attemptKey, Context()));

    Assert(decisions.Count(decision => decision.MayAttempt) == 1,
        "exactly one concurrent caller must atomically own an attempt");
    Assert(manager.GetEntries().Count == 2 && manager.GetEntries().Single(entry => entry.Key.Equals(attemptKey)).State == QuestRecoveryState.Attempting,
        "attempt ownership must be represented by one synchronized record");

    var firstOwner = decisions.Single(decision => decision.MayAttempt);
    manager.Report(
        QuestAttemptOutcome.Failure(
            attemptKey,
            attemptKey,
            firstOwner.AttemptGeneration,
            QuestFailureReason.PickupTargetNotOffered,
            "probe failed"),
        Context());
    Assert(manager.GetEntries().Single(entry => entry.Key.Equals(attemptKey)).EpisodeCount == 1,
        "failure by an attempt owner must count one episode");
    clock.UtcNow = now.AddMinutes(16);
    var secondOwner = manager.TryBeginAttempt(attemptKey, Context());
    Assert(secondOwner.MayAttempt && secondOwner.AttemptGeneration > firstOwner.AttemptGeneration,
        "a failure after cooldown must first acquire a fresh exact ownership generation");
    manager.Report(
        QuestAttemptOutcome.Failure(
            attemptKey,
            attemptKey,
            secondOwner.AttemptGeneration,
            QuestFailureReason.PickupTargetNotOffered,
            "probe failed"),
        Context());
    var repeatedFailure = manager.GetEntries().Single(entry => entry.Key.Equals(attemptKey));
    Assert(repeatedFailure.EpisodeCount == 2 &&
           repeatedFailure.Evidence.Count(evidence => evidence.Text == "probe failed") == 2,
        "identical evidence from an actual new failure episode must append instead of coalescing");
}

static void TestSuccessfulAttemptReleasesOwnership(string settingsRoot, DateTime now)
{
    var manager = new QuestRecoveryManager(new FixedClock(now));
    var key = QuestRecoveryKey.ForQuestStage(1200, QuestRecoveryStage.Pickup);
    manager.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));

    var owner = manager.TryBeginAttempt(key, Context());
    Assert(owner.MayAttempt,
        "the first attempt must acquire ownership");
    var success = QuestAttemptOutcome.Success(key, owner.AttemptGeneration, "pickup accepted");
    Assert(success.Kind == QuestAttemptOutcomeKind.Success && !success.IsFailureEpisode,
        "success must be explicitly distinguishable from failure, observation, and redirect outcomes");
    var released = manager.Report(success, Context());
    Assert(released.State == QuestRecoveryState.Eligible && released.MayAttempt,
        "a successful owned attempt must return the circuit to eligible");

    var decisions = new QuestRecoveryDecision[128];
    Parallel.For(0, decisions.Length, index => decisions[index] = manager.TryBeginAttempt(key, Context()));
    Assert(decisions.Count(decision => decision.MayAttempt) == 1,
        "after success exactly one later caller must acquire fresh ownership without a restart");
}

static void TestStaleSuccessCannotReleaseNewOwner(string settingsRoot, DateTime now)
{
    var manager = new QuestRecoveryManager(new FixedClock(now));
    var key = QuestRecoveryKey.ForQuestStage(1204, QuestRecoveryStage.Pickup);
    manager.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));

    var ownerA = manager.TryBeginAttempt(key, Context());
    Assert(manager.OwnsAttempt(key, ownerA.AttemptGeneration)
           && !manager.OwnsAttempt(key, ownerA.AttemptGeneration + 1),
        "the manager must expose exact Attempting-generation eligibility to production callers");
    Assert(ownerA.MayAttempt && ownerA.AttemptGeneration > 0,
        "attempt A must receive a nonzero ownership generation");
    manager.Report(
        QuestAttemptOutcome.Success(key, ownerA.AttemptGeneration, "A succeeded"),
        Context());

    var ownerB = manager.TryBeginAttempt(key, Context());
    Assert(ownerB.MayAttempt && ownerB.AttemptGeneration > ownerA.AttemptGeneration,
        "attempt B must receive a monotonically newer ownership generation");

    var delayedA = manager.Report(
        QuestAttemptOutcome.Success(key, ownerA.AttemptGeneration, "delayed duplicate A"),
        Context());
    Assert(delayedA.State == QuestRecoveryState.Attempting && !delayedA.MayAttempt,
        "delayed success from A must not release active owner B");
    var activeB = manager.GetEntries().Single();
    Assert(activeB.AttemptGeneration == ownerB.AttemptGeneration
           && activeB.Evidence.All(item => item.Text != "delayed duplicate A"),
        "rejecting delayed A must preserve B's ownership generation without recording false success evidence");
    Assert(!manager.TryBeginAttempt(key, Context()).MayAttempt,
        "attempt C must remain denied while B owns the circuit");

    manager.Report(
        QuestAttemptOutcome.Success(key, ownerB.AttemptGeneration, "B succeeded"),
        Context());
    var ownerC = manager.TryBeginAttempt(key, Context());
    Assert(ownerC.MayAttempt && ownerC.AttemptGeneration > ownerB.AttemptGeneration,
        "valid B success must release ownership for a newer attempt C");
}

static void TestIncompleteRedirectRequiresExactGeneration(string settingsRoot, DateTime now)
{
    var environment = CreateEnvironment(settingsRoot, "Jeof", "Lordaeron");
    var manager = new QuestRecoveryManager(new FixedClock(now));
    var key = QuestRecoveryKey.ForQuestStage(1220, QuestRecoveryStage.TurnIn);
    manager.Configure(environment);

    var ownerA = manager.TryBeginAttempt(key, Context());
    var stale = manager.TryReportOwnedRedirect(
        new QuestAttemptOutcome
        {
            Key = key,
            AttemptKey = key,
            AttemptGeneration = ownerA.AttemptGeneration + 1,
            Kind = QuestAttemptOutcomeKind.Redirect,
            Reason = QuestFailureReason.TurnInQuestIncomplete,
            IsFailureEpisode = false,
            Evidence = "stale redirect"
        },
        Context());
    var stillA = manager.GetEntries().Single();
    Assert(!stale.Accepted
           && stale.Decision.State == QuestRecoveryState.Attempting
           && !stale.Decision.MayAttempt
           && stillA.AttemptGeneration == ownerA.AttemptGeneration
           && stillA.EpisodeCount == 0
           && stillA.Evidence.All(item => item.Text != "stale redirect"),
        "a stale incomplete redirect must not release or mutate the exact active owner");

    var redirected = manager.TryReportOwnedRedirect(
        new QuestAttemptOutcome
        {
            Key = key,
            AttemptKey = key,
            AttemptGeneration = ownerA.AttemptGeneration,
            Kind = QuestAttemptOutcomeKind.Redirect,
            Reason = QuestFailureReason.TurnInQuestIncomplete,
            IsFailureEpisode = false,
            Evidence = "objectives remain incomplete"
        },
        Context());
    var releasedA = manager.GetEntries().Single();
    Assert(redirected.Accepted
           && redirected.Decision.State == QuestRecoveryState.Eligible
           && redirected.Decision.MayAttempt
           && releasedA.State == QuestRecoveryState.Eligible
           && releasedA.Reason == QuestFailureReason.TurnInQuestIncomplete
           && releasedA.AttemptGeneration == ownerA.AttemptGeneration
           && releasedA.EpisodeCount == 0
           && releasedA.Evidence.Any(item => item.Text == "objectives remain incomplete")
           && !manager.OwnsAttempt(key, ownerA.AttemptGeneration),
        "the exact incomplete redirect must atomically release ownership and persist evidence without escalation");
    manager.Flush();

    var reloaded = new QuestRecoveryManager(new FixedClock(now));
    reloaded.Configure(environment);
    var persisted = reloaded.GetEntries().Single();
    Assert(persisted.State == QuestRecoveryState.Eligible
           && persisted.EpisodeCount == 0
           && persisted.Evidence.Any(item => item.Text == "objectives remain incomplete"),
        "an accepted incomplete redirect must remain durable without becoming a failure episode");

    var ownerB = reloaded.TryBeginAttempt(key, Context());
    var delayedA = reloaded.TryReportOwnedRedirect(
        new QuestAttemptOutcome
        {
            Key = key,
            AttemptKey = key,
            AttemptGeneration = ownerA.AttemptGeneration,
            Kind = QuestAttemptOutcomeKind.Redirect,
            Reason = QuestFailureReason.TurnInQuestIncomplete,
            IsFailureEpisode = false,
            Evidence = "delayed A redirect"
        },
        Context());
    var stillB = reloaded.GetEntries().Single();
    Assert(ownerB.AttemptGeneration > ownerA.AttemptGeneration
           && !delayedA.Accepted
           && delayedA.Decision.State == QuestRecoveryState.Attempting
           && !delayedA.Decision.MayAttempt
           && stillB.AttemptGeneration == ownerB.AttemptGeneration
           && stillB.Evidence.All(item => item.Text != "delayed A redirect")
           && reloaded.OwnsAttempt(key, ownerB.AttemptGeneration),
        "a delayed redirect from A must not mutate or release newly acquired owner B");

    var childRedirects = new[]
    {
        new QuestAttemptOutcome
        {
            Key = QuestRecoveryKey.ForNpc(1220, QuestRecoveryStage.TurnIn, 3338),
            AttemptKey = key,
            AttemptGeneration = ownerB.AttemptGeneration,
            Kind = QuestAttemptOutcomeKind.Redirect,
            Reason = QuestFailureReason.TurnInQuestIncomplete,
            IsFailureEpisode = false,
            Evidence = "child relation redirect"
        },
        new QuestAttemptOutcome
        {
            Key = QuestRecoveryKey.ForEndpoint(
                1220, QuestRecoveryStage.Navigation, 1, "cell:4:5"),
            AttemptKey = key,
            AttemptGeneration = ownerB.AttemptGeneration,
            Kind = QuestAttemptOutcomeKind.Redirect,
            Reason = QuestFailureReason.TurnInQuestIncomplete,
            IsFailureEpisode = false,
            Evidence = "child endpoint redirect"
        }
    };
    Assert(childRedirects.All(outcome => !reloaded.TryReportOwnedRedirect(outcome, Context()).Accepted)
           && reloaded.OwnsAttempt(key, ownerB.AttemptGeneration)
           && reloaded.GetEntries().Where(item => item.Key.QuestId == 1220)
               .SelectMany(item => item.Evidence)
               .All(item => item.Text is not ("child relation redirect" or "child endpoint redirect")),
        "owned redirects must reject structurally related child relation and endpoint keys because the redirect key must be exact");

    var malformedManager = new QuestRecoveryManager(new FixedClock(now));
    var malformedKey = QuestRecoveryKey.ForEndpoint(
        1223, QuestRecoveryStage.TurnIn, 1, "cell:1:2");
    malformedManager.Configure(CreateEnvironment(
        Path.Combine(settingsRoot, "malformed-scope"), "Jeof", "Lordaeron"));
    var malformedOwner = malformedManager.TryBeginAttempt(malformedKey, Context());
    var malformed = malformedManager.TryReportOwnedRedirect(
        new QuestAttemptOutcome
        {
            Key = malformedKey,
            AttemptKey = malformedKey,
            AttemptGeneration = malformedOwner.AttemptGeneration,
            Kind = QuestAttemptOutcomeKind.Redirect,
            Reason = QuestFailureReason.TurnInQuestIncomplete,
            IsFailureEpisode = false,
            Evidence = "malformed endpoint owner"
        },
        Context());
    Assert(!malformed.Accepted
           && malformed.Decision.State == QuestRecoveryState.Attempting
           && !malformed.Decision.MayAttempt
           && malformedManager.OwnsAttempt(malformedKey, malformedOwner.AttemptGeneration)
           && malformedManager.GetEntries().Single().Evidence.All(
               item => item.Text != "malformed endpoint owner"),
        "an incomplete redirect must reject a non-quest-stage owner even when its stage enum is TurnIn");
}

static void TestOwnedTerminalOutcomesRequireExactCurrentGeneration(string settingsRoot, DateTime now)
{
    var environment = CreateEnvironment(settingsRoot, "Jeof", "Lordaeron");
    var key = QuestRecoveryKey.ForQuestStage(1233, QuestRecoveryStage.TurnIn);
    var manager = new QuestRecoveryManager(new FixedClock(now));
    manager.Configure(environment);
    var ownerA = manager.TryBeginAttempt(key, Context());
    manager.AbandonAttempt(key, ownerA.AttemptGeneration);
    var ownerB = manager.TryBeginAttempt(key, Context());

    QuestRecoveryReportResult staleSuccess = manager.TryReportOwnedOutcome(
        QuestAttemptOutcome.Success(key, ownerA.AttemptGeneration, "delayed owner A success"),
        Context());
    QuestRecoveryReportResult childFailure = manager.TryReportOwnedOutcome(
        QuestAttemptOutcome.Failure(
            QuestRecoveryKey.ForNpc(1233, QuestRecoveryStage.TurnIn, 3338),
            key,
            ownerB.AttemptGeneration,
            QuestFailureReason.TurnInTargetNotOffered,
            "child terminal mismatch"),
        Context());
    QuestRecoveryRecord stillB = manager.GetEntries().Single();
    Assert(!staleSuccess.Accepted
           && !childFailure.Accepted
           && stillB.State == QuestRecoveryState.Attempting
           && stillB.AttemptGeneration == ownerB.AttemptGeneration
           && stillB.Evidence.All(item => item.Text is not (
               "delayed owner A success" or "child terminal mismatch")),
        "owned terminal reports must reject stale generations and non-exact child keys with zero mutation");

    QuestRecoveryReportResult acceptedFailure = manager.TryReportOwnedOutcome(
        QuestAttemptOutcome.Failure(
            key,
            key,
            ownerB.AttemptGeneration,
            QuestFailureReason.InteractionTimedOut,
            "exact current failure"),
        Context());
    QuestRecoveryRecord failed = manager.GetEntries().Single();
    Assert(acceptedFailure.Accepted
           && failed.State == QuestRecoveryState.CoolingDown
           && failed.AttemptGeneration == ownerB.AttemptGeneration
           && failed.Evidence.Any(item => item.Text == "exact current failure"),
        "an exact current owned stage failure must be accepted and release its generation");

    var successManager = new QuestRecoveryManager(new FixedClock(now));
    successManager.Configure(CreateEnvironment(
        Path.Combine(settingsRoot, "success"), "Jeof", "Lordaeron"));
    var successOwner = successManager.TryBeginAttempt(key, Context());
    QuestRecoveryReportResult acceptedSuccess = successManager.TryReportOwnedOutcome(
        QuestAttemptOutcome.Success(key, successOwner.AttemptGeneration, "exact current success"),
        Context());
    Assert(acceptedSuccess.Accepted
           && successManager.GetEntries().Single().State == QuestRecoveryState.Eligible
           && successManager.GetEntries().Single().Evidence.Any(
               item => item.Text == "exact current success"),
        "an exact current owned success must be accepted and release its generation");
}

static void TestOwnedRedirectApiRejectsNonRedirectOutcomesWithoutMutation(
    string settingsRoot,
    DateTime now)
{
    var cases = new List<(string Name, Func<QuestRecoveryKey, long, QuestAttemptOutcome> Create)>
    {
        ("success", (key, generation) =>
            QuestAttemptOutcome.Success(key, generation, "redirect API success misuse")),
        ("observation", (key, generation) => new QuestAttemptOutcome
        {
            Key = key,
            AttemptKey = key,
            AttemptGeneration = generation,
            Kind = QuestAttemptOutcomeKind.Observation,
            Reason = QuestFailureReason.TurnInQuestIncomplete,
            IsFailureEpisode = false,
            Evidence = "redirect API observation misuse"
        }),
        ("redirect-marked-as-failure", (key, generation) => new QuestAttemptOutcome
        {
            Key = key,
            AttemptKey = key,
            AttemptGeneration = generation,
            Kind = QuestAttemptOutcomeKind.Redirect,
            Reason = QuestFailureReason.TurnInQuestIncomplete,
            IsFailureEpisode = true,
            Evidence = "redirect API failure-flag misuse"
        })
    };
    foreach (QuestFailureReason reason in Enum.GetValues<QuestFailureReason>())
    {
        cases.Add(($"failure-{reason}", (key, generation) => new QuestAttemptOutcome
        {
            Key = key,
            AttemptKey = key,
            AttemptGeneration = generation,
            Kind = QuestAttemptOutcomeKind.Failure,
            Reason = reason,
            IsFailureEpisode = true,
            Evidence = $"redirect API failure misuse: {reason}"
        }));
        if (reason != QuestFailureReason.TurnInQuestIncomplete)
        {
            cases.Add(($"redirect-reason-{reason}", (key, generation) => new QuestAttemptOutcome
            {
                Key = key,
                AttemptKey = key,
                AttemptGeneration = generation,
                Kind = QuestAttemptOutcomeKind.Redirect,
                Reason = reason,
                IsFailureEpisode = false,
                Evidence = $"redirect API reason misuse: {reason}"
            }));
        }
    }

    var failures = new List<string>();
    for (int index = 0; index < cases.Count; index++)
    {
        var misuse = cases[index];
        string caseRoot = Path.Combine(settingsRoot, $"case-{index}");
        var key = QuestRecoveryKey.ForQuestStage(1237, QuestRecoveryStage.TurnIn);
        var manager = new QuestRecoveryManager(new FixedClock(now));
        manager.Configure(CreateEnvironment(caseRoot, "Jeof", "Lordaeron"));
        QuestRecoveryDecision owner = manager.TryBeginAttempt(key, Context());
        manager.Flush();

        Directory.Delete(caseRoot, recursive: true);
        File.WriteAllText(caseRoot, "blocks unexpected dirty-state persistence");

        QuestAttemptOutcome outcome = misuse.Create(key, owner.AttemptGeneration);
        QuestRecoveryReportResult result = manager.TryReportOwnedRedirect(outcome, Context());
        QuestRecoveryRecord record = manager.GetEntries().Single();
        bool remainedClean = manager.TryFlush();
        if (result.Accepted ||
            record.State != QuestRecoveryState.Attempting ||
            record.AttemptGeneration != owner.AttemptGeneration ||
            record.EpisodeCount != 0 ||
            record.Evidence.Any(item => item.Text == outcome.Evidence) ||
            !manager.OwnsAttempt(key, owner.AttemptGeneration) ||
            !remainedClean)
        {
            failures.Add(misuse.Name);
        }
    }

    Assert(failures.Count == 0,
        "the redirect-specific API must reject every success, failure, None reason, and malformed redirect " +
        $"without record, evidence, ownership, or dirty-state mutation; failed: {string.Join(", ", failures)}");
}

static void TestOwnedFailureRequiresExactGenerationAndReleasesAtomically(string settingsRoot, DateTime now)
{
    var manager = new QuestRecoveryManager(new FixedClock(now));
    var key = QuestRecoveryKey.ForQuestStage(1210, QuestRecoveryStage.Objective);
    manager.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));

    var ownerA = manager.TryBeginAttempt(key, Context());
    var stale = manager.Report(
        QuestAttemptOutcome.Failure(
            key,
            key,
            ownerA.AttemptGeneration + 1,
            QuestFailureReason.NoObjectiveProgress,
            "stale failure"),
        Context());
    Assert(stale.State == QuestRecoveryState.Attempting && !stale.MayAttempt,
        "a stale generated failure must not release the exact active owner");
    Assert(manager.GetEntries().Single().EpisodeCount == 0
           && manager.GetEntries().Single().Evidence.All(item => item.Text != "stale failure"),
        "a stale generated failure must not mutate escalation or evidence");

    var failed = manager.Report(
        QuestAttemptOutcome.Failure(
            key,
            key,
            ownerA.AttemptGeneration,
            QuestFailureReason.NoObjectiveProgress,
            "owned failure"),
        Context());
    Assert(failed.State == QuestRecoveryState.CoolingDown && !failed.MayAttempt,
        "an exact-generation failure must atomically apply policy and release Attempting ownership");
    Assert(!manager.OwnsAttempt(key, ownerA.AttemptGeneration),
        "the manager must stop recognizing the exact generation in the same failure transition");
    var record = manager.GetEntries().Single();
    Assert(record.EpisodeCount == 1
           && record.AttemptGeneration == ownerA.AttemptGeneration
           && record.Evidence.Any(item => item.Text == "owned failure"),
        "a valid owned failure must retain its generation and diagnostic evidence");

    manager.RetryNow(key);
    var ownerB = manager.TryBeginAttempt(key, Context());
    Assert(ownerB.MayAttempt && ownerB.AttemptGeneration > ownerA.AttemptGeneration,
        "a later controlled probe must acquire a newer generation");
    var delayedA = manager.Report(
        QuestAttemptOutcome.Failure(
            key,
            key,
            ownerA.AttemptGeneration,
            QuestFailureReason.NoObjectiveProgress,
            "delayed A failure"),
        Context());
    Assert(delayedA.State == QuestRecoveryState.Attempting && !delayedA.MayAttempt
           && manager.GetEntries().Single().AttemptGeneration == ownerB.AttemptGeneration
           && manager.GetEntries().Single().Evidence.All(item => item.Text != "delayed A failure"),
        "a delayed failure from A must not affect newer owner B");
}

static void TestOwnedEndpointFailurePreservesStageHistory(string settingsRoot, DateTime now)
{
    var clock = new FixedClock(now);
    var manager = new QuestRecoveryManager(clock);
    var stageKey = QuestRecoveryKey.ForQuestStage(1211, QuestRecoveryStage.Objective);
    var endpointKey = QuestRecoveryKey.ForEndpoint(1211, QuestRecoveryStage.Navigation, 1, "cell:4:5");
    manager.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));
    manager.Report(
        QuestAttemptOutcome.Failure(stageKey, QuestFailureReason.NoObjectiveProgress, "prior stage history"),
        Context());
    manager.RetryNow(stageKey);
    var owner = manager.TryBeginAttempt(stageKey, Context());

    manager.Report(
        QuestAttemptOutcome.Failure(
            endpointKey,
            stageKey,
            owner.AttemptGeneration,
            QuestFailureReason.PathGenerationFailed,
            "endpoint-only failure"),
        Context());

    var stage = manager.GetEntries().Single(item => item.Key.Equals(stageKey));
    var endpoint = manager.GetEntries().Single(item => item.Key.Equals(endpointKey));
    Assert(stage.State == QuestRecoveryState.Attempting
           && stage.EpisodeCount == 1
           && stage.Reason == QuestFailureReason.NoObjectiveProgress
           && stage.Evidence.Any(item => item.Text == "prior stage history")
           && stage.Evidence.All(item => item.Text != "endpoint-only failure"),
        "a subordinate endpoint failure must preserve the active stage owner and its broader history");
    Assert(endpoint.State == QuestRecoveryState.CoolingDown
           && endpoint.EpisodeCount == 1
           && endpoint.Reason == QuestFailureReason.PathGenerationFailed,
        "the exact endpoint failure must receive endpoint policy without widening to the objective stage");

    manager.Report(
        QuestAttemptOutcome.Failure(
            stageKey,
            stageKey,
            owner.AttemptGeneration,
            QuestFailureReason.NoNavigableHotspot,
            "stage exhausted"),
        Context());
    stage = manager.GetEntries().Single(item => item.Key.Equals(stageKey));
    Assert(stage.State == QuestRecoveryState.CoolingDown
           && stage.EpisodeCount == 2
           && stage.Evidence.Any(item => item.Text == "prior stage history")
           && stage.Evidence.Any(item => item.Text == "stage exhausted"),
        "the later exact stage failure must release ownership without erasing prior objective history");
}

static void TestGeneratedFailureAuthorityAndAtomicStageSequence(string settingsRoot, DateTime now)
{
    var manager = new QuestRecoveryManager(new FixedClock(now));
    manager.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));
    var source = QuestRecoveryKey.ForQuestStage(1213, QuestRecoveryStage.Objective);
    var endpoint = QuestRecoveryKey.ForEndpoint(1213, QuestRecoveryStage.Navigation, 1, "cell:6:7");
    var owner = manager.TryBeginAttempt(source, Context());

    AssertThrows<ArgumentException>(
        () => QuestAttemptOutcome.Failure(
            QuestRecoveryKey.ForEndpoint(9999, QuestRecoveryStage.Navigation, 1, "cross"),
            source,
            owner.AttemptGeneration,
            QuestFailureReason.PathGenerationFailed,
            "cross quest"),
        "public generated-failure construction must reject cross-quest authority");
    AssertThrows<ArgumentException>(
        () => QuestAttemptOutcome.Failure(
            QuestRecoveryKey.ForNpc(1213, QuestRecoveryStage.Pickup, 99),
            source,
            owner.AttemptGeneration,
            QuestFailureReason.PickupTargetNotOffered,
            "unrelated scope"),
        "public generated-failure construction must reject structurally unrelated keys");

    var maliciousCrossQuest = new QuestAttemptOutcome
    {
        Key = QuestRecoveryKey.ForEndpoint(9999, QuestRecoveryStage.Navigation, 1, "cross"),
        AttemptKey = source,
        AttemptGeneration = owner.AttemptGeneration,
        Kind = QuestAttemptOutcomeKind.Failure,
        Reason = QuestFailureReason.PathGenerationFailed,
        IsFailureEpisode = true,
        Evidence = "bypassed factory"
    };
    Parallel.For(0, 64, _ => manager.Report(maliciousCrossQuest, Context()));
    Assert(manager.OwnsAttempt(source, owner.AttemptGeneration)
           && manager.GetEntries().All(item => item.Key.QuestId != 9999),
        "manager Report must reject cross-quest generated failures even if a caller bypasses the factory");
    var maliciousUnrelated = new QuestAttemptOutcome
    {
        Key = QuestRecoveryKey.ForNpc(1213, QuestRecoveryStage.Pickup, 99),
        AttemptKey = source,
        AttemptGeneration = owner.AttemptGeneration,
        Kind = QuestAttemptOutcomeKind.Failure,
        Reason = QuestFailureReason.PickupTargetNotOffered,
        IsFailureEpisode = true,
        Evidence = "bypassed unrelated factory"
    };
    Parallel.For(0, 64, _ => manager.Report(maliciousUnrelated, Context()));
    Assert(manager.OwnsAttempt(source, owner.AttemptGeneration)
           && manager.GetEntries().All(item => item.Key.Scope != QuestRecoveryScope.NpcRelation),
        "manager Report must reject structurally unrelated generated targets under concurrency");

    var independentlyOwnedTarget = manager.TryBeginAttempt(endpoint, Context());
    Assert(independentlyOwnedTarget.MayAttempt, "the authority fixture requires an independently owned endpoint");
    Parallel.For(0, 64, _ => manager.Report(
        QuestAttemptOutcome.Failure(
            endpoint,
            source,
            owner.AttemptGeneration,
            QuestFailureReason.PathGenerationFailed,
            "must not overwrite target owner"),
        Context()));
    Assert(manager.OwnsAttempt(source, owner.AttemptGeneration)
           && manager.OwnsAttempt(endpoint, independentlyOwnedTarget.AttemptGeneration),
        "a generated subordinate failure must never overwrite a target owned by another generation");
    Assert(!manager.TryReportGeneratedFailures(
               new[]
               {
                   QuestAttemptOutcome.Failure(
                       endpoint,
                       source,
                       owner.AttemptGeneration,
                       QuestFailureReason.PathGenerationFailed,
                       "contended endpoint"),
                   QuestAttemptOutcome.Failure(
                       source,
                       source,
                       owner.AttemptGeneration,
                       QuestFailureReason.NoNavigableHotspot,
                       "contended stage")
               },
               Context(),
               out var contendedDecisions)
           && contendedDecisions.Count == 0
           && manager.OwnsAttempt(source, owner.AttemptGeneration)
           && manager.OwnsAttempt(endpoint, independentlyOwnedTarget.AttemptGeneration),
        "an independently owned generated target must reject the batch without throwing or mutation");

    manager.AbandonAttempt(endpoint, independentlyOwnedTarget.AttemptGeneration);
    manager.AbandonAttempt(source, owner.AttemptGeneration);
    var newer = manager.TryBeginAttempt(source, Context());
    Parallel.For(0, 64, _ => manager.Report(
        QuestAttemptOutcome.Failure(
            endpoint,
            source,
            owner.AttemptGeneration,
            QuestFailureReason.PathGenerationFailed,
            "stale source"),
        Context()));
    Assert(manager.OwnsAttempt(source, newer.AttemptGeneration)
           && manager.GetEntries().Single(item => item.Key.Equals(endpoint)).EpisodeCount == 0,
        "a stale source generation must not mutate an otherwise available subordinate target");
    Assert(!manager.TryReportGeneratedFailures(
               new[]
               {
                   QuestAttemptOutcome.Failure(
                       endpoint,
                       source,
                       owner.AttemptGeneration,
                       QuestFailureReason.PathGenerationFailed,
                       "stale endpoint batch"),
                   QuestAttemptOutcome.Failure(
                       source,
                       source,
                       owner.AttemptGeneration,
                       QuestFailureReason.NoNavigableHotspot,
                       "stale stage batch")
               },
               Context(),
               out var staleDecisions)
           && staleDecisions.Count == 0
           && manager.OwnsAttempt(source, newer.AttemptGeneration),
        "a stale generated source must reject the batch without throwing or releasing the newer owner");

    Assert(manager.TryReportGeneratedFailures(
        new[]
        {
            QuestAttemptOutcome.Failure(
                endpoint,
                source,
                newer.AttemptGeneration,
                QuestFailureReason.PathGenerationFailed,
                "final endpoint failed"),
            QuestAttemptOutcome.Failure(
                source,
                source,
                newer.AttemptGeneration,
                QuestFailureReason.NoNavigableHotspot,
                "all stage endpoints failed")
        },
        Context(),
        out var decisions),
        "the exact available generated batch must be accepted");
    var endpointRecord = manager.GetEntries().Single(item => item.Key.Equals(endpoint));
    var stageRecord = manager.GetEntries().Single(item => item.Key.Equals(source));
    Assert(decisions.Count == 2
           && endpointRecord.State == QuestRecoveryState.CoolingDown
           && endpointRecord.EpisodeCount == 1
           && stageRecord.State == QuestRecoveryState.CoolingDown
           && stageRecord.EpisodeCount == 1
           && !manager.OwnsAttempt(source, newer.AttemptGeneration),
        "one atomic subordinate-then-stage sequence must cool both scopes and release the exact stage owner last");
}

static void TestPickupAttemptAuthorizesNarrowAdapterFailures(string settingsRoot, DateTime now)
{
    var manager = new QuestRecoveryManager(new FixedClock(now));
    manager.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));
    var pickup = QuestRecoveryKey.ForQuestStage(876, QuestRecoveryStage.Pickup);
    var relation = QuestRecoveryKey.ForNpc(876, QuestRecoveryStage.Pickup, 3338);
    var endpoint = QuestRecoveryKey.ForEndpoint(876, QuestRecoveryStage.Navigation, 1, "3338:-437.2,-3192.1,91.6");
    var owner = manager.TryBeginAttempt(pickup, Context());

    var relationFailure = QuestAttemptOutcome.Failure(
        relation,
        pickup,
        owner.AttemptGeneration,
        QuestFailureReason.PickupWrongQuestShown,
        "target=876; shown=867; giver=3338; offered=[867,875]");
    manager.Report(relationFailure, Context());

    Assert(manager.OwnsAttempt(pickup, owner.AttemptGeneration)
           && manager.GetEntries().Single(item => item.Key.Equals(relation)).EpisodeCount == 1,
        "an exact pickup-stage owner must authorize its same-quest pickup/NPC relation failure");

    var endpointFailure = QuestAttemptOutcome.Failure(
        endpoint,
        pickup,
        owner.AttemptGeneration,
        QuestFailureReason.PathGenerationFailed,
        "map=1; endpoint=3338:-437.2,-3192.1,91.6");
    manager.Report(endpointFailure, Context());

    Assert(manager.OwnsAttempt(pickup, owner.AttemptGeneration)
           && manager.GetEntries().Single(item => item.Key.Equals(endpoint)).EpisodeCount == 1,
        "an exact pickup-stage owner must authorize its same-quest navigation endpoint failure");

    manager.Report(
        QuestAttemptOutcome.Failure(
            pickup,
            pickup,
            owner.AttemptGeneration,
            QuestFailureReason.EndpointUnreachable,
            "all pickup giver endpoints exhausted"),
        Context());
    Assert(!manager.OwnsAttempt(pickup, owner.AttemptGeneration),
        "the exact pickup-stage terminal outcome must release adapter ownership");
}

static void TestTurnInAttemptAuthorizesNarrowAdapterFailures(string settingsRoot, DateTime now)
{
    var manager = new QuestRecoveryManager(new FixedClock(now));
    manager.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));
    var turnIn = QuestRecoveryKey.ForQuestStage(876, QuestRecoveryStage.TurnIn);
    var relation = QuestRecoveryKey.ForNpc(876, QuestRecoveryStage.TurnIn, 3338);
    var endpoint = QuestRecoveryKey.ForEndpoint(876, QuestRecoveryStage.Navigation, 1, "cell:-6:-40");
    var owner = manager.TryBeginAttempt(turnIn, Context());

    manager.Report(
        QuestAttemptOutcome.Failure(
            relation,
            turnIn,
            owner.AttemptGeneration,
            QuestFailureReason.TurnInTargetNotOffered,
            "target=876; shown=867; ender=3338"),
        Context());
    manager.Report(
        QuestAttemptOutcome.Failure(
            endpoint,
            turnIn,
            owner.AttemptGeneration,
            QuestFailureReason.PathGenerationFailed,
            "map=1; endpoint=cell:-6:-40"),
        Context());

    Assert(manager.OwnsAttempt(turnIn, owner.AttemptGeneration)
           && manager.GetEntries().Single(item => item.Key.Equals(relation)).EpisodeCount == 1
           && manager.GetEntries().Single(item => item.Key.Equals(endpoint)).EpisodeCount == 1,
        "an exact turn-in owner must authorize same-quest turn-in relations and navigation endpoints");
    AssertThrows<ArgumentException>(
        () => QuestAttemptOutcome.Failure(
            QuestRecoveryKey.ForNpc(876, QuestRecoveryStage.Pickup, 3338),
            turnIn,
            owner.AttemptGeneration,
            QuestFailureReason.PickupTargetNotOffered,
            "wrong relation stage"),
        "a turn-in owner must reject pickup relation failures");
    AssertThrows<ArgumentException>(
        () => QuestAttemptOutcome.Failure(
            QuestRecoveryKey.ForEndpoint(876, QuestRecoveryStage.Objective, 1, "cell:-6:-40"),
            turnIn,
            owner.AttemptGeneration,
            QuestFailureReason.PathGenerationFailed,
            "wrong endpoint stage"),
        "a turn-in owner must reject non-navigation endpoint failures");

    manager.Report(
        QuestAttemptOutcome.Failure(
            turnIn,
            turnIn,
            owner.AttemptGeneration,
            QuestFailureReason.EndpointUnreachable,
            "all turn-in ender endpoints exhausted"),
        Context());
    Assert(!manager.OwnsAttempt(turnIn, owner.AttemptGeneration),
        "the exact turn-in stage terminal outcome must release adapter ownership");
}

static void TestNeutralAbandonReleasesOnlyExactAttemptGeneration(string settingsRoot, DateTime now)
{
    var manager = new QuestRecoveryManager(new FixedClock(now));
    var key = QuestRecoveryKey.ForQuestStage(1212, QuestRecoveryStage.Objective);
    manager.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));

    var ownerA = manager.TryBeginAttempt(key, Context());
    Assert(manager.AbandonAttempt(key, ownerA.AttemptGeneration),
        "an exact owner must be able to abandon an interrupted attempt neutrally");
    var released = manager.GetEntries().Single();
    Assert(released.State == QuestRecoveryState.Eligible
           && released.EpisodeCount == 0
           && released.Reason == QuestFailureReason.None
           && released.Evidence.Count == 0,
        "neutral abandon must not synthesize success, failure, evidence, or escalation");

    var ownerB = manager.TryBeginAttempt(key, Context());
    Assert(ownerB.MayAttempt && ownerB.AttemptGeneration > ownerA.AttemptGeneration,
        "the same identity must reacquire immediately after a user Stop/Start abandon");
    Assert(!manager.AbandonAttempt(key, ownerA.AttemptGeneration)
           && manager.OwnsAttempt(key, ownerB.AttemptGeneration),
        "a stale generation must not release a newer owner");
    Assert(manager.AbandonAttempt(key, ownerB.AttemptGeneration)
           && !manager.OwnsAttempt(key, ownerB.AttemptGeneration),
        "the newer exact generation must remain independently releasable");
}

static void TestIdentitySwitchCannotReuseOwnership(string settingsRoot, DateTime now)
{
    var manager = new QuestRecoveryManager(new FixedClock(now));
    var key = QuestRecoveryKey.ForQuestStage(1205, QuestRecoveryStage.Pickup);
    manager.Configure(CreateEnvironment(settingsRoot, "First", "Lordaeron"));
    var ownerA = manager.TryBeginAttempt(key, Context());

    manager.Configure(CreateEnvironment(settingsRoot, "Second", "Lordaeron"));
    var ownerB = manager.TryBeginAttempt(key, Context());
    Assert(ownerA.MayAttempt && ownerB.MayAttempt
           && ownerB.AttemptGeneration > ownerA.AttemptGeneration,
        "identity replacement must issue B an ownership generation newer than A");

    var delayedA = manager.Report(
        QuestAttemptOutcome.Success(key, ownerA.AttemptGeneration, "old identity A"),
        Context());
    Assert(delayedA.State == QuestRecoveryState.Attempting && !delayedA.MayAttempt,
        "success from identity A must not release the same quest key owned by identity B");
    Assert(!manager.TryBeginAttempt(key, Context()).MayAttempt,
        "identity B must retain ownership after delayed identity-A success");
}

static void TestManualBlacklistReplacementCannotReuseOwnership(string settingsRoot, DateTime now)
{
    var manager = new QuestRecoveryManager(new FixedClock(now));
    var key = QuestRecoveryKey.ForQuestStage(1206, QuestRecoveryStage.Pickup);
    manager.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));
    var ownerA = manager.TryBeginAttempt(key, Context());

    manager.SetManualBlacklist(key.QuestId, true);
    manager.SetManualBlacklist(key.QuestId, false);
    var ownerB = manager.TryBeginAttempt(key, Context());
    Assert(ownerA.MayAttempt && ownerB.MayAttempt
           && ownerB.AttemptGeneration > ownerA.AttemptGeneration,
        "manual blacklist replacement must not reuse A's ownership generation for B");

    var delayedA = manager.Report(
        QuestAttemptOutcome.Success(key, ownerA.AttemptGeneration, "pre-blacklist A"),
        Context());
    Assert(delayedA.State == QuestRecoveryState.Attempting && !delayedA.MayAttempt,
        "pre-blacklist success A must not release post-clear owner B");
    Assert(!manager.TryBeginAttempt(key, Context()).MayAttempt,
        "post-clear owner B must remain active after delayed A success");
}

static void TestManualBlacklistNormalizesEveryOwnedScope(string settingsRoot, DateTime now)
{
    var environment = CreateEnvironment(settingsRoot, "Jeof", "Lordaeron");
    var manager = new QuestRecoveryManager(new FixedClock(now));
    var objective = QuestRecoveryKey.ForQuestStage(1209, QuestRecoveryStage.Objective);
    var pickup = QuestRecoveryKey.ForQuestStage(1209, QuestRecoveryStage.Pickup);
    manager.Configure(environment);
    manager.Report(
        QuestAttemptOutcome.Observation(objective, QuestFailureReason.NoObjectiveProgress, "objective history"),
        Context());
    var objectiveOwner = manager.TryBeginAttempt(objective, Context());
    var pickupOwner = manager.TryBeginAttempt(pickup, Context());

    manager.SetManualBlacklist(1209, true);
    var blacklisted = manager.GetEntries().Where(item => item.Key.QuestId == 1209).ToArray();
    Assert(blacklisted.All(item => item.State != QuestRecoveryState.Attempting)
           && blacklisted.Count(item => item.State == QuestRecoveryState.ManualBlacklist) == 1
           && blacklisted.Any(item => item.Key.Equals(objective)
               && item.AttemptGeneration == objectiveOwner.AttemptGeneration
               && item.Evidence.Any(evidence => evidence.Text == "objective history"))
           && blacklisted.Any(item => item.Key.Equals(pickup)
               && item.AttemptGeneration == pickupOwner.AttemptGeneration),
        "manual exclusion must neutrally normalize every same-quest owner while preserving generations and evidence");
    Assert(!manager.TryReportGeneratedFailures(
               new[]
               {
                   QuestAttemptOutcome.Failure(
                       objective,
                       objective,
                       objectiveOwner.AttemptGeneration,
                       QuestFailureReason.NoNavigableHotspot,
                       "manual terminal raced generated batch")
               },
               Context(),
               out var terminalDecisions)
           && terminalDecisions.Count == 0
           && manager.GetEntries().Where(item => item.Key.QuestId == 1209)
               .All(item => item.State != QuestRecoveryState.Attempting),
        "a quest-wide terminal must reject a generated batch without leaving its source Attempting");
    manager.Flush();

    var reloaded = new QuestRecoveryManager(new FixedClock(now));
    reloaded.Configure(environment);
    Assert(reloaded.GetEntries().Where(item => item.Key.QuestId == 1209)
            .All(item => item.State != QuestRecoveryState.Attempting),
        "manual exclusion must persist without orphaned Attempting records");
    reloaded.SetManualBlacklist(1209, false);
    Assert(reloaded.GetEntries().Where(item => item.Key.QuestId == 1209)
            .All(item => item.State is not (QuestRecoveryState.Attempting or QuestRecoveryState.ManualBlacklist)),
        "removing manual exclusion must leave neither terminal nor orphaned owners");
    var nextObjective = reloaded.TryBeginAttempt(objective, Context());
    Assert(nextObjective.MayAttempt
           && nextObjective.AttemptGeneration > objectiveOwner.AttemptGeneration
           && nextObjective.AttemptGeneration > pickupOwner.AttemptGeneration,
        "manual exclusion removal must permit a new exact owner above every prior generation");
    reloaded.SetManualBlacklist(1209, false);
    Assert(reloaded.OwnsAttempt(objective, nextObjective.AttemptGeneration),
        "an idempotent manual removal must not abandon a newly acquired exact owner");
}

static void TestManualBlacklistPreservesCanonicalPickupAutomatic(string settingsRoot, DateTime now)
{
    var environment = CreateEnvironment(settingsRoot, "Jeof", "Lordaeron");
    var pickup = QuestRecoveryKey.ForQuestStage(1231, QuestRecoveryStage.Pickup);
    var manualKey = QuestRecoveryKey.ForManualTerminal(1231);
    string storePath = Path.Combine(
        settingsRoot, "QuestRecovery", "Jeof-Lordaeron", "quest-recovery.json");
    new QuestRecoveryStore(storePath).Save(new QuestRecoveryDocument
    {
        CharacterName = "Jeof",
        RealmName = "Lordaeron",
        LastAttemptGeneration = 41,
        Records = new[]
        {
            new QuestRecoveryRecord
            {
                Key = pickup,
                State = QuestRecoveryState.Quarantined,
                Reason = QuestFailureReason.PickupTargetNotOffered,
                FirstFailureUtc = now.AddHours(-3),
                LastFailureUtc = now.AddHours(-1),
                NextHalfOpenUtc = now.AddHours(5),
                EpisodeCount = 3,
                RecoveryCycleId = 7,
                AttemptGeneration = 41,
                AttemptCountInEpisode = 2,
                DeathCountInEpisode = 1,
                LastProgressUtc = now.AddDays(-1),
                ObjectiveCounts = new[] { 2, 3 },
                PlayerLevelAtFailure = 34,
                EquipmentFingerprint = "100:healthy",
                DatasetVersion = "quest-data-v1",
                CoreVersion = "core-v1",
                NavigationFingerprint = "nav-v1",
                Evidence = new[]
                {
                    new QuestRecoveryEvidence
                    {
                        ObservedUtc = now.AddHours(-1),
                        Reason = QuestFailureReason.PickupTargetNotOffered,
                        Text = "preserve automatic pickup",
                        EpisodeCount = 3,
                        RecoveryCycleId = 7,
                        SourceKey = pickup
                    }
                }
            }
        }
    });

    var manager = new QuestRecoveryManager(new FixedClock(now));
    manager.Configure(environment);
    Assert(manager.TrySetManualBlacklist(1231, true),
        "setting a manual terminal must report a state change");
    var marked = manager.GetEntries().Where(item => item.Key.QuestId == 1231).ToArray();
    Assert(marked.Length == 2
           && marked.Single(item => item.State == QuestRecoveryState.ManualBlacklist)
               .Key.Equals(manualKey),
        "manual exclusion must use a distinct persisted terminal key instead of overwriting canonical Pickup");
    AssertPreservedAutomaticPickup(marked.Single(item => item.Key.Equals(pickup)), pickup, now);
    manager.Flush();

    var reloaded = new QuestRecoveryManager(new FixedClock(now));
    reloaded.Configure(environment);
    var reloadedEntries = reloaded.GetEntries().Where(item => item.Key.QuestId == 1231).ToArray();
    Assert(reloadedEntries.Length == 2
           && reloadedEntries.Any(item => item.Key.Equals(manualKey)
               && item.State == QuestRecoveryState.ManualBlacklist),
        "the distinct manual terminal and automatic Pickup record must both survive reload and compaction");
    AssertPreservedAutomaticPickup(
        reloadedEntries.Single(item => item.Key.Equals(pickup)), pickup, now);

    Assert(reloaded.TrySetManualBlacklist(1231, false),
        "direct manual removal must remove the same-quest manual terminal");
    var restored = reloaded.GetEntries().Where(item => item.Key.QuestId == 1231).ToArray();
    Assert(restored.Length == 1 && restored[0].Key.Equals(pickup),
        "direct manual removal must reveal exactly the original canonical Pickup record");
    AssertPreservedAutomaticPickup(restored[0], pickup, now);
    reloaded.Flush();

    var restoredReload = new QuestRecoveryManager(new FixedClock(now));
    restoredReload.Configure(environment);
    QuestRecoveryRecord durable = restoredReload.GetEntries().Single(
        item => item.Key.QuestId == 1231);
    AssertPreservedAutomaticPickup(durable, pickup, now);
}

static void TestClearExclusionCombinesOverlayAndSelectedAutomatic(
    string settingsRoot,
    DateTime now)
{
    var selected = QuestRecoveryKey.ForQuestStage(1234, QuestRecoveryStage.Pickup);
    var sameQuestOtherStage = QuestRecoveryKey.ForQuestStage(1234, QuestRecoveryStage.TurnIn);
    var otherQuest = QuestRecoveryKey.ForQuestStage(1235, QuestRecoveryStage.Pickup);
    var completed = QuestRecoveryKey.ForQuestStage(1236, QuestRecoveryStage.TurnIn);
    string storePath = Path.Combine(
        settingsRoot, "QuestRecovery", "Jeof-Lordaeron", "quest-recovery.json");
    new QuestRecoveryStore(storePath).Save(new QuestRecoveryDocument
    {
        CharacterName = "Jeof",
        RealmName = "Lordaeron",
        Records = new[]
        {
            new QuestRecoveryRecord
            {
                Key = selected,
                State = QuestRecoveryState.Quarantined,
                Reason = QuestFailureReason.PickupTargetNotOffered,
                AttemptGeneration = 11
            },
            new QuestRecoveryRecord
            {
                Key = sameQuestOtherStage,
                State = QuestRecoveryState.CoolingDown,
                Reason = QuestFailureReason.TurnInTargetNotOffered,
                AttemptGeneration = 12
            },
            new QuestRecoveryRecord
            {
                Key = otherQuest,
                State = QuestRecoveryState.CoolingDown,
                Reason = QuestFailureReason.PickupTargetNotOffered,
                AttemptGeneration = 13
            },
            new QuestRecoveryRecord
            {
                Key = completed,
                State = QuestRecoveryState.Completed,
                AttemptGeneration = 14
            }
        }
    });

    var manager = new QuestRecoveryManager(new FixedClock(now));
    manager.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));
    Assert(manager.TrySetManualBlacklist(1234, true),
        "the combined-clear fixture must install its distinct manual overlay");
    Assert(manager.TryClearExclusion(selected),
        "one-click Clear must report removal of the overlay and selected automatic exclusion");
    QuestRecoveryRecord[] remaining = manager.GetEntries().ToArray();
    Assert(remaining.All(record => record.State != QuestRecoveryState.ManualBlacklist)
           && remaining.All(record => !record.Key.Equals(selected))
           && remaining.Any(record => record.Key.Equals(sameQuestOtherStage)
               && record.State == QuestRecoveryState.CoolingDown)
           && remaining.Any(record => record.Key.Equals(otherQuest)
               && record.State == QuestRecoveryState.CoolingDown)
           && remaining.Any(record => record.Key.Equals(completed)
               && record.State == QuestRecoveryState.Completed),
        "one-click Clear must remove both overlay and selected automatic only, preserving other stages, quests, and Completed");
}

static void AssertPreservedAutomaticPickup(
    QuestRecoveryRecord record,
    QuestRecoveryKey pickup,
    DateTime now)
{
    Assert(record.Key.Equals(pickup)
           && record.State == QuestRecoveryState.Quarantined
           && record.Reason == QuestFailureReason.PickupTargetNotOffered
           && record.FirstFailureUtc == now.AddHours(-3)
           && record.LastFailureUtc == now.AddHours(-1)
           && record.NextHalfOpenUtc == now.AddHours(5)
           && record.EpisodeCount == 3
           && record.RecoveryCycleId == 7
           && record.AttemptGeneration == 41
           && record.AttemptCountInEpisode == 2
           && record.DeathCountInEpisode == 1
           && record.LastProgressUtc == now.AddDays(-1)
           && record.ObjectiveCounts.SequenceEqual(new[] { 2, 3 })
           && record.PlayerLevelAtFailure == 34
           && record.EquipmentFingerprint == "100:healthy"
           && record.DatasetVersion == "quest-data-v1"
           && record.CoreVersion == "core-v1"
           && record.NavigationFingerprint == "nav-v1"
           && record.Evidence.Count == 1
           && record.Evidence[0].Text == "preserve automatic pickup"
           && record.Evidence[0].SourceKey?.Equals(pickup) == true,
        "manual toggling must preserve the complete canonical Pickup automatic record exactly");
}

static void TestLegacyCanonicalManualBlacklistMigrates(string settingsRoot, DateTime now)
{
    var environment = CreateEnvironment(settingsRoot, "Jeof", "Lordaeron");
    var legacyKey = QuestRecoveryKey.ForQuestStage(1232, QuestRecoveryStage.Pickup);
    string storePath = Path.Combine(
        settingsRoot, "QuestRecovery", "Jeof-Lordaeron", "quest-recovery.json");
    new QuestRecoveryStore(storePath).Save(new QuestRecoveryDocument
    {
        CharacterName = "Jeof",
        RealmName = "Lordaeron",
        LastAttemptGeneration = 19,
        Records = new[]
        {
            new QuestRecoveryRecord
            {
                Key = legacyKey,
                State = QuestRecoveryState.ManualBlacklist,
                Reason = QuestFailureReason.UserExcluded,
                AttemptGeneration = 19,
                Evidence = new[]
                {
                    new QuestRecoveryEvidence
                    {
                        ObservedUtc = now,
                        Reason = QuestFailureReason.UserExcluded,
                        Text = "legacy manual terminal",
                        SourceKey = legacyKey
                    }
                }
            }
        }
    });

    var manager = new QuestRecoveryManager(new FixedClock(now));
    manager.Configure(environment);
    Assert(manager.Evaluate(
            QuestRecoveryKey.ForQuestStage(1232, QuestRecoveryStage.TurnIn), Context()).State ==
        QuestRecoveryState.ManualBlacklist,
        "legacy canonical manual records must retain quest-wide terminal precedence");
    Assert(manager.TrySetManualBlacklist(1232, true),
        "reapplying a legacy manual exclusion must migrate it to the distinct terminal key");
    var migrated = manager.GetEntries().Single(record => record.Key.QuestId == 1232);
    Assert(migrated.Key.Equals(QuestRecoveryKey.ForManualTerminal(1232))
           && migrated.State == QuestRecoveryState.ManualBlacklist
           && migrated.AttemptGeneration == 19
           && migrated.Evidence.Single().Text == "legacy manual terminal",
        "legacy manual migration must retain its persisted recovery evidence and generation");
    manager.Flush();

    var reloaded = new QuestRecoveryManager(new FixedClock(now));
    reloaded.Configure(environment);
    Assert(reloaded.GetEntries().Single(record => record.Key.QuestId == 1232).Key.Equals(
            QuestRecoveryKey.ForManualTerminal(1232)),
        "the migrated manual terminal key must survive reload and compaction");
    Assert(reloaded.TrySetManualBlacklist(1232, false)
           && reloaded.GetEntries().All(record => record.Key.QuestId != 1232),
        "removing a migrated legacy manual exclusion must remain supported");
}

static void TestQuestWideTerminalPrecedenceAndAutomaticRestoration(string settingsRoot, DateTime now)
{
    var clock = new FixedClock(now);
    var manager = new QuestRecoveryManager(clock);
    var turnIn = QuestRecoveryKey.ForQuestStage(1221, QuestRecoveryStage.TurnIn);
    var endpoint = QuestRecoveryKey.ForEndpoint(1221, QuestRecoveryStage.Navigation, 1, "cell:4:5");
    manager.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));

    manager.Report(
        QuestAttemptOutcome.Failure(turnIn, QuestFailureReason.TurnInTargetNotOffered, "turn-in failure one"),
        Context());
    clock.UtcNow = now.AddMinutes(31);
    manager.Report(
        QuestAttemptOutcome.Failure(turnIn, QuestFailureReason.TurnInTargetNotOffered, "turn-in failure two"),
        Context());
    clock.UtcNow = now.AddMinutes(92);
    manager.Report(
        QuestAttemptOutcome.Failure(turnIn, QuestFailureReason.TurnInTargetNotOffered, "turn-in failure three"),
        Context());
    manager.Report(
        QuestAttemptOutcome.Failure(endpoint, QuestFailureReason.PathGenerationFailed, "endpoint failure"),
        Context());

    Assert(manager.Evaluate(turnIn, Context()).State == QuestRecoveryState.Quarantined
           && manager.Evaluate(endpoint, Context()).State == QuestRecoveryState.CoolingDown
           && manager.GetRecord(turnIn)?.Key.Equals(turnIn) == true
           && manager.GetRecord(endpoint)?.Key.Equals(endpoint) == true,
        "without a quest-wide terminal, exact stage and endpoint automatic decisions must remain authoritative");

    manager.SetManualBlacklist(1221, true);
    QuestRecoveryRecord manual = manager.GetRecord(turnIn)
        ?? throw new InvalidOperationException("manual terminal record missing");
    Assert(manual.State == QuestRecoveryState.ManualBlacklist
           && manual.Key.Equals(QuestRecoveryKey.ForManualTerminal(1221))
           && manager.Evaluate(turnIn, Context()).State == QuestRecoveryState.ManualBlacklist
           && manager.Evaluate(endpoint, Context()).State == QuestRecoveryState.ManualBlacklist,
        "a canonical pickup manual terminal must override every exact automatic scope for the quest");

    manager.SetManualBlacklist(1221, false);
    var restoredTurnIn = manager.GetRecord(turnIn);
    var restoredEndpoint = manager.GetRecord(endpoint);
    Assert(restoredTurnIn?.State == QuestRecoveryState.Quarantined
           && restoredTurnIn.Key.Equals(turnIn)
           && restoredTurnIn.Evidence.Any(item => item.Text == "turn-in failure three")
           && restoredEndpoint?.State == QuestRecoveryState.CoolingDown
           && restoredEndpoint.Key.Equals(endpoint)
           && restoredEndpoint.Evidence.Any(item => item.Text == "endpoint failure")
           && manager.GetEntries().Where(item => item.Key.QuestId == 1221)
               .All(item => item.State != QuestRecoveryState.Attempting),
        "removing the manual terminal must reveal preserved automatic records without orphaning ownership");

    string completedRoot = Path.Combine(settingsRoot, "completed-wins");
    string completedPath = Path.Combine(
        completedRoot, "QuestRecovery", "Jeof-Lordaeron", "quest-recovery.json");
    var completedTurnIn = QuestRecoveryKey.ForQuestStage(1222, QuestRecoveryStage.TurnIn);
    var manualPickup = QuestRecoveryKey.ForQuestStage(1222, QuestRecoveryStage.Pickup);
    var automaticEndpoint = QuestRecoveryKey.ForEndpoint(1222, QuestRecoveryStage.Navigation, 1, "cell:8:9");
    new QuestRecoveryStore(completedPath).Save(new QuestRecoveryDocument
    {
        CharacterName = "Jeof",
        RealmName = "Lordaeron",
        Records = new[]
        {
            new QuestRecoveryRecord
            {
                Key = automaticEndpoint,
                State = QuestRecoveryState.CoolingDown,
                Reason = QuestFailureReason.PathGenerationFailed
            },
            new QuestRecoveryRecord
            {
                Key = manualPickup,
                State = QuestRecoveryState.ManualBlacklist,
                Reason = QuestFailureReason.UserExcluded
            },
            new QuestRecoveryRecord
            {
                Key = completedTurnIn,
                State = QuestRecoveryState.Completed,
                Reason = QuestFailureReason.None
            }
        }
    });
    var completedManager = new QuestRecoveryManager(new FixedClock(now));
    completedManager.Configure(CreateEnvironment(completedRoot, "Jeof", "Lordaeron"));
    Assert(completedManager.GetRecord(automaticEndpoint)?.State == QuestRecoveryState.Completed
           && completedManager.Evaluate(automaticEndpoint, Context()).State == QuestRecoveryState.Completed,
        "Completed must outrank ManualBlacklist and an exact automatic record when legacy records coexist");
}

static void TestOwnershipFenceSurvivesManualReleaseReload(string settingsRoot, DateTime now)
{
    var environment = CreateEnvironment(settingsRoot, "Jeof", "Lordaeron");
    var key = QuestRecoveryKey.ForQuestStage(1208, QuestRecoveryStage.Pickup);
    var manager = new QuestRecoveryManager(new FixedClock(now));
    manager.Configure(environment);
    var ownerA = manager.TryBeginAttempt(key, Context());
    manager.SetManualBlacklist(key.QuestId, true);
    manager.SetManualBlacklist(key.QuestId, false);
    var released = manager.GetEntries().Single(record => record.Key.QuestId == key.QuestId);
    Assert(released.Key.Equals(key)
           && released.State == QuestRecoveryState.Eligible
           && released.AttemptGeneration == ownerA.AttemptGeneration,
        "manual removal must preserve the released automatic record and its ownership generation");
    manager.Flush();

    var reloaded = new QuestRecoveryManager(new FixedClock(now));
    reloaded.Configure(environment);
    var ownerB = reloaded.TryBeginAttempt(key, Context());
    Assert(ownerB.MayAttempt && ownerB.AttemptGeneration > ownerA.AttemptGeneration,
        "the released automatic record and ownership high-water mark must survive reload");
}

static void TestCompletedRecordsSurviveManualBlacklistToggleAndCompaction(string settingsRoot, DateTime now)
{
    var environment = CreateEnvironment(settingsRoot, "Jeof", "Lordaeron");
    var manager = new QuestRecoveryManager(new FixedClock(now));
    var pickup = QuestRecoveryKey.ForQuestStage(1210, QuestRecoveryStage.Pickup);
    var objective = QuestRecoveryKey.ForQuestStage(1210, QuestRecoveryStage.Objective);
    var turnIn = QuestRecoveryKey.ForQuestStage(1210, QuestRecoveryStage.TurnIn);
    manager.Configure(environment);

    manager.Report(
        QuestAttemptOutcome.Observation(pickup, QuestFailureReason.PickupTargetNotOffered, "pickup completion evidence"),
        Context());
    var pickupOwner = manager.TryBeginAttempt(pickup, Context());
    manager.Report(
        QuestAttemptOutcome.Success(pickup, pickupOwner.AttemptGeneration, "pickup completed"),
        Context());
    manager.Report(
        QuestAttemptOutcome.Observation(objective, QuestFailureReason.NoObjectiveProgress, "objective completion evidence"),
        Context());
    var objectiveOwnerA = manager.TryBeginAttempt(objective, Context());
    manager.Report(
        QuestAttemptOutcome.Success(objective, objectiveOwnerA.AttemptGeneration, "objective progress one"),
        Context());
    var objectiveOwnerB = manager.TryBeginAttempt(objective, Context());
    manager.Report(
        QuestAttemptOutcome.Success(objective, objectiveOwnerB.AttemptGeneration, "objective progress two"),
        Context());

    manager.MarkCompleted(1210);
    var completed = manager.GetEntries().Where(record => record.Key.QuestId == 1210).ToArray();
    long expectedGeneration = completed.Max(record => record.AttemptGeneration);
    long expectedCycle = completed.Max(record => record.RecoveryCycleId);
    var expectedEvidence = completed.SelectMany(record => record.Evidence)
        .Select(evidence => evidence.Text)
        .OrderBy(text => text, StringComparer.Ordinal)
        .ToArray();
    Assert(completed.All(record => record.State == QuestRecoveryState.Completed)
           && completed.Any(record => record.Key.Equals(turnIn))
           && expectedGeneration == objectiveOwnerB.AttemptGeneration
           && expectedCycle == 2
           && expectedEvidence.Contains("pickup completion evidence")
           && expectedEvidence.Contains("objective completion evidence"),
        "MarkCompleted must retain same-quest completion evidence/high-water records and always install a canonical TurnIn sentinel");

    manager.SetManualBlacklist(1210, true);
    manager.SetManualBlacklist(1210, false);
    var toggled = manager.GetEntries().Where(record => record.Key.QuestId == 1210).ToArray();
    Assert(toggled.Length == completed.Length
           && toggled.All(record => record.State == QuestRecoveryState.Completed)
           && toggled.All(record => record.Reason != QuestFailureReason.UserExcluded)
           && toggled.Max(record => record.AttemptGeneration) == expectedGeneration
           && toggled.Max(record => record.RecoveryCycleId) == expectedCycle
           && toggled.SelectMany(record => record.Evidence)
               .Select(evidence => evidence.Text)
               .OrderBy(text => text, StringComparer.Ordinal)
               .SequenceEqual(expectedEvidence),
        "manual blacklist true/false on a completed quest must be redundant and must neither overwrite nor reopen completion records");

    manager.Flush();
    var reloaded = new QuestRecoveryManager(new FixedClock(now));
    reloaded.Configure(environment);
    var compacted = reloaded.GetEntries().Where(record => record.Key.QuestId == 1210).ToArray();
    Assert(compacted.Length == 1
           && compacted[0].Key.Equals(turnIn)
           && compacted[0].State == QuestRecoveryState.Completed
           && compacted[0].AttemptGeneration == expectedGeneration
           && compacted[0].RecoveryCycleId == expectedCycle
           && compacted[0].Evidence.Any(evidence => evidence.Text == "pickup completion evidence")
           && compacted[0].Evidence.Any(evidence => evidence.Text == "objective completion evidence"),
        "completion compaction and reload must preserve the canonical TurnIn sentinel, evidence, and generation/cycle high-water marks");

    reloaded.SetManualBlacklist(1210, true);
    reloaded.SetManualBlacklist(1210, false);
    reloaded.Flush();
    var toggledReload = new QuestRecoveryManager(new FixedClock(now));
    toggledReload.Configure(environment);
    var durable = toggledReload.GetEntries().Single(record => record.Key.QuestId == 1210);
    Assert(durable.Key.Equals(turnIn)
           && durable.State == QuestRecoveryState.Completed
           && durable.AttemptGeneration == expectedGeneration
           && durable.RecoveryCycleId == expectedCycle
           && durable.Evidence.Any(evidence => evidence.Text == "pickup completion evidence")
           && durable.Evidence.Any(evidence => evidence.Text == "objective completion evidence"),
        "manual toggles after reload must leave the durable compacted completion sentinel unchanged");
}

static void TestRetryNowRejectsPriorOwnerSuccess(string settingsRoot, DateTime now)
{
    var manager = new QuestRecoveryManager(new FixedClock(now));
    var key = QuestRecoveryKey.ForQuestStage(1207, QuestRecoveryStage.Pickup);
    manager.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));
    var ownerA = manager.TryBeginAttempt(key, Context());
    manager.Report(
        QuestAttemptOutcome.Failure(
            key,
            key,
            ownerA.AttemptGeneration,
            QuestFailureReason.PickupTargetNotOffered,
            "A failed"),
        Context());
    manager.RetryNow(key);

    var delayedA = manager.Report(
        QuestAttemptOutcome.Success(key, ownerA.AttemptGeneration, "late A after retry"),
        Context());
    var halfOpen = manager.GetEntries().Single();
    Assert(delayedA.State == QuestRecoveryState.HalfOpen
           && halfOpen.State == QuestRecoveryState.HalfOpen
           && halfOpen.Reason == QuestFailureReason.PickupTargetNotOffered
           && halfOpen.Evidence.All(item => item.Text != "late A after retry"),
        "RetryNow must reject old A success until a new half-open probe acquires ownership");

    var probeB = manager.TryBeginAttempt(key, Context());
    Assert(probeB.MayAttempt && probeB.AttemptGeneration > ownerA.AttemptGeneration,
        "the half-open probe must acquire a distinct ownership generation");
}

static void TestSuccessfulHalfOpenClearsEscalation(string settingsRoot, DateTime now)
{
    var clock = new FixedClock(now);
    var manager = new QuestRecoveryManager(clock);
    var key = QuestRecoveryKey.ForQuestStage(1201, QuestRecoveryStage.Pickup);
    manager.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));
    manager.Report(
        QuestAttemptOutcome.Failure(key, QuestFailureReason.PickupTargetNotOffered, "first failure"),
        Context());
    clock.UtcNow = now.AddMinutes(16);
    manager.RetryNow(key);
    var probeOwner = manager.TryBeginAttempt(key, Context());
    Assert(probeOwner.State == QuestRecoveryState.Attempting,
        "a half-open probe must acquire attempt ownership");
    manager.Report(
        QuestAttemptOutcome.Success(key, probeOwner.AttemptGeneration, "probe succeeded"),
        Context());

    var record = manager.GetEntries().Single();
    Assert(record.State == QuestRecoveryState.Eligible
           && record.Reason == QuestFailureReason.None
           && record.EpisodeCount == 0
           && record.AttemptCountInEpisode == 0
           && record.CooldownUntilUtc == null
           && record.NextHalfOpenUtc == null,
        "successful probing must clear ownership, retry timers, and active escalation");
    Assert(record.Evidence.Any(item => item.Text == "first failure")
           && record.Evidence.Any(item => item.Text == "probe succeeded")
           && record.Evidence.Count <= 10,
        "success must retain bounded diagnostic evidence");

    manager.Flush();
    var reloaded = new QuestRecoveryManager(clock);
    reloaded.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));
    var persisted = reloaded.GetEntries().Single();
    Assert(persisted.State == QuestRecoveryState.Eligible
           && persisted.Reason == QuestFailureReason.None
           && persisted.CooldownUntilUtc == null
           && persisted.AttemptGeneration == probeOwner.AttemptGeneration
           && persisted.Evidence.Any(item => item.Text == "probe succeeded"),
        "a successful release and its last ownership generation must survive persistence and reload");
    var reloadedOwner = reloaded.TryBeginAttempt(key, Context());
    Assert(reloadedOwner.MayAttempt
           && reloadedOwner.AttemptGeneration > probeOwner.AttemptGeneration,
        "ownership generation must remain monotonic across persistence and reload");
}

static void TestSuccessCannotReopenTerminalStates(string settingsRoot, DateTime now)
{
    var manual = new QuestRecoveryManager(new FixedClock(now));
    var manualKey = QuestRecoveryKey.ForQuestStage(1202, QuestRecoveryStage.Pickup);
    manual.Configure(CreateEnvironment(Path.Combine(settingsRoot, "manual"), "Jeof", "Lordaeron"));
    manual.SetManualBlacklist(manualKey.QuestId, true);
    var manualDecision = manual.Report(
        QuestAttemptOutcome.Success(manualKey, 1, "must stay manual"), Context());
    Assert(manualDecision.State == QuestRecoveryState.ManualBlacklist && !manualDecision.MayAttempt,
        "success must never reopen a manual blacklist");

    var completed = new QuestRecoveryManager(new FixedClock(now));
    var completedKey = QuestRecoveryKey.ForQuestStage(1203, QuestRecoveryStage.TurnIn);
    completed.Configure(CreateEnvironment(Path.Combine(settingsRoot, "completed"), "Jeof", "Lordaeron"));
    completed.MarkCompleted(completedKey.QuestId);
    var completedDecision = completed.Report(
        QuestAttemptOutcome.Success(completedKey, 1, "must stay completed"), Context());
    Assert(completedDecision.State == QuestRecoveryState.Completed && !completedDecision.MayAttempt,
        "success must never reopen a completed circuit");
}

static QuestRecoveryEnvironment CreateEnvironment(string settingsRoot, string character, string realm) =>
    new(settingsRoot, character, realm, "quest-data-v1", "core-v1", "nav-v1");

static void TestStuckDetectionUsesActualMovementSpeedAndDisplacement()
{
    Assert(!DefaultStuckHandler.HasInsufficientProgress(
               currentSpeed: 0f,
               fallbackSpeed: 7f,
               elapsed: TimeSpan.FromSeconds(1),
               actualDistance: 0f),
        "a naturally completed CTM stop must not be classified as obstructed from fallback run speed");
    Assert(DefaultStuckHandler.HasInsufficientProgress(
               currentSpeed: 7f,
               fallbackSpeed: 7f,
               elapsed: TimeSpan.FromSeconds(1),
               actualDistance: 0.25f),
        "a moving player that makes materially less direct progress than expected must be classified as stuck");
    Assert(!DefaultStuckHandler.HasInsufficientProgress(
               currentSpeed: 7f,
               fallbackSpeed: 7f,
               elapsed: TimeSpan.FromSeconds(1),
               actualDistance: 5f),
        "normal direct movement progress must not trigger unstick recovery");
}

static void TestMeshNavigatorPulseDoesNotStreamTiles()
{
    var navigatorField = typeof(Navigator).GetField(
        "_navigator",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
        ?? throw new InvalidOperationException("Navigator backing field was not found");
    var pulseMethod = typeof(MeshNavigator).GetMethod(
        "OnPulse",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
        ?? throw new InvalidOperationException("MeshNavigator pulse handler was not found");
    var originalPlayer = ObjectManager.Me;
    var originalNavigator = navigatorField.GetValue(null);

    try
    {
        navigatorField.SetValue(null, null);
        ObjectManager.Me = new LocalPlayer(0);

        pulseMethod.Invoke(new MeshNavigator(), new object[] { new object(), EventArgs.Empty });

        Assert(navigatorField.GetValue(null) == null,
            "a routine movement pulse must not initialize or synchronously stream the native navigator");
    }
    finally
    {
        navigatorField.SetValue(null, originalNavigator);
        ObjectManager.Me = originalPlayer;
    }
}

static void TestQuestTravelSuppressesOpportunisticTargeting()
{
    Assert(Bots.Quest.QuestBot.ShouldSuppressOpportunisticTargeting(PoiType.QuestPickUp),
        "mounted travel to a quest pickup must not stop for an unengaged path mob");
    Assert(Bots.Quest.QuestBot.ShouldSuppressOpportunisticTargeting(PoiType.QuestTurnIn),
        "mounted travel to a quest turn-in must not stop for an unengaged path mob");
    Assert(Bots.Quest.QuestBot.ShouldSuppressOpportunisticTargeting(PoiType.Kill),
        "an existing kill POI must not be replaced by opportunistic targeting");
    Assert(Bots.Quest.QuestBot.ShouldSuppressOpportunisticTargeting(PoiType.Sell),
        "service travel must retain its existing opportunistic-targeting suppression");
    Assert(!Bots.Quest.QuestBot.ShouldSuppressOpportunisticTargeting(PoiType.Hotspot),
        "objective hotspot targeting must retain normal combat targeting");
    Assert(Bots.Quest.QuestBot.ShouldSuppressOpportunisticTargeting(PoiType.None, mounted: true),
        "mounted quest travel with no POI must outrun unengaged mobs instead of pulling them");
    Assert(Bots.Quest.QuestBot.ShouldSuppressOpportunisticTargeting(PoiType.Hotspot, mounted: true),
        "mounted hotspot travel must outrun unengaged mobs instead of pulling them");
    Assert(!Bots.Quest.QuestBot.ShouldSuppressOpportunisticTargeting(PoiType.Hotspot, mounted: false),
        "unmounted hotspot work must still be allowed to acquire its quest targets");
}

static void TestExclusiveForcedBehaviorSuppressesServicePreemption()
{
    Assert(!Bots.Quest.QuestBot.ShouldRunServiceBehavior(exclusiveForcedBehaviorActive: true),
        "an active transport behavior must not be preempted by flight-master or trainer service work");
    Assert(Bots.Quest.QuestBot.ShouldRunServiceBehavior(exclusiveForcedBehaviorActive: false),
        "ordinary quest work must retain normal service behavior");
}

static void TestTrainerTravelRequiresEfficientRoute()
{
    Assert(Bots.Grind.LevelBot.ShouldVisitTrainer(600f, hasKnownFlightConnection: false),
        "nearby trainers should remain reachable by ordinary ground navigation");
    Assert(Bots.Grind.LevelBot.ShouldVisitTrainer(3000f, hasKnownFlightConnection: true),
        "a known flight connection should permit a distant trainer visit");
    Assert(!Bots.Grind.LevelBot.ShouldVisitTrainer(3000f, hasKnownFlightConnection: false),
        "automatic training must not start a cross-zone blind ground-mount trip");
}

static void TestQuestTurnInDoesNotPreemptCombatPoi()
{
    var originalPoi = BotPoi.Current;
    var cachedWeightSetField = typeof(WeightSetEx).GetField(
        "_cachedWeightSet",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
        ?? throw new InvalidOperationException("WeightSetEx cache field was not found");
    var loadedWeightSetsField = typeof(WeightSetEx).GetField(
        "_loadedWeightSets",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
        ?? throw new InvalidOperationException("WeightSetEx loaded field was not found");
    var originalCachedWeightSet = cachedWeightSetField.GetValue(null);
    var originalLoadedWeightSets = loadedWeightSetsField.GetValue(null);

    try
    {
        var weightSet = (WeightSetEx)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(WeightSetEx));
        loadedWeightSetsField.SetValue(null, new[] { weightSet });
        cachedWeightSetField.SetValue(null, weightSet);
        BotPoi.Current = new BotPoi(new WoWPoint(12.7f, -701.35f, -19.13f), PoiType.Kill);
        var turnIn = new ForcedQuestTurnIn(
            6548,
            "Avenge My Village",
            11857,
            "Makaba Flathoof",
            new WoWPoint(-263f, -943f, 12.44f));

        var context = new object();
        turnIn.Branch.Start(context);
        for (var tick = 0; tick < 3; tick++)
        {
            turnIn.Branch.Tick(context);
        }
        turnIn.Branch.Stop(context);

        Assert(BotPoi.Current.Type == PoiType.Kill,
            "quest turn-in must yield to an active combat POI instead of causing Kill/TurnIn oscillation");
    }
    finally
    {
        BotPoi.Current = originalPoi;
        cachedWeightSetField.SetValue(null, originalCachedWeightSet);
        loadedWeightSetsField.SetValue(null, originalLoadedWeightSets);
    }
}

static QuestRecoveryContext Context(
    int playerLevel = 34,
    IReadOnlyList<int>? objectiveCounts = null) => new()
{
    PlayerLevel = playerLevel,
    ObjectiveCounts = objectiveCounts ?? Array.Empty<int>(),
    EquipmentFingerprint = "100:healthy",
    DatasetVersion = "quest-data-v1",
    CoreVersion = "core-v1",
    NavigationFingerprint = "nav-v1"
};

static void ResetDirectory(string path)
{
    if (Directory.Exists(path))
    {
        Directory.Delete(path, recursive: true);
    }

    Directory.CreateDirectory(path);
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertThrows<TException>(Action action, string message)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

static void AssertImmediateDataQuarantine(QuestFailureReason reason, DateTime now)
{
    var key = QuestRecoveryKey.ForObjective(867, 0);
    var failureContext = new QuestRecoveryContext
    {
        PlayerLevel = 35,
        EquipmentFingerprint = "gear-v1",
        DatasetVersion = "data-v1",
        CoreVersion = "core-v1",
        NavigationFingerprint = "nav-v1"
    };
    var quarantined = QuestRecoveryPolicy.ApplyFailure(
        QuestRecoveryRecord.Create(key),
        reason,
        failureContext,
        now);

    Assert(quarantined.State == QuestRecoveryState.Quarantined, $"{reason} must quarantine immediately");
    Assert(!QuestRecoveryPolicy.Evaluate(quarantined, failureContext, 0, now.AddMinutes(1)).MayAttempt,
        $"{reason} must stay quarantined for the failure-time dataset and core");
    Assert(!QuestRecoveryPolicy.Evaluate(quarantined, new QuestRecoveryContext
    {
        PlayerLevel = 36,
        EquipmentFingerprint = "gear-v2",
        DatasetVersion = "data-v1",
        CoreVersion = "core-v1",
        NavigationFingerprint = "nav-v2"
    }, 0, now.AddMinutes(1)).MayAttempt,
        $"irrelevant context changes must not reopen {reason}");
    Assert(QuestRecoveryPolicy.Evaluate(quarantined, new QuestRecoveryContext
    {
        DatasetVersion = "data-v2",
        CoreVersion = "core-v1",
        NavigationFingerprint = "nav-v1"
    }, 0, now.AddMinutes(1)).MayAttempt,
        $"a dataset change must reopen {reason}");
    Assert(QuestRecoveryPolicy.Evaluate(quarantined, new QuestRecoveryContext
    {
        DatasetVersion = "data-v1",
        CoreVersion = "core-v2",
        NavigationFingerprint = "nav-v1"
    }, 0, now.AddMinutes(1)).MayAttempt,
        $"a core-version change must reopen {reason}");

    var dueProbe = QuestRecoveryPolicy.Evaluate(quarantined, failureContext, 0, now.AddHours(6));
    Assert(dueProbe.State == QuestRecoveryState.HalfOpen && dueProbe.MayAttempt,
        $"a six-hour {reason} quarantine must permit one half-open probe");
}

sealed class FixedClock : IQuestRecoveryClock
{
    public FixedClock(DateTime utcNow) => UtcNow = utcNow;

    public DateTime UtcNow { get; set; }
}
