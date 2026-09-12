using System;
using Styx.Logic.Pathing;

namespace Styx.Logic
{
    /// <summary>Counts actual life transitions, not corpse-retrieval attempts.</summary>
    public sealed class CorpseRecoveryState
    {
        private bool? _wasAlive;
        private uint _map;
        private DateTime? _resurrectedAt;
        private WoWPoint _resurrectionPoint;
        public bool RepeatedDeath { get; private set; }

        public void Reset()
        {
            _wasAlive = null;
            _resurrectedAt = null;
            RepeatedDeath = false;
        }

        public void Observe(bool alive, uint map, WoWPoint location, DateTime now)
        {
            if (_wasAlive == null || _map != map)
            {
                _wasAlive = alive;
                _map = map;
                _resurrectedAt = null;
                RepeatedDeath = false;
                return;
            }
            if (alive && _wasAlive == false)
            {
                _resurrectedAt = now;
                _resurrectionPoint = location;
                RepeatedDeath = false;
            }
            else if (!alive && _wasAlive == true)
            {
                RepeatedDeath = _resurrectedAt.HasValue &&
                    now - _resurrectedAt.Value <= TimeSpan.FromMinutes(2) &&
                    location.Distance(_resurrectionPoint) <= 100f;
            }
            _wasAlive = alive;
        }

        public static bool ShouldUseHealer(bool enabled, bool inInstance, bool inBattleground,
            bool repeatedDeath, bool noSafePoint) =>
            enabled && !inInstance && !inBattleground && (repeatedDeath || noSafePoint);

        public static bool CanRetrieve(bool isGhost, bool pointIsSafe, bool healerRequested,
            bool recoveryDelayReady) => isGhost && pointIsSafe && !healerRequested && recoveryDelayReady;
    }
}
