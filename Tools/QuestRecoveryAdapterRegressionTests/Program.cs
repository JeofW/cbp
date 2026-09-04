using Styx.Bot.Quest_Behaviors;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Profiles.Quest;
using Styx.Logic.Questing;
using Styx.Logic.Questing.Recovery;
using System;
using System.Collections.Generic;
using System.Linq;
using TreeSharp;

TestOutcomeFactories();
TestEndpointQuantization();
TestDeniedClaimNeverClearsWinnerPoi();
TestCandidateOrderingDedupAndCap();
TestLiveGiverSurvivesMissingDatabase();
TestUnknownCompletionAndDeferredChildPauseAllWork();
TestNaturalMismatchAdvancesToAlternateAndAlternateCanSucceed();
TestInteractionCyclesSpanChildReplacementAndBeatTimeout();
TestTerminalAndStaleCleanupUseExactPoiIdentity();

Console.WriteLine("Quest recovery adapter regression tests passed.");

static void TestOutcomeFactories()
{
    var attemptKey = QuestRecoveryKey.ForQuestStage(876, QuestRecoveryStage.Pickup);
    var failed = SafePickUp.CreateUnavailableOutcome(
        876, 867, 3338, new uint[] { 867, 875 },
        QuestFailureReason.PickupWrongQuestShown,
        "target=876; shown=867; giver=3338; offered=[867,875]",
        true, 3, attemptKey, 41);
    Assert(failed.Key.Equals(QuestRecoveryKey.ForNpc(876, QuestRecoveryStage.Pickup, 3338))
           && failed.AttemptKey.Equals(attemptKey)
           && failed.AttemptGeneration == 41
           && failed.Kind == QuestAttemptOutcomeKind.Failure
           && failed.ObservedQuestId == 867
           && failed.OfferedQuestIds.SequenceEqual(new uint[] { 867, 875 })
           && failed.InteractionCycleId == 3,
        "the terminal pickup mismatch must preserve the exact narrow evidence and owner");

    var sample = SafePickUp.CreateUnavailableOutcome(
        876, 0, 3391, Array.Empty<uint>(),
        QuestFailureReason.PickupTargetNotOffered,
        "sample", false, 1, attemptKey, 41);
    Assert(sample.Kind == QuestAttemptOutcomeKind.Observation && !sample.IsFailureEpisode,
        "pre-boundary pickup mismatches must remain observations");
}

static void TestEndpointQuantization()
{
    var a = SafePickUp.CreateEndpointKey(876, 1, new WoWPoint(79.1f, -0.1f, 5));
    var b = SafePickUp.CreateEndpointKey(876, 1, new WoWPoint(79.9f, -79.9f, 99));
    var c = SafePickUp.CreateEndpointKey(876, 1, new WoWPoint(80.0f, -0.1f, 5));
    Assert(a.Equals(b), "small movement inside an 80-yard map cell must preserve the endpoint key");
    Assert(!a.Equals(c), "crossing an 80-yard cell boundary must produce a distinct endpoint key");
    Assert(a.Endpoint == "cell:0:-1" && a.MapId == 1,
        "pickup endpoint identity must use the scheduler's deterministic map/cell format");
}

static void TestDeniedClaimNeverClearsWinnerPoi()
{
    var runtime = new FakeRuntime { ClaimAllowed = false };
    var winnerPoi = PickupPoi(876, 3338, new WoWPoint(10, 10, 0));
    runtime.CurrentPoi = winnerPoi;
    var behavior = NewBehavior(runtime);
    behavior.OnStart();
    Assert(behavior.IsDone && ReferenceEquals(runtime.CurrentPoi, winnerPoi) && runtime.ClearCount == 0,
        "a denied SafePickUp must never clear a concurrent winner's same-quest POI");
}

static void TestCandidateOrderingDedupAndCap()
{
    var runtime = new FakeRuntime();
    runtime.LiveGivers.Add(new SafePickUpGiverCandidate(3338, "live", new WoWPoint(1, 1, 0), QuestObjectType.Npc, "live"));
    runtime.DatabaseGiver = new SafePickUpGiverCandidate(3338, "db", new WoWPoint(81, 1, 0), QuestObjectType.Npc, "db");
    var behavior = NewBehavior(runtime,
        "4001,2,2,0;4002,161,1,0;4003,241,1,0;4004,321,1,0;4005,401,1,0;4006,481,1,0");
    behavior.OnStart();

    for (var index = 0; index < 5 && !behavior.IsDone; index++)
    {
        runtime.Now = runtime.Now.AddSeconds(31);
        behavior.TickForTesting();
    }

    Assert(runtime.CreatedCandidates.Select(candidate => candidate.Entry)
            .SequenceEqual(new uint[] { 3338, 3338, 4002, 4003, 4004 }),
        "candidate order must be live, primary DB, then valid alternates, deduped and capped at five cells");
    Assert(runtime.CreatedCandidates.Count == 5,
        "one pickup episode must navigate at most five distinct endpoint cells");
}

