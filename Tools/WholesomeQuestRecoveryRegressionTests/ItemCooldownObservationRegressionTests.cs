using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Actual item readers and their generated Lua requests, with the production
// managed conversion and stock Lua5.1. World/item/clock/API replies are leaves;
// no live item use, cooldown acknowledgement or inventory mutation is simulated.
internal static class ItemCooldownObservationRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        string Read(string path) => File.ReadAllText(Path.Combine(root, path));
        var item = CSharpSyntaxTree.ParseText(Read("Styx/WoWInternals/WoWObjects/WoWItem.cs")).GetRoot()
            .DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "WoWItem");
        var methods = new HashSet<string> { "ReadCooldownObservation", "RequireEnabledCooldown",
            "TryGetCooldownObservation", "BuildItemCooldownQuery", "TryParseItemCooldown" };
        string members = string.Join("\n", item.Members.OfType<PropertyDeclarationSyntax>()
            .Where(p => p.Identifier.ValueText is "Cooldown" or "CooldownTimeLeft" or "IsCooldownReady").Select(p => p.ToString()))
            + string.Join("\n", item.Members.OfType<MethodDeclarationSyntax>().Where(m => methods.Contains(m.Identifier.ValueText)).Select(m => m.ToString()))
            + string.Join("\n", item.Members.OfType<ClassDeclarationSyntax>().Where(c => c.Identifier.ValueText == "ItemCooldownObservation").Select(c => c.ToString()));
        string source = Prefix + "public sealed class WoWItem { public bool IsValid=true;public ulong Guid=2,OwnerGuid=1;public uint BaseAddress=8192,Entry=33447;\n"
            + members + "}\n" + Cases;
        Console.WriteLine("ITEM_COOLDOWN_OWNER_SHA256=" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant());
        string folder = Path.Combine(Path.GetTempPath(), "cb-item-cooldown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        bool logging = Styx.Helpers.Logging.FileLogging;
        try
        {
            Styx.Helpers.Logging.FileLogging = false;
            RewardLua51Boundary.WriteManagedBridge(folder, Read("Styx/WoWInternals/Lua.cs"));
            File.WriteAllText(Path.Combine(folder, "Probe.cs"), source);
            Type type = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(type, new object[] { folder })!;
            var result = (CompilerResults)type.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Actual item cooldown owners: " + string.Join("; ", errors.Select(e => e.ToString())));
            var assembly = (Assembly)type.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            using var stock = new RewardLua51Boundary.StockLua51(root);
            var loadSize = (Func<string, uint>)assembly.GetType("RewardRecordedBridge", true)!.GetMethod("LoadSize")!.CreateDelegate(typeof(Func<string, uint>));
            assembly.GetType("Lua", true)!.GetField("StockObservation")!.SetValue(null,
                new Func<string, string, List<string>>((script, mode) =>
                {
                    var observed = stock.Execute(script, loadSize(script), mode, new[] { "", "" }, StockSetup);
                    if (observed.Load != 0) throw new InvalidOperationException("Item cooldown query failed to load: " + observed.Error);
                    return observed.Call == 0 ? observed.Values : new List<string>();
                }));
            try { assembly.GetType("ItemCooldownCases", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        finally { Styx.Helpers.Logging.FileLogging = logging; Directory.Delete(folder, true); }
    }

    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "CopilotBuddy.csproj"))) return dir.FullName;
        throw new InvalidOperationException("Tracked source required");
    }

    private const string Prefix = """
#nullable disable
using System;using System.Collections.Generic;using System.Globalization;using System.Linq;using System.Reflection;using Styx.Helpers;
public static class Environment {public static long TickCount64=100000;}
public sealed class Actor {public bool IsValid=true;public ulong Guid=1;public uint BaseAddress=4096,MapId=530;}
public static class StyxWoW {public static Actor Me=new Actor();}
public static class ObjectManager {public static object Wow=new object(),Executor=new object();}
public static class TreeRoot {
 public static object Current=new object(),RunIdentity=new object();public static bool IsRunning=true,CurrentThreadIsBotThread;
 public static void VerifyPulseOwner(object bot,bool worker){if(worker&&(!IsRunning||!ReferenceEquals(bot,Current)))throw new OperationCanceledException("item observer Stop");}
}
public static class ObservationFailureDiagnostics {public static int Count;public static void Report(ObservationUnavailableException error,string consumer=null){Count++;}}
public static class Lua {
 public static string Mode;public static int Reads;public static List<string> Values;public static Action OnRead;public static Exception Error;
 public static Func<string,string,List<string>> StockObservation;
 public static List<string> GetReturnValues(string script){Reads++;if(Error!=null)throw Error;var values=Mode==null?Values:StockObservation(script,Mode);var callback=OnRead;OnRead=null;callback?.Invoke();return values;}
 public static T GetReturnVal<T>(string script,uint index)=>RewardRecordedBridge.GetReturnVal<T>(script,index);
 public static T ParseLuaValue<T>(string value)=>Styx.WoWInternals.Lua.ParseLuaValue<T>(value);
}
""";

    private const string Cases = """
public static class ItemCooldownCases {
 private sealed class Failure(string reason):Exception(reason){}
 private static readonly string[] Readers={"start","remaining","ready"};
 private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
 private static WoWItem Reset(string mode="ready"){
  StyxWoW.Me=new Actor();ObjectManager.Wow=new object();ObjectManager.Executor=new object();TreeRoot.Current=new object();TreeRoot.RunIdentity=new object();
  TreeRoot.IsRunning=true;TreeRoot.CurrentThreadIsBotThread=false;Environment.TickCount64=100000;ObservationFailureDiagnostics.Count=0;
  Lua.Mode=mode;Lua.Reads=0;Lua.Values=null;Lua.OnRead=null;Lua.Error=null;RewardRecordedBridge.Observe=Lua.GetReturnValues;return new WoWItem();
 }
 private static object Read(WoWItem item,string reader){
  if(reader=="start")return item.Cooldown;
  if(reader=="remaining")return item.CooldownTimeLeft;
  var property=typeof(WoWItem).GetProperty("IsCooldownReady");
  // This is the real enabled DrinkPotions admission before the explicit API.
  if(property==null)return item.Cooldown==0;
  try{return property.GetValue(item);}catch(TargetInvocationException error)when(error.InnerException!=null){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
 }
 private static void Refused(WoWItem item,string reader){
  bool unknown=false;object value=null;try{value=Read(item,reader);}catch(ObservationUnavailableException){unknown=true;}
  if(reader=="ready")Check(!unknown&&value is false,"unavailable item admission did not defer without an exception");
  else Check(unknown,"unavailable item data became a numeric value: "+value);
 }
 public static void Run(){int total=0,passed=0,assertions=0,unexpected=0;
  void Case(string name,Action action){total++;try{action();passed++;Console.WriteLine("PASS item cooldown: "+name);}catch(Failure error){assertions++;Console.Error.WriteLine("FAIL item cooldown: "+name+": "+error.Message);}catch(Exception error){unexpected++;Console.Error.WriteLine("ERROR item cooldown: "+name+": "+error);}}
  foreach(string reader in Readers)foreach(string mode in new[]{"ready","active","expired"})
   Case("valid/"+reader+"/"+mode,()=>{var item=Reset(mode);object result=Read(item,reader);Check(reader=="start"?(float)result==(mode=="ready"?0:100):reader=="remaining"?(TimeSpan)result==TimeSpan.FromSeconds(mode=="active"?5:0):(bool)result==(mode!="active"),"complete current observation or legacy numeric units changed");});
  string[] invalid={"missing-name","empty-name","name-type","missing-start","missing-duration","missing-enabled","enabled-type","enabled-two","nan-start","nan-duration","infinite-start","infinite-duration","negative-start","negative-duration","string-duration","negative-now","nan-now","infinite-now","future-start","overflow-duration","api-error"};
  foreach(string reader in Readers)foreach(string mode in invalid)
   Case("unavailable/"+reader+"/"+mode,()=>{var item=Reset(mode);Refused(item,reader);});
  foreach(string reader in Readers)
   Case("disabled/"+reader,()=>{var item=Reset("disabled");Refused(item,reader);});
  foreach(string reader in Readers)foreach(var values in new List<string>[] {null,new(),new(){"item-cooldown","0"},new(){"item-cooldown","0","0","1","110","extra"},new(){"bad","0","0","1","110"}})
   Case("transport shape/"+reader+"/"+(values==null?"null":values.Count),()=>{var item=Reset(null);Lua.Values=values;Refused(item,reader);});
  foreach(string reader in Readers)foreach(string change in new[]{"actor","actor-guid","map","memory","executor","bot","worker","stop","item-guid","item-entry","item-base","item-invalid","clock"})
   Case("stale reply/"+reader+"/"+change,()=>{var item=Reset();Lua.OnRead=()=>Change(item,change);Refused(item,reader);});
  foreach(string reader in Readers)foreach(string missing in new[]{"actor","memory","executor","item-guid","item-entry","item-invalid"})
   Case("missing context/"+reader+"/"+missing,()=>{var item=Reset();if(missing=="actor")StyxWoW.Me=null;else if(missing=="memory")ObjectManager.Wow=null;else if(missing=="executor")ObjectManager.Executor=null;else if(missing=="item-guid")item.Guid=0;else if(missing=="item-entry")item.Entry=0;else item.IsValid=false;Refused(item,reader);Check(Lua.Reads==0,"invalid owner still issued a cooldown query");});
  foreach(string reader in Readers)foreach(bool wrapped in new[]{false,true})
   Case("cancellation/"+reader+"/"+wrapped,()=>{var item=Reset();Lua.Error=wrapped?new TargetInvocationException(new OperationCanceledException("controlled item cancel")):new OperationCanceledException("controlled item cancel");bool cancelled=false;try{_=Read(item,reader);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"item reader swallowed direct/wrapped cancellation");});
  foreach(string reader in Readers)
   Case("explicit worker Stop/"+reader,()=>{var item=Reset();TreeRoot.CurrentThreadIsBotThread=true;Lua.OnRead=()=>TreeRoot.IsRunning=false;bool cancelled=false;try{_=Read(item,reader);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"worker Stop did not propagate");});
  foreach(string reader in Readers)
   Case("valid reply after unknown recovers/"+reader,()=>{var item=Reset("missing-start");Refused(item,reader);Lua.Mode="ready";Check(reader=="start"?(float)Read(item,reader)==0:reader=="remaining"?(TimeSpan)Read(item,reader)==TimeSpan.Zero:(bool)Read(item,reader),"unknown observation became a permanent negative cache");});
  Case("known disabled is distinct from unavailable",()=>{var item=Reset("disabled");var method=typeof(WoWItem).GetMethod("TryGetCooldownObservation");Check(method!=null,"explicit disabled observation API is missing");object[] args={null};Check((bool)method.Invoke(item,args)&&args[0]!=null&&!(bool)args[0].GetType().GetProperty("Enabled").GetValue(args[0]),"disabled became unknown or enabled");Lua.Mode="missing-start";args[0]=null;Check(!(bool)method.Invoke(item,args)&&args[0]==null,"unknown exposed a usable partial cooldown observation");});
  Case("positive sub-tick cooldown stays positive",()=>{var item=Reset("sub-tick");Check(item.CooldownTimeLeft>TimeSpan.Zero&&!(bool)Read(item,"ready"),"positive remaining duration rounded down to ready");});
  Console.WriteLine($"Item cooldown observation cases: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; actual item readers and generated Lua5.1; no item use or game.");
  if(assertions+unexpected!=0)throw new InvalidOperationException("Item cooldown observation regressions failed");
 }
 private static void Change(WoWItem item,string change){
  if(change=="actor")StyxWoW.Me=new Actor();else if(change=="actor-guid")StyxWoW.Me.Guid=3;else if(change=="map")StyxWoW.Me.MapId=0;
  else if(change=="memory")ObjectManager.Wow=new object();else if(change=="executor")ObjectManager.Executor=new object();else if(change=="bot")TreeRoot.Current=new object();
  else if(change=="worker")TreeRoot.RunIdentity=new object();else if(change=="stop")TreeRoot.IsRunning=false;
  else if(change=="item-guid")item.Guid=3;else if(change=="item-entry")item.Entry=999;else if(change=="item-base")item.BaseAddress=12288;else if(change=="item-invalid")item.IsValid=false;
  else if(change=="clock")Environment.TickCount64--;
 }
}
""";

    private const string StockSetup = """
function GetItemInfo(id)
 if scenario=='missing-name' then return nil end
 if scenario=='empty-name' then return '' end
 if scenario=='name-type' then return 1 end
 return 'Selected Item'
end
function GetItemCooldown(id)
 if scenario=='missing-start' then return nil,0,1 end
 if scenario=='missing-duration' then return 0,nil,1 end
 if scenario=='missing-enabled' then return 0,0,nil end
 if scenario=='disabled' then return 0,0,0 end
 if scenario=='enabled-type' then return 0,0,true end
 if scenario=='enabled-two' then return 0,0,2 end
 if scenario=='nan-start' then return 0/0,0,1 end
 if scenario=='nan-duration' then return 0,0/0,1 end
 if scenario=='infinite-start' then return math.huge,0,1 end
 if scenario=='infinite-duration' then return 0,math.huge,1 end
 if scenario=='negative-start' then return -1,0,1 end
 if scenario=='negative-duration' then return 0,-1,1 end
 if scenario=='string-duration' then return 0,'bad',1 end
 if scenario=='overflow-duration' then return 0,1e300,1 end
 if scenario=='future-start' then return 200,15,1 end
 if scenario=='api-error' then error('unavailable item API') end
 if scenario=='active' then return 100,15,1 end
 if scenario=='expired' then return 100,5,1 end
 if scenario=='sub-tick' then return 0,1e-12,1 end
 return 0,0,1
end
function GetTime()
 if scenario=='negative-now' then return -1 end
 if scenario=='nan-now' then return 0/0 end
 if scenario=='infinite-now' then return math.huge end
 if scenario=='sub-tick' then return 0 end
 return 110
end
""";
}
