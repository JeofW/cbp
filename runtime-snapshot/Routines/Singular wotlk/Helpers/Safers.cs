using System;
using System.Drawing;
using System.Linq;
using CommonBehaviors.Actions;
using Singular.Settings;

using Styx;
using Styx.Logic;
using Styx.Logic.POI;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
using Singular.Managers;
using Action = TreeSharp.Action;

namespace Singular.Helpers
{
    internal static class Safers
    {
        /// <summary>
        ///  This behavior SHOULD be called at top of the combat behavior. This behavior won't let the rest of the combat behavior to be called
        /// if you don't have a target. Also it will find a proper target, if the current target is dead or you don't have a target and still in combat.
        /// Tank targeting is also dealed in this behavior.
        /// </summary>
        /// <returns></returns>
        public static Composite EnsureTarget()
        {
            WoWUnit actor = null, displayed = null;
            ulong actorGuid = 0, displayedGuid = 0;
            SingularSettings settings = null;
            bool tank = false, disableTankSwitching = false;
            bool Current() => actorGuid != 0 && actor != null && actor.IsValid && actor.IsAlive
                && actor.Guid == actorGuid && ReferenceEquals(actor, StyxWoW.Me)
                && ReferenceEquals(settings, SingularSettings.Instance) && !settings.DisableAllTargeting
                && settings.DisableTankTargetSwitching == disableTankSwitching && Group.MeIsTank == tank;
            bool SameDisplay() => Current() && ReferenceEquals(actor.CurrentTarget, displayed)
                && (actor.CurrentTarget?.Guid ?? 0) == displayedGuid && Current();

            WoWUnit Preferred()
            {
                if (!Current() || tank && !disableTankSwitching) return null;
                var target = actor.CurrentTarget;
                if (target == null || target.Dead) return null;
                // A stun or an absent victim target does not invalidate an engaged enemy.
                if ((target.Combat || target.Aggro) && target.IsEligibleDungeonCombatTarget()) return null;
                if (BotPoi.Current.Type == PoiType.Kill)
                {
                    var unit = BotPoi.Current.AsObject as WoWUnit;
                    if (unit != null && unit.IsEligibleDungeonCombatTarget() && actor.CurrentTarget != unit)
                        return unit;
                }
                var first = Targeting.Instance.FirstUnit;
                return first != null && first.IsEligibleDungeonCombatTarget() && actor.CurrentTarget != first ? first : null;
            }

            WoWUnit Recovery()
            {
                if (!Current()) return null;
                var leader = RaFHelper.Leader;
                var leaderTarget = leader?.CurrentTarget;
                if (leader != null && leader.IsValid && !leader.IsMe && leader.Combat
                    && leaderTarget != null && leaderTarget.IsAlive
                    && leaderTarget.IsEligibleDungeonCombatTarget() && !Blacklist.Contains(leaderTarget))
                    return leaderTarget;
                if (BotPoi.Current.Type == PoiType.Kill)
                {
                    var unit = BotPoi.Current.AsObject as WoWUnit;
                    if (unit != null && unit.IsAlive && !unit.IsMe
                        && unit.IsEligibleDungeonCombatTarget() && !Blacklist.Contains(unit)) return unit;
                }
                var first = Targeting.Instance.FirstUnit;
                if (first != null && first.IsAlive && !first.IsMe && first.Combat
                    && first.IsEligibleDungeonCombatTarget() && !Blacklist.Contains(first)) return first;
                float range = DungeonEngagementPolicy.GetFallbackRange(Unit.IsDungeonCombatBotTargetingRestricted);
                return ObjectManager.GetObjectsOfType<WoWUnit>(false, false)
                    .Where(unit => unit != null && !Blacklist.Contains(unit) && unit.IsHostile
                        && !unit.IsOnTransport && !unit.Dead && !unit.Mounted
                        && unit.DistanceSqr <= range * range && unit.Combat && unit.IsEligibleDungeonCombatTarget())
                    .OrderBy(unit => unit.DistanceSqr).FirstOrDefault();
            }

            Composite Switch(Func<WoWUnit> select, string message, bool tankSwitch, bool fallThrough)
            {
                WoWUnit selected = null;
                ulong selectedGuid = 0;
                object timer = null;
                bool ParticipantCurrent() => Current() && selectedGuid != 0 && selected != null
                    && selected.IsValid && selected.IsAlive && !selected.IsMe && selected.Guid == selectedGuid
                    && selected.IsEligibleDungeonCombatTarget() && Current();
                bool CanSubmit() => ParticipantCurrent() && SameDisplay()
                    && ReferenceEquals(select(), selected) && ParticipantCurrent() && SameDisplay();
                bool Acknowledged() => ParticipantCurrent() && ReferenceEquals(actor.CurrentTarget, selected)
                    && actor.CurrentTargetGuid == selectedGuid && ParticipantCurrent();
                return new Sequence(
                    new Action(ret =>
                    {
                        selected = null; selectedGuid = 0;
                        if (!SameDisplay()) return RunStatus.Failure;
                        selected = select(); selectedGuid = selected?.Guid ?? 0;
                        timer = TankManager.TargetingTimer;
                        return CanSubmit() ? RunStatus.Success : RunStatus.Failure;
                    }),
                    new Action(ret => Logger.Write(Color.Orange, message + selected.SafeName() + "!")),
                    new Action(ret =>
                    {
                        // Diagnostics and selectors can replace the actor or selection.
                        if (!CanSubmit()) return RunStatus.Failure;
                        selected.Target();
                        return ParticipantCurrent() ? RunStatus.Success : RunStatus.Failure;
                    }),
                    new WaitContinue(2,
                        ret => !ParticipantCurrent() || !SameDisplay() || Acknowledged(),
                        new ActionAlwaysSucceed()),
                    // WaitContinue reports timeout as success. Require actual selection.
                    new Action(ret => Acknowledged() ? RunStatus.Success : RunStatus.Failure),
                    tankSwitch ? Helpers.Common.CreateWaitForLagDuration() : new ActionAlwaysSucceed(),
                    new Action(ret =>
                    {
                        if (!Acknowledged()) return RunStatus.Failure;
                        if (tankSwitch)
                        {
                            if (!ReferenceEquals(timer, TankManager.TargetingTimer)) return RunStatus.Failure;
                            TankManager.TargetingTimer.Reset();
                        }
                        return fallThrough ? RunStatus.Failure : RunStatus.Success;
                    }));
            }

            return new Sequence(
                new Action(ret =>
                {
                    actor = StyxWoW.Me; actorGuid = actor?.Guid ?? 0;
                    settings = SingularSettings.Instance;
                    tank = Group.MeIsTank; disableTankSwitching = settings.DisableTankTargetSwitching;
                    displayed = actor?.CurrentTarget; displayedGuid = displayed?.Guid ?? 0;
                    return Current() ? RunStatus.Success : RunStatus.Failure;
                }),
                new PrioritySelector(
                    new Decorator(ret => Current() && tank && !disableTankSwitching
                        && TankManager.TargetingTimer.IsFinished && actor.Combat
                        && TankManager.Instance.FirstUnit != null
                        && TankManager.Instance.FirstUnit.IsEligibleDungeonCombatTarget()
                        && actor.CurrentTarget != TankManager.Instance.FirstUnit,
                        Switch(() => TankManager.Instance.FirstUnit, "Targeting first unit of TankTargeting: ", true, false)),
                    // Successful preferred selection still falls through to spell priority.
                    Switch(Preferred, "Current target is not the best target. Switching to ", false, true),
                    new Decorator(ret => Current() && (actor.CurrentTarget == null || actor.CurrentTarget.Dead
                        || !actor.CurrentTarget.IsEligibleDungeonCombatTarget()),
                        Switch(Recovery, "Current target is invalid. Switching to ", false, false)),
                    // Preserve the manual unengaged dungeon target without attacking it.
                    new Decorator(ret => Current() && Unit.IsDungeonCombatBotTargetingRestricted
                        && actor.CurrentTarget != null && !actor.CurrentTarget.IsEligibleDungeonCombatTarget(),
                        new ActionAlwaysSucceed())));
        }
    }
}
