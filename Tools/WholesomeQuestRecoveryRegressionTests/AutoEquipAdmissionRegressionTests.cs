using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Complete tracked ammo/context/cursor admission, equipment-slot selector and
// loot-roll handler. Only world, API and scoring leaves are controlled. Does not
// turn the legacy ammo request into a physical-copy/server-acknowledged owner.
internal static class AutoEquipAdmissionRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,
            "runtime-snapshot/Plugins/AutoEquip2/AutoEquip.cs"))).GetRoot();
        var names = new HashSet<string> { "CheckAndEquipAmmo", "CanEquipNow", "CursorHasAnyItem", "HandleLootRoll", "FindBestEquipmentSlot" };
        string methods = string.Join("\n", syntax.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(m => names.Contains(m.Identifier.ValueText)).Select(m => m.ToFullString()));
        if (syntax.DescendantNodes().OfType<MethodDeclarationSyntax>().Count(m => names.Contains(m.Identifier.ValueText)) != names.Count)
            throw new InvalidOperationException("Missing complete AutoEquip admission owner");
        string pending = syntax.DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Single(p => p.Identifier.ValueText == "HasPendingEquip").ToFullString();
        string directory = Path.Combine(Path.GetTempPath(), "cb-auto-admission-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "Probe.cs"), Prefix + "public sealed class AutoEquipProbe {\n" +
                OwnerLeaves + pending + methods + Cases + "}\n");
            Type type = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(type, new object[] { directory })!;
            var result = (CompilerResults)type.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException(string.Join("; ", errors.Select(e => e.ToString())));
            var assembly = (Assembly)type.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            try { assembly.GetType("AutoEquipProbe", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException e) when (e.InnerException != null)
            { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
        }
        finally { Directory.Delete(directory, true); }
    }
    private static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CopilotBuddy.csproj"))) return d.FullName;
        throw new InvalidOperationException("Tracked checkout required");
    }
    private const string Prefix = """
#nullable disable
using System;using System.Collections.Generic;using System.Globalization;using System.Linq;
public enum InventorySlot {None=-1,AmmoSlot=0,HeadSlot=1,MainHandSlot=16,SecondaryHandSlot=17}
public enum InventoryType {Head,Ammo,Ranged,Cloak,Trinket,Neck,Finger,Weapon,TwoHandWeapon,WeaponMainHand,WeaponOffHand,Shield}
public enum WeaponStyle {None,TwoHander,OneHanderAndShield}
[Flags] public enum WeaponType {None=0,All=0x7FFFFFFF}
public enum WoWItemClass {Armor,Weapon}
public enum WoWItemArmorClass {Cloth,Leather,Mail,Plate}
public enum WoWItemQuality {Common,Heirloom}
public enum Stat {Strength,Intellect,Agility}
public enum StatTypes {Strength,Intellect,Agility}
public sealed class LuaEventArgs {public object[] Args=new object[]{17};}
public sealed class LocalPlayer {public bool IsValid=true,IsAlive=true,Combat,Dead,IsGhost;public ulong Guid=7;}
public static class ObjectManager {public static LocalPlayer Me=new LocalPlayer();}
public static class StyxWoW {public static bool IsInGame=true,IsInWorld=true;public static LocalPlayer Me=>ObjectManager.Me;}
public static class TreeRoot {public static bool IsRunning=true,IsPaused;}
public static class Battlegrounds {public static bool IsInsideBattleground;}
public sealed class AutoEquipSettings {
 public static AutoEquipSettings Instance=new AutoEquipSettings();
 public bool AutoEquipItems=true,RollForLootInDungeons=true,RollForLootDE,IncludeEnchants,ReplaceHeirlooms;
 public List<InventorySlot> ProtectedSlots=new List<InventorySlot>();
 public WeaponStyle WeaponStyle=WeaponStyle.TwoHander;public WeaponType WeaponType=WeaponType.All;
}
public sealed class ItemInfo {
 public string Name="Item";public InventoryType EquipSlot=InventoryType.Head,InventoryType=InventoryType.Head;
 public WoWItemClass ItemClass=WoWItemClass.Armor;public WoWItemArmorClass ArmorClass=WoWItemArmorClass.Plate;
 public static ItemInfo FromId(uint id){World.ItemInfoReads++;return World.MissingItemInfo?null:new ItemInfo();}
}
public sealed class WoWItem {public WoWItemQuality Quality=WoWItemQuality.Common;public ItemInfo ItemInfo=new ItemInfo();public float Score=20;}
public sealed class ItemStats {
 public Dictionary<StatTypes,float> Stats=new Dictionary<StatTypes,float>();
 public ItemStats(string link){World.StatsReads++;Stats[World.WrongPrimary?StatTypes.Intellect:StatTypes.Strength]=10;}
}
public sealed class WeightSet {
 public float EvaluateItem(WoWItem item,bool includeEnchants){World.ScoringReads++;return item.Score;}
 public float EvaluateItem(ItemInfo info,ItemStats stats){World.ScoringReads++;return World.RollScore;}
 public float GetStatScore(Stat stat,int amount)=>stat==Stat.Strength?3:1;
 public WoWItemArmorClass GetWantedArmorClass()=>WoWItemArmorClass.Plate;
}
public static class InventoryManager {
 public static List<InventorySlot> GetInventorySlotsByEquipSlot(InventoryType type)=>new List<InventorySlot>{InventorySlot.HeadSlot};
}
public static class World {
 public static string Link,AmmoResult;public static bool MissingItemInfo,CanNeed,CanDisenchant,WrongPrimary;
 public static float RollScore;public static int AmmoRequests,CursorReads,CursorReceipt,ItemInfoReads,StatsReads,ScoringReads,Exceptions;
 public static Action AfterCursor;public static List<string> Mutations=new List<string>();
}
public static class Lua {
 public static T GetReturnVal<T>(string script,uint index){
  if(script.Contains("GetCursorInfo")){
   World.CursorReads++;var action=World.AfterCursor;World.AfterCursor=null;action?.Invoke();return (T)(object)World.CursorReceipt;
  }
  if(script.Contains("EquipItemByName")){World.AmmoRequests++;return (T)(object)World.AmmoResult;}
  if(script.Contains("GetLootRollItemLink"))return (T)(object)World.Link;
  if(script.Contains("GetLootRollItemInfo"))return (T)(object)(index==7?World.CanDisenchant:World.CanNeed);
  throw new InvalidOperationException("Unexpected controlled API request: "+script);
 }
 public static void DoString(string script,params object[] args){World.Mutations.Add(args.Length==0?script:string.Format(CultureInfo.InvariantCulture,script,args));}
}
public static class Logging {public static void WriteException(Exception exception){World.Exceptions++;}}
""";
    private const string OwnerLeaves = """
 private bool _isDisposed;private ulong _pendingEquipGuid;private uint _pendingEquipEntry;
 private WeightSet _weightSet=new WeightSet();
 private Dictionary<InventorySlot,WoWItem> EquippedItems=new Dictionary<InventorySlot,WoWItem>();
 private static void Log(object message,params object[] args){}
 private static void LogDebug(string message,params object[] args){}
 private static WeaponType FindWeaponType(WoWItem item)=>WeaponType.All;
 private static List<InventoryType> GetInventoryTypesForWeaponStyle(WeaponStyle style)=>new List<InventoryType>{InventoryType.Weapon,InventoryType.Head};
""";
    private const string Cases = """
 private sealed class Failure(string reason):Exception(reason){}
 private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
 private static AutoEquipProbe Reset(){
  ObjectManager.Me=new LocalPlayer();StyxWoW.IsInGame=StyxWoW.IsInWorld=TreeRoot.IsRunning=true;
  TreeRoot.IsPaused=Battlegrounds.IsInsideBattleground=false;AutoEquipSettings.Instance=new AutoEquipSettings();
  World.Link="|Hitem:123:0|h[Item]|h";World.AmmoResult="Ammo";World.MissingItemInfo=World.CanDisenchant=World.WrongPrimary=false;World.CanNeed=true;
  World.RollScore=100;World.AmmoRequests=World.CursorReads=World.ItemInfoReads=World.StatsReads=World.ScoringReads=World.Exceptions=0;
  World.CursorReceipt=2;World.AfterCursor=null;World.Mutations.Clear();
  var probe=new AutoEquipProbe();probe.EquippedItems[InventorySlot.HeadSlot]=new WoWItem();probe.EquippedItems[InventorySlot.MainHandSlot]=new WoWItem();return probe;
 }
 private static InventorySlot Select(AutoEquipProbe probe,IEnumerable<InventorySlot> slots,out float score){
  try{return probe.FindBestEquipmentSlot(slots,out score);}
  catch(KeyNotFoundException){throw new Failure("unknown equipment slot was dereferenced before its intended guard");}
 }
 public static void Run(){
  int passed=0,assertions=0,unexpected=0,total=0;
  void Case(string name,Action body){total++;try{body();passed++;Console.WriteLine("PASS AutoEquip admission: "+name);}
   catch(Failure e){assertions++;Console.Error.WriteLine("FAIL AutoEquip admission: "+name+": "+e.Message);}
   catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR AutoEquip admission: "+name+": "+e);}}
  foreach(string mode in new[]{"disabled-setting","protected-ammo","pending-owner","paused","not-running","not-in-game","not-in-world","no-player","invalid-player","zero-player-guid","dead","combat","ghost","battleground","disposed","cursor-item","cursor-unknown","cursor-invalid"}){
   Case("ammo-admission/"+mode,()=>{
    var probe=Reset();switch(mode){
     case "disabled-setting":AutoEquipSettings.Instance.AutoEquipItems=false;break;
     case "protected-ammo":AutoEquipSettings.Instance.ProtectedSlots.Add(InventorySlot.AmmoSlot);break;
     case "pending-owner":probe._pendingEquipGuid=8;probe._pendingEquipEntry=123;break;
     case "paused":TreeRoot.IsPaused=true;break;case "not-running":TreeRoot.IsRunning=false;break;
     case "not-in-game":StyxWoW.IsInGame=false;break;case "not-in-world":StyxWoW.IsInWorld=false;break;
     case "no-player":ObjectManager.Me=null;break;case "invalid-player":ObjectManager.Me.IsValid=false;break;
     case "zero-player-guid":ObjectManager.Me.Guid=0;break;
     case "dead":ObjectManager.Me.IsAlive=false;ObjectManager.Me.Dead=true;break;
     case "combat":ObjectManager.Me.Combat=true;break;case "ghost":ObjectManager.Me.IsGhost=true;break;
     case "battleground":Battlegrounds.IsInsideBattleground=true;break;case "disposed":probe._isDisposed=true;break;
     case "cursor-item":World.CursorReceipt=1;break;case "cursor-unknown":World.CursorReceipt=0;break;case "cursor-invalid":World.CursorReceipt=7;break;
    }
    probe.CheckAndEquipAmmo();Check(World.AmmoRequests==0,"forbidden/unknown ammo context reached the mutation request");
    Check(World.Mutations.Count==0,"ammo admission cleared or mutated an unrelated cursor");
    Check(World.Exceptions==0,"ordinary missing/invalid ammo context escaped through exception logging");
   });
  }
  foreach(string mode in new[]{"actor-replaced","guid-changed","setting-disabled","slot-protected","paused","pending-owner"}){
   Case("ammo-after-cursor-observation/"+mode,()=>{
    var probe=Reset();World.AfterCursor=()=>{
     if(mode=="actor-replaced")ObjectManager.Me=new LocalPlayer{Guid=8};
     if(mode=="guid-changed")ObjectManager.Me.Guid=8;
     if(mode=="setting-disabled")AutoEquipSettings.Instance.AutoEquipItems=false;
     if(mode=="slot-protected")AutoEquipSettings.Instance.ProtectedSlots.Add(InventorySlot.AmmoSlot);
     if(mode=="paused")TreeRoot.IsPaused=true;
     if(mode=="pending-owner"){probe._pendingEquipGuid=8;probe._pendingEquipEntry=123;}
    };
    probe.CheckAndEquipAmmo();Check(World.CursorReads>0,"ammo request did not observe current cursor admission");
    Check(World.AmmoRequests==0,"actor/settings/owner changed during observation but ammo dispatch continued");
   });
  }
  foreach(string mode in new[]{"healthy","weapon-style-none","other-slot-protected","already-equipped","no-ammo"}){
   Case("ammo-preserved/"+mode,()=>{
    var probe=Reset();if(mode=="weapon-style-none")AutoEquipSettings.Instance.WeaponStyle=WeaponStyle.None;
    if(mode=="other-slot-protected")AutoEquipSettings.Instance.ProtectedSlots.Add(InventorySlot.HeadSlot);
    if(mode=="already-equipped")World.AmmoResult="EQUIPPED";if(mode=="no-ammo")World.AmmoResult="NONE";
    probe.CheckAndEquipAmmo();Check(World.AmmoRequests==1&&World.Exceptions==0,"eligible legacy ammo scan did not preserve one request");
    Check(World.Mutations.Count==0,"legacy scan gained an unrelated mutation");
   });
  }
  foreach(string mode in new[]{"protected-empty","protected-occupied","protected-empty-then-valid","unknown","unknown-then-valid","unprotected-empty","heirloom-protected","heirloom-allowed","ordinary"}){
   Case("slot-selection/"+mode,()=>{
    var probe=Reset();var first=InventorySlot.HeadSlot;var second=InventorySlot.MainHandSlot;var unknown=(InventorySlot)99;
    var slots=new List<InventorySlot>{first};InventorySlot expected=InventorySlot.None;
    if(mode.StartsWith("protected")){AutoEquipSettings.Instance.ProtectedSlots.Add(first);if(mode!="protected-occupied")probe.EquippedItems[first]=null;}
    if(mode=="protected-empty-then-valid"){slots.Add(second);expected=second;}
    if(mode=="unknown"||mode=="unknown-then-valid"){slots=new List<InventorySlot>{unknown};if(mode=="unknown-then-valid"){slots.Add(second);expected=second;}}
    if(mode=="unprotected-empty"){probe.EquippedItems[first]=null;expected=first;}
    if(mode=="heirloom-protected"||mode=="heirloom-allowed"){probe.EquippedItems[first].Quality=WoWItemQuality.Heirloom;AutoEquipSettings.Instance.ReplaceHeirlooms=mode=="heirloom-allowed";if(mode=="heirloom-allowed")expected=first;}
    if(mode=="ordinary")expected=first;
    var actual=Select(probe,slots,out float score);Check(actual==expected,"empty/unknown/protected slot bypassed its actual policy");
    if(expected==InventorySlot.None)Check(score==float.MaxValue,"no admissible slot manufactured a favorable score");
    if(mode=="unprotected-empty")Check(score==float.MinValue,"unprotected empty-slot policy changed");
   });
  }
  foreach(string mode in new[]{"null","empty","no-colon","bad-id","zero-id","missing-info","healthy-need","no-need-permission","disenchant","no-weight","disabled","inferior","wrong-primary"}){
   Case("actual-loot-handler/"+mode,()=>{
    var probe=Reset();switch(mode){
     case "null":World.Link=null;break;case "empty":World.Link="";break;case "no-colon":World.Link="garbage";break;
     case "bad-id":World.Link="item:garbage:0";break;case "zero-id":World.Link="item:0:0";break;
     case "missing-info":World.MissingItemInfo=true;break;case "no-need-permission":World.CanNeed=false;break;
     case "disenchant":AutoEquipSettings.Instance.RollForLootDE=true;World.CanDisenchant=true;break;
     case "no-weight":probe._weightSet=null;break;case "disabled":AutoEquipSettings.Instance.RollForLootInDungeons=false;break;
     case "inferior":World.RollScore=1;break;case "wrong-primary":World.WrongPrimary=true;break;
    }
    try{probe.HandleLootRoll(null,new LuaEventArgs());}
    catch(NullReferenceException) when(mode=="null"){throw new Failure("null loot link was split before its intended validation");}
    int expected=mode=="healthy-need"?1:mode=="disenchant"?3:mode=="no-need-permission"||mode=="no-weight"||mode=="inferior"||mode=="wrong-primary"?2:0;
    Check(World.Mutations.Count==(expected==0?0:1),"loot rejection or valid roll mutation count changed");
    if(expected!=0)Check(World.Mutations[0]=="RollOnLoot(17, "+expected+")","valid need/greed/disenchant policy changed");
    if(mode=="null"||mode=="empty"||mode=="no-colon"||mode=="bad-id"||mode=="zero-id")Check(World.ItemInfoReads==0&&World.ScoringReads==0,"invalid link reached item/scoring leaves");
   });
  }
  Console.WriteLine($"AutoEquip admission scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; complete tracked ammo/context/cursor, slot selection and loot handler; controlled world/API/scoring; no native inventory mutation or physical-copy/server acknowledgement.");
  if(assertions+unexpected!=0)throw new InvalidOperationException("AutoEquip admission regression");
 }
""";
}
