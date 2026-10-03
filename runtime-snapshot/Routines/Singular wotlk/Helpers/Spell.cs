using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CommonBehaviors.Actions;

using Styx;
using Styx.Logic;
using Styx.Logic.Combat;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

using TreeSharp;

using Action = TreeSharp.Action;

namespace Singular.Helpers
{
    public delegate WoWUnit UnitSelectionDelegate(object context);

    public delegate bool SimpleBooleanDelegate(object context);

    internal static class Spell
    {
        public static TimeSpan GetSpellCooldown(string spell)
        {
            if (SpellManager.HasSpell(spell))
                return SpellManager.Spells[spell].CooldownTimeLeft();
            throw new Styx.Helpers.ObservationUnavailableException("spell-cooldown",
                "No current known spell for the named Singular cooldown observation.");
        }

        // Temp wrapper for upcoming HB API
        // WotLK 3.3.5a: GetSpellCooldown() requires the localized spell name.
        // On non-English clients (Russian, German...) passing the English name returns 0.
        // Fix: GetSpellInfo(id) returns the localized name — works on ALL clients.
        public static TimeSpan CooldownTimeLeft(this WoWSpell spell)
        {
            return SpellManager.GetSpellCooldownTimeLeft(spell.Id);
        }

        #region Properties

        internal static string LastSpellCast { get; set; }

        #endregion

        private static WoWSpell GetSpellByName(string spellName)
        {
            WoWSpell spell;
            if (!SpellManager.Spells.TryGetValue(spellName, out spell))
                spell = SpellManager.RawSpells.Values.FirstOrDefault(s => s.Name == spellName);

            return spell;
        }

        #region Wait

        /// <summary>
        ///   Creates a composite that will return a success, so long as you are currently casting. (Use this to prevent the CC from
        ///   going down to lower branches in the tree, while casting.)
        /// </summary>
        /// <returns></returns>
        public static Composite WaitForCast()
        {
            return WaitForCast(false, true);
        }

        // Ported from Singular 5.4.8 (Helpers/Spell.cs:512). The 5.4.8 source reads
        // Spell.FixGlobalCooldown (a WoWSpell property from HB 6.2.3 Navigator that
        // 3.3.5a CopilotBuddy does not expose). SpellManager.GlobalCooldown is the
        // same value read via the WoW client cooldown list — equivalent semantics for
        // "is the GCD currently active".
        public static bool IsGlobalCooldown()
        {
            return SpellManager.GlobalCooldown;
        }

        // Ported from Singular 5.4.8 (Helpers/Spell.cs:622). Wraps WaitForCast +
        // WaitForChannel so the priority selector doesn't tick the damage-spell
        // sub-tree while a cast or channel is still in flight — the primary fix
        // for the Heroic Throw spam that the 4.3.4 port inherited.
        public static Composite WaitForCastOrChannel()
        {
            return new PrioritySelector(
                WaitForCast(),
                new Decorator(ret =>
                    {
                        var owner = StyxWoW.Me;
                        ulong ownerGuid = owner?.Guid ?? 0;
                        return ownerGuid != 0 && owner != null && owner.IsValid && owner.IsAlive
                            && owner.ChanneledCastingSpellId > 0 && owner.Guid == ownerGuid
                            && ReferenceEquals(owner, StyxWoW.Me) && owner.IsValid && owner.IsAlive;
                    },
                    new ActionAlwaysSucceed()));
        }

        /// <summary>
        ///   Creates a composite that will return a success, so long as you are currently casting. (Use this to prevent the CC from
        ///   going down to lower branches in the tree, while casting.)
        /// </summary>
        /// <remarks>
        ///   Created 13/5/2011.
        /// </remarks>
        /// <param name = "faceDuring">Whether or not to face during casting</param>-
        /// <returns></returns>
        public static Composite WaitForCast(bool faceDuring)
        {
            return WaitForCast(faceDuring, true);
        }

        /// <summary>
        ///   Creates a composite that will return a success, so long as you are currently casting. (Use this to prevent the CC from
        ///   going down to lower branches in the tree, while casting.)
        /// </summary>
        /// <remarks>
        ///   Created 13/5/2011.
        /// </remarks>
        /// <param name = "faceDuring">Whether or not to face during casting</param>
        /// <param name = "allowLagTollerance">Whether or not to allow lag tollerance for spell queueing</param>
        /// <returns></returns>
        public static Composite WaitForCast(bool faceDuring, bool allowLagTollerance)
        {
            WoWUnit owner = null;
            ulong ownerGuid = 0;
            bool IsCurrent() => ownerGuid != 0 && owner != null && owner.IsValid && owner.IsAlive
                && owner.Guid == ownerGuid && ReferenceEquals(owner, StyxWoW.Me);
            return new Sequence(
                new Action(ret =>
                            {
                                var castingPlayer = StyxWoW.Me;
                                owner = castingPlayer;
                                ownerGuid = owner?.Guid ?? 0;
                                if (!IsCurrent() || !owner.IsCasting || !IsCurrent())
                                    return RunStatus.Failure;

                                if (castingPlayer.IsWanding() || !IsCurrent())
                                    return RunStatus.Failure;

                                if (owner.ChannelObjectGuid > 0 || !IsCurrent())
                                    return RunStatus.Failure;

                                var latency = StyxWoW.WoWClient.Latency * 2;
                                if (!IsCurrent()) return RunStatus.Failure;
                                var castTimeLeft = owner.CurrentCastTimeLeft;
                                if (!IsCurrent() || !owner.IsCasting || !IsCurrent())
                                    return RunStatus.Failure;
                                if (allowLagTollerance && castTimeLeft != TimeSpan.Zero && castTimeLeft.TotalMilliseconds < latency)
                                    return RunStatus.Failure;

                                return RunStatus.Success;
                            }),
                // Own and execute the facing child instead of discarding a
                // newly constructed Composite. No turn still means keep waiting.
                new PrioritySelector(
                    new Decorator(
                        ret => faceDuring && IsCurrent() && owner.IsCasting && IsCurrent()
                            && owner.ChanneledCastingSpellId == 0 && IsCurrent(),
                        Movement.CreateFaceTargetBehavior()),
                    new ActionAlwaysSucceed()),
                new Action(ret => IsCurrent() && owner.IsCasting && IsCurrent()
                    ? RunStatus.Success : RunStatus.Failure));
        }

        #endregion

        #region PreventDoubleCast

