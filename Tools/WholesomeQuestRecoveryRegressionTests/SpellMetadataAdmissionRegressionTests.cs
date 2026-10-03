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

// Complete tracked metadata parser/getters and real host CanCast admission.
// Controlled original-nine-value API observations and world/readiness leaves;
// no invented Art of War return, native execution or simplified host substitute.
internal static class SpellMetadataAdmissionRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        var spell = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,
            "Styx/Logic/Combat/WoWSpell.cs"))).GetRoot();
        var propertyNames = new HashSet<string> { "PowerCost", "IsFunnel", "CastTime", "MinRange", "MaxRange" };
        var methodNames = new HashSet<string> { "GetSpellInfo", "GetCachedSpellInfo", "TryGetCurrentSpellInfo" };
        string properties = string.Join("\n", spell.DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Where(p => propertyNames.Contains(p.Identifier.ValueText)).Select(p => p.ToFullString()));
        string methods = string.Join("\n", spell.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(m => methodNames.Contains(m.Identifier.ValueText)).Select(m => m.ToFullString()));
        string fields = string.Join("\n", spell.DescendantNodes().OfType<FieldDeclarationSyntax>()
            .Where(f => f.Declaration.Variables.Any(v => v.Identifier.ValueText.StartsWith("_spellInfo", StringComparison.Ordinal)))
            .Select(f => f.ToFullString()));
        string cctor = string.Join("\n", spell.DescendantNodes().OfType<ConstructorDeclarationSyntax>()
            .Where(c => c.Modifiers.Any(m => m.ValueText == "static")).Select(c => c.ToFullString()));
        string data = spell.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(c => c.Identifier.ValueText == "SpellInfoCache").ToFullString();
        var manager = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,
            "Styx/Logic/Combat/SpellManager.cs"))).GetRoot();
        string admission = manager.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m =>
            m.Identifier.ValueText == "CanCast" && m.ParameterList.Parameters.Count == 5).ToFullString();
        string directory = Path.Combine(Path.GetTempPath(), "cb-spell-metadata-admission-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            RewardLua51Boundary.WriteManagedBridge(directory, File.ReadAllText(Path.Combine(root, "Styx/WoWInternals/Lua.cs")));
            File.WriteAllText(Path.Combine(directory, "Probe.cs"), Prefix +
                "public sealed class WoWSpell { public int Id; public string Name=\"Selected\";\n" +
                fields + cctor + properties + methods + data + "}\n" +
                "public static class SpellManager {\n" + admission + ManagerLeaves + "}\n" + Cases);
            Type type = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(type, new object[] { directory })!;
            var result = (CompilerResults)type.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException(string.Join("; ", errors.Select(e => e.ToString())));
            var assembly = (Assembly)type.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            using var stock = new RewardLua51Boundary.StockLua51(root);
            var loadSize = (Func<string, uint>)assembly.GetType("RewardRecordedBridge", true)!
                .GetMethod("LoadSize")!.CreateDelegate(typeof(Func<string, uint>));
            assembly.GetType("Lua", true)!.GetField("StockObservation")!.SetValue(null,
                new Func<string, string, List<string>>((script, mode) =>
                {
                    var observation = stock.Execute(script, loadSize(script), mode, new[] { "", "" }, StockSetup);
                    if (observation.Load != 0 || observation.Call != 0)
                        throw new InvalidOperationException("Unexpected stock Lua metadata load/call: " + observation.Error);
                    return observation.Values;
                }));
            try { assembly.GetType("MetadataCases", true)!.GetMethod("Run")!.Invoke(null, null); }
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
using System;using System.Collections.Generic;using System.Globalization;using System.Linq;using Styx.Helpers;
public class WoWUnit {public bool Sight=true,ThrowOnSight,IsWithinMeleeRange=true;public int SightReads;public double Distance=10;public bool InLineOfSpellSight {get {SightReads++;if(ThrowOnSight)throw new InvalidOperationException("invalid zero-length collision segment");return Sight;}set=>Sight=value;}}
public sealed class LocalPlayer:WoWUnit {public bool IsMoving,IsCasting;public int ChanneledCastingSpellId;public ulong Guid=7;}
public sealed class Client {public uint Value=35;public int Reads;public Exception Error;public uint Latency{get{Reads++;if(Error!=null)throw Error;return Value;}}}
public static class StyxWoW {public static LocalPlayer Me=new LocalPlayer();public static Client WoWClient=new Client();}
public static class World {public static bool Known=true,Available=true;public static int AvailabilityReads;public static uint Lag;public static bool LagPath;}
public static class Lua {
 public static List<string> Values;public static int Reads;public static string LastScript;
 public static string StockMode;public static Func<string,string,List<string>> StockObservation;
 public static List<string> GetReturnValues(string script,string name){Reads++;LastScript=script;if(StockMode!=null)return StockObservation(script,StockMode);return Values==null?null:new List<string>(Values);}
}
public static class Logging {public static void WriteDebug(string message,params object[] args){}}
""";
    private const string ManagerLeaves = """
 private static bool HasSpell(string name)=>World.Known;
 private static bool IsSpellAvailable(WoWSpell spell,uint lag,bool lagPath){World.AvailabilityReads++;World.Lag=lag;World.LagPath=lagPath;return World.Available;}
""";
    private const string Cases = """
public static class MetadataCases {
 private sealed class Failure(string reason):Exception(reason){}
 private static int id;
 private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
 private static List<string> Valid(string cast="1500",string funnel="false",string min="1.5",string max="30.5",string cost="75")=>
  new List<string>{"Selected","Rank 1","Icon",cost,funnel,"0",cast,min,max};
 private static WoWSpell Reset(){
  StyxWoW.Me=new LocalPlayer();StyxWoW.WoWClient=new Client();World.Known=World.Available=true;
  World.AvailabilityReads=0;World.Lag=0;World.LagPath=false;Lua.Reads=0;Lua.StockMode=null;Lua.Values=Valid();return new WoWSpell{Id=++id};
 }
 private static List<string> Invalid(string mode){
  if(mode=="null")return null;if(mode=="empty")return new List<string>();if(mode=="short")return new List<string>{"Selected"};
  var values=Valid();switch(mode){
   case "missing-name":values[0]="";break;case "nil-name":values[0]="nil";break;
   case "bad-cost":values[3]="unknown";break;case "negative-cost":values[3]="-1";break;
   case "bad-funnel":values[4]="unknown";break;case "missing-funnel":values[4]=null;break;
   case "bad-cast":values[6]="unknown";break;case "negative-cast":values[6]="-1";break;
   case "bad-min":values[7]="unknown";break;case "negative-min":values[7]="-1";break;
   case "nan-min":values[7]="NaN";break;case "infinite-max":values[8]="Infinity";break;
   case "negative-max":values[8]="-1";break;case "inverted-range":values[7]="40";values[8]="30";break;
   default:throw new InvalidOperationException("Unknown invalid vector");
  }return values;
 }
 private static object Read(WoWSpell spell,string property)=>typeof(WoWSpell).GetProperty(property).GetValue(spell);
 private static bool Admit(WoWSpell spell,bool range=true,bool movement=true,bool lag=true)=>
  SpellManager.CanCast(spell,new WoWUnit(),range,movement,lag);
 public static void Run(){
  int passed=0,assertions=0,unexpected=0,total=0;
  void Case(string name,Action body){total++;try{body();passed++;Console.WriteLine("PASS spell metadata admission: "+name);}
   catch(Failure e){assertions++;Console.Error.WriteLine("FAIL spell metadata admission: "+name+": "+e.Message);}
   catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR spell metadata admission: "+name+": "+e);}}
  string[] invalid={"null","empty","short","missing-name","nil-name","bad-cost","negative-cost","bad-funnel","missing-funnel","bad-cast","negative-cast","bad-min","negative-min","nan-min","infinite-max","negative-max","inverted-range"};
  foreach(bool available in new[]{false,true}) Case("unavailable optional latency retains strict current readiness/"+available,()=>{
   var spell=Reset();World.Available=available;
   StyxWoW.WoWClient.Error=new ObservationUnavailableException("network-latency","original observed invalid ring");
   bool admitted=false;Exception escaped=null;try{admitted=Admit(spell);}catch(Exception e){escaped=e;}
   Check(escaped==null&&admitted==available&&World.AvailabilityReads==1&&World.Lag==0&&!World.LagPath,
     "optional network timing blocked admission instead of consulting strict current spell readiness");
  });
  Case("active cast rejects without querying optional network timing",()=>{
   var spell=Reset();StyxWoW.Me.IsCasting=true;
   StyxWoW.WoWClient.Error=new ObservationUnavailableException("network-latency","unavailable");
   bool admitted=false;Exception escaped=null;try{admitted=Admit(spell);}catch(Exception e){escaped=e;}
   Check(escaped==null&&!admitted&&StyxWoW.WoWClient.Reads==0&&World.AvailabilityReads==0,
     "already-casting guard depends on unrelated network timing");
  });
  Case("unrepresentable lag allowance uses strict admission rather than overflow",()=>{
   var spell=Reset();StyxWoW.WoWClient.Value=uint.MaxValue;Check(Admit(spell),"ready spell lost strict admission");
   Check(World.AvailabilityReads==1&&World.Lag==0&&!World.LagPath,"overflowing latency created a permissive cast window");
  });
  Case("latency cancellation is not a strict-admission fallback",()=>{
   var spell=Reset();var stop=new OperationCanceledException("cancel");StyxWoW.WoWClient.Error=stop;
   Exception escaped=null;try{_=Admit(spell);}catch(Exception e){escaped=e;}
   Check(ReferenceEquals(escaped,stop)&&World.AvailabilityReads==0,"cancellation became permission to continue casting");
  });
  foreach(bool available in new[]{false,true}) Case("self support never traces a zero-length segment/"+available,()=>{
   var spell=Reset();Lua.Values=Valid(cast:"0",min:"0",max:"30");World.Available=available;
   var self=StyxWoW.Me;self.Distance=0;self.ThrowOnSight=true;
   bool admitted=false;Exception escaped=null;
   try{admitted=SpellManager.CanCast(spell,self,true,true,true);}catch(Exception e){escaped=e;}
   Check(escaped==null&&self.SightReads==0&&admitted==available&&World.AvailabilityReads==1,
     "self-target blessing/aura admission queried invalid collision geometry instead of current spell readiness");
  });
  Case("distinct nearby recipient still needs observed sight",()=>{
   var spell=Reset();Lua.Values=Valid(cast:"0",min:"0",max:"30");var other=new WoWUnit{Distance=0,Sight=false};
   Check(!SpellManager.CanCast(spell,other,true,true,true)&&other.SightReads==1,"coincident foreign target borrowed self visibility");
  });
  foreach(string mode in invalid)
  foreach(string property in new[]{"PowerCost","IsFunnel","CastTime","MinRange","MaxRange"}){
   Case("invalid-then-valid/"+mode+"/"+property,()=>{
    var spell=Reset();Lua.Values=Invalid(mode);Read(spell,property);
    Lua.Values=Valid(funnel:"true");object actual=Read(spell,property);
    object expected=property=="PowerCost"?(object)75:property=="IsFunnel"?(object)true:property=="CastTime"?(object)1500U:property=="MinRange"?(object)1.5f:30.5f;
    Check(actual.Equals(expected),"invalid observation poisoned a later valid metadata value");
    Check(Lua.Reads>=2,"invalid observation was retained as a successful cache entry");
   });
  }
  foreach(string mode in invalid)
  foreach(bool warmed in new[]{false,true}){
   Case("actual-host-invalid/"+mode+"/"+warmed,()=>{
    var spell=Reset();if(warmed){Check(Admit(spell),"healthy warmup denied");}
    Lua.Values=Invalid(mode);int before=World.AvailabilityReads;
    Check(!Admit(spell),"unknown/invalid current metadata acquired actual host admission");
    Check(World.AvailabilityReads==before,"invalid metadata reached the readiness leaf");
    Lua.Values=Valid(cast:"0");StyxWoW.Me.IsMoving=true;
    Check(Admit(spell),"a subsequent explicitly valid instant observation did not recover");
   });
  }
  foreach(string mode in new[]{"cast-to-instant","instant-to-cast","ordinary-to-funnel","funnel-to-ordinary","range-shrinks","range-expands","minimum-increases","minimum-decreases"}){
   Case("actual-host-transition/"+mode,()=>{
    var spell=Reset();Lua.Values=Valid(cast:"0",min:"0",max:"30");StyxWoW.Me.IsMoving=true;
    bool expectedFirst=true,expectedNext=false;
    if(mode=="cast-to-instant"){Lua.Values[6]="1500";expectedFirst=false;expectedNext=true;}
    if(mode=="funnel-to-ordinary"){Lua.Values[4]="true";expectedFirst=false;expectedNext=true;}
    if(mode=="range-expands"){Lua.Values[8]="5";expectedFirst=false;expectedNext=true;}
    if(mode=="minimum-decreases"){Lua.Values[7]="20";expectedFirst=false;expectedNext=true;}
    Check(Admit(spell)==expectedFirst,"initial valid admission policy changed");
    Lua.Values=Valid(cast:"0",min:"0",max:"30");
    if(mode=="instant-to-cast")Lua.Values[6]="1500";
    if(mode=="ordinary-to-funnel")Lua.Values[4]="true";
    if(mode=="range-shrinks")Lua.Values[8]="5";
    if(mode=="minimum-increases")Lua.Values[7]="20";
    Check(Admit(spell)==expectedNext,"actual host reused stale cast/funnel/range metadata");
   });
  }
  foreach(string culture in new[]{"en-US","fr-FR","de-DE"}){
   Case("original-numeric-format/"+culture,()=>{
    var old=CultureInfo.CurrentCulture;try{CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo(culture);
     var spell=Reset();Check(Admit(spell),"invariant original numeric observation denied under local culture");
     Check(spell.MinRange==1.5f&&spell.MaxRange==30.5f,"dot-decimal Lua range changed with Windows culture");
    }finally{CultureInfo.CurrentCulture=old;}
   });
  }
  foreach(bool moving in new[]{false,true})
  foreach(bool funnel in new[]{false,true})
  foreach(uint cast in new uint[]{0,1500})
  foreach(bool checkMovement in new[]{false,true}){
   Case("preserved-movement/"+moving+"/"+funnel+"/"+cast+"/"+checkMovement,()=>{
    var spell=Reset();Lua.Values=Valid(cast.ToString(CultureInfo.InvariantCulture),funnel?"true":"false");StyxWoW.Me.IsMoving=moving;
    Check(Admit(spell,movement:checkMovement)==(!checkMovement||!moving||cast==0&&!funnel),"explicit movement-check policy changed");
   });
  }
  foreach(string mode in new[]{"null-spell","unknown","no-player","no-los","already-casting","unavailable","zero-range-melee","zero-range-not-melee","range-optout","nonlag","channeled-nonlag"}){
   Case("preserved-admission/"+mode,()=>{
    var spell=Reset();var target=new WoWUnit();bool range=true,lag=true,expected=false;
    if(mode=="null-spell")spell=null;
    if(mode=="unknown")World.Known=false;
    if(mode=="no-player")StyxWoW.Me=null;
    if(mode=="no-los")target.InLineOfSpellSight=false;
    if(mode=="already-casting")StyxWoW.Me.IsCasting=true;
    if(mode=="unavailable")World.Available=false;
    if(mode.StartsWith("zero-range")){Lua.Values=Valid(min:"0",max:"0");target.IsWithinMeleeRange=mode=="zero-range-melee";expected=target.IsWithinMeleeRange;}
    if(mode=="range-optout"){target.Distance=100;range=false;expected=true;}
    if(mode=="nonlag"){lag=false;expected=true;}
    if(mode=="channeled-nonlag"){StyxWoW.Me.ChanneledCastingSpellId=123;expected=true;}
    Check(SpellManager.CanCast(spell,target,range,true,lag)==expected,"existing admission/explicit opt-out changed");
    if(mode=="null-spell"||mode=="unknown"||mode=="no-player")Check(Lua.Reads==0,"failed cheap admission performed API reads");
    if(mode=="nonlag"||mode=="channeled-nonlag")Check(!World.LagPath&&World.Lag==0,"non-lag path changed");
   });
  }
  foreach(string mode in new[]{"ordinary","funnel","missing","missing-funnel"})
  foreach(bool moving in new[]{false,true}){
   Case("actual-stock-Lua51/"+mode+"/"+moving,()=>{
    var spell=Reset();Lua.StockMode=mode;StyxWoW.Me.IsMoving=moving;
    bool expected=mode=="ordinary"||mode=="funnel"&&!moving;
    Check(Admit(spell)==expected,"actual generated Lua/C string conversion lost funnel validity or admitted unavailable metadata");
    if(mode=="funnel")Check(spell.IsFunnel,"actual Lua true funnel was lost by lua_tolstring conversion");
    if(mode=="ordinary")Check(!spell.IsFunnel&&spell.CastTime==0&&spell.MaxRange==30.5f,"valid original zero/false metadata changed");
   });
  }
  Console.WriteLine($"Spell metadata admission scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; complete tracked metadata owner and actual host movement/range gate; generated requests also run under stock Lua5.1 C conversion; controlled nine-value API/world/readiness; no client/proc/game execution.");
  if(assertions+unexpected!=0)throw new InvalidOperationException("Spell metadata admission regression");
 }
}
""";
    private const string StockSetup = """
function GetSpellInfo(id)
 if scenario=='missing' then return nil end
 local funnel=scenario=='funnel'
 if scenario=='missing-funnel' then funnel=nil end
 return 'Selected','Rank 1','Icon',75,funnel,0,0,1.5,30.5
end
""";
}
