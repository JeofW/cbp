#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Styx.Logic.Questing;

/// <summary>
/// Bounded no-progress diagnostics, independent of movement or profile creation.
/// This clock never moves, interacts, blacklists or claims quest completion.
/// The caller must revalidate the exact owner before a suggested rescan.
/// </summary>
public sealed class QuestExecutionWatchdog
{
    private object? _owner;
    private long _generation;
    private DateTime _lastSample;
    private int[] _counts = Array.Empty<int>();
    private bool _previousActive;
    private double _activeSeconds, _reportedAt, _recoveredAt;
    private int _recoveries;

    public sealed record Update(bool ShouldLog, bool MayRecover, bool MadeProgress,
        double NoProgressSeconds, IReadOnlyList<int> Before, IReadOnlyList<int> Current, int RecoveryRequests);

    public Update Sample(object owner, long generation, IReadOnlyList<int>? counts, bool active, DateTime now)
    {
        int[] next = counts != null && counts.All(value => value >= 0) ? counts.ToArray() : Array.Empty<int>();
        if (owner == null || !ReferenceEquals(owner, _owner) || generation != _generation)
        {
            _owner = owner; _generation = generation; _counts = next; _lastSample = now;
            _previousActive = active; ResetEpisode();
            return new(false, false, false, 0, next, next, 0);
        }
        int[] previous = _counts;
        bool comparable = previous.Length > 0 && previous.Length == next.Length;
        bool progressed = comparable && next.Where((value, index) => value > previous[index]).Any();
        double gap = (now - _lastSample).TotalSeconds;
        _lastSample = now;
        if (progressed)
            ResetEpisode();
        else if (next.Length > 0 && !comparable)
            ResetEpisode(resetRecoveryBudget: false);
        else if (active && _previousActive && gap > 0 && gap <= 10)
            _activeSeconds += gap;
        _previousActive = active;
        _counts = next;
        bool report = active && !progressed && _activeSeconds - _reportedAt >= 30;
        if (report) _reportedAt = _activeSeconds;
        bool recover = active && comparable && !progressed && _recoveries < 2
            && _activeSeconds >= 60 && _activeSeconds - _recoveredAt >= 60;
        if (recover) { _recoveries++; _recoveredAt = _activeSeconds; }
        return new(report, recover, progressed, _activeSeconds, previous, next, _recoveries);
    }

    public void Reset()
    {
        _owner = null; _generation = 0; _lastSample = default; _previousActive = false;
        _counts = Array.Empty<int>(); ResetEpisode();
    }

    private void ResetEpisode(bool resetRecoveryBudget = true)
    {
        _activeSeconds = _reportedAt = _recoveredAt = 0;
        if (resetRecoveryBudget) _recoveries = 0;
    }
}
