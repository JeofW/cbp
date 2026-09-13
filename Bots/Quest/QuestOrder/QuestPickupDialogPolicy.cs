using Styx.Logic.Questing;
using Styx.Logic.Questing.Recovery;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Bots.Quest.QuestOrder;

public enum QuestPickupDialogAction
{
    Wait,
    AcceptTarget,
    AdvanceCompletedQuest,
    RejectMismatch
}

public enum QuestPickupDialogCommand
{
    None,
    CloseFrame,
    Accept,
    Continue,
    SelectReward,
    Complete
}

public sealed class QuestPickupDialogExecutionPlan
{
    public QuestPickupDialogCommand Command { get; init; }
    public bool KeepRunning { get; init; }
    public bool CloseFrame => Command == QuestPickupDialogCommand.CloseFrame;
    public bool Accept => Command == QuestPickupDialogCommand.Accept;
    public bool Continue => Command == QuestPickupDialogCommand.Continue;
    public bool SelectReward => Command == QuestPickupDialogCommand.SelectReward;
    public bool Complete => Command == QuestPickupDialogCommand.Complete;
}

public sealed class QuestPickupDialogDecision
{
    private IReadOnlyList<uint> _offeredQuestIds = Array.Empty<uint>();

    public QuestPickupDialogAction Action { get; init; }
    public QuestFailureReason Reason { get; init; }
    public uint TargetQuestId { get; init; }
    public uint ShownQuestId { get; init; }
    public uint GiverId { get; init; }
    // Positive identity/list evidence is distinct from an unloaded Wait observation.
    public bool TargetObserved { get; init; }
    public IReadOnlyList<uint> OfferedQuestIds
    {
        get => _offeredQuestIds;
        init => _offeredQuestIds = Array.AsReadOnly((value ?? Array.Empty<uint>()).ToArray());
    }
    public string Evidence { get; init; } = "";
}

public static class QuestPickupDialogPolicy
{
    public static bool TryFindUniqueExactTitleIndex(
        IReadOnlyList<string>? titles,
        string targetQuestName,
        out int index)
    {
        index = -1;
        string targetTitle = (targetQuestName ?? "").Trim();
        if (targetTitle.Length == 0 || titles == null)
            return false;

        int matches = 0;
        for (int candidateIndex = 0; candidateIndex < titles.Count; candidateIndex++)
        {
            if (!string.Equals(
                    (titles[candidateIndex] ?? "").Trim(),
                    targetTitle,
                    StringComparison.Ordinal))
                continue;

            matches++;
            index = candidateIndex;
        }

        if (matches == 1)
            return true;

        index = -1;
        return false;
    }

    public static QuestPickupDialogDecision Decide(
        uint targetQuestId,
        string targetQuestName,
        uint shownQuestId,
        string shownQuestName,
        uint giverId,
        IReadOnlyList<uint> offeredQuestIds,
        bool acceptVisible,
        bool continueVisible,
        bool completeQuestVisible,
        bool rewardChoicesAvailable,
        CompletedQuestCacheStatus completionStatus,
        bool shownQuestCompleted)
    {
        return Decide(
            targetQuestId,
            targetQuestName,
            shownQuestId,
            shownQuestName,
            giverId,
            offeredQuestIds,
            acceptVisible,
            continueVisible,
            completeQuestVisible,
            rewardChoicesAvailable,
            completionStatus == CompletedQuestCacheStatus.Valid,
            shownQuestCompleted,
            shownTitleUniquelyResolved: false);
    }

