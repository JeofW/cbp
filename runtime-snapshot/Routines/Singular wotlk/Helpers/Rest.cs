using System;
using System.Linq;
using System.Threading;

using CommonBehaviors.Actions;

using Singular.Settings;
using Styx;
using Styx.Combat.CombatRoutine;
using Styx.Helpers;
using Styx.Logic.Combat;
using Styx.Logic.Inventory;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

using TreeSharp;
using Action = TreeSharp.Action;

namespace Singular.Helpers
{
    internal static class Rest
    {
        private static DateTime _nextFoodDiagnostic, _nextDrinkDiagnostic;
        private static string _foodAdmission = "unobserved", _drinkAdmission = "unobserved";
        private static CannibalizeLease _cannibalize;

        private sealed class CannibalizeLease
        {
            internal LocalPlayer Actor;
            internal object Memory, Run;
            internal ulong Guid;
            internal uint Address, Map;
        }

        private static bool CannibalizeOwnerCurrent(CannibalizeLease lease)
        {
            var actor = lease?.Actor;
            return actor != null && ReferenceEquals(_cannibalize, lease)
                && lease.Memory != null && lease.Run != null
                && ReferenceEquals(StyxWoW.Me, actor) && ReferenceEquals(ObjectManager.Me, actor)
                && ReferenceEquals(ObjectManager.Wow, lease.Memory)
                && ReferenceEquals(Styx.Logic.BehaviorTree.TreeRoot.RunIdentity, lease.Run)
                && actor.IsValid && actor.IsAlive && actor.Guid == lease.Guid && lease.Guid != 0
                && actor.BaseAddress == lease.Address && lease.Address != 0 && actor.MapId == lease.Map;
        }

        private static void ClearCannibalize(CannibalizeLease lease)
        {
            if (ReferenceEquals(_cannibalize, lease))
                _cannibalize = null;
        }

        private static bool CanPrepareCannibalize()
        {
            var actor = StyxWoW.Me;
            var memory = ObjectManager.Wow;
            var run = Styx.Logic.BehaviorTree.TreeRoot.RunIdentity;
            if (actor == null || memory == null || run == null || actor.Guid == 0 || actor.BaseAddress == 0
                || Styx.Logic.Common.Rest.GetAdmissionDenial(actor, requireStationary: false) != null)
                return false;
            var lease = new CannibalizeLease
            {
                Actor = actor, Memory = memory, Run = run, Guid = actor.Guid,
                Address = actor.BaseAddress, Map = actor.MapId
            };
            _cannibalize = lease;
            return CannibalizeOwnerCurrent(lease);
        }

        private static RunStatus SubmitCannibalize()
        {
            var lease = _cannibalize;
            if (!CannibalizeOwnerCurrent(lease)
                || Styx.Logic.Common.Rest.GetAdmissionDenial(lease.Actor) != null
                || !SpellManager.CanCast("Cannibalize") || !CorpseAround
                || !CannibalizeOwnerCurrent(lease))
            {
                ClearCannibalize(lease);
                return RunStatus.Failure;
            }
            bool submitted = SpellManager.Cast("Cannibalize");
            if (!submitted || !CannibalizeOwnerCurrent(lease))
            {
                ClearCannibalize(lease);
                return RunStatus.Failure;
            }
            // Local dispatch is not healing or channel acknowledgement. The next
            // pulse must observe the actual Cannibalize cast before it is retained.
            return RunStatus.Success;
        }

        private static bool ContinueCannibalize()
        {
            var lease = _cannibalize;
            if (!CannibalizeOwnerCurrent(lease))
            {
                ClearCannibalize(lease);
                return false;
            }
            var casting = lease.Actor.CastingSpell;
            if ((!lease.Actor.IsCasting && !lease.Actor.IsChanneling)
                || casting == null || casting.Name != "Cannibalize"
                || Styx.Logic.Common.Rest.GetContinuationDenial(lease.Actor) != null
                || !CannibalizeOwnerCurrent(lease))
            {
                ClearCannibalize(lease);
                return false;
            }
            casting = lease.Actor.CastingSpell;
            return CannibalizeOwnerCurrent(lease) && casting != null && casting.Name == "Cannibalize";
        }

