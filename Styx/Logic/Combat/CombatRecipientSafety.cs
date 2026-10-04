using System;
using System.Collections.Generic;
using System.Linq;
using Styx.Helpers;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic.Combat
{
    /// <summary>
    /// Observe harmful-action recipients before preparing a native command, then
    /// supply a memory-only admission check for its final submission boundary.
    /// Reaction, aura and spell metadata queries never run in that final check.
    /// </summary>
    public static class CombatRecipientSafety
    {
        private sealed class Participant
        {
            internal readonly WoWUnit Unit;
            private readonly ulong _guid, _descriptor, _charmer, _summoner, _creator, _duel;
            private readonly uint _address, _type, _faction, _duelTeam;
            private readonly bool _controlled, _possessed;
            internal Participant(WoWUnit unit)
            {
                using var uncached = ObjectManager.Wow?.TemporaryCacheState(false);
                Unit = unit; _guid = unit.Guid; _descriptor = unit.DescriptorGuid;
                _address = unit.BaseAddress; _type = (uint)unit.Type; _faction = unit.FactionId;
                _charmer = unit.CharmedByGuid; _summoner = unit.SummonedByGuid; _creator = unit.CreatedByGuid;
                _controlled = unit.PlayerControlled; _possessed = unit.Possessed;
                if (unit is WoWPlayer player) { _duel = player.DuelArbiterGuid; _duelTeam = player.DuelTeam; }
            }
            internal bool Current
            {
                get
                {
                    using var uncached = ObjectManager.Wow?.TemporaryCacheState(false);
                    return _guid != 0 && _address != 0 && _descriptor == _guid
                && Unit.IsValid && Unit.Guid == _guid && Unit.DescriptorGuid == _descriptor
                && Unit.BaseAddress == _address && (uint)Unit.Type == _type && Unit.FactionId == _faction
                && Unit.CharmedByGuid == _charmer && Unit.SummonedByGuid == _summoner && Unit.CreatedByGuid == _creator
                && Unit.PlayerControlled == _controlled && Unit.Possessed == _possessed
                && (Unit is not WoWPlayer player || player.DuelArbiterGuid == _duel && player.DuelTeam == _duelTeam)
                && ReferenceEquals(Unit, Unit.IsMe ? StyxWoW.Me : ObjectManager.GetObjectByGuid<WoWUnit>(_guid));
                }
            }
        }

        public static Func<bool>? PrepareSpell(int spellId, ulong targetGuid)
            => Prepare(spellId, targetGuid, false);

        public static Func<bool>? PrepareAttack(WoWUnit target)
            => target == null ? null : Prepare(0, target.Guid, true);

        private static Func<bool>? Prepare(int spellId, ulong targetGuid, bool attack)
        {
            var actor = StyxWoW.Me;
            if (actor == null || !actor.IsValid) return null;
            var memory = ObjectManager.Wow;
            var executor = ObjectManager.Executor;
            if (memory == null || executor == null) return null;
            var implicitTarget = targetGuid == 0 ? actor.CurrentTarget : null;
            var recipient = targetGuid == 0 ? implicitTarget ?? actor
                : targetGuid == actor.Guid ? actor : ObjectManager.GetObjectByGuid<WoWUnit>(targetGuid);
            if (recipient == null || !recipient.IsValid) return null;
            var actorStamp = new Participant(actor);
            var recipientStamp = ReferenceEquals(actor, recipient) ? actorStamp : new Participant(recipient);
            uint map = actor.MapId;
            bool dungeon = actor.CurrentMap.IsDungeon, raid = actor.CurrentMap.IsRaid;
            bool inParty = actor.IsInParty, inRaid = actor.IsInRaid;
            if (!GroupObservation.TryReadMemberGuids(actor, out var group)) return null;
            // Include derived WoWPlayer wrappers AND a reused WoWUnit whose raw
            // type has changed. Movement does not replace group identity.
            WoWUnit[] Players() => ObjectManager.GetObjectsOfType<WoWUnit>(true, false)
                .Where(unit => unit is WoWPlayer || unit.IsPlayer).ToArray();
            var players = Players().Select(unit => new Participant(unit)).ToArray();
            bool requireNoCleave = false;
            bool NoCleave()
            {
                // Raw aura IDs/flags are memory observations, not name/mechanic
                // lookups. This remains safe inside a prepared native guard.
                if (!actor.TryGetRawAuras(out var auras) || auras == null) return false;
                return !auras.Any(aura => aura.IsActive && (aura.SpellId == 20375
                    || aura.SpellId == 13877 || aura.SpellId == 12328));
            }
            bool Current()
            {
                using var uncached = memory.TemporaryCacheState(false);
                if (!ReferenceEquals(actor, StyxWoW.Me) || !ReferenceEquals(memory, ObjectManager.Wow)
                    || !ReferenceEquals(executor, ObjectManager.Executor) || !actorStamp.Current || !recipientStamp.Current
                    || actor.MapId != map || actor.CurrentMap.IsDungeon != dungeon || actor.CurrentMap.IsRaid != raid
                    || actor.IsInParty != inParty || actor.IsInRaid != inRaid
                    || !GroupObservation.TryReadMemberGuids(actor, out var liveGroup) || !group.SequenceEqual(liveGroup)
                    || targetGuid == 0 && !ReferenceEquals(actor.CurrentTarget, implicitTarget)) return false;
                var livePlayers = Players();
                return livePlayers.Length == players.Length && players.All(snapshot => snapshot.Current
                    && livePlayers.Any(unit => ReferenceEquals(unit, snapshot.Unit)))
                    && (!requireNoCleave || NoCleave()) && actorStamp.Current && recipientStamp.Current;
            }
            if (!Current()) return null;

            WoWSpell? spell = attack ? null : WoWSpell.FromId(spellId);
            if (!attack && (spell == null || spell.Id != spellId || !HasCompleteEffects(spell.InternalInfo))) return null;
            if (!Current()) return null;
            bool support = !attack && IsFriendlySupport(spell!, recipient);
            if (!Current()) return null;
            // The explicit player recipient is also how caster-centered spells
            // such as Consecration are submitted. Their secondary recipients are
            // checked below; selecting the caster is not a direct friendly attack.
            if (!recipient.IsMe && GroupCombatSafety.IsProtectedPlayer(recipient) && !support) return null;
            if (!support && (!recipient.IsAlive || !recipient.IsMe && !GroupCombatSafety.MayAttack(recipient))) return null;
            if (!Current()) return null;

            if (!support)
            {
                bool protectedHazard = false;
                foreach (var participant in players)
                {
                    var player = participant.Unit;
                    if (!player.IsAlive || !GroupCombatSafety.IsProtectedPlayer(player)) continue;
                    // Charm can set an owner before the native reaction update.
                    // It is unsafe to treat that transition as a friendly absence.
                    if (player.CharmedByGuid != 0 || player.Possessed || !player.IsFriendly) protectedHazard = true;
                    if (!Current()) return null;
                }
                if (protectedHazard)
                {
                    // Unknown/scripted/area/chain effects cannot prove exclusion
                    // of a protected player. Direct single-recipient damage can
                    // continue only with observed absence of passive cleaves.
                    if (!attack && !IsSingleEnemyEffect(spell!.InternalInfo)) return null;
                    requireNoCleave = true;
                    if (!Current()) return null;
                }
            }
            return Current() ? Current : null;
        }

        private static bool HasCompleteEffects(SpellEntry row) => row.Id != 0
            && row.Effect?.Length == 3 && row.EffectImplicitTargetA?.Length == 3 && row.EffectImplicitTargetB?.Length == 3
            && row.EffectRadiusIndex?.Length == 3 && row.EffectChainTarget?.Length == 3
            && row.EffectApplyAuraName?.Length == 3 && row.EffectTriggerSpell?.Length == 3;

        internal static bool IsFriendlySupport(WoWSpell spell, WoWUnit recipient)
        {
            var row = spell.InternalInfo;
            if ((row.Attributes & 0x04000000u) != 0) return false;
            // These original-client dummy effects explicitly select the healing
            // branch for a friendly target. A final relation change revokes it.
            if ((spell.Name == "Holy Shock" || spell.Name == "Penance")
                && row.Effect.Take(3).Any(effect => effect == 3)
                && row.EffectImplicitTargetA.Take(3).Any(target => target == 25))
                return recipient.IsMe || recipient.IsFriendly;
            // Original Dispel Magic selects friend or enemy. Only the friendly
            // removal branch is support; final relation admission still applies.
            if (row.Effect.Where(effect => effect != 0).All(effect => effect == 38)
                && row.Effect.Any(effect => effect == 38)
                && row.EffectImplicitTargetA.All(target => target is 0 or 21 or 25)
                && row.EffectImplicitTargetB.All(target => target == 0))
                return recipient.IsMe || recipient.IsFriendly;
            bool present = false, casterOnly = true;
            for (int i = 0; i < 3; i++)
            {
                if (row.Effect[i] == 0) continue;
                present = true;
                // Instakill, damage, resource drain/burn and weapon effects never
                // become support merely because a stale recipient is friendly.
                if (row.Effect[i] is 1 or 2 or 8 or 9 or 17 or 31 or 58 or 62 or 121)
                    return false;
                // Caster-targeted periodic damage (for example Hellfire) is not
                // a defensive aura. Its hostile area recipient check is required.
                if (row.EffectApplyAuraName[i] is 3 or 53 or 64 or 89 or 162)
                    return false;
                if (!FriendlyTarget(row.EffectImplicitTargetA[i]) || !FriendlyTarget(row.EffectImplicitTargetB[i]))
                    return false;
                if (row.EffectImplicitTargetA[i] == 0 && row.EffectImplicitTargetB[i] == 0) return false;
                casterOnly &= (row.EffectImplicitTargetA[i] is 0 or 1) && (row.EffectImplicitTargetB[i] is 0 or 1);
            }
            return present && (casterOnly || recipient.IsMe || recipient.IsFriendly);
        }

        // Original build12340 Targets enum: caster, ally, party, raid and their
        // area selectors. TARGET_UNIT_TARGET_ANY (25) is deliberately excluded.
        private static bool FriendlyTarget(uint target) => target is 0 or 1 or 3 or 4 or 5 or 20 or 21
            or 27 or 29 or 30 or 31 or 33 or 34 or 35 or 37 or 45 or 56 or 57 or 58 or 59 or 61 or 92;

        private static bool IsSingleEnemyEffect(SpellEntry row)
        {
            bool present = false;
            for (int i = 0; i < 3; i++)
            {
                if (row.Effect[i] == 0) continue;
                present = true;
                if (row.EffectChainTarget[i] > 1 || row.EffectRadiusIndex[i] != 0
                    || !((row.EffectImplicitTargetA[i] == 6 && row.EffectImplicitTargetB[i] == 0)
                        || (row.EffectImplicitTargetA[i] == 0 && row.EffectImplicitTargetB[i] == 6))) return false;
            }
            return present;
        }
    }
}