        /// <summary>
        /// Creates a composite to avoid double casting spells on current target. Mostly usable for spells like Immolate, Devouring Plague etc.
        /// </summary>
        /// <remarks>
        /// Created 19/12/2011 raphus
        /// </remarks>
        /// <param name="spellNames"> Spell names to check </param>
        /// <returns></returns>
        public static Composite PreventDoubleCast(params string[] spellNames)
        {
            return PreventDoubleCast(ret => StyxWoW.Me?.CurrentTarget, spellNames);
        }

        /// <summary>
        /// Creates a composite to avoid double casting spells on specified unit. Mostly usable for spells like Immolate, Devouring Plague etc.
        /// </summary>
        /// <remarks>
        /// Created 19/12/2011 raphus
        /// </remarks>
        /// <param name="unit"> Unit to check </param>
        /// <param name="spellNames"> Spell names to check </param>
        /// <returns></returns>
        public static Composite PreventDoubleCast(UnitSelectionDelegate unit, params string[] spellNames)
        {
            WoWUnit owner = null, subject = null;
            ulong ownerGuid = 0, subjectGuid = 0;
            int spellId = 0;
            bool OwnsActor() => ownerGuid != 0 && owner != null && owner.IsValid && owner.IsAlive
                && owner.Guid == ownerGuid && ReferenceEquals(owner, StyxWoW.Me);
            bool OwnsSubject() => OwnsActor() && subjectGuid != 0 && subject != null
                && subject.IsValid && subject.IsAlive && subject.Guid == subjectGuid;
            bool SameObservedCast()
            {
                if (!OwnsActor() || !owner.IsCasting || !OwnsActor()) return false;
                var observed = owner.CastingSpell;
                return observed != null && observed.Id == spellId && spellNames.Contains(observed.Name)
                    && owner.CastingSpellId == spellId && OwnsActor();
            }
            bool HasOwnedDuplicate(object context, bool reselect)
            {
                if (!OwnsSubject() || !SameObservedCast()) return false;
                if (reselect && (!ReferenceEquals(unit(context), subject) || !OwnsSubject())) return false;
                // The complete collection preserves an owned aura when another
                // caster has an effect with the same localized name.
                var auras = subject.GetAllAuras();
                return auras != null && auras.Any(a => a != null && a.SpellId == spellId
                    && a.CreatorGuid == ownerGuid && a.IsActive) && OwnsSubject() && SameObservedCast();
            }
            return
                new PrioritySelector(
                    new Decorator(
                        ret =>
                        {
                            owner = StyxWoW.Me;
                            ownerGuid = owner?.Guid ?? 0;
                            subject = null; subjectGuid = 0; spellId = 0;
                            if (unit == null || spellNames == null || spellNames.Length == 0 || !OwnsActor())
                                return false;
                            var observed = owner.CastingSpell;
                            if (!OwnsActor() || observed == null || observed.Id <= 0 || !spellNames.Contains(observed.Name))
                                return false;
                            spellId = observed.Id;
                            subject = unit(ret);
                            subjectGuid = subject?.Guid ?? 0;
                            return HasOwnedDuplicate(ret, false);
                        },
                        new Action(ret =>
                        {
                            if (!HasOwnedDuplicate(ret, true)) return RunStatus.Failure;
                            // These are stable managed observations, not proof of
                            // a native cast instance or its original recipient.
                            SpellManager.StopCasting();
                            return OwnsSubject() ? RunStatus.Success : RunStatus.Failure;
                        })));
        }

        #endregion

        #region Cast - by name

        private static float MeleeRangeFor(WoWUnit actor, WoWUnit target)
        {
            if (actor == null || target == null) return 0f;
            if (target.IsPlayer) return 3.5f;
            float reach = actor.CombatReach + 1.3333334f + target.CombatReach;
            return float.IsFinite(reach) ? Math.Max(5f, reach) : 0f;
        }

        // Use the same admission before setup and immediately before dispatch.
        // Preserve the existing self/melee/ranged policy instead of introducing
        // a second, subtly different LOS or range calculation at the boundary.
        private static bool CanCastNamedSpell(string name, WoWUnit target,
            SimpleBooleanDelegate checkMovement, SimpleBooleanDelegate requirements, object ret, Func<bool> isCurrent)
        {
            if (string.IsNullOrWhiteSpace(name) || requirements == null || checkMovement == null)
                return false;
            if (target == null || !isCurrent())
                return false;
            if (!SpellManager.TryClaimCastCandidate(name))
                return false;
            var minReqs = requirements(ret) && isCurrent() && Unit.IsCombatActionSafe(name, target) && isCurrent();
            var canCast = false;
            var inRange = false;
            if (minReqs)
            {
                bool movementRequired = checkMovement(ret);
                if (!isCurrent()) return false;
                canCast = SpellManager.CanCast(name, target, false, movementRequired);
                if (!isCurrent()) return false;

                if (canCast)
                {
                    // We're always in range of ourselves. So just ignore this bit if we're casting it on us
                    if (target.IsMe)
                    {
                        inRange = true;
                    }
                    else
                    {
                        WoWSpell spell;
                        if (SpellManager.Spells.TryGetValue(name, out spell))
                        {
                            var rangeId = spell.SpellRangeId;
                            var minRange = spell.MinRange;
                            var maxRange = spell.MaxRange;
                            var targetDistance = target.Distance;
                            if (!isCurrent()) return false;
                            // RangeId 1 is "Self Only".
                            if (rangeId == 1)
                                inRange = true;
                            // RangeId 2 is melee range — no LOS needed.
                            else if (rangeId == 2)
                                inRange = targetDistance < MeleeRangeFor(StyxWoW.Me, target);
                            else
                            {
                                // LOS check only for true ranged spells.
                                if (!target.InLineOfSpellSight)
                                    inRange = false;
                                else
                                    inRange = targetDistance < maxRange &&
                                              targetDistance > (minRange == 0 ? minRange : minRange + 3);
                            }
                        }
                    }
                }
            }

            return minReqs && canCast && inRange && isCurrent();
        }

