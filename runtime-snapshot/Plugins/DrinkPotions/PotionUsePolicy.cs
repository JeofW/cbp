using System;

namespace DrinkPotions
{
    public static class PotionUsePolicy
    {
        public static bool ShouldUsePotion(double currentPercent, int thresholdPercent)
        {
            return currentPercent < thresholdPercent;
        }

        public static bool ShouldSuppressAutomaticUse(string currentBotName, bool isInstance)
        {
            return isInstance;
        }

        public static bool ShouldEvaluate(DateTime nowUtc, DateTime nextEvaluationUtc)
        {
            return nowUtc >= nextEvaluationUtc;
        }
    }
}
