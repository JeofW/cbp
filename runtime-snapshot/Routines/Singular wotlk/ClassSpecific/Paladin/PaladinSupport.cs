using System;
using System.Collections.Generic;
using System.Linq;
using Singular.Helpers;
using Singular.Managers;
using Singular.Settings;
using Styx;
using Styx.Combat.CombatRoutine;
using Styx.Logic.Combat;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

namespace Singular.ClassSpecific.Paladin
{
    // WotLK 3.3.5a support decisions. No modern role, aura or spellbook Lua APIs.
    public partial class Common
    {
        private sealed class SupportAction
        {
            internal string Spell;
            internal WoWPlayer Target;
            internal Func<WoWPlayer, string> Revalidate;
        }

        private static bool CanMaintainSupport()
        {
            var me = StyxWoW.Me;
            return me != null && me.IsValid && me.IsAlive && !me.Mounted && !me.IsOnTransport
                && !me.IsCasting && !me.IsChanneling && !me.HasAura("Food") && !me.HasAura("Drink");
        }

        private static bool IsSupportRecipient(WoWPlayer player) =>
            player != null && player.IsValid && player.IsAlive && player.IsFriendly
            && (player.IsMe || player.DistanceSqr < 40 * 40 && player.InLineOfSpellSight);

        private static IEnumerable<WoWPlayer> SupportRecipients(bool includeGroup)
        {
            var me = StyxWoW.Me;
            if (me == null) yield break;
            if (IsSupportRecipient(me)) yield return me;
            if (!includeGroup) yield break;
            // Roster membership, not inferred class/spec or inspect-derived healing roles.
            var members = me.IsInRaid ? me.RaidMembers : me.IsInParty ? me.PartyMembers : Enumerable.Empty<WoWPlayer>();
            var seen = new HashSet<ulong> { me.Guid };
            foreach (var player in members)
                if (IsSupportRecipient(player) && seen.Add(player.Guid)) yield return player;
        }

        private static bool IsCurrentRecipient(WoWPlayer player, bool includeGroup) =>
            IsSupportRecipient(player) && SupportRecipients(includeGroup).Any(p => p.Guid == player.Guid);

        private static WoWAura[] SupportAuras(WoWPlayer player) =>
            player.GetAllAuras().Where(a => a != null && a.IsActive).ToArray();

        private static bool MatchesBlessing(WoWAura aura, string name) =>
            aura.Name == name || aura.Name == "Greater " + name;

        private static string SelectBlessing(WoWPlayer player)
        {
            if (!CanMaintainSupport() || !IsCurrentRecipient(player, true)) return null;
            var auras = SupportAuras(player);
            var setting = SingularSettings.Instance.Paladin.Blessings;
            string[] order;
            if (setting != PaladinBlessings.Auto)
                order = new[] { "Blessing of " + setting };
            else
            {
                bool caster = player.Class == WoWClass.Mage || player.Class == WoWClass.Priest
                    || player.Class == WoWClass.Warlock || player.HasAura("Moonkin Form") || player.HasAura("Tree of Life")
                    || player.IsMe && TalentManager.CurrentSpec == TalentSpec.HolyPaladin;
                order = caster
                    ? new[] { "Blessing of Kings", "Blessing of Wisdom" }
                    : new[] { "Blessing of Kings", "Blessing of Might", "Blessing of Wisdom" };
            }
            foreach (string name in order)
            {
                if (name == "Blessing of Wisdom" && player.MaxMana <= 0) continue;
                var coverage = auras.Where(a => MatchesBlessing(a, name)).ToArray();
                bool external = coverage.Any(a => a.CreatorGuid != 0 && a.CreatorGuid != StyxWoW.Me.Guid);
                // Our only useful contribution must not be replaced on the next pulse.
                // Multiple same-name owners are retained by GetAllAuras, not a name-keyed dictionary.
                if (coverage.Any(a => a.CreatorGuid == StyxWoW.Me.Guid) && !external) return null;
                if (coverage.Length != 0) continue;
                if (SpellManager.HasSpell(name) && SpellManager.CanCast(name, player)) return name;
            }
            return null;
        }

        private static SupportAction FindBlessingAction() => FindSupportAction(true, SelectBlessing);

        private static SupportAction FindSupportAction(bool includeGroup, Func<WoWPlayer, string> choose)
        {
            if (!CanMaintainSupport()) return null;
            foreach (var player in SupportRecipients(includeGroup))
            {
                string spell = choose(player);
                if (spell != null) return new SupportAction { Spell = spell, Target = player, Revalidate = choose };
            }
            return null;
        }

