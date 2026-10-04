// Controlled external observations and dispatch only. Common, Retribution,
// Throttle, attributes and TreeSharp below are linked, unchanged production code.
using Styx.Combat.CombatRoutine;
using Styx.Logic.Combat;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

internal static class Fixture
{
    internal static readonly HashSet<string> Known = new(StringComparer.Ordinal);
    internal static readonly HashSet<string> Unavailable = new(StringComparer.Ordinal);
    internal static readonly List<(string Spell, ulong Target)> Attempts = new();
    internal static readonly List<(string Spell, bool Aura)> RecoveryRoutes = new();
    internal static List<string>? RosterObservation;
    internal static readonly List<Exception> Errors = new();
    internal static readonly Dictionary<string, WoWSpell> Metadata = new();
    internal static readonly List<string> LuaQueries = new();
    internal static Func<string, List<string>>? LuaResult;
    internal static int DefaultRestCalls, NearbyReads, SupportReadinessReads;
    internal static Exception? NearbyError;
    internal static RunStatus DefaultRestResult = RunStatus.Failure;
    internal static bool GlobalCooldown;
    internal static TimeSpan ConsecrationCooldown;
    internal static void Reset()
    {
        Known.Clear(); Unavailable.Clear(); Attempts.Clear(); Errors.Clear(); Metadata.Clear(); RecoveryRoutes.Clear(); RosterObservation=null;
        LuaQueries.Clear(); LuaResult = null; Styx.WoWInternals.ObjectManager.Wow=new(); Styx.WoWInternals.ObjectManager.Resolve=null;
        DefaultRestCalls = 0; DefaultRestResult = RunStatus.Failure;
        GlobalCooldown = false;
        ConsecrationCooldown = TimeSpan.Zero;
        Singular.Managers.TankManager.Instance.FirstUnit = null;
        Singular.Managers.TankManager.Instance.NeedToTaunt.Clear();
        Singular.Managers.HealerManager.Instance.FirstUnit = null;
        Singular.Managers.HealerManager.NeedHealTargeting = false;
        Singular.Helpers.Group.Tanks.Clear();
        Singular.Settings.SingularSettings.Instance.EnableTaunting = false;
        Styx.StyxWoW.Me = new LocalPlayer { Guid = 1, BaseAddress = 100, Class = WoWClass.Paladin, Name = "SelfPaladin" };
        Singular.Settings.SingularSettings.Instance.Paladin = new();
        Singular.Settings.SingularSettings.Instance.DeathKnight = new();
        Singular.Settings.SingularSettings.Instance.Rogue = new();
        Singular.Managers.TalentManager.Glyphs.Clear();
        Singular.Managers.TalentManager.CurrentSpec = Singular.Managers.TalentSpec.RetributionPaladin;
        NearbyError = null;
        Singular.Helpers.Unit.NearbyUnfriendlyUnits.Clear();
        NearbyReads = 0;
        SupportReadinessReads = 0;
    }
    internal static WoWPlayer Add(WoWClass kind = WoWClass.Warrior, bool raid = false)
    {
        var me = Styx.StyxWoW.Me;
        var p = new WoWPlayer { Guid = (ulong)(me.PartyMembers.Count + me.RaidMembers.Count + 10),
            BaseAddress = (uint)(1000 + me.PartyMembers.Count + me.RaidMembers.Count), Class = kind,
            Name = "Member" + (me.PartyMembers.Count + me.RaidMembers.Count + 10),
            MaxMana = kind is WoWClass.Warrior or WoWClass.Rogue or WoWClass.DeathKnight ? 0 : 100 };
        if (raid) { me.IsInRaid = true; me.RaidMembers.Add(p); }
        else { me.IsInParty = true; me.PartyMembers.Add(p); }
        return p;
    }
    internal static void Aura(WoWUnit p, string name, ulong owner, int id = 1, WoWDispelType dispel = WoWDispelType.None)
        => p.ObservedAuras.Add(new WoWAura { Name = name, CreatorGuid = owner, SpellId = id == 1 ? name switch { "Blessing of Kings" => 20217, "Blessing of Might" => 19740, "Blessing of Wisdom" => 19742, "Blessing of Sanctuary" => 20911, "Greater Blessing of Kings" => 25898, "Greater Blessing of Might" => 25782, "Greater Blessing of Wisdom" => 25894, "Greater Blessing of Sanctuary" => 25899, "Battle Shout" => 2048, "Moonkin Form" => 24858, "Tree of Life" => 5420, "Devotion Aura" => 465, "Retribution Aura" => 7294, "Concentration Aura" => 19746, "Shadow Resistance Aura" => 19876, "Frost Resistance Aura" => 19888, "Fire Resistance Aura" => 19891, "Crusader Aura" => 32223, "Seal of Command" => 20375, "Seal of Corruption" => 53736, "Seal of Justice" => 20164, "Seal of Light" => 20165, "Seal of Righteousness" => 20154, "Seal of Vengeance" => 31801, "Seal of Wisdom" => 20166, "Judgement of Wisdom" => 20186, "Judgement of Light" => 20185, "Horde Flag" => 14267, "Alliance Flag" => 14268, "Divine Shield" => 642, "Divine Protection" => 498, _ => 1 } : id,
            IsHarmful = dispel != WoWDispelType.None, Spell = new WoWSpell { DispelType = dispel } });
    internal static Composite Nothing() => new TreeSharp.Action(_ => RunStatus.Failure);
    internal static Composite Submit(string name, Func<object, WoWUnit?> select, Func<object, bool>? requires = null, bool buff = false, bool checkAura = true)
        => new TreeSharp.Action(context =>
        {
            var target = select(context);
            if (target == null || (requires != null && !requires(context)) || !SpellManager.CanCast(name, target))
                return RunStatus.Failure;
            if (buff && checkAura && target.HasMyAura(name)) return RunStatus.Failure;
            RecoveryRoutes.Add((name,buff));
            Attempts.Add((name, target.Guid));
            return RunStatus.Success;
        });
    internal static RunStatus Tick(Composite root)
    {
        root.Start(null!);
        RunStatus status;
        int ticks = 0;
        do
        {
            status = root.Tick(null!);
            if (++ticks > 200) throw new InvalidOperationException("Unexpected blocking/never-ending support behavior");
        } while (status == RunStatus.Running);
        root.Stop(null!);
        if (Errors.Count != 0) throw new InvalidOperationException("Swallowed exception: " + Errors[0]);
        return status;
    }
}
namespace Styx.Combat.CombatRoutine
{
    public enum WoWClass { None, Warrior, Paladin, Hunter, Rogue, Priest, DeathKnight, Shaman, Mage, Warlock, Druid }
}
namespace Styx.Logic.Combat
{
    public enum WoWSpellMechanic { Dazed, Disoriented, Frozen, Incapacitated, Rooted, Slowed, Snared }
    public sealed class SpellRecord { public int[]? Reagent = new int[8]; public uint[]? ReagentCount = new uint[8]; }
    public class WoWSpell { public WoWDispelType DispelType { get; set; } public SpellRecord InternalInfo { get; } = new(); public float MaxRange { get; set; } = 30; }
    public class WoWAura
    {
        public string Name { get; set; } = "";
        public ulong CreatorGuid { get; set; }
        public int SpellId { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsHarmful { get; set; }
        public bool IsPassive { get; set; }
        public TimeSpan TimeLeft { get; set; } = TimeSpan.FromMinutes(10);
        public WoWSpell? Spell { get; set; } = new();
    }
    public static class SpellManager
    {
        private static bool CountReadiness() { Fixture.SupportReadinessReads++; return true; }
        public static Dictionary<string, WoWSpell> Spells => Fixture.Metadata;
        public static bool HasSpell(string name) => Fixture.Known.Contains(name);
        public static bool CanCast(string name) => CanCast(name, Styx.StyxWoW.Me.CurrentTarget ?? Styx.StyxWoW.Me);
        public static bool CanCast(string name, WoWUnit target, bool checkRange = true, bool checkMovement = false)
            => CountReadiness() && HasSpell(name) && !Fixture.Unavailable.Contains(name) && target.IsValid && target.IsAlive &&
                (target.IsMe || target.Distance < 40 && target.InLineOfSpellSight);
    }
}
namespace Styx.WoWInternals.WoWObjects
{
    public class MapState { public bool IsBattleground { get; set; } public bool IsInstance { get; set; } }
    public class WoWUnit
    {
        public MapState CurrentMap { get; } = new();
        public bool IsInInstance => CurrentMap.IsInstance;
        public ulong Guid { get; set; }
        public ulong DescriptorGuid => Guid;
        public uint MapId { get; set; } = 530;
        public uint BaseAddress { get; set; } = 100;
        public uint Entry { get; set; } = 1;
        public string Name { get; set; } = "";
        public bool IsValid { get; set; } = true;
        public bool IsAlive { get; set; } = true;
        public bool IsFriendly { get; set; } = true;
        public bool IsGhost { get; set; }
        public bool Dead => !IsAlive;
        public bool IsMe => ReferenceEquals(this, Styx.StyxWoW.Me);
        public bool Combat { get; set; }
        public bool Mounted { get; set; }
        public bool IsOnTransport { get; set; }
        public bool IsCasting { get; set; }
        public uint ChanneledCastingSpellId { get; set; }
        public ulong CurrentTargetGuid => CurrentTarget?.Guid ?? 0;
        public bool IsInCombat => Combat;
        public bool IsChanneling { get; set; }
        public bool IsMoving { get; set; }
        public bool IsFlying { get; set; }
        public bool MeIsBehind { get; set; } = true;
        public bool IsTargetingMeOrPet { get; set; }
        public bool StunnedObservation { get; set; }
        public bool IsPlayer { get; set; }
        public bool Elite { get; set; }
        public bool Fleeing { get; set; }
        public bool IsAutoAttacking { get; set; }
        public float Distance { get; set; } = 5;
        public float DistanceSqr => Distance * Distance;
        public float Distance2DSqr => DistanceSqr;
        public float Z { get; set; }
        public Styx.WoWPowerType PowerType { get; set; }
        public ThreatObservation ThreatInfo { get; } = new();
        public Styx.WoWPoint Location { get; set; }
        public bool InLineOfSpellSight { get; set; } = true;
        public bool IsWithinMeleeRange => Distance <= 5;
        public int MaxMana { get; set; } = 100;
        public double ManaPercent { get; set; } = 100;
        public double HealthPercent { get; set; } = 100;
        public int Level { get; set; } = 80;
        public WoWClass Class { get; set; }
        public WoWUnit? CurrentTarget { get; set; }
        public bool GotTarget => CurrentTarget != null;
        public bool IsInMyPartyOrRaid => IsMe || Styx.StyxWoW.Me.PartyMembers.Any(p => p.Guid == Guid)
            || Styx.StyxWoW.Me.RaidMembers.Any(p => p.Guid == Guid);
        public bool IsSafelyFacing(WoWUnit target) => true;
        public List<WoWAura> ObservedAuras { get; } = new();
        public Dictionary<string, WoWAura> Auras => ObservedAuras.GroupBy(a => a.Name).ToDictionary(g => g.Key, g => g.Last());
        public Dictionary<string, WoWAura> ActiveAuras => Auras;
        public Dictionary<string, WoWAura> Debuffs => Auras.Where(a => a.Value.IsHarmful).ToDictionary(a => a.Key, a => a.Value);
        public bool RawUnknown, MetadataUnknown;
        public IEnumerable<WoWAura> GetRawAuras() => RawUnknown ? throw new Styx.Helpers.ObservationUnavailableException("auras", "raw observation unavailable") : ObservedAuras;
        public IEnumerable<WoWAura> GetAllAuras() => MetadataUnknown ? throw new Styx.Helpers.ObservationUnavailableException("auras", "metadata unavailable") : GetRawAuras();
        public bool HasAura(string name) => ObservedAuras.Any(a => a.Name == name && a.IsActive);
        public bool HasMyAura(string name) => ObservedAuras.Any(a => a.Name == name && a.IsActive && a.CreatorGuid == Styx.StyxWoW.Me.Guid) ? true : MetadataUnknown ? throw new InvalidOperationException("metadata unavailable") : false;
        public TimeSpan GetAuraTimeLeft(string name, bool mine) => ObservedAuras
            .FirstOrDefault(a => a.Name == name && a.IsActive && (!mine || a.CreatorGuid == Styx.StyxWoW.Me.Guid))?.TimeLeft ?? TimeSpan.Zero;
        public bool IsStunned() => StunnedObservation;
        public bool HasAuraWithMechanic(params WoWSpellMechanic[] _) => false;
        public bool HasHarmfulAuraWithMechanic(params WoWSpellMechanic[] _) => false;
        public bool IsImmune(Styx.WoWSpellSchool school) => false;
        public bool IsCrowdControlled() => false;
        public bool IsBoss() => false;
        public bool IsUndeadOrDemon() => false;
    }
    public class WoWPlayer : WoWUnit
    {
        public bool IsInParty { get; set; }
        public bool IsInRaid { get; set; }
        public List<WoWPlayer> PartyMembers { get; } = new();
        public List<WoWPlayer> RaidMembers { get; } = new();
    }
    public sealed class LocalPlayer : WoWPlayer
    {
        public int ComboPoints { get; set; }
        public bool IsStealthed { get; set; }
        public WoWUnit? Pet { get; set; }
        public bool GotAlivePet => Pet != null && Pet.IsValid && Pet.IsAlive;
        public int BloodRuneCount { get; set; }
        public int FrostRuneCount { get; set; }
        public int UnholyRuneCount { get; set; }
        public int DeathRuneCount { get; set; }
        public int CurrentRunicPower { get; set; }
        public Dictionary<uint, int> ItemCounts { get; } = new();
        public int GetCarriedItemCount(uint id) => ItemCounts.TryGetValue(id, out int count) ? count : 0;
    }
    public sealed class ThreatObservation { public double RawPercent { get; set; } }
}
namespace Styx
{
    public static class StyxWoW { public static LocalPlayer Me { get; set; } = new(); }
    public readonly record struct WoWPoint(float X, float Y, float Z);
    public enum WoWSpellSchool { Frost }
}
namespace Styx.Logic { }
namespace Styx.Logic.Pathing
{
    public static class Navigator { public static PlayerMover PlayerMover { get; } = new(); }
    public sealed class PlayerMover { public void MoveStop() => Styx.StyxWoW.Me.IsMoving = false; }
}
namespace CommonBehaviors.Actions
{
    public sealed class ActionAlwaysSucceed : TreeSharp.Action
    {
        public ActionAlwaysSucceed() : base(_ => RunStatus.Success) { }
    }
}
namespace Styx.WoWInternals
{
    public static class Lua
    {
        public static List<string> GetObservedReturnValues(string code) => GetReturnValues(code);
        public static List<string> GetReturnValues(string code)
        {
            Fixture.LuaQueries.Add(code);
            if(code.Contains("group-v1"))
            {
                if(Fixture.RosterObservation!=null)return Fixture.RosterObservation;
                var me=Styx.StyxWoW.Me;var roster=me.IsInRaid?me.RaidMembers:me.PartyMembers;
                var members=me.IsInRaid?new[]{me}.Concat(roster.Where(p=>p.Guid!=me.Guid)).ToArray():roster.ToArray();
                return new List<string>{"group-v1",me.IsInRaid?members.Length.ToString():"0",me.IsInParty?me.PartyMembers.Count.ToString():"0","0x"+me.Guid.ToString("X")}.Concat(members.Select(p=>"0x"+p.Guid.ToString("X"))).ToList();
            }
            return Fixture.LuaResult?.Invoke(code) ?? new List<string>();
        }
        public static string Escape(string value) => (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
namespace Styx.Helpers
{
    public static class Logging { public static void WriteException(Exception ex) => Fixture.Errors.Add(ex); }
    // Time is an external controlled observation. Test scenarios do not learn
    // Death Strike, so no timer-dependent rotation result is claimed here.
    public sealed class WaitTimer
    {
        public WaitTimer(TimeSpan duration) { }
        public bool IsFinished => true;
        public void Reset() { }
    }
}
namespace Singular.Managers
{
    public enum TalentSpec { RetributionPaladin, HolyPaladin, ProtectionPaladin, Lowbie, BloodDeathKnight, FrostDeathKnight, UnholyDeathKnight, AssasinationRogue }
    public static class TalentManager
    {
        public static TalentSpec CurrentSpec { get; set; }
        public static HashSet<string> Glyphs { get; } = new(StringComparer.Ordinal);
        public static bool HasGlyph(string name) => Glyphs.Contains(name);
        public static int GetCount(int tab, int talent) => 0;
    }
    public sealed class HealerManager
    {
        public static HealerManager Instance { get; } = new();
        public static bool NeedHealTargeting { get; set; }
        public WoWUnit? FirstUnit { get; set; }
    }
    public sealed class TankManager
    {
        public static TankManager Instance { get; } = new();
        public WoWUnit? FirstUnit { get; set; }
        public List<WoWUnit> NeedToTaunt { get; } = new();
    }
}
namespace Singular.Dynamics
{
    public enum BehaviorType { Heal, Rest, Pull, Combat, PreCombatBuffs, CombatBuffs, PullBuffs }
    public enum WoWContext { All, Normal, Battlegrounds, Instances }
}
namespace Singular.Settings
{
    internal sealed class RogueSettings
    {
        public bool UseStealthOnPull { get; set; }
        public bool UseTricksOfTheTrade { get; set; }
    }
    internal sealed class DeathKnightSettings
    {
        public bool UseDeathAndDecay { get; set; }
        public int DeathAndDecayCount { get; set; } = 2;
        public bool UseAntiMagicShell { get; set; }
        public bool UseIceboundFortitude { get; set; }
        public int IceboundFortitudePercent { get; set; } = 30;
        public int DeathStrikeEmergencyPercent { get; set; } = 30;
        public bool UseLichborne { get; set; }
        public int LichbornePercent { get; set; } = 60;
        public bool LichborneExclusive { get; set; }
        public bool UseDancingRuneWeapon { get; set; }
        public bool UseVampiricBlood { get; set; }
        public int VampiricBloodPercent { get; set; } = 60;
        public bool VampiricBloodExclusive { get; set; }
        public bool UsePetSacrifice { get; set; }
        public int PetSacrificePercent { get; set; } = 60;
        public int PetSacrificeSummonPercent { get; set; } = 60;
        public bool PetSacrificeExclusive { get; set; }
        public bool IceboundFortitudeExclusive { get; set; }
        public int EmpowerRuneWeaponPercent { get; set; } = 30;
        public bool UseArmyOfTheDead { get; set; }
        public int ArmyOfTheDeadPercent { get; set; } = 20;
        public bool UseRaiseDead { get; set; }
        public bool UseEmpowerRuneWeapon { get; set; }
        public bool UseSummonGargoyle { get; set; }
    }
    internal class PaladinSettings
    {
        public Singular.ClassSpecific.Paladin.PaladinSeal Seal { get; set; }
        public Singular.ClassSpecific.Paladin.PaladinAura Aura { get; set; }
        public Singular.ClassSpecific.Paladin.PaladinBlessings Blessings { get; set; }
        public bool UseGreaterBlessings { get; set; }
        public bool UsePallyPowerAssignments { get; set; }
        public bool DispelDebuffs { get; set; } = true;
        public bool DispelParty { get; set; } = true;
        public int LayOnHandsHealth => 15;
        public int HolyLightHealth { get; set; } = 30;
        public int HolyShockHealth { get; set; } = 90;
        public int FlashOfLightHealth => 50;
        public int DivineProtectionHealthRet => 20;
        public int DivineProtectionHealthProt => 20;
        public int ProtConsecrationCount => 3;
        public bool AvengersPullOnly { get; set; }
        public int ConsecrationCount => 3;
        public int DivinePleaMana => 30;
        public int RetributionHealHealth => 30;
        // Existing support cases retain the feature's disabled default.
        public bool UseSoloSealOfLight => false;
        public int SoloSealOfLightHealth => 50;
        public int SoloSealOfLightRecoveryHealth => 75;
        public int SoloSealOfLightMinimumMana => 30;
    }
    internal class SingularSettings
    {
        public static SingularSettings Instance { get; } = new();
        public PaladinSettings Paladin { get; set; } = new();
        public DeathKnightSettings DeathKnight { get; set; } = new();
        public RogueSettings Rogue { get; set; } = new();
        public bool EnableTaunting { get; set; }
    }
}
namespace Singular.Helpers
{
    public static class Item { public static bool RangedIsType(Styx.WoWItemWeaponClass kind) => false; }
    public static class Unit
    {
        private static readonly List<WoWUnit> Nearby = new();
        public static List<WoWUnit> NearbyUnfriendlyUnits { get { Fixture.NearbyReads++; if (Fixture.NearbyError != null) throw Fixture.NearbyError; return Nearby; } }
        public static IEnumerable<WoWUnit> UnfriendlyUnitsNearTarget(float range) => NearbyUnfriendlyUnits;
        public static IEnumerable<WoWUnit> UnfriendlyUnitsWithin(float range) => NearbyUnfriendlyUnits.Where(unit => unit.Distance <= range);
        public static bool IsAreaEffectSafe(string name, WoWUnit target) => true;
        public static IEnumerable<WoWPlayer> NearbyFriendlyPlayers => Styx.StyxWoW.Me.PartyMembers.Concat(Styx.StyxWoW.Me.RaidMembers);
    }
    public static class Group { public static List<WoWPlayer> Tanks { get; } = new(); }
    public static class Safers { public static Composite EnsureTarget() => Fixture.Nothing(); }
    public static class Common
    {
        public static Composite CreateAutoAttack(bool _) => Fixture.Nothing();
        public static Composite CreateInterruptSpellCast(Func<object, WoWUnit?> _) => Fixture.Nothing();
    }
    public static class Movement
    {
        public static Composite CreateMoveToLosBehavior() => Fixture.Nothing();
        public static Composite CreateMoveToLosBehavior(Func<object, WoWUnit?> select) => Fixture.Nothing();
        public static Composite CreateFaceTargetBehavior() => Fixture.Nothing();
        public static Composite CreateMoveToMeleeBehavior(bool _) => Fixture.Nothing();
        public static Composite CreateMoveBehindTargetBehavior() => Fixture.Nothing();
        public static Composite CreateMoveToTargetBehavior(bool _, float range) => Fixture.Nothing();
        public static Composite CreateMoveToTargetBehavior(bool _, float range, Func<object, WoWUnit?> select) => Fixture.Nothing();
    }
    public static class Rest
    {
        public static Composite CreateDefaultRestBehaviour() => new TreeSharp.Action(_ =>
        { Fixture.DefaultRestCalls++; return Fixture.DefaultRestResult; });
    }
    public static class Spell
    {
        public const float MeleeRange = 5;
        public static Composite WaitForCast(bool _ = true, bool __ = true) => Fixture.Nothing();
        public static Composite WaitForCastOrChannel() => Fixture.Nothing();
        public static bool IsGlobalCooldown() => Fixture.GlobalCooldown;
        public static TimeSpan GetSpellCooldown(string name) => name == "Consecration"
            ? Fixture.ConsecrationCooldown : throw new InvalidOperationException("Unconfigured fixture cooldown: " + name);
        public static Composite Resurrect(string _) => Fixture.Nothing();
        public static Composite Cast(string name, Func<object, bool>? requires = null) => Fixture.Submit(name, _ => Styx.StyxWoW.Me.CurrentTarget, requires);
        public static Composite Cast(string name, Func<object, WoWUnit?> select, Func<object, bool> requires) => Fixture.Submit(name, select, requires);
        public static Composite CastOnGround(string name, Func<object, Styx.WoWPoint> point, Func<object, bool> requires)
            => Fixture.Submit(name, _ => Styx.StyxWoW.Me.CurrentTarget, requires);
        public static Composite BuffSelf(string name, Func<object, bool>? requires = null) => Fixture.Submit(name, _ => Styx.StyxWoW.Me, requires, true);
        public static Composite Buff(string name, Func<object, bool>? requires = null) => Fixture.Submit(name, _ => Styx.StyxWoW.Me.CurrentTarget, requires, true);
        public static Composite Buff(string name, bool myBuff, params string[] names)
            => Buff(name, myBuff, _ => true, names);
        public static Composite Buff(string name, bool myBuff, Func<object, bool> requires, params string[] names)
            => Fixture.Submit(name, _ => Styx.StyxWoW.Me.CurrentTarget, context => requires(context)
                && Styx.StyxWoW.Me.CurrentTarget != null
                && (names.Length == 0 ? new[] { name } : names).All(aura => myBuff
                    ? !Styx.StyxWoW.Me.CurrentTarget.HasMyAura(aura) : !Styx.StyxWoW.Me.CurrentTarget.HasAura(aura)));
        public static Composite Buff(string name, Func<object, WoWUnit?> select, Func<object, bool> requires)
            => Fixture.Submit(name, select, context => requires(context)
                && select(context) is WoWUnit target && !target.HasAura(name));
        public static Composite Buff(string name, bool myBuff, Func<object, WoWUnit?> select, Func<object, bool> requires, params string[] names)
            => Fixture.Submit(name, select, context => requires(context)
                && select(context) is WoWUnit target && names.All(a => myBuff ? !target.HasMyAura(a) : !target.HasAura(a)), true, false);
        public static Composite Heal(string name, Func<object, WoWUnit?> select, Func<object, bool> requires) => Fixture.Submit(name, select, requires);
    }
}

// These unrelated Rogue helpers are external boundaries for the complete
// Assassination decision owner. No Blind/Tricks implementation is claimed here.
namespace Singular.ClassSpecific.Rogue
{
    public static class Common
    {
        public static Composite CreateRogueBlindOnAddBehavior() => Fixture.Nothing();
        public static WoWUnit? BestTricksTarget => null;
    }
}

namespace Styx.Logic.Common
{
 public static class Rest
 {
  public static bool TryObserveActivity(Styx.WoWInternals.WoWObjects.LocalPlayer player,out bool food,out bool drink)
  { food=player.ObservedAuras.Any(a=>a.Name=="Food"&&a.IsActive);drink=player.ObservedAuras.Any(a=>a.Name=="Drink"&&a.IsActive);return !player.RawUnknown; }
 }
}

namespace Styx.WoWInternals
{
 public sealed class GroupMemory {
  public GroupExecutor Executor=new();
  private sealed class Scope:IDisposable {public void Dispose(){}}
  public IDisposable TemporaryCacheState(bool value)=>new Scope();
  // These support fixtures use the actual Lua membership protocol. No raw
  // process memory is provided; raw-only admission must remain UNKNOWN here.
  public byte[] ReadBytes(uint address,int count)=>Array.Empty<byte>();
 }
 public sealed class GroupExecutor { public uint FrameCount=1; public GroupMemory Memory => ObjectManager.Wow; }
 public static class ObjectManager
 {
  public static GroupMemory Wow=new();
  public static GroupExecutor Executor => Wow.Executor;
  public static Func<ulong,WoWPlayer?>? Resolve;
  public static T? GetObjectByGuid<T>(ulong guid) where T:WoWPlayer => (Resolve!=null?Resolve(guid):Styx.StyxWoW.Me.PartyMembers.Concat(Styx.StyxWoW.Me.RaidMembers).FirstOrDefault(p=>p.Guid==guid)) as T;
 }
}
