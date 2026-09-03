using Bots.Quest.QuestOrder;
using Styx;
using Styx.Logic.Pathing;
using Styx.Logic.Questing;
using Styx.Logic.Questing.Recovery;
using Styx.WoWInternals.WoWObjects;

try
{
    TestDecisionMatrix();
    TestZeroIdTitleAuthority();
    TestTitleFallbackRequiresUniqueSelection();
    TestCompletionMustBeAuthoritative();
    TestShownQuestCompletionUsesLiveOrCacheAuthority();
    TestDecisionSnapshotsMismatchEvidence();
    TestInteractionCyclesFormOneEpisode();
    TestWaitExecutionPlanHasNoEffects();
    TestRepeatedSameCyclePulsesDoNotIncrement();
    TestThreeDistinctCyclesFormOneEpisode();
    TestChangedEvidenceWithinAndAcrossCyclesResetsSequence();
    TestSuccessfulDecisionResetsCycleLifecycle();
    TestWaitPreservesMismatchLifecycleAcrossCycles();
    TestWaitAfterUnavailablePreservesFailureOutcome();
    TestAuthorizedAdvanceResetsMismatchLifecycle();
    TestOutcomeIsTaggedAndConsumedOnceByItsProducingInteraction();
    TestLoadedOfferListMissingTargetIsNotUnavailableAmbiguity();
    Console.WriteLine("Quest pickup policy regression tests passed.");
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    global::System.Environment.ExitCode = 1;
}

static void TestDecisionMatrix()
{
    Assert(Decide(shown: 867, accept: true).Action == QuestPickupDialogAction.RejectMismatch,
        "unrelated quest must not be accepted");
    Assert(Decide(shown: 876, accept: true).Action == QuestPickupDialogAction.AcceptTarget,
        "target must be accepted");
    Assert(Decide(shown: 875, continueVisible: true, shownCompleted: true).Action
        == QuestPickupDialogAction.AdvanceCompletedQuest,
        "completed blocker may be advanced");
    Assert(Decide(shown: 867, continueVisible: true).Action
        == QuestPickupDialogAction.RejectMismatch,
        "incomplete blocker must not be advanced");
    Assert(Decide(shown: 0, shownName: "", accept: true).Action == QuestPickupDialogAction.Wait,
        "a visible Accept button without quest identity must wait");
}

static void TestZeroIdTitleAuthority()
{
    Assert(Decide(shown: 0, shownName: "  A Final Blow  ", accept: true).Action
        == QuestPickupDialogAction.AcceptTarget,
        "an exact trimmed title may identify a zero-ID target");
    Assert(Decide(shown: 0, shownName: "A final blow", accept: true).Action
        == QuestPickupDialogAction.RejectMismatch,
        "title matching must be exact rather than case-insensitive or fuzzy");
    Assert(Decide(shown: 867, shownName: "A Final Blow", accept: true).Action
        == QuestPickupDialogAction.RejectMismatch,
        "a nonzero shown ID must outrank a matching title");
    Assert(QuestPickupDialogPolicy.Decide(
        876, "", 0, "", 123, Array.Empty<uint>(), true, false, false, false,
        shownQuestCompletionKnown: true, shownQuestCompleted: false,
        shownTitleUniquelyResolved: false).Action == QuestPickupDialogAction.Wait,
        "an empty target name must disable title authority");
    Assert(Decide(shown: 0, shownName: "A Final Blow", accept: true, titleUnique: false).Action
           == QuestPickupDialogAction.RejectMismatch,
        "a zero-ID title without uniqueness proof must never accept the target");
}

