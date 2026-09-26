using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Actual tracked pickup owner/helpers and equipment helpers; actual generated
// Lua5.1 requests, loader byte count and managed conversion. Controlled container,
// cursor and equipment APIs are not physical identity or server acknowledgement.
internal static class EquipmentLua51BoundaryRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        string directory = Path.Combine(Path.GetTempPath(), "cb-equipment-lua-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            RewardLua51Boundary.WriteManagedBridge(directory, File.ReadAllText(Path.Combine(root, "Styx/WoWInternals/Lua.cs")));
            string item = File.ReadAllText(Path.Combine(root, "Styx/WoWInternals/WoWObjects/WoWItem.cs"));
            string pickup = Methods(item, "TryResolveContainerLocation", "IsContainerLocationCurrent", "BuildValidatedContainerPickupLua") +
                Method(item, "TryPickUp", 2);
            string owners = "";
            foreach (var source in new[] {
                (Path: "runtime-snapshot/Quest Behaviors/EquipItem.cs", Name: "QuestEquipProbe"),
                (Path: "runtime-snapshot/Plugins/AutoEquip2/AutoEquip.cs", Name: "AutoEquipProbe") })
                owners += "public sealed class " + source.Name + " {\n" + OwnerFields +
                    Methods(File.ReadAllText(Path.Combine(root, source.Path)), "SubmitOwnedCursorEquip", "ReturnDisplacedCursorToSource", "CursorHasAnyItem") + OwnerCalls + "}\n";
            string equipmentBridge = "public static partial class Lua {\n" +
                Methods(File.ReadAllText(Path.Combine(root, "Styx/WoWInternals/Lua.cs")),
                    "TryEquipCursorItem", "BuildEquipCursorSubmissionLua") + "}\n";
            File.WriteAllText(Path.Combine(directory, "Probe.cs"), Prefix + pickup + "}\n" + owners + equipmentBridge);
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { directory })!;
            var result = (CompilerResults)compilerType.GetMethod("Compile")!.Invoke(compiler, null)!;
            string[] errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).Select(e => e.ToString()).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Equipment Lua probe compile: " + string.Join("; ", errors));
            var assembly = (Assembly)compilerType.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            Execute(assembly, root);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void Execute(Assembly probe, string root)
    {
        using var lua = new RewardLua51Boundary.StockLua51(root);
        Type bridge = probe.GetType("RewardRecordedBridge", true)!;
        FieldInfo observe = bridge.GetField("Observe")!;
        var loadSize = (Func<string, uint>)bridge.GetMethod("LoadSize")!.CreateDelegate(typeof(Func<string, uint>));
        int passed = 0, assertions = 0, unexpected = 0, total = 0;
        void Case(string label, Action test)
        {
            total++;
            try { test(); passed++; Console.WriteLine("PASS equipment Lua51: " + label); }
            catch (Failure e) { assertions++; Console.Error.WriteLine("FAIL equipment Lua51: " + label + ": " + e.Message); }
            catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR equipment Lua51: " + label + ": " + e); }
            finally { observe.SetValue(null, null); }
        }
        void Scenario(string ownerName, string operation, string mode, bool expected, int mutations, string transport = "actual")
        {
            Case(ownerName + "/" + operation + "/" + mode + "/" + transport, () =>
            {
                RewardLua51Boundary.Observation? observation = null;
                int requests = 0;
                observe.SetValue(null, new Func<string, List<string>>(script =>
                {
                    requests++;
                    // Even fault injection uses actual Lua C conversion; no configured
                    // C# boolean replaces a Lua response. Preserve the request length.
                    string executed = transport switch {
                        "missing" => "return", "nil" => "return nil", "malformed" => "return 'garbage'",
                        "raw-boolean" => "return true", "error" => "error('controlled transport failure')",
                        _ => script
                    };
                    observation = lua.Execute(executed, loadSize(executed), mode, new[] { "", "" }, Setup);
                    return observation.Values;
                }));
                Type owner = probe.GetType(ownerName, true)!;
                object instance = Activator.CreateInstance(owner)!;
                object[] args = operation == "pickup" ? new object[] { 0, 0 } : new object[] { operation };
                bool actual = (bool)owner.GetMethod(operation == "pickup" ? "TryPickUp" : "Request")!.Invoke(instance, args)!;
                Check(requests == 1 && observation != null, "tracked owner did not issue one Lua request");
                Check(observation!.Load == 0 && (transport == "error" ? observation.Call != 0 : observation.Call == 0),
                    "unexpected Lua load/call failure: " + observation.Error);
                Check(observation.Clicks == mutations, "mutation count=" + observation.Clicks + " expected=" + mutations);
                Check(actual == expected, "managed result=" + actual + " expected=" + expected + "; values=" + string.Join("|", observation.Values));
                if (operation == "pickup")
                    Check((int)args[0] == (expected ? 0 : -1) && (int)args[1] == (expected ? 1 : -1), "pickup coordinates disagree with receipt");
            });
        }
        foreach (var c in new[] {
            (Mode:"pickup", Result:true, Mutations:1), (Mode:"pickup-held", Result:false, Mutations:0),
            (Mode:"pickup-spell", Result:false, Mutations:0), (Mode:"pickup-wrong-source", Result:false, Mutations:0),
            (Mode:"pickup-refused", Result:false, Mutations:1), (Mode:"pickup-wrong-result", Result:false, Mutations:1) })
            Scenario("PickupProbe", "pickup", c.Mode, c.Result, c.Mutations);

        foreach (string owner in new[] { "QuestEquipProbe", "AutoEquipProbe" })
        {
            foreach (var c in new[] {
                (Mode:"submit", Result:true, Mutations:1), (Mode:"submit-delayed", Result:true, Mutations:1),
                (Mode:"submit-empty", Result:false, Mutations:0), (Mode:"submit-foreign", Result:false, Mutations:0),
                (Mode:"submit-wrong-slot", Result:false, Mutations:0), (Mode:"submit-locked", Result:false, Mutations:0) })
                Scenario(owner, "submit", c.Mode, c.Result, c.Mutations);
            foreach (var c in new[] {
                (Mode:"return-empty", Result:true, Mutations:0), (Mode:"return-displaced", Result:true, Mutations:1),
                (Mode:"return-refused", Result:false, Mutations:1), (Mode:"return-pending", Result:false, Mutations:0),
                (Mode:"return-spell", Result:false, Mutations:0), (Mode:"return-occupied", Result:false, Mutations:0) })
                Scenario(owner, "return", c.Mode, c.Result, c.Mutations);
            Scenario(owner, "busy", "query-held", true, 0);
            Scenario(owner, "busy", "query-empty", false, 0);
            Scenario(owner, "no-source", "query-held", false, 0);
            Scenario(owner, "no-source", "query-empty", true, 0);
            foreach (string transport in new[] { "missing", "nil", "malformed", "raw-boolean", "error" })
            {
                Scenario(owner, "submit", "submit", false, 0, transport);
                Scenario(owner, "return", "return-empty", false, 0, transport);
                Scenario(owner, "busy", "query-empty", true, 0, transport);
                Scenario(owner, "no-source", "query-empty", false, 0, transport);
            }
        }
        foreach (string transport in new[] { "missing", "nil", "malformed", "raw-boolean", "error" })
            Scenario("PickupProbe", "pickup", "pickup", false, 0, transport);
        Console.WriteLine($"Equipment Lua51 boundary scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; tracked pickup/equip helpers, actual generated Lua5.1, loader size and verbatim managed conversion; controlled APIs; no physical GUID/client/server proof.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Equipment Lua51 boundary regression");
    }

    private static string Methods(string source, params string[] names) => string.Join("\n", names.Select(n => Method(source, n)));
    private static string Method(string source, string name, int? parameterCount = null)
    {
        return CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == name && (!parameterCount.HasValue || m.ParameterList.Parameters.Count == parameterCount)).ToFullString();
    }
    private static void Check(bool value, string why) { if (!value) throw new Failure(why); }
    private static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CopilotBuddy.csproj"))) return d.FullName;
        throw new InvalidOperationException("Tracked checkout required");
    }
    private const string Prefix = """
using System;
public enum InventorySlot { None=0, HeadSlot=1 }
public sealed class WoWContainer { public ulong[] ItemGuids=new ulong[]{200}; }
public sealed class Inventory { public WoWContainer Backpack=new WoWContainer(); }
public sealed class LocalPlayer { public Inventory Inventory=new Inventory();public WoWContainer GetBagAtIndex(uint index)=>null; }
public static class StyxWoW { public static LocalPlayer Me=new LocalPlayer(); }
public static class Logging { public static void WriteDebug(string format,params object[] args){} }
public static partial class Lua {
 public static T GetReturnVal<T>(string script,uint index)=>RewardRecordedBridge.GetReturnVal<T>(script,index);
 public static System.Collections.Generic.List<string> GetReturnValuesCore(string script,string name,ulong guid)=>RewardRecordedBridge.GetReturnValues(script);
}
public sealed class PickupProbe
{
    public ulong Guid=>200;public uint Entry=>100;public bool IsValid=>true;public string Name=>"Probe";
""";
    private const string OwnerFields = """
    private uint _pendingEquipEntry=100;
    private ulong _pendingEquipGuid=200;
    private InventorySlot _pendingEquipSlot=InventorySlot.HeadSlot;
    private int _pendingSourceBag=0,_pendingSourceSlot=1;
    private bool HasPendingEquip=>true;
    private bool OwnsPendingEquipContext()=>true;
    private void LogDebug(string format,params object[] args){}
    private void LogMessage(string level,string format,params object[] args){}
""";
    private const string OwnerCalls = """
    public bool Request(string operation)
    {
        if(operation=="submit")return SubmitOwnedCursorEquip();
        if(operation=="busy")return CursorHasAnyItem();
        if(operation=="no-source"){_pendingSourceBag=-1;_pendingSourceSlot=-1;}
        return ReturnDisplacedCursorToSource();
    }
""";
    private const string Setup = """
clicks=0
function StaticPopup_FindVisible(kind) return nil end
function GetItemInfo(entry) assert(entry==100);return 'item' end
function CreateFrame()
 return {RegisterEvent=function() end,UnregisterAllEvents=function() end,SetScript=function() end}
end
local kind,id=nil,nil
local link=nil
if string.sub(scenario,1,6)=='pickup' then
 link='|Hitem:100:0|h[Probe]|h'
 if scenario=='pickup-wrong-source' then link='|Hitem:999:0|h[Other]|h' end
 if scenario=='pickup-held' then kind,id='item',999 end
 if scenario=='pickup-spell' then kind,id='spell',999 end
elseif string.sub(scenario,1,6)=='submit' then
 kind,id='item',100
 if scenario=='submit-empty' then kind,id=nil,nil end
 if scenario=='submit-foreign' then kind,id='item',999 end
elseif scenario=='query-held' or scenario=='return-displaced' or scenario=='return-refused' or scenario=='return-occupied' then kind,id='item',99
elseif scenario=='return-pending' then kind,id='item',100
elseif scenario=='return-spell' then kind,id='spell',99 end
if scenario=='return-occupied' then link='|Hitem:333:0|h[Occupied]|h' end
function GetCursorInfo() return kind,id end
function CursorHasItem() return kind=='item' end
function GetContainerItemLink(bag,slot) assert(bag==0 and slot==1);return link end
function CursorCanGoInSlot(slot) assert(slot==1);return scenario~='submit-wrong-slot' end
function IsInventoryItemLocked(slot) assert(slot==1);return scenario=='submit-locked' end
function EquipCursorItem(slot) assert(slot==1);clicks=clicks+1;if scenario~='submit-delayed' then kind,id=nil,nil end end
function PickupContainerItem(bag,slot)
 assert(bag==0 and slot==1);clicks=clicks+1
 if scenario=='pickup-refused' or scenario=='return-refused' then return end
 if string.sub(scenario,1,6)=='pickup' then kind,id='item',scenario=='pickup-wrong-result' and 999 or 100;link=nil
 else kind,id=nil,nil end
end
""";
}
