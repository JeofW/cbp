using System;

using Singular.Managers;
using Styx;
using Styx.Combat.CombatRoutine;
using Styx.Helpers;
using Styx.Logic.Combat;
using TreeSharp;
using Action = TreeSharp.Action;
using Styx.WoWInternals;
using CommonBehaviors.Actions;

namespace Singular.Helpers
{
    internal static class Common
    {
        /// <summary>
        ///  Creates a behavior to start auto attacking to current target.
        /// </summary>
        /// <remarks>
        ///  Created 23/05/2011
        /// </remarks>
        /// <param name="includePet"> This will also toggle pet auto attack. </param>
        /// <returns></returns>
        public static Composite CreateAutoAttack(bool includePet)
        {
            const int spellIdAutoShot = 75;

            return new PrioritySelector(
                new Decorator(
                    ret => !StyxWoW.Me.IsAutoAttacking && StyxWoW.Me.AutoRepeatingSpellId != spellIdAutoShot,
                    new Action(ret =>
                        {
                            if (!GroupCombatSafety.MayAttackCurrentTarget()) return RunStatus.Failure;
                            StyxWoW.Me.ToggleAttack();
                            return RunStatus.Failure;
                        })),
                new Decorator(
                    ret => includePet && StyxWoW.Me.GotAlivePet && (StyxWoW.Me.Pet.CurrentTarget == null || StyxWoW.Me.Pet.CurrentTarget != StyxWoW.Me.CurrentTarget),
                    new Action(
                        delegate
                        {
                            if (!GroupCombatSafety.MayAttackCurrentTarget()) return RunStatus.Failure;
                            PetManager.CastPetAction("Attack");
                            return RunStatus.Failure;
                        }))
                );
        }

        /// <summary>
        ///  Creates a behavior to start shooting current target with the wand.
        /// </summary>
        /// <remarks>
        ///  Created 23/05/2011.
        /// </remarks>
        /// <returns></returns>
        public static Composite CreateUseWand()
        {
            return CreateUseWand(ret => true);
        }

        /// <summary>
        ///  Creates a behavior to start shooting current target with the wand if extra conditions are met.
        /// </summary>
        /// <param name="extra"> Extra conditions to check to start shooting. </param>
        /// <returns></returns>
        public static Composite CreateUseWand(SimpleBooleanDelegate extra)
        {
            return new PrioritySelector(
                new Decorator(
                    ret => Item.HasWand && !StyxWoW.Me.IsWanding() && extra(ret),
                    new Action(ret =>
                    {
                        if (!GroupCombatSafety.MayAttackCurrentTarget()) return RunStatus.Failure;
                        return SpellManager.Cast("Shoot") ? RunStatus.Success : RunStatus.Failure;
                    }))
                );
        }

        /// <summary>Creates an interrupt decision with class abilities before racials.</summary>
        /// <param name="onUnit">Selector evaluated with the original caller context.</param>
        public static Composite CreateInterruptSpellCast(UnitSelectionDelegate onUnit)
        {
            UnitSelectionDelegate current = context => context is UnitSelectionDelegate observation
                ? observation(context) : null;
            return new PrioritySelector(
                context => CaptureInterruptTarget(onUnit, context),
                new Decorator(
                    ret => current(ret) != null,
                    new PrioritySelector(
                        // Spellbook availability alone is not current role authority.
                        Spell.Cast("Avenger's Shield", ret => TalentManager.CurrentSpec == TalentSpec.ProtectionPaladin
                            ? current(ret) : null),
                        Spell.Cast("Hammer of Justice", current),
                        Spell.Cast("Repentance", current,
                            ret => current(ret) is { } target && (target.IsPlayer || target.IsDemon || target.IsHumanoid ||
                                target.IsDragon || target.IsGiant || target.IsUndead)),

                        Spell.Cast("Kick", current),
                        Spell.Cast("Gouge", current, ret => current(ret) is { } target && !target.IsBoss() && !target.MeIsSafelyBehind),
                        Spell.Cast("Counterspell", current),
                        Spell.Cast("Wind Shear", current),
                        Spell.Cast("Pummel", current),
                        // Gag Order is a silence; retain its non-boss and talent gates.
                        Spell.Cast("Heroic Throw", current, ret => TalentManager.GetCount(3, 10) == 2
                            && current(ret) is { } target && !target.IsBoss()),
                        Spell.Cast("Silence", current),
                        Spell.Cast("Silencing Shot", current),
                        Spell.Cast("Bash", current, ret => current(ret) is { } target && !target.IsBoss()),
                        Spell.Cast("Strangulate", current),
                        Spell.Cast("Mind Freeze", current),
                        // Racials last, preserving the existing order.
                        Spell.Cast("Arcane Torrent", current),
                        Spell.Cast("War Stomp", current, ret => current(ret) is { } target && !target.IsBoss() && target.Distance < 8)
                    )));
        }