static void TestTitleFallbackRequiresUniqueSelection()
{
    Assert(QuestPickupDialogPolicy.TryFindUniqueExactTitleIndex(
               new[] { "Raptor Thieves", "  A Final Blow  ", "Thwarting Kolkar Aggression" },
               "A Final Blow",
               out var uniqueIndex)
           && uniqueIndex == 1,
        "one exact trimmed title must select its gossip index and prove uniqueness");
    Assert(!QuestPickupDialogPolicy.TryFindUniqueExactTitleIndex(
               new[] { "A Final Blow", "  A Final Blow  ", "Raptor Thieves" },
               "A Final Blow",
               out var duplicateIndex)
           && duplicateIndex == -1,
        "duplicate exact localized titles must not select the first gossip entry");
    Assert(!QuestPickupDialogPolicy.TryFindUniqueExactTitleIndex(
               new[] { "A final blow" },
               "A Final Blow",
               out _),
        "title selection must remain exact ordinal after trimming");
}

static void TestCompletionMustBeAuthoritative()
{
    Assert(Decide(shown: 875, continueVisible: true, shownCompleted: true,
        shownCompletionKnown: false).Action == QuestPickupDialogAction.RejectMismatch,
        "unknown completion authority must not advance another quest");
    Assert(Decide(shown: 875, continueVisible: true, shownCompleted: true,
        shownCompletionKnown: false).Action == QuestPickupDialogAction.RejectMismatch,
        "failed completion refresh must not advance another quest");
    Assert(Decide(shown: 875, rewardChoicesAvailable: true, shownCompleted: true).Action
        == QuestPickupDialogAction.AdvanceCompletedQuest,
        "an authoritative completed quest may advance through reward selection");
}

static void TestShownQuestCompletionUsesLiveOrCacheAuthority()
{
    Assert(Decide(shown: 875, continueVisible: true,
               shownCompletionKnown: true, shownCompleted: true,
               completionStatus: CompletedQuestCacheStatus.RefreshFailed).Action
           == QuestPickupDialogAction.AdvanceCompletedQuest,
        "a live accepted-complete quest must advance even when historical cache refresh failed");
    Assert(Decide(shown: 875, continueVisible: true,
               shownCompletionKnown: true, shownCompleted: false).Action
           == QuestPickupDialogAction.RejectMismatch,
        "an accepted-incomplete shown quest must not advance");
    Assert(Decide(shown: 875, continueVisible: true,
               shownCompletionKnown: true, shownCompleted: true).Action
           == QuestPickupDialogAction.AdvanceCompletedQuest,
        "a valid turned-in cache completion may advance a non-accepted shown quest");
    Assert(Decide(shown: 875, continueVisible: true,
               shownCompletionKnown: false, shownCompleted: false).Action
           == QuestPickupDialogAction.RejectMismatch,
        "unknown shown-quest completion must reject or wait safely");
}

static void TestDecisionSnapshotsMismatchEvidence()
{
    var offered = new List<uint> { 867, 875 };
    var decision = QuestPickupDialogPolicy.Decide(
        876, "A Final Blow", 867, "Raptor Thieves", 123, offered,
        true, false, false, false, CompletedQuestCacheStatus.Valid, false);
    offered[0] = 876;
    offered.Add(999);

    Assert(decision.TargetQuestId == 876 && decision.ShownQuestId == 867 && decision.GiverId == 123,
        "a mismatch decision must retain target, shown, and giver IDs");
    Assert(decision.OfferedQuestIds.SequenceEqual(new uint[] { 867, 875 }),
        "a mismatch decision must defensively snapshot offered quest IDs");
    Assert(decision.Reason == QuestFailureReason.PickupWrongQuestShown,
        "a mismatch decision must report PickupWrongQuestShown");
    Assert(decision.Evidence.Contains("target=876", StringComparison.Ordinal)
           && decision.Evidence.Contains("shown=867", StringComparison.Ordinal)
           && decision.Evidence.Contains("giver=123", StringComparison.Ordinal)
           && decision.Evidence.Contains("offered=[867,875]", StringComparison.Ordinal),
        "mismatch evidence must identify the target, shown quest, giver, and offered IDs");
}