static void TestLiveGiverSurvivesMissingDatabase()
{
    var runtime = new FakeRuntime { ThrowDatabaseLookup = true };
    runtime.LiveGivers.Add(new SafePickUpGiverCandidate(3338, "live", new WoWPoint(15, 5, 0), QuestObjectType.Npc, "live"));
    var behavior = NewBehavior(runtime);
    behavior.OnStart();
    Assert(!behavior.IsDone && runtime.CreatedCandidates.Count == 1 && runtime.Batches.Count == 0,
        "a live matching object must remain usable when the local NPC database is missing");
}

static void TestUnknownCompletionAndDeferredChildPauseAllWork()
{
    var runtime = new FakeRuntime
    {
        Completion = new QuestCompletionSnapshot(false, QuestCompletionState.Unknown),
        DatabaseGiver = new SafePickUpGiverCandidate(3338, "db", new WoWPoint(100, 0, 0), QuestObjectType.Npc, "db")
    };
    var behavior = NewBehavior(runtime);
    behavior.OnStart();
    runtime.Now = runtime.Now.AddMinutes(10);
    behavior.TickForTesting();
    Assert(runtime.CreatedCandidates.Count == 0 && runtime.Reports.Count == 0 && runtime.Batches.Count == 0,
        "unknown completion authority must defer candidate creation and every negative outcome");

    runtime.Completion = new QuestCompletionSnapshot(false, QuestCompletionState.KnownIncomplete);
    behavior.TickForTesting();
    var child = runtime.Children.Single();
    var ticksBeforeDeferral = child.TickCount;
    child.ExecutionDeferred = true;
    behavior.TickForTesting();
    runtime.Now = runtime.Now.AddMinutes(10);
    behavior.TickForTesting();
    Assert(child.TickCount == ticksBeforeDeferral && runtime.Batches.Count == 0,
        "a ForcedQuestPickUp execution deferral must pause interactions, timers, and reports");

    child.ExecutionDeferred = false;
    behavior.TickForTesting();
    Assert(runtime.CreatedCandidates.Count == 1 && runtime.Batches.Count == 0,
        "resuming after deferral must restore the timer budget instead of immediately timing out");
}

static void TestInteractionCyclesSpanChildReplacementAndBeatTimeout()
{
    var runtime = new FakeRuntime { PlayerLocation = new WoWPoint(1, 1, 0) };
    runtime.LiveGivers.Add(new SafePickUpGiverCandidate(3338, "live", new WoWPoint(1, 1, 0), QuestObjectType.Npc, "live"));
    runtime.DatabaseGiver = new SafePickUpGiverCandidate(3338, "db", new WoWPoint(161, 1, 0), QuestObjectType.Npc, "db");
    var behavior = NewBehavior(runtime, "3391,321,1,0");
    behavior.OnStart();
    behavior.TickForTesting();

    var first = runtime.Children[0];
    runtime.Now = runtime.Now.AddSeconds(10);
    first.Outcomes.Enqueue(Mismatch(876, 3338, 1));
    behavior.TickForTesting();
    Assert(runtime.CreatedCandidates.Count == 2 && !behavior.IsDone && runtime.Batches.Count == 0,
        "the first real mismatch must beat an expired silence timer and deliberately advance to the next candidate");

    var second = runtime.Children[1];
    second.Outcomes.Enqueue(Mismatch(876, 3338, 1));
    behavior.TickForTesting();
    Assert(runtime.CreatedCandidates.Count == 3 && !behavior.IsDone,
        "two natural mismatch cycles across two children must advance without resetting the shared boundary");

    var third = runtime.Children[2];
    third.Outcomes.Enqueue(Mismatch(876, 3391, 1));
    behavior.TickForTesting();
    Assert(behavior.IsDone && runtime.Batches.Count == 1,
        "the third distinct real cycle across child replacements must end the single outer episode");
    Assert(runtime.Reports.Count(outcome => !outcome.IsFailureEpisode) == 3,
        "each real pre-terminal dialog result is retained as a non-episode sample");
    Assert(runtime.Batches[0].Last().Reason is QuestFailureReason.PickupTargetNotOffered,
        "the three-cycle boundary must report the literal pickup-unavailable reason");
    Assert(runtime.Batches[0].Any(outcome => outcome.Key.NpcEntry == 3338)
           && runtime.Batches[0].Any(outcome => outcome.Key.NpcEntry == 3391),
        "the terminal batch must retain narrow relation evidence for the tried primary and alternate givers");
}