        private static bool CanSelectNamedSpell(string name, WoWUnit target,
            SimpleBooleanDelegate checkMovement, SimpleBooleanDelegate requirements, object ret, Func<bool> isCurrent)
        {
            if (string.IsNullOrWhiteSpace(name) || requirements == null || checkMovement == null)
                return false;
            if (target == null || !isCurrent())
                return false;
            var owner = StyxWoW.Me;
            if (owner == null || owner.IsCasting || !SpellManager.Spells.ContainsKey(name))
                return false;
            if (!requirements(ret) || !isCurrent() || !Unit.IsCombatActionSafe(name, target) || !isCurrent())
                return false;
            // Selection is deliberately cheap. Current range, movement, funnel,
            // usability and cooldown metadata are all re-observed by the strict
            // CanCastNamedSpell immediately before native submission.
            return SpellManager.TryClaimCastCandidate(name) && isCurrent();
        }

        /// <summary>
        ///   Creates a behavior to cast a spell by name. Returns RunStatus.Success if successful, RunStatus.Failure otherwise.
        /// </summary>
        /// <remarks>
        ///   Created 5/2/2011.
        /// </remarks>
        /// <param name = "name">The name.</param>
        /// <returns>.</returns>
        public static Composite Cast(string name)
        {
            return Cast(name, ret => true);
        }

        /// <summary>
        ///   Creates a behavior to cast a spell by name, with special requirements. Returns RunStatus.Success if successful,
        ///   RunStatus.Failure otherwise.
        /// </summary>
        /// <remarks>
        ///   Created 5/2/2011.
        /// </remarks>
        /// <param name = "name">The name.</param>
        /// <param name = "requirements">The requirements.</param>
        /// <returns>.</returns>
        public static Composite Cast(string name, SimpleBooleanDelegate requirements)
        {
            return Cast(name, ret => true, ret => StyxWoW.Me.CurrentTarget, requirements);
        }

        /// <summary>
        ///   Creates a behavior to cast a spell by name, on a specific unit. Returns
        ///   RunStatus.Success if successful, RunStatus.Failure otherwise.
        /// </summary>
        /// <remarks>
        ///   Created 5/2/2011.
        /// </remarks>
        /// <param name = "name">The name.</param>
        /// <param name = "onUnit">The on unit.</param>
        /// <returns>.</returns>
        public static Composite Cast(string name, UnitSelectionDelegate onUnit)
        {
            return Cast(name, ret => true, onUnit, ret => true);
        }
        /// <summary>
        ///   Creates a behavior to cast a spell by name, on a specific unit. Returns
        ///   RunStatus.Success if successful, RunStatus.Failure otherwise.
        /// </summary>
        /// <remarks>
        ///   Created 5/2/2011.
        /// </remarks>
        /// <param name = "name">The name.</param>
        /// <param name = "onUnit">The on unit.</param>
        /// <param name = "requirements">The requirements.</param>
        /// <returns>.</returns>
        public static Composite Cast(string name, UnitSelectionDelegate onUnit, SimpleBooleanDelegate requirements)
        {
            return Cast(name, ret => true, onUnit, requirements);
        }

        /// <summary>
        ///   Creates a behavior to cast a spell by name, with special requirements, on a specific unit. Returns
        ///   RunStatus.Success if successful, RunStatus.Failure otherwise.
        /// </summary>
        /// <remarks>
        ///   Created 5/2/2011.
        /// </remarks>
        /// <param name = "name">The name.</param>
        /// <param name="checkMovement"></param>
        /// <param name = "onUnit">The on unit.</param>
        /// <param name = "requirements">The requirements.</param>
        /// <returns>.</returns>
        public static Composite Cast(string name, SimpleBooleanDelegate checkMovement, UnitSelectionDelegate onUnit, SimpleBooleanDelegate requirements)
        {
            return CastWithRecovery(name, checkMovement, onUnit, requirements, false, false);
        }

        private static Composite CastWithRecovery(string name, SimpleBooleanDelegate checkMovement,
            UnitSelectionDelegate onUnit, SimpleBooleanDelegate requirements, bool healing, bool aura)
        {
            WoWUnit owner = null, selected = null;
            ulong ownerGuid = 0, selectedGuid = 0;
            bool OwnsActor() => ownerGuid != 0 && owner != null && owner.IsValid && owner.IsAlive
                && owner.Guid == ownerGuid && ReferenceEquals(owner, StyxWoW.Me);
            bool IsCurrent() => OwnsActor() && selectedGuid != 0 && selected != null
                && selected.IsValid && selected.Guid == selectedGuid;
            UnitSelectionDelegate retainedSelection = ret =>
            {
                if (!IsCurrent() || onUnit == null) return null;
                // Re-evaluate caller coverage, but never transfer an admitted
                // cast to a different recipient after setup or another callback.
                var current = onUnit(ret);
                return ReferenceEquals(current, selected) && IsCurrent() ? selected : null;
            };
            return new Decorator(
                ret =>
                {
                    owner = StyxWoW.Me;
                    ownerGuid = owner?.Guid ?? 0;
                    selected = null;
                    selectedGuid = 0;
                    if (string.IsNullOrWhiteSpace(name) || onUnit == null || requirements == null || checkMovement == null || !OwnsActor())
                        return false;
                    selected = onUnit(ret);
                    selectedGuid = selected?.Guid ?? 0;
                    return IsCurrent() && CanSelectNamedSpell(name, selected, checkMovement, requirements, ret, IsCurrent)
                        && retainedSelection(ret) != null && IsCurrent();
                },
                new Sequence( 
                    new DecoratorContinue(ret => IsCurrent() && owner.Mounted && !name.Contains("Aura") && !name.Contains("Presence") && !name.Contains("Stance"),
                        Common.CreateDismount("Casting spell")),
                    // Off-target hostile casts only (multi-dot, Seed of Corruption) — skips heals and CurrentTarget casts
                    new DecoratorContinue(
                        ret => IsCurrent() && Movement.NeedsOffTargetCastSetup(retainedSelection(ret)),
                        Movement.CreateEnsureTargetAndFaceBehavior(retainedSelection)),
                    new Action(
                        ret =>
                        {
                            // Setup may have yielded since the outer predicate. Resolve once
                            // here, and never turn a rejected submission into tree success.
                            if (string.IsNullOrWhiteSpace(name) || onUnit == null)
                                return RunStatus.Failure;
                            var target = retainedSelection(ret);
                            if (target == null || !Unit.IsCombatActionSafe(name, target))
                                return RunStatus.Failure;
                            Logger.Write("Casting " + name + " on " + target.SafeName());
                            // Dismount/target setup and logging may have changed sight,
                            // range, availability or caller requirements. Do not reselect
                            // a different recipient between this check and submission.
                            if (retainedSelection(ret) == null ||
                                !CanCastNamedSpell(name, target, checkMovement, requirements, ret, IsCurrent) ||
                                retainedSelection(ret) == null || !IsCurrent())
                            {
                                SpellManager.RecordCastCandidateResult(name, false);
                                return RunStatus.Failure;
                            }
                            bool submitted = RecoveryActions.TryCast(name, target, healing, aura, "Singular.Cast") && IsCurrent();
                            SpellManager.RecordCastCandidateResult(name, submitted);
                            return submitted ? RunStatus.Success : RunStatus.Failure;

                            //WoWSpell spell;
                            //if (SpellManager.Spells.TryGetValue(name, out spell))
                            //{
                            //    // This is here to prevent cancelling funneled and channeled spells right after the cast. /raphus
                            //    if (spell.IsFunnel || spell.IsChanneled)
                            //    {
                            //        Thread.Sleep(500);
                            //    }
                            //}
                        }),
                        // changed to WaitContinue to avoid using Thread.Sleep as it freezes Wow momentarily because behaviors are now wrapped in framelock. /highvoltz
                    new WaitContinue(TimeSpan.FromMilliseconds(500), ret => 
                    {
                        if (!IsCurrent()) return true;
                        WoWSpell spell;
                        if (SpellManager.Spells.TryGetValue(name, out spell))
                        {
                            // This is here to prevent cancelling funneled and channeled spells right after the cast. /raphus
                            if (spell.IsFunnel || spell.IsChanneled)
                                return false;
                        }
                        return true;
                    }, new ActionAlwaysSucceed()),
                    new Action(ret => IsCurrent() ? RunStatus.Success : RunStatus.Failure))
                );
        }

