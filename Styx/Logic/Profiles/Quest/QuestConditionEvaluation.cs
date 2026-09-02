using Styx.Logic.Questing;

namespace Styx.Logic.Profiles.Quest;

public enum QuestConditionEvaluationState
{
    Unknown,
    False,
    True
}

/// <summary>
/// Preserves completion uncertainty while legacy profile expressions continue
/// to expose bool-returning IsQuestCompleted helpers.
/// </summary>
public static class QuestConditionEvaluation
{
    [ThreadStatic]
    private static EvaluationFrame? _current;

    public static QuestConditionEvaluationState Evaluate(Func<bool> condition) =>
        Evaluate(condition, completionResolver: null);

    internal static QuestConditionEvaluationState Evaluate(
        Func<bool> condition,
        Func<uint, QuestCompletionState>? completionResolver)
    {
        ArgumentNullException.ThrowIfNull(condition);

        var parent = _current;
        var frame = new EvaluationFrame(completionResolver ?? parent?.CompletionResolver);
        _current = frame;
        try
        {
            bool value = condition();
            return frame.CompletionUnknown
                ? QuestConditionEvaluationState.Unknown
                : value
                    ? QuestConditionEvaluationState.True
                    : QuestConditionEvaluationState.False;
        }
        finally
        {
            _current = parent;
            if (frame.CompletionUnknown && parent != null)
                parent.CompletionUnknown = true;
        }
    }

    internal static QuestCompletionState ResolveCompletion(
        uint questId,
        Func<QuestCompletionState> liveResolver)
    {
        ArgumentNullException.ThrowIfNull(liveResolver);
        return _current?.CompletionResolver is { } resolver
            ? resolver(questId)
            : liveResolver();
    }

    internal static bool ToBoolean(QuestCompletionState completionState)
    {
        if (completionState == QuestCompletionState.Unknown)
        {
            if (_current != null)
                _current.CompletionUnknown = true;
            return false;
        }

        return completionState == QuestCompletionState.KnownComplete;
    }

    private sealed class EvaluationFrame
    {
        public EvaluationFrame(Func<uint, QuestCompletionState>? completionResolver)
        {
            CompletionResolver = completionResolver;
        }

        public Func<uint, QuestCompletionState>? CompletionResolver { get; }
        public bool CompletionUnknown { get; set; }
    }
}
