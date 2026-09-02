using Bots.Quest.QuestOrder;
using Styx.Logic.Questing;
using Styx.Logic.Questing.Recovery;

try
{
    TestDecisionMatrix();
    TestZeroIdTitleAuthority();
    TestCompletionMustBeAuthoritative();
    TestDecisionSnapshotsMismatchEvidence();
    TestInteractionCyclesFormOneEpisode();
    TestWaitExecutionPlanHasNoEffects();
    TestRepeatedSameCyclePulsesDoNotIncrement();
    TestThreeDistinctCyclesFormOneEpisode();
    TestChangedEvidenceWithinAndAcrossCyclesResetsSequence();
    TestSuccessfulDecisionResetsCycleLifecycle();
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
        CompletedQuestCacheStatus.Valid, false).Action == QuestPickupDialogAction.Wait,
        "an empty target name must disable title authority");
}

static void TestCompletionMustBeAuthoritative()
{
    Assert(Decide(shown: 875, continueVisible: true, shownCompleted: true,
        completionStatus: CompletedQuestCacheStatus.Unknown).Action == QuestPickupDialogAction.RejectMismatch,
        "unknown completion authority must not advance another quest");
    Assert(Decide(shown: 875, continueVisible: true, shownCompleted: true,
        completionStatus: CompletedQuestCacheStatus.RefreshFailed).Action == QuestPickupDialogAction.RejectMismatch,
        "failed completion refresh must not advance another quest");
    Assert(Decide(shown: 875, rewardChoicesAvailable: true, shownCompleted: true).Action
        == QuestPickupDialogAction.AdvanceCompletedQuest,
        "an authoritative completed quest may advance through reward selection");
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
    CompletedQuestCacheStatus completionStatus = CompletedQuestCacheStatus.Valid) =>
    QuestPickupDialogPolicy.Decide(
        target, targetName, shown, shownName, 123, new uint[] { 867, 875 },
        accept, continueVisible, completeVisible, rewardChoicesAvailable,
        completionStatus, shownCompleted);

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
