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
    private static long _nextFoodAdmissionDiagnostic, _nextDrinkAdmissionDiagnostic;

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
            uint map = me.MapId;
            var run = Styx.Logic.BehaviorTree.TreeRoot.RunIdentity;
            string foodName = LevelbotSettings.Instance.FoodName;
            string drinkName = LevelbotSettings.Instance.DrinkName;
            bool OwnsRest() =>
                ReferenceEquals(_legacyFeedOwner, owner) && ReferenceEquals(ObjectManager.Wow, memory)
                && ReferenceEquals(ObjectManager.Me, me) && ReferenceEquals(run, Styx.Logic.BehaviorTree.TreeRoot.RunIdentity)
                && me.BaseAddress == address && me.Guid == guid && me.MapId == map
                && LevelbotSettings.Instance.FoodName == foodName && LevelbotSettings.Instance.DrinkName == drinkName;
            bool StillAdmitted(bool requireStationary = true) =>
                OwnsRest() && CanUseConsumables(me, requireStationary) && OwnsRest();
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

            if (!TryObserveActivity(me, out bool food, out bool drink) || !StillAdmitted()) return;
            if (!string.IsNullOrEmpty(foodName) && me.HealthPercent <= 55.0 && !food)
                UseImmediate(false, foodName, () => OwnsRest() && me.HealthPercent <= 55.0);

            // A food/logging callback can change settings, world or rest owner.
            if (!StillAdmitted()) return;
            if (!string.IsNullOrEmpty(drinkName) && me.ManaPercent <= 55.0
                && TryObserveActivity(me, out _, out drink) && !drink)
                UseImmediate(true, drinkName, () => OwnsRest() && me.ManaPercent <= 55.0);

            if (!StillAdmitted())
                return;
            // Preserve the legacy synchronous API, but not stale permission
            // across its waits. The routine's immediate APIs stay nonblocking.
            if (!string.IsNullOrEmpty(drinkName) || !string.IsNullOrEmpty(foodName))
            {
                StyxWoW.Sleep(1000);
                while (StillAdmitted() && TryObserveActivity(me, out food, out drink) && (food || drink))
                {
                    StyxWoW.Sleep(100);
                    if (!StillAdmitted())
                        return;
                    if (me.HealthPercent == 100.0 && me.ManaPercent == 100.0)
                        break;
                    if (me.HealthPercent == 100.0 && !drink)
                        break;
                    if (me.ManaPercent == 100.0 && !food)
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

    /// <summary>Returns submission success, not a server Food/Drink aura acknowledgement.</summary>
    public static bool TryFeedImmediate() => UseImmediate(false);
    public static bool TryDrinkImmediate() => UseImmediate(true);

    /// <summary>Cheap retry admission; ongoing rest still requires current safety observations.</summary>
    public static bool IsConsumableRetryReady(bool drinking) => (drinking ? _drinkTimer : _feedTimer).IsFinished;

    /// <summary>
    /// Observe supported original-client food/drink activity from complete raw
    /// aura records. Unknown spell metadata is not a missing raw observation.
    /// This does not classify arbitrary server-defined recovery mechanics.
    /// </summary>
    public static bool TryObserveActivity(LocalPlayer? player, out bool food, out bool drink)
    {
        food = drink = false;
        var memory = ObjectManager.Wow;
        if (player == null || memory == null || !ReferenceEquals(player, ObjectManager.Me) || !player.IsValid)
            return false;
        ulong guid = player.Guid;
        uint address = player.BaseAddress;
        if (!player.TryGetRawAuras(out var auras, "supported rest activity") || auras == null)
            return false;
        bool observedFood = auras.Any(a => RestSpellFamilies.Food.Contains(a.SpellId));
        bool observedDrink = auras.Any(a => RestSpellFamilies.Drink.Contains(a.SpellId));
        if (!ReferenceEquals(player, ObjectManager.Me) || !ReferenceEquals(memory, ObjectManager.Wow)
            || player.Guid != guid || player.BaseAddress != address || !player.IsValid)
            return false;
        food = observedFood;
        drink = observedDrink;
        return true;
    }

    private static bool CanUseConsumables(LocalPlayer? player, bool requireStationary = true)
        => GetAdmissionDenial(player, requireStationary) == null;

    private static void ReportAdmissionDenial(bool drinking, string reason)
    {
        long now = Environment.TickCount64;
        ref long next = ref (drinking ? ref _nextDrinkAdmissionDiagnostic : ref _nextFoodAdmissionDiagnostic);
        if (now < next) return;
        next = now + 5000;
        Logging.WriteDebug("Rest {0} denied: {1}.", drinking ? "drink" : "food", reason);
    }

    /// <summary>First decisive rest denial; null means this observation admits rest.</summary>
    public static string? GetAdmissionDenial(LocalPlayer? player, bool requireStationary = true, bool allowQueries = true)
        => GetAdmissionDenialCore(player, requireStationary, allowQueries, false);

    /// <summary>
    /// Current safety for an already-observed recovery cast/channel. Casting or
    /// channeling itself is expected here; every other rest-safety observation
    /// remains authoritative and UNKNOWN still denies continuation.
    /// </summary>
    public static string? GetContinuationDenial(LocalPlayer? player, bool requireStationary = true, bool allowQueries = true)
        => GetAdmissionDenialCore(player, requireStationary, allowQueries, true);

    private static string? GetAdmissionDenialCore(LocalPlayer? player, bool requireStationary,
        bool allowQueries, bool allowCastingOrChanneling)
    {
        if (player == null || !ReferenceEquals(ObjectManager.Me, player) || !player.IsValid) return "actor-unavailable";
        if (!player.IsAlive || player.IsGhost) return "dead-or-ghost";
        Styx.WoWInternals.World.WorldQueryObservation.GroundUnitState ground;
        try { ground = Styx.WoWInternals.World.WorldQueryObservation.ReadGroundUnitState(player); }
        catch (Styx.Helpers.ObservationUnavailableException) { return "ground-unit-state-unknown"; }

        // Original build12340 UNIT_FIELD_FLAGS values. The strict ground-state
        // reader proves the complete descriptor bytes before these bits can deny
        // or admit automatic recovery; failed legacy descriptor reads no longer
        // become false combat/mount/taxi observations.
        const uint petInCombat = 0x00000800u, inCombat = 0x00080000u;
        if ((ground.Flags & (petInCombat | inCombat)) != 0) return "actor-or-pet-combat";

        if (!player.TryGetMovementState(out uint movementFlags, out ulong transportGuid))
            return "movement-state-unknown";
        // Original build12340 movement layout: directional/pitch MotionMask
        // 0x000000FF, transport 0x00000200, flying 0x02000000.
        if ((movementFlags & 0x02000000u) != 0) return "flying";
        if (ground.Mounted) return "mounted";
        if (ground.OnTaxi || transportGuid != 0 || (movementFlags & 0x00000200u) != 0) return "transport";
        if (requireStationary && (movementFlags & 0x000000FFu) != 0) return "moving";
        if (!allowCastingOrChanneling && (player.IsCasting || player.IsChanneling)) return "casting-or-channeling";
        if (LiquidEnvironment.IsPlayerInLiquid(player, allowQueries)) return "liquid-or-dry-observation-unavailable";
        var map = player.CurrentMap;
        if (map == null) return "map-unavailable";
        if (map.IsBattleground) return "battleground";
        if (map.IsInstance)
        {
            if (!Styx.Logic.GroupObservation.TryGetMembers(player, out var members, out string reason, allowQueries))
                return "instance-roster-unknown:" + reason;
            foreach (var member in members)
            {
                if (!member.IsValid || !member.IsAlive || member.IsGhost) return "instance-member-unavailable-or-dead";
                if (member.Combat || member.PetInCombat) return "instance-group-combat";
                if (member.IsMoving && (requireStationary || !ReferenceEquals(member, player))) return "instance-group-moving";
            }
        }
        return ReferenceEquals(ObjectManager.Me, player) ? null : "actor-replaced";
    }

    private static bool UseImmediate(bool drinking, string? configuredName = null, Func<bool>? additionalAdmission = null)
    {
        var timer = drinking ? _drinkTimer : _feedTimer;
        if (!timer.IsFinished)
            return false;
        var player = ObjectManager.Me;
        var memory = ObjectManager.Wow;
        uint address = player?.BaseAddress ?? 0U;
        ulong guid = player?.Guid ?? 0UL;
        uint map = player?.MapId ?? 0U;
        var run = Styx.Logic.BehaviorTree.TreeRoot.RunIdentity;
        bool OwnsRest() => player != null && memory != null && ReferenceEquals(ObjectManager.Wow, memory)
            && ReferenceEquals(player, ObjectManager.Me)
            && ReferenceEquals(run, Styx.Logic.BehaviorTree.TreeRoot.RunIdentity)
            && (additionalAdmission == null || additionalAdmission())
            && player.BaseAddress == address && player.Guid == guid && player.MapId == map;
        bool StillAdmitted() => OwnsRest() && CanUseConsumables(player) && OwnsRest();
        if (!StillAdmitted())
        {
            ReportAdmissionDenial(drinking, GetAdmissionDenial(player) ?? "owner-replaced");
            return false;
        }

        if (!Styx.Logic.Combat.RecoveryActions.CanPrepareRestConsumable(!drinking, drinking))
        {
            ReportAdmissionDenial(drinking, "recovery-owner-pending-or-unavailable");
            return false;
        }
        if (!StillAdmitted()) return false;
        var observation = configuredName != null ? Consumable.ObserveNamedRestItem(drinking, configuredName)
            : drinking ? Consumable.ObserveBestDrink(false) : Consumable.ObserveBestFood(false);
        WoWItem? item = observation.Item;
        if (!StillAdmitted())
            return false;
        // An ineligible environment is not missing inventory and must not spend
        // the retry interval. Both public entry points share the same admission.
        timer.Reset();
        Logging.WriteDebug("Rest {0} inventory: complete={1}; reason={2}; candidates={3}.",
            drinking ? "drink" : "food", observation.IsComplete, observation.Reason, observation.Details);
        if (!StillAdmitted()) return false;
        if (item != null)
        {
            string name = item.Name;
            if (!StillAdmitted())
                return false;
            // A previous empty or unhydrated observation is not permanent.
            if (drinking) NoDrink = false; else NoFood = false;
            Logging.Write(drinking ? "Requesting drink: {0}" : "Requesting food: {0}", name);
            // Logging/inventory observation can reenter or change the world.
            if (!StillAdmitted()) return false;
            bool submitted = Styx.Logic.Combat.RecoveryActions.TryUseRestConsumable(item, !drinking, drinking,
                drinking ? "rest drink" : "rest food", OwnsRest);
            if (!submitted)
                Logging.WriteDebug("Rest item request was declined; retry remains bounded by the consumable timer.");
            return submitted && ReferenceEquals(ObjectManager.Me, player)
                && ReferenceEquals(ObjectManager.Wow, memory) && player!.BaseAddress == address
                && player.Guid == guid && player.MapId == map
                && ReferenceEquals(run, Styx.Logic.BehaviorTree.TreeRoot.RunIdentity);
        }
        else
        {
            if (drinking) NoDrink = observation.IsComplete; else NoFood = observation.IsComplete;
            if (observation.IsComplete)
            {
                if (configuredName != null) Logging.Write("No {0} in bags.", configuredName);
                else Logging.Write(drinking ? "No usable water observed in bags." : "No usable food observed in bags.");
            }
            else
                Logging.WriteDebug("Rest {0} deferred: status=UNKNOWN reason={1}.", drinking ? "drink" : "food", observation.Reason);
        }
        return false;
    }
}
