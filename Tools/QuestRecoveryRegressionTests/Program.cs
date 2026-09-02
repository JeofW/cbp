using Styx.Logic.Questing.Recovery;

var now = new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);
var testRoot = Path.Combine(AppContext.BaseDirectory, "quest-recovery-test-data");
ResetDirectory(testRoot);

try
{
    RunPolicyRegressions(now);
    TestStoreRoundTripAndAtomicReplacement(Path.Combine(testRoot, "store"), now);
    TestCorruptStoreQuarantine(Path.Combine(testRoot, "corrupt"));
    TestPersistenceAndIdentityIsolation(Path.Combine(testRoot, "manager"), now);
    TestPersistenceFailureCanRetry(Path.Combine(testRoot, "persistence-retry"), now);
    TestManagerRollingBudget(Path.Combine(testRoot, "rolling-budget"), now);
    TestLegacyMigrationIsIdempotent(Path.Combine(testRoot, "migration"), now);
    TestConcurrentReportingAndAttemptOwnership(Path.Combine(testRoot, "concurrency"), now);
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
                Evidence = new[]
                {
                    new QuestRecoveryEvidence
                    {
                        ObservedUtc = now,
                        Reason = QuestFailureReason.EndpointUnreachable,
                        Text = "unreachable"
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
    Assert(loadedRecord.Evidence.Single().Text == "unreachable", "store must round-trip evidence");
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

static void TestConcurrentReportingAndAttemptOwnership(string settingsRoot, DateTime now)
{
    var clock = new FixedClock(now);
    var manager = new QuestRecoveryManager(clock);
    manager.Configure(CreateEnvironment(settingsRoot, "Jeof", "Lordaeron"));
    var observedKey = QuestRecoveryKey.ForQuestStage(867, QuestRecoveryStage.Pickup);

    Parallel.For(0, 1_000, index =>
        manager.Report(
            QuestAttemptOutcome.Observation(observedKey, QuestFailureReason.NpcNotFoundInWorld, $"sample-{index}"),
            Context()));

    var observed = manager.GetEntries().Single();
    Assert(observed.EpisodeCount == 0, "timer observations must not create failure episodes");
    Assert(observed.Reason == QuestFailureReason.None,
        "timer observations must not replace the active episode's failure reason");
    Assert(observed.Evidence.Count == 10, "concurrent evidence must be bounded to the ten newest records");

    var attemptKey = QuestRecoveryKey.ForQuestStage(875, QuestRecoveryStage.Pickup);
    var decisions = new QuestRecoveryDecision[256];
    Parallel.For(0, decisions.Length, index => decisions[index] = manager.TryBeginAttempt(attemptKey, Context()));

    Assert(decisions.Count(decision => decision.MayAttempt) == 1,
        "exactly one concurrent caller must atomically own an attempt");
    Assert(manager.GetEntries().Count == 2 && manager.GetEntries().Single(entry => entry.Key.Equals(attemptKey)).State == QuestRecoveryState.Attempting,
        "attempt ownership must be represented by one synchronized record");

    manager.Report(
        QuestAttemptOutcome.Failure(attemptKey, QuestFailureReason.PickupTargetNotOffered, "probe failed"),
        Context());
    Assert(manager.GetEntries().Single(entry => entry.Key.Equals(attemptKey)).EpisodeCount == 1,
        "failure by an attempt owner must count one episode");
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
