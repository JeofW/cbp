using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// The old and repaired owners are exercised through the same stock Lua 5.1
// boundary. Native GUID dispatch is deliberately not simulated by this fixture.
internal static class EquipmentPendingIndexRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        string luaSource = File.ReadAllText(Path.Combine(root, "Styx/WoWInternals/Lua.cs"));
        string directory = Path.Combine(Path.GetTempPath(), "cb-pending-index-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        int passed = 0, failures = 0, unexpected = 0, total = 0;
        try
        {
            RewardLua51Boundary.WriteManagedBridge(directory, luaSource);
            var luaMethods = Methods(luaSource);
            string helpers = string.Join("\n", luaMethods.Where(m => m.Identifier.ValueText is
                "TryEquipCursorItem" or "BuildEquipCursorSubmissionLua").Select(m => m.ToFullString()));
            string owners = "";
            foreach (var owner in new[] {
                (Path:"runtime-snapshot/Quest Behaviors/EquipItem.cs", Name:"QuestProbe"),
                (Path:"runtime-snapshot/Plugins/AutoEquip2/AutoEquip.cs", Name:"AutoProbe") })
            {
                var methods = Methods(File.ReadAllText(Path.Combine(root, owner.Path)));
                string submit = methods.Single(m => m.Identifier.ValueText == "SubmitOwnedCursorEquip").ToFullString();
                var confirm = methods.SingleOrDefault(m => m.Identifier.ValueText == "ConfirmOwnedEquipPopup");
                owners += "public sealed class " + owner.Name + " {\n" + Fields + submit +
                    (confirm?.ToFullString() ?? "private void ConfirmOwnedEquipPopup() {}") +
                    "public bool Submit()=>SubmitOwnedCursorEquip(); public void Later()=>ConfirmOwnedEquipPopup(); }\n";
            }
            File.WriteAllText(Path.Combine(directory, "Probe.cs"), Prefix + helpers + "}\n" + owners);
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { directory })!;
            var result = (CompilerResults)compilerType.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException(string.Join("; ", errors.Select(e => e.ToString())));
            var assembly = (Assembly)compilerType.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            var observe = assembly.GetType("RewardRecordedBridge", true)!.GetField("Observe")!;
            using var lua = new RewardLua51Boundary.StockLua51(root);
            foreach (string owner in new[] { "QuestProbe", "AutoProbe" })
            foreach (var c in new[] {
                (Mode:"index-zero", Submits:1, Confirms:1),
                (Mode:"index-seven", Submits:1, Confirms:1),
                (Mode:"auto-index-zero", Submits:1, Confirms:1),
                (Mode:"no-bind", Submits:1, Confirms:0),
                (Mode:"preexisting", Submits:0, Confirms:0),
                (Mode:"two-events", Submits:1, Confirms:0),
                (Mode:"cursor-changed", Submits:1, Confirms:0),
                (Mode:"wrong-popup-data", Submits:1, Confirms:0),
                (Mode:"missing-button", Submits:1, Confirms:0),
                (Mode:"late-event", Submits:1, Confirms:0),
                (Mode:"submit-error", Submits:1, Confirms:0) })
            {
                total++;
                try
                {
                    using var session = lua.BeginSession("scenario='" + c.Mode + "'\n" + Setup);
                    RewardLua51Boundary.Observation Execute(string script)
                    {
                        var observation = session.Execute(script, checked((uint)Encoding.UTF8.GetByteCount(script)));
                        if (observation.Load != 0 || observation.Call != 0)
                            throw new InvalidOperationException("Lua error: " + observation.Error);
                        return observation;
                    }
                    observe.SetValue(null, new Func<string, List<string>>(script => Execute(script).Values));
                    object instance = Activator.CreateInstance(assembly.GetType(owner, true)!)!;
                    instance.GetType().GetMethod("Submit")!.Invoke(instance, null);
                    var after = Execute("return submits,confirms").Values;
                    Check(after.SequenceEqual(new[] { c.Submits.ToString(), c.Confirms.ToString() }),
                        "submission/confirmation=" + string.Join("/", after));
                    // A same-slot foreign popup appears after the owned dispatch.
                    Execute("popup=MakePopup(16); Fire('EQUIP_BIND_CONFIRM',16)");
                    instance.GetType().GetMethod("Later")!.Invoke(instance, null);
                    Check(Execute("return confirms").Values.SequenceEqual(new[] { c.Confirms.ToString() }),
                        "later foreign or reused pending index acquired ownership");
                    Check(Execute("return ActiveObservers()").Values.SequenceEqual(new[] { "0" }),
                        "temporary observer remained registered after submission");
                    passed++;
                }
                catch (Failure e) { failures++; Console.Error.WriteLine("FAIL pending index " + owner + "/" + c.Mode + ": " + e.Message); }
                catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR pending index " + owner + "/" + c.Mode + ": " + e); }
                finally { observe.SetValue(null, null); }
            }
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine($"Equipment pending-index scenarios: {passed}/{total}; assertions={failures}; unexpected={unexpected}; tracked owner/generated Lua; stock Lua5.1; controlled synchronous events; no native client proof.");
        if (failures + unexpected != 0) throw new InvalidOperationException("Equipment pending-index regression");
    }

    private static MethodDeclarationSyntax[] Methods(string source) => CSharpSyntaxTree.ParseText(source).GetRoot()
        .DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
    private static void Check(bool condition, string reason) { if (!condition) throw new Failure(reason); }
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
 public static T GetReturnVal<T>(string script,uint index)=>RewardRecordedBridge.GetReturnVal<T>(script,index);
 public static List<string> GetReturnValuesCore(string script,string name,ulong guid)=>RewardRecordedBridge.GetReturnValues(script);
 public static void DoString(string script)=>RewardRecordedBridge.GetReturnValues(script);
