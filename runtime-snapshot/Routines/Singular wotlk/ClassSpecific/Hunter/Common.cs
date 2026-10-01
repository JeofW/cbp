using System;
using System.Linq;
using CommonBehaviors.Actions;
using Singular.Dynamics;
using Singular.Helpers;
using Singular.Managers;
using Singular.Settings;
using Styx;
using Styx.Combat.CombatRoutine;
using Styx.Helpers;
using Styx.Logic.Combat;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
using Action = TreeSharp.Action;

namespace Singular.ClassSpecific.Hunter
{
    public class Common
    {
        static Common()
        {
            // Lets hook this event so we can disable growl
            SingularRoutine.OnWoWContextChanged += SingularRoutine_OnWoWContextChanged;
        }

        // Disable pet growl in instances but enable it outside.
        static void SingularRoutine_OnWoWContextChanged(object sender, SingularRoutine.WoWContextEventArg e)
        {
            Lua.DoString(e.CurrentContext == WoWContext.Instances
                             ? "DisableSpellAutocast(GetSpellInfo(2649))"
                             : "EnableSpellAutocast(GetSpellInfo(2649))");
        }

        [Class(WoWClass.Hunter)]
        [Spec(TalentSpec.BeastMasteryHunter)]
        [Spec(TalentSpec.SurvivalHunter)]
        [Spec(TalentSpec.MarksmanshipHunter)]
        [Spec(TalentSpec.Lowbie)]
        [Behavior(BehaviorType.PreCombatBuffs)]
        [Context(WoWContext.All)]
        public static Composite CreateHunterBuffs()
        {
            return new PrioritySelector(
                Spell.WaitForCast(true),
                // WotLK QC: Dragonhawk (L74) replaces Hawk  try it first, fall back to Hawk for <74
                Spell.BuffSelf("Aspect of the Dragonhawk", ret => !StyxWoW.Me.HasAura("Aspect of the Viper")),
                Spell.BuffSelf("Aspect of the Hawk", ret => !SpellManager.HasSpell("Aspect of the Dragonhawk") && !StyxWoW.Me.HasAura("Aspect of the Viper")),
                Spell.BuffSelf("Track Hidden"),
                // WotLK MM fix: Trueshot Aura is a MM talent (tier 4) that gives +10% ranged AP to the raid.
                // HB 4.3.4/5.4.8/6.x never had it because Cata+ made it automatic.
                // Spell ID 19506 (Rank 1, the only rank in WotLK), 2 min CD in WotLK 3.3.5a.
                // Spell.BuffSelf handles !HasAura + CanCast (GCD + cooldown) + DoubleCastPreventionDict
                // (updated unconditionally for instant spells by the Spell.cs fix).
                new Decorator(
                    ret => TalentManager.CurrentSpec == TalentSpec.MarksmanshipHunter &&
                           SpellManager.HasSpell("Trueshot Aura"),
                    Spell.BuffSelf("Trueshot Aura")),
                new Decorator(ctx => SingularSettings.Instance.DisablePetUsage && StyxWoW.Me.GotAlivePet,
                    new Action(ctx => SpellManager.Cast("Dismiss Pet"))),

                new Decorator(ctx => !SingularSettings.Instance.DisablePetUsage,
                    new PrioritySelector(
                        CreateHunterCallPetBehavior(true),
                        Spell.Cast("Mend Pet", ret => StyxWoW.Me.GotAlivePet && (StyxWoW.Me.Pet.HealthPercent < 70 || (StyxWoW.Me.Pet.HappinessPercent < 90 && TalentManager.HasGlyph("Mend Pet"))) && !StyxWoW.Me.Pet.HasAura("Mend Pet"))
                        )
                    )
                );
        }

        // WotLK MM fix: re-cast Trueshot Aura if it was dispelled or lost after a death/rez in combat.
        // PreCombatBuffs only runs out of combat, so this CombatBuffs entry covers the mid-fight case.
        // Spell ID 19506, 2 min CD in WotLK 3.3.5a, lasts 30 min when active.
        // Spell.BuffSelf handles !HasAura + CanCast (GCD + cooldown) + DoubleCastPreventionDict
        // (updated unconditionally for instant spells by the Spell.cs fix), so the bot doesn't
        // spam CastSpellById every pulse while the 2 min CD is active.
        [Class(WoWClass.Hunter)]
        [Spec(TalentSpec.BeastMasteryHunter)]
        [Spec(TalentSpec.SurvivalHunter)]
        [Spec(TalentSpec.MarksmanshipHunter)]
        [Behavior(BehaviorType.CombatBuffs)]
        [Context(WoWContext.All)]
        public static Composite CreateHunterCombatBuffs()
        {
            return Spell.BuffSelf("Trueshot Aura",
                ret => TalentManager.CurrentSpec == TalentSpec.MarksmanshipHunter &&
                       SpellManager.HasSpell("Trueshot Aura"));
        }

        public static Composite CreateHunterBackPedal()
        {
            return
                new Decorator(
                    ret => !SingularSettings.Instance.DisableAllMovement && StyxWoW.Me.CurrentTarget.Distance <= Spell.MeleeRange + 5f &&
                           StyxWoW.Me.CurrentTarget.IsAlive &&
                           (StyxWoW.Me.CurrentTarget.CurrentTarget == null ||
                            StyxWoW.Me.CurrentTarget.CurrentTarget != StyxWoW.Me ||
                            StyxWoW.Me.CurrentTarget.IsStunned()),
                    new Action(
                        ret =>
                        {
                            var moveTo = WoWMathHelper.CalculatePointFrom(StyxWoW.Me.Location, StyxWoW.Me.CurrentTarget.Location, Spell.MeleeRange + 10f);

                            if (Navigator.CanNavigateFully(StyxWoW.Me.Location, moveTo))
                            {
                                Navigator.MoveTo(moveTo);
                                return RunStatus.Success;
                            }

                            return RunStatus.Failure;
                        }));
        }

