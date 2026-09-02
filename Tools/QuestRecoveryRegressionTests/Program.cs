using Styx.Logic.Questing.Recovery;

var now = new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);
var key = QuestRecoveryKey.ForQuestStage(867, QuestRecoveryStage.Pickup);
var record = QuestRecoveryRecord.Create(key);

var first = QuestRecoveryPolicy.ApplyFailure(record, QuestFailureReason.PickupTargetNotOffered, now);
Assert(first.State == QuestRecoveryState.CoolingDown, "first pickup failure must cool down");
Assert(first.CooldownUntilUtc == now.AddMinutes(15), "pickup cooldown must be 15 minutes");

var observation = QuestRecoveryPolicy.ApplyFailure(first, QuestFailureReason.PickupTargetNotOffered, now.AddMinutes(1));
Assert(observation.EpisodeCount == 1, "repeated observations must retain the same episode");

var second = QuestRecoveryPolicy.ApplyFailure(first, QuestFailureReason.PickupTargetNotOffered, now.AddMinutes(16));
Assert(second.CooldownUntilUtc == now.AddMinutes(76), "second episode must cool for 60 minutes");

var third = QuestRecoveryPolicy.ApplyFailure(second, QuestFailureReason.PickupTargetNotOffered, now.AddMinutes(77));
Assert(third.State == QuestRecoveryState.Quarantined, "third episode must quarantine");
Assert(third.NextHalfOpenUtc == now.AddMinutes(77).AddHours(6), "quarantine probe must wait six hours");

var failedProbe = QuestRecoveryPolicy.ApplyFailure(
    third,
    QuestFailureReason.PickupTargetNotOffered,
    third.NextHalfOpenUtc!.Value);
Assert(failedProbe.State == QuestRecoveryState.Quarantined, "failed half-open probe must return to quarantine");
Assert(failedProbe.NextHalfOpenUtc == third.NextHalfOpenUtc!.Value.AddHours(6), "failed half-open probe must wait another six hours");

var objectiveKey = QuestRecoveryKey.ForObjective(867, 0);
var objectiveFailure = QuestRecoveryPolicy.ApplyFailure(
    QuestRecoveryRecord.Create(objectiveKey),
    QuestFailureReason.RepeatedDeaths,
    now);
var progressed = QuestRecoveryPolicy.ApplyProgress(objectiveFailure, new[] { 1, 0 }, now.AddMinutes(1));
Assert(progressed.State == QuestRecoveryState.Eligible, "objective progress must reopen objective work");
Assert(progressed.EpisodeCount == 0 && progressed.DeathCountInEpisode == 0, "objective progress must clear objective and death escalation");
Assert(progressed.ObjectiveCounts.SequenceEqual(new[] { 1, 0 }), "objective counts must be retained after progress");

var failedEndpoint = QuestRecoveryPolicy.ApplyFailure(
    QuestRecoveryRecord.Create(QuestRecoveryKey.ForEndpoint(867, QuestRecoveryStage.Navigation, 1, "10,20")),
    QuestFailureReason.PathGenerationFailed,
    now);
var alternativeEndpoint = QuestRecoveryKey.ForEndpoint(867, QuestRecoveryStage.Navigation, 1, "30,40");
Assert(QuestRecoveryPolicy.Evaluate(QuestRecoveryRecord.Create(alternativeEndpoint), new QuestRecoveryContext(), 0, now).MayAttempt,
    "endpoint failure must leave another endpoint eligible");
Assert(failedEndpoint.Key != alternativeEndpoint, "endpoint keys must distinguish endpoints");

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
