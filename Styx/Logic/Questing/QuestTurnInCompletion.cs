using System;
using System.Collections.Generic;
using System.Linq;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Combat;
using Styx.WoWInternals;

#nullable disable

namespace Styx.Logic.Questing
{
    public enum QuestTurnInCompletionState { Prepared, Submitted, PendingCompletionConfirmation, Confirmed, Rejected, Unresolved, Cancelled }

    /// <summary>
    /// One submitted reward's observation owner. Generated profile replacement
    /// cannot erase an action already sent to the server. Run/actor/memory changes
    /// revoke it; callbacks holding an older record cannot acquire the new scope.
    /// </summary>
    public sealed class QuestTurnInCompletion
    {
        private static readonly Dictionary<uint, QuestTurnInCompletion> Pending = new();
        private static readonly object Sync = new();
        private static readonly HashSet<uint> RepeatableQuests = new();
        private static object _actor, _memory, _run;
        private static long _generation;
        private static ulong _actorGuid;
        private static uint _actorAddress;
        private static int _processId;
        private static string _identity;
        private readonly QuestLog _log;
        private readonly object _owner;
        private readonly CompletedQuestHistoryObservation _baseline;
        private readonly bool _requiresCompletedHistory;
        private readonly QuestDailySnapshot _dailyBaseline;
        private DateTime _submittedUtc;
        private long _submittedTick, _nextRefreshTick;
        private long _requestedGeneration;
        private bool _departed;
        private int _refreshes;

        private QuestTurnInCompletion(uint questId, object owner, QuestLog log,
            CompletedQuestHistoryObservation baseline, bool requiresCompletedHistory, QuestDailySnapshot dailyBaseline)
        {
            QuestId = questId; _owner = owner; _log = log; _baseline = baseline;
            _requiresCompletedHistory = requiresCompletedHistory;
            _dailyBaseline = dailyBaseline;
            Generation = ++_generation;
        }
        public uint QuestId { get; }
        public long Generation { get; }
        public QuestTurnInCompletionState State { get; private set; } = QuestTurnInCompletionState.Prepared;
        public bool WasSubmitted => _submittedUtc != DateTime.MinValue;
        public bool BlocksPickup => State is QuestTurnInCompletionState.Submitted
            or QuestTurnInCompletionState.PendingCompletionConfirmation or QuestTurnInCompletionState.Unresolved;

        private static bool CurrentScope()
        {
            var actor = ObjectManager.Me;
            if (!TreeRoot.IsRunning || actor == null || ObjectManager.Wow == null)
            {
                if (Pending.Values.Any(record => record.WasSubmitted)) QuestLog.InvalidateCompletedQuestHistory();
                foreach (var record in Pending.Values) record.State = QuestTurnInCompletionState.Cancelled;
                Pending.Clear(); RepeatableQuests.Clear(); _actor = _memory = _run = null;
                return false;
            }
            string identity = actor == null ? null : actor.Name + "\u001f" + actor.RealmName;
            bool same = TreeRoot.IsRunning && actor != null && ReferenceEquals(_actor, actor)
                && ReferenceEquals(_memory, ObjectManager.Wow) && ReferenceEquals(_run, TreeRoot.RunIdentity)
                && actor.Guid == _actorGuid && actor.BaseAddress == _actorAddress && ObjectManager.Wow.ProcessId == _processId
                && string.Equals(identity, _identity, StringComparison.OrdinalIgnoreCase);
            if (!same)
            {
                if (Pending.Values.Any(record => record.WasSubmitted)) QuestLog.InvalidateCompletedQuestHistory();
                foreach (var record in Pending.Values) record.State = QuestTurnInCompletionState.Cancelled;
                Pending.Clear();
                RepeatableQuests.Clear();
                _actor = actor; _memory = ObjectManager.Wow; _run = TreeRoot.RunIdentity; _identity = identity;
                _actorGuid = actor.Guid; _actorAddress = actor.BaseAddress; _processId = ObjectManager.Wow.ProcessId;
            }
            return TreeRoot.IsRunning && actor != null && ObjectManager.Wow != null;
        }

        private bool Current => CurrentScope() && Pending.TryGetValue(QuestId, out var record)
            && ReferenceEquals(record, this) && ReferenceEquals(ObjectManager.Me.QuestLog, _log);

        public static QuestTurnInCompletion Find(uint questId)
        {
            lock (Sync)
            {
                if (!CurrentScope()) return null;
                if (!Pending.TryGetValue(questId, out var record)) return null;
                if (record.Current) return record;
                Revoke(record);
                return null;
            }
        }

