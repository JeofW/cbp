using System;
using System.Linq;
using System.Threading;
using Styx.Helpers;
using Styx.Logic.Inventory;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic.Common;

/// <summary>
/// Handles resting (eating/drinking) functionality.
/// </summary>
public static class Rest
{
    /// <summary>
    /// Gets or sets the health percentage threshold for resting.
    /// </summary>
    public static double RestPercentageHealth { get; set; }

    /// <summary>
    /// Gets or sets the mana percentage threshold for resting.
    /// </summary>
    public static double RestPercentageMana { get; set; }

    /// <summary>
    /// Gets whether the player has no food available.
    /// </summary>
    public static bool NoFood { get; private set; }

    // Throttle timers to prevent spamming food/drink every pulse (HB 4.3.4 bugfix)
    private static readonly WaitTimer _feedTimer = new(TimeSpan.FromSeconds(5));
    private static readonly WaitTimer _drinkTimer = new(TimeSpan.FromSeconds(5));

    /// <summary>
    /// Gets whether the player has no drink available.
    /// </summary>
    public static bool NoDrink { get; private set; }

    private static object? _legacyFeedOwner;

    /// <summary>
    /// Makes the player eat and drink to restore health and mana.
    /// </summary>
    public static void Feed()
    {
        // A nested call supersedes this invocation even when the same player
        // and item settings remain. Old cleanup must not clear the new owner.
        var owner = new object();
        _legacyFeedOwner = owner;
        try
        {
            var me = ObjectManager.Me;
            var memory = ObjectManager.Wow;
            if (memory == null || me == null || !CanUseConsumables(me, requireStationary: false))
                return;
            uint address = me.BaseAddress;
            ulong guid = me.Guid;
            string foodName = LevelbotSettings.Instance.FoodName;
            string drinkName = LevelbotSettings.Instance.DrinkName;
            bool StillAdmitted(bool requireStationary = true) =>
                ReferenceEquals(_legacyFeedOwner, owner) && ReferenceEquals(ObjectManager.Wow, memory)
                && me.BaseAddress == address && me.Guid == guid
                && LevelbotSettings.Instance.FoodName == foodName && LevelbotSettings.Instance.DrinkName == drinkName
                && CanUseConsumables(me, requireStationary)
                && ReferenceEquals(_legacyFeedOwner, owner) && ReferenceEquals(ObjectManager.Wow, memory)
                && me.BaseAddress == address && me.Guid == guid;
            if (!StillAdmitted(false))
                return;

            if (me.CurrentTarget != null)
            {
                ulong targetGuid = me.CurrentTargetGuid;
                Logging.Write("Resting.");
                if (!StillAdmitted(false) || me.CurrentTargetGuid != targetGuid)
                    return;
                me.ClearTarget();
                if (!StillAdmitted(false))
                    return;
            }

            if (me.IsMoving)
                WoWMovement.MoveStop();
            // A movement-stop request is not acknowledgement that we can eat.
            if (!StillAdmitted())
                return;

            if (!string.IsNullOrEmpty(foodName) && me.HealthPercent <= 55.0 && !me.Auras.ContainsKey("Food"))
            {
                var escapedFood = Lua.Escape(foodName);
                var foodCount = Lua.GetReturnVal<int>($"return GetItemCount(\"{escapedFood}\")", 0);
                if (!StillAdmitted())
                    return;
                if (foodCount > 0)
                {
                    Logging.Write("Eating {0}", foodName);
                    if (!StillAdmitted() || me.HealthPercent > 55.0 || me.Auras.ContainsKey("Food"))
                        return;
                    Lua.DoString($"UseItemByName(\"{escapedFood}\")");
                    if (!StillAdmitted())
                        return;
                    NoFood = false;
                }
                else
                {
                    NoFood = true;
                    Logging.Write("No {0} in bags.", foodName);
                }
            }

            // A food/logging callback can change the world or start another rest.
            if (!StillAdmitted())
                return;
            if (!string.IsNullOrEmpty(drinkName) && me.ManaPercent <= 55.0 && !me.Auras.ContainsKey("Drink"))
            {
                var escapedDrink = Lua.Escape(drinkName);
                var drinkCount = Lua.GetReturnVal<int>($"return GetItemCount(\"{escapedDrink}\")", 0);
                if (!StillAdmitted())
                    return;
                if (drinkCount > 0)
                {
                    Logging.Write("Drinking {0}", drinkName);
                    if (!StillAdmitted() || me.ManaPercent > 55.0 || me.Auras.ContainsKey("Drink"))
                        return;
                    Lua.DoString($"UseItemByName(\"{escapedDrink}\")");
                    if (!StillAdmitted())
                        return;
                    NoDrink = false;
                }
                else
                {
                    NoDrink = true;
                    Logging.Write("No {0} in bags.", drinkName);
                }
            }

            if (!StillAdmitted())
                return;
            // Preserve the legacy synchronous API, but not stale permission
            // across its waits. The routine's immediate APIs stay nonblocking.
            if (!string.IsNullOrEmpty(drinkName) || !string.IsNullOrEmpty(foodName))
            {
                StyxWoW.Sleep(1000);
                while (StillAdmitted() && (me.Auras.ContainsKey("Food") || me.Auras.ContainsKey("Drink")))
                {
                    StyxWoW.Sleep(100);
                    if (!StillAdmitted())
                        return;
                    if (me.HealthPercent == 100.0 && me.ManaPercent == 100.0)
                        break;
                    if (me.HealthPercent == 100.0 && !me.Auras.ContainsKey("Drink"))
                        break;
                    if (me.ManaPercent == 100.0 && !me.Auras.ContainsKey("Food"))
                        break;
                }
            }
        }
        finally
        {
            if (ReferenceEquals(_legacyFeedOwner, owner))
                _legacyFeedOwner = null;
        }
    }

