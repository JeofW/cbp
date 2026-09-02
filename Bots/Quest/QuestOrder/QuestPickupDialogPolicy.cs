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

public sealed class QuestPickupDialogDecision
{
    private IReadOnlyList<uint> _offeredQuestIds = Array.Empty<uint>();

    public QuestPickupDialogAction Action { get; init; }
    public QuestFailureReason Reason { get; init; }
    public uint TargetQuestId { get; init; }
    public uint ShownQuestId { get; init; }
    public uint GiverId { get; init; }
    public IReadOnlyList<uint> OfferedQuestIds
    {
        get => _offeredQuestIds;
        init => _offeredQuestIds = Array.AsReadOnly((value ?? Array.Empty<uint>()).ToArray());
    }
    public string Evidence { get; init; } = "";
}

public static class QuestPickupDialogPolicy
{
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
        uint[] offeredSnapshot = (offeredQuestIds ?? Array.Empty<uint>()).ToArray();
        string targetTitle = (targetQuestName ?? "").Trim();
        string shownTitle = (shownQuestName ?? "").Trim();
        bool targetIdentified = shownQuestId == targetQuestId && shownQuestId != 0;

        if (shownQuestId == 0 && targetTitle.Length > 0)
            targetIdentified = string.Equals(targetTitle, shownTitle, StringComparison.Ordinal);

        QuestPickupDialogAction action;
        QuestFailureReason reason = QuestFailureReason.None;
        if (targetIdentified)
        {
            action = acceptVisible ? QuestPickupDialogAction.AcceptTarget : QuestPickupDialogAction.Wait;
        }
        else if (shownQuestId == 0 && shownTitle.Length == 0)
        {
            action = QuestPickupDialogAction.Wait;
        }
        else if (shownQuestId != 0
                 && completionStatus == CompletedQuestCacheStatus.Valid
                 && shownQuestCompleted
                 && (continueVisible || completeQuestVisible || rewardChoicesAvailable))
        {
            action = QuestPickupDialogAction.AdvanceCompletedQuest;
        }
        else
        {
            action = QuestPickupDialogAction.RejectMismatch;
            reason = QuestFailureReason.PickupWrongQuestShown;
        }

        string evidence = string.Format(
            "target={0}; shown={1}; giver={2}; offered=[{3}]; targetTitle='{4}'; shownTitle='{5}'; completion={6}; completed={7}; buttons=accept:{8},continue:{9},complete:{10},reward:{11}",
            targetQuestId,
            shownQuestId,
            giverId,
            string.Join(",", offeredSnapshot),
            targetTitle,
            shownTitle,
            completionStatus,
            shownQuestCompleted,
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
            OfferedQuestIds = offeredSnapshot,
            Evidence = evidence
        };
    }

    public static QuestAttemptOutcome CreateMismatchOutcome(
        QuestPickupDialogDecision decision,
        int confirmedInteractionCycles)
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
            Reason = QuestFailureReason.PickupWrongQuestShown,
            IsFailureEpisode = confirmedInteractionCycles >= 3,
            Evidence = decision.Evidence,
            ObservedQuestId = decision.ShownQuestId,
            OfferedQuestIds = decision.OfferedQuestIds
        };
    }
}

#nullable disable
public sealed class QuestPickupMismatchTracker
{
    private string _lastEvidence;

    public int ConfirmedCycles { get; private set; }
    public bool PickupUnavailable { get; private set; }
    public QuestAttemptOutcome LastOutcome { get; private set; }

    public QuestAttemptOutcome Observe(QuestPickupDialogDecision decision)
    {
        if (decision == null)
            throw new ArgumentNullException(nameof(decision));
        if (decision.Action != QuestPickupDialogAction.RejectMismatch)
        {
            Reset();
            return null;
        }

        if (!string.Equals(_lastEvidence, decision.Evidence, StringComparison.Ordinal))
        {
            ConfirmedCycles = 0;
            PickupUnavailable = false;
            _lastEvidence = decision.Evidence;
        }

        if (PickupUnavailable)
            return LastOutcome;

        ConfirmedCycles++;
        LastOutcome = QuestPickupDialogPolicy.CreateMismatchOutcome(decision, ConfirmedCycles);
        PickupUnavailable = LastOutcome.IsFailureEpisode;
        return LastOutcome;
    }

    public void Reset()
    {
        _lastEvidence = null;
        ConfirmedCycles = 0;
        PickupUnavailable = false;
        LastOutcome = null;
    }
}
