using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Build12340: pickup selects/locks an item still in its source slot; equip sends
// a complete slot swap and clears that selection. 0x513740 locks an item, it
// does not put the old equipment on the cursor. Actual tracked helpers execute
// through stock Lua5.1 below. Physical dispatch is a controlled boundary; the
// separate generated-instruction tests verify the existing full GUID guard.
internal static class EquipmentCleanupOwnershipRegressionTests
{
    private const string Token = "0123456789abcdef0123456789abcdef";
    private sealed class Failure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        string source = File.ReadAllText(Path.Combine(root, "Styx/WoWInternals/Lua.cs"));
        string directory = Path.Combine(Path.GetTempPath(), "cb-equipment-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        int passed = 0, assertions = 0, unexpected = 0, total = 0;
        try
        {
            RewardLua51Boundary.WriteManagedBridge(directory, source);
            string helpers = Methods(source, "BeginEquipCursorOwnership", "BuildEquipCursorOwnershipLua",
                "TryEquipCursorItem", "BuildEquipCursorSubmissionLua", "TryCancelEquipCursorItem", "BuildCancelEquipCursorLua");
            string owners = "";
            foreach (var owner in new[] {
                (Path:"runtime-snapshot/Quest Behaviors/EquipItem.cs", Name:"QuestProbe"),
                (Path:"runtime-snapshot/Plugins/AutoEquip2/AutoEquip.cs", Name:"AutoProbe") })
                owners += "public sealed class " + owner.Name + " {\n" + OwnerFields +
                    Methods(File.ReadAllText(Path.Combine(root, owner.Path)), "ReturnDisplacedCursorToSource",
                        "RestoreOwnedCursorToSource", "CursorHasAnyItem", "SubmitOwnedCursorEquip") + OwnerCalls + "}\n";
            string pickup = Methods(File.ReadAllText(Path.Combine(root, "Styx/WoWInternals/WoWObjects/WoWItem.cs")),
                "BuildValidatedContainerPickupLua");
            File.WriteAllText(Path.Combine(directory, "Probe.cs"), Prefix + helpers + "}\n" + owners +
                "public static class PickupProbe {\n" + pickup + "public static string Script()=>BuildValidatedContainerPickupLua(0,1,100);}\n");
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { directory })!;
            var compiled = (CompilerResults)compilerType.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = compiled.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException(string.Join("; ", errors.Select(e => e.ToString())));
            var assembly = (Assembly)compilerType.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            var bridge = assembly.GetType("RewardRecordedBridge", true)!;
            var observe = bridge.GetField("Observe")!;
            var loadSize = (Func<string, uint>)bridge.GetMethod("LoadSize")!.CreateDelegate(typeof(Func<string, uint>));
            var api = assembly.GetType("Lua", true)!;
            var begin = api.GetMethod("BeginEquipCursorOwnership");
            string pickupScript = (string)assembly.GetType("PickupProbe", true)!.GetMethod("Script")!.Invoke(null, null)!;
            using var lua = new RewardLua51Boundary.StockLua51(root);
            void Case(string name, Action test)
            {
                total++;
                try { test(); passed++; Console.WriteLine("PASS equipment cleanup: " + name); }
                catch (Failure e) { assertions++; Console.Error.WriteLine("FAIL equipment cleanup: " + name + ": " + e.Message); }
                catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR equipment cleanup: " + name + ": " + e); }
                finally { observe.SetValue(null, null); }
            }
            foreach (string owner in new[] { "QuestProbe", "AutoProbe" })
            foreach (var c in new[] {
                (Mode:"return-empty", Result:true, Clears:0, Submits:0, Confirms:0),
                (Mode:"return-foreign-different", Result:false, Clears:0, Submits:0, Confirms:0),
                (Mode:"return-foreign-same", Result:false, Clears:0, Submits:0, Confirms:0),
                (Mode:"return-spell", Result:false, Clears:0, Submits:0, Confirms:0),
                (Mode:"timeout-source-occupied", Result:false, Clears:1, Submits:0, Confirms:0),
                (Mode:"timeout-source-empty", Result:false, Clears:1, Submits:0, Confirms:0),
                (Mode:"timeout-foreign-same", Result:false, Clears:0, Submits:0, Confirms:0),
                (Mode:"timeout-foreign-different", Result:false, Clears:0, Submits:0, Confirms:0),
                (Mode:"timeout-reselected", Result:false, Clears:0, Submits:0, Confirms:0),
                (Mode:"timeout-virtual", Result:false, Clears:0, Submits:0, Confirms:0),
                (Mode:"timeout-empty", Result:false, Clears:0, Submits:0, Confirms:0),
                (Mode:"timeout-revoked", Result:false, Clears:0, Submits:0, Confirms:0),
                (Mode:"timeout-lost-observer", Result:false, Clears:0, Submits:0, Confirms:0),
                (Mode:"timeout-missing-owner", Result:false, Clears:0, Submits:0, Confirms:0),
                (Mode:"timeout-native-guid-changed", Result:false, Clears:0, Submits:0, Confirms:0),
                (Mode:"timeout-repeat", Result:false, Clears:1, Submits:0, Confirms:0),
                (Mode:"submit-normal", Result:true, Clears:0, Submits:1, Confirms:0),
                (Mode:"submit-bind", Result:true, Clears:0, Submits:1, Confirms:1),
                (Mode:"submit-cache-missing", Result:false, Clears:1, Submits:0, Confirms:0),
                (Mode:"submit-reselected", Result:false, Clears:0, Submits:0, Confirms:0),
                (Mode:"submit-foreign-same", Result:false, Clears:0, Submits:0, Confirms:0),
                (Mode:"submit-lost-observer", Result:false, Clears:0, Submits:0, Confirms:0),
                (Mode:"submit-missing-transition", Result:false, Clears:0, Submits:0, Confirms:0),
                (Mode:"submit-extra-transition", Result:false, Clears:0, Submits:0, Confirms:0) })
            {
                Case(owner + "/" + c.Mode, () =>
                {
                    using var session = lua.BeginSession("scenario='" + c.Mode + "'\n" + Setup);
                    RewardLua51Boundary.Observation Execute(string script)
                    {
                        var result = session.Execute(script, loadSize(script));
                        if (result.Load != 0 || result.Call != 0)
                            throw new InvalidOperationException("Lua boundary: " + result.Error);
                        return result;
                    }
                    void RefreshPhysical()
                    {
                        var physical = Execute("return nativeKind,nativeGuid").Values;
                        api.GetField("CursorKind")!.SetValue(null, uint.Parse(physical[0]));
                        api.GetField("CursorGuid")!.SetValue(null, ulong.Parse(physical[1]));
                    }
                    api.GetField("LastExpectedGuid")!.SetValue(null, 0UL);
                    observe.SetValue(null, new Func<string, List<string>>(script =>
                    {
                        var result = Execute(script);
                        RefreshPhysical();
                        return result.Values;
                    }));
                    if (begin != null)
                        Check((bool)begin.Invoke(null, new object[] { 100U, Token })!, "empty-cursor ownership setup refused");
                    Check(Execute(pickupScript).Values.SequenceEqual(new[] { "1" }), "controlled native pickup failed");
                    Execute("moves=0;clears=0;submits=0;confirms=0;PrepareScenario()");
                    RefreshPhysical();
                    var type = assembly.GetType(owner, true)!;
                    object instance = Activator.CreateInstance(type)!;
                    if (c.Mode == "timeout-revoked") type.GetField("Context")!.SetValue(instance, false);
                    if (c.Mode == "timeout-missing-owner") type.GetMethod("ForgetOwner")!.Invoke(instance, null);
                    if (c.Mode.StartsWith("return-", StringComparison.Ordinal))
                        Check((bool)type.GetMethod("Return")!.Invoke(instance, null)! == c.Result, "cursor release observation disagreed");
                    else if (c.Mode.StartsWith("submit-", StringComparison.Ordinal))
                    {
                        Check((bool)type.GetMethod("Submit")!.Invoke(instance, null)! == c.Result, "stale lifetime submitted, or valid equip was suppressed");
                        if (c.Mode == "submit-cache-missing") type.GetMethod("Timeout")!.Invoke(instance, null);
                        if (c.Result) Check((bool)type.GetMethod("Return")!.Invoke(instance, null)!, "normal swap lost cursor-release completion");
                    }
                    else
                    {
                        type.GetMethod("Timeout")!.Invoke(instance, null);
                        if (c.Mode == "timeout-repeat") type.GetMethod("Timeout")!.Invoke(instance, null);
                    }
                    var counts = Execute("return moves,clears,submits,confirms").Values;
                    Check(counts.SequenceEqual(new[] { "0", c.Clears.ToString(), c.Submits.ToString(), c.Confirms.ToString() }),
                        "bag moves/clears/submits/confirms=" + string.Join("/", counts));
                    if (c.Clears != 0 || c.Submits != 0)
                        Check((ulong)api.GetField("LastExpectedGuid")!.GetValue(null)! == 200UL,
                            "mutation did not use the physical GUID dispatch boundary");
                    if (c.Clears != 0 || c.Result && c.Submits != 0)
                        Check(Execute("return ActiveObservers()").Values.SequenceEqual(new[] { "0" }),
                            "finished cursor lifetime left an active observer");
                });
            }
            Case("owned cleanup and pre-pickup lifetime APIs exist", () =>
                Check(begin != null && api.GetMethod("TryCancelEquipCursorItem") != null,
                    "no physical-GUID-and-lifetime cancellation API; entry/source-slot cleanup is insufficient"));
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine($"Equipment cleanup ownership: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; actual tracked owners/generated Lua5.1; controlled build12340 cursor transitions and physical dispatch admission; no game/server/native execution.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Equipment cleanup ownership regression");
    }

