using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Complete tracked mount-up, companion lookup/dispatch and speed-form fallback
// owners. Eligibility/metadata/native leaves and time are controlled; no game,
// geometry, real mount success or server acknowledgement is certified.
internal static class HostMountAdmissionRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "CopilotBuddy.csproj"))) directory = directory.Parent;
        if (directory == null) throw new InvalidOperationException("Tracked checkout required.");
        var owner = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(directory.FullName, "Styx/Logic/Mount.cs")))
            .GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "Mount");
        var methods = owner.Members.OfType<MethodDeclarationSyntax>().Where(m =>
            m.Identifier.ValueText == "MountUp" && m.ParameterList.Parameters.Count == 1 && m.ParameterList.Parameters[0].Type?.ToString() is "CanMountDelegate" or "LocationRetriever"
            || m.Identifier.ValueText is "DoMount" or "GetMountIndex" or "TryUseShapeshiftSpeedBuff").ToArray();
        if (methods.Length != 5) throw new InvalidOperationException("Complete tracked public/private mount owners and nearby-target wrapper required.");
        string source = Prefix + "\npublic static class HostMountOwner {\n" +
            "public delegate bool CanMountDelegate();public delegate WoWPoint LocationRetriever();private static LocalPlayer Me=>World.Player;private static float MountDistance=>30;\n" +
            "private static readonly Timer _mountTimer=new Timer();private static LocationRetriever _currentDestinationRetriever=()=>{World.AfterDestination?.Invoke();return WoWPoint.Empty;};\n" +
            "private static void AutoDetectMount()=>World.AfterDetect?.Invoke();\n" +
            "private static bool CanMount()=>World.Ready&&World.Player!=null&&World.Player.IsAlive&&!World.Player.Combat&&!World.Player.IsSwimming&&World.Player.IsOutdoors;\n" +
            "private static bool AllowMountAttempt(bool flying,string name,WoWPoint destination){World.AfterAdmission?.Invoke();return World.AdmissionAllowed;}\n" +
            "private static void AddCantMountSpot(WoWPoint point)=>World.Record(\"cant-mount\");\n" +
            "private static void RemoveCantMountSpotsNear(WoWPoint point,float radius)=>World.Record(\"clear-spots\");\n" +
            "public static bool Invoke()=>MountUp(()=>{World.AfterExtra?.Invoke();return World.ExtraAllowed;});\n" +
            "public static void InvokeNearby()=>MountUp(new LocationRetriever(()=>{World.AfterDestination?.Invoke();return WoWPoint.Empty;}));\n" +
            string.Join("\n", methods.Select(m => m.ToString())) + "}\n" + Cases;
        var trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? throw new InvalidOperationException("Runtime references required.");
        var compilation = CSharpCompilation.Create("W110HostMountAdmission_" + Guid.NewGuid().ToString("N"), new[] { CSharpSyntaxTree.ParseText(source) },
            trusted.Split(Path.PathSeparator).Distinct(StringComparer.OrdinalIgnoreCase).Select(p => MetadataReference.CreateFromFile(p)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream(); var result = compilation.Emit(output);
        if (!result.Success) throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var assembly = Assembly.Load(output.ToArray());
        try { assembly.GetType("HostMountAdmissionCases", true)!.GetMethod("Run")!.Invoke(null, null); }
        catch (TargetInvocationException error) when (error.InnerException != null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }

    private const string Prefix = """
#nullable disable
using System;using System.Collections.Generic;using System.Linq;
public enum WoWRace {Human,BloodElf}
public enum WoWClass {Paladin,Druid,Shaman}
public sealed class WoWUnit {public float Distance;}
public sealed class Targeting {public static Targeting Instance=new Targeting();public WoWUnit FirstUnit;}
public struct WoWPoint {public static WoWPoint Empty=>default;}
public sealed class LocalPlayer {
 public ulong Guid=123;public bool IsValid=true,IsAlive=true,IsGhost,Mounted,Combat,IsSwimming,IsMoving,IsOutdoors=true;public int Level=80;
 public bool Dead=>!IsAlive;public WoWRace Race;public WoWClass Class;public string LastRedErrorMessage="";public WoWPoint Location;
 public bool HasAura(string name)=>World.Auras.Contains(name);
}
public sealed class LevelbotSettings {public static LevelbotSettings Instance=new LevelbotSettings();public bool UseMount=true;public string MountName="Horse";}
public sealed class CharacterSettings {public static CharacterSettings Instance=new CharacterSettings();public string FlyingMountName="Flying mount";}
public static class Flightor {public static bool CanFly=>World.FlyEnabled;}
public static class MountHelper {public static List<object> GroundMounts=new List<object>{new object()};}
public static class Environment {public static int TickCount;}
public sealed class Timer {public void Reset(){World.TimerResets++;}}
public static class World {
 public static LocalPlayer Player;public static readonly List<string> Commands=new List<string>();public static readonly HashSet<string> Spells=new HashSet<string>(),Auras=new HashSet<string>();
 public static bool Ready=true,ExtraAllowed=true,AdmissionAllowed=true,FlyEnabled,Acknowledge=true,SpellResult=true,StopWorks=true;public static int TimerResets;
 public static string IndexResult="1",LookupText="";
 public static System.Action AfterExtra,AfterDetect,AfterDestination,AfterAdmission,AfterStop,AfterLog,AfterLookup,AfterDispatch;
 public static System.Action<int> AfterSleep;
 public static void Reset(){Player=new LocalPlayer();Commands.Clear();Spells.Clear();Auras.Clear();Ready=true;ExtraAllowed=true;AdmissionAllowed=true;
  FlyEnabled=false;Acknowledge=true;SpellResult=true;StopWorks=true;TimerResets=0;IndexResult="1";LookupText="";AfterExtra=null;AfterDetect=null;AfterDestination=null;
  AfterAdmission=null;AfterStop=null;AfterLog=null;AfterLookup=null;AfterDispatch=null;AfterSleep=null;Environment.TickCount=0;
  LevelbotSettings.Instance=new LevelbotSettings();CharacterSettings.Instance=new CharacterSettings();MountHelper.GroundMounts=new List<object>{new object()};}
 public static void Record(string command)=>Commands.Add(command+":"+(Player?.Guid??0));
}
public static class Logging {
 public static void Write(string text,params object[] args)=>World.AfterLog?.Invoke();
 public static void WriteDebug(string text,params object[] args){}
}
public static class WoWMovement {public static void MoveStop(){World.Record("stop");if(World.StopWorks&&World.Player!=null)World.Player.IsMoving=false;World.AfterStop?.Invoke();}}
public static class StyxWoW {public static void Sleep(int milliseconds){Environment.TickCount+=milliseconds;World.AfterSleep?.Invoke(milliseconds);}}
public static class SpellManager {
 public static bool HasSpell(string name)=>World.Spells.Contains(name);
 public static bool Cast(string name){World.Record("spell-"+name);World.AfterDispatch?.Invoke();return World.SpellResult;}
}
public static class Lua {
 public static T GetReturnVal<T>(string code,int result){World.LookupText=code;World.AfterLookup?.Invoke();return (T)(object)World.IndexResult;}
 public static void DoString(string code){World.Record(code);if(World.Acknowledge&&World.Player!=null)World.Player.Mounted=true;World.AfterDispatch?.Invoke();}
}
""";

    private const string Cases = """
public static class HostMountAdmissionCases {
 private sealed class Failure(string reason):Exception(reason){}
 private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
 private static bool Submitted=>World.Commands.Any(c=>c.StartsWith("CallCompanion")||c.StartsWith("spell-"));
 private static bool Invoke(){try{return HostMountOwner.Invoke();}catch(NullReferenceException){throw new Failure("missing or changed player was dereferenced");}}
 private static void Change(string state){switch(state){
  case "missing":World.Player=null;break;case "invalid":World.Player.IsValid=false;break;case "dead":World.Player.IsAlive=false;break;
  case "ghost":World.Player.IsGhost=true;break;case "unknown-guid":World.Player.Guid=0;break;case "changed-guid":World.Player.Guid++;break;
  case "changed-player":World.Player=new LocalPlayer{Guid=789};break;case "mounted":World.Player.Mounted=true;break;
  case "combat":World.Player.Combat=true;break;case "swimming":World.Player.IsSwimming=true;break;case "indoors":World.Player.IsOutdoors=false;break;
  case "disabled":LevelbotSettings.Instance.UseMount=false;break;case "not-ready":World.Ready=false;break;
  case "different-mount":LevelbotSettings.Instance.MountName="Other horse";break;case "different-flight-context":World.FlyEnabled=true;break;}}
 public static void Run(){
  var tests=new List<(string Name,System.Action Body)>();
  foreach(bool flying in new[]{false,true}){
   bool fly=flying;tests.Add(((fly?"flying":"ground")+" selected companion",()=>{World.Reset();World.FlyEnabled=fly;
    Check(Invoke()&&Submitted&&World.TimerResets==1,"ordinary mount dispatch/backoff changed");
    Check(World.LookupText.Contains("string.lower('"+(fly?"Flying mount":"Horse")+"')"),"selected mount identity changed before lookup");}));
  }
  tests.Add(("unacknowledged dispatch remains only an attempt",()=>{World.Reset();World.Acknowledge=false;Check(Invoke()&&Submitted&&World.TimerResets==1&&!World.Player.Mounted,"attempt receipt incorrectly depended on server acknowledgement");}));
  tests.Add(("failed movement stop cannot authorize mount",()=>{World.Reset();World.Player.IsMoving=true;World.StopWorks=false;Check(!Invoke()&&!Submitted&&World.TimerResets==0,"failed stop authorized a mount cast");}));
  tests.Add(("successful movement stop retains mount",()=>{World.Reset();World.Player.IsMoving=true;Check(Invoke()&&Submitted,"ordinary stop-then-mount was removed");}));
  tests.Add(("movement resumed during lookup",()=>{World.Reset();World.AfterLookup=()=>World.Player.IsMoving=true;Check(!Invoke()&&!Submitted&&World.TimerResets==0,"lookup delay reused stale stationary state");}));
  foreach(string state in new[]{"missing","invalid","dead","ghost","unknown-guid","mounted","combat","swimming","indoors","disabled","not-ready"}){
   string observed=state;tests.Add(("admission/"+state,()=>{World.Reset();Change(observed);Check(!Invoke()&&!Submitted&&World.TimerResets==0,"unavailable actor/readiness authorized a mount attempt");}));
  }
  foreach(string phase in new[]{"extra","detect","destination","admission","stop","log","sleep","lookup"}){
   string boundary=phase;foreach(string state in new[]{"changed-player","changed-guid","invalid","dead","ghost","mounted","combat","swimming","indoors","disabled","not-ready"}){
    string observed=state;tests.Add((boundary+"/"+state,()=>{World.Reset();System.Action change=()=>Change(observed);
     switch(boundary){case "extra":World.AfterExtra=change;break;case "detect":World.AfterDetect=change;break;case "destination":World.AfterDestination=change;break;
      case "admission":World.AfterAdmission=change;break;case "stop":World.AfterStop=change;break;case "log":World.AfterLog=change;break;
      case "sleep":World.AfterSleep=ms=>{if(ms==200)change();};break;case "lookup":World.AfterLookup=change;break;}
     Check(!Invoke()&&!Submitted&&World.TimerResets==0,"stale actor/readiness retained dispatch or timer permission");
    }));
   }
  }
  foreach(string phase in new[]{"admission","stop","log","sleep","lookup"}){
   string boundary=phase;foreach(string state in new[]{"different-mount","different-flight-context"}){
    string observed=state;tests.Add((boundary+"/"+state,()=>{World.Reset();System.Action change=()=>Change(observed);
     switch(boundary){case "admission":World.AfterAdmission=change;break;case "stop":World.AfterStop=change;break;case "log":World.AfterLog=change;break;
      case "sleep":World.AfterSleep=ms=>{if(ms==200)change();};break;case "lookup":World.AfterLookup=change;break;}
     Check(!Invoke()&&!Submitted&&World.TimerResets==0,"a changed mount selection inherited the earlier authorization");
    }));
   }
  }
  foreach(string result in new[]{"0","-1","","unreadable"}){
   string value=result;tests.Add(("unavailable companion index/"+result,()=>{World.Reset();World.IndexResult=value;Check(!Invoke()&&!Submitted&&World.TimerResets==0,"invalid lookup became a native mount attempt");}));
  }
  foreach(string name in new[]{"Warhorse","Summon Warhorse","Charger","Summon Charger","34769"}){
   string configured=name;tests.Add(("manual Blood Elf companion/"+name,()=>{World.Reset();World.Player.Race=WoWRace.BloodElf;World.Player.Class=WoWClass.Paladin;LevelbotSettings.Instance.MountName=configured;
    Check(Invoke()&&World.LookupText.Contains("string.lower('"+configured+"')"),"manual companion name/ID was silently substituted");}));
  }
  tests.Add(("lost actor after dispatch receives no bookkeeping",()=>{World.Reset();World.AfterDispatch=()=>Change("changed-player");Check(!Invoke()&&Submitted&&World.TimerResets==0
   &&!World.Commands.Any(c=>c=="clear-spots:789"),"post-dispatch continuation changed replacement actor state");}));
  foreach(string form in new[]{"Ghost Wolf","Travel Form"}){
   string spell=form;foreach(bool accepted in new[]{false,true}){
    bool success=accepted;tests.Add(("speed-form cast receipt/"+spell+"/"+success,()=>{World.Reset();MountHelper.GroundMounts.Clear();World.Spells.Add(spell);World.SpellResult=success;
     Check(Invoke()==success&&World.Commands.Contains("spell-"+spell+":123"),"speed-form return ignored the actual cast receipt");}));
   }
   foreach(string state in new[]{"changed-player","changed-guid","dead","invalid","indoors"}){
    string observed=state;tests.Add(("speed-form log boundary/"+spell+"/"+state,()=>{World.Reset();MountHelper.GroundMounts.Clear();World.Spells.Add(spell);World.AfterLog=()=>Change(observed);
     Check(!Invoke()&&!Submitted,"speed fallback cast on a stale actor/readiness");}));
   }
  }
  tests.Add(("combat Travel Form control",()=>{World.Reset();World.Player.Combat=true;World.Player.Class=WoWClass.Druid;World.Spells.Add("Travel Form");Check(Invoke()&&World.Commands.Contains("spell-Travel Form:123"),"existing instant combat speed-form control was removed");}));
  tests.Add(("extra veto",()=>{World.Reset();World.ExtraAllowed=false;Check(!Invoke()&&!Submitted,"caller veto ignored");}));
  tests.Add(("mount event veto",()=>{World.Reset();World.AdmissionAllowed=false;Check(!Invoke()&&!Submitted,"mount event veto ignored");}));
  foreach(string phase in new[]{"detect","destination","admission","stop","log","sleep","lookup"}){
   string boundary=phase;tests.Add(("caller permission revoked/"+boundary,()=>{World.Reset();System.Action revoke=()=>World.ExtraAllowed=false;
    switch(boundary){case "detect":World.AfterDetect=revoke;break;case "destination":World.AfterDestination=revoke;break;
     case "admission":World.AfterAdmission=revoke;break;case "stop":World.AfterStop=revoke;break;case "log":World.AfterLog=revoke;break;
     case "sleep":World.AfterSleep=ms=>{if(ms==200)revoke();};break;case "lookup":World.AfterLookup=revoke;break;}
    Check(!Invoke()&&!Submitted&&World.TimerResets==0,"caller permission (including nearby-target veto) survived setup/wait/lookup");
   }));
  }
  foreach(string phase in new[]{"initial","detect","destination","admission","stop","log","sleep","lookup"}){
   string boundary=phase;tests.Add(("actual nearby-target wrapper/"+boundary,()=>{World.Reset();Targeting.Instance.FirstUnit=null;
    System.Action approach=()=>Targeting.Instance.FirstUnit=new WoWUnit{Distance=10};
    switch(boundary){case "initial":approach();break;case "detect":World.AfterDetect=approach;break;
     case "destination":World.AfterDestination=approach;break;case "admission":World.AfterAdmission=approach;break;
     case "stop":World.AfterStop=approach;break;case "log":World.AfterLog=approach;break;
     case "sleep":World.AfterSleep=ms=>{if(ms==200)approach();};break;case "lookup":World.AfterLookup=approach;break;}
    HostMountOwner.InvokeNearby();Check(!Submitted&&World.TimerResets==0,"real nearby-target veto was not rechecked before native submission");
    Targeting.Instance.FirstUnit=null;
   }));
  }
  tests.Add(("actual distant-target wrapper keeps normal mounting",()=>{World.Reset();Targeting.Instance.FirstUnit=new WoWUnit{Distance=45};
   HostMountOwner.InvokeNearby();Check(Submitted&&World.TimerResets==1,"distant target needlessly denied mounting");Targeting.Instance.FirstUnit=null;}));
  int passed=0,assertions=0,unexpected=0;
  foreach(var test in tests){try{test.Body();passed++;Console.WriteLine("PASS host mount admission: "+test.Name);}
   catch(Failure error){assertions++;Console.Error.WriteLine("FAIL host mount admission: "+test.Name+": "+error.Message);}
   catch(Exception error){unexpected++;Console.Error.WriteLine("ERROR host mount admission: "+test.Name+": "+error);}}
  Console.WriteLine($"Host mount admission scenarios: {passed}/{tests.Count}; assertions={assertions}; unexpected={unexpected}; complete tracked owners, controlled eligibility/metadata/native/time leaves; no game attached.");
  if(assertions+unexpected!=0)throw new InvalidOperationException($"Host mount admission regressions: assertions={assertions}; unexpected={unexpected}");
 }
}
""";
}