        private static bool CanStartConsumable(bool drinking)
        {
            var player = StyxWoW.Me;
            string reason = player == null ? "actor-unavailable" : null;
            if (reason == null)
            {
                int configured = drinking ? CharacterSettings.Instance.DrinkAmount : CharacterSettings.Instance.FoodAmount;
                double current = drinking ? player.ManaPercent : player.HealthPercent;
                double threshold = drinking ? SingularSettings.Instance.MinMana : SingularSettings.Instance.MinHealth;
                if (configured <= 0) reason = "configured-amount-disabled";
                else if (drinking && player.PowerType != WoWPowerType.Mana && player.Class != WoWClass.Druid) reason = "no-mana-resource";
                else if (current > threshold) reason = "above-threshold";
                else if (!Styx.Logic.Common.Rest.IsConsumableRetryReady(drinking)) reason = "retry-throttled";
            }
            if (reason == null)
                reason = Styx.Logic.Common.Rest.GetAdmissionDenial(player, requireStationary: false);
            if (reason == null && !RecoveryActions.CanPrepareRestConsumable(!drinking, drinking))
                reason = "recovery-owner-pending-or-unavailable";
            if (reason == null)
            {
                if (!Styx.Logic.Common.Rest.TryObserveActivity(player, out bool food, out bool drink)) reason = "aura-coverage-UNKNOWN";
                else if (drinking ? drink : food) reason = "rest-aura-already-active";
                else
                {
                    var observed = drinking ? Consumable.ObserveBestDrink(false) : Consumable.ObserveBestFood(false);
                    if (observed.Item == null) reason = (observed.IsComplete ? "inventory-known:" : "inventory-UNKNOWN:") + observed.Reason;
                }
            }
            if (drinking) _drinkAdmission = reason ?? "admitted"; else _foodAdmission = reason ?? "admitted";
            DateTime next = drinking ? _nextDrinkDiagnostic : _nextFoodDiagnostic;
            if (DateTime.UtcNow >= next && player != null &&
                (player.HealthPercent <= SingularSettings.Instance.MinHealth || player.ManaPercent <= SingularSettings.Instance.MinMana))
            {
                Logger.WriteDebug("Rest {0}: reason={1}; hp={2:F1}/{3}; mana={4:F1}/{5}; food-setting={6}; drink-setting={7}.",
                    drinking ? "drink" : "food", reason ?? "admitted", player.HealthPercent, SingularSettings.Instance.MinHealth,
                    player.ManaPercent, SingularSettings.Instance.MinMana, CharacterSettings.Instance.FoodAmount, CharacterSettings.Instance.DrinkAmount);
                if (drinking) _nextDrinkDiagnostic = DateTime.UtcNow.AddSeconds(5); else _nextFoodDiagnostic = DateTime.UtcNow.AddSeconds(5);
            }
            return reason == null && ReferenceEquals(player, StyxWoW.Me);
        }

        private static bool ContinueSupportedRest()
        {
            var player = StyxWoW.Me;
            return Styx.Logic.Common.Rest.GetAdmissionDenial(player) == null
                && Styx.Logic.Common.Rest.TryObserveActivity(player, out bool food, out bool drink)
                && ((food && player.HealthPercent < 95) || (drink && player.PowerType == WoWPowerType.Mana && player.ManaPercent < 95));
        }

        private static bool CorpseAround
        {
            get
            {
                return ObjectManager.GetObjectsOfType<WoWUnit>(true, false).Any(
                    u => u.Distance < 5 && u.Dead &&
                         (u.CreatureType == WoWCreatureType.Humanoid || u.CreatureType == WoWCreatureType.Undead));
            }
        }

        private static bool PetInCombat
        {
            get { return StyxWoW.Me.GotAlivePet && StyxWoW.Me.PetInCombat; }
        }