    public static QuestPickupDialogDecision Decide(
        uint targetQuestId,
        string targetQuestName,
        uint shownQuestId,
        string shownQuestName,
        uint giverId,
        IReadOnlyList<uint> offeredQuestIds,
        bool acceptVisible,
        bool continueVisible,
        bool completeQuestVisible,
        bool rewardChoicesAvailable,
        bool shownQuestCompletionKnown,
        bool shownQuestCompleted,
        bool shownTitleUniquelyResolved,
        bool offeredQuestListLoaded = false)
    {
        uint[] offeredSnapshot = (offeredQuestIds ?? Array.Empty<uint>()).ToArray();
        string targetTitle = (targetQuestName ?? "").Trim();
        string shownTitle = (shownQuestName ?? "").Trim();
        bool targetIdentified = shownQuestId == targetQuestId && shownQuestId != 0;

        if (shownQuestId == 0 && targetTitle.Length > 0 && shownTitleUniquelyResolved)
            targetIdentified = string.Equals(targetTitle, shownTitle, StringComparison.Ordinal);

        QuestPickupDialogAction action;
        QuestFailureReason reason = QuestFailureReason.None;
        if (targetIdentified)
        {
            action = acceptVisible ? QuestPickupDialogAction.AcceptTarget : QuestPickupDialogAction.Wait;
        }
        else if (shownQuestId != 0
                 && shownQuestCompletionKnown
                 && shownQuestCompleted
                 && (continueVisible || completeQuestVisible || rewardChoicesAvailable))
        {
            action = QuestPickupDialogAction.AdvanceCompletedQuest;
        }
        else if (offeredQuestListLoaded && !offeredSnapshot.Contains(targetQuestId))
        {
            action = QuestPickupDialogAction.RejectMismatch;
            reason = QuestFailureReason.PickupTargetNotOffered;
        }
        else if (shownQuestId == 0 && shownTitle.Length == 0)
        {
            action = QuestPickupDialogAction.Wait;
        }
        else
        {
            action = QuestPickupDialogAction.RejectMismatch;
            reason = QuestFailureReason.PickupWrongQuestShown;
        }

        string evidence = string.Format(
            "target={0}; shown={1}; giver={2}; offered=[{3}]; targetTitle='{4}'; shownTitle='{5}'; completionKnown={6}; completed={7}; titleUnique={8}; buttons=accept:{9},continue:{10},complete:{11},reward:{12}",
            targetQuestId,
            shownQuestId,
            giverId,
            string.Join(",", offeredSnapshot),
            targetTitle,
            shownTitle,
            shownQuestCompletionKnown,
            shownQuestCompleted,
            shownTitleUniquelyResolved,
            acceptVisible,
            continueVisible,
            completeQuestVisible,
            rewardChoicesAvailable);

        return new QuestPickupDialogDecision
        {
            Action = action,
            Reason = reason,
            TargetQuestId = targetQuestId,
            ShownQuestId = shownQuestId,
            GiverId = giverId,
            TargetObserved = targetIdentified || (targetQuestId != 0 && offeredQuestListLoaded && offeredSnapshot.Contains(targetQuestId)),
            OfferedQuestIds = offeredSnapshot,
            Evidence = evidence
        };
    }

    public static QuestAttemptOutcome CreateMismatchOutcome(
        QuestPickupDialogDecision decision,
        int confirmedInteractionCycles,
        long interactionCycleId = 0)
    {
        if (decision == null)
            throw new ArgumentNullException(nameof(decision));
        if (decision.Action != QuestPickupDialogAction.RejectMismatch)
            throw new ArgumentException("Only a confirmed pickup mismatch can create a mismatch outcome.", nameof(decision));

        return new QuestAttemptOutcome
        {
            Key = QuestRecoveryKey.ForNpc(
                decision.TargetQuestId,
                QuestRecoveryStage.Pickup,
                decision.GiverId),
            Kind = confirmedInteractionCycles >= 3
                ? QuestAttemptOutcomeKind.Failure
                : QuestAttemptOutcomeKind.Observation,
            Reason = decision.Reason,
            IsFailureEpisode = confirmedInteractionCycles >= 3,
            Evidence = decision.Evidence,
            ObservedQuestId = decision.ShownQuestId,
            OfferedQuestIds = decision.OfferedQuestIds,
            InteractionCycleId = interactionCycleId
        };
    }
}

