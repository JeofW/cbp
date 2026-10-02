using System;
using System.Collections.Generic;
using System.Linq;

namespace Styx.Logic.Combat
{
    internal enum RecoveryActionKind { Heal, Aura, Consumable }
    [Flags]
    internal enum RecoveryResource { None = 0, Health = 1, Mana = 2 }
    internal enum RecoveryActionState
    {
        Prepared, Submitted, Casting, AwaitingEffect,
        Acknowledged, Rejected, Interrupted, TimedOut, Replaced
    }

    /// <summary>A reservation is distinct from both native submission and its observed effect.</summary>
    internal sealed class RecoveryActionTicket
    {
        internal RecoveryActionTicket(long generation, object context, RecoveryActionKind kind, int spellId,
            ulong targetGuid, ulong itemGuid, RecoveryResource resources, long now, long budget, string owner)
        {
            Generation = generation; Context = context; Kind = kind; SpellId = spellId;
            TargetGuid = targetGuid; ItemGuid = itemGuid; Resources = resources;
            CreatedAt = now; Budget = budget; Deadline = now + budget;
            Owner = owner.Length <= 160 ? owner : owner.Substring(0, 160);
        }
        internal readonly long Generation, CreatedAt, Budget;
        internal readonly object Context;
        internal readonly RecoveryActionKind Kind;
        internal readonly int SpellId;
        internal readonly ulong TargetGuid, ItemGuid;
        internal readonly RecoveryResource Resources;
        internal readonly string Owner;
        internal RecoveryActionState State = RecoveryActionState.Prepared;
        internal bool WasSubmitted;
        internal long SubmittedAt, Deadline, BlockedCount, FirstBlockedAt, LastBlockedAt;
    }

    internal sealed record RecoveryActionTransition(long Generation, RecoveryActionKind Kind,
        RecoveryActionState State, int SpellId, ulong TargetGuid, ulong ItemGuid, string Owner,
        long CreatedAt, long SubmittedAt, long At, long Deadline,
        long BlockedCount, long FirstBlockedAt, long LastBlockedAt, string Reason);

    /// <summary>
    /// Shared bounded action ownership. World reads, event matching and side effects
    /// belong to the adapter; no external callback runs under this owner's lock.
    /// </summary>
    internal sealed class RecoveryActionLedger
    {
        internal const int MaximumPending = 32;
        internal const long MaximumBudgetMilliseconds = 600000;
        private readonly object _sync = new object();
        private readonly List<RecoveryActionTicket> _pending = new List<RecoveryActionTicket>();
        private readonly Queue<RecoveryActionTransition> _transitions = new Queue<RecoveryActionTransition>();
        private object? _context;
        private long _generation, _lastNow;

        internal IReadOnlyList<RecoveryActionTicket> Pending
        {
            get { lock (_sync) return _pending.ToArray(); }
        }

        internal bool TryPrepare(object? context, RecoveryActionKind kind, int spellId, ulong targetGuid,
            ulong itemGuid, RecoveryResource resources, long now, long budget, string owner,
            out RecoveryActionTicket? ticket)
        {
            ticket = null;
            if (context == null || targetGuid == 0 || now < 0 || budget <= 0
                || budget > MaximumBudgetMilliseconds || now > long.MaxValue - budget
                || string.IsNullOrWhiteSpace(owner) || !Enum.IsDefined(kind)
                || (resources & ~(RecoveryResource.Health | RecoveryResource.Mana)) != 0
                || (kind == RecoveryActionKind.Consumable ? itemGuid == 0 : spellId <= 0))
                return false;
            lock (_sync)
            {
                AdvanceCore(context, now);
                if (!CanReserveCore(kind, spellId, targetGuid, resources, now)) return false;
                ticket = new RecoveryActionTicket(unchecked(++_generation), context, kind, spellId,
                    targetGuid, itemGuid, resources, now, budget, owner);
                _pending.Add(ticket);
                Record(ticket, now, "prepared; effect not observed");
                return true;
            }
        }

        // This advisory check avoids expensive world queries for an already
        // conflicting action. Final TryPrepare still checks atomically again.
        internal bool CanPrepare(object context, RecoveryActionKind kind, int spellId, ulong targetGuid,
            RecoveryResource resources, long now)
        {
            lock (_sync)
            {
                AdvanceCore(context, now);
                return CanReserveCore(kind, spellId, targetGuid, resources, now);
            }
        }

        private bool CanReserveCore(RecoveryActionKind kind, int spellId, ulong targetGuid,
            RecoveryResource resources, long now)
        {
            bool conflict = false;
            foreach (var existing in _pending)
            {
                bool sameSpell = spellId > 0 && existing.SpellId == spellId && existing.TargetGuid == targetGuid;
                bool sameHealth = existing.TargetGuid == targetGuid
                    && (existing.Resources & resources & RecoveryResource.Health) != 0;
                bool bothItems = kind == RecoveryActionKind.Consumable && existing.Kind == RecoveryActionKind.Consumable;
                if (!sameSpell && !sameHealth && !bothItems) continue;
                conflict = true;
                if (existing.BlockedCount == 0) existing.FirstBlockedAt = now;
                if (existing.BlockedCount < long.MaxValue) existing.BlockedCount++;
                existing.LastBlockedAt = now;
            }
            return !conflict && _pending.Count < MaximumPending;
        }