static void TestInteractionCyclesFormOneEpisode()
{
    var decision = Decide(shown: 867, accept: true);
    var first = QuestPickupDialogPolicy.CreateMismatchOutcome(decision, 1);
    var second = QuestPickupDialogPolicy.CreateMismatchOutcome(decision, 2);
    var third = QuestPickupDialogPolicy.CreateMismatchOutcome(decision, 3);

    Assert(!first.IsFailureEpisode && !second.IsFailureEpisode,
        "the first two confirmed interaction cycles must remain observations");
    Assert(third.IsFailureEpisode,
        "three confirmed interaction cycles must form exactly one failure episode");
    Assert(third.Key.Equals(QuestRecoveryKey.ForNpc(876, QuestRecoveryStage.Pickup, 123))
           && third.Reason == QuestFailureReason.PickupWrongQuestShown
           && third.ObservedQuestId == 867
           && third.OfferedQuestIds.SequenceEqual(new uint[] { 867, 875 }),
        "the failure episode must retain the structured mismatch fields");
}

static void TestWaitExecutionPlanHasNoEffects()
{
    var plan = QuestPickupDialogExecutionPolicy.CreatePlan(
        Decide(shown: 0, shownName: "", accept: true),
        continueVisible: false,
        completeVisible: false,
        rewardChoicesAvailable: false);

    Assert(plan.KeepRunning && plan.Command == QuestPickupDialogCommand.None,
        "a loading Wait decision must keep running without a dialog command");
    Assert(!plan.CloseFrame && !plan.Accept && !plan.Continue
           && !plan.SelectReward && !plan.Complete,
        "a loading Wait decision must not close, accept, continue, select a reward, or complete");
}

static void TestRepeatedSameCyclePulsesDoNotIncrement()
{
    var tracker = new QuestPickupMismatchTracker();
    var mismatch867 = Decide(shown: 867, accept: true);
    var first = tracker.Observe(mismatch867, interactionCycleId: 10);
    var repeated = tracker.Observe(mismatch867, interactionCycleId: 10);

    Assert(tracker.ConfirmedCycles == 1 && !repeated.IsFailureEpisode,
        "repeated pulses in one interaction cycle must count once");
    Assert(ReferenceEquals(first, repeated) && ReferenceEquals(tracker.LastOutcome, repeated),
        "a repeated same-cycle pulse must preserve the existing structured outcome");
}

static void TestThreeDistinctCyclesFormOneEpisode()
{
    var tracker = new QuestPickupMismatchTracker();
    var mismatch = Decide(shown: 867, accept: true);

    var first = tracker.Observe(mismatch, interactionCycleId: 20);
    var second = tracker.Observe(mismatch, interactionCycleId: 21);
    var third = tracker.Observe(mismatch, interactionCycleId: 22);
    var repeatedThird = tracker.Observe(mismatch, interactionCycleId: 22);

    Assert(!first.IsFailureEpisode && !second.IsFailureEpisode && third.IsFailureEpisode,
        "three distinct confirmed interaction cycles must form one failure episode");
    Assert(tracker.ConfirmedCycles == 3 && tracker.PickupUnavailable,
        "the third distinct mismatch cycle must make pickup unavailable");
    Assert(ReferenceEquals(third, repeatedThird) && ReferenceEquals(tracker.LastOutcome, third),
        "later pulses in the third cycle must not create another failure episode");
}

static void TestChangedEvidenceWithinAndAcrossCyclesResetsSequence()
{
    var tracker = new QuestPickupMismatchTracker();
    var mismatch867 = Decide(shown: 867, accept: true);
    var mismatch875 = Decide(shown: 875, accept: true);

    tracker.Observe(mismatch867, interactionCycleId: 30);
    var changedWithinCycle = tracker.Observe(mismatch875, interactionCycleId: 30);
    Assert(tracker.ConfirmedCycles == 1 && !changedWithinCycle.IsFailureEpisode,
        "changed evidence within one cycle must reset to one without counting the cycle twice");

    tracker.Observe(mismatch875, interactionCycleId: 31);
    Assert(tracker.ConfirmedCycles == 2,
        "unchanged evidence in the next interaction cycle must increment the reset sequence");

    var changedAcrossCycles = tracker.Observe(mismatch867, interactionCycleId: 32);
    Assert(tracker.ConfirmedCycles == 1 && !changedAcrossCycles.IsFailureEpisode,
        "changed evidence across interaction cycles must reset to one");
}

