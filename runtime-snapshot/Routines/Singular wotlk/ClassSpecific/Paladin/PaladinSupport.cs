using System;
using System.Collections.Generic;
using System.Linq;
using Singular.Dynamics;
using Singular.Helpers;
using Singular.Managers;
using Singular.Settings;
using Styx;
using Styx.Combat.CombatRoutine;
using Styx.Logic.Combat;
using Styx.WoWInternals;
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
            internal WoWPlayer Caster;
            internal ulong CasterGuid, TargetGuid;
            internal WoWPlayer Target;
            internal Func<WoWPlayer, string> Revalidate;

            // A selected action belongs to these observed participants. A new
            // valid actor must make a new decision, not inherit this action.
            internal bool HasCurrentParticipants => Caster != null && Target != null
                && CasterGuid != 0 && TargetGuid != 0
                && ReferenceEquals(StyxWoW.Me, Caster) && Caster.Guid == CasterGuid
                && Target.Guid == TargetGuid;
        }

        private static bool CanMaintainSupport()
        {
            var me = StyxWoW.Me;
            return me != null && me.IsValid && me.IsAlive && !me.Mounted && !me.IsOnTransport
                && !me.IsCasting && !me.IsChanneling
                && Styx.Logic.Common.Rest.TryObserveActivity(me, out bool food, out bool drink) && !food && !drink;
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

        // Exact name families from the original enUS build-12340 Spell.dbc. This
        // is a closed coverage contract for these policies, not a generic claim
        // about missing server metadata. See docs/audit/2026-10-02/paladin-observation.
        internal static string SupportedAuraName(WoWAura aura)
        {
            switch (aura.SpellId)
            {
                case 20217: case 56525: case 58054:
                    return "Blessing of Kings";
                case 19740: case 19834: case 19835: case 19836: case 19837:
                case 19838: case 25291: case 27140: case 48931: case 48932:
                case 56520:
                    return "Blessing of Might";
                case 19742: case 19850: case 19852: case 19853: case 19854:
                case 25290: case 27142: case 48935: case 48936: case 56521:
                    return "Blessing of Wisdom";
                case 20911: case 57319: case 57320: case 57321: case 67480:
                    return "Blessing of Sanctuary";
                case 25898: case 43223:
                    return "Greater Blessing of Kings";
                case 25782: case 25916: case 27141: case 29381: case 33564:
                case 43940: case 48933: case 48934:
                    return "Greater Blessing of Might";
                case 25894: case 25918: case 27143: case 48937: case 48938:
                    return "Greater Blessing of Wisdom";
                case 25899:
                    return "Greater Blessing of Sanctuary";
                case 2048: case 5242: case 6192: case 6673: case 9128:
                case 11549: case 11550: case 11551: case 24438: case 25101:
                case 25289: case 26043: case 26099: case 27578: case 30635:
                case 30833: case 30931: case 31403: case 32064: case 38232:
                case 42247: case 46763: case 47436: case 49724: case 59614:
                case 64062: case 70750:
                    return "Battle Shout";
                case 24858: case 48369: case 53506: case 62795:
                    return "Moonkin Form";
                case 5420: case 33891: case 34123: case 48371: case 53691:
                case 65139:
                    return "Tree of Life";
                case 465: case 643: case 1032: case 8258: case 10290:
                case 10291: case 10292: case 10293: case 17232: case 27149:
                case 41452: case 48941: case 48942: case 52442: case 57740:
                case 58944:
                    return "Devotion Aura";
                case 7294: case 8990: case 10298: case 10299: case 10300:
                case 10301: case 13008: case 27150: case 54043:
                    return "Retribution Aura";
                case 19746:
                    return "Concentration Aura";
                case 19876: case 19895: case 19896: case 27151: case 48943:
                    return "Shadow Resistance Aura";
                case 19888: case 19897: case 19898: case 27152: case 48945:
                    return "Frost Resistance Aura";
                case 19891: case 19899: case 19900: case 27153: case 48947:
                    return "Fire Resistance Aura";
                case 32223:
                    return "Crusader Aura";
                case 20375: case 20424: case 29385: case 33127: case 41469:
                case 42058: case 57769: case 57770: case 66004: case 68020:
                case 68021: case 68022: case 69403:
                    return "Seal of Command";
                case 53736: case 53739:
                    return "Seal of Corruption";
                case 20164:
                    return "Seal of Justice";
                case 20165: case 20167:
                    return "Seal of Light";
                case 20154: case 21084: case 25742:
                    return "Seal of Righteousness";
                case 31801: case 42463:
                    return "Seal of Vengeance";
                case 20166: case 20168:
                    return "Seal of Wisdom";
                case 20186: case 20268: case 53408:
                    return "Judgement of Wisdom";
                case 20185: case 20267: case 20271: case 28775: case 57774:
                    return "Judgement of Light";
                case 14267:
                    return "Horde Flag";
                case 14268:
                    return "Alliance Flag";
                case 642: case 13874: case 29382: case 33581: case 40733:
                case 41367: case 54322: case 63148: case 66010: case 67251:
                case 71550:
                    return "Divine Shield";
                case 498: case 13007: case 27778: case 27779:
                    return "Divine Protection";
                case 31884: case 43430: case 50837: case 66011:
                    return "Avenging Wrath";
                case 54428:
                    return "Divine Plea";
                default: return null;
            }
        }

        private static WoWAura[] SupportCoverageAuras(WoWUnit player) =>
            player.GetRawAuras().Where(a => a != null && a.IsActive).ToArray();

        internal static bool HasSupportedAura(WoWUnit player, string name) =>
            SupportCoverageAuras(player).Any(a => SupportedAuraName(a) == name);

        private enum PallyPowerReadStatus
        {
            Absent,
            Verified,
            Uncertain
        }

        private sealed class PallyPowerAssignment
        {
            internal PallyPowerReadStatus Status;
            internal string Blessing;
            internal string Aura;
        }

        // PallyPower v3.2.21 Wrath mapping, corroborated by the reviewed W61
        // upload and public source d8f7a78de637e73d7d3e44a67721623f1d3fcd07.
        // Raw slot numbers are never interpreted unless the loaded addon itself
        // reports IsWrath and the explicit "Wrath" tables are readable.
        private static readonly string[] PallyPowerWrathBlessings =
        {
            null, "Blessing of Wisdom", "Blessing of Might",
            "Blessing of Kings", "Blessing of Sanctuary"
        };

        private static readonly string[] PallyPowerWrathAuras =
        {
            null, "Devotion Aura", "Retribution Aura", "Concentration Aura",
            "Shadow Resistance Aura", "Frost Resistance Aura", "Fire Resistance Aura",
            "Crusader Aura"
        };

        private static int PallyPowerClassIndex(WoWClass wowClass)
        {
            switch (wowClass)
            {
                case WoWClass.Warrior: return 1;
                case WoWClass.Rogue: return 2;
                case WoWClass.Priest: return 3;
                case WoWClass.Druid: return 4;
                case WoWClass.Paladin: return 5;
                case WoWClass.Hunter: return 6;
                case WoWClass.Mage: return 7;
                case WoWClass.Warlock: return 8;
                case WoWClass.Shaman: return 9;
                case WoWClass.DeathKnight: return 10;
                default: return 0;
            }
        }

        private static PallyPowerAssignment ReadPallyPowerAssignment(WoWPlayer player)
        {
            if (player == null || string.IsNullOrEmpty(player.Name))
                return new PallyPowerAssignment { Status = PallyPowerReadStatus.Uncertain };

            int classIndex = PallyPowerClassIndex(player.Class);
            if (classIndex == 0)
                return new PallyPowerAssignment { Status = PallyPowerReadStatus.Uncertain };

            string target = Lua.Escape(player.Name);
            string query =
                "local classIndex=" + classIndex + ";local targetName=\"" + target + "\";" +
                "if type(IsAddOnLoaded)=='function' and not IsAddOnLoaded('PallyPower') then return '0' end;" +
                "if type(GetAddOnMetadata)~='function' then return '2' end;" +
                "local ppVersion=GetAddOnMetadata('PallyPower','Version');if ppVersion~='v3.2.21' then return '2' end;" +
                "local pp=PallyPower;if type(pp)~='table' then return '2' end;" +
                "if pp.IsWrath~=true then return '2' end;" +
                "local a=PallyPower_Assignments and PallyPower_Assignments[\"Wrath\"];" +
                "local n=PallyPower_NormalAssignments and PallyPower_NormalAssignments[\"Wrath\"];" +
                "local u=PallyPower_AuraAssignments and PallyPower_AuraAssignments[\"Wrath\"];" +
                "if type(a)~='table' or type(n)~='table' or type(u)~='table' or type(pp.player)~='string' then return '2' end;" +
                "local p=pp.player;local classSlot=0;local normalSlot=0;local auraSlot=tonumber(u[p]) or 0;" +
                "if type(a[p])=='table' then classSlot=tonumber(a[p][classIndex]) or 0 end;" +
                "if type(n[p])=='table' and type(n[p][classIndex])=='table' then normalSlot=tonumber(n[p][classIndex][targetName]) or 0 end;" +
                "return '1',tostring(classSlot),tostring(normalSlot),tostring(auraSlot)";

            List<string> values;
            try
            {
                values = Lua.GetReturnValues(query);
            }
            catch (Exception error)
            {
                RecoveryActions.RethrowControlFlow(error);
                return new PallyPowerAssignment { Status = PallyPowerReadStatus.Uncertain };
            }

            if (values == null || values.Count == 0)
                return new PallyPowerAssignment { Status = PallyPowerReadStatus.Uncertain };
            if (values[0] == "0")
                return new PallyPowerAssignment { Status = PallyPowerReadStatus.Absent };
            if (values[0] != "1" || values.Count != 4)
                return new PallyPowerAssignment { Status = PallyPowerReadStatus.Uncertain };

            int classSlot, normalSlot, auraSlot;
            if (!int.TryParse(values[1], out classSlot) ||
                !int.TryParse(values[2], out normalSlot) ||
                !int.TryParse(values[3], out auraSlot) ||
                classSlot < 0 || classSlot >= PallyPowerWrathBlessings.Length ||
                normalSlot < 0 || normalSlot >= PallyPowerWrathBlessings.Length ||
                auraSlot < 0 || auraSlot >= PallyPowerWrathAuras.Length)
                return new PallyPowerAssignment { Status = PallyPowerReadStatus.Uncertain };

            int blessingSlot = normalSlot > 0 ? normalSlot : classSlot;
            return new PallyPowerAssignment
            {
                Status = PallyPowerReadStatus.Verified,
                Blessing = PallyPowerWrathBlessings[blessingSlot],
                Aura = PallyPowerWrathAuras[auraSlot]
            };
        }

        private static bool MatchesBlessing(WoWAura aura, string name) =>
            SupportedAuraName(aura) == name || SupportedAuraName(aura) == "Greater " + name;

        private static string SelectBlessing(WoWPlayer player)
        {
            string normal = SelectNormalBlessing(player);
            return normal == null ? null : SelectBlessingVariant(player, normal);
        }

        private static string SelectNormalBlessing(WoWPlayer player)
        {
            if (!CanMaintainSupport() || !IsCurrentRecipient(player, true)) return null;
            var auras = SupportCoverageAuras(player).Where(a => a.TimeLeft > TimeSpan.Zero).ToArray();
            var paladinSettings = SingularSettings.Instance.Paladin;
            var setting = paladinSettings.Blessings;
            bool battleShout = auras.Any(a => SupportedAuraName(a) == "Battle Shout");
            string[] order;
            bool assignmentControlled = false;
            if (setting != PaladinBlessings.Auto)
                order = new[] { "Blessing of " + setting };
            else
            {
                if (paladinSettings.UsePallyPowerAssignments)
                {
                    PallyPowerAssignment assignment = ReadPallyPowerAssignment(player);
                    if (assignment.Status == PallyPowerReadStatus.Uncertain)
                        return null;
                    if (assignment.Status == PallyPowerReadStatus.Verified)
                    {
                        assignmentControlled = true;
                        if (string.IsNullOrEmpty(assignment.Blessing))
                            return null;
                        order = new[] { assignment.Blessing };
                    }
                    else
                        order = null;
                }
                else
                    order = null;

                if (!assignmentControlled)
                {
                    bool caster = player.Class == WoWClass.Mage || player.Class == WoWClass.Priest
                        || player.Class == WoWClass.Warlock || HasSupportedAura(player, "Moonkin Form") || HasSupportedAura(player, "Tree of Life")
                        || player.IsMe && TalentManager.CurrentSpec == TalentSpec.HolyPaladin;
                    // Ret is a known damage role only for our own character. Do not
                    // invent a teammate's spec/tank assignment from its class alone.
                    bool groupedRet = player.IsMe && TalentManager.CurrentSpec == TalentSpec.RetributionPaladin
                        && (StyxWoW.Me.IsInParty || StyxWoW.Me.IsInRaid)
                        && SingularRoutine.CurrentWoWContext != WoWContext.Battlegrounds;
                    order = caster
                        ? new[] { "Blessing of Kings", "Blessing of Wisdom" }
                        : groupedRet && !battleShout
                            ? new[] { "Blessing of Might", "Blessing of Kings", "Blessing of Wisdom" }
                            : new[] { "Blessing of Kings", "Blessing of Might", "Blessing of Wisdom" };

                    // Preserve a unique, useful contribution rather than fighting an
                    // existing assignment on every pulse. For known duplicate
                    // creators, one stable GUID winner retains the contribution
                    // while the others fill missing coverage. Explicit settings and a verified
                    // PallyPower assignment bypass this Auto contribution rule.
                    foreach (string retained in new[] { "Blessing of Kings", "Blessing of Might", "Blessing of Wisdom" })
                    {
                        if (retained == "Blessing of Might" && (caster || battleShout)) continue;
                        if (retained == "Blessing of Wisdom" && player.MaxMana <= 0) continue;
                        var owners = auras.Where(a => MatchesBlessing(a, retained)).ToArray();
                        if (owners.Any(a => a.CreatorGuid == StyxWoW.Me.Guid)
                            && !owners.Any(a => a.CreatorGuid > StyxWoW.Me.Guid))
                            return null;
                    }
                }
            }
            foreach (string name in order)
            {
                // Flat attack-power coverage is not the separate percentage-AP
                // category (Trueshot/Unleashed Rage/Abomination's Might).
                // Explicit preference is not permission to fight existing coverage.
                // Effective aura rank/talent strength is not observed here: defer
                // Might conservatively and reconsider when the covering aura expires.
                if (name == "Blessing of Might" && battleShout) continue;
                if (name == "Blessing of Wisdom" && player.MaxMana <= 0) continue;
                var coverage = auras.Where(a => MatchesBlessing(a, name)).ToArray();
                bool external = coverage.Any(a => a.CreatorGuid != 0 && a.CreatorGuid != StyxWoW.Me.Guid);
                // Our only useful contribution must not be replaced on the next pulse.
                // Multiple same-name owners are retained by GetAllAuras, not a name-keyed dictionary.
                if (coverage.Any(a => a.CreatorGuid == StyxWoW.Me.Guid) && !external) return null;
                if (coverage.Length != 0) continue;
                if (SpellManager.HasSpell(name) && SupportSpellAvailable(name, player)) return name;
            }
            return null;
        }

        private static string SelectBlessingVariant(WoWPlayer player, string normal)
        {
            var me = StyxWoW.Me;
            string greater = "Greater " + normal;
            if (!SingularSettings.Instance.Paladin.UseGreaterBlessings || me == null
                || me.Guid == 0 || player.Guid == 0 || me.Combat
                || !SpellManager.HasSpell(greater) || !SupportSpellAvailable(greater, player)
                || !HasGreaterBlessingReagents(greater))
                return normal;

            // Greater blessings can reach other group members of the selected
            // class. Do not silently replace an assignment or borrow coverage
            // from an unobservable member. The normal action remains available.
            if (!Styx.Logic.GroupObservation.TryGetMembers(me, out var members, out _)) return normal;
            var seen = new HashSet<ulong>();
            foreach (var member in new[] { me }.Cast<WoWPlayer>().Concat(members))
            {
                if (member.Class != player.Class || !seen.Add(member.Guid)) continue;
                if (!IsSupportRecipient(member)) return normal;
                var auras = SupportCoverageAuras(member).Where(a => a.TimeLeft > TimeSpan.Zero).ToArray();
                if (auras.Any(a => a.CreatorGuid == me.Guid
                    && ((SupportedAuraName(a) ?? "").StartsWith("Blessing of ", StringComparison.Ordinal)
                        || (SupportedAuraName(a) ?? "").StartsWith("Greater Blessing of ", StringComparison.Ordinal))
                    && !MatchesBlessing(a, normal)))
                    return normal;
                // Evaluate only the underlying single-target policy here, not
                // this variant selector recursively. Covered/discordant members
                // deny a mass rebuff, including Might versus active Battle Shout.
                if (SelectNormalBlessing(member) != normal) return normal;
            }
            return greater;
        }

        private static bool HasGreaterBlessingReagents(string name)
        {
            WoWSpell spell;
            if (!SpellManager.Spells.TryGetValue(name, out spell) || spell == null) return false;
            var data = spell.InternalInfo;
            if (data.Reagent == null || data.ReagentCount == null
                || data.Reagent.Length != 8 || data.ReagentCount.Length != 8) return false;
            var needed = new Dictionary<uint, long>();
            for (int index = 0; index < data.Reagent.Length; index++)
            {
                int id = data.Reagent[index];
                uint count = data.ReagentCount[index];
                if (id <= 0)
                {
                    if (count != 0) return false;
                    continue;
                }
                if (count == 0) return false;
                long prior;
                needed.TryGetValue((uint)id, out prior);
                needed[(uint)id] = prior + count;
            }
            // Missing/zeroed metadata is not proof that a Greater buff is free.
            // Sum repeated reagent entries before comparing actual carried stock.
            return needed.Count > 0 && needed.All(item =>
                StyxWoW.Me.GetCarriedItemCount(item.Key) >= item.Value);
        }

        private static SupportAction FindBlessingAction() => FindSupportAction(true, SelectBlessing);

        [ThreadStatic] private static SupportAction _readinessSelection;
        [ThreadStatic] private static bool _recordReadiness;

        // These receipts rank candidates within ONE selection; they never grant
        // cast permission. The shared spell dispatcher still performs current
        // readiness at native submission. Coverage/roster/assignment policies are
        // re-evaluated on every callback, without reissuing all their Lua probes.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SupportAction,
            List<Tuple<WoWPlayer, ulong, string, bool>>> SupportReadiness = new();

        private static bool SupportSpellAvailable(string spell, WoWPlayer player)
        {
            var action = _readinessSelection;
            if (action == null) return SpellManager.CanCast(spell, player);
            var probes = SupportReadiness.GetOrCreateValue(action);
            var prior = probes.FirstOrDefault(p => ReferenceEquals(p.Item1, player) && p.Item2 == player.Guid && p.Item3 == spell);
            if (prior != null) return prior.Item4;
            if (!_recordReadiness || !action.HasCurrentParticipants) return false;
            bool available = SpellManager.CanCast(spell, player);
            if (!action.HasCurrentParticipants) return false;
            probes.Add(Tuple.Create(player, player.Guid, spell, available));
            return available;
        }

        private static string EvaluateSupportSelection(SupportAction action, bool record)
        {
            var previous = _readinessSelection;
            bool wasRecording = _recordReadiness;
            _readinessSelection = action; _recordReadiness = record;
            try { return action.Revalidate(action.Target); }
            finally { _readinessSelection = previous; _recordReadiness = wasRecording; }
        }

        private static SupportAction FindSupportAction(bool includeGroup, Func<WoWPlayer, string> choose)
        {
            var caster = StyxWoW.Me;
            ulong casterGuid = caster?.Guid ?? 0;
            if (casterGuid == 0 || !CanMaintainSupport()) return null;
            foreach (var player in SupportRecipients(includeGroup))
            {
                ulong targetGuid = player.Guid;
                if (targetGuid == 0) continue;
                var action = new SupportAction
                {
                    Caster = caster, CasterGuid = casterGuid, Target = player,
                    TargetGuid = targetGuid, Revalidate = choose
                };
                if (!action.HasCurrentParticipants) return null;
                action.Spell = EvaluateSupportSelection(action, true);
                // Assignment/availability observations may change participants.
                if (!action.HasCurrentParticipants) return null;
                if (action.Spell != null) return action;
            }
            return null;
        }

        private static Composite CreateSupportBehavior(Func<SupportAction> choose, params string[] spellNames) =>
            CreateSupportBehavior(true, choose, spellNames);

        private static Composite CreateSupportBehavior(bool aura, Func<SupportAction> choose, params string[] spellNames)
        {
            return new Throttle(2, new PrioritySelector(_ => choose(),
                spellNames.Select(name => aura
                    // The revalidated selector owns the complete supported-family
                    // coverage check; Buff still owns pending TryCast acknowledgement.
                    ? Spell.Buff(name, false,
                        context => ValidSupportAction(context, name) ? ((SupportAction)context).Target : null,
                        context => ValidSupportAction(context, name), new string[0])
                    : Spell.Cast(name,
                        context => ValidSupportAction(context, name) ? ((SupportAction)context).Target : null,
                        context => ValidSupportAction(context, name))).ToArray()));
        }

        private static bool ValidSupportAction(object context, string spell)
        {
            var action = context as SupportAction;
            return action != null && action.Spell == spell && action.HasCurrentParticipants
                && CanMaintainSupport() && IsSupportRecipient(action.Target)
                && EvaluateSupportSelection(action, false) == spell && action.HasCurrentParticipants;
        }

        private static string SelectAura(WoWPlayer player)
        {
            if (!CanMaintainSupport() || player == null || !player.IsMe || player.Guid == 0) return null;
            var paladinSettings = SingularSettings.Instance.Paladin;
            var setting = paladinSettings.Aura;
            string[] order;
            bool assignmentControlled = false;
            if (setting != PaladinAura.Auto)
                order = new[] { setting == PaladinAura.Resistance ? "Shadow Resistance Aura" : setting + " Aura" };
            else
            {
                if (paladinSettings.UsePallyPowerAssignments)
                {
                    PallyPowerAssignment assignment = ReadPallyPowerAssignment(player);
                    if (assignment.Status == PallyPowerReadStatus.Uncertain)
                        return null;
                    if (assignment.Status == PallyPowerReadStatus.Verified)
                    {
                        assignmentControlled = true;
                        if (string.IsNullOrEmpty(assignment.Aura))
                            return null;
                        order = new[] { assignment.Aura };
                    }
                    else
                        order = null;
                }
                else
                    order = null;

                if (!assignmentControlled)
                {
                    if (TalentManager.CurrentSpec == TalentSpec.HolyPaladin)
                        order = new[] { "Concentration Aura", "Devotion Aura", "Retribution Aura" };
                    else if (TalentManager.CurrentSpec == TalentSpec.ProtectionPaladin && (StyxWoW.Me.IsInParty || StyxWoW.Me.IsInRaid))
                        order = new[] { "Devotion Aura", "Retribution Aura", "Concentration Aura" };
                    else
                        order = new[] { "Retribution Aura", "Devotion Aura", "Concentration Aura" };
                }
            }
            var auras = SupportCoverageAuras(player);
            if (setting == PaladinAura.Auto && !assignmentControlled)
            {
                // Preserve a useful contribution even when a preferred aura briefly
                // disappears. For known duplicate casters, one stable GUID ordering
                // keeps every Paladin from switching away at the same time.
                foreach (string name in order)
                {
                    var coverage = auras.Where(a => SupportedAuraName(a) == name).ToArray();
                    if (coverage.Any(a => a.CreatorGuid == player.Guid)
                        && !coverage.Any(a => a.CreatorGuid != 0 && a.CreatorGuid < player.Guid))
                        return null;
                }
            }
            foreach (string name in order)
            {
                var coverage = auras.Where(a => SupportedAuraName(a) == name).ToArray();
                bool external = coverage.Any(a => a.CreatorGuid != 0 && a.CreatorGuid != player.Guid);
                if (coverage.Any(a => a.CreatorGuid == player.Guid) && !external) return null;
                if (coverage.Length != 0) continue;
                if (SpellManager.HasSpell(name) && SupportSpellAvailable(name, player)) return name;
            }
            return null;
        }

        [Class(WoWClass.Paladin)]
        [Spec(TalentSpec.RetributionPaladin)]
        [Spec(TalentSpec.HolyPaladin)]
        [Spec(TalentSpec.ProtectionPaladin)]
        [Spec(TalentSpec.Lowbie)]
        [Behavior(BehaviorType.CombatBuffs)]
        [Context(WoWContext.All)]
        public static Composite CreatePaladinCombatAuras() => CreatePaladinAuraBehavior();

        internal static Composite CreatePaladinAuraBehavior() => CreateSupportBehavior(
            () => FindSupportAction(false, SelectAura), "Devotion Aura", "Retribution Aura", "Concentration Aura",
            "Shadow Resistance Aura", "Frost Resistance Aura", "Fire Resistance Aura", "Crusader Aura");

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
            WoWAura[] harmful;
            try
            {
                // A dispel needs complete metadata for its harmful removal mask,
                // not for unrelated helpful/server-only auras. Do not turn a
                // missing harmful spell into permission to remove another debuff.
                harmful = player.GetRawAuras().Where(a => a != null && a.IsActive && a.IsHarmful).ToArray();
                if (harmful.Any(a => a.Spell == null))
                    throw new Styx.Helpers.ObservationUnavailableException("paladin-dispel", "Harmful aura removal coverage is unavailable.");
            }
            catch (Styx.Helpers.ObservationUnavailableException error)
            {
                RecoveryActions.ReportDeferral(error, "Paladin dispel selection");
                return null;
            }
            bool SafeFor(params WoWDispelType[] types) =>
                harmful.Any(a => types.Contains(a.Spell.DispelType))
                && !harmful.Any(a => types.Contains(a.Spell.DispelType) && ManualDispelEffects.Contains(a.SpellId));
            // Purify cannot incidentally dispel unsafe Magic while curing Disease/Poison.
            // Cleanse can, so its complete removal mask must be safe, not just one debuff.
            bool purify = SafeFor(WoWDispelType.Disease, WoWDispelType.Poison)
                && SpellManager.HasSpell("Purify") && SupportSpellAvailable("Purify", player);
            bool cleanse = SafeFor(WoWDispelType.Disease, WoWDispelType.Poison, WoWDispelType.Magic)
                && SpellManager.HasSpell("Cleanse") && SupportSpellAvailable("Cleanse", player);
            if (cleanse && harmful.Any(a => a.Spell.DispelType == WoWDispelType.Magic)) return "Cleanse";
            return purify ? "Purify" : cleanse ? "Cleanse" : null;
        }

        public static Composite CreatePaladinDispelBehavior() => CreateSupportBehavior(false,
            () => FindSupportAction(SingularSettings.Instance.Paladin.DispelParty, SelectDispel), "Purify", "Cleanse");
    }
}