        private static Composite CreateSupportBehavior(Func<SupportAction> choose, params string[] spellNames)
        {
            return new Throttle(2, new PrioritySelector(_ => choose(),
                spellNames.Select(name => Spell.Cast(name,
                    context => ValidSupportAction(context, name) ? ((SupportAction)context).Target : null,
                    context => ValidSupportAction(context, name))).ToArray()));
        }

        private static bool ValidSupportAction(object context, string spell)
        {
            var action = context as SupportAction;
            return action != null && action.Spell == spell && CanMaintainSupport()
                && IsSupportRecipient(action.Target) && action.Revalidate(action.Target) == spell;
        }

        private static string SelectAura(WoWPlayer player)
        {
            if (!CanMaintainSupport() || !player.IsMe) return null;
            var setting = SingularSettings.Instance.Paladin.Aura;
            string[] order;
            if (setting != PaladinAura.Auto)
                order = new[] { setting == PaladinAura.Resistance ? "Shadow Resistance Aura" : setting + " Aura" };
            else if (TalentManager.CurrentSpec == TalentSpec.HolyPaladin)
                order = new[] { "Concentration Aura", "Devotion Aura", "Retribution Aura" };
            else if (TalentManager.CurrentSpec == TalentSpec.ProtectionPaladin && (player.IsInParty || player.IsInRaid))
                order = new[] { "Devotion Aura", "Retribution Aura", "Concentration Aura" };
            else
                order = new[] { "Retribution Aura", "Devotion Aura", "Concentration Aura" };
            var auras = SupportAuras(player);
            foreach (string name in order)
            {
                var coverage = auras.Where(a => a.Name == name).ToArray();
                bool external = coverage.Any(a => a.CreatorGuid != 0 && a.CreatorGuid != player.Guid);
                if (coverage.Any(a => a.CreatorGuid == player.Guid) && !external) return null;
                if (coverage.Length != 0) continue;
                if (SpellManager.HasSpell(name) && SpellManager.CanCast(name, player)) return name;
            }
            return null;
        }

        private static Composite CreatePaladinAuraBehavior() => CreateSupportBehavior(
            () => FindSupportAction(false, SelectAura), "Devotion Aura", "Retribution Aura", "Concentration Aura",
            "Shadow Resistance Aura", "Crusader Aura");

        // Removal can punish the dispeller or trigger a position-sensitive encounter mechanic.
        // This conservative list is NOT a complete encounter policy. Disable automatic dispels
        // for assignments that require the raid leader's timing rather than a generic decision.
        private static readonly HashSet<int> ManualDispelEffects = new HashSet<int>
        {
            28169, // Grobbulus: Mutating Injection
            70337, 73912, 73913, 73914, // Lich King: Necrotic Plague variants
            30108, 30404, 30405, 47841, 47843, // Unstable Affliction ranks
            34914, 34916, 34917, 48159, 48160 // Vampiric Touch ranks
        };

        private static string SelectDispel(WoWPlayer player)
        {
            var settings = SingularSettings.Instance.Paladin;
            if (!settings.DispelDebuffs || !CanMaintainSupport() || !IsCurrentRecipient(player, settings.DispelParty))
                return null;
            var harmful = SupportAuras(player).Where(a => a.IsHarmful && a.Spell != null).ToArray();
            bool SafeFor(params WoWDispelType[] types) =>
                harmful.Any(a => types.Contains(a.Spell.DispelType))
                && !harmful.Any(a => types.Contains(a.Spell.DispelType) && ManualDispelEffects.Contains(a.SpellId));
            // Purify cannot incidentally dispel unsafe Magic while curing Disease/Poison.
            // Cleanse can, so its complete removal mask must be safe, not just one debuff.
            bool purify = SafeFor(WoWDispelType.Disease, WoWDispelType.Poison)
                && SpellManager.HasSpell("Purify") && SpellManager.CanCast("Purify", player);
            bool cleanse = SafeFor(WoWDispelType.Disease, WoWDispelType.Poison, WoWDispelType.Magic)
                && SpellManager.HasSpell("Cleanse") && SpellManager.CanCast("Cleanse", player);
            if (cleanse && harmful.Any(a => a.Spell.DispelType == WoWDispelType.Magic)) return "Cleanse";
            return purify ? "Purify" : cleanse ? "Cleanse" : null;
        }

        public static Composite CreatePaladinDispelBehavior() => CreateSupportBehavior(
            () => FindSupportAction(SingularSettings.Instance.Paladin.DispelParty, SelectDispel), "Purify", "Cleanse");
    }
}