        #endregion

        #region Cast - by ID

        /// <summary>
        ///   Creates a behavior to cast a spell by ID. Returns RunStatus.Success if successful,
        ///   RunStatus.Failure otherwise.
        /// </summary>
        /// <remarks>
        ///   Created 5/2/2011.
        /// </remarks>
        /// <param name = "spellId">Identifier for the spell.</param>
        /// <returns>.</returns>
        public static Composite Cast(int spellId)
        {
            return Cast(spellId, ret => true);
        }

        /// <summary>
        ///   Creates a behavior to cast a spell by ID, with special requirements. Returns
        ///   RunStatus.Success if successful, RunStatus.Failure otherwise.
        /// </summary>
        /// <remarks>
        ///   Created 5/2/2011.
        /// </remarks>
        /// <param name = "spellId">Identifier for the spell.</param>
        /// <param name = "requirements">The requirements.</param>
        /// <returns>.</returns>
        public static Composite Cast(int spellId, SimpleBooleanDelegate requirements)
        {
            return Cast(spellId, ret => StyxWoW.Me.CurrentTarget, requirements);
        }

        /// <summary>
        ///   Creates a behavior to cast a spell by ID, on a specific unit. Returns
        ///   RunStatus.Success if successful, RunStatus.Failure otherwise.
        /// </summary>
        /// <remarks>
        ///   Created 5/2/2011.
        /// </remarks>
        /// <param name = "spellId">Identifier for the spell.</param>
        /// <param name = "onUnit">The on unit.</param>
        /// <returns>.</returns>
        public static Composite Cast(int spellId, UnitSelectionDelegate onUnit)
        {
            return Cast(spellId, onUnit, ret => true);
        }

        /// <summary>
        ///   Creates a behavior to cast a spell by ID, with special requirements, on a specific unit.
        ///   Returns RunStatus.Success if successful, RunStatus.Failure otherwise.
        /// </summary>
        /// <remarks>
        ///   Created 5/2/2011.
        /// </remarks>
        /// <param name = "spellId">Identifier for the spell.</param>
        /// <param name = "onUnit">The on unit.</param>
        /// <param name = "requirements">The requirements.</param>
        /// <returns>.</returns>
        public static Composite Cast(int spellId, UnitSelectionDelegate onUnit, SimpleBooleanDelegate requirements)
        {
            return CastWithRecovery(spellId, onUnit, requirements, false, false);
        }

        private static Composite CastWithRecovery(int spellId, UnitSelectionDelegate onUnit,
            SimpleBooleanDelegate requirements, bool healing, bool aura)
        {
            WoWUnit owner = null, selected = null;
            ulong ownerGuid = 0, selectedGuid = 0;
            bool OwnsActor() => ownerGuid != 0 && owner != null && owner.IsValid && owner.IsAlive
                && owner.Guid == ownerGuid && ReferenceEquals(owner, StyxWoW.Me);
            bool IsCurrent() => OwnsActor() && selectedGuid != 0 && selected != null
                && selected.IsValid && selected.Guid == selectedGuid;
            UnitSelectionDelegate retainedSelection = ret =>
            {
                if (!IsCurrent() || onUnit == null) return null;
                var current = onUnit(ret);
                return ReferenceEquals(current, selected) && IsCurrent() ? selected : null;
            };
            return new Decorator(
                ret =>
                {
                    owner = StyxWoW.Me;
                    ownerGuid = owner?.Guid ?? 0;
                    selected = null;
                    selectedGuid = 0;
                    if (spellId <= 0 || onUnit == null || requirements == null || !OwnsActor())
                        return false;
                    selected = onUnit(ret);
                    selectedGuid = selected?.Guid ?? 0;
                    return IsCurrent() && requirements(ret) && IsCurrent() && Unit.IsCombatActionSafe(spellId, selected)
                        && SpellManager.CanCast(spellId, selected, true) && retainedSelection(ret) != null && IsCurrent();
                },
                new Sequence(
                    new DecoratorContinue(
                        ret => IsCurrent() && Movement.NeedsOffTargetCastSetup(retainedSelection(ret)),
                        Movement.CreateEnsureTargetAndFaceBehavior(retainedSelection)),
                    new Action(
                        ret =>
                        {
                            // Setup may have yielded since the outer predicate. Resolve once
                            // here, and never turn a rejected submission into tree success.
                            if (spellId <= 0 || onUnit == null)
                                return RunStatus.Failure;
                            var target = retainedSelection(ret);
                            if (target == null || !Unit.IsCombatActionSafe(spellId, target))
                                return RunStatus.Failure;
                            Logger.Write("Casting " + spellId + " on " + target.SafeName());
                            // The ID overload retains the host's range/LOS policy.
                            if (retainedSelection(ret) == null || requirements == null || !requirements(ret) || !IsCurrent() ||
                                !Unit.IsCombatActionSafe(spellId, target) ||
                                !SpellManager.CanCast(spellId, target, true) || retainedSelection(ret) == null || !IsCurrent())
                                return RunStatus.Failure;
                            return RecoveryActions.TryCast(spellId, target, healing, aura, "Singular.CastId") && IsCurrent()
                                ? RunStatus.Success
                                : RunStatus.Failure;
                        }))
                );
        }

