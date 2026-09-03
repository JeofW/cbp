using Styx.Logic.Questing.Recovery;
using Styx.Logic.Questing;
using Styx.Logic.Profiles.Quest;
using Bots.Quest.QuestOrder;
using Styx.Helpers;

var now = new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);
var testRoot = Path.Combine(AppContext.BaseDirectory, "quest-recovery-test-data");
ResetDirectory(testRoot);

try
{
    TestCompletedQuestTraversalStopsAtInvalidPointers();
    TestCompletedQuestTraversalStopsAtRepeatedPointer();
    TestCompletedQuestTraversalStopsAtReaderFailure();
    TestCompletedQuestTraversalCapsAtTenThousandNodes();
    TestCompletedQuestTraversalDeduplicatesQuestIds();
    TestCompletedQuestCacheInvalidatesAcrossIdentityChanges();
    TestCompletedQuestCacheSnapshotsAreStable();
    TestCompletedQuestCacheSerializesConcurrentRefreshes();
    TestCompletedQuestCacheDiscardsRefreshWhenIdentityChanges();
    TestQuestOrderDoesNotMutateWithoutAuthoritativeCompletion();
    TestQuestingCompletedQuestIdsAreSafeWithoutClient();
    TestQuestManagerObsoleteGuidanceShowsCompilableTryCall();
    TestQuestCompletionAuthorityIsTriState();
    TestEquipmentFingerprintPreservesSlotOrderAndDurabilityClass();
    TestProfileCompletionExpressionsPreserveUnknown();
    TestCompileBatchCompletionExpressionsPreserveUnknown();
    TestCompletionEvaluationScopesNestAndRecoverFromExceptions();
    TestForcedConditionBehaviorsDeferUnknown();
    TestCompletionDependentActionGatesDeferUnknown();
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
    TestOwnedFailureRequiresExactGenerationAndReleasesAtomically(Path.Combine(testRoot, "failure-generation"), now);
    TestOwnedEndpointFailurePreservesStageHistory(Path.Combine(testRoot, "failure-endpoint-owner"), now);
    TestGeneratedFailureAuthorityAndAtomicStageSequence(Path.Combine(testRoot, "failure-authority"), now);
    TestNeutralAbandonReleasesOnlyExactAttemptGeneration(Path.Combine(testRoot, "neutral-abandon"), now);
    TestRetryNowRejectsPriorOwnerSuccess(Path.Combine(testRoot, "success-retry-now"), now);
    TestIdentitySwitchCannotReuseOwnership(Path.Combine(testRoot, "success-identity"), now);
    TestManualBlacklistReplacementCannotReuseOwnership(Path.Combine(testRoot, "success-manual"), now);
    TestOwnershipFenceSurvivesEmptyStoreReload(Path.Combine(testRoot, "success-empty-reload"), now);
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
           == QuestNodeCompletionAction.Defer,
        "an accepted objective with an incoherent unknown completion snapshot must defer, never skip");
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
    Assert(loop.Branch.Tick(context) == TreeSharp.RunStatus.Running && !loop.IsDone,
        "ForcedWhile must keep running without scheduling or completing its body while completion is unknown");
    loop.Branch.Stop(context);
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
    var manager = new QuestRecoveryManager(new FixedClock(now));
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

    var decisions = manager.ReportGeneratedFailures(
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
        Context());
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

static void TestOwnershipFenceSurvivesEmptyStoreReload(string settingsRoot, DateTime now)
{
    var environment = CreateEnvironment(settingsRoot, "Jeof", "Lordaeron");
    var key = QuestRecoveryKey.ForQuestStage(1208, QuestRecoveryStage.Pickup);
    var manager = new QuestRecoveryManager(new FixedClock(now));
    manager.Configure(environment);
    var ownerA = manager.TryBeginAttempt(key, Context());
    manager.SetManualBlacklist(key.QuestId, true);
    manager.SetManualBlacklist(key.QuestId, false);
    Assert(manager.GetEntries().Count == 0,
        "the reload fixture must persist an empty record set after blacklist removal");
    manager.Flush();

    var reloaded = new QuestRecoveryManager(new FixedClock(now));
    reloaded.Configure(environment);
    var ownerB = reloaded.TryBeginAttempt(key, Context());
    Assert(ownerB.MayAttempt && ownerB.AttemptGeneration > ownerA.AttemptGeneration,
        "the ownership high-water mark must survive reload even when no record remains");
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

static QuestRecoveryContext Context(int playerLevel = 34) => new()
{
    PlayerLevel = playerLevel,
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
