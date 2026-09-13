// World, settings and merchant dispatch are controlled. SellByQuality is extracted
// verbatim during build; ProtectedItemsManager and its collections are real sources.
namespace Styx.Helpers { public static class Logging { public static void Write(string _) { } } }
namespace Styx.Logic.Profiles
{
 public sealed class Profile { public Styx.Helpers.DualHashSet<uint,string> ProtectedItems { get; }=new(); }
 public static class ProfileManager { public static Profile CurrentProfile { get; set; }=new(); }
}
namespace Styx { public static class StyxWoW { public static WholesomeAQ.Player? Me {get;set;}=new(); } }
namespace WholesomeAQ
{
 [Flags] public enum ItemQuality { None=0,Common=1,Uncommon=2,Rare=4 }
 public enum WoWItemClass { Miscellaneous,Projectile,Quiver,Reagent,Key }
 public sealed class Item { public uint Entry; public WoWItemClass ItemClass; }
 public sealed class Player { public bool IsValid=true,IsAlive=true; public QuestLog QuestLog {get;}=new(); public List<Item> BagItems {get;}=new(); }
 public sealed class QuestLog { public List<AcceptedQuest>? Quests {get;set;}=new(); public Exception? Failure; public List<AcceptedQuest>? GetAllQuests() { if(Failure!=null)throw Failure;return Quests; } }
 public sealed class AcceptedQuest { public uint Id; public bool IsCompleted; }
 public sealed class Objective { public int ItemId; }
 public sealed class QuestEntry { public int Id,StartItem; public List<Objective>? Objectives=new(); }
 public sealed class Database { public List<QuestEntry>? Quests=new(); }
 public sealed class Loader { public Database? Database=new(); }
 public sealed class Scheduler { public HashSet<int> ActiveQuestIds {get;set;}=new(); }
 public sealed class Settings { public bool SellWhite=true,SellGreen,SellBlue; }
 public static class Consumable
 {
  public static Item? Food,Drink;
  public static Item? GetBestFood(bool _) => Food;
  public static Item? GetBestDrink(bool _) => Drink;
 }
 public sealed class MerchantFrame
 {
  public static MerchantFrame Instance {get;set;}=new();
  public bool IsVisible=true; public int Calls;
  public ItemQuality Mask; public HashSet<uint> Ids=new();public HashSet<string> Names=new(StringComparer.OrdinalIgnoreCase);
  public void SellItemQualities(ItemQuality mask,IEnumerable<string>? names,IEnumerable<uint>? ids)
  { Calls++;Mask=mask;Ids=ids?.ToHashSet()??new();Names=new(names??Array.Empty<string>(),StringComparer.OrdinalIgnoreCase); }
 }
 public partial class WholesomeAutoQuest
 {
  public Settings _settings=new(); public Loader _dataLoader=new();public Scheduler? _scheduler=new();
  private bool _lastFrameVisible;
  private void Log(string _) { }
  public void RunSale()=>SellByQuality();
 }
}