        #endregion

        #region Buff - by name

        /// <summary>
        ///   Creates a behavior to cast a buff by name on current target. Returns
        ///   RunStatus.Success if successful, RunStatus.Failure otherwise.
        /// </summary>
        /// <remarks>
        ///   Created 5/2/2011.
        /// </remarks>
        /// <param name = "name">The name of the buff</param>
        /// <returns></returns>
        public static Composite Buff(string name)
        {
            return Buff(name, ret => true);
        }

        public static Composite Buff(string name, params string[] buffNames)
        {
            return Buff(name, ret => true, buffNames);
        }

        public static Composite Buff(string name, bool myBuff)
        {
            return Buff(name, myBuff, ret => true);
        }

        /// <summary>
        ///   Creates a behavior to cast a buff by name, with special requirements, on current target. Returns
        ///   RunStatus.Success if successful, RunStatus.Failure otherwise.
        /// </summary>
        /// <remarks>
        ///   Created 5/2/2011.
        /// </remarks>
        /// <param name = "name">The name of the buff</param>
        /// <param name = "requirements">The requirements.</param>
        /// <returns></returns>
        public static Composite Buff(string name, SimpleBooleanDelegate requirements)
        {
            return Buff(name, false, ret => StyxWoW.Me.CurrentTarget, requirements, name);
        }

        /// <summary>
        ///   Creates a behavior to cast a buff by name on a specific unit. Returns
        ///   RunStatus.Success if successful, RunStatus.Failure otherwise.
        /// </summary>
        /// <remarks>
        ///   Created 5/2/2011.
        /// </remarks>
        /// <param name = "name">The name of the buff</param>
        /// <param name = "onUnit">The on unit</param>
        /// <returns></returns>
        public static Composite Buff(string name, UnitSelectionDelegate onUnit)
        {
            return Buff(name, false, onUnit, ret => true, name);
        }

        public static Composite Buff(string name, bool myBuff, params string[] buffNames)
        {
            return Buff(name, myBuff, ret => true, buffNames);
        }

        public static Composite Buff(string name, UnitSelectionDelegate onUnit, params string[] buffNames)
        {
            return Buff(name, onUnit, ret => true, buffNames);
        }

        public static Composite Buff(string name, SimpleBooleanDelegate requirements, params string[] buffNames)
        {
            return Buff(name, ret => StyxWoW.Me.CurrentTarget, requirements, buffNames);
        }

        public static Composite Buff(string name, bool myBuff, UnitSelectionDelegate onUnit)
        {
            return Buff(name, myBuff, onUnit, ret => true);
        }

        public static Composite Buff(string name, bool myBuff, SimpleBooleanDelegate requirements)
        {
            return Buff(name, myBuff, ret => StyxWoW.Me.CurrentTarget, requirements);
        }

        public static Composite Buff(string name, UnitSelectionDelegate onUnit, SimpleBooleanDelegate requirements)
        {
            return Buff(name, false, onUnit, requirements);
        }

        public static Composite Buff(string name, bool myBuff, SimpleBooleanDelegate requirements, params string[] buffNames)
        {
            return Buff(name, myBuff, ret => StyxWoW.Me.CurrentTarget, requirements, buffNames);
        }

        public static Composite Buff(string name, bool myBuff, UnitSelectionDelegate onUnit, params string[] buffNames)
        {
            return Buff(name, myBuff, onUnit, ret => true, buffNames);
        }

        public static Composite Buff(string name, bool myBuff, UnitSelectionDelegate onUnit, SimpleBooleanDelegate requirements)
        {
            return Buff(name, myBuff, onUnit, requirements, name);
        }

        public static Composite Buff(string name, UnitSelectionDelegate onUnit, SimpleBooleanDelegate requirements, params string[] buffNames)
        {
            return Buff(name, false, onUnit, requirements, buffNames);
        }

        //private static string _lastBuffCast = string.Empty;
        //private static System.Diagnostics.Stopwatch _castTimer = new System.Diagnostics.Stopwatch();
        /// <summary>
        ///   Creates a behavior to cast a buff by name, with special requirements, on a specific unit. Returns
        ///   RunStatus.Success if successful, RunStatus.Failure otherwise.
        /// </summary>
        /// <remarks>
        ///   Created 5/2/2011.
        /// </remarks>
        /// <param name = "name">The name of the buff</param>
        /// <param name = "myBuff">Check for self debuffs or not</param>
        /// <param name = "onUnit">The on unit</param>
        /// <param name = "requirements">The requirements.</param>
        /// <returns></returns>
        public static Composite Buff(string name, bool myBuff, UnitSelectionDelegate onUnit, SimpleBooleanDelegate requirements, params string[] buffNames)
        {
            //if (name == _lastBuffCast && _castTimer.IsRunning && _castTimer.ElapsedMilliseconds < 250)
            //{
            //    return new Action(ret => RunStatus.Success);
            //}

            //if (name == _lastBuffCast && StyxWoW.Me.IsCasting)
            //{
            //    _castTimer.Reset();
            //    _castTimer.Start();
            //    return new Action(ret => RunStatus.Success);
            //}

            // Cast resolves this selector again after any yielded setup. Keeping
            // coverage in the selector prevents a stale outer predicate from
            // submitting a duplicate while retaining caller-declared equivalents.
            UnitSelectionDelegate uncoveredUnit = ret =>
            {
                if (string.IsNullOrWhiteSpace(name) || onUnit == null || requirements == null || buffNames == null)
                    return null;
                var target = onUnit(ret);
                return target != null && !DoubleCastPreventionDict.ContainsKey(name) &&
                       buffNames.All(b => myBuff ? !target.HasMyAura(b) : !target.HasAura(b))
                    ? target : null;
            };
            return
                new Decorator(
                    ret => onUnit != null && requirements != null && buffNames != null
                           && !string.IsNullOrWhiteSpace(name) && uncoveredUnit(ret) != null,
                    new Sequence(
                // new Action(ctx => _lastBuffCast = name),
                        CastWithRecovery(name, ret => true, uncoveredUnit, requirements, false, true),
                        // WotLK QC fix: instant-cast buffs (Aspect of the Viper/Dragonhawk, etc.)
                        // skip the WaitContinue below and were never added to the dict, so the bot
                        // spammed CastSpellById every pulse (~5x/600ms in the wild). Mark the spell
                        // cast immediately so the next pulse skips the decorator.
                        // Ported from Singular 6.X.X Buff() which calls UpdateDoubleCast unconditionally.
                        new Action(ret => UpdateDoubleCastDict(name)),
                        new DecoratorContinue(
                            ret => SpellManager.Spells[name].CastTime > 0,
                            new Sequence(
                                new WaitContinue(
                                    1,
                                    ret => StyxWoW.Me.IsCasting,
                                    new Action(ret => UpdateDoubleCastDict(name))))
                                    ))
                        );
        }