""";
    private const string Fields = """
 private ulong _pendingEquipGuid=200;
 private uint _pendingEquipEntry=100;
 private InventorySlot _pendingEquipSlot=InventorySlot.MainHandSlot;
 private bool _pendingEquipSubmitted=true;
 private bool HasPendingEquip=>true;
 private bool OwnsPendingEquipContext()=>true;
 private void LogDebug(string format,params object[] args){}
 private void LogMessage(string level,string format,params object[] args){}
""";
    private const string Setup = """
clicks=0;submits=0;confirms=0;popup=nil
local frames={}
function CreateFrame()
 local f={events={}}
 function f:RegisterEvent(e) self.events[e]=true end
 function f:UnregisterAllEvents() self.events={} end
 function f:SetScript(e,fn) self.fn=fn end
 frames[#frames+1]=f;return f
end
function ActiveObservers()
 local n=0;for _,f in ipairs(frames) do for _ in pairs(f.events) do n=n+1 end end;return n
end
function Fire(e,arg)
 for _,f in ipairs(frames) do if f.events[e] and f.fn then f.fn(f,e,arg) end end
end
function MakePopup(index)
 local p={data=index,which='EQUIP_BIND'}
 p.button1={Click=function() confirms=confirms+1;clicks=clicks+1;popup=nil;Fire('CURSOR_UPDATE') end}
 return p
end
if scenario=='preexisting' then popup=MakePopup(16) end
function StaticPopup_FindVisible(kind) if popup and popup.which==kind then return popup end end
function GetCursorInfo() return 'item',100 end
function CursorHasItem() return true end
function CursorCanGoInSlot(slot) assert(slot==16);return true end
function IsInventoryItemLocked(slot) assert(slot==16);return false end
function EquipCursorItem(slot)
 assert(slot==16);submits=submits+1
 if scenario=='submit-error' then error('controlled submission failure') end
 if scenario=='no-bind' or scenario=='late-event' then return end
 local index=scenario=='index-seven' and 7 or 0
 popup=MakePopup(index)
 local event='EQUIP_BIND_CONFIRM'
 if scenario=='auto-index-zero' then popup.which='AUTOEQUIP_BIND';event='AUTOEQUIP_BIND_CONFIRM' end
 if scenario=='missing-button' then popup.button1=nil end
 Fire(event,index)
 if scenario=='two-events' then popup=MakePopup(index);Fire(event,index) end
 if scenario=='cursor-changed' then Fire('CURSOR_UPDATE') end
 if scenario=='wrong-popup-data' then popup.data=16 end
end
""";
}
