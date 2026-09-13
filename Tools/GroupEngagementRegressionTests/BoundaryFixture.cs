// Unit and the engagement policy are real linked sources. Only world observations are controlled.
namespace Styx
{
 public static class StyxWoW { public static WoWInternals.WoWObjects.LocalPlayer Me { get; set; } = new(); }
}
namespace Styx.Logic
{
 public sealed class Bot { public string Name { get; set; } = "Combat Bot"; }
 public static class BotManager { public static Bot Current { get; set; } = new(); }
 public static class RaFHelper { public static Styx.WoWInternals.WoWObjects.WoWUnit? Leader { get; set; } }
 public static class Blacklist { public static bool Contains(ulong _) => false; }
}
namespace Styx.Helpers { public static class Marker { } }
namespace Styx.Logic.Pathing
{
 public readonly record struct WoWPoint(float X, float Y, float Z)
 { public float DistanceSqr(WoWPoint p) => (X-p.X)*(X-p.X)+(Y-p.Y)*(Y-p.Y)+(Z-p.Z)*(Z-p.Z); }
}
namespace Styx.Logic.Combat
{
 public enum WoWCreatureType { Humanoid, Undead, Demon }
 public enum WoWSpellMechanic { None, Banished, Charmed, Horrified, Incapacitated, Polymorphed, Sapped, Shackled, Asleep, Frozen, Invulnerable, Invulnerable2, Turned }
 public enum WoWApplyAuraType { None, ModResistancePct, ModDamagePercentDone, ModMechanicDamageTakenPercent }
 public sealed class SpellEffect { public WoWApplyAuraType AuraType; public int MiscValueA; public int BasePoints; }
 public sealed class WoWSpell { public static WoWSpell FromId(int id) => new() { Name = id == 53385 ? "Divine Storm" : "Crusader Strike" }; public string Name = ""; public WoWSpellMechanic Mechanic; public SpellEffect? GetSpellEffect(int _) => null; }
 public sealed class WoWAura { public string Name=""; public int StackCount; public ulong CreatorGuid; public TimeSpan TimeLeft; public WoWSpell Spell=new(); }
}
namespace Styx.WoWInternals
{
 public static class ObjectManager
 {
  public static readonly List<WoWObjects.WoWUnit> Objects = new();
  public static IEnumerable<T> GetObjectsOfType<T>(bool _ = false, bool __ = false) => Objects.OfType<T>();
 }
}
namespace Styx.WoWInternals.WoWObjects
{
 using Styx.Logic.Combat; using Styx.Logic.Pathing;
 public sealed class Map { public bool IsDungeon = true; public bool IsRaid; }
 public struct UnitThreatInfo { public uint ThreatValue; }
 public class WoWUnit
 {
  public ulong Guid { get; set; }
  public uint Entry { get; set; } = 1;
  public bool IsValid { get; set; } = true;
  public bool IsAlive { get; set; } = true;
  public bool Dead => !IsAlive;
  public bool IsFriendly { get; set; }
  public bool IsMe => ReferenceEquals(this, Styx.StyxWoW.Me);
  public bool CanSelect { get; set; } = true;
  public bool Attackable { get; set; } = true;
  public bool IsPet { get; set; }
  public WoWUnit? OwnedByRoot { get; set; }
  public bool IsNonCombatPet { get; set; }
  public bool IsCritter { get; set; }
  public bool Combat { get; set; }
  public bool Aggro { get; set; }
  public bool PetAggro { get; set; }
  public bool IsTargetingMeOrPet { get; set; }
  public bool IsTargetingAnyMinion { get; set; }
  public bool IsTargetingMyPartyMember { get; set; }
  public bool IsTargetingMyRaidMember { get; set; }
  public bool TaggedByMe { get; set; }
  public float DistanceSqr { get; set; } = 25;
  public WoWUnit? CurrentTarget { get; set; }
  public WoWPoint Location { get; set; }
  public WoWCreatureType CreatureType { get; set; }
  public Dictionary<string, WoWAura> Auras { get; } = new();
  public IEnumerable<WoWAura> GetAllAuras() => Auras.Values;
  public Dictionary<ulong,uint> Threats { get; } = new();
  public UnitThreatInfo GetThreatInfoFor(WoWUnit p) => new() { ThreatValue = Threats.GetValueOrDefault(p.Guid) };
 }
 public class WoWPlayer : WoWUnit { public bool IsInMyPartyOrRaid = true; }
 public sealed class LocalPlayer : WoWPlayer
 {
  public Map CurrentMap { get; set; } = new();
  public bool IsInParty { get; set; } = true;
  public bool IsInRaid { get; set; }
  public List<WoWPlayer> PartyMembers { get; } = new();
  public List<WoWPlayer> RaidMembers { get; } = new();
 }
}
namespace Singular.Helpers
{
 public static class Group { public static List<Styx.WoWInternals.WoWObjects.WoWPlayer> Tanks { get; } = new(); }
}
namespace Singular.Lists
{
 public static class BossList { public static HashSet<uint> BossIds { get; }=new(); public static HashSet<uint> TrainingDummies { get; }=new(); }
}