        private static void UpdateDoubleCastDict(string spellName)
        {
            // WotLK QC fix: original code was `if (ContainsKey) Set else Add` but the Add ran
            // unconditionally and threw on duplicate keys when called twice for the same spell
            // (which now happens since instant-cast buffs call it before the CastTime>0 path).
            // Dictionary indexer assignment is a safe set-or-add.
            DoubleCastPreventionDict[spellName] = DateTime.UtcNow;
        }

        public static readonly Dictionary<string, DateTime> DoubleCastPreventionDict = new Dictionary<string, DateTime>();

        #endregion

        #region BuffSelf - by name

        /// <summary>
        ///   Creates a behavior to cast a buff by name on yourself. Returns
        ///   RunStatus.Success if successful, RunStatus.Failure otherwise.
        /// </summary>
        /// <remarks>
        ///   Created 5/6/2011.
        /// </remarks>
        /// <param name = "name">The buff name.</param>
        /// <returns>.</returns>
        public static Composite BuffSelf(string name)
        {
            return Buff(name, ret => StyxWoW.Me, ret => true);
        }

        /// <summary>
        ///   Creates a behavior to cast a buff by name on yourself with special requirements. Returns RunStatus.Success if
        ///   successful, RunStatus.Failure otherwise.
        /// </summary>
        /// <remarks>
        ///   Created 5/6/2011.
        /// </remarks>
        /// <param name = "name">The buff name.</param>
        /// <param name = "requirements">The requirements.</param>
        /// <returns>.</returns>
        public static Composite BuffSelf(string name, SimpleBooleanDelegate requirements)
        {
            return Buff(name, ret => StyxWoW.Me, requirements);
        }

        #endregion

        #region Buff - by ID

        /// <summary>
        ///   Creates a behavior to cast a buff by name on current target. Returns
        ///   RunStatus.Success if successful, RunStatus.Failure otherwise.
        /// </summary>
        /// <remarks>
        ///   Created 5/2/2011.
        /// </remarks>
        /// <param name = "spellId">The ID of the buff</param>
        /// <returns></returns>
        public static Composite Buff(int spellId)
        {
            return Buff(spellId, ret => true);
        }

        /// <summary>
        ///   Creates a behavior to cast a buff by name, with special requirements, on current target. Returns
        ///   RunStatus.Success if successful, RunStatus.Failure otherwise.
        /// </summary>
        /// <remarks>
        ///   Created 5/2/2011.
        /// </remarks>
        /// <param name = "spellId">The ID of the buff</param>
        /// <param name = "requirements">The requirements.</param>
        /// <returns>.</returns>
        public static Composite Buff(int spellId, SimpleBooleanDelegate requirements)
        {
            return Buff(spellId, ret => StyxWoW.Me.CurrentTarget, requirements);
        }

        /// <summary>
        ///   Creates a behavior to cast a buff by name on a specific unit. Returns
        ///   RunStatus.Success if successful, RunStatus.Failure otherwise.
        /// </summary>
        /// <remarks>
        ///   Created 5/2/2011.
        /// </remarks>
        /// <param name = "spellId">The ID of the buff</param>
        /// <param name = "onUnit">The on unit</param>
        /// <returns></returns>
        public static Composite Buff(int spellId, UnitSelectionDelegate onUnit)
        {
            return Buff(spellId, onUnit, ret => true);
        }

        /// <summary>
        ///   Creates a behavior to cast a buff by name, with special requirements, on a specific unit. Returns
        ///   RunStatus.Success if successful, RunStatus.Failure otherwise.
        /// </summary>
        /// <remarks>
        ///   Created 5/2/2011.
        /// </remarks>
        /// <param name = "spellId">The ID of the buff</param>
        /// <param name = "onUnit">The on unit</param>
        /// <param name = "requirements">The requirements.</param>
        /// <returns></returns>
        public static Composite Buff(int spellId, UnitSelectionDelegate onUnit, SimpleBooleanDelegate requirements)
        {
            UnitSelectionDelegate uncoveredUnit = ret =>
            {
                if (spellId <= 0 || onUnit == null || requirements == null)
                    return null;
                var target = onUnit(ret);
                return target != null && !target.Auras.Values.Any(a => a.SpellId == spellId)
                    ? target : null;
            };
            return new Decorator(ret => spellId > 0 && onUnit != null && requirements != null
                && uncoveredUnit(ret) != null,
                CastWithRecovery(spellId, uncoveredUnit, requirements, false, true));
        }

        #endregion

        #region BufSelf - by ID

        /// <summary>
        ///   Creates a behavior to cast a buff by ID on yourself. Returns
        ///   RunStatus.Success if successful, RunStatus.Failure otherwise.
        /// </summary>
        /// <remarks>
        ///   Created 5/6/2011.
        /// </remarks>
        /// <param name = "spellId">The buff ID.</param>
        /// <returns>.</returns>
        public static Composite BuffSelf(int spellId)
        {
            return Buff(spellId, ret => StyxWoW.Me, ret => true);
        }

        /// <summary>
        ///   Creates a behavior to cast a buff by ID on yourself with special requirements. Returns RunStatus.Success if
        ///   successful, RunStatus.Failure otherwise.
        /// </summary>
        /// <remarks>
        ///   Created 5/6/2011.
        /// </remarks>
        /// <param name = "spellId">The buff ID.</param>
        /// <param name = "requirements">The requirements.</param>
        /// <returns>.</returns>
        public static Composite BuffSelf(int spellId, SimpleBooleanDelegate requirements)
        {
            return Buff(spellId, ret => StyxWoW.Me, requirements);
        }

        #endregion

        #region Heal - by name

