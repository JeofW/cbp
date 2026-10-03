using System;
using Styx.Helpers;

namespace Styx.Logic.Pathing;

/// <summary>Nonblocking selection/submission/observation lifetime for one ground journey.</summary>
internal sealed class GroundMountRequest
{
    private int _spell;
    private double _deadline, _nextProbe, _last = double.NegativeInfinity;
    private bool _submitted, _inside;
    internal bool Waiting(double now, bool mounted, bool casting, bool moving,
        Func<bool> current, Func<int> select, Func<int, bool> submit, Action stop)
    {
        if (!current()) return false;
        if (!double.IsFinite(now) || now < _last)
            throw new ObservationUnavailableException("ground-mount", "Monotonic mount clock unavailable.");
        _last = now;
        if (_inside) return true;
        _inside = true;
        try
        {
            if (mounted) { _spell = 0; _submitted = false; _nextProbe = now; return false; }
            if (_spell != 0 && now >= _deadline) { _spell = 0; _submitted = false; return false; }
            if (_submitted || casting) return current();
            if (_spell == 0)
            {
                if (now < _nextProbe) return false;
                _nextProbe = now + 5;
                int selected = select();
                if (!current() || selected <= 0) return false;
                _spell = selected; _deadline = now + 8;
            }
            if (moving) { stop(); return current(); }
            if (!current()) return false;
            _submitted = submit(_spell);
            // A rejected preparation issued no mount request. Permit walking
            // out of that spot and a prompt bounded retry. Only a submitted
            // request owns the longer observation/retry lifetime.
            _nextProbe = now + (_submitted ? 30 : 1);
            if (!_submitted) _spell = 0;
            return _submitted && current();
        }
        finally { _inside = false; }
    }
}