        public static void SetRepeatableQuestIds(IEnumerable<uint> ids)
        {
            lock (Sync)
            {
                if (!CurrentScope()) return;
                RepeatableQuests.Clear();
                RepeatableQuests.UnionWith(ids);
            }
        }

        public static bool RequiresCompletedHistory(uint questId)
        {
            lock (Sync)
            {
                CurrentScope();
                if (RepeatableQuests.Contains(questId)) return false;
                var quest = Quest.FromId(questId);
                return quest == null || !quest.IsDaily && !quest.IsWeekly;
            }
        }

        public static QuestTurnInCompletion Prepare(uint questId, object owner)
        {
            lock (Sync)
            {
                if (questId == 0 || owner == null || !CurrentScope()) return null;
                foreach (var terminal in Pending.Where(pair => !pair.Value.BlocksPickup
                    && pair.Value.State != QuestTurnInCompletionState.Prepared).Select(pair => pair.Key).ToArray()) Pending.Remove(terminal);
                var existing = Find(questId);
                if (existing != null && existing.BlocksPickup) return existing;
                var actor = ObjectManager.Me;
                var run = TreeRoot.RunIdentity;
                var log = actor.QuestLog;
                bool requiresCompletedHistory = RequiresCompletedHistory(questId);
                var baseline = requiresCompletedHistory ? log.CaptureCompletedQuestHistory() : null;
                bool isDaily = !requiresCompletedHistory && Quest.FromId(questId)?.IsDaily == true;
                var dailyBaseline = isDaily ? QuestDailySnapshot.Capture(actor) : null;
                // Original daily descriptor IDs identify rewards from this reset
                // period. Only an observed absent -> present transition can
                // acknowledge this submission; a pre-existing ID cannot do so.
                if (isDaily && (dailyBaseline?.QuestIds == null || dailyBaseline.QuestIds.Contains(questId)
                    || !dailyBaseline.IsCurrent())) return null;
                if (!CurrentScope() || !ReferenceEquals(actor, ObjectManager.Me) || !ReferenceEquals(run, TreeRoot.RunIdentity)) return null;
                if (!ReferenceEquals(Find(questId), existing)) return null;
                if (requiresCompletedHistory && baseline?.Identity == null) return null;
                if (Pending.Count >= 25 && !Pending.ContainsKey(questId)) return null;
                var record = new QuestTurnInCompletion(questId, owner, log, baseline, requiresCompletedHistory, dailyBaseline);
                if (existing != null) existing.State = QuestTurnInCompletionState.Cancelled;
                Pending[questId] = record;
                return record;
            }
        }

        public bool Submit(object owner)
        {
            lock (Sync)
            {
                if (!Current || !ReferenceEquals(_owner, owner) || State != QuestTurnInCompletionState.Prepared
                    || _dailyBaseline != null && !_dailyBaseline.IsCurrent() || !Current) return false;
                _submittedUtc = DateTime.UtcNow;
                _submittedTick = Environment.TickCount64;
                Transition(QuestTurnInCompletionState.Submitted);
                return CanDispatch(owner);
            }
        }

        /// <summary>Revalidate after diagnostics/observations and immediately before reward dispatch.</summary>
        public bool CanDispatch(object owner)
        {
            lock (Sync)
                return Current && ReferenceEquals(_owner, owner) && State == QuestTurnInCompletionState.Submitted
                    && (_dailyBaseline == null || _dailyBaseline.IsCurrent()) && Current;
        }

        /// <summary>A confirmed receipt belongs only to the behavior that submitted it.</summary>
        public bool IsConfirmedFor(object owner)
        {
            lock (Sync)
            {
                if (!Current || !ReferenceEquals(_owner, owner)) return false;
                return Observe() == QuestTurnInCompletionState.Confirmed && Current && ReferenceEquals(_owner, owner);
            }
        }

        public void Cancel(object owner)
        {
            lock (Sync)
            {
                if (Current && ReferenceEquals(_owner, owner))
                {
                    if (WasSubmitted) QuestLog.InvalidateCompletedQuestHistory();
                    State = QuestTurnInCompletionState.Cancelled;
                    Pending.Remove(QuestId);
                }
            }
        }

        public QuestTurnInCompletionState Observe()
        {
            lock (Sync)
            {
                try
                {
                    if (!Current) return QuestTurnInCompletionState.Cancelled;
                    var snapshot = _log.CaptureSnapshot();
                    if (!snapshot.IsIdentityComplete || !Current) return State;
                    return Observe(snapshot);
                }
                catch (Exception error)
                {
                    RecoveryActions.ReportDeferral(error, "Quest turn-in completion observation");
                    return State;
                }
            }
        }

