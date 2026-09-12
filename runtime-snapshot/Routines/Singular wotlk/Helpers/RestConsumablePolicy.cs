namespace Singular.Helpers
{
    internal static class RestConsumablePolicy
    {
        internal static bool CanEat(int configuredFoodAmount)
        {
            return configuredFoodAmount > 0;
        }

        internal static bool CanDrink(int configuredDrinkAmount)
        {
            return configuredDrinkAmount > 0;
        }

        internal static bool CanUseAutomaticConsumables(bool isInstance)
        {
            return !isInstance;
        }

        internal static bool ShouldWait(bool healthLow, bool manaLow, bool foodEnabled, bool drinkEnabled)
        {
            return (healthLow && foodEnabled) || (manaLow && drinkEnabled);
        }
    }
}
