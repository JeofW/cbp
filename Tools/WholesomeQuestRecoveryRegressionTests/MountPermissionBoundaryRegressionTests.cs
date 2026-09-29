using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// The earlier mount-dispatch fixture deliberately controls permission leaves.
// This separate fixture executes the complete real host/flying permission owners
// and the complete Flightor mount owner, including its real post-wait control flow.
internal static class MountPermissionBoundaryRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "CopilotBuddy.csproj"))) root = root.Parent;
        if (root == null) throw new InvalidOperationException("Tracked checkout required.");
        SyntaxNode Read(string path) => CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName, path))).GetRoot();
        var host = Read("Styx/Logic/Mount.cs").DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "Mount");
        var hostPermission = host.Members.OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.ValueText == "CanMount");
        var flight = Read("Styx/Logic/Pathing/Flightor.cs").DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "MountHelper");
        var flightPermission = flight.Members.OfType<PropertyDeclarationSyntax>().Single(p => p.Identifier.ValueText == "CanMount");
        var flightDispatch = flight.Members.OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.ValueText == "MountUpInternal");
        string source = Prefix + "\npublic static class HostPermission {private static LocalPlayer Me=>World.Player;" +
            "private static readonly Timer _combatTimer=new Timer(\"host-combat\"),_mountTimer=new Timer(\"host-mount\");" +
            "private static readonly List<WoWPoint> _cantMountSpots=new List<WoWPoint>();" +
            "private static void AddCantMountSpot(WoWPoint point)=>World.Commands.Add(\"blacklist\");\n" + hostPermission + "\n}\n" +
            "public static class FlyingPermission {public static WoWSpell FlyingMount {get{World.Hit(\"selection\");return World.Selected;}}" +
            "public static bool Mounted=>World.Player?.Mounted==true;\n" + flightPermission + "\n" + flightDispatch + "\n}\n" + Cases;
        string trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? throw new InvalidOperationException("Runtime references required.");
        var compilation = CSharpCompilation.Create("W110MountPermission_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(source) },
            trusted.Split(Path.PathSeparator).Distinct(StringComparer.OrdinalIgnoreCase).Select(p => MetadataReference.CreateFromFile(p)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream(); var emitted = compilation.Emit(stream);
        if (!emitted.Success) throw new InvalidOperationException(string.Join("; ", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var assembly = Assembly.Load(stream.ToArray());
        try { assembly.GetType("MountPermissionCases", true)!.GetMethod("Run")!.Invoke(null, null); }
        catch (TargetInvocationException error) when (error.InnerException != null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }

    private const string Prefix = """
#nullable disable
using System;using System.Collections.Generic;using System.Linq;
public enum WoWClass {Paladin,Druid}
public struct WoWPoint {public float X,Y,Z;public WoWPoint(float x,float y,float z){X=x;Y=y;Z=z;}
 public static WoWPoint operator+(WoWPoint a,WoWPoint b)=>new WoWPoint(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
 public float Distance(WoWPoint other)=>MathF.Sqrt((X-other.X)*(X-other.X)+(Y-other.Y)*(Y-other.Y)+(Z-other.Z)*(Z-other.Z));}
public sealed class LocalPlayer {
 public ulong Guid=123;public bool IsValid=true,IsAlive=true,IsGhost,Combat,IsSwimming,IsMoving,Mounted,InVehicle,IsOnTransport;
 public bool IsOutdoors=true;public uint MapId=530;public int Level=80;public float BoundingHeight=2;public WoWClass Class;
 public bool Dead=>!IsAlive;public WoWPoint Location;public WoWPoint GetTraceLinePos()=>Location+new WoWPoint(0,0,2);
}
public sealed class WoWSpell {public int Id=32235;public string Name="Gryphon";public int CastTime=3000;}
public sealed class CharacterSettings {public static CharacterSettings Instance=new CharacterSettings();public string FlyingMountName="Gryphon";}
public sealed class LevelbotSettings {public static LevelbotSettings Instance=new LevelbotSettings();public bool UseMount=true;}
public sealed class Timer(string name){public bool IsFinished{get{World.Hit(name);return World.TimersReady;}}}
public static class MountHelper {public static int NumMounts{get{World.Hit("mount-list");return World.MountCount;}}}
public static class Mount {
 public static bool AreMountTimersReady {get{World.Hit("flight-timer");return World.TimersReady;}}
 public static void ClearShapeshift(){World.Record("clear");World.Hit("clear");}
 public static void ResetMountTimer(){World.Record("timer");}
}
public static class StyxWoW {
 public static LocalPlayer Me=>World.Player;
 public static void SleepForLagDuration(){World.Record("lag");World.Hit("lag");}
 public static void Sleep(int milliseconds){World.Record("sleep");World.Hit("sleep");}
}
public static class GameWorld {
 public enum CGWorldFrameHitFlags {HitTestLiquid,HitTestLOS}
 public static bool TraceLine(WoWPoint a,WoWPoint b,CGWorldFrameHitFlags flags){World.Hit(flags==CGWorldFrameHitFlags.HitTestLiquid?"liquid":"ceiling");return flags==CGWorldFrameHitFlags.HitTestLiquid?World.Liquid:World.Blocked;}
}
public static class SpellManager {
 public static bool HasSpell(string name){World.Hit("skill");return World.ColdWeather;}
 public static bool HasSpell(int id)=>World.Selected!=null&&World.Selected.Id==id;
 public static bool Cast(WoWSpell spell){World.Record("cast-"+spell.Id);World.Hit("dispatch");return World.CastAccepted;}
}
public static class Logging {public static void Write(string text,params object[] args)=>World.Hit("log");}
public sealed class PlayerMover {public void MoveStop(){World.Record("stop");if(World.StopWorks&&World.Player!=null)World.Player.IsMoving=false;World.Hit("stop");}}
public static class Navigator {public static bool IsRidingElevator{get{World.Hit("elevator");return World.Elevator;}}public static PlayerMover PlayerMover=new PlayerMover();}
public static class World {
 public static LocalPlayer Player;public static WoWSpell Selected;
 public static readonly List<string> Commands=new List<string>();public static Action<string> OnBoundary;
 public static bool TimersReady,ColdWeather,Blocked,Liquid,Elevator,StopWorks,CastAccepted;public static int MountCount;
 public static void Hit(string name)=>OnBoundary?.Invoke(name);
 public static void Record(string command)=>Commands.Add(command+":"+(Player?.Guid??0));
 public static void Reset(){Player=new LocalPlayer();Selected=new WoWSpell();Commands.Clear();OnBoundary=null;
  TimersReady=ColdWeather=StopWorks=CastAccepted=true;Blocked=Liquid=Elevator=false;MountCount=1;
  CharacterSettings.Instance=new CharacterSettings();LevelbotSettings.Instance=new LevelbotSettings();}
}
""";

    private const string Cases = """
public static class MountPermissionCases {
 private sealed class Failure(string message):Exception(message){}
 private static void Check(bool value,string message){if(!value)throw new Failure(message);}
 private static bool Permit(bool flying){try{return flying?FlyingPermission.CanMount:HostPermission.CanMount();}catch(NullReferenceException){throw new Failure("changed or missing player dereferenced");}}
 private static void Invoke(bool quick){try{FlyingPermission.MountUpInternal(quick);}catch(NullReferenceException){throw new Failure("changed or missing player dereferenced");}}
 private static bool Cast=>World.Commands.Any(c=>c.StartsWith("cast-"));
 private static void Change(string name){switch(name){
  case "missing":World.Player=null;break;case "replacement":World.Player=new LocalPlayer{Guid=789};break;
  case "same-guid-replacement":World.Player=new LocalPlayer();break;case "changed-guid":World.Player.Guid++;break;
  case "zero-guid":World.Player.Guid=0;break;case "dead":World.Player.IsAlive=false;break;case "invalid":World.Player.IsValid=false;break;
  case "ghost":World.Player.IsGhost=true;break;case "combat":World.Player.Combat=true;break;case "indoors":World.Player.IsOutdoors=false;break;
  case "mounted":World.Player.Mounted=true;break;case "moving":World.Player.IsMoving=true;break;
  case "blocked":World.Blocked=true;break;case "no-mount":World.Selected=null;World.MountCount=0;break;
  case "wrong-map":World.Player.MapId=0;break;case "elevator":World.Elevator=true;break;
  case "selection":CharacterSettings.Instance.FlyingMountName="Different mount";World.Selected=new WoWSpell{Id=32239,Name="Different mount"};break;
 }}
 public static void Run(){var tests=new List<(string Name,Action Body)>();
  foreach(bool value in new[]{false,true}){bool flying=value;string prefix=flying?"flying permission/":"host permission/";
   tests.Add((prefix+"normal",()=>{World.Reset();Check(Permit(flying),"ordinary eligible actor rejected");}));
   foreach(string state in new[]{"missing","zero-guid","dead","invalid","ghost","combat","indoors"}){string changed=state;
    tests.Add((prefix+"initial/"+changed,()=>{World.Reset();Change(changed);Check(!Permit(flying),"unavailable actor gained permission");}));}
   foreach(string stage in flying?new[]{"skill","selection","liquid","flight-timer","ceiling"}:new[]{"mount-list","host-combat","host-mount","ceiling"}){
    string boundary=stage;foreach(string state in new[]{"replacement","same-guid-replacement","changed-guid","missing","dead","invalid","ghost","combat","indoors"}){string changed=state;
     tests.Add((prefix+boundary+"/"+changed,()=>{World.Reset();bool applied=false;World.OnBoundary=name=>{if(name==boundary&&!applied){applied=true;Change(changed);}};
      Check(!Permit(flying)&&!World.Commands.Contains("blacklist"),"changed actor retained permission or received blacklist bookkeeping");}));}
   }
  }
  tests.Add(("host ceiling hit after replacement does not blacklist",()=>{World.Reset();World.OnBoundary=name=>{if(name=="ceiling"){Change("replacement");World.Blocked=true;}};
   Check(!Permit(false)&&!World.Commands.Contains("blacklist"),"old observation blacklisted a location after actor replacement");}));
  tests.Add(("host real ceiling hit stays a geometry refusal",()=>{World.Reset();World.Blocked=true;Check(!Permit(false)&&World.Commands.Contains("blacklist"),"existing ceiling refusal changed");}));
  tests.Add(("flying cold-weather map gate",()=>{World.Reset();World.Player.MapId=571;World.ColdWeather=false;Check(!Permit(true),"Northrend missing skill admitted");}));
  tests.Add(("flying water cooldown bypass remains eligibility only",()=>{World.Reset();World.TimersReady=false;World.Liquid=true;Check(Permit(true),"retained surface movement eligibility lost");}));
  foreach(bool fast in new[]{false,true}){bool quick=fast;string prefix=quick?"quick mount/":"normal mount/";
   tests.Add((prefix+"valid dispatch",()=>{World.Reset();World.Player.IsMoving=true;Invoke(quick);Check(Cast&&World.Commands.Contains("timer:123"),"ordinary mount attempt removed");}));
   tests.Add((prefix+"failed stop",()=>{World.Reset();World.Player.IsMoving=true;World.StopWorks=false;Invoke(quick);Check(!Cast&&!World.Commands.Any(c=>c.StartsWith("timer")),"failed stop authorized mount");}));
   tests.Add((prefix+"refused cast",()=>{World.Reset();World.CastAccepted=false;Invoke(quick);Check(Cast&&!World.Commands.Any(c=>c.StartsWith("timer")),"refused submission fabricated a successful attempt timer");}));
   tests.Add((prefix+"flight form avoids caster transition",()=>{World.Reset();World.Player.Class=WoWClass.Druid;World.Selected=new WoWSpell{Id=33943,Name="Flight Form",CastTime=0};
    CharacterSettings.Instance.FlyingMountName="Flight Form";Invoke(quick);Check(Cast&&!World.Commands.Any(c=>c.StartsWith("clear")),"druid flight transition cancelled its old form");}));
   foreach(string stage in quick?new[]{"clear","stop","log"}:new[]{"clear","stop","lag","log"}){string boundary=stage;
    foreach(string state in new[]{"replacement","same-guid-replacement","changed-guid","missing","invalid","dead","ghost","combat","indoors","mounted","moving","blocked","no-mount","wrong-map","elevator","selection"}){
     string changed=state;tests.Add((prefix+boundary+"/"+changed,()=>{World.Reset();World.Player.IsMoving=true;bool applied=false;
      World.OnBoundary=name=>{if(name==boundary&&!applied){applied=true;Change(changed);}};Invoke(quick);
      // Movement during ClearShapeshift may legitimately be stopped before casting.
      if(boundary=="clear"&&changed=="moving")Check(Cast,"recoverable pre-stop movement denied valid attempt");
      else Check(!Cast&&!World.Commands.Any(c=>c.StartsWith("timer")),"stale post-setup permission authorized mount or bookkeeping");}));
    }
   }
   tests.Add((prefix+"replacement after dispatch has no follow-up",()=>{World.Reset();World.OnBoundary=name=>{if(name=="dispatch")Change("replacement");};Invoke(quick);
    Check(Cast&&!World.Commands.Any(c=>c.StartsWith("timer")||c.EndsWith(":789")),"follow-up was applied to replacement actor");}));
  }
  int passed=0,assertions=0,unexpected=0;
  foreach(var test in tests){try{test.Body();passed++;Console.WriteLine("PASS mount permission: "+test.Name);}
   catch(Failure error){assertions++;Console.Error.WriteLine("FAIL mount permission: "+test.Name+": "+error.Message);}
   catch(Exception error){unexpected++;Console.Error.WriteLine("ERROR mount permission: "+test.Name+": "+error);}}
  Console.WriteLine($"Mount permission and Flightor wait scenarios: {passed}/{tests.Count}; assertions={assertions}; unexpected={unexpected}; complete tracked permission and dispatch owners, controlled external observations/time; no game attached.");
  if(assertions+unexpected!=0)throw new InvalidOperationException($"Mount permission regressions: assertions={assertions}; unexpected={unexpected}");
 }
}
""";
}
