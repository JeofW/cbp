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

// Actual tracked cooldown readers/parser/admission, generated Lua5.1 requests and
// production managed conversion. Only world, clock, executor and client API leaves
// are controlled. No cast success, original native ABI or live realm is emulated.
internal static class SpellCooldownObservationRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        string Read(string path) => File.ReadAllText(Path.Combine(root, path));
        var manager = CSharpSyntaxTree.ParseText(Read("Styx/Logic/Combat/SpellManager.cs")).GetRoot();
        var methodNames = new HashSet<string> { "CalculateTrackedCooldownRemaining", "IsCooldownReady",
            "IsTrackedCooldownBlocking", "TryParseAvailability", "GetTrackedCooldownTimeLeft", "TrackCooldown",
            "TrackDeadline", "IsSpellAvailable", "GetSpellCooldownTimeLeft", "CreateCooldownQuery",
            "CaptureSpellObservation", "PrepareCooldownContext", "ResetCooldownObservations", "TryCastSpellById" };
        var fieldNames = new HashSet<string> { "_knownSpells", "_cooldownSync", "_cooldownReadyAtTicks",
            "_castVerificationUntilTicks", "_readinessProbeNotBeforeTicks", "CastAttemptVerificationDelayMs",
            "UnavailableProbeBackoffMs", "FailedProbeBackoffMs", "_cooldownContext", "_cooldownEpoch", "_lastCooldownObservationTicks" };
        string members = string.Join("\n", manager.DescendantNodes().OfType<FieldDeclarationSyntax>()
            .Where(f => f.Declaration.Variables.Any(v => fieldNames.Contains(v.Identifier.ValueText))).Select(f => f.ToFullString()))
            + string.Join("\n", manager.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(m => methodNames.Contains(m.Identifier.ValueText)).Select(m => m.ToFullString()))
            + string.Join("\n", manager.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Where(c => c.Identifier.ValueText == "SpellObservationContext").Select(c => c.ToFullString()));
        var spell = CSharpSyntaxTree.ParseText(Read("Styx/Logic/Combat/WoWSpell.cs")).GetRoot();
        string properties = string.Join("\n", spell.DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Where(p => p.Identifier.ValueText is "Cooldown" or "CooldownTimeLeft").Select(p => p.ToFullString()));
        var singular = CSharpSyntaxTree.ParseText(Read("runtime-snapshot/Routines/Singular wotlk/Helpers/Spell.cs")).GetRoot();
        string extensions = string.Join("\n", singular.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(m => m.Identifier.ValueText is "CooldownTimeLeft" or "GetSpellCooldown").Select(m => m.ToFullString()));
        var legacy = CSharpSyntaxTree.ParseText(Read("Styx/Logic/Combat/LegacySpellManager.cs")).GetRoot();
        string integerReader = legacy.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == "GetSpellCooldown").ToFullString();
        string owners = Prefix + "public static class SpellManager {\n" + members + ManagerLeaves + "}\n"
            + "public sealed class WoWSpell { public int Id; private int _id=>Id; public string Name=\"Selected\";\n" + properties + "}\n"
            + "public static class SingularSpell {\n" + extensions + "}\n"
            + "public static class LegacySpellManager {\n" + integerReader + "}\n" + Cases;
        Console.WriteLine("COOLDOWN_OWNER_SOURCE_SHA256=" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(owners))).ToLowerInvariant());
        string directory = Path.Combine(Path.GetTempPath(), "cb-cooldown-observation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        bool logging = Styx.Helpers.Logging.FileLogging;
        try
        {
            Styx.Helpers.Logging.FileLogging = false;
            RewardLua51Boundary.WriteManagedBridge(directory, Read("Styx/WoWInternals/Lua.cs"));
            File.WriteAllText(Path.Combine(directory, "Probe.cs"), owners);
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { directory })!;
            var result = (CompilerResults)compilerType.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Actual cooldown owners: " + string.Join("; ", errors.Select(e => e.ToString())));
            var assembly = (Assembly)compilerType.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            using var stock = new RewardLua51Boundary.StockLua51(root);
            var loadSize = (Func<string, uint>)assembly.GetType("RewardRecordedBridge", true)!
                .GetMethod("LoadSize")!.CreateDelegate(typeof(Func<string, uint>));
            assembly.GetType("Lua", true)!.GetField("StockObservation")!.SetValue(null,
                new Func<string, string, List<string>>((script, mode) =>
                {
                    var observed = stock.Execute(script, loadSize(script), mode, new[] { "", "" }, StockSetup);
                    if (observed.Load != 0) throw new InvalidOperationException("Production cooldown query did not load: " + observed.Error);
                    // Lua.GetReturnValues returns an empty observation on a failed
                    // protected call. Execute the failing API/arithmetic first.
                    return observed.Call == 0 ? observed.Values : new List<string>();
                }));
            try { assembly.GetType("CooldownCases", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        finally { Styx.Helpers.Logging.FileLogging = logging; Directory.Delete(directory, true); }
    }

    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "CopilotBuddy.csproj"))) return dir.FullName;
        throw new InvalidOperationException("Tracked source required");
    }

    private const string Prefix = """
#nullable disable
using System;using System.Collections.Generic;using System.Globalization;using System.Linq;using System.Reflection;using Styx.Helpers;using Patchables=Styx.Patchables;
public static class Environment {public static long TickCount64=1000000;}
public static class ObjectManager {public static ExecutorRand Executor;public static object Wow=new object();}
public sealed class Actor {public bool IsValid=true;public ulong Guid=1;public uint BaseAddress=4096,MapId=530;}
public static class StyxWoW {public static Actor Me=new Actor();public static void ResetAfk(){}}
public static class TreeRoot {
 public static object Current=new object(),RunIdentity=new object();public static bool IsRunning=true,CurrentThreadIsBotThread;
 public static void VerifyPulseOwner(object bot,bool worker){if(worker&&(!IsRunning||!ReferenceEquals(bot,Current)))throw new OperationCanceledException("controlled Stop");}
}
public sealed class ExecutorRand {
 public readonly object AssemblyLock=new object();public readonly ReadMemory Memory=new ReadMemory();public IntPtr ReturnPointer=>IntPtr.Zero;
 public static string Mode;public static Action OnExecute;public static int Executions;public void Clear(){}public void AddLine(string text,params object[] values){}
 public void Execute(){Executions++;var next=OnExecute;OnExecute=null;next?.Invoke();var stop=new OperationCanceledException("controlled native cancellation");if(Mode=="cancel")throw stop;if(Mode=="wrapped-cancel")throw new TargetInvocationException(stop);if(Mode=="failure")throw new InvalidOperationException("unavailable native read");}
}
public sealed class ReadMemory {public T Read<T>(IntPtr pointer)=>(T)(object)(ExecutorRand.Mode=="active"?1:0);}
public static class Logging {public static void WriteDebug(string text,params object[] values){}public static void WriteException(Exception error){}}
public static class Lua {
 public static List<string> Values;public static string Mode;public static int Reads;public static Func<string,string,List<string>> StockObservation;public static Action OnRead;
 public static List<string> GetReturnValues(string script){Reads++;var values=Mode==null?Values:StockObservation(script,Mode);var callback=OnRead;OnRead=null;callback?.Invoke();return values;}
 public static T GetReturnVal<T>(string script,uint index)=>RewardRecordedBridge.GetReturnVal<T>(script,index);
}
""";

    private const string ManagerLeaves = """
 public static Dictionary<string,WoWSpell> Spells=>_knownSpells;
 public static bool HasSpell(string name)=>_knownSpells.ContainsKey(name);
 public static bool Admit(WoWSpell spell)=>IsSpellAvailable(spell,35,true);
 public static bool Dispatch(WoWSpell spell)=>TryCastSpellById(spell.Id,1UL);
 public static bool Parse(IReadOnlyList<string> values)=>TryParseAvailability(values,out _);
 public static void Reset(WoWSpell spell){_knownSpells.Clear();_knownSpells[spell.Name]=spell;_cooldownReadyAtTicks.Clear();_castVerificationUntilTicks.Clear();_readinessProbeNotBeforeTicks.Clear();}
 public static void Submitted(WoWSpell spell){
  var capture=typeof(SpellManager).GetMethod("CaptureSpellObservation",BindingFlags.NonPublic|BindingFlags.Static);
  if(capture!=null)typeof(SpellManager).GetMethod("PrepareCooldownContext",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new[]{capture.Invoke(null,null)});
  _castVerificationUntilTicks[spell.Id]=Environment.TickCount64+250;
 }
 public static void NewSession(){typeof(SpellManager).GetMethod("ResetCooldownObservations",BindingFlags.NonPublic|BindingFlags.Static)?.Invoke(null,null);}
""";

    private const string Cases = """
public static class CooldownCases {
 private sealed class Failure(string text):Exception(text){}
 private static int id;
 private static void Check(bool condition,string reason){if(!condition)throw new Failure(reason);}
 private static WoWSpell Reset(string mode="ready"){
  var spell=new WoWSpell{Id=++id};SpellManager.Reset(spell);Lua.Mode=mode;Lua.Values=null;Lua.Reads=0;Lua.OnRead=null;ObjectManager.Executor=new ExecutorRand();ObjectManager.Wow=new object();ExecutorRand.Mode=null;ExecutorRand.OnExecute=null;ExecutorRand.Executions=0;
  StyxWoW.Me=new Actor();TreeRoot.Current=new object();TreeRoot.RunIdentity=new object();TreeRoot.IsRunning=true;TreeRoot.CurrentThreadIsBotThread=false;Environment.TickCount64=1000000;
  RewardRecordedBridge.Observe=Lua.GetReturnValues;return spell;
 }
 private static TimeSpan Read(WoWSpell spell,string reader)=>reader=="manager"?SpellManager.GetSpellCooldownTimeLeft(spell.Id):reader=="spell"?spell.CooldownTimeLeft:reader=="singular"?SingularSpell.CooldownTimeLeft(spell):TimeSpan.FromMilliseconds(LegacySpellManager.GetSpellCooldown(spell.Name));
 private static void Unknown(WoWSpell spell,string reader){
  bool unknown=false;TimeSpan result=default;try{result=Read(spell,reader);}catch(ObservationUnavailableException){unknown=true;}
  catch(Exception error){throw new Failure("uncontrolled "+error.GetType().Name+" escaped "+reader);}
  Check(unknown,"incomplete observation became a numeric cooldown: "+result);
 }
 public static void Run(){
  int total=0,passed=0,assertions=0,unexpected=0;
  void Case(string name,Action test){total++;try{test();passed++;Console.WriteLine("PASS cooldown observation: "+name);}catch(Failure error){assertions++;Console.Error.WriteLine("FAIL cooldown observation: "+name+": "+error.Message);}catch(Exception error){unexpected++;Console.Error.WriteLine("ERROR cooldown observation: "+name+": "+error);}}
  string[] readers={"manager","spell","singular","legacy"};
  foreach(string reader in readers)foreach(string mode in new[]{"ready","active","expired"})
   Case("known/"+reader+"/"+mode,()=>{var spell=Reset(mode);Check(Read(spell,reader)==TimeSpan.FromSeconds(mode=="active"?5:0),"valid current cooldown changed");});
  string[] invalid={"missing-name","name-type","name-empty","missing-start","missing-duration","missing-enabled","disabled","enabled-type","nan-start","nan-duration","infinite-start","infinite-duration","negative-start","negative-duration","negative-now","nan-now","infinite-now","string-duration","overflow-duration"};
  foreach(string reader in readers)foreach(string mode in invalid)
   Case("unknown/"+reader+"/"+mode,()=>{var spell=Reset(mode);Unknown(spell,reader);});
  foreach(string mode in invalid)
   Case("admission/"+mode,()=>{var spell=Reset(mode);bool admitted=false;try{admitted=SpellManager.Admit(spell);}catch(Exception error){throw new Failure("uncontrolled admission "+error.GetType().Name);}Check(!admitted,"unknown observation authorized cast admission");});
  foreach(string mode in new[]{"ready","active","unusable","expired"})
   Case("admission known/"+mode,()=>{var spell=Reset(mode);Check(SpellManager.Admit(spell)==(mode=="ready"||mode=="expired"),"known admission/lag behavior changed");});
  foreach(string value in new[]{"NaN","Infinity","-Infinity","1e300","-2","unknown","","9.223372036854776e11"})
   Case("marked parser/"+value,()=>{Reset();Check(!SpellManager.Parse(new[]{"ok",value}),"malformed duration accepted by shared parser");});
  foreach(var values in new IReadOnlyList<string>[] {null,Array.Empty<string>(),new[]{"ok"},new[]{"bad","0"},new[]{"ok","0","unexpected"}})
   Case("marked parser shape/"+(values==null?"null":string.Join("|",values)),()=>{Reset();Check(!SpellManager.Parse(values),"partial/ambiguous observation accepted");});
  foreach(string value in new[]{"0","5","-1","0.035"})
   Case("marked parser control/"+value,()=>{Reset();Check(SpellManager.Parse(new[]{"ok",value}),"valid protocol value rejected");});
  foreach(string reader in readers)
   Case("empty transport/"+reader,()=>{var spell=Reset(null);Lua.Values=new List<string>();Unknown(spell,reader);});
  Case("dispatch hold is not observed cooldown",()=>{var spell=Reset();SpellManager.Submitted(spell);Check(!SpellManager.Admit(spell)&&Lua.Reads==0,"local verification hold was lost");Check(SpellManager.GetSpellCooldownTimeLeft(spell.Id)==TimeSpan.Zero&&Lua.Reads==1,"local submitted call masqueraded as authoritative cooldown acknowledgement");});
  foreach(bool numericFirst in new[]{false,true})
   Case("actual dispatch guard without prior query/numeric="+numericFirst,()=>{var spell=Reset();SpellManager.NewSession();Check(SpellManager.Dispatch(spell)&&ExecutorRand.Executions==1,"actual dispatch control failed");if(numericFirst)Check(Read(spell,"manager")==TimeSpan.Zero,"submitted action became an observed cooldown");int reads=Lua.Reads;Check(!SpellManager.Admit(spell)&&Lua.Reads==reads,"first observation erased the actual local-dispatch hold");Environment.TickCount64+=250;Check(SpellManager.Admit(spell),"bounded local hold failed to expire");});
  foreach(string change in new[]{"actor","map","memory","executor","bot","worker","session"})
   Case("actual dispatch reply after owner change/"+change,()=>{var spell=Reset();Check(SpellManager.Admit(spell),"initial ready control failed");ExecutorRand.OnExecute=()=>{Change(change);Check(SpellManager.Admit(spell),"replacement ready control failed");};Check(!SpellManager.Dispatch(spell),"revoked dispatch published completed-owner success");Check(SpellManager.Admit(spell),"old dispatch hold poisoned the replacement owner");});
  foreach(string mode in new[]{"cancel","wrapped-cancel"})
   Case("actual dispatch cancellation/"+mode,()=>{var spell=Reset();ExecutorRand.Mode=mode;bool cancelled=false;try{_=SpellManager.Dispatch(spell);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"actual dispatch swallowed cancellation");});
  Case("dispatch retains a later same-owner cooldown observation",()=>{var spell=Reset();var other=new WoWSpell{Id=100000,Name="Other"};ExecutorRand.OnExecute=()=>{Environment.TickCount64+=100;Lua.Mode="active";Check(Read(other,"manager")==TimeSpan.FromSeconds(5),"nested current cooldown control failed");};Check(SpellManager.Dispatch(spell),"same-owner nested observation rejected a completed dispatch");Lua.Mode="ready";Check(!SpellManager.Admit(other),"older dispatch registration erased the newer authoritative cooldown");});
  foreach(string mode in new[]{"cancel","wrapped-cancel"})
   Case("native cancellation/"+mode,()=>{var spell=Reset();ObjectManager.Executor=new ExecutorRand();ExecutorRand.Mode=mode;bool cancelled=false;try{_=spell.Cooldown;}catch(OperationCanceledException){cancelled=true;}Check(cancelled&&Lua.Reads==0,"cancelled native observation fell through to another query");});
  Case("native ordinary failure remains unknown",()=>{var spell=Reset("missing-name");ObjectManager.Executor=new ExecutorRand();ExecutorRand.Mode="failure";bool unknown=false;try{_=spell.Cooldown;}catch(ObservationUnavailableException){unknown=true;}Check(unknown,"failed native and unavailable fallback became ready");});
  foreach(string reader in new[]{"singular","legacy"})
   Case("missing named spell/"+reader,()=>{Reset();bool unknown=false;try{if(reader=="singular")_=SingularSpell.GetSpellCooldown("Unobserved");else _=LegacySpellManager.GetSpellCooldown("Unobserved");}catch(ObservationUnavailableException){unknown=true;}Check(unknown,"missing spell became a numeric cooldown");});
  string[] changes={"actor","guid","address","map","memory","executor","bot","running","session","clock","worker"};
  foreach(string reader in readers)foreach(string change in changes)
   Case("revoked numeric/"+reader+"/"+change,()=>{var spell=Reset("active");Lua.OnRead=()=>Change(change);Unknown(spell,reader);Lua.Mode="ready";Check(SpellManager.Admit(spell),"revoked reply poisoned replacement readiness");});
  foreach(string change in changes)
   Case("revoked admission/"+change,()=>{var spell=Reset();Lua.OnRead=()=>Change(change);Check(!SpellManager.Admit(spell),"revoked reply authorized admission");Lua.Mode="ready";Check(SpellManager.Admit(spell),"revoked admission poisoned replacement context");});
  foreach(string change in changes)
   Case("cached cooldown/"+change,()=>{var spell=Reset("active");Check(!SpellManager.Admit(spell),"active cooldown control failed");Change(change);Lua.Mode="ready";Check(SpellManager.Admit(spell),"old context cooldown denied the replacement");});
  foreach(string missing in new[]{"actor","guid","memory","executor","validity"})
   foreach(string reader in readers)
    Case("missing context/"+reader+"/"+missing,()=>{var spell=Reset();if(missing=="actor")StyxWoW.Me=null;else if(missing=="guid")StyxWoW.Me.Guid=0;else if(missing=="memory")ObjectManager.Wow=null;else if(missing=="executor")ObjectManager.Executor=null;else StyxWoW.Me.IsValid=false;Unknown(spell,reader);});
  Case("known ready observation clears previous cooldown",()=>{var spell=Reset("active");Check(!SpellManager.Admit(spell),"active control");Lua.Mode="ready";Check(Read(spell,"manager")==TimeSpan.Zero&&SpellManager.Admit(spell),"known cooldown reset retained old deadline");});
  foreach(bool numeric in new[]{false,true})
   Case("delayed reply retains its observed remaining cooldown/numeric="+numeric,()=>{var spell=Reset("active");long sent=Environment.TickCount64;Lua.OnRead=()=>Environment.TickCount64+=2000;if(numeric)Check(Read(spell,"manager")==TimeSpan.FromSeconds(5),"active numeric control");else Check(!SpellManager.Admit(spell),"active admission control");Environment.TickCount64=sent+5001;Lua.Mode="ready";Check(!SpellManager.Admit(spell),"query latency shortened the observed remaining cooldown");});
  Case("clock rollback after delayed publication clears the old context",()=>{var spell=Reset("active");Lua.OnRead=()=>Environment.TickCount64+=2000;Check(Read(spell,"manager")==TimeSpan.FromSeconds(5),"delayed observation control");Environment.TickCount64--;Lua.Mode="ready";Check(SpellManager.Admit(spell),"rollback after query completion retained an obsolete deadline");});
  Case("Stop during query remains cancellation",()=>{var spell=Reset();TreeRoot.CurrentThreadIsBotThread=true;Lua.OnRead=()=>TreeRoot.IsRunning=false;bool cancelled=false;try{_=Read(spell,"manager");}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"explicit Stop was relabeled as an observation");});
  foreach(string change in changes.Where(value=>value!="clock"))
   Case("native result ownership/"+change,()=>{var spell=Reset();ExecutorRand.OnExecute=()=>Change(change);bool unknown=false;try{_=spell.Cooldown;}catch(ObservationUnavailableException){unknown=true;}Check(unknown&&Lua.Reads==0,"revoked native observation returned readiness or fell through");});
  foreach(int invalidId in new[]{0,-1}) {
   Case("invalid admission identity/"+invalidId,()=>{var spell=Reset();spell.Id=invalidId;Check(!SpellManager.Admit(spell)&&Lua.Reads==0,"invalid spell identity reached admission observation");});
   Case("invalid native identity/"+invalidId,()=>{var spell=Reset();spell.Id=invalidId;bool unknown=false;try{_=spell.Cooldown;}catch(ObservationUnavailableException){unknown=true;}Check(unknown&&ExecutorRand.Executions==0&&Lua.Reads==0,"invalid spell identity reached native cooldown dispatch");});
  }
  foreach(string mode in invalid)
   Case("native zero needs complete readiness/"+mode,()=>{var spell=Reset(mode);bool unknown=false;try{_=spell.Cooldown;}catch(ObservationUnavailableException){unknown=true;}Check(unknown,"native zero became ready without complete metadata/cooldown");});
  Case("native active cooldown keeps positive evidence",()=>{var spell=Reset("missing-name");ExecutorRand.Mode="active";Check(spell.Cooldown&&Lua.Reads==0,"positive native cooldown was replaced with unrelated missing data");});
  foreach(string mode in new[]{"ready","active"})
   Case("native zero is confirmed by the later complete reply/"+mode,()=>{var spell=Reset(mode);Check(spell.Cooldown==(mode=="active")&&Lua.Reads==1,"native zero skipped complete cooldown confirmation");});
  Case("positive sub-millisecond legacy cooldown cannot become zero",()=>{var spell=Reset(null);Lua.Values=new(){"ok","0.0005"};Check(LegacySpellManager.GetSpellCooldown(spell.Name)==1,"integer conversion declared a positive cooldown ready");});
  Case("unrepresentable legacy cooldown remains unavailable",()=>{var spell=Reset(null);Lua.Values=new(){"ok","2147483.648"};Unknown(spell,"legacy");});
  Case("maximum representable legacy cooldown remains exact",()=>{var spell=Reset(null);Lua.Values=new(){"ok","2147483.647"};Check(LegacySpellManager.GetSpellCooldown(spell.Name)==int.MaxValue,"valid maximum cooldown changed");});
  Console.WriteLine($"Spell cooldown observation cases: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; exact tracked readers/parser/admission and generated Lua5.1 plus production conversion; controlled APIs/native failures; no game attached.");
  if(assertions+unexpected!=0)throw new InvalidOperationException("Spell cooldown observation regression");
 }
 private static void Change(string change){
  if(change=="actor")StyxWoW.Me=new Actor();else if(change=="guid")StyxWoW.Me.Guid++;
  else if(change=="address")StyxWoW.Me.BaseAddress+=4096;else if(change=="map")StyxWoW.Me.MapId++;
  else if(change=="memory")ObjectManager.Wow=new object();else if(change=="executor")ObjectManager.Executor=new ExecutorRand();
  else if(change=="bot")TreeRoot.Current=new object();else if(change=="running")TreeRoot.IsRunning=!TreeRoot.IsRunning;
  else if(change=="session")SpellManager.NewSession();else if(change=="clock")Environment.TickCount64-=1;
  else if(change=="worker")TreeRoot.RunIdentity=new object();
 }
}
""";

    private const string StockSetup = """
function GetSpellInfo(id)
 if scenario=='missing-name' then return nil end
 if scenario=='name-type' then return 12 end
 if scenario=='name-empty' then return '' end
 return 'Selected'
end
function GetSpellCooldown(name)
 if scenario=='missing-start' then return nil,0,1 end
 if scenario=='missing-duration' then return 0,nil,1 end
 if scenario=='missing-enabled' then return 0,0,nil end
 if scenario=='disabled' then return 0,0,0 end
 if scenario=='enabled-type' then return 0,0,true end
 if scenario=='nan-start' then return 0/0,0,1 end
 if scenario=='nan-duration' then return 0,0/0,1 end
 if scenario=='infinite-start' then return math.huge,0,1 end
 if scenario=='infinite-duration' then return 0,math.huge,1 end
 if scenario=='negative-start' then return -1,0,1 end
 if scenario=='negative-duration' then return 0,-1,1 end
 if scenario=='string-duration' then return 0,'bad',1 end
 if scenario=='overflow-duration' then return 0,1e300,1 end
 if scenario=='active' then return 100,15,1 end
 if scenario=='expired' then return 100,5,1 end
 return 0,0,1
end
function GetTime()
 if scenario=='negative-now' then return -1 end
 if scenario=='nan-now' then return 0/0 end
 if scenario=='infinite-now' then return math.huge end
 return 110
end
function IsUsableSpell(name) return scenario~='unusable' end
""";
}
