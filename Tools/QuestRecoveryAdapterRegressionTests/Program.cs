using Styx.Bot.Quest_Behaviors;
using Styx.Logic.Questing.Recovery;
using System;

var attemptKey = QuestRecoveryKey.ForQuestStage(876, QuestRecoveryStage.Pickup);
var failed = SafePickUp.CreateUnavailableOutcome(
    targetQuestId: 876,
    shownQuestId: 867,
    giverId: 3338,
    offeredQuestIds: new uint[] { 867, 875 },
    reason: QuestFailureReason.PickupWrongQuestShown,
    evidence: "target=876; shown=867; giver=3338; offered=[867,875]",
    isFailureEpisode: true,
    interactionCycleId: 3,
    attemptKey: attemptKey,
    attemptGeneration: 41);

Assert(failed.Key.Equals(QuestRecoveryKey.ForNpc(876, QuestRecoveryStage.Pickup, 3338)),
    "unavailable pickup must use the exact pickup/NPC relation key");
Assert(failed.AttemptKey != null && failed.AttemptKey.Equals(attemptKey)
       && failed.AttemptGeneration == 41,
    "unavailable pickup must retain exact pickup-stage attempt ownership");
Assert(failed.Kind == QuestAttemptOutcomeKind.Failure
       && failed.Reason == QuestFailureReason.PickupWrongQuestShown
       && failed.IsFailureEpisode,
    "the third confirmed mismatch cycle must be one failure episode");
Assert(failed.ObservedQuestId == 867
       && failed.OfferedQuestIds.Count == 2
       && failed.OfferedQuestIds[0] == 867
       && failed.OfferedQuestIds[1] == 875
       && failed.InteractionCycleId == 3,
    "the adapter outcome must preserve literal shown, offered, and cycle evidence");
Assert(failed.Evidence == "target=876; shown=867; giver=3338; offered=[867,875]",
    "the adapter outcome must preserve exact runtime evidence");

var sample = SafePickUp.CreateUnavailableOutcome(
    targetQuestId: 876,
    shownQuestId: 0,
    giverId: 3391,
    offeredQuestIds: Array.Empty<uint>(),
    reason: QuestFailureReason.PickupTargetNotOffered,
    evidence: "target=876; shown=0; giver=3391; offered=[]",
    isFailureEpisode: false,
    interactionCycleId: 1,
    attemptKey: attemptKey,
    attemptGeneration: 41);

Assert(sample.Kind == QuestAttemptOutcomeKind.Observation
       && !sample.IsFailureEpisode
       && sample.Reason == QuestFailureReason.PickupTargetNotOffered
       && sample.Key.Equals(QuestRecoveryKey.ForNpc(876, QuestRecoveryStage.Pickup, 3391)),
    "a pre-boundary mismatch sample must remain a narrow non-episode observation");

var interactionTimeout = SafePickUp.CreateInteractionTimeoutOutcome(
    targetQuestId: 876,
    giverId: 3391,
    evidence: "quest=876; giver=3391; interactionTimeoutSeconds=20",
    attemptKey: attemptKey,
    attemptGeneration: 41);

Assert(interactionTimeout.Key.Equals(
           QuestRecoveryKey.ForNpc(876, QuestRecoveryStage.Pickup, 3391))
       && interactionTimeout.AttemptKey != null
       && interactionTimeout.AttemptKey.Equals(attemptKey)
       && interactionTimeout.AttemptGeneration == 41
       && interactionTimeout.Kind == QuestAttemptOutcomeKind.Failure
       && interactionTimeout.Reason == QuestFailureReason.InteractionTimedOut
       && interactionTimeout.IsFailureEpisode
       && interactionTimeout.Evidence == "quest=876; giver=3391; interactionTimeoutSeconds=20",
    "a reached giver with no dialog must end as one exact owned NPC interaction timeout");

Console.WriteLine("Quest recovery adapter regression tests passed.");

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