        /// <summary>
        ///   Creates a behavior to cast a heal spell by name. Heal behaviors will make sure
        ///   we don't double cast. Returns RunStatus.Success if successful, RunStatus.Failure otherwise.
        /// </summary>
        /// <remarks>
        ///   Created 5/2/2011.
        /// </remarks>
        /// <param name = "name">The name.</param>
        /// <returns>.</returns>
        public static Composite Heal(string name)
        {
            return Heal(name, ret => true);
        }

        /// <summary>
        ///   Creates a behavior to cast a heal spell by name, with special requirements. Heal behaviors will make sure
        ///   we don't double cast. Returns RunStatus.Success if successful, RunStatus.Failure otherwise.
        /// </summary>
        /// <remarks>
        ///   Created 5/2/2011.
        /// </remarks>
        /// <param name = "name">The name.</param>
        /// <param name = "requirements">The requirements.</param>
        /// <returns>.</returns>
        public static Composite Heal(string name, SimpleBooleanDelegate requirements)
        {
            return Heal(name, ret => true, ret => StyxWoW.Me, requirements);
        }

        /// <summary>
        ///   Creates a behavior to cast a heal spell by name, on a specific unit. Heal behaviors will make sure
        ///   we don't double cast. Returns RunStatus.Success if successful, RunStatus.Failure otherwise.
        /// </summary>
        /// <remarks>
        ///   Created 5/2/2011.
        /// </remarks>
        /// <param name = "name">The name.</param>
        /// <param name = "onUnit">The on unit.</param>
        /// <returns>.</returns>
        public static Composite Heal(string name, UnitSelectionDelegate onUnit)
        {
            return Heal(name, ret => true, onUnit, ret => true);
        }
        /// <summary>
        ///   Creates a behavior to cast a heal spell by name, on a specific unit. Heal behaviors will make sure
        ///   we don't double cast. Returns RunStatus.Success if successful, RunStatus.Failure otherwise.
        /// </summary>
        /// <remarks>
        ///   Created 5/2/2011.
        /// </remarks>
        /// <param name = "name">The name.</param>
        /// <param name = "onUnit">The on unit.</param>
        /// <param name = "requirements">The requirements.</param>
        /// <returns>.</returns>
        public static Composite Heal(string name, UnitSelectionDelegate onUnit, SimpleBooleanDelegate requirements)
        {
            return Heal(name, ret => true, onUnit, requirements);
        }

        /// <summary>
        ///   Creates a behavior to cast a heal spell by name, with special requirements, on a specific unit. Heal behaviors will make sure
        ///   we don't double cast. Returns RunStatus.Success if successful, RunStatus.Failure otherwise.
        /// </summary>
        /// <remarks>
        ///   Created 5/2/2011.
        /// </remarks>
        /// <param name = "name">The name.</param>
        /// <param name="checkMovement"></param>
        /// <param name = "onUnit">The on unit.</param>
        /// <param name = "requirements">The requirements.</param>
        /// <returns>.</returns>
        public static Composite Heal(string name, SimpleBooleanDelegate checkMovement, UnitSelectionDelegate onUnit, SimpleBooleanDelegate requirements)
        {
            WoWUnit owner = null;
            WoWUnit recipient = null;
            ulong ownerGuid = 0, recipientGuid = 0;
            WoWSpell selectedSpell = null;
            int expectedSpellId = 0;
            bool observedCast = false;
            bool IsCurrent() => ownerGuid != 0 && recipientGuid != 0
                && owner != null && recipient != null && owner.IsValid && owner.IsAlive
                && recipient.IsValid && recipient.Guid == recipientGuid && owner.Guid == ownerGuid
                && ReferenceEquals(owner, StyxWoW.Me);

            return
                new Sequence(
                    new Action(ret =>
                    {
                        // Reset on each activation; a reused factory cannot retain an
                        // earlier character, recipient or observed casting state.
                        recipient = null;
                        recipientGuid = 0;
                        selectedSpell = null;
                        expectedSpellId = 0;
                        observedCast = false;
                        owner = StyxWoW.Me;
                        ownerGuid = owner?.Guid ?? 0;
                        if (ownerGuid == 0 || owner == null || !owner.IsValid || !owner.IsAlive
                            || string.IsNullOrWhiteSpace(name) || onUnit == null || requirements == null || checkMovement == null)
                            return RunStatus.Failure;
                        recipient = onUnit(ret);
                        recipientGuid = recipient?.Guid ?? 0;
                        if (!IsCurrent()) return RunStatus.Failure;
                        SpellManager.Spells.TryGetValue(name, out selectedSpell);
                        expectedSpellId = selectedSpell?.Id ?? 0;
                        return IsCurrent() ? RunStatus.Success : RunStatus.Failure;
                    }),
                    CastWithRecovery(name, checkMovement, ret => IsCurrent() ? recipient : null,
                        ret => IsCurrent() && requirements(ret) && IsCurrent(), true, false),
                    // A local Cast receipt is not a native cast-instance receipt.
                    // Observe the expected spell, without adopting a different cast.
                    new WaitContinue(
                        1,
                        ret =>
                        {
                            if (!IsCurrent()) return true;
                            bool casting = owner.IsCasting;
                            int currentSpellId = owner.CastingSpellId;
                            if (casting && expectedSpellId > 0 && currentSpellId == expectedSpellId)
                            {
                                observedCast = IsCurrent();
                                return true;
                            }
                            return !IsCurrent() || selectedSpell == null || selectedSpell.CastTime == 0
                                || (casting && currentSpellId != expectedSpellId);
                        },
                        new ActionAlwaysSucceed()),
                    new WaitContinue(
                        10,
                        ret =>
                        {
                            if (!IsCurrent() || !observedCast || owner.CastingSpellId != expectedSpellId)
                                return true;
                            // Channels still run to completion; never cancel a
                            // replacement actor's channel to finish this old wait.
                            if (owner.ChanneledCastingSpellId != 0) return !IsCurrent();
                            if (!owner.IsCasting) return true;

                            bool needed = requirements(ret);
                            // Caller code can change actors, recipients or casts.
                            if (!IsCurrent() || owner.CastingSpellId != expectedSpellId || !owner.IsCasting)
                                return true;
                            if (owner.ChanneledCastingSpellId != 0) return !IsCurrent();
                            if (!needed)
                            {
                                if (IsCurrent() && owner.CastingSpellId == expectedSpellId)
                                    SpellManager.StopCasting();
                                return true;
                            }
                            return !IsCurrent();
                        },
                        new ActionAlwaysSucceed()),
                    new Action(ret => IsCurrent() ? RunStatus.Success : RunStatus.Failure));
        }