    private static string Methods(string source, params string[] names) => string.Join("\n",
        CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(m => names.Contains(m.Identifier.ValueText)).Select(m => m.ToFullString()));
    private static void Check(bool value, string why) { if (!value) throw new Failure(why); }
    private static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CopilotBuddy.csproj"))) return d.FullName;
        throw new InvalidOperationException("Tracked checkout required");
    }
    private const string Prefix = """
using System; using System.Collections.Generic; using System.Globalization;
public enum InventorySlot { None=0, MainHandSlot=16 }
public static class Logging { public static void WriteDebug(string format,params object[] args){} }
public static class Lua {
 public static uint CursorKind;
 public static ulong CursorGuid,LastExpectedGuid;
 public static T GetReturnVal<T>(string script,uint index)=>RewardRecordedBridge.GetReturnVal<T>(script,index);
 public static List<string> GetReturnValues(string script)=>RewardRecordedBridge.GetReturnValues(script);
 public static List<string> GetReturnValuesCore(string script,string name,ulong guid) {
  LastExpectedGuid=guid;
  if(guid==0 || CursorKind!=1 || CursorGuid!=guid)return new List<string>();
  return RewardRecordedBridge.GetReturnValues(script);
 }
 public static void DoString(string format,params object[] args)=>RewardRecordedBridge.GetReturnValues(args.Length==0?format:string.Format(format,args));
""";
    private const string OwnerFields = """
 private ulong _pendingEquipGuid=200;
 private uint _pendingEquipEntry=100;
 private InventorySlot _pendingEquipSlot=InventorySlot.MainHandSlot;
 private int _pendingSourceBag=0,_pendingSourceSlot=1;
 private string _pendingCursorOwner="0123456789abcdef0123456789abcdef";
 public bool Context=true;
 private bool HasPendingEquip=>true;
 private bool OwnsPendingEquipContext()=>Context;
 private void LogDebug(string format,params object[] args){}
 private void LogMessage(string level,string format,params object[] args){}
""";
    private const string OwnerCalls = """
 public bool Return()=>ReturnDisplacedCursorToSource();
 public void Timeout()=>RestoreOwnedCursorToSource();
 public bool Submit()=>SubmitOwnedCursorEquip();
 public void ForgetOwner(){_pendingCursorOwner=null;_pendingSourceBag=-1;_pendingSourceSlot=-1;}
""";
    private const string Setup = """
clicks=0;moves=0;clears=0;submits=0;confirms=0
nativeKind=0;nativeGuid=0
local kind,entry=nil,nil
local sourceLink='|Hitem:100:0|h[Original]|h'
local frames={}
local popup=nil
function CreateFrame()
 local f={events={}}
 function f:RegisterEvent(e) self.events[e]=true end
 function f:UnregisterAllEvents() self.events={} end
 function f:SetScript(e,fn) self.fn=fn end
 function f:GetScript(e) return self.fn end
 frames[#frames+1]=f;return f
end
function ActiveObservers()
 local n=0;for _,f in ipairs(frames) do for _ in pairs(f.events) do n=n+1 end end;return n
end
function Fire(e,arg)
 for _,f in ipairs(frames) do if f.events[e] and f.fn then f.fn(f,e,arg) end end
end
function SetCursor(k,id,guid,physicalKind)
 kind,entry=k,id;nativeKind=physicalKind or (k=='item' and 1 or 0);nativeGuid=guid or 0
 Fire('CURSOR_UPDATE')
end
function GetCursorInfo() return kind,entry end
function CursorHasItem() return kind=='item' end
function GetContainerItemLink(bag,slot) assert(bag==0 and slot==1);return sourceLink end
function PickupContainerItem(bag,slot)
 assert(bag==0 and slot==1)
 if kind then moves=moves+1;SetCursor(nil);return end
 if scenario~='submit-missing-transition' then SetCursor(nil) end
 SetCursor('item',100,200)
 if scenario=='submit-extra-transition' then Fire('CURSOR_UPDATE') end
end
function ClearCursor() clears=clears+1;SetCursor(nil) end
function GetItemInfo(id) assert(id==100);if scenario~='submit-cache-missing' then return 'Original' end end
function CursorCanGoInSlot(slot) assert(slot==16);return true end
function IsInventoryItemLocked(slot) assert(slot==16);return false end
function StaticPopup_FindVisible(which) if popup and popup.which==which then return popup end end
function EquipCursorItem(slot)
 assert(slot==16);submits=submits+1
 if scenario=='submit-bind' then
  popup={which='EQUIP_BIND',data=0}
  popup.button1={Click=function() confirms=confirms+1;popup=nil;SetCursor(nil) end}
  Fire('EQUIP_BIND_CONFIRM',0)
 else SetCursor(nil) end
end
function PrepareScenario()
 if scenario~='timeout-source-occupied' and scenario~='timeout-repeat' and scenario~='submit-cache-missing' then sourceLink=nil end
 if scenario=='return-empty' or scenario=='timeout-empty' then SetCursor(nil)
 elseif string.find(scenario,'foreign%-same') then SetCursor(nil);SetCursor('item',100,201)
 elseif string.find(scenario,'foreign%-different') then SetCursor(nil);SetCursor('item',999,201)
 elseif string.find(scenario,'reselected') then SetCursor(nil);SetCursor('item',100,200)
 elseif scenario=='return-spell' then SetCursor('spell',99,0)
 elseif scenario=='timeout-virtual' then SetCursor('item',100,0,7)
 elseif scenario=='timeout-native-guid-changed' then nativeGuid=201
 elseif string.find(scenario,'lost%-observer') then
  local f=_G.CopilotBuddy_EquipCursorFrame
  if f then f:UnregisterAllEvents();f:SetScript('OnEvent',nil) end
  _G.CopilotBuddy_EquipCursorFrame=nil
 end
end
""";
}
