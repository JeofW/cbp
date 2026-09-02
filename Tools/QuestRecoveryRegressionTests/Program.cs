using Styx.Logic.Questing.Recovery;

var now = new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);
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

var manualObjective = new QuestRecoveryRecord
{
    Key = objectiveKey,
    State = QuestRecoveryState.ManualBlacklist
};
var completedObjective = new QuestRecoveryRecord
{
    Key = objectiveKey,
    State = QuestRecoveryState.Completed
};
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

Console.WriteLine("Quest recovery policy regression tests passed.");

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