        public static Composite CreateDefaultRestBehaviour()
        {
            return

                // Don't fucking run the rest behavior (or any other) if we're dead or a ghost. Thats all.
                new Decorator(
                    ret => !StyxWoW.Me.Dead && !StyxWoW.Me.IsGhost && !StyxWoW.Me.IsCasting,
                    new PrioritySelector(
                // Make sure we wait out res sickness. Fuck the classes that can deal with it. :O
                        new Decorator(
                            ret => SingularSettings.Instance.WaitForResSickness && StyxWoW.Me.HasAura(15007),
                            new Action(ret => { })),
                // Wait while cannibalizing
                        new Decorator(
                            ret => ContinueCannibalize() &&
                                   (StyxWoW.Me.HealthPercent < 95 || (StyxWoW.Me.PowerType == WoWPowerType.Mana && StyxWoW.Me.ManaPercent < 95)),
                            new Sequence(
                                new Action(ret => Logger.Write("Waiting for Cannibalize")),
                                new ActionAlwaysSucceed())),
                // Cannibalize support goes before drinking/eating (only for health, not mana!)
                        new Decorator(
                            ret =>
                            StyxWoW.Me.HealthPercent <= SingularSettings.Instance.MinHealth &&
                            SpellManager.CanCast("Cannibalize") && CorpseAround && CanPrepareCannibalize(),
                            new Sequence(
                                new Action(ret => Navigator.PlayerMover.MoveStop()),
                                Helpers.Common.CreateWaitForLagDuration(),
                                new Action(ret => SubmitCannibalize()),
                                new WaitContinue(1, ret => false, new ActionAlwaysSucceed()))),
                // Check if we're allowed to eat (and make sure we have some food. Don't bother going further if we have none.
                        new Decorator(
                            ret =>
                            CanStartConsumable(false),
                            new PrioritySelector(
                                new Decorator(
                                    ret => StyxWoW.Me.IsMoving,
                                    new Action(ret => Navigator.PlayerMover.MoveStop())),
                                new Sequence(
                                    new Action(
                                        ret =>
                                        {
                                            return Styx.Logic.Common.Rest.TryFeedImmediate() ? RunStatus.Success : RunStatus.Failure;
                                        }),
                                    Helpers.Common.CreateWaitForLagDuration()))),
                // Make sure we're a class with mana, if not, just ignore drinking all together! Other than that... same for food.
                        new Decorator(
                            ret =>
                            CanStartConsumable(true),
                            new PrioritySelector(
                                new Decorator(
                                    ret => StyxWoW.Me.IsMoving,
                                    new Action(ret => Navigator.PlayerMover.MoveStop())),
                                new Sequence(
                                    new Action(ret =>
                                        {
                                            return Styx.Logic.Common.Rest.TryDrinkImmediate() ? RunStatus.Success : RunStatus.Failure;
                                        }),
                                    Helpers.Common.CreateWaitForLagDuration()))),
                // This is to ensure we STAY SEATED while eating/drinking. No reason for us to get up before we have to.
                        new Decorator(
                            ret =>
                            ContinueSupportedRest(),
                            new ActionAlwaysSucceed()),
                        new Decorator(
                            ret =>
                            Styx.Logic.Common.Rest.GetAdmissionDenial(StyxWoW.Me) == null &&
                            RestConsumablePolicy.ShouldWait(
                                StyxWoW.Me.HealthPercent <= SingularSettings.Instance.MinHealth,
                                StyxWoW.Me.PowerType == WoWPowerType.Mana && StyxWoW.Me.ManaPercent <= SingularSettings.Instance.MinMana,
                                RestConsumablePolicy.CanEat(CharacterSettings.Instance.FoodAmount),
                                RestConsumablePolicy.CanDrink(CharacterSettings.Instance.DrinkAmount)) &&
                            !StyxWoW.Me.CurrentMap.IsBattleground,
                            new Sequence(
                                new Action(ret => Logger.Write("Waiting for recovery; food={0}; drink={1}.", _foodAdmission, _drinkAdmission)),
                                new WaitContinue(3, ret => StyxWoW.Me.Combat || (StyxWoW.Me.HealthPercent >= 85 && StyxWoW.Me.ManaPercent >= 85), new ActionAlwaysSucceed())))
                        ));
        }

    }
}