public static class QuestPickupDialogExecutionPolicy
{
    public static QuestPickupDialogExecutionPlan CreatePlan(
        QuestPickupDialogDecision decision,
        bool continueVisible,
        bool completeVisible,
        bool rewardChoicesAvailable)
    {
        if (decision == null)
            throw new ArgumentNullException(nameof(decision));

        if (decision.Action == QuestPickupDialogAction.Wait)
            return new QuestPickupDialogExecutionPlan { KeepRunning = true };
        if (decision.Action == QuestPickupDialogAction.RejectMismatch)
            return new QuestPickupDialogExecutionPlan { Command = QuestPickupDialogCommand.CloseFrame };
        if (decision.Action == QuestPickupDialogAction.AcceptTarget)
            return new QuestPickupDialogExecutionPlan { Command = QuestPickupDialogCommand.Accept };
        if (continueVisible)
            return new QuestPickupDialogExecutionPlan
            {
                Command = QuestPickupDialogCommand.Continue,
                KeepRunning = true
            };
        if (rewardChoicesAvailable && !completeVisible)
            return new QuestPickupDialogExecutionPlan
            {
                Command = QuestPickupDialogCommand.SelectReward,
                KeepRunning = true
            };
        if (completeVisible)
            return new QuestPickupDialogExecutionPlan
            {
                Command = QuestPickupDialogCommand.Complete,
                KeepRunning = true
            };

        return new QuestPickupDialogExecutionPlan { Command = QuestPickupDialogCommand.CloseFrame };
    }
}

#nullable disable
public sealed class QuestPickupMismatchTracker
{
    private string _lastEvidence;
    private long? _lastInteractionCycleId;
    private long? _latestObservedInteractionCycleId;

    public int ConfirmedCycles { get; private set; }
    public bool PickupUnavailable { get; private set; }
    public QuestAttemptOutcome LastOutcome { get; private set; }

    public QuestAttemptOutcome Observe(QuestPickupDialogDecision decision, long interactionCycleId)
    {
        if (decision == null)
            throw new ArgumentNullException(nameof(decision));
        // Older callbacks cannot add an attempt or erase newer observations.
        if (_latestObservedInteractionCycleId.HasValue && interactionCycleId < _latestObservedInteractionCycleId.Value)
            return LastOutcome;
        _latestObservedInteractionCycleId = interactionCycleId;
        if (decision.Action == QuestPickupDialogAction.Wait)
        {
            if (decision.TargetObserved)
                ClearEpisode();
            return LastOutcome;
        }
        if (decision.Action != QuestPickupDialogAction.RejectMismatch)
        {
            ClearEpisode();
            return null;
        }

        // The requested quest is still absent when unrelated offers change. Keep
        // raw ordered diagnostics on the outcome, not in this semantic retry key.
        string identity = decision.Reason == QuestFailureReason.PickupTargetNotOffered
            ? $"missing:{decision.TargetQuestId}:{decision.GiverId}:{decision.Reason}"
            : decision.Evidence;
        bool evidenceChanged = !string.Equals(_lastEvidence, identity, StringComparison.Ordinal);
        if (evidenceChanged)
        {
            ConfirmedCycles = 0;
            PickupUnavailable = false;
            _lastEvidence = identity;
            LastOutcome = null;
        }

        if (!evidenceChanged
            && _lastInteractionCycleId == interactionCycleId
            && LastOutcome != null)
            return LastOutcome;

        if (PickupUnavailable)
            return LastOutcome;

        _lastInteractionCycleId = interactionCycleId;
        ConfirmedCycles++;
        LastOutcome = QuestPickupDialogPolicy.CreateMismatchOutcome(
            decision,
            ConfirmedCycles,
            interactionCycleId);
        PickupUnavailable = LastOutcome.IsFailureEpisode;
        return LastOutcome;
    }

    public void Reset()
    {
        _latestObservedInteractionCycleId = null;
        ClearEpisode();
    }

    private void ClearEpisode()
    {
        _lastEvidence = null;
        _lastInteractionCycleId = null;
        ConfirmedCycles = 0;
        PickupUnavailable = false;
        LastOutcome = null;
    }
}
