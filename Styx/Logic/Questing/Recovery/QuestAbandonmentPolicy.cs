namespace Styx.Logic.Questing.Recovery;

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
        if (context.RecoveryState == QuestRecoveryState.ManualBlacklist ||
            context.Reason == QuestFailureReason.UserExcluded)
        {
            return Denied("Automatic abandonment denied: quest is manually blacklisted.");
        }

        if (context.RecoveryState != QuestRecoveryState.Quarantined)
        {
            return Denied(
                $"Automatic abandonment denied: recovery state {context.RecoveryState} is not an automatic quarantine.");
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
}