static void TestSuccessfulDecisionResetsCycleLifecycle()
{
    var tracker = new QuestPickupMismatchTracker();
    var mismatch = Decide(shown: 867, accept: true);

    tracker.Observe(mismatch, interactionCycleId: 40);
    tracker.Observe(mismatch, interactionCycleId: 41);
    var resetResult = tracker.Observe(Decide(shown: 876, accept: true), interactionCycleId: 41);
    Assert(resetResult == null && tracker.ConfirmedCycles == 0
           && !tracker.PickupUnavailable && tracker.LastOutcome == null,
        "a successful non-reject decision must clear mismatch outcome and availability state");

    var restarted = tracker.Observe(mismatch, interactionCycleId: 42);
    Assert(tracker.ConfirmedCycles == 1 && !restarted.IsFailureEpisode,
        "a mismatch after success must begin a new cycle sequence");
}

static void TestWaitPreservesMismatchLifecycleAcrossCycles()
{
    var tracker = new QuestPickupMismatchTracker();
    var mismatch = Decide(shown: 867, accept: true);
    var loading = Decide(shown: 0, shownName: "", accept: true);
    var first = tracker.Observe(mismatch, interactionCycleId: 50);

    var whileLoading = tracker.Observe(loading, interactionCycleId: 51);
    Assert(tracker.ConfirmedCycles == 1 && ReferenceEquals(tracker.LastOutcome, first)
           && ReferenceEquals(whileLoading, first) && !tracker.PickupUnavailable,
        "a loading Wait in a later interaction cycle must preserve mismatch lifecycle state");

    var second = tracker.Observe(mismatch, interactionCycleId: 52);
    var third = tracker.Observe(mismatch, interactionCycleId: 53);
    Assert(!second.IsFailureEpisode && third.IsFailureEpisode
           && tracker.ConfirmedCycles == 3 && tracker.PickupUnavailable,
        "three actual mismatch cycles must form one episode despite intervening Wait pulses");
}

static void TestWaitAfterUnavailablePreservesFailureOutcome()
{
    var tracker = new QuestPickupMismatchTracker();
    var mismatch = Decide(shown: 867, accept: true);
    tracker.Observe(mismatch, interactionCycleId: 60);
    tracker.Observe(mismatch, interactionCycleId: 61);
    var failed = tracker.Observe(mismatch, interactionCycleId: 62);

    var afterFailureWait = tracker.Observe(
        Decide(shown: 0, shownName: "", accept: true),
        interactionCycleId: 63);
    Assert(tracker.ConfirmedCycles == 3 && tracker.PickupUnavailable
           && ReferenceEquals(tracker.LastOutcome, failed)
           && ReferenceEquals(afterFailureWait, failed),
        "Wait after pickup becomes unavailable must not silently clear its failure outcome");

    tracker.Reset();
    Assert(tracker.ConfirmedCycles == 0 && !tracker.PickupUnavailable && tracker.LastOutcome == null,
        "an explicit lifecycle reset must clear an unavailable pickup outcome");
}

static void TestAuthorizedAdvanceResetsMismatchLifecycle()
{
    var tracker = new QuestPickupMismatchTracker();
    tracker.Observe(Decide(shown: 867, accept: true), interactionCycleId: 70);
    var advance = Decide(shown: 875, continueVisible: true, shownCompleted: true);

    Assert(advance.Action == QuestPickupDialogAction.AdvanceCompletedQuest,
        "the reset fixture must be an authorized completed-quest advance");
    var result = tracker.Observe(advance, interactionCycleId: 71);
    Assert(result == null && tracker.ConfirmedCycles == 0
           && !tracker.PickupUnavailable && tracker.LastOutcome == null,
        "an affirmative authorized advance must reset mismatch lifecycle state");
}