        private static UnitSelectionDelegate CaptureInterruptTarget(UnitSelectionDelegate select, object callerContext)
        {
            var player = StyxWoW.Me;
            var spec = TalentManager.CurrentSpec;
            ulong playerGuid = player?.Guid ?? 0;
            var target = select?.Invoke(callerContext);
            ulong targetGuid = target?.Guid ?? 0;
            // The closure owns this decision, not the caller's future target. Spell.Cast
            // resolves it again after setup; no alternate target may inherit permission.
            return _ =>
            {
                if (select == null || player == null || target == null
                    || !ReferenceEquals(StyxWoW.Me, player) || player.Guid != playerGuid
                    || TalentManager.CurrentSpec != spec)
                    return null;
                var observed = select(callerContext);
                if (!ReferenceEquals(observed, target) || target.Guid != targetGuid
                    || !target.IsValid || !target.IsAlive || !target.IsCasting || !target.CanInterruptCurrentSpellCast)
                    return null;
                // Selectors and virtual observations can change ownership themselves.
                return ReferenceEquals(StyxWoW.Me, player) && player.Guid == playerGuid
                    && TalentManager.CurrentSpec == spec && target.Guid == targetGuid
                    ? target : null;
            };
        }

        /// <summary>
        /// Creates a dismount composite. This down't use thread.Sleep() like the buildin one thus it works nicely with behaviors and framelocks. This will decend until bot lands if flying. 
        /// </summary>
        /// <param name="reason">The reason to dismount</param>
        /// <returns></returns>
        public static Composite CreateDismount(string reason)
        {
            return new Sequence(
                    new Action(ret => Logging.WriteDebug("Stop and dismount..." + (!string.IsNullOrEmpty(reason) ? (" Reason: " + reason) : string.Empty))),
                // stop moving 
                    new DecoratorContinue(ret => StyxWoW.Me.IsMoving,
                        new Sequence(
                            new Action(ret => WoWMovement.MoveStop()),
                            CreateWaitForLagDuration())
                    ),   // Land if we're flying
                    new DecoratorContinue(ret => StyxWoW.Me.IsFlying,
                        new Sequence(
                            new Action(ret => WoWMovement.Move(WoWMovement.MovementDirection.Descend)),
                            new WaitContinue(30, ret => !StyxWoW.Me.IsFlying, new ActionAlwaysSucceed()),
                            new Action(ret => WoWMovement.MoveStop(WoWMovement.MovementDirection.Descend))
                        )), // and finally dismount - but only if actually mounted!
                   new Action(r =>
                   {
                       // HB 3.3.5a: Check if actually mounted before calling Dismount()
                       if (!StyxWoW.Me.Mounted)
                           return;
                           
                       ShapeshiftForm shapeshift = StyxWoW.Me.Shapeshift;
                       if ((shapeshift != ShapeshiftForm.FlightForm) && (shapeshift != ShapeshiftForm.EpicFlightForm))
                           Lua.DoString("Dismount()");
                       else
                           Lua.DoString("RunMacroText('/cancelform')");
                   }));
        }
        /// <summary>
        /// This is meant to replace the 'SleepForLagDuration()' method. Should only be used in a Sequence
        /// </summary>
        /// <returns></returns>
        public static Composite CreateWaitForLagDuration()
        {
            return new WaitContinue(TimeSpan.FromMilliseconds((StyxWoW.WoWClient.Latency * 2) + 150), ret => false, new ActionAlwaysSucceed());
        }

        private static readonly WaitTimer InterruptTimer = new WaitTimer(TimeSpan.FromMilliseconds(500));

        private static bool PreventDoubleInterrupt
        {
            get
            {
                var tmp = InterruptTimer.IsFinished;
                if (tmp)
                    InterruptTimer.Reset();
                return tmp;
            }
        }
    }
}
