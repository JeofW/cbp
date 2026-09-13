// Only world observation, native spell dispatch and unrelated movement/target
// helpers are controlled here. Retribution, attributes, Throttle and TreeSharp
// are linked production source, not a rewritten rotation or behavior tree.
using TreeSharp;

internal static class Fixture
{
    internal static readonly HashSet<string> Known = new();
    internal static readonly HashSet<string> Ready = new();
    internal static readonly List<string> Trace = new();
    internal static readonly List<Exception> Exceptions = new();
    internal static bool AreaSafe = true;
    internal static string? Selected;
    internal static void Reset(int count = 1, int level = 80)
    {
        Known.Clear(); Ready.Clear(); Trace.Clear(); Exceptions.Clear();
        AreaSafe = true; Selected = null;
        Styx.StyxWoW.Me = new Styx.Player { Level = level, CurrentTarget = new Styx.UnitState() };
        Singular.Helpers.Unit.NearbyUnfriendlyUnits.Clear();
        for (int i = 0; i < count; i++) Singular.Helpers.Unit.NearbyUnfriendlyUnits.Add(new Styx.UnitState());
    }
    internal static Composite Nothing() => new TreeSharp.Action(_ => RunStatus.Failure);
    internal static Composite Attempt(string spell, Func<object, Styx.UnitState?> select, Func<object, bool>? requires) =>
        new TreeSharp.Action(context =>
        {
            var target = select(context);
            string? reason = target == null ? "no-target" : requires != null && !requires(context) ? "requirements"
                : !Known.Contains(spell) ? "unknown" : !Ready.Contains(spell) ? "unavailable"
                : (spell is "Crusader Strike" or "Divine Storm") && target.Distance > 8 ? "range"
                : spell == "Divine Storm" && !AreaSafe ? "area-safety" : null;
            Trace.Add(spell + ":" + (reason ?? "selected"));
            if (reason != null) return RunStatus.Failure;
            Selected = spell;
            return RunStatus.Success;
        });
}
namespace Styx
{
    public class Aura { public string Name { get; set; } = ""; public ulong CreatorGuid { get; set; } }
    public class UnitState
    {
        public uint Entry { get; set; } = 1;
        public double HealthPercent { get; set; } = 100;
        public float Distance { get; set; } = 3;
        public ulong Guid { get; set; } = 1;
        public bool UndeadOrDemon { get; set; }
        public bool Boss { get; set; }
        public bool IsWithinMeleeRange => Distance <= 5;
        public readonly Dictionary<string, Aura> Auras = new();
        public bool HasAura(string name) => Auras.ContainsKey(name);
        public bool IsUndeadOrDemon() => UndeadOrDemon;
        public bool IsBoss() => Boss;
    }
    public sealed class Player : UnitState
    {
        public UnitState? CurrentTarget { get; set; }
        public int Level { get; set; }
        public double ManaPercent { get; set; } = 100;
        public bool IsAutoAttacking { get; set; } = true;
        public bool IsMoving { get; set; }
        public Dictionary<string, Aura> ActiveAuras => Auras;
        public bool HasAuraWithMechanic(params Logic.Combat.WoWSpellMechanic[] _) => false;
    }
    public static class StyxWoW { public static Player Me { get; set; } = new(); }
}
namespace Styx.Helpers
{
    public static class Logging { public static void WriteException(Exception error) => Fixture.Exceptions.Add(error); }
}
namespace Styx.Combat.CombatRoutine { public enum WoWClass { Paladin } }
namespace Styx.Logic.Combat
{
    public enum WoWSpellMechanic { Dazed, Disoriented, Frozen, Incapacitated, Rooted, Slowed, Snared }
    public static class SpellManager { public static bool HasSpell(string name) => Fixture.Known.Contains(name); }
}
namespace Singular.Managers
{
    public enum TalentSpec { RetributionPaladin }
    public static class HealerManager { public static bool NeedHealTargeting { get; set; } }
}
namespace Singular.Dynamics
{
    public enum BehaviorType { Heal, Rest, Pull, Combat }
    public enum WoWContext { All, Normal, Battlegrounds, Instances }
}
namespace Singular.Settings
{
    public sealed class PaladinSettings
    {
        public int LayOnHandsHealth => 15;
        public int HolyLightHealth => 30;
        public int FlashOfLightHealth => 50;
        public int DivineProtectionHealthRet => 20;
        public int ConsecrationCount => 3;
        public int DivinePleaMana => 30;
    }
    public sealed class SingularSettings
    {
        public static SingularSettings Instance { get; } = new();
        public PaladinSettings Paladin { get; } = new();
    }
}
namespace Singular.Helpers
{
    public static class Unit { public static List<Styx.UnitState> NearbyUnfriendlyUnits { get; } = new(); }
    public static class Safers { public static Composite EnsureTarget() => Fixture.Nothing(); }
    public static class Common
    {
        public static Composite CreateAutoAttack(bool _) => Fixture.Nothing();
        public static Composite CreateInterruptSpellCast(Func<object, Styx.UnitState?> _) => Fixture.Nothing();
    }
    public static class Movement
    {
        public static Composite CreateMoveToLosBehavior() => Fixture.Nothing();
        public static Composite CreateFaceTargetBehavior() => Fixture.Nothing();
        public static Composite CreateMoveToMeleeBehavior(bool _) => new TreeSharp.Action(context =>
        { Fixture.Selected = "movement"; return RunStatus.Success; });
    }
    public static class Rest { public static Composite CreateDefaultRestBehaviour() => Fixture.Nothing(); }
    public static class Spell
    {
        public const float MeleeRange = 5;
        public static Composite WaitForCast(bool _ = true, bool __ = true) => Fixture.Nothing();
        public static Composite Resurrect(string _) => Fixture.Nothing();
        public static Composite Cast(string name, Func<object, bool>? requires = null) =>
            Fixture.Attempt(name, _ => Styx.StyxWoW.Me.CurrentTarget, requires);
        public static Composite Cast(string name, Func<object, Styx.UnitState?> select, Func<object, bool> requires) =>
            Fixture.Attempt(name, select, requires);
        public static Composite BuffSelf(string name, Func<object, bool>? requires = null) =>
            Fixture.Attempt(name, _ => Styx.StyxWoW.Me, requires);
        public static Composite Heal(string name, Func<object, Styx.UnitState?> select, Func<object, bool> requires) =>
            Fixture.Attempt(name, select, requires);
    }
}

// The support suite links the actual Common owner; rotation-only tests isolate it.
namespace Singular.ClassSpecific.Paladin
{
    public static class Common
    {
        public static Composite CreatePaladinDispelBehavior() => Fixture.Nothing();
    }
}
