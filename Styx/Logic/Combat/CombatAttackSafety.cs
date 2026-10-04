using System;
using System.Linq;
using Styx.Helpers;
using Styx.Logic.BehaviorTree;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic.Combat
{
    /// <summary>Owned autoattack/pet commands with original-client recipient checks.</summary>
    public static class CombatAttackSafety
    {
        public static bool TryStartAttack(WoWUnit target)
            => Submit(target, 0, 0, "target", "StartAttack()", false);

        public static bool TryPetAction(WoWUnit target, int slot, int spellId, bool useFocus = false)
        {
            if (slot < 1 || slot > 10 || target == null) return false;
            var pet = StyxWoW.Me?.Pet;
            if (pet == null || !pet.IsValid || !pet.IsAlive) return false;
            return Submit(target, pet.Guid, spellId, useFocus ? "focus" : "target",
                "CastPetAction(" + slot + ", '" + (useFocus ? "focus" : "target") + "')", true);
        }

        private static bool Submit(WoWUnit target, ulong petGuid, int spellId, string token, string action, bool petAction)
        {
            try
            {
                var actor = StyxWoW.Me;
                var pet = petAction ? actor?.Pet : null;
                var run = TreeRoot.RunIdentity;
                if (actor == null || target == null || !target.IsValid || !target.IsAlive) return false;
                var admitted = spellId > 0 ? CombatRecipientSafety.PrepareSpell(spellId, target.Guid)
                    : CombatRecipientSafety.PrepareAttack(target);
                if (admitted == null) return false;
                bool support = spellId > 0 && WoWSpell.FromId(spellId) is { } spell
                    && CombatRecipientSafety.IsFriendlySupport(spell, target);
                bool Current() => ReferenceEquals(TreeRoot.RunIdentity, run) && ReferenceEquals(StyxWoW.Me, actor)
                    && admitted() && (!petAction || ReferenceEquals(actor.Pet, pet) && pet != null && pet.IsValid
                        && pet.IsAlive && pet.Guid == petGuid && pet.DescriptorGuid == petGuid);
                if (!Current()) return false;
                string script = BuildAttackLua(actor.Guid, target.Guid, petGuid, token, action, support);
                var receipt = Lua.GetObservedReturnValues(script, Current);
                return receipt.Count == 1 && receipt[0] == "1";
            }
            catch (Exception error)
            {
                RecoveryActions.RethrowControlFlow(error);
                if (ObservationUnavailableException.Find(error) is { } unknown)
                    ObservationFailureDiagnostics.Report(unknown, "combat-attack");
                return false;
            }
        }

        internal static string BuildAttackLua(ulong actorGuid, ulong targetGuid, ulong petGuid, string token, string action,
            bool support = false)
        {
            if (actorGuid == 0 || targetGuid == 0 || token is not ("target" or "focus"))
                throw new ArgumentException("Captured combat recipients required.");
            return "local actor='0x" + actorGuid.ToString("X16") + "'; local recipient='0x" + targetGuid.ToString("X16")
                + "'; local pet='0x" + petGuid.ToString("X16") + "'; local token='" + token + "';\n"
                + @"
local function same(a,b) return type(a)=='string' and string.upper(a)==string.upper(b) end
if type(UnitGUID)~='function' or not same(UnitGUID('player'),actor) or not same(UnitGUID(token),recipient) then return 0 end
if pet~='0x0000000000000000' and not same(UnitGUID('pet'),pet) then return 0 end
"
                + (support ? "if type(UnitCanAssist)~='function' or not UnitCanAssist('player',token) then return 0 end\n" : @"
if type(UnitIsPlayer)~='function' or type(UnitCanAttack)~='function' or not UnitCanAttack('player',token) then return 0 end
if UnitIsPlayer(token) then
 if type(IsInInstance)~='function' or type(GetNumPartyMembers)~='function' or type(GetNumRaidMembers)~='function' then return 0 end
 local inside,kind=IsInInstance()
 if kind=='party' or kind=='raid' then return 0 end
 local parties,raiders=GetNumPartyMembers(),GetNumRaidMembers()
 if type(parties)~='number' or parties<0 or parties>4 or parties~=math.floor(parties)
  or type(raiders)~='number' or raiders<0 or raiders>40 or raiders~=math.floor(raiders) then return 0 end
 for index=1,parties do local guid=UnitGUID('party'..index);if type(guid)~='string' or same(guid,recipient) then return 0 end end
 for index=1,raiders do local guid=UnitGUID('raid'..index);if type(guid)~='string' or same(guid,recipient) then return 0 end end
 if same(recipient,actor) then return 0 end
end
")
                + "if not same(UnitGUID('player'),actor) or not same(UnitGUID(token),recipient) then return 0 end\n"
                + "if pet~='0x0000000000000000' and not same(UnitGUID('pet'),pet) then return 0 end\n"
                + action + "; return 1";
        }

        /// <summary>Revoke ongoing auto/pet attacks when their current recipient becomes protected.</summary>
        public static void StopUnsafeAttacks()
        {
            var actor = StyxWoW.Me;
            if (actor == null || !actor.IsValid || !actor.IsAlive) return;
            var target = actor.CurrentTarget;
            var pet = actor.Pet;
            var petTarget = pet?.CurrentTarget;
            bool stopAuto = target != null && (actor.IsAutoAttacking || actor.AutoRepeatingSpellId != 0)
                && CombatRecipientSafety.PrepareAttack(target) == null;
            bool stopPet = pet != null && pet.IsAlive && pet.Combat && petTarget != null
                && CombatRecipientSafety.PrepareAttack(petTarget) == null;
            if (!stopAuto && !stopPet) return;
            ulong actorGuid = actor.Guid, targetGuid = target?.Guid ?? 0, petGuid = pet?.Guid ?? 0, petTargetGuid = petTarget?.Guid ?? 0;
            var memory = ObjectManager.Wow;
            var executor = ObjectManager.Executor;
            var run = TreeRoot.RunIdentity;
            bool Current() => ReferenceEquals(TreeRoot.RunIdentity, run) && ReferenceEquals(ObjectManager.Wow, memory)
                && ReferenceEquals(ObjectManager.Executor, executor) && ReferenceEquals(StyxWoW.Me, actor)
                && actor.IsValid && actor.DescriptorGuid == actorGuid
                && (!stopAuto || ReferenceEquals(actor.CurrentTarget, target) && target?.DescriptorGuid == targetGuid)
                && (!stopPet || ReferenceEquals(actor.Pet, pet) && pet?.DescriptorGuid == petGuid
                    && ReferenceEquals(pet.CurrentTarget, petTarget) && petTarget?.DescriptorGuid == petTargetGuid);
            string command = "local function same(a,b) return type(a)=='string' and string.upper(a)==string.upper(b) end; "
                + "if not same(UnitGUID('player'),'0x" + actorGuid.ToString("X16") + "') then return 0 end; "
                + (stopAuto ? "if same(UnitGUID('target'),'0x" + targetGuid.ToString("X16")
                    + "') then StopAttack(); " + (actor.AutoRepeatingSpellId != 0 ? "if IsAutoRepeatSpell(75) or IsAutoRepeatSpell(5019) then SpellStopCasting() end; " : "") + "end; " : "")
                + (stopPet ? "if same(UnitGUID('pet'),'0x" + petGuid.ToString("X16") + "') and same(UnitGUID('pettarget'),'0x"
                    + petTargetGuid.ToString("X16") + "') then PetFollow() end; " : "")
                + "return 1";
            if (Current()) Lua.GetObservedReturnValues(command, Current);
        }
    }
}