static void TestNaturalMismatchAdvancesToAlternateAndAlternateCanSucceed()
{
    var runtime = new FakeRuntime { PlayerLocation = new WoWPoint(1, 1, 0) };
    runtime.LiveGivers.Add(new SafePickUpGiverCandidate(
        3338, "primary", new WoWPoint(1, 1, 0), QuestObjectType.Npc, "live"));
    var behavior = NewBehavior(runtime, "3391,161,1,0");
    behavior.OnStart();

    var primary = runtime.Children.Single();
    var primaryPoi = PickupPoi(876, 3338, new WoWPoint(1, 1, 0));
    primary.OnTick = () => runtime.CurrentPoi = primaryPoi;
    behavior.TickForTesting();
    primary.Outcomes.Enqueue(PickupDialogOutcome(
        876, 3338, 1, QuestFailureReason.PickupWrongQuestShown, 867));
    behavior.TickForTesting();

    Assert(runtime.CreatedCandidates.Select(candidate => candidate.Entry)
            .SequenceEqual(new uint[] { 3338, 3391 }),
        "a natural primary mismatch must advance directly to the next ordered alternate without a timeout");
    Assert(primary.DisposeCount == 1 && runtime.ClearCount == 1,
        "natural candidate replacement must dispose the primary child and clear only its exact owned POI");

    var alternate = runtime.Children[1];
    var alternatePoi = PickupPoi(876, 3391, new WoWPoint(161, 1, 0));
    alternate.OnTick = () => runtime.CurrentPoi = alternatePoi;
    behavior.TickForTesting();
    alternate.Outcomes.Enqueue(PickupDialogOutcome(
        876, 3391, 1, QuestFailureReason.PickupTargetNotOffered, 0));
    behavior.TickForTesting();
    Assert(!behavior.IsDone && runtime.CreatedCandidates.Count == 2,
        "the second distinct outer cycle may continue on the last alternate without resetting the shared count");

    runtime.Completion = new QuestCompletionSnapshot(true, QuestCompletionState.KnownIncomplete);
    behavior.TickForTesting();
    behavior.Dispose();
    QuestAttemptOutcome success = runtime.Reports.Last();
    Assert(behavior.IsDone
           && success.Kind == QuestAttemptOutcomeKind.Success
           && success.Key.Equals(QuestRecoveryKey.ForQuestStage(876, QuestRecoveryStage.Pickup))
           && success.AttemptGeneration == 41,
        "a valid alternate pickup must close the exact stage generation with Success");
    Assert(runtime.Batches.Count == 0
           && runtime.Reports.Count(outcome => outcome.IsFailureEpisode) == 0
           && runtime.AbandonCount == 0
           && alternate.DisposeCount == 1
           && runtime.ClearCount == 2,
        "alternate success must discard queued prior-candidate failures as episodes and clean up without neutral abandon");
}

