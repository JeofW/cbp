using Styx.Logic.Questing.Recovery;
using WholesomeAQ;

var utcNow = new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc);

try
{
    TestStagePriority();
    TestStageSpecificCooldown();
    TestEndpointCooldownIsolation();
    TestEndpointAttemptCap();
    TestOrdinaryWorkPrecedesHalfOpenProbe();
    TestNoWorkReturnsEarliestRetry();
    TestCandidateTieBreakers();
    TestValidatedGrindFallbackRequiresVettedPath();
    Console.WriteLine("Wholesome scheduling policy regression tests passed.");
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    global::System.Environment.ExitCode = 1;
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
