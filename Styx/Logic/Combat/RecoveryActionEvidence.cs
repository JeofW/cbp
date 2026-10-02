using System;
using System.Collections.Generic;
using System.Globalization;

namespace Styx.Logic.Combat
{
    internal sealed record RecoveryEvent(long Sequence, double ClientTime, string Kind,
        string SpellName, string SpellRank, long CastId, ulong SourceGuid, ulong TargetGuid,
        int SpellId, double Amount, double Overheal);

    internal sealed record RecoveryEventBatch(string Token, double ClientTime, long LastSequence,
        long LostEvents, IReadOnlyList<RecoveryEvent> Events);

    internal sealed record RecoveryItemObservation(uint Entry, ulong ActorGuid, long Count,
        double CooldownStart, double CooldownDuration, bool Enabled, double ClientTime)
    {
        internal double Remaining => Math.Max(0, CooldownStart + CooldownDuration - ClientTime);
        internal bool IsReady => Enabled && Remaining == 0;
        internal bool Acknowledges(RecoveryItemObservation before) => Entry == before.Entry
            && ActorGuid == before.ActorGuid && before.IsReady && before.Count > 0
            && ClientTime >= before.ClientTime && Count < before.Count
            && Enabled && Remaining > 0 && CooldownStart > before.CooldownStart;
    }

    /// <summary>Strict complete protocols; malformed/partial replies have no usable observation.</summary>
    internal static class RecoveryActionEvidence
    {
        internal const int MaximumBatchEvents = 5; // 6 header + 5 * 11 fields <= 64 observed Lua values.
        internal const int EventFields = 11;

        internal static bool TryParseBatch(IReadOnlyList<string>? values, string token,
            out RecoveryEventBatch? observation)
        {
            observation = null;
            if (values == null || values.Count < 6 || values[0] != "recovery-events" || values[1] != token
                || string.IsNullOrEmpty(token) || !Finite(values[2], out double now)
                || !Unsigned(values[3], out long last) || !Unsigned(values[4], out long lost)
                || !Unsigned(values[5], out long count) || count > MaximumBatchEvents
                || values.Count != 6 + count * EventFields)
                return false;
            var events = new List<RecoveryEvent>((int)count);
            long previous = 0;
            double previousTime = 0;
            for (int index = 0; index < count; index++)
            {
                int start = 6 + index * EventFields;
                string kind = values[start + 2], name = values[start + 3], rank = values[start + 4];
                bool heal = kind == "HEAL";
                if ((!heal && kind != "START" && kind != "SUCCEEDED" && kind != "FAILED" && kind != "INTERRUPTED")
                    || name == null || rank == null || name.Length > 256 || rank.Length > 256
                    || (!heal && name.Length == 0)
                    || !Unsigned(values[start], out long sequence) || sequence == 0 || sequence > last
                    || sequence <= previous || (index > 0 && sequence != previous + 1)
                    || !Finite(values[start + 1], out double time) || time > now || time < previousTime
                    || !Unsigned(values[start + 5], out long castId)
                    || !GuidValue(values[start + 6], out ulong source) || source == 0
                    || !GuidValue(values[start + 7], out ulong target, !heal) || (heal && target == 0)
                    || !Unsigned(values[start + 8], out long spellId) || spellId > int.MaxValue
                    || (heal ? spellId == 0 || castId != 0 : spellId != 0)
                    || !Finite(values[start + 9], out double amount)
                    || !Finite(values[start + 10], out double overheal))
                    return false;
                events.Add(new RecoveryEvent(sequence, time, kind, name, rank, castId,
                    source, target, (int)spellId, amount, overheal));
                previous = sequence;
                previousTime = time;
            }
            observation = new RecoveryEventBatch(token, now, last, lost, events);
            return true;
        }

        internal static bool TryParseItem(IReadOnlyList<string>? values, uint expectedEntry,
            ulong expectedActor, out RecoveryItemObservation? observation)
        {
            observation = null;
            if (values == null || values.Count != 8 || values[0] != "recovery-item"
                || expectedEntry == 0 || expectedActor == 0 || !Unsigned(values[1], out long entry)
                || entry != expectedEntry || !GuidValue(values[2], out ulong actor) || actor != expectedActor
                || !Unsigned(values[3], out long count) || !Finite(values[4], out double start)
                || !Finite(values[5], out double duration) || (values[6] != "0" && values[6] != "1")
                || !Finite(values[7], out double now) || start > now || !double.IsFinite(start + duration))
                return false;
            observation = new RecoveryItemObservation(expectedEntry, actor, count,
                start, duration, values[6] == "1", now);
            return true;
        }