static void TestTerminalAndStaleCleanupUseExactPoiIdentity()
{
    var runtime = new FakeRuntime();
    runtime.LiveGivers.Add(new SafePickUpGiverCandidate(3338, "live", new WoWPoint(1, 1, 0), QuestObjectType.Npc, "live"));
    var behavior = NewBehavior(runtime);
    behavior.OnStart();
    var child = runtime.Children.Single();
    var installed = PickupPoi(876, 3338, new WoWPoint(1, 1, 0));
    child.OnTick = () => runtime.CurrentPoi = installed;
    behavior.TickForTesting();
    Assert(ReferenceEquals(runtime.CurrentPoi, installed), "the fixture must install the child's exact POI instance");

    var winner = PickupPoi(876, 3338, new WoWPoint(2, 2, 0));
    runtime.OnBatch = () => runtime.CurrentPoi = winner;
    child.Outcomes.Enqueue(Mismatch(876, 3338, 1));
    child.Outcomes.Enqueue(Mismatch(876, 3338, 2));
    child.Outcomes.Enqueue(Mismatch(876, 3338, 3));
    behavior.TickForTesting();
    behavior.TickForTesting();
    runtime.AcceptBatch = false;
    behavior.TickForTesting();
    Assert(behavior.IsDone && ReferenceEquals(runtime.CurrentPoi, winner) && runtime.ClearCount == 0,
        "stale terminal rejection must never clear a winner's replacement same-quest POI");

    var runtimeAccepted = new FakeRuntime();
    runtimeAccepted.LiveGivers.Add(new SafePickUpGiverCandidate(3338, "live", new WoWPoint(1, 1, 0), QuestObjectType.Npc, "live"));
    var accepted = NewBehavior(runtimeAccepted);
    accepted.OnStart();
    var acceptedPoi = PickupPoi(876, 3338, new WoWPoint(1, 1, 0));
    runtimeAccepted.Children[0].OnTick = () => runtimeAccepted.CurrentPoi = acceptedPoi;
    accepted.TickForTesting();
    runtimeAccepted.Children[0].Outcomes.Enqueue(Mismatch(876, 3338, 1));
    runtimeAccepted.Children[0].Outcomes.Enqueue(Mismatch(876, 3338, 2));
    runtimeAccepted.Children[0].Outcomes.Enqueue(Mismatch(876, 3338, 3));
    accepted.TickForTesting();
    accepted.TickForTesting();
    accepted.TickForTesting();
    accepted.Dispose();
    Assert(accepted.IsDone && runtimeAccepted.ClearCount == 1 && runtimeAccepted.AbandonCount == 0,
        "an accepted terminal outcome must clear its exact installed POI and release ownership without neutral abandon");

    var runtime2 = new FakeRuntime();
    runtime2.LiveGivers.Add(new SafePickUpGiverCandidate(3338, "live", new WoWPoint(1, 1, 0), QuestObjectType.Npc, "live"));
    var behavior2 = NewBehavior(runtime2);
    behavior2.OnStart();
    var ownedPoi = PickupPoi(876, 3338, new WoWPoint(1, 1, 0));
    runtime2.Children[0].OnTick = () => runtime2.CurrentPoi = ownedPoi;
    behavior2.TickForTesting();
    behavior2.Dispose();
    Assert(runtime2.ClearCount == 1 && runtime2.CurrentPoi.Type == PoiType.None && runtime2.AbandonCount == 1,
        "neutral disposal must clear only its exact installed POI and release only its exact attempt");
}

static QuestAttemptOutcome Mismatch(uint questId, uint giverId, long cycle) => new()
{
    Key = QuestRecoveryKey.ForNpc(questId, QuestRecoveryStage.Pickup, giverId),
    Kind = QuestAttemptOutcomeKind.Observation,
    Reason = QuestFailureReason.PickupTargetNotOffered,
    IsFailureEpisode = false,
    InteractionCycleId = cycle,
    Evidence = $"giver={giverId}; cycle={cycle}",
    OfferedQuestIds = Array.Empty<uint>()
};

static QuestAttemptOutcome PickupDialogOutcome(
    uint questId,
    uint giverId,
    long cycle,
    QuestFailureReason reason,
    uint shownQuestId) => new()
{
    Key = QuestRecoveryKey.ForNpc(questId, QuestRecoveryStage.Pickup, giverId),
    Kind = QuestAttemptOutcomeKind.Observation,
    Reason = reason,
    IsFailureEpisode = false,
    InteractionCycleId = cycle,
    Evidence = $"target={questId}; shown={shownQuestId}; giver={giverId}",
    ObservedQuestId = shownQuestId,
    OfferedQuestIds = shownQuestId == 0 ? Array.Empty<uint>() : new uint[] { shownQuestId }
};

static SafePickUp NewBehavior(FakeRuntime runtime, string alternates = "")
{
    var args = new Dictionary<string, string>
    {
        ["QuestId"] = "876",
        ["QuestName"] = "Test quest",
        ["GiverId"] = "3338",
        ["GiverName"] = "Test giver",
        ["TimeoutSeconds"] = "5",
        ["NavigationTimeoutSeconds"] = "30",
        ["ArrivalDistance"] = "15"
    };
    if (!string.IsNullOrEmpty(alternates))
        args["AlternateGivers"] = alternates;
    return new SafePickUp(args, runtime);
}

