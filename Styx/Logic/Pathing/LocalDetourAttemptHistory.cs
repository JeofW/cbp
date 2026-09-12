using System;
using System.Collections.Generic;

namespace Styx.Logic.Pathing
{
    // Retained across path rebuilds: landing successfully does not prove that the
    // regenerated mesh route avoids the obstacle which required the detour.
    internal sealed class LocalDetourAttemptHistory
    {
        private readonly List<(uint Map, WoWPoint Origin, DateTime Utc)> _attempts = new();

        internal bool CanAttempt(uint map, WoWPoint origin, DateTime utcNow)
        {
            _attempts.RemoveAll(attempt => utcNow < attempt.Utc || utcNow - attempt.Utc >= TimeSpan.FromMinutes(2));
            return !_attempts.Exists(attempt => attempt.Map == map && attempt.Origin.DistanceSqr(origin) <= 25f);
        }

        internal void Record(uint map, WoWPoint origin, DateTime utcNow)
        {
            if (_attempts.Count >= 8) _attempts.RemoveAt(0);
            _attempts.Add((map, origin, utcNow));
        }
    }
}
