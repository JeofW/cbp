using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Complete UseItemOn plus actual container use/quest-info methods and MIR's
// quest protection predicate. Stock Lua5.1 + real converters; controlled world.
internal static class ContainerLua51BoundaryRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }
    private const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        string luaSource = File.ReadAllText(Path.Combine(root, "Styx/WoWInternals/Lua.cs"));
        string itemSource = File.ReadAllText(Path.Combine(root, "Styx/WoWInternals/WoWObjects/WoWItem.cs"));
        string methods = Methods(itemSource, "TryUseContainerItem", "TryGetContainerItemQuestInfo", "TryResolveContainerLocation",
            "IsContainerLocationCurrent", "BuildValidatedContainerUseLua", "BuildValidatedContainerQuestInfoLua");
        string boundary = (string)typeof(QuestItemTargetSelectionRegressionTests).GetField("Boundary", Hidden)!.GetRawConstantValue()!;
        void Replace(string before, string after)
        {
            if (boundary.Split(new[] { before }, StringSplitOptions.None).Length != 2) throw new InvalidOperationException("Controlled fixture shape changed: " + before);
            boundary = boundary.Replace(before, after);
        }
        Replace("public bool TryUseContainerItem()=>true;", methods);
        Replace("public bool IsMoving{get;set;}", "public ContainerInventory Inventory=new();public WoWContainer GetBagAtIndex(uint index)=>null;public bool IsMoving{get;set;}");
        Replace("public static List<WoWObject>? Objects{get;set;}=new();", "public static List<WoWObject>? Objects{get;set;}=new();public static T? GetObjectByGuid<T>(ulong id)where T:WoWObject=>Objects?.OfType<T>().FirstOrDefault(o=>o.Guid==id);");
        string guard = Methods(File.ReadAllText(Path.Combine(root, "runtime-snapshot/Plugins/MrItemRemover2/Methods.cs")), "IsQuestItem");
        string directory = Path.Combine(Path.GetTempPath(), "cb-container-lua-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        bool oldLogging = Styx.Helpers.Logging.FileLogging;
        try
        {
            Styx.Helpers.Logging.FileLogging = false;
            RewardLua51Boundary.WriteManagedBridge(directory, luaSource);
            File.WriteAllText(Path.Combine(directory, "ParseBridge.cs"),
                "using System;using System.Globalization;public static class ContainerParseBridge {" +
                Methods(luaSource, "ParseLuaValue", "IsLuaIntegerType", "ParseInteger") + "}");
            File.Copy(Path.Combine(root, "runtime-snapshot/Quest Behaviors/UseItemOn.cs"), Path.Combine(directory, "UseItemOn.cs"));
            File.WriteAllText(Path.Combine(directory, "Boundary.cs"), boundary + Extra +
                "public sealed class QuestProtectionProbe { private void Dlog(string f,params object[] a){} public bool Protect(WoWItem item)=>IsQuestItem(item);" + guard + "}");
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { directory })!;
            var result = (CompilerResults)compilerType.GetMethod("Compile")!.Invoke(compiler, null)!;
            string[] errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).Select(e => e.ToString()).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Actual container/consumer compilation: " + string.Join("; ", errors));
            Execute((Assembly)compilerType.GetProperty("CompiledAssembly")!.GetValue(compiler)!, root);
        }
        finally { Styx.Helpers.Logging.FileLogging = oldLogging; Directory.Delete(directory, true); }
    }
    private static void Execute(Assembly probe, string root)
    {
        using var lua = new RewardLua51Boundary.StockLua51(root);
        Type bridge = probe.GetType("RewardRecordedBridge", true)!;
        var loadSize = (Func<string, uint>)bridge.GetMethod("LoadSize")!.CreateDelegate(typeof(Func<string, uint>));
        FieldInfo observe = bridge.GetField("Observe")!;
        MethodInfo invoke = probe.GetType("ContainerConsumer", true)!.GetMethod("Invoke")!;
        int passed = 0, assertions = 0, unexpected = 0, total = 0;
        void Scenario(string operation, string mode, string fault, string expected, int mutations)
        {
            total++;
            string label = operation + "/" + mode + "/" + fault;
            try
            {
                RewardLua51Boundary.Observation? observed = null;
                int requests = 0;
                observe.SetValue(null, new Func<string, List<string>>(script =>
                {
                    requests++;
                    string executed = fault == "actual" ? script : fault;
                    observed = lua.Execute(executed, loadSize(executed), mode, new[] { "", "" }, Setup);
                    return observed.Values;
                }));
                string actual = (string)invoke.Invoke(null, new object[] { operation })!;
                Check(requests == 1 && observed != null, "actual consumer did not issue one Lua request");
                Check(observed!.Load == 0 && (fault.StartsWith("error", StringComparison.Ordinal) ? observed.Call != 0 : observed.Call == 0), "unexpected Lua error: " + observed.Error);
                Check(observed.Clicks == mutations, "mutation count=" + observed.Clicks + " expected=" + mutations);
                Check(actual == expected, "result=" + actual + " expected=" + expected + "; values=" + string.Join("|", observed.Values));
                passed++; Console.WriteLine("PASS container Lua51: " + label);
            }
            catch (Failure e) { assertions++; Console.Error.WriteLine("FAIL container Lua51: " + label + ": " + e.Message); }
            catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR container Lua51: " + label + ": " + e); }
            finally { observe.SetValue(null, null); }
        }
        foreach (string operation in new[] { "use", "dispatch" })
        {
            Scenario(operation, "ordinary", "actual", operation == "use" ? "True" : "1/1", 1);
            Scenario(operation, "wrong-source", "actual", operation == "use" ? "False" : "0/0", 0);
            foreach (string fault in new[] { "return", "return nil", "return true", "return 'garbage'", "error('transport')" })
                Scenario(operation, "ordinary", fault, operation == "use" ? "False" : "0/0", 0);
        }
        foreach (var c in new[] {
            (Mode:"ordinary", Info:"True/False/0/False", Protect:"False"),
            (Mode:"quest-item", Info:"True/True/0/False", Protect:"True"),
            (Mode:"quest-id", Info:"True/False/900001/False", Protect:"True"),
            (Mode:"active", Info:"True/True/900001/True", Protect:"True"),
            (Mode:"numeric-flags", Info:"True/True/900001/True", Protect:"True"),
            (Mode:"wrong-source", Info:"False/False/0/False", Protect:"True") })
        {
            Scenario("info", c.Mode, "actual", c.Info, 0);
            Scenario("protect", c.Mode, "actual", c.Protect, 0);
        }
        foreach (string fault in new[] {
            "return", "return nil", "return true,false,0,false", "error('transport')",
            "return 1,0,0", "return 1,0,0,0,'extra'", "return 'garbage',0,0,0",
            "return 1,'garbage',0,0", "return 1,0,'garbage',0", "return 1,0,0,'garbage'",
            "return 1,0,-1,0", "return 1,0,1.5,0", "return 1,0,2147483648,0",
            "return 1,2,0,0", "return 1,0,0,2", "return 0,0,0,0" })
        {
            Scenario("info", "ordinary", fault, "False/False/0/False", 0);
            Scenario("protect", "ordinary", fault, "True", 0);
        }
        Console.WriteLine($"Container Lua51 boundary scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; actual item methods/full UseItemOn/quest protection, Lua5.1 and converters; controlled observations; no native recipient effect or server credit.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Container Lua51 boundary regression");
    }
    private static string Methods(string source, params string[] names)
    {
        var declarations = CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
        return string.Join("\n", names.Select(n => declarations.Single(m => m.Identifier.ValueText == n).ToFullString()));
    }
    private static void Check(bool value, string why) { if (!value) throw new Failure(why); }
    private static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CopilotBuddy.csproj"))) return d.FullName;
        throw new InvalidOperationException("Tracked checkout required");
    }
    private const string Extra = """
/* Embedded controlled observation namespace, not the outer initializer. */ namespace Styx.WoWInternals.WoWObjects
{
    public sealed class WoWContainer {public ulong[] ItemGuids=new ulong[]{17};}
    public sealed class ContainerInventory {public WoWContainer Backpack=new();}
}
/* Embedded controlled observation namespace, not the outer initializer. */ namespace Styx.WoWInternals
{
    public static class Lua
    {
        public static T GetReturnVal<T>(string script,uint index)=>RewardRecordedBridge.GetReturnVal<T>(script,index);
        public static List<string> GetReturnValues(string script)=>RewardRecordedBridge.GetReturnValues(script);
        public static T ParseLuaValue<T>(string value)=>ContainerParseBridge.ParseLuaValue<T>(value);
    }
}
/* Embedded controlled observation namespace, not the outer initializer. */ namespace Styx.Logic.Inventory.Frames.Merchant
{
    public sealed class MerchantFrame {public static MerchantFrame Instance=new();public bool IsVisible=>false;}
}
public static class Logging {public static void WriteDebug(string f,params object[] a){}}
public static class ContainerConsumer
{
    private const BindingFlags H=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    public static string Invoke(string operation)
    {
        typeof(QuestItemSelectionCases).GetMethod("Reset",H)!.Invoke(null,null);
        var owner=(Script)typeof(QuestItemSelectionCases).GetField("owner",H)!.GetValue(null)!;
        typeof(Script).GetField("_isDisposed",H)!.SetValue(owner,false);
        typeof(Script).GetField("_submissionRefusalUtc",H)!.SetValue(owner,-1L);
        typeof(Script).GetProperty("SubmissionRefusalTimeout",H)!.SetValue(owner,5000);
        GC.SuppressFinalize(owner);
        var item=new WoWItem{Guid=17,Entry=12345};ObjectManager.Me!.CarriedItems!.Add(item);
        if(operation=="use")return item.TryUseContainerItem().ToString();
        if(operation=="info")
        {
            bool ok=item.TryGetContainerItemQuestInfo(out bool quest,out int id,out bool active);
            return ok+"/"+quest+"/"+id+"/"+active;
        }
        if(operation=="protect")return new QuestProtectionProbe().Protect(item).ToString();
        var errors=new List<string>();
        void Record(Styx.Helpers.LogLevel level,string message){if(message.Contains("Exception")||message.Contains("Object reference not set"))errors.Add(message);}
        Styx.Helpers.Logging.OnMessageLogged+=Record;
        try
        {
            Composite tree=(Composite)typeof(Script).GetMethod("CreateBehavior",H)!.Invoke(owner,null)!;
            tree.Start(null!);
            try {int count=0;while(tree.Tick(null!)==RunStatus.Running)if(++count>20)throw new InvalidOperationException("Unbounded item tree");}
            finally {tree.Stop(null!);}
            if(errors.Count!=0)throw new InvalidOperationException("Swallowed owner error: "+string.Join(";",errors));
            return typeof(Script).GetProperty("Counter",H)!.GetValue(owner)+"/"+((List<ulong>)typeof(Script).GetField("_npcBlacklist",H)!.GetValue(owner)!).Count;
        }
        finally {Styx.Helpers.Logging.OnMessageLogged-=Record;}
    }
}
""";
    private const string Setup = """
clicks=0
function GetContainerItemLink(bag,slot)
 assert(bag==0 and slot==1)
 if scenario=='wrong-source' then return '|Hitem:999:0|h[Other]|h' end
 return '|Hitem:12345:0|h[Item]|h'
end
function UseContainerItem(bag,slot) assert(bag==0 and slot==1);clicks=clicks+1 end
function GetContainerItemQuestInfo(bag,slot)
 assert(bag==0 and slot==1)
 if scenario=='quest-item' then return true,nil,nil end
 if scenario=='quest-id' then return nil,900001,false end
 if scenario=='active' then return true,900001,true end
 if scenario=='numeric-flags' then return 1,900001,1 end
 return nil,nil,nil
end
""";
}