        internal void Advance(object context, long now)
        {
            lock (_sync) AdvanceCore(context, now);
        }

        private void AdvanceCore(object context, long now)
        {
            bool replaced = !ReferenceEquals(context, _context) || now < _lastNow;
            if (replaced)
            {
                foreach (var ticket in _pending.ToArray())
                    Finish(ticket, RecoveryActionState.Replaced, now, "owner or monotonic clock replaced");
                _context = context;
            }
            _lastNow = now;
            foreach (var ticket in _pending.ToArray())
                if (now >= ticket.Deadline)
                    Finish(ticket, ticket.WasSubmitted ? RecoveryActionState.TimedOut : RecoveryActionState.Rejected,
                        now, ticket.WasSubmitted ? "acknowledgement deadline elapsed; outcome unproven" : "preparation expired without dispatch");
        }

        // A callback carrying an old context is never allowed to install that
        // context again. Only a fresh adapter admission/maintenance may Advance.
        private bool Owns(RecoveryActionTicket ticket, object context, long now)
        {
            if (!ReferenceEquals(context, _context) || !ReferenceEquals(ticket.Context, context)
                || !_pending.Contains(ticket)) return false;
            AdvanceCore(context, now);
            return _pending.Contains(ticket);
        }

        internal bool BeginSubmission(RecoveryActionTicket ticket, object context, long now)
        {
            lock (_sync)
            {
                if (!Owns(ticket, context, now) || ticket.State != RecoveryActionState.Prepared
                    || now > long.MaxValue - ticket.Budget) return false;
                ticket.WasSubmitted = true;
                ticket.SubmittedAt = now;
                ticket.Deadline = now + ticket.Budget;
                ticket.State = RecoveryActionState.Submitted;
                Record(ticket, now, "native dispatch entered; effect not acknowledged");
                return true;
            }
        }

        internal void RejectUnsubmitted(RecoveryActionTicket ticket, object context, long now)
        {
            lock (_sync)
                if (Owns(ticket, context, now) && !ticket.WasSubmitted)
                    Finish(ticket, RecoveryActionState.Rejected, now, "request did not reach native dispatch");
        }

        internal void RejectKnownUnexecuted(RecoveryActionTicket ticket, object context, long now)
        {
            lock (_sync)
                if (Owns(ticket, context, now))
                    Finish(ticket, RecoveryActionState.Rejected, now, "authoritative dispatch guard refused the action");
        }

        internal void Casting(RecoveryActionTicket ticket, object context, long now)
        {
            lock (_sync)
                if (Owns(ticket, context, now) && ticket.State == RecoveryActionState.Submitted)
                {
                    ticket.State = RecoveryActionState.Casting;
                    Record(ticket, now, "matching cast observed; effect still pending");
                }
        }

        internal void CastSucceeded(RecoveryActionTicket ticket, object context, long now)
        {
            lock (_sync)
                if (Owns(ticket, context, now) && ticket.WasSubmitted && ticket.State != RecoveryActionState.AwaitingEffect)
                {
                    ticket.State = RecoveryActionState.AwaitingEffect;
                    Record(ticket, now, "matching cast succeeded; authoritative effect still pending");
                }
        }

        internal void Observe(RecoveryActionTicket ticket, object context, long now, bool? acknowledged, bool? interrupted)
        {
            lock (_sync)
            {
                if (!Owns(ticket, context, now) || !ticket.WasSubmitted || (acknowledged == true && interrupted == true))
                    return;
                if (acknowledged == true)
                    Finish(ticket, RecoveryActionState.Acknowledged, now, "authoritative expected effect observed");
                else if (interrupted == true)
                    Finish(ticket, RecoveryActionState.Interrupted, now, "matching failure or interruption observed");
            }
        }

        internal void Revoke(object context, long now)
        {
            lock (_sync)
            {
                if (!ReferenceEquals(context, _context)) return;
                foreach (var ticket in _pending.ToArray())
                    Finish(ticket, RecoveryActionState.Replaced, now, "owned session stopped or cancelled");
                _context = null;
            }
        }

        private void Finish(RecoveryActionTicket ticket, RecoveryActionState state, long now, string reason)
        {
            ticket.State = state;
            _pending.Remove(ticket);
            Record(ticket, now, reason);
        }

        private void Record(RecoveryActionTicket ticket, long now, string reason)
        {
            if (_transitions.Count == 64) _transitions.Dequeue();
            _transitions.Enqueue(new RecoveryActionTransition(ticket.Generation, ticket.Kind, ticket.State,
                ticket.SpellId, ticket.TargetGuid, ticket.ItemGuid, ticket.Owner, ticket.CreatedAt,
                ticket.SubmittedAt, now, ticket.Deadline, ticket.BlockedCount,
                ticket.FirstBlockedAt, ticket.LastBlockedAt, reason));
        }

        internal IReadOnlyList<RecoveryActionTransition> DrainTransitions()
        {
            lock (_sync)
            {
                var result = _transitions.ToArray();
                _transitions.Clear();
                return result;
            }
        }
    }
}
