using System;

namespace Styx.Logic
{
    /// <summary>
    /// Pure safety decisions shared by grind recovery and external plugins.
    /// </summary>
    public static class GrindSafetyPolicy
    {
        public static bool ShouldAbortFleeingChase(
            bool isFleeing,
            bool taggedByMe,
            bool isTargetingMeOrPet,
            float distance,
            bool isInsideAvoidedPack)
        {
            return isFleeing
                   && taggedByMe
                   && !isTargetingMeOrPet
                   && distance > 8f
                   && isInsideAvoidedPack;
        }

        public static bool CanLoot(bool playerCombat, bool petCombat)
        {
            return !playerCombat && !petCombat;
        }

        public static bool ShouldUseInstancePortal(
            bool diedInInstance,
            bool isGhost,
            bool hasPortalDestination)
        {
            return diedInInstance && isGhost && hasPortalDestination;
        }

        public static bool IsHostileSafeForResurrection(float distance, float aggroRange)
        {
            return distance >= Math.Max(25f, aggroRange + 5f);
        }

        public static bool ShouldRetrieveCorpse(bool waitTimerExpired, bool pointIsSafe)
        {
            return pointIsSafe;
        }

        public static bool ShouldCreateAvoidanceBlackspot(
            float nearestExistingDistance,
            float blackspotRadius)
        {
            return nearestExistingDistance > Math.Max(0f, blackspotRadius);
        }
    }
}
