namespace WholesomeAQ
{
    internal static class WholesomeRestPolicy
    {
        internal const double CriticalHealthPercent = 30;

        internal static bool ShouldRunQuestRoot(bool restingPaused)
        {
            return !restingPaused;
        }

        internal static bool HasImmediateThreat(bool inCombat, bool hostileTargetsMeOrPet)
        {
            return inCombat || hostileTargetsMeOrPet;
        }

        internal static bool ShouldStartRest(
            double healthPercent,
            double manaPercent,
            bool usesMana,
            bool hasPendingLoot,
            bool hasImmediateThreat,
            WholesomeAQSettings settings)
        {
            if (hasImmediateThreat)
                return false;

            bool needsRest = healthPercent <= settings.RestHealthPercent
                || usesMana && manaPercent <= settings.RestManaPercent;
            return needsRest && (!hasPendingLoot || healthPercent <= CriticalHealthPercent);
        }

        internal static bool ShouldYieldRest(
            double healthPercent,
            bool hasPendingLoot,
            bool hasImmediateThreat)
        {
            return hasImmediateThreat
                || hasPendingLoot && healthPercent > CriticalHealthPercent;
        }

        internal static bool IsRecovered(
            double healthPercent,
            double manaPercent,
            bool usesMana,
            WholesomeAQSettings settings)
        {
            return healthPercent >= settings.RestResumeHealthPercent
                && (!usesMana || manaPercent >= settings.RestResumeManaPercent);
        }
    }
}