        private QuestTurnInCompletionState Observe(QuestLogSnapshot snapshot)
        {
            if (!Current || !BlocksPickup) return State;
            bool accepted = snapshot.AcceptedQuestIds.Contains(QuestId);
            long now = Environment.TickCount64;
            bool deadline = now - _submittedTick >= 30000;
            if (!accepted && !_departed)
            {
                _departed = true;
                _nextRefreshTick = 0;
                Transition(QuestTurnInCompletionState.PendingCompletionConfirmation);
            }
            // Repeatable/daily/weekly rewards still need one exact submission owner,
            // but permanent completed-history is not authoritative for their result.
            // Keep the request owned while its result is unknown; log departure,
            // frame closure and the deadline cannot acknowledge or renew it.
            if (!_requiresCompletedHistory)
            {
                if (_dailyBaseline != null && !accepted)
                {
                    var daily = QuestDailySnapshot.Capture(ObjectManager.Me);
                    if (!Current) return QuestTurnInCompletionState.Cancelled;
                    if (daily.QuestIds?.Contains(QuestId) == true && daily.PlayerGuid == _dailyBaseline.PlayerGuid
                        && daily.IsCurrent() && _log.IsSnapshotCurrent(snapshot) && Current)
                    {
                        Transition(QuestTurnInCompletionState.Confirmed);
                        return Current ? State : QuestTurnInCompletionState.Cancelled;
                    }
                }
                // Ordinary repeatables/weekly results have no equivalent
                // proven per-submission receipt here. Keep them unresolved.
                if (deadline) Transition(QuestTurnInCompletionState.Unresolved);
                return State;
            }
            // While the quest is accepted, allow the server to process the one
            // submission. At deadline, fresh negative history plus acceptance
            // permits a controlled retry; absence alone never does.
            if (!_departed && !deadline) return State;
            if (now >= _nextRefreshTick)
            {
                _requestedGeneration = _log.RequestCompletedQuestHistoryRefresh();
                _refreshes++;
                _nextRefreshTick = now + (_refreshes < 3 ? 2000 : 60000);
            }
            if (!Current) return QuestTurnInCompletionState.Cancelled;
            var history = _log.CaptureCompletedQuestHistory();
            if (!Current) return QuestTurnInCompletionState.Cancelled;
            if (!_log.IsSnapshotCurrent(snapshot)) return State;
            bool fresh = string.Equals(history.Identity, _baseline.Identity, StringComparison.OrdinalIgnoreCase) && history.IsAuthoritativeAfter(_requestedGeneration)
                && history.Revision > _baseline.Revision && history.ObservedUtc >= _submittedUtc;
            if (fresh && !accepted && history.QuestIds.Contains(QuestId))
                Transition(QuestTurnInCompletionState.Confirmed);
            else if (fresh && accepted && deadline && !history.QuestIds.Contains(QuestId))
                Transition(QuestTurnInCompletionState.Rejected);
            else if (deadline)
                Transition(QuestTurnInCompletionState.Unresolved);
            return State;
        }

        private static void Revoke(QuestTurnInCompletion record)
        {
            if (record.WasSubmitted) QuestLog.InvalidateCompletedQuestHistory();
            record.State = QuestTurnInCompletionState.Cancelled;
            if (Pending.TryGetValue(record.QuestId, out var current) && ReferenceEquals(record, current))
                Pending.Remove(record.QuestId);
        }

        private void Transition(QuestTurnInCompletionState state)
        {
            if (State == state) return;
            State = state;
            Styx.Helpers.Logging.WriteDiagnostic("[TurnInConfirmation] quest={0} generation={1} state={2} historyRequest={3} refreshes={4} deadlineMs=30000",
                QuestId, Generation, state, _requestedGeneration, _refreshes);
        }

        /// <summary>Observe submitted actions before a scheduler uses cached history.</summary>
        public static IReadOnlyCollection<uint> ObservePending(QuestLogSnapshot snapshot)
        {
            lock (Sync)
            {
                if (Pending.Count == 0) return Array.Empty<uint>();
                if (!CurrentScope()) return Array.Empty<uint>();
                foreach (var record in Pending.Values.ToArray())
                {
                    if (!record.Current) { Revoke(record); continue; }
                    if (snapshot != null && snapshot.IsIdentityComplete && record._log.IsSnapshotCurrent(snapshot))
                    {
                        try { record.Observe(snapshot); }
                        catch (Exception error) { RecoveryActions.ReportDeferral(error, "Quest scheduler completion observation"); }
                    }
                }
                return Pending.Values.Where(record => record.BlocksPickup).Select(record => record.QuestId).ToArray();
            }
        }
    }
}