        public static Composite CreateHunterTrapBehavior(string trapName)
        {
            return CreateHunterTrapBehavior(trapName, ret => StyxWoW.Me.CurrentTarget);
        }

        public static Composite CreateHunterTrapBehavior(string trapName, bool useLauncher)
        {
            return CreateHunterTrapBehavior(trapName, useLauncher, ret => StyxWoW.Me.CurrentTarget);
        }

        public static Composite CreateHunterTrapBehavior(string trapName, UnitSelectionDelegate onUnit)
        {
            return CreateHunterTrapBehavior(trapName, true, onUnit);
        }

        public static Composite CreateHunterTrapBehavior(string trapName, bool useLauncher, UnitSelectionDelegate onUnit)
        {
            // Original-client traps are placed at the hunter, not launched at
            // the selected enemy. Keep the compatibility parameter and the
            // existing pre-placement range policy; neither promises trap credit.
            WoWUnit actor = null, subject = null;
            ulong actorGuid = 0, subjectGuid = 0;
            bool Current(object context) => actor != null && actorGuid != 0
                && ReferenceEquals(StyxWoW.Me, actor) && actor.IsValid && actor.IsAlive && actor.Guid == actorGuid
                && IsHunterTrapCandidate(subject) && subject.Guid == subjectGuid
                && onUnit != null && ReferenceEquals(onUnit(context), subject)
                && IsHunterTrapCandidate(subject) && subject.Guid == subjectGuid
                && Unit.IsCombatActionSafe(trapName, subject)
                && ReferenceEquals(StyxWoW.Me, actor) && actor.Guid == actorGuid && actor.IsAlive;
            return new Sequence(
                new Action(context =>
                {
                    actor = StyxWoW.Me; actorGuid = actor?.Guid ?? 0;
                    subject = null; subjectGuid = 0;
                    if (!IsHunterTrapName(trapName) || onUnit == null || actor == null || actorGuid == 0
                        || !actor.IsValid || !actor.IsAlive) return RunStatus.Failure;
                    subject = onUnit(context); subjectGuid = subject?.Guid ?? 0;
                    return Current(context) ? RunStatus.Success : RunStatus.Failure;
                }),
                // The shared path checks readiness/GCD/resources and owns any
                // yielded dismount/setup. SpellManager selects the learned rank
                // and returns whether local executor dispatch completed.
                Spell.Cast(trapName, context => actor, Current));
        }

        public static Composite CreateHunterTrapOnAddBehavior(string trapName)
        {
            return CreateHunterTrapBehavior(trapName, false, context => Unit.NearbyUnfriendlyUnits
                .Where(unit => IsHunterTrapCandidate(unit) && unit.Combat && unit != StyxWoW.Me.CurrentTarget
                    && (!unit.IsMoving || unit.IsPlayer))
                .OrderBy(unit => unit.DistanceSqr).FirstOrDefault());
        }

        private static bool IsHunterTrapName(string name) => name == "Immolation Trap" || name == "Freezing Trap"
            || name == "Explosive Trap" || name == "Frost Trap" || name == "Snake Trap";

        private static bool IsHunterTrapCandidate(WoWUnit unit) => unit != null && unit.IsValid && unit.IsAlive
            && unit.Guid != 0 && !unit.IsMe && !unit.IsFriendly && float.IsFinite(unit.DistanceSqr)
            && unit.DistanceSqr < 40 * 40;

        public static Composite CreateHunterCallPetBehavior(bool reviveInCombat)
        {
            return new Decorator(
                ret =>  !SingularSettings.Instance.DisablePetUsage && !StyxWoW.Me.GotAlivePet && PetManager.PetTimer.IsFinished
                        && SpellManager.HasSpell("Call Pet"),
                new PrioritySelector(
                    Spell.WaitForCast(),
                    new Decorator(
                        ret => StyxWoW.Me.Pet != null && (!StyxWoW.Me.Combat || reviveInCombat),
                        new PrioritySelector(
                            Movement.CreateEnsureMovementStoppedBehavior(),
                            Spell.BuffSelf("Revive Pet"))),
                    new Sequence(
                        new Action(ret =>
                        {
                            if (!PetManager.CallPet(SingularSettings.Instance.Hunter.PetSlot))
                                return RunStatus.Failure;
                            return RunStatus.Success;
                        }),
                        Helpers.Common.CreateWaitForLagDuration(),
                        new WaitContinue(2, ret => StyxWoW.Me.GotAlivePet || StyxWoW.Me.Combat, new ActionAlwaysSucceed()),
                        new Action(ret => { if (!StyxWoW.Me.GotAlivePet) PetManager.PetTimer.Reset(); return RunStatus.Success; }),
                        new Decorator(
                            ret => !StyxWoW.Me.GotAlivePet && (!StyxWoW.Me.Combat || reviveInCombat),
                            Spell.BuffSelf("Revive Pet")))
                    )
                );
        }
    }
}