static void TestOutcomeIsTaggedAndConsumedOnceByItsProducingInteraction()
{
    var pickup = new ForcedQuestPickUp(
        876, "A Final Blow", 123, "Giver", WoWPoint.Zero, null);
    var interact = typeof(ForcedQuestPickUp).GetMethod(
        "InteractWithQuestGiver",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    var record = typeof(ForcedQuestPickUp).GetMethod(
        "RecordPickupDecision",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    Assert(interact != null && record != null,
        "the real interaction path must own explicit cycle-start and result-publication boundaries");

    for (long expectedCycle = 1; expectedCycle <= 3; expectedCycle++)
    {
        try
        {
            interact!.Invoke(pickup, new object?[] { null });
            throw new InvalidOperationException("the null-giver fixture must stop after the real cycle start");
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is NullReferenceException)
        {
        }
        Assert(pickup.InteractionCycleId == expectedCycle
               && !pickup.TryConsumeOutcome(out _),
            "the real giver interaction must clear prior output before the following 1.5-second dialog wait");
        record!.Invoke(pickup, new object[] { Decide(shown: 867, accept: true) });
        Assert(pickup.TryConsumeOutcome(out var result)
               && result.InteractionCycleId == expectedCycle
               && result.IsFailureEpisode == (expectedCycle == 3),
            "the produced dialog result must be tagged with its exact interaction and fail only on cycle three");
        Assert(!pickup.TryConsumeOutcome(out _),
            "an outer pulse must consume a tagged pickup outcome at most once");
    }
}

static void TestLoadedOfferListMissingTargetIsNotUnavailableAmbiguity()
{
    var loadedMissing = QuestPickupDialogPolicy.Decide(
        876, "A Final Blow", 0, "", 123, new uint[] { 867, 875 },
        acceptVisible: false,
        continueVisible: false,
        completeQuestVisible: false,
        rewardChoicesAvailable: false,
        shownQuestCompletionKnown: false,
        shownQuestCompleted: false,
        shownTitleUniquelyResolved: false,
        offeredQuestListLoaded: true);
    var unknown = QuestPickupDialogPolicy.Decide(
        876, "A Final Blow", 0, "", 123, Array.Empty<uint>(),
        acceptVisible: false,
        continueVisible: false,
        completeQuestVisible: false,
        rewardChoicesAvailable: false,
        shownQuestCompletionKnown: false,
        shownQuestCompleted: false,
        shownTitleUniquelyResolved: false,
        offeredQuestListLoaded: false);

    Assert(loadedMissing.Action == QuestPickupDialogAction.RejectMismatch
           && loadedMissing.Reason == QuestFailureReason.PickupTargetNotOffered,
        "a positively loaded gossip/native list missing the target must produce PickupTargetNotOffered");
    Assert(unknown.Action == QuestPickupDialogAction.Wait
           && unknown.Reason == QuestFailureReason.None,
        "an unknown, unloaded, or ambiguous list must remain Wait");

    var tracker = new QuestPickupMismatchTracker();
    var outcome = tracker.Observe(loadedMissing, interactionCycleId: 91);
    Assert(outcome.Reason == QuestFailureReason.PickupTargetNotOffered
           && outcome.InteractionCycleId == 91
           && outcome.OfferedQuestIds.SequenceEqual(new uint[] { 867, 875 }),
        "the loaded-list result must use the same tagged structured outcome mechanism as a shown-quest mismatch");
}

static QuestPickupDialogDecision Decide(
    uint target = 876,
    uint shown = 867,
    string targetName = "A Final Blow",
    string shownName = "Raptor Thieves",
    bool accept = false,
    bool continueVisible = false,
    bool completeVisible = false,
    bool rewardChoicesAvailable = false,
    bool shownCompleted = false,
    CompletedQuestCacheStatus completionStatus = CompletedQuestCacheStatus.Valid,
    bool? shownCompletionKnown = null,
    bool titleUnique = true) =>
    QuestPickupDialogPolicy.Decide(
        target, targetName, shown, shownName, 123, new uint[] { 867, 875 },
        accept, continueVisible, completeVisible, rewardChoicesAvailable,
        shownCompletionKnown ?? completionStatus == CompletedQuestCacheStatus.Valid,
        shownCompleted,
        titleUnique);

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