        #endregion

        #region CastOnGround - placeable spell casting

        /// <summary>
        ///   Creates a behavior to cast a spell by name, on the ground at the specified location. Returns
        ///   RunStatus.Success if successful, RunStatus.Failure otherwise.
        /// </summary>
        /// <remarks>
        ///   Created 5/2/2011.
        /// </remarks>
        /// <param name = "spell">The spell.</param>
        /// <param name = "onLocation">The on location.</param>
        /// <returns>.</returns>
        public static Composite CastOnGround(string spell, LocationRetriever onLocation)
        {
            return CastOnGround(spell, onLocation, ret => true);
        }

        /// <summary>
        ///   Creates a behavior to cast a spell by name, on the ground at the specified location. Returns RunStatus.Success if successful, RunStatus.Failure otherwise.
        /// </summary>
        /// <remarks>
        ///   Created 5/2/2011.
        /// </remarks>
        /// <param name = "spell">The spell.</param>
        /// <param name = "onLocation">The on location.</param>
        /// <param name = "requirements">The requirements.</param>
        /// <returns>.</returns>
        public static Composite CastOnGround(string spell, LocationRetriever onLocation, SimpleBooleanDelegate requirements)
        {
            LocalPlayer owner = null;
            ulong ownerGuid = 0;
            WoWPoint destination = WoWPoint.Empty;
            bool IsCurrent() => ownerGuid != 0 && owner != null && owner.IsValid && owner.IsAlive
                && owner.Guid == ownerGuid && ReferenceEquals(owner, StyxWoW.Me);
            bool CanPlace(object context)
            {
                if (!IsCurrent() || requirements == null || !requirements(context) || !IsCurrent()
                    || !Unit.IsAreaEffectSafe(spell, destination) || !IsCurrent()
                    || !SpellManager.Spells.TryGetValue(spell, out var metadata) || metadata == null)
                    return false;
                double maximum = metadata.MaxRange;
                double distance = owner.Location.Distance(destination);
                return double.IsFinite(maximum) && maximum >= 0 && double.IsFinite(distance)
                    && (maximum == 0 || distance <= maximum) && IsCurrent();
            }

            return new Decorator(
                ret =>
                {
                    owner = StyxWoW.Me;
                    ownerGuid = owner?.Guid ?? 0;
                    destination = WoWPoint.Empty;
                    if (string.IsNullOrWhiteSpace(spell) || onLocation == null || requirements == null || !IsCurrent())
                        return false;
                    // A location selector may follow a changing target. One cast
                    // retains its admitted point instead of selecting again after a wait.
                    destination = onLocation(ret);
                    return destination != WoWPoint.Empty && destination != WoWPoint.Zero
                        && double.IsFinite(destination.X) && double.IsFinite(destination.Y) && double.IsFinite(destination.Z)
                        && CanPlace(ret) && SpellManager.CanCast(spell) && IsCurrent();
                },
                new Sequence(
                    new Action(ret => Logger.Write("Casting {0} at location {1}", spell, destination)),
                    new Action(ret => CanPlace(ret) && SpellManager.CanCast(spell) && IsCurrent()
                        && SpellManager.Cast(spell) && IsCurrent() ? RunStatus.Success : RunStatus.Failure),
                    new WaitContinue(
                        1,
                        ret => !IsCurrent() || owner.HasPendingSpell(spell),
                        new ActionAlwaysSucceed()),
                    new Action(ret =>
                    {
                        // Timeout is not pending-cursor permission. The matching
                        // observation remains necessary, but is not native request provenance.
                        // CanCast is intentionally not repeated after submission: its
                        // own GCD/cooldown must not veto a healthy placement continuation.
                        if (!CanPlace(ret) || !owner.HasPendingSpell(spell) || !IsCurrent())
                            return RunStatus.Failure;
                        return SpellManager.ClickRemoteLocation(destination) && IsCurrent()
                            ? RunStatus.Success : RunStatus.Failure;
                    }))
                );
        }

        #endregion

        #region Resurrect

        /// <summary>
        ///   Creates a behavior to resurrect dead players around. This behavior will res each player once in every 10 seconds.
        ///   Returns RunStatus.Success if successful, RunStatus.Failure otherwise.
        /// </summary>
        /// <remarks>
        ///   Created 16/12/2011.
        /// </remarks>
        /// <param name = "spellName">The name of resurrection spell.</param>
        /// <returns>.</returns>
        public static Composite Resurrect(string spellName)
        {
            return
                new PrioritySelector(
                    ctx => Unit.ResurrectablePlayers.FirstOrDefault(u => !Blacklist.Contains(u)),
                    new Decorator(
                        ctx => ctx != null && SingularRoutine.CurrentWoWContext != WoWContext.Battlegrounds,
                        new Sequence(
                            Cast(spellName, ctx => (WoWPlayer)ctx),
                            new Action(ctx => Blacklist.Add((WoWPlayer)ctx, TimeSpan.FromSeconds(30))))));
        }

        #endregion

        public static float MeleeRange
        {
            get
            {
                var owner = StyxWoW.Me;
                var target = owner?.CurrentTarget;
                if (owner == null || !owner.IsValid || target == null || !target.IsValid
                    || target.Guid == 0 || owner.CurrentTargetGuid != target.Guid)
                    return 0f;

                float range = MeleeRangeFor(owner, target);
                return ReferenceEquals(owner, StyxWoW.Me) && ReferenceEquals(target, owner.CurrentTarget)
                    && owner.CurrentTargetGuid == target.Guid && ReferenceEquals(owner, StyxWoW.Me)
                    ? range : 0f;
            }
        }

        public static float SafeMeleeRange { get { return Math.Max(MeleeRange - 1f, 5f); } }

        public static float ActualMaxRange(this WoWSpell spell, WoWUnit unit)
        {
            if (spell.MaxRange == 0)
                return 0;
           return unit != null ? spell.MaxRange + unit.CombatReach + 1f : spell.MaxRange;
        }

        public static float ActualMinRange(this WoWSpell spell, WoWUnit unit)
        {
            if (spell.MinRange == 0)
                return 0;
            return unit != null ? spell.MinRange + unit.CombatReach + 1.6666667f : spell.MinRange;
        }
    }
}
