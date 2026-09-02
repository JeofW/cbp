using Styx.Logic.Questing;

namespace Bots.Quest.QuestOrder;

internal enum QuestNodeCompletionAction
{
    Execute,
    Skip,
    Defer
}

internal static class QuestNodeCompletionPolicy
{
    public static QuestNodeCompletionAction ForPickup(QuestCompletionState completion, bool accepted)
    {
        if (accepted || completion == QuestCompletionState.KnownComplete)
            return QuestNodeCompletionAction.Skip;
        return completion == QuestCompletionState.Unknown
            ? QuestNodeCompletionAction.Defer
            : QuestNodeCompletionAction.Execute;
    }

    public static QuestNodeCompletionAction ForTurnIn(QuestCompletionState completion, bool accepted)
    {
        if (accepted)
            return QuestNodeCompletionAction.Execute;
        return completion == QuestCompletionState.Unknown
            ? QuestNodeCompletionAction.Defer
            : QuestNodeCompletionAction.Skip;
    }

    public static QuestNodeCompletionAction ForObjective(QuestCompletionState completion, bool accepted)
    {
        if (completion == QuestCompletionState.Unknown)
            return QuestNodeCompletionAction.Defer;
        if (accepted && completion == QuestCompletionState.KnownIncomplete)
            return QuestNodeCompletionAction.Execute;
        return QuestNodeCompletionAction.Skip;
    }
}
