using System;
using System.Collections.Generic;

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
                            // Startup may follow a stale inactive observation.
                            // Build12340 StartAttack checks live state; AttackTarget toggles it.
                            Lua.DoString("StartAttack()");
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

        // A yielded descent belongs to one observed player. Cleanup may release
        // its own descent command, but must never command a replacement actor.
        private sealed class DismountRequest
        {
            private readonly Func<bool> sameActor;
            private bool descending;

            internal DismountRequest()
            {
                var player = StyxWoW.Me;
                ulong guid = player?.Guid ?? 0;
                sameActor = () => player != null && guid != 0
                    && ReferenceEquals(StyxWoW.Me, player) && player.Guid == guid;
            }

            internal bool CanContinue => sameActor() && StyxWoW.Me.IsValid && StyxWoW.Me.IsAlive
                && !StyxWoW.Me.IsOnTransport && !StyxWoW.Me.IsFalling && sameActor();

            internal bool CanRemoveFlight => CanContinue
                && StyxWoW.Me.TryGetMovementState(out uint flags, out ulong transportGuid)
                && transportGuid == 0 && (flags & 0x02003000U) == 0 && CanContinue;

            internal RunStatus StartDescending()
            {
                if (!CanContinue) return RunStatus.Failure;
                descending = true;
                WoWMovement.Move(WoWMovement.MovementDirection.Descend);
                return RunStatus.Success;
            }

            internal void StopDescending()
            {
                if (!descending) return;
                descending = false;
                if (sameActor()) WoWMovement.MoveStop(WoWMovement.MovementDirection.Descend);
            }
        }

        private sealed class DismountSequence : Sequence
        {
            internal DismountSequence(params Composite[] children) : base(children) { }

            protected override IEnumerable<RunStatus> Execute(object context)
            {
                var request = new DismountRequest();
                try
                {
                    foreach (var status in base.Execute(request)) yield return status;
                }
                finally { request.StopDescending(); }
            }
        }

        /// <summary>
        /// Descend before dismounting, without blocking the behavior thread.
        /// Expiry releases descent; it is not a landing or successful cast setup.
        /// </summary>
        /// <param name="reason">The reason to dismount</param>
        /// <returns></returns>
        public static Composite CreateDismount(string reason)
        {
            return new DismountSequence(
                    new Action(ret => ((DismountRequest)ret).CanContinue ? RunStatus.Success : RunStatus.Failure),
                    new Action(ret => Logging.WriteDebug("Stop and dismount..." + (!string.IsNullOrEmpty(reason) ? (" Reason: " + reason) : string.Empty))),
                // stop moving 
                    new DecoratorContinue(ret => ((DismountRequest)ret).CanContinue && StyxWoW.Me.IsMoving,
                        new Sequence(
                            new Action(ret =>
                            {
                                if (!((DismountRequest)ret).CanContinue) return RunStatus.Failure;
                                WoWMovement.MoveStop();
                                return RunStatus.Success;
                            }),
                            CreateWaitForLagDuration())
                    ),   // Land if we're flying
                    new DecoratorContinue(ret => ((DismountRequest)ret).CanContinue && StyxWoW.Me.IsFlying,
                        new Sequence(
                            new Action(ret => ((DismountRequest)ret).StartDescending()),
                            new WaitContinue(30, ret => !((DismountRequest)ret).CanContinue || !StyxWoW.Me.IsFlying, new ActionAlwaysSucceed()),
                            new Action(ret => ((DismountRequest)ret).StopDescending())
                        )), // and finally dismount - but only if actually mounted!
                   new Action(r =>
                   {
                       // A timeout, failed descent or changed actor cannot authorize
                       // removal or let the caller proceed as though landing succeeded.
                       if (!((DismountRequest)r).CanRemoveFlight)
                           return RunStatus.Failure;
                       ShapeshiftForm shapeshift = StyxWoW.Me.Shapeshift;
                       if (!StyxWoW.Me.Mounted && shapeshift != ShapeshiftForm.FlightForm && shapeshift != ShapeshiftForm.EpicFlightForm)
                           return RunStatus.Success;

                       if (!((DismountRequest)r).CanRemoveFlight)
                           return RunStatus.Failure;
                       if ((shapeshift != ShapeshiftForm.FlightForm) && (shapeshift != ShapeshiftForm.EpicFlightForm))
                           Lua.DoString("Dismount()");
                       else
                           Lua.DoString("RunMacroText('/cancelform')");
                       return RunStatus.Success;
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
