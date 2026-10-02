using Styx.Logic.POI;
using Styx.WoWInternals.WoWObjects;
public readonly record struct WoWPoint(float X, float Y, float Z) { public static WoWPoint Empty => default; public float DistanceSqr(WoWPoint p) => (X-p.X)*(X-p.X)+(Y-p.Y)*(Y-p.Y)+(Z-p.Z)*(Z-p.Z); public float Distance(WoWPoint p) => MathF.Sqrt(DistanceSqr(p)); }
public delegate void ObjectInvalidateDelegate();
namespace Styx.Helpers { public static class Logging { public static void WriteDebug(string text, params object?[] args) { } public static void WriteException(Exception e) { throw e; } } }
namespace Styx.WoWInternals.WoWObjects {
 public class WoWObject { public uint BaseAddress=1; public ulong Guid; public uint Entry; public WoWPoint Location; public double DistanceSqr=>0; public bool IsValid=true, IsDisabled; public string Name="object"; public event ObjectInvalidateDelegate? OnInvalidate; public void Invalidate() { IsValid=false; OnInvalidate?.Invoke(); } }
 public class WoWUnit:WoWObject { public bool CanLoot; }
 public class LocalPlayer:WoWUnit { public uint MapId; public bool IsAlive=true, Combat, IsActuallyInCombat; }
 public class WoWPlayer:WoWUnit { }
 public class WoWItem:WoWObject { }
 public enum WoWGameObjectType { Mailbox }
 public class WoWGameObject:WoWObject { public WoWGameObjectType SubType; public bool CanLoot=true,WithinInteractRange; }
}
namespace Styx.WoWInternals { public static class ObjectManager { public static LocalPlayer? Me; public static object? Wow; public static readonly List<WoWObject> Objects=new(); public static IEnumerable<WoWObject> ObjectList=>Objects; public static IEnumerable<WoWUnit> CachedUnits=>Objects.OfType<WoWUnit>(); public static T? GetObjectByGuid<T>(ulong guid) where T:WoWObject => Objects.OfType<T>().FirstOrDefault(o=>o.Guid==guid); } }
namespace Styx.Logic {
 public class Targeting { public sealed class TargetPriority { public WoWObject Object=null!; public double Score; } }
 public class LootTargeting:Targeting { public static LootTargeting Instance=new(); public event Action<List<TargetPriority>>? WeighTargetsFilter; public void Weigh(List<TargetPriority> targets)=>WeighTargetsFilter?.Invoke(targets); }
 public static class Blacklist { public static readonly HashSet<ulong> Entries=new(); public static bool Contains(ulong guid,bool flush)=>Entries.Contains(guid); }
 public static class BotEvents { public static class Player { public static event Action? OnPlayerDied; public static void Die()=>OnPlayerDied?.Invoke(); } }
 public static class VendorSafetyPolicy { public static bool IsService(PoiType type)=>false; }
 public static class VendorManager { public static void RejectVendor(int id,string why){} }
 public static class FlightPaths { public static void ResetOwnedState(bool clear, string reason){ if(clear)BotPoi.Current=new BotPoi(PoiType.None); } }
}
namespace Styx.Logic.Questing { public class Quest { public uint Id; public string Name="quest"; } }
namespace Styx.Logic.Profiles {
 public class Profile { }
 public static class ProfileManager { public static Profile? CurrentProfile; public static Profile? CurrentProfileSnapshot=>CurrentProfile; }
 public class Mailbox { public WoWPoint Location; }
 public class Vendor { public string Name="vendor"; public int Entry; public WoWPoint Location; }
}
namespace Styx.Logic.Profiles.Quest {
 public enum QuestObjectType { Npc, GameObject, Item }
 public class PickUpNode { public uint GiverId; public WoWPoint GiverLocation; public string GiverName="giver"; public QuestObjectType? GiverType; }
 public class TurnInNode { public uint TurnInId; public WoWPoint TurnInLocation; public string TurnInName="ender"; public QuestObjectType? TurnInType; }
}
namespace Styx.Logic.POI { public enum PoiType {None,Kill,Loot,Skin,Harvest,Sell,Repair,Train,Buy,Mail,Fly,Hotspot,Quest,QuestPickUp,QuestTurnIn} }
namespace Styx.Logic.Pathing {
 public interface IPlayerMover { }
 public sealed class Mover:IPlayerMover { }
 public class NavigationProvider { }
 public static class Navigator { public static IPlayerMover PlayerMover=new Mover(); public static NavigationProvider? NavigationProvider; public static void InvalidatePoiRoute(){} }
 public partial class MeshNavigator {
  private object _routeOwner=new();
  public Func<bool> Observe(){_routeLease=null;var request=new MovementRequestObservation(this);return ()=>request.IsCurrent(this);}
  public Func<bool> ObserveLeased(Func<bool> lease){_routeLease=lease;var request=new MovementRequestObservation(this);return ()=>request.IsCurrent(this);}
  public void ReplaceRequest()=>_routeOwner=new();
 }
}
