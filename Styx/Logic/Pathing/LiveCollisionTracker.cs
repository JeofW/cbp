namespace Styx.Logic.Pathing
{
    /// <summary>
    /// Confirms that consecutive live-world collision probes hit the same obstruction.
    /// A single hit may be a transient unit or a frame-boundary read and must not reroute.
    /// </summary>
    internal sealed class LiveCollisionTracker
    {
        private const float MinimumHitDistance = 0.75f;
        private const float EndpointClearance = 1.0f;
        private const float ConsistencyRadiusSqr = 4.0f;

        private WoWPoint _lastHit = WoWPoint.Zero;
        private int _consistentHitCount;

        internal bool Observe(
            bool traceHit,
            WoWPoint hitPoint,
            float hitDistance,
            float probeDistance)
        {
            if (!traceHit
                || hitPoint == WoWPoint.Zero
                || hitDistance < MinimumHitDistance
                || probeDistance - hitDistance < EndpointClearance)
            {
                Reset();
                return false;
            }

            _consistentHitCount = _lastHit != WoWPoint.Zero
                && _lastHit.Distance2DSqr(hitPoint) <= ConsistencyRadiusSqr
                    ? _consistentHitCount + 1
                    : 1;
            _lastHit = hitPoint;

            if (_consistentHitCount < 2)
                return false;

            Reset();
            return true;
        }

        internal void Reset()
        {
            _lastHit = WoWPoint.Zero;
            _consistentHitCount = 0;
        }
    }
}