    /// <summary>
    /// Immediately uses food to restore health without waiting.
    /// Used by Singular for quick eating.
    /// </summary>
    public static void FeedImmediate() => UseImmediate(false);

    /// <summary>Immediately uses drink without waiting, on a current dry owner only.</summary>
    public static void DrinkImmediate() => UseImmediate(true);

    private static bool CanUseConsumables(LocalPlayer? player, bool requireStationary = true)
    {
        return player != null && ReferenceEquals(ObjectManager.Me, player)
            && player.IsValid && player.IsAlive && !player.IsGhost && !player.Combat
            && !player.Mounted && !player.IsOnTransport && (!requireStationary || !player.IsMoving)
            && !player.IsCasting && !player.IsChanneling
            && !LiquidEnvironment.IsPlayerInLiquid(player)
            && ReferenceEquals(ObjectManager.Me, player);
    }

    private static void UseImmediate(bool drinking)
    {
        var timer = drinking ? _drinkTimer : _feedTimer;
        if (!timer.IsFinished)
            return;
        var player = ObjectManager.Me;
        var memory = ObjectManager.Wow;
        uint address = player?.BaseAddress ?? 0U;
        bool StillAdmitted() => ReferenceEquals(ObjectManager.Wow, memory)
            && (player?.BaseAddress ?? 0U) == address && CanUseConsumables(player)
            && ReferenceEquals(ObjectManager.Wow, memory) && player!.BaseAddress == address;
        if (!StillAdmitted())
            return;

        WoWItem? item = drinking ? Consumable.GetBestDrink(false) : Consumable.GetBestFood(false);
        if (!StillAdmitted())
            return;
        // An ineligible environment is not missing inventory and must not spend
        // the retry interval. Both public entry points share the same admission.
        timer.Reset();
        if (item != null)
        {
            string name = item.Name;
            if (!StillAdmitted())
                return;
            Logging.Write(drinking ? "Drinking {0}" : "Eating {0}", name);
            // Logging/inventory observation can reenter or change the world.
            if (StillAdmitted())
                item.Use();
        }
        else
        {
            if (drinking) NoDrink = true; else NoFood = true;
            Logging.Write(drinking ? "Could not find any water to drink." : "Could not find any food to eat.");
        }
    }
}
