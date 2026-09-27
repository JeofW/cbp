using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Both actual equipment owners, actual pickup/ownership Lua and host InventorySlot.
// The native/server boundaries are controlled, not a running game: by-name lookup
// can select another same-entry copy, whereas automatic cursor equip uses the
// selected physical item and raises AUTOEQUIP_BIND_CONFIRM synchronously.
internal static class DefaultEquipmentOwnershipRegressionTests
{
    private sealed class Failure(string reason) : Exception(reason) { }
    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        string directory = Path.Combine(Path.GetTempPath(), "cb-default-equip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        int passed = 0, total = 0, assertions = 0, unexpected = 0;
        try
        {
            string luaSource = File.ReadAllText(Path.Combine(root, "Styx/WoWInternals/Lua.cs"));
            RewardLua51Boundary.WriteManagedBridge(directory, luaSource);
            string bridge = "public static class Lua {\n" + LuaBoundary + Methods(luaSource,
                "BeginEquipCursorOwnership", "BuildEquipCursorOwnershipLua", "TryEquipCursorItem",
                "BuildEquipCursorSubmissionLua", "TryCancelEquipCursorItem", "BuildCancelEquipCursorLua") + "}\n";
            string itemSource = File.ReadAllText(Path.Combine(root, "Styx/WoWInternals/WoWObjects/WoWItem.cs"));
            string pickup = string.Join("\n", Nodes(itemSource).Where(m =>
                m.Identifier.ValueText is "TryResolveContainerLocation" or "IsContainerLocationCurrent" or "BuildValidatedContainerPickupLua" ||
                m.Identifier.ValueText == "TryPickUp" && m.ParameterList.Parameters.Count == 2).Select(m => m.ToFullString()));
            string owners = "";
            foreach (var owner in new[] {
                (Path:"runtime-snapshot/Quest Behaviors/EquipItem.cs", Name:"QuestProbe", Quest:true),
                (Path:"runtime-snapshot/Plugins/AutoEquip2/AutoEquip.cs", Name:"AutoProbe", Quest:false) })
                owners += "public sealed class " + owner.Name + " {\n" + OwnerFields +
                    Methods(File.ReadAllText(Path.Combine(root, owner.Path)), "TickPendingEquip", "BeginEquip",
                        "CanEquipNow", "OwnsPendingEquipContext", "SubmitOwnedCursorEquip", "IsPendingEquipAcknowledged",
                        "ReturnDisplacedCursorToSource", "RestoreOwnedCursorToSource", "CursorHasAnyItem", "ResetPendingEquip") +
                    (owner.Quest ? "public void Begin(){TickPendingEquip();}\n" :
                        "public void Begin(){BeginEquip(World.Candidate,Slot,Slot==InventorySlot.None);}\n") + OwnerCalls + "}\n";
            File.WriteAllText(Path.Combine(directory, "Probe.cs"), Prefix + pickup + "}\n" + World + bridge + owners);
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { directory })!;
            var compiled = (CompilerResults)compilerType.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = compiled.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException(string.Join("; ", errors.Select(e => e.ToString())));
            var assembly = (Assembly)compilerType.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            var recorded = assembly.GetType("RewardRecordedBridge", true)!;
            var observe = recorded.GetField("Observe")!;
            var loadSize = (Func<string, uint>)recorded.GetMethod("LoadSize")!.CreateDelegate(typeof(Func<string, uint>));
            var api = assembly.GetType("Lua", true)!;
            var world = assembly.GetType("World", true)!;
            using var lua = new RewardLua51Boundary.StockLua51(root);
            void Case(string name, Action test)
            {
                total++;
                try { test(); passed++; Console.WriteLine("PASS default equipment: " + name); }
                catch (Failure e) { assertions++; Console.Error.WriteLine("FAIL default equipment: " + name + ": " + e.Message); }
                catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR default equipment: " + name + ": " + e); }
                finally { observe.SetValue(null, null); }
            }
            foreach (string ownerName in new[] { "QuestProbe", "AutoProbe" })
            foreach (var c in new[] {
                (Mode:"default", Auto:1, Explicit:0, Confirms:0, Clears:0, Equipped:200),
                (Mode:"bag-default", Auto:1, Explicit:0, Confirms:0, Clears:0, Equipped:200),
                (Mode:"bind-zero", Auto:1, Explicit:0, Confirms:1, Clears:0, Equipped:200),
                (Mode:"bind-seven", Auto:1, Explicit:0, Confirms:1, Clears:0, Equipped:200),
                (Mode:"cache-missing", Auto:0, Explicit:0, Confirms:0, Clears:1, Equipped:0),
                (Mode:"cache-retry", Auto:1, Explicit:0, Confirms:0, Clears:0, Equipped:200),
                (Mode:"preexisting-popup", Auto:0, Explicit:0, Confirms:0, Clears:1, Equipped:0),
                (Mode:"wrong-popup-data", Auto:1, Explicit:0, Confirms:0, Clears:1, Equipped:0),
                (Mode:"two-bind-events", Auto:1, Explicit:0, Confirms:0, Clears:1, Equipped:0),
                (Mode:"missing-button", Auto:1, Explicit:0, Confirms:0, Clears:1, Equipped:0),
                (Mode:"late-bind", Auto:1, Explicit:0, Confirms:0, Clears:1, Equipped:0),
                (Mode:"cursor-changed-bind", Auto:1, Explicit:0, Confirms:0, Clears:0, Equipped:0),
                (Mode:"reselected", Auto:0, Explicit:0, Confirms:0, Clears:0, Equipped:0),
                (Mode:"foreign-copy", Auto:0, Explicit:0, Confirms:0, Clears:0, Equipped:0),
                (Mode:"virtual-cursor", Auto:0, Explicit:0, Confirms:0, Clears:0, Equipped:0),
                (Mode:"lost-observer", Auto:0, Explicit:0, Confirms:0, Clears:0, Equipped:0),
                (Mode:"pickup-refused", Auto:0, Explicit:0, Confirms:0, Clears:0, Equipped:0),
                (Mode:"wrong-source", Auto:0, Explicit:0, Confirms:0, Clears:0, Equipped:0),
                (Mode:"stopped", Auto:0, Explicit:0, Confirms:0, Clears:0, Equipped:0),
                (Mode:"actor-change", Auto:0, Explicit:0, Confirms:0, Clears:0, Equipped:0),
                (Mode:"stop-during-pickup", Auto:0, Explicit:0, Confirms:0, Clears:0, Equipped:0),
                (Mode:"already-equipped", Auto:0, Explicit:0, Confirms:0, Clears:0, Equipped:200),
                (Mode:"already-busy", Auto:0, Explicit:0, Confirms:0, Clears:0, Equipped:200),
                (Mode:"already-unknown", Auto:0, Explicit:0, Confirms:0, Clears:0, Equipped:200),
                (Mode:"explicit-main", Auto:0, Explicit:1, Confirms:0, Clears:0, Equipped:200) })
            {
                Case(ownerName + "/" + c.Mode, () =>
                {
                    world.GetMethod("Reset")!.Invoke(null, new object[] { c.Mode });
                    using var session = lua.BeginSession("scenario='" + c.Mode + "'\n" + Setup);
                    RewardLua51Boundary.Observation Execute(string script)
                    {
                        var result = session.Execute(script, loadSize(script));
                        if (result.Load != 0 || result.Call != 0) throw new InvalidOperationException("Lua: " + result.Error);
                        return result;
                    }
                    void Sync()
                    {
                        var v = Execute("return nativeKind,nativeGuid,equippedGuid,destination").Values;
                        api.GetField("CursorKind")!.SetValue(null, uint.Parse(v[0], CultureInfo.InvariantCulture));
                        api.GetField("CursorGuid")!.SetValue(null, ulong.Parse(v[1], CultureInfo.InvariantCulture));
                        world.GetMethod("SetEquipment")!.Invoke(null, new object[] {
                            ulong.Parse(v[2], CultureInfo.InvariantCulture), int.Parse(v[3], CultureInfo.InvariantCulture) });
                    }
                    api.GetField("LastExpectedGuid")!.SetValue(null, 0UL);
                    observe.SetValue(null, new Func<string, List<string>>(script =>
                    {
                        string execution = c.Mode == "already-unknown" && script.Contains("and 1 or 2", StringComparison.Ordinal)
                            ? "return" : script;
                        var result = Execute(execution); Sync();
                        if (script.Contains("PickupContainerItem(", StringComparison.Ordinal))
                            world.GetMethod("AfterPickup")!.Invoke(null, null);
                        return result.Values;
                    }));
                    Sync();
                    Type type = assembly.GetType(ownerName, true)!;
                    object owner = Activator.CreateInstance(type)!;
                    if (c.Mode == "explicit-main") type.GetMethod("Explicit")!.Invoke(owner, null);
                    type.GetMethod("Begin")!.Invoke(owner, null);
                    if (c.Mode == "cache-retry") Execute("cacheReady=true");
                    if (c.Mode == "late-bind") Execute("popup=MakePopup(16);Fire('AUTOEQUIP_BIND_CONFIRM',16)");
                    bool Pending() => (bool)type.GetProperty("Pending")!.GetValue(owner)!;
                    if (Pending()) type.GetMethod("Tick")!.Invoke(owner, null);
                    if (Pending()) type.GetMethod("Tick")!.Invoke(owner, null);
                    if (Pending()) type.GetMethod("Expire")!.Invoke(owner, null);
                    Check(!Pending(), "default equipment failed to complete or bound its original deadline");
                    var counts = Execute("return byNames,autoSubmits,explicitSubmits,confirms,clears,equippedGuid").Values;
                    Check(counts.SequenceEqual(new[] { "0", c.Auto.ToString(), c.Explicit.ToString(), c.Confirms.ToString(),
                        c.Clears.ToString(), c.Equipped.ToString() }), "by-name/auto/explicit/confirm/clear/equipped=" + string.Join("/", counts));
                    if (c.Auto + c.Explicit + c.Clears != 0)
                        Check((ulong)api.GetField("LastExpectedGuid")!.GetValue(null)! == 200UL,
                            "default request did not retain the captured full physical GUID dispatch boundary");
                    if (c.Mode.StartsWith("already-", StringComparison.Ordinal) || c.Mode == "stopped")
                        Check(Execute("return pickups").Values.SequenceEqual(new[] { "0" }), "satisfied or revoked work picked up an item");
                    if (c.Clears != 0 || c.Auto + c.Explicit != 0 && c.Equipped == 200)
                        Check(Execute("return ActiveObservers()").Values.SequenceEqual(new[] { "0" }), "finished selected cursor retained an active observer");
                });
            }
            foreach (int slot in new[] { -2, -1, 0, 24 })
                Case("real slot routing/" + slot, () =>
                {
                    Check((int)Styx.Logic.Inventory.InventorySlot.None == -1 && (int)Styx.Logic.Inventory.InventorySlot.AmmoSlot == 0,
                        "host default/ammo slot contract changed");
                    using var session = lua.BeginSession("scenario='route'\n" + Setup);
                    List<string> Execute(string script)
                    {
                        var r = session.Execute(script, loadSize(script));
                        if (r.Load != 0 || r.Call != 0) throw new InvalidOperationException(r.Error);
                        return r.Values;
                    }
                    observe.SetValue(null, new Func<string, List<string>>(Execute));
                    string token = "0123456789abcdef0123456789abcdef";
                    Check((bool)api.GetMethod("BeginEquipCursorOwnership")!.Invoke(null, new object[] { 100U, token })!, "route observer setup failed");
                    Execute("PickupContainerItem(0,1)");
                    api.GetField("CursorKind")!.SetValue(null, 1U);api.GetField("CursorGuid")!.SetValue(null, 200UL);
                    bool result = (bool)api.GetMethod("TryEquipCursorItem")!.Invoke(null, new object[] { 200UL, 100U, slot, token })!;
                    Check(result == (slot == -1 || slot == 0), "valid default/ammo or invalid slot admission differs");
                    Check(Execute("return autoSubmits,explicitSubmits,byNames").SequenceEqual(new[] {
                        slot == -1 ? "1" : "0", slot == 0 ? "1" : "0", "0" }), "default sentinel used an explicit ammo slot or by-name lookup");
                });
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine($"Default equipment ownership scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; actual tracked owners/pickup/generated Lua5.1 and host slot enum; controlled physical dispatch and server acknowledgement; no game/native execution.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Default equipment ownership regression");
    }
    private static MethodDeclarationSyntax[] Nodes(string s) => CSharpSyntaxTree.ParseText(s).GetRoot()
        .DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
    private static string Methods(string s, params string[] names) => string.Join("\n", Nodes(s)
        .Where(m => names.Contains(m.Identifier.ValueText)).Select(m => m.ToFullString()));
    private static void Check(bool value, string why) { if (!value) throw new Failure(why); }
    private static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CopilotBuddy.csproj"))) return d.FullName;
        throw new InvalidOperationException("Tracked checkout required");
    }
    private const string Prefix = """
using System;using System.Linq;using System.Collections.Generic;using System.Globalization;using Styx.Logic.Inventory;
public enum RunStatus {Success,Failure,Running}
public sealed class WoWContainer {public ulong[] ItemGuids=new ulong[]{200,201};}
public sealed class Equipment {public WoWItem[] Items=new WoWItem[23];}
public sealed class Inventory {public WoWContainer Backpack=new WoWContainer();public Equipment Equipped=new Equipment();}
public sealed class LocalPlayer {
 public ulong Guid=7;public bool IsValid=true,IsAlive=true,IsGhost,Combat;
 public Inventory Inventory=new Inventory();public List<WoWItem> CarriedItems=new List<WoWItem>();
 public WoWContainer GetBagAtIndex(uint index)=>null;
}
public static class StyxWoW {public static LocalPlayer Me;public static bool IsInGame=true;}
public static class ObjectManager {public static LocalPlayer Me=>StyxWoW.Me;}
public static class TreeRoot {public static bool IsRunning=true;}
public static class Battlegrounds {public static bool IsInsideBattleground;}
public static class Logging {public static void WriteDebug(string format,params object[] args){}}
public sealed class WoWItem {public ulong Guid=200;public uint Entry=100;public bool IsValid=true;public string Name=>"Candidate";
""";
    private const string World = """
public static class World {
 public static WoWItem Candidate;public static string Mode;
 public static void Reset(string mode){
  Mode=mode;Candidate=new WoWItem();StyxWoW.Me=new LocalPlayer();
  StyxWoW.Me.CarriedItems.Add(Candidate);StyxWoW.Me.CarriedItems.Add(new WoWItem{Guid=201});
  StyxWoW.IsInGame=true;TreeRoot.IsRunning=mode!="stopped";Battlegrounds.IsInsideBattleground=false;
 }
 public static void SetEquipment(ulong guid,int slot){
  if(guid!=0)StyxWoW.Me.Inventory.Equipped.Items[slot-1]=new WoWItem{Guid=guid};
 }
 public static void AfterPickup(){
  if(Mode=="actor-change"){StyxWoW.Me=new LocalPlayer();StyxWoW.Me.CarriedItems.Add(Candidate);}
  if(Mode=="stop-during-pickup")TreeRoot.IsRunning=false;
 }
}
""";
    private const string LuaBoundary = """
 public static uint CursorKind;public static ulong CursorGuid,LastExpectedGuid;
 public static List<string> GetReturnValues(string script)=>RewardRecordedBridge.GetReturnValues(script);
 public static T GetReturnVal<T>(string script,uint index)=>RewardRecordedBridge.GetReturnVal<T>(script,index);
 public static void DoString(string format,params object[] args)=>GetReturnValues(args.Length==0?format:string.Format(CultureInfo.InvariantCulture,format,args));
 public static List<string> GetReturnValuesCore(string script,string name,ulong guid){
  LastExpectedGuid=guid;
  return guid!=0&&CursorKind==1&&CursorGuid==guid?GetReturnValues(script):new List<string>();
 }
""";
    private const string OwnerFields = """
 private bool _isBehaviorDone,_isDisposed,_pendingEquipSubmitted;
 private ulong _pendingEquipGuid,_pendingEquipPlayerGuid;private uint _pendingEquipEntry;
 private LocalPlayer _pendingEquipPlayer;private InventorySlot _pendingEquipSlot=InventorySlot.None;
 private int _pendingSourceBag=-1,_pendingSourceSlot=-1;private string _pendingCursorOwner;
 private DateTime _pendingEquipSince;private static readonly TimeSpan EquipTimeout=TimeSpan.FromSeconds(10);
 private bool HasPendingEquip=>_pendingEquipGuid!=0&&_pendingEquipEntry!=0;
 private int ItemId=>100;private int QuestId=>900001;private int QuestRequirementInLog=>1;private int QuestRequirementComplete=>0;
 private InventorySlot Slot=InventorySlot.None;
 private bool UtilIsProgressRequirementsMet(int quest,int inLog,int complete)=>true;
 private void LogMessage(string level,string format,params object[] args){}
 private void Log(string format,params object[] args){}
""";
    private const string OwnerCalls = """
 public bool Pending=>HasPendingEquip;
 public void Tick(){TickPendingEquip();}
 public void Expire(){_pendingEquipSince=DateTime.UtcNow-TimeSpan.FromSeconds(11);TickPendingEquip();}
 public void Explicit(){Slot=InventorySlot.MainHandSlot;}
""";
    private const string Setup = """
clicks=0;byNames=0;autoSubmits=0;explicitSubmits=0;confirms=0;clears=0;pickups=0
nativeKind=0;nativeGuid=0;equippedGuid=0;destination=scenario=='bag-default' and 20 or 16
cacheReady=scenario~='cache-missing' and scenario~='cache-retry'
local kind,entry=nil,nil;local frames={};popup=nil
function CreateFrame()
 local f={events={}}
 function f:RegisterEvent(e) self.events[e]=true end
 function f:IsEventRegistered(e) return self.events[e] or false end
 function f:UnregisterAllEvents() self.events={} end
 function f:SetScript(e,fn) self.fn=fn end
 function f:GetScript(e) return self.fn end
 frames[#frames+1]=f;return f
end
function Fire(e,value) for _,f in ipairs(frames) do if f.events[e] and f.fn then f.fn(f,e,value) end end end
function ActiveObservers() local n=0;for _,f in ipairs(frames) do for _ in pairs(f.events) do n=n+1 end end;return n end
function SetCursor(k,id,guid,physicalKind)
 kind,entry=k,id;nativeKind=physicalKind or (k=='item' and 1 or 0);nativeGuid=guid or 0;Fire('CURSOR_UPDATE')
end
if string.sub(scenario,1,8)=='already-' then equippedGuid=200 end
if scenario=='already-busy' then SetCursor('item',999,900) end
function GetCursorInfo() return kind,entry end
function CursorHasItem() return kind=='item' end
function GetContainerItemLink(bag,slot)
 assert(bag==0 and slot==1);return scenario=='wrong-source' and '|Hitem:999:0|h[Other]|h' or '|Hitem:100:0|h[Candidate]|h'
end
function PickupContainerItem(bag,slot)
 assert(bag==0 and slot==1);pickups=pickups+1
 if scenario=='pickup-refused' then return end
 SetCursor(nil);SetCursor('item',100,200)
 if scenario=='reselected' then SetCursor(nil);SetCursor('item',100,200) end
 if scenario=='foreign-copy' then SetCursor(nil);SetCursor('item',100,201) end
 if scenario=='virtual-cursor' then SetCursor('item',100,0,7) end
 if scenario=='lost-observer' then _G.CopilotBuddy_EquipCursorFrame=nil end
end
function ClearCursor() clears=clears+1;SetCursor(nil) end
function GetItemInfo(id) assert(id==100);if cacheReady then return 'Candidate' end end
function CursorCanGoInSlot(slot) assert(slot==16 or slot==0);return true end
function IsInventoryItemLocked(slot) assert(slot==16 or slot==0);return false end
function StaticPopup_FindVisible(which) if popup and popup.which==which then return popup end end
function MakePopup(index)
 local p={which='AUTOEQUIP_BIND',data=index}
 p.button1={Click=function() confirms=confirms+1;equippedGuid=nativeGuid;popup=nil;SetCursor(nil) end}
 return p
end
if scenario=='preexisting-popup' then popup=MakePopup(16) end
function EquipItemByName(id)
 assert(tonumber(id)==100);byNames=byNames+1;equippedGuid=201;SetCursor(nil)
end
function EquipCursorItem(slot)
 assert(slot==16 or slot==0);explicitSubmits=explicitSubmits+1;equippedGuid=nativeGuid;SetCursor(nil)
end
function AutoEquipCursorItem()
 autoSubmits=autoSubmits+1;assert(nativeKind==1 and nativeGuid==200)
 if scenario=='late-bind' then return end
 if scenario=='bind-zero' or scenario=='bind-seven' or scenario=='wrong-popup-data' or scenario=='two-bind-events'
  or scenario=='missing-button' or scenario=='cursor-changed-bind' then
  local index=scenario=='bind-seven' and 7 or 0;popup=MakePopup(index)
  if scenario=='missing-button' then popup.button1=nil end
  Fire('AUTOEQUIP_BIND_CONFIRM',index)
  if scenario=='wrong-popup-data' then popup.data=16 end
  if scenario=='two-bind-events' then Fire('AUTOEQUIP_BIND_CONFIRM',index) end
  if scenario=='cursor-changed-bind' then Fire('CURSOR_UPDATE') end
 else equippedGuid=nativeGuid;SetCursor(nil) end
end
""";
}
