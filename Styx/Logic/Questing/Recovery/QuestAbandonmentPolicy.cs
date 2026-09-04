namespace Styx.Logic.Questing.Recovery;

public sealed class QuestAbandonmentLiveSnapshot
{
    public bool IsAccepted { get; init; }
    public bool IsCompleted { get; init; }
    public bool StateIsCertain { get; init; }
    public bool HasObjectiveProgress { get; init; }
    public int FreeQuestLogSlots { get; init; }
}

public sealed class QuestAbandonmentContext
{
    public bool IsAccepted { get; init; }
    public bool IsCompleted { get; init; }
    public bool StateIsCertain { get; init; }
    public bool HasObjectiveProgress { get; init; }
    public int FreeQuestLogSlots { get; init; }
    public QuestRecoveryState RecoveryState { get; init; }
    public QuestFailureReason Reason { get; init; }
}

public sealed class QuestAbandonmentDecision
{
    public bool MayAbandon { get; init; }
    public string Reason { get; init; } = "";
}

public static class QuestAbandonmentPolicy
{
    public static QuestAbandonmentDecision Evaluate(QuestAbandonmentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.IsAccepted)
            return Denied("Automatic abandonment denied: quest is not accepted.");
        if (context.IsCompleted)
            return Denied("Automatic abandonment denied: quest is completed.");
        if (!context.StateIsCertain)
            return Denied("Automatic abandonment denied: accepted/completed/progress state is uncertain.");
        if (context.HasObjectiveProgress)
            return Denied("Automatic abandonment denied: quest has objective progress.");
        if (context.FreeQuestLogSlots < 0)
        {
            return Denied(
                $"Automatic abandonment denied: free quest-log slot count {context.FreeQuestLogSlots} is invalid.");
        }

        if (context.RecoveryState == QuestRecoveryState.ManualBlacklist)
        {
            return Denied("Automatic abandonment denied: quest is manually blacklisted.");
        }

        if (context.RecoveryState is QuestRecoveryState.Eligible or
            QuestRecoveryState.Attempting or
            QuestRecoveryState.CoolingDown or
            QuestRecoveryState.HalfOpen or
            QuestRecoveryState.Completed)
        {
            return Denied(
                $"Automatic abandonment denied: recovery state {context.RecoveryState} is not Quarantined.");
        }

        if (context.RecoveryState != QuestRecoveryState.Quarantined)
        {
            return Denied(
                $"Automatic abandonment denied: recovery state {(int)context.RecoveryState} is unknown.");
        }

        if (context.Reason == QuestFailureReason.UserExcluded)
            return Denied("Automatic abandonment denied: quest is manually blacklisted.");
        if (context.Reason == QuestFailureReason.None)
            return Denied("Automatic abandonment denied: quarantine has no automatic failure reason.");
        if (context.Reason == QuestFailureReason.TurnInQuestIncomplete)
        {
            return Denied(
                "Automatic abandonment denied: TurnInQuestIncomplete is a scheduler redirect, not a quarantine failure reason.");
        }

        if (!IsAutomaticQuarantineReason(context.Reason))
        {
            return Denied(
                $"Automatic abandonment denied: quarantine failure reason {(int)context.Reason} is unknown or unsupported.");
        }

        if (context.FreeQuestLogSlots > 2)
        {
            return Denied(
                $"Automatic abandonment denied: quest log has {context.FreeQuestLogSlots} free slots; pressure requires 2 or fewer.");
        }

        return new QuestAbandonmentDecision
        {
            MayAbandon = true,
            Reason = $"Automatic abandonment permitted: accepted, incomplete, certain, zero-progress quest is automatically quarantined with {context.FreeQuestLogSlots} free quest-log slots."
        };
    }

    private static QuestAbandonmentDecision Denied(string reason) =>
        new() { MayAbandon = false, Reason = reason };

    private static bool IsAutomaticQuarantineReason(QuestFailureReason reason) =>
        reason is QuestFailureReason.PickupTargetNotOffered or
            QuestFailureReason.PickupWrongQuestShown or
            QuestFailureReason.NpcNotFoundInWorld or
            QuestFailureReason.NpcMissingFromDatabase or
            QuestFailureReason.InteractionTimedOut or
            QuestFailureReason.PathGenerationFailed or
            QuestFailureReason.EndpointUnreachable or
            QuestFailureReason.NoNavigableHotspot or
            QuestFailureReason.NoObjectiveTargetsObserved or
            QuestFailureReason.NoObjectiveProgress or
            QuestFailureReason.RepeatedDeaths or
            QuestFailureReason.TurnInTargetNotOffered or
            QuestFailureReason.UnsupportedObjective or
            QuestFailureReason.InvalidQuestData or
            QuestFailureReason.InternalBehaviorError or
            QuestFailureReason.LegacyUnknown;
}
