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
    Console.WriteLine("Wholesome scheduler recovery regression tests passed.");
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
