// Only external memory/cache/world/merchant boundaries are controlled.
// Entire production QuestLog and PlayerQuest sources are linked, including FromId.
using Styx.Logic.Questing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Styx.WoWInternals.WoWCache;
namespace GreenMagic
{
 public sealed class Memory
 {
  public WoWDescriptorQuest[] Slots=Enumerable.Range(0,25).Select(_=>new WoWDescriptorQuest{ObjectivesDone=new ushort[4]}).ToArray();
  public int ArrayReads; public bool CacheEnabled=true,ReadWhileCached; public Action? AfterArrayRead; public Exception? Failure;
  public int ProcessId=1;
  // Models the existing Memory.ReadBytes full-length/null contract. These switches
  // are external observations, not production snapshot or readiness implementations.
  public bool FailRawLogReads, ShortRawLogRead;
  public byte[]? ReadBytes(uint address,int count)
  {
   if(Failure!=null)throw Failure;
   var me=ObjectManager.Me ?? throw new InvalidOperationException("No observed player");
   if(address==me.BaseAddress+8U && count==4)return BitConverter.GetBytes(me.Descriptor);
   if((address==me.Descriptor || address==me.BaseAddress+48U) && count==8)return BitConverter.GetBytes(me.Guid);
   if(address==me.Descriptor+632U && count==500)
   {
    ArrayReads++;ReadWhileCached|=CacheEnabled;
    if(FailRawLogReads)return null;
    byte[] data=new byte[500];
    for(int i=0;i<25;i++)
    {
     BitConverter.GetBytes(Slots[i].Id).CopyTo(data,i*20);
     BitConverter.GetBytes((uint)Slots[i].Flags).CopyTo(data,i*20+4);
     for(int j=0;j<4;j++)BitConverter.GetBytes(Slots[i].ObjectivesDone[j]).CopyTo(data,i*20+8+j*2);
     BitConverter.GetBytes(Slots[i].SecondsBeforeFailed).CopyTo(data,i*20+16);
    }
    AfterArrayRead?.Invoke();
    return ShortRawLogRead ? data[..499] : data;
   }
   throw new InvalidOperationException("Unexpected byte read "+address+"/"+count);
  }
  public T Read<T>(uint address) where T:struct
  {
   if(Failure!=null)throw Failure;
   if(FailRawLogReads && ObjectManager.Me!=null && address>=ObjectManager.Me.Descriptor+632U && address<ObjectManager.Me.Descriptor+1132U)return default;
   object value;
   var me=ObjectManager.Me;
   if(address==me!.BaseAddress+8U)value=me.Descriptor;
   else if(address==me.Descriptor || address==me.BaseAddress+48U)value=me.Guid;
   else if(address==12729040U)value=(uint)Slots.Count(q=>q.Id!=0);
   else if(address>=me.Descriptor+632U && address<me.Descriptor+1132U)value=Slots[(address-me.Descriptor-632U)/20U].Id;
   else throw new InvalidOperationException("Unexpected memory read "+address);
   return (T)value;
  }
  public T[] ReadStructArray<T>(uint address,int count) where T:struct
  {
   if(Failure!=null)throw Failure;
   if(typeof(T)!=typeof(WoWDescriptorQuest) || count!=25)throw new InvalidOperationException("Unexpected native boundary");
   ArrayReads++;ReadWhileCached|=CacheEnabled;
   if(FailRawLogReads)return (T[])(object)new WoWDescriptorQuest[25];
   var result=Slots.Select(q=>new WoWDescriptorQuest{Id=q.Id,Flags=q.Flags,ObjectivesDone=(ushort[])q.ObjectivesDone.Clone(),SecondsBeforeFailed=q.SecondsBeforeFailed}).ToArray();
   AfterArrayRead?.Invoke();return (T[])(object)result;
  }
  public T ReadStruct<T>(uint address) where T:struct => Read<T>(address);
  public IDisposable TemporaryCacheState(bool enabled){var old=CacheEnabled;CacheEnabled=enabled;return new Restore(()=>CacheEnabled=old);}
  private sealed class Restore(Action action):IDisposable {public void Dispose()=>action();}
 }
 public sealed class ExecutorRand
 {
  public object AssemblyLock=new(); public Memory Memory=new();public uint ReturnPointer;
  public void Clear()=>throw new InvalidOperationException("Native dispatch forbidden in fixture");
  public void AddLine(string _,params object[] args) { }
  public void Execute()=>throw new InvalidOperationException("Native dispatch forbidden in fixture");
 }
}
namespace Styx.WoWInternals
{
 public static class ObjectManager
 {public static LocalPlayer? Me;public static GreenMagic.Memory? Wow;public static GreenMagic.ExecutorRand? Executor;public static bool IsInGame=true;}
 public sealed class LuaEventWait(string name):IDisposable {public bool Wait(int ms)=>false;public void Dispose() { }}
 public static class Lua {public static void DoString(string _) { }public static List<string> GetReturnValues(string _,string name)=>new(){""};}
}
namespace Styx.WoWInternals.WoWObjects
{
 public sealed class LocalPlayer
 {
  public uint BaseAddress=4096,Descriptor=8192;public ulong Guid=123;public bool IsValid=true,IsAlive=true;
  public string Name="fixture",RealmName="realm";
  public readonly QuestLog QuestLog=new();public List<WholesomeAQ.Item> BagItems=new();
  public T ReadDescriptor<T>(uint field) where T:struct => ObjectManager.Wow!.Read<T>(Descriptor+field*4);
 }
}
namespace Styx.WoWInternals.WoWCache
{
 public enum CacheDb{Quest}
 public sealed class WoWCache
 {
  public sealed class QuestCacheEntry {public uint Id;}
  public sealed class InfoBlock {public QuestCacheEntry Quest=new();}
  public Dictionary<uint,InfoBlock> Entries=new(); public int Lookups;public Action<uint>? AfterLookup;public Exception? Failure;
  public WoWCache this[CacheDb db]=>this;
  public InfoBlock? GetInfoBlockById(uint id){if(Failure!=null)throw Failure;Lookups++;Entries.TryGetValue(id,out var result);AfterLookup?.Invoke(id);return result;}
 }
}
namespace Styx
{
 public static class StyxWoW
 {
  public static LocalPlayer? Me {get=>ObjectManager.Me;set=>ObjectManager.Me=value;}
  public static WoWCache Cache=new();public static OffsetTable Offsets=new();public static bool IsInGame=>ObjectManager.IsInGame;
 }
 public sealed class OffsetTable {public uint GetOffsetByIndex(int index)=>0;}
}
namespace Styx.Logic.Questing
{
 public class Quest {protected Quest(WoWCache.QuestCacheEntry entry){Id=entry.Id;}public uint Id {get;}public static Quest? FromId(uint id)=>null;}
 public struct WoWQuestCompletionInfo {public int QuestID;}
}
namespace Styx.Helpers
{
 public static class Logging {public static void Write(string _,params object[] args){}public static void WriteDebug(string _,params object[] args){}public static void WriteDiagnostic(string _,params object[] args){}public static void WriteException(Exception _) {}}
}
namespace Styx.Logic.Profiles
{
 public sealed class Profile {public Styx.Helpers.DualHashSet<uint,string> ProtectedItems {get;}=new();}
 public static class ProfileManager {public static Profile CurrentProfile {get;set;}=new();}
}
namespace WholesomeAQ
{
 [Flags] public enum ItemQuality {None=0,Common=1,Uncommon=2,Rare=4}
 public enum WoWItemClass {Miscellaneous,Projectile,Quiver,Reagent,Key}
 public sealed class Item {public uint Entry;public WoWItemClass ItemClass;}
 public sealed class Objective {public int ItemId;}
 public sealed class QuestEntry {public int Id,StartItem;public List<Objective>? Objectives=new();}
 public sealed class Database {public List<QuestEntry>? Quests=new();}
 public sealed class Loader {public Database? Database=new();}
 public sealed class Scheduler {public HashSet<int> ActiveQuestIds {get;set;}=new();}
 public sealed class Settings {public bool SellWhite=true,SellGreen,SellBlue;}
 public static class Consumable {public static Action? OnFood;public static Item? GetBestFood(bool _){OnFood?.Invoke();return null;}public static Item? GetBestDrink(bool _)=>null;}
 public sealed class MerchantFrame
 {
  public static MerchantFrame Instance {get;set;}=new();public bool IsVisible=true;public int Calls;public HashSet<uint> Ids=new();
  public void SellItemQualities(ItemQuality mask,IEnumerable<string>? names,IEnumerable<uint>? ids){Calls++;Ids=ids?.ToHashSet()??new();}
 }
 public partial class WholesomeAutoQuest
 {public Settings _settings=new();public Loader _dataLoader=new();public Scheduler? _scheduler=new();private bool _lastFrameVisible;private void Log(string _){}public void RunSale()=>SellByQuality();}
}