static BotPoi PickupPoi(uint questId, uint giverId, WoWPoint location) => new(
    new PickUpNode(location, giverId, "giver", QuestObjectType.Npc, questId, "quest"));

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class FakeRuntime : SafePickUpRuntime
{
    public DateTime Now { get; set; } = new(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);
    public bool ClaimAllowed { get; set; } = true;
    public bool AcceptBatch { get; set; } = true;
    public bool ThrowDatabaseLookup { get; set; }
    public QuestCompletionSnapshot Completion { get; set; } = new(false, QuestCompletionState.KnownIncomplete);
    public WoWPoint PlayerLocation { get; set; } = new(1000, 1000, 0);
    public int MapId { get; set; } = 1;
    public BotPoi CurrentPoi { get; set; } = new(PoiType.None);
    public int ClearCount { get; private set; }
    public int AbandonCount { get; private set; }
    public List<SafePickUpGiverCandidate> LiveGivers { get; } = new();
    public SafePickUpGiverCandidate DatabaseGiver { get; set; }
    public List<SafePickUpGiverCandidate> CreatedCandidates { get; } = new();
    public List<FakeChild> Children { get; } = new();
    public List<QuestAttemptOutcome> Reports { get; } = new();
    public List<IReadOnlyList<QuestAttemptOutcome>> Batches { get; } = new();
    public System.Action OnBatch { get; set; }

    public override DateTime UtcNow => Now;
    public override WoWPoint CurrentPlayerLocation => PlayerLocation;
    public override int CurrentMapId => MapId;
    public override BotPoi CurrentBotPoi => CurrentPoi;
    public override void EnsureConfigured() { }
    public override QuestRecoveryContext CaptureContext() => new() { PlayerLevel = 34 };
    public override QuestRecoveryDecision TryBeginAttempt(QuestRecoveryKey key, QuestRecoveryContext context) => new()
    {
        MayAttempt = ClaimAllowed,
        State = ClaimAllowed ? QuestRecoveryState.Attempting : QuestRecoveryState.CoolingDown,
        AttemptGeneration = ClaimAllowed ? 41 : 0,
        Status = ClaimAllowed ? "owned" : "denied"
    };
    public override QuestCompletionSnapshot GetQuestCompletionSnapshot(uint questId) => Completion;
    public override IReadOnlyList<SafePickUpGiverCandidate> FindLiveGivers(uint giverId) => LiveGivers;
    public override SafePickUpGiverCandidate FindDatabaseGiver(uint giverId, string giverName)
    {
        if (ThrowDatabaseLookup)
            throw new InvalidOperationException("database unavailable");
        return DatabaseGiver;
    }
    public override ISafePickUpChild CreateChild(uint questId, string questName, SafePickUpGiverCandidate candidate)
    {
        CreatedCandidates.Add(candidate);
        var child = new FakeChild();
        Children.Add(child);
        return child;
    }
    public override QuestRecoveryDecision Report(QuestAttemptOutcome outcome, QuestRecoveryContext context)
    {
        Reports.Add(outcome);
        return new QuestRecoveryDecision { State = QuestRecoveryState.Attempting, MayAttempt = true, AttemptGeneration = 41 };
    }
    public override bool TryReportGeneratedFailures(IReadOnlyList<QuestAttemptOutcome> outcomes, QuestRecoveryContext context, out IReadOnlyList<QuestRecoveryDecision> decisions)
    {
        Batches.Add(outcomes.ToArray());
        OnBatch?.Invoke();
        decisions = Array.Empty<QuestRecoveryDecision>();
        return AcceptBatch;
    }
    public override bool OwnsAttempt(QuestRecoveryKey key, long generation) => ClaimAllowed && Batches.Count == 0;
    public override void AbandonAttempt(QuestRecoveryKey key, long generation) => AbandonCount++;
    public override void ClearBotPoi(string reason)
    {
        ClearCount++;
        CurrentPoi = new BotPoi(PoiType.None);
    }
}

sealed class FakeChild : ISafePickUpChild
{
    public Queue<QuestAttemptOutcome> Outcomes { get; } = new();
    public bool Done { get; set; }
    public bool ExecutionDeferred { get; set; }
    public int TickCount { get; private set; }
    public int DisposeCount { get; private set; }
    public System.Action OnTick { get; set; }
    public bool IsDone => Done;
    public bool IsExecutionDeferred => ExecutionDeferred;
    public void OnStart() { }
    public RunStatus Tick(object context)
    {
        TickCount++;
        OnTick?.Invoke();
        return RunStatus.Running;
    }
    public bool TryConsumeOutcome(out QuestAttemptOutcome outcome)
    {
        outcome = Outcomes.Count == 0 ? null : Outcomes.Dequeue();
        return outcome != null;
    }
    public void Dispose() => DisposeCount++;
}