        internal static bool Finite(string? value, out double result) => double.TryParse(value,
            NumberStyles.Float, CultureInfo.InvariantCulture, out result) && double.IsFinite(result) && result >= 0;

        internal static bool Unsigned(string? value, out long result) => long.TryParse(value,
            NumberStyles.None, CultureInfo.InvariantCulture, out result) && result >= 0 && result <= 9007199254740991L;

        internal static bool GuidValue(string? value, out ulong result, bool allowEmpty = false)
        {
            result = 0;
            if (allowEmpty && value == "") return true;
            return value != null && value.Length > 2 && value.Length <= 18
                && value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                && ulong.TryParse(value.AsSpan(2), NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture, out result);
        }
    }

    /// <summary>
    /// Correlates original 3.3.5 cast counters and direct-heal events. SENT is not
    /// START, STOP is not a result, and success without the expected effect is pending.
    /// </summary>
    internal sealed class RecoverySpellEvidence
    {
        private readonly int _spellId;
        private readonly string _name, _rank;
        private readonly ulong _actor, _target;
        private readonly long _baseline;
        private readonly double _earliest;
        internal bool Instant { get; }
        private long _lastSequence;
        private double _startedAt;
        internal long? CastId { get; private set; }
        internal bool Started { get; private set; }
        internal bool Succeeded { get; private set; }
        internal bool Healed { get; private set; }
        internal bool Interrupted { get; private set; }
        internal bool Ambiguous { get; private set; }
        internal bool Acknowledged => Started && Succeeded && Healed && !Interrupted && !Ambiguous;

        internal RecoverySpellEvidence(int spellId, string name, string rank, ulong actor,
            ulong target, long baselineSequence, double earliestClientTime, bool instant)
        {
            _spellId = spellId; _name = name; _rank = rank; _actor = actor; _target = target;
            _baseline = baselineSequence; _lastSequence = baselineSequence;
            _earliest = earliestClientTime; Instant = instant;
            Ambiguous = spellId <= 0 || string.IsNullOrEmpty(name) || rank == null || actor == 0 || target == 0
                || baselineSequence < 0 || !double.IsFinite(earliestClientTime) || earliestClientTime < 0;
        }

        internal void MarkUnavailable() { Ambiguous = true; }

        internal void Observe(RecoveryEvent value)
        {
            if (value.Sequence <= _baseline || value.Sequence <= _lastSequence || value.ClientTime < _earliest
                || !double.IsFinite(value.ClientTime) || value.SourceGuid != _actor)
                return;
            _lastSequence = value.Sequence;
            if (value.Kind == "HEAL")
            {
                if (Started && value.TargetGuid == _target && value.SpellId == _spellId
                    && value.ClientTime >= _startedAt && double.IsFinite(value.Amount) && value.Amount >= 0
                    && double.IsFinite(value.Overheal) && value.Overheal >= 0)
                {
                    if (Interrupted) Ambiguous = true;
                    Healed = true;
                }
                return;
            }
            if (value.SpellName != _name || value.SpellRank != _rank || value.CastId <= 0)
                return;
            // Original success carries a counter, but it does not establish
            // which pending request owns that counter without an observed start.
            // Instant metadata does not authorize borrowing a late success.
            if (value.Kind == "START")
            {
                if (CastId.HasValue && CastId.Value != value.CastId)
                {
                    Ambiguous = true;
                    return;
                }
                if (!CastId.HasValue)
                {
                    CastId = value.CastId;
                    _startedAt = value.ClientTime;
                }
                Started = true;
            }
            if (!CastId.HasValue || CastId.Value != value.CastId) return;
            if (value.Kind == "SUCCEEDED")
            {
                if (Interrupted) Ambiguous = true;
                Succeeded = true;
            }
            else if (value.Kind == "FAILED" || value.Kind == "INTERRUPTED")
            {
                if (Succeeded || Healed) Ambiguous = true;
                else Interrupted = true;
            }
        }
    }
}
