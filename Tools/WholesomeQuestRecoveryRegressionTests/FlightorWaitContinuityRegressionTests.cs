using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Complete tracked MoveTo and DoAntiStuck methods. Geometry, time, native input
// and mount observations are controlled leaves; this does not certify a route.
internal static class FlightorWaitContinuityRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
        => Build().GetType("FlightWaitCases", true)!.GetMethod("Run")!.Invoke(null, null);

    internal static void RunRawAura(Func<string, bool> names, Func<int, bool> ids, bool unavailable)
        => Build(true).GetType("FlightWaitCases", true)!.GetMethod("RunRawAura")!.Invoke(null, new object[] { names, ids, unavailable });

    private static Assembly Build(bool originalClientAura = false)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "CopilotBuddy.csproj"))) root = root.Parent;
        if (root == null) throw new InvalidOperationException("Tracked checkout required.");
        var source = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName, "Styx/Logic/Pathing/Flightor.cs"))).GetRoot();
        var owner = source.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "Flightor");
        string move = string.Join("\n", owner.Members.OfType<MethodDeclarationSyntax>().Where(m => m.Identifier.ValueText is "MoveToCore" or "MoveToGroundInteraction" or "MoveToOwnedExterior"
            || m.Identifier.ValueText == "MoveTo" && m.ParameterList.Parameters.Count == 2));
        string unstuck = string.Join("\n", owner.Members.OfType<MethodDeclarationSyntax>().Where(m => m.Identifier.ValueText == "DoAntiStuck"));
        var invalidate = owner.Members.OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.ValueText == "InvalidateRouteContext");
        string trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? throw new InvalidOperationException("Runtime references required.");
        string controls = Controls;
        if (originalClientAura)
            controls = controls.Replace("private static bool HasSeaLegs(LocalPlayer player)=>World.SeaLegs;",
                owner.Members.OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.ValueText == "HasSeaLegs").ToString());
        string generated = Prefix + "\npublic static class FlightOwner {\n" + controls + "\n" + move + "\n" + unstuck + "\n" + invalidate + "\n}\n" + Cases;
        var compilation = CSharpCompilation.Create("W110FlightWait_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(generated) },
            trusted.Split(Path.PathSeparator).Distinct(StringComparer.OrdinalIgnoreCase).Select(p => MetadataReference.CreateFromFile(p)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream(); var emitted = compilation.Emit(output);
        if (!emitted.Success) throw new InvalidOperationException(string.Join("; ", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return Assembly.Load(output.ToArray());
    }
    private const string Prefix = """
#nullable disable
using System;using System.Collections.Generic;
public enum WoWClass {Paladin,Druid}
public enum NavigationType {Fly,Run}
public enum WoWSkill {Riding}
public struct Vector2 {public float X,Y;public Vector2(float x,float y){X=x;Y=y;}}
public struct WoWPoint {
 public float X,Y,Z;public WoWPoint(float x,float y,float z){X=x;Y=y;Z=z;}
 public static WoWPoint Empty=>default;public bool IsEmpty=>this==Empty;
 public static WoWPoint operator+(WoWPoint a,WoWPoint b)=>new WoWPoint(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
 public static bool operator==(WoWPoint a,WoWPoint b)=>a.X==b.X&&a.Y==b.Y&&a.Z==b.Z;
 public static bool operator!=(WoWPoint a,WoWPoint b)=>!(a==b);public override bool Equals(object o)=>o is WoWPoint p&&this==p;public override int GetHashCode()=>HashCode.Combine(X,Y,Z);
 public float DistanceSqr(WoWPoint b)=>(X-b.X)*(X-b.X)+(Y-b.Y)*(Y-b.Y)+(Z-b.Z)*(Z-b.Z);
 public float Distance2DSqr(WoWPoint b)=>(X-b.X)*(X-b.X)+(Y-b.Y)*(Y-b.Y);
 public float Distance(WoWPoint b)=>MathF.Sqrt(DistanceSqr(b));
 public WoWPoint Add(float x,float y,float z)=>this+new WoWPoint(x,y,z);
}
public class WoWObject {public uint BaseAddress=1;public WoWPoint Location=new WoWPoint(10,10,10);}
public class WoWUnit:WoWObject {public ulong Guid=123;public bool IsValid=true,IsAlive=true,IsGhost,IsFlying,IsSwimming,IsMoving;public uint MapId=530;}
public sealed class MovementInfo {public bool IsFlying=>World.Player.IsFlying;}
public sealed class LocalPlayer:WoWUnit {public bool Mounted,IsOutdoors=true,Combat;public WoWClass Class;public MovementInfo MovementInfo=new MovementInfo();public bool HasAura(string name)=>World.ReadName?.Invoke(name)??World.SeaLegs;public bool HasAura(int id)=>World.ReadId?.Invoke(id)??false;public WoWPoint GetTraceLinePos()=>Location;}
public static class ObjectManager {public static object Wow=new object(),Executor=new object();}
public static class StyxWoW {public static LocalPlayer Me=>World.Player;public static void Sleep(int ms){World.Record("sleep-"+ms);World.Hit("sleep");}}
public static class Mount {public static bool IsInCantMountSpot(WoWPoint p)=>false;public static bool ShouldMount(WoWPoint p)=>false;public static void MountUp()=>World.Record("ground-mount");public static bool MountUp(Func<bool> admitted,Func<WoWPoint> destination){if(!admitted())return false;_=destination();if(!admitted())return false;World.Record("ground-mount");return true;}}
public static class GameWorld {
 public enum CGWorldFrameHitFlags {HitTestLiquid}
 public static bool IsInLineOfSight(WoWPoint a,WoWPoint b)=>true;
 public static bool TraceLine(WoWPoint a,WoWPoint b,CGWorldFrameHitFlags flags)=>true;
}
public static class SpellManager {public static bool CanCast(string name)=>World.TestCrusader;public static bool HasSpell(string name)=>true;public static bool Cast(string name){World.Record("cast");return true;}}
public static class Colors {public static readonly object Orange="orange",Red="red";}
public static class Logging {public static void Write(string text,params object[] args)=>World.Hit("log");public static void Write(object color,string text,params object[] args)=>World.Hit("log");public static void WriteDiagnostic(string text,params object[] args)=>World.Hit("log");}
public static class WoWMathHelper {public static float CalculateNeededFacing(WoWPoint a,WoWPoint b)=>0;public static float DegreesToRadians(float value)=>value*MathF.PI/180;}
public sealed class PlayerMover {
 public void MoveTowards(WoWPoint point){World.Record("towards");World.Hit("towards");}
 public void MoveStop(){World.Record("player-stop");World.Hit("stop");}
}
public static class Navigator {
 public static object NavigationProvider=new object();
 public static bool IsInNoFlyZone=>false;public static bool IsRidingElevator=>false;public static float PathPrecision=>3;public static PlayerMover PlayerMover=new PlayerMover();
 public static void MoveTo(WoWPoint point){World.Record("ground");World.Hit("ground");}
 public static bool CanNavigateWithin(WoWPoint a,WoWPoint b,float range)=>true;
}
public static class WoWMovement {
 [Flags] public enum MovementDirection {Forward=1,JumpAscend=2,StrafeLeft=4,StrafeRight=8,Backwards=16}
 public struct ClickToMoveInfoStruct {public bool IsClickMoving;public WoWPoint ClickPos;}
 public static ClickToMoveInfoStruct ClickToMoveInfo=>default;
 public static WoWUnit ActiveMover=>World.Mover;
 public static void Move(MovementDirection flags){World.Record("move-"+flags);World.Hit("move");}
 public static void MoveStop(){World.Record("stop");World.Hit("stop");}
 public static void MoveStop(MovementDirection flags){World.Record("stop-"+flags);World.Hit("stop");}
}
public static class World {
 public static LocalPlayer Player;public static WoWUnit Mover;public static bool SeaLegs,Walk,EmptyPoint,FormSurface;public static int PathBuilds;
 public static Func<string,bool> ReadName;public static Func<int,bool> ReadId;public static bool TestCrusader;
 public static readonly List<string> Commands=new List<string>();public static Action<string> OnBoundary;
 public static void Record(string command)=>Commands.Add(command+":"+(Player?.Guid??0));
 public static void Hit(string name)=>OnBoundary?.Invoke(name);
 public static void Reset(){ObjectManager.Wow=new object();ObjectManager.Executor=new object();Player=new LocalPlayer();Mover=Player;SeaLegs=Walk=EmptyPoint=FormSurface=false;PathBuilds=0;Commands.Clear();OnBoundary=null;BotPoi.Current=new BotPoi();BotPoi.CurrentGeneration++;BotPoi.CurrentWorkGeneration++;ProfileManager.CurrentProfileSnapshot=new object();Navigator.NavigationProvider=new object();}
}
public class BotPoi {public static BotPoi Current=new BotPoi();public static long CurrentGeneration,CurrentWorkGeneration;public bool IsWorldSubjectBlacklisted;}
public static class ProfileManager {public static object CurrentProfileSnapshot=new object();}
""";
    private const string Controls = """
private static bool HasSeaLegs(LocalPlayer player)=>World.SeaLegs;
private static WoWPoint _takeoffSpot,_takeoffDestination,_lastDestination,_lastFlightWaypoint,_antiStuckStartPos;
private static int _pulseCount;private static bool _asAscended,_asStrafedLeft,_asStrafedRight;
private static LocalPlayer _antiStuckPlayer;private static WoWUnit _antiStuckOwner;
private static ulong _antiStuckPlayerGuid,_antiStuckOwnerGuid;private static uint _antiStuckMap,_antiStuckPlayerAddress,_antiStuckOwnerAddress;
private static LocalPlayer _pathPlayer;private static WoWUnit _pathMover;
private static ulong _pathPlayerGuid,_pathMoverGuid;private static uint _pathMap,_pathPlayerAddress,_pathMoverAddress;private static bool _pathAlive,_pathGhost;
private static BotPoi _pathPoi,_antiStuckPoi;private static long _pathPoiGeneration,_antiStuckPoiGeneration;private static Func<bool> _pathRouteLease,_antiStuckRouteLease;
private static object _pathProfile,_pathProvider,_pathInput,_antiStuckProfile,_antiStuckProvider,_antiStuckInput,_flightRequestOwner,_antiStuckRequestOwner,_pathMemory,_pathExecutor,_antiStuckMemory,_antiStuckExecutor;
private static FlightPath _flightPath;
private sealed class FlightPath {public int Id;public Queue<Vector2> Waypoints=new Queue<Vector2>(new[]{new Vector2(100,100)});}
public static int CurrentPathId=>_flightPath?.Id??0;
public static bool RecoveryActive=>_antiStuckPlayer!=null;
public static void RecoverWithLease(Func<bool> routeLease)=>DoAntiStuck(routeLease);
public static void ClearRecoveryOwner(){_antiStuckPlayer=null;_antiStuckOwner=null;_antiStuckRouteLease=null;}
private static bool CanFly=>true;private static bool AntiStuck=>false;
private static bool ShouldWalk(WoWPoint p)=>World.Walk;
private static bool ShouldAttemptGroundMount(bool canFly,bool mounted,bool allows)=>!canFly&&!mounted&&allows;
private static WoWObject FindTakeoffCandidate(WoWPoint location,float range)=>null;
private static void NavigateToTakeoffSpot()=>Navigator.MoveTo(_takeoffSpot);
private static WoWPoint GetPointInDirection(WoWPoint p,float distance,float facing,float pitch)=>p;
private static FlightPath BuildPath(Vector2 a,Vector2 b){int id=++World.PathBuilds;World.Hit("path");return new FlightPath{Id=id};}
private static WoWPoint CalculateFlightPoint(WoWPoint point,float height){World.Hit("point");return World.EmptyPoint?WoWPoint.Empty:point;}
private static class MountHelper {
 public static bool Mounted=>World.Player?.Mounted==true;
 // The later conditional is reachable when the aura observation changes during
 // eligibility. This is a controlled source boundary, not a claim that the
 // named legacy aura is an admitted build12340 mechanic.
 public static bool CanMount{get{if(World.FormSurface)World.SeaLegs=false;return true;}}
 public static void MountUpInternal(bool quick){World.Record("flight-mount");World.Hit("mount");}
 public static void MountUp()=>MountUpInternal(false);
}
public static void Reset(int phase){_pulseCount=1;_flightPath=null;_takeoffSpot=_takeoffDestination=_lastDestination=_lastFlightWaypoint=_antiStuckStartPos=WoWPoint.Empty;
 _asAscended=phase>0;_asStrafedLeft=phase>1;_asStrafedRight=phase>2;
 _pathPlayer=null;_pathMover=null;_pathRouteLease=null;_antiStuckRouteLease=null;_pathPlayerGuid=_pathMoverGuid=0;_pathMap=0;_pathAlive=_pathGhost=false;
 _antiStuckMemory=ObjectManager.Wow;_antiStuckExecutor=ObjectManager.Executor;_antiStuckPlayerAddress=World.Player.BaseAddress;_antiStuckOwnerAddress=World.Mover.BaseAddress;_antiStuckPlayer=World.Player;_antiStuckOwner=World.Mover;_antiStuckPlayerGuid=World.Player.Guid;_antiStuckOwnerGuid=World.Mover.Guid;_antiStuckMap=World.Player.MapId;_antiStuckAlive=World.Player.IsAlive;_antiStuckGhost=World.Player.IsGhost;
 _pathPoi=null;_pathProfile=_pathProvider=null;_flightRequestOwner=null;_pathPoiGeneration=0;
 _antiStuckPoi=BotPoi.Current;_antiStuckPoiGeneration=BotPoi.CurrentGeneration;_antiStuckProfile=ProfileManager.CurrentProfileSnapshot;_antiStuckProvider=Navigator.NavigationProvider;_antiStuckInput=Navigator.PlayerMover;}
private static bool _antiStuckAlive,_antiStuckGhost;
""";
    private const string Cases = """
public static class FlightWaitCases {
 private sealed class Failure(string text):Exception(text){}
 private static void Check(bool condition,string text){if(!condition)throw new Failure(text);}
 private static void Configure(string route){World.Reset();FlightOwner.Reset(0);
  switch(route){case "swim-rise":World.Player.IsSwimming=true;break;
   case "swim-form":World.Player.IsSwimming=true;World.Player.Class=WoWClass.Druid;World.SeaLegs=World.FormSurface=true;break;
   case "takeoff":World.Player.Mounted=true;break;
   case "second-ascent":World.Player.Mounted=true;World.Player.IsFlying=true;break;
   case "flight":World.Player.Mounted=true;World.Player.IsFlying=true;break;}
 }
 private static void Change(string change){switch(change){
  case "replacement":World.Player=new LocalPlayer{Guid=789,Mounted=true};World.Mover=World.Player;break;
  case "same-guid-replacement":World.Player=new LocalPlayer{Mounted=true};World.Mover=World.Player;break;
  case "missing":World.Player=null;World.Mover=null;break;case "guid":World.Player.Guid++;break;
  case "memory":ObjectManager.Wow=new object();break;case "executor":ObjectManager.Executor=new object();break;case "address":World.Player.BaseAddress++;break;case "map":World.Player.MapId++;break;case "dead":World.Player.IsAlive=false;break;case "invalid":World.Player.IsValid=false;break;
  case "mover":World.Mover=new WoWUnit{Guid=456};break;
  case "poi":BotPoi.Current=new BotPoi();BotPoi.CurrentGeneration++;BotPoi.CurrentWorkGeneration++;break;
  case "poi-generation":BotPoi.CurrentGeneration++;break;
  case "work-generation":BotPoi.CurrentGeneration++;BotPoi.CurrentWorkGeneration++;break;
  case "profile":ProfileManager.CurrentProfileSnapshot=new object();break;
  case "input-mover":Navigator.PlayerMover=new PlayerMover();break;
  case "provider":Navigator.NavigationProvider=new object();break;case "blacklist":BotPoi.Current.IsWorldSubjectBlacklisted=true;break;}
 }
 private static void Move(){try{FlightOwner.MoveTo(new WoWPoint(100,100,50),15);}catch(NullReferenceException){throw new Failure("stale wait dereferenced a missing actor");}}
 private static Func<bool> Lease(out BotPoi captured){var owner=BotPoi.Current;captured=owner;long work=BotPoi.CurrentWorkGeneration;return ()=>ReferenceEquals(BotPoi.Current,owner)&&BotPoi.CurrentWorkGeneration==work;}
 private static void MoveLeased(Func<bool> lease){FlightOwner.MoveToOwnedExterior(new WoWPoint(100,100,50),()=>true,_=>{},lease);}
 private static void Recover(){try{FlightOwner.DoAntiStuck();}catch(NullReferenceException){throw new Failure("stale recovery dereferenced a missing actor");}}
 public static void RunRawAura(Func<string,bool> names,Func<int,bool> ids,bool unavailable){
  Configure("flight");World.ReadName=names;World.ReadId=ids;World.TestCrusader=true;
  Exception observed=null;try{Move();}catch(Exception e){observed=e;}
  if(unavailable)Check(observed!=null&&observed.GetType().Name=="ObservationUnavailableException"&&!World.Commands.Exists(c=>c.StartsWith("towards:")||c.StartsWith("cast:")),"unreadable raw auras authorized movement or Crusader cast");
  else {Check(observed==null&&World.Commands.Exists(c=>c.StartsWith("towards:")),"unrelated unknown metadata starved actual flight MoveTo: "+observed);
   Check(World.Commands.Exists(c=>c.StartsWith("cast:"))!=ids(32223),"known Crusader presence was lost or absent aura blocked eligible cast");}
 }
 public static void Run(){var tests=new List<(string Name,Action Body)>();
  tests.Add(("ground interaction selected flight bypasses legacy walk comparison",()=>{Configure("flight");World.Walk=true;FlightOwner.MoveToGroundInteraction(new WoWPoint(100,100,50),()=>true);Check(World.Commands.Exists(c=>c.StartsWith("towards:"))&&!World.Commands.Exists(c=>c.StartsWith("ground:")),"selected direct-object flight was changed back to walking");}));
  tests.Add(("ground interaction flight retains caller across takeoff wait",()=>{Configure("takeoff");bool current=true;World.OnBoundary=b=>{if(b=="sleep")current=false;};FlightOwner.MoveToGroundInteraction(new WoWPoint(100,100,50),()=>current);Check(!World.Commands.Exists(c=>c.StartsWith("towards:")),"revoked direct-object owner retained flight input");}));
  tests.Add(("POI revocation stops owned flight input once",()=>{Configure("flight");Move();World.Commands.Clear();BotPoi.Current=new BotPoi();FlightOwner.InvalidateRouteContext();
   Check(World.Commands.FindAll(c=>c.StartsWith("player-stop:")).Count==1,"flight route invalidation left its old input active");World.Commands.Clear();FlightOwner.InvalidateRouteContext();Check(World.Commands.Count==0,"revoked flight stopped input twice");}));
  tests.Add(("explicit combat lease retains actual flight path across coordinate route revision",()=>{Configure("flight");FlightOwner.ClearRecoveryOwner();var lease=Lease(out var captured);MoveLeased(lease);Check(World.PathBuilds==1&&FlightOwner.CurrentPathId!=0,"leased actual flight path was not established");
   World.Commands.Clear();long work=BotPoi.CurrentWorkGeneration;BotPoi.CurrentGeneration++;FlightOwner.InvalidateRouteContext();
   Check(ReferenceEquals(BotPoi.Current,captured)&&BotPoi.CurrentWorkGeneration==work&&FlightOwner.CurrentPathId!=0,"coordinate-only revision revoked explicit combat flight lease");
   Check(!World.Commands.Exists(c=>c.StartsWith("player-stop:")),"coordinate-only revision stopped explicitly leased flight input");MoveLeased(lease);Check(World.PathBuilds==1,"coordinate-only revision rebuilt explicitly leased flight path");}));
  tests.Add(("explicit combat lease revokes actual flight path on semantic replacement",()=>{Configure("flight");FlightOwner.ClearRecoveryOwner();var lease=Lease(out _);MoveLeased(lease);World.Commands.Clear();Change("poi");FlightOwner.InvalidateRouteContext();
   Check(FlightOwner.CurrentPathId==0,"semantic POI replacement retained explicitly leased flight path");Check(World.Commands.FindAll(c=>c.StartsWith("player-stop:")).Count==1,"semantic replacement did not stop the still-owned leased flight input exactly once");}));
  tests.Add(("explicit combat lease retains actual anti-stuck owner across coordinate route revision",()=>{World.Reset();FlightOwner.Reset(0);var lease=Lease(out var captured);FlightOwner.RecoverWithLease(lease);Check(FlightOwner.RecoveryActive,"leased actual anti-stuck owner was not established");
   World.Commands.Clear();long work=BotPoi.CurrentWorkGeneration;BotPoi.CurrentGeneration++;FlightOwner.InvalidateRouteContext();Check(ReferenceEquals(BotPoi.Current,captured)&&BotPoi.CurrentWorkGeneration==work&&FlightOwner.RecoveryActive,"coordinate-only revision revoked leased anti-stuck owner");
   Check(!World.Commands.Exists(c=>c.StartsWith("player-stop:")),"coordinate-only revision stopped leased anti-stuck input");}));
  tests.Add(("explicit combat lease revokes actual anti-stuck owner on semantic replacement",()=>{World.Reset();FlightOwner.Reset(0);var lease=Lease(out _);FlightOwner.RecoverWithLease(lease);World.Commands.Clear();Change("poi");FlightOwner.InvalidateRouteContext();
   Check(!FlightOwner.RecoveryActive,"semantic POI replacement retained leased anti-stuck owner");Check(World.Commands.FindAll(c=>c.StartsWith("player-stop:")).Count==1,"semantic replacement did not stop leased anti-stuck input exactly once");}));
  tests.Add(("provider replacement cannot stop replacement flight input",()=>{Configure("flight");Move();World.Commands.Clear();Navigator.NavigationProvider=new object();FlightOwner.InvalidateRouteContext();Check(World.Commands.Count==0,"revoked flight stopped a replacement provider");}));
  foreach(string epoch in new[]{"memory","executor","address"}){string changed=epoch;
   tests.Add(("pulse detaches flight without stopping replacement epoch/"+changed,()=>{Configure("flight");Move();World.Commands.Clear();Change(changed);FlightOwner.InvalidateRouteContext();
    Check(FlightOwner.CurrentPathId==0&&World.Commands.Count==0,"stale flight route survived or cleanup reached replacement memory/input");}));
   tests.Add(("pulse detaches recovery without stopping replacement epoch/"+changed,()=>{World.Reset();FlightOwner.Reset(0);Recover();World.Commands.Clear();Change(changed);FlightOwner.InvalidateRouteContext();
    Check(World.Commands.Count==0,"recovery cleanup reached replacement memory/input");Recover();Check(World.Commands.Exists(c=>c.StartsWith("move-JumpAscend:")),"replacement recovery inherited stale phase");}));
  }
  tests.Add(("flight cleanup preserves stop callback successor",()=>{Configure("flight");Move();BotPoi.Current=new BotPoi();World.OnBoundary=b=>{if(b!="stop")return;World.OnBoundary=null;Move();Move();};FlightOwner.InvalidateRouteContext();Check(FlightOwner.CurrentPathId==2,"old flight cleanup overwrote callback successor");}));
  tests.Add(("POI revocation stops owned recovery input",()=>{World.Reset();FlightOwner.Reset(0);Recover();World.Commands.Clear();BotPoi.Current=new BotPoi();FlightOwner.InvalidateRouteContext();Check(World.Commands.FindAll(c=>c.StartsWith("player-stop:")).Count==1,"recovery revocation left owned direction input active");}));
  foreach(string route in new[]{"swim-rise","swim-form","takeoff","second-ascent","flight"}){string selected=route;
   tests.Add((selected+" valid route",()=>{Configure(selected);if(selected=="second-ascent")World.OnBoundary=name=>{if(name=="towards")World.Player.IsFlying=false;};Move();
    Check(World.Commands.Count>0,"valid route lost all movement");
    if(selected=="swim-form")Check(World.Commands.FindAll(c=>c.StartsWith("flight-mount")).Count==3,"valid controlled form surface sequence changed");
   }));
   foreach(string boundary in selected=="flight"?new[]{"path","point"}:new[]{"sleep"}){string at=boundary;
    int sleepCount=selected=="swim-form"?3:selected=="second-ascent"?2:1;
    for(int occurrence=1;occurrence<=sleepCount;occurrence++){int when=occurrence;
     foreach(string change in new[]{"replacement","same-guid-replacement","memory","executor","address","missing","guid","map","dead","invalid","mover","poi","poi-generation","profile","provider","input-mover","blacklist"}){string mutation=change;
      tests.Add(($"{selected} {at}{when}/{mutation}",()=>{Configure(selected);int seen=0,commandCount=-1;
       World.OnBoundary=name=>{if(selected=="second-ascent"&&name=="towards")World.Player.IsFlying=false;
        if(name==at&&++seen==when){Change(mutation);commandCount=World.Commands.Count;}};
       Move();Check(commandCount>=0,"selected wait/observation boundary was not reached");
       Check(World.Commands.Count==commandCount,"movement or mount command continued after actor/control replacement");
      }));
     }
    }
   }
  }
  for(int selected=0;selected<4;selected++){int phase=selected;
   tests.Add(($"recovery{phase} valid",()=>{World.Reset();FlightOwner.Reset(phase);Recover();Check(World.Commands.Exists(c=>c.StartsWith("move-"))&&World.Commands.Exists(c=>c.StartsWith("stop-")),"valid recovery sequence lost its input cleanup");}));
   foreach(string boundary in new[]{"sleep","log"}){string at=boundary;
    foreach(string change in new[]{"replacement","same-guid-replacement","memory","executor","address","missing","guid","map","dead","invalid","mover","poi","poi-generation","profile","provider","input-mover","blacklist"}){string mutation=change;
     tests.Add(($"recovery{phase}/{at}/{mutation}",()=>{World.Reset();FlightOwner.Reset(phase);World.Player.IsMoving=at=="sleep";int commandCount=-1;
      World.OnBoundary=name=>{if(name==at&&commandCount<0){Change(mutation);commandCount=World.Commands.Count;}};Recover();
      Check(commandCount>=0,"selected recovery boundary was not reached");Check(World.Commands.Count==commandCount,"recovery input continued after actor/control replacement");
     }));
    }
   }
  }
  foreach(string change in new[]{"replacement","same-guid-replacement","memory","executor","address","guid","map","mover"}){string mutation=change;
   tests.Add(("new recovery context restarts sequence/"+mutation,()=>{World.Reset();FlightOwner.Reset(0);Recover();World.Commands.Clear();Change(mutation);Recover();
    Check(World.Commands.Exists(c=>c.StartsWith("move-JumpAscend:"))&&!World.Commands.Exists(c=>c.StartsWith("move-StrafeLeft:")),"new actor/map inherited the prior actor's recovery step");}));}
  tests.Add(("same recovery context advances sequence",()=>{World.Reset();FlightOwner.Reset(0);Recover();World.Commands.Clear();Recover();
   Check(World.Commands.Exists(c=>c.StartsWith("move-StrafeLeft:")),"same actor lost its retained recovery step");}));
  foreach(string change in new[]{"replacement","same-guid-replacement","memory","executor","address","guid","map","mover","dead","poi","poi-generation","profile","provider","input-mover"}){string mutation=change;
   tests.Add(("cached route revoked for new context/"+mutation,()=>{Configure("flight");Move();Check(World.PathBuilds==1,"control route was not built");Change(mutation);Move();Move();
    Check(World.PathBuilds==2,"new actor/control/map reused the old flight route");}));}
  tests.Add(("same context reuses its valid route",()=>{Configure("flight");Move();Move();Move();Check(World.PathBuilds==1,"same route/context needlessly rebuilt its path");}));
  tests.Add(("blacklisted subject cannot retain flight path authority",()=>{Configure("flight");Move();int commands=World.Commands.Count;
   BotPoi.Current.IsWorldSubjectBlacklisted=true;Move();Check(World.Commands.Count==commands,"blacklisted object received more flight movement");
   BotPoi.Current.IsWorldSubjectBlacklisted=false;Move();Move();Check(World.PathBuilds==2,"revoked path was reused after blacklist expiry");}));
  tests.Add(("late successful path cannot overwrite nested same-destination request",()=>{Configure("flight");bool nested=false;
   World.OnBoundary=name=>{if(name=="path"&&!nested){nested=true;World.OnBoundary=null;Move();Move();}};
   Move();Check(nested&&World.PathBuilds==2,"actual nested path generation was not reached");
   Check(FlightOwner.CurrentPathId==2,"obsolete successful path overwrote the nested request result");}));
  foreach(string loss in new[]{"player","mover","invalid","zero-guid"}){string unavailable=loss;
   tests.Add(("observed unavailable context revokes cached route/"+unavailable,()=>{Configure("flight");Move();
    Check(World.PathBuilds==1,"control route was not built");var player=World.Player;var mover=World.Mover;ulong guid=player.Guid;
    switch(unavailable){case "player":World.Player=null;break;case "mover":World.Mover=null;break;
     case "invalid":player.IsValid=false;break;case "zero-guid":player.Guid=0;break;}
    int commands=World.Commands.Count;Move();Check(World.Commands.Count==commands,"unavailable context received movement");
    World.Player=player;World.Mover=mover;player.IsValid=true;player.Guid=guid;Move();Move();
    Check(World.PathBuilds==2,"route authority survived an observed unavailable context");}));}
  foreach(string loss in new[]{"player","mover","invalid","zero-guid"}){string unavailable=loss;
   tests.Add(("observed unavailable context resets recovery step/"+unavailable,()=>{World.Reset();FlightOwner.Reset(0);Recover();
    var player=World.Player;var mover=World.Mover;ulong guid=player.Guid;
    switch(unavailable){case "player":World.Player=null;break;case "mover":World.Mover=null;break;
     case "invalid":player.IsValid=false;break;case "zero-guid":player.Guid=0;break;}
    int commands=World.Commands.Count;Recover();Check(World.Commands.Count==commands,"unavailable recovery context received movement");
    World.Player=player;World.Mover=mover;player.IsValid=true;player.Guid=guid;World.Commands.Clear();Recover();
    Check(World.Commands.Exists(c=>c.StartsWith("move-JumpAscend:"))&&!World.Commands.Exists(c=>c.StartsWith("move-StrafeLeft:")),"recovery step survived an observed ownership gap");}));}
  foreach(string life in new[]{"alive","ghost"}){string changed=life;
   tests.Add(("changed life context resets recovery step/"+changed,()=>{World.Reset();FlightOwner.Reset(0);Recover();World.Commands.Clear();
    if(changed=="alive")World.Player.IsAlive=false;else World.Player.IsGhost=true;Recover();
    Check(World.Commands.Exists(c=>c.StartsWith("move-JumpAscend:"))&&!World.Commands.Exists(c=>c.StartsWith("move-StrafeLeft:")),"recovery step survived a changed life context");}));}
  int passed=0,assertions=0,unexpected=0;foreach(var test in tests){try{test.Body();passed++;Console.WriteLine("PASS Flightor wait: "+test.Name);}
   catch(Failure error){assertions++;Console.Error.WriteLine("FAIL Flightor wait: "+test.Name+": "+error.Message);}
   catch(Exception error){unexpected++;Console.Error.WriteLine("ERROR Flightor wait: "+test.Name+": "+error);}}
  Console.WriteLine($"Flightor movement wait scenarios: {passed}/{tests.Count}; assertions={assertions}; unexpected={unexpected}; complete tracked MoveTo/recovery, controlled geometry/native/time; no route or live acceptance.");
  if(assertions+unexpected!=0)throw new InvalidOperationException($"Flightor wait regressions: assertions={assertions}; unexpected={unexpected}");
 }
}
""";
}
