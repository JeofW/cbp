using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var root = new DirectoryInfo(AppContext.BaseDirectory);
while (root != null && !File.Exists(Path.Combine(root.FullName, "CopilotBuddy.csproj"))) root = root.Parent;
if (root == null) throw new InvalidOperationException("Tracked source required");
var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName, "Styx/Logic/Pathing/Flightor.cs"))).GetRoot();
var owner = syntax.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "Flightor");
var names = new[] { "BuildPath", "CalculateFlightPoint", "BuildRayList", "GetPointInDirection", "GetFlightRouteWaypoint", "CanFollowFlightSegment" };
string methods = string.Join("\n", owner.Members.OfType<MethodDeclarationSyntax>().Where(m => names.Contains(m.Identifier.ValueText)));
string fields = string.Join("\n", owner.Members.OfType<FieldDeclarationSyntax>().Where(f => f.Declaration.Variables.Any(v => v.Identifier.ValueText.StartsWith("_polyNav"))));
string pathClass = owner.Members.OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "FlightPath").ToString();
string boundary = """
#nullable disable
using System;using System.Linq;using System.Collections.Generic;
using Styx.Logic.Pathing;using Styx.Logic.Pathing.FlightorNavigation;using Styx.WoWInternals;using Styx.WoWInternals.World;using Styx.Helpers;
using Vector2=Tripper.XNAMath.Vector2;
using LocalPlayer=Player;
namespace Styx {public static class StyxWoW {public static Player Me=new();}}
public sealed class Player {public uint MapId=530,ZoneId;public bool IsHorde=true;public bool IsAlliance=>!IsHorde;public bool IsValid=true;public ulong Guid=1;public WoWPoint Location=new(0,0,100);public bool IsInInstance;public float BoundingRadius=.6f,Rotation;public WoWPoint GetTraceLinePos()=>Location.Add(0,0,2);}
public static class World {public static int Clears;public static bool CollisionClear=true;}
public static class Logging {public static void WriteDebug(string text,params object[] args){} public static void WriteDiagnostic(string text,params object[] args){} }
public static class GameWorld {
 [Flags]public enum CGWorldFrameHitFlags {HitTestLiquid=1,HitTestGround=2,HitTestWMO=4,HitTestGroundAndStructures=8}
 public static bool IsInLineOfSight(WoWPoint from,WoWPoint to)=>World.CollisionClear;
 public static bool TraceLine(WoWPoint from,WoWPoint to,CGWorldFrameHitFlags flags)=>!World.CollisionClear;
 public static void MassTraceLine(IEnumerable<WorldLine> lines,CGWorldFrameHitFlags flags,out bool[] hits){hits=lines.Select(_=>!World.CollisionClear).ToArray();}
}
public static class Areas {public static Dictionary<uint,Vector2[]> ContinentAreas=new();}
namespace Styx.Logic.Pathing {public static class Flightor {
private static bool HasSeaLegs(Player actor)=>false;
public static void Clear(){World.Clears++;_polyNav=null;_polyNavMapId=null;}
public static Vector2[] Route(Vector2 from,Vector2 to)=>BuildPath(from,to).Waypoints.ToArray();
public static WoWPoint FlightPoint(WoWPoint destination)=>CalculateFlightPoint(destination,40);
""";
string cases = """
public static class Cases {
 private static void Check(bool value,string why){if(!value)throw new InvalidOperationException(why);}
 private static Vector2[] Box(float xmin,float ymin,float xmax,float ymax)=>new[]{new Vector2(xmin,ymin),new Vector2(xmax,ymin),new Vector2(xmax,ymax),new Vector2(xmin,ymax)};
 public static void Run(){int passed=0,total=0;var errors=new List<string>();
  void Case(string name,Action body){total++;Styx.StyxWoW.Me=new();Flightor.Clear();World.CollisionClear=true;try{body();passed++;Console.WriteLine("PASS aerial route: "+name);}catch(Exception error){errors.Add(name+": "+error.Message);Console.Error.WriteLine("FAIL aerial route: "+errors[^1]);}}
  Case("opposing Honor Hold settlement is an altitude-independent exclusion",()=>{
   Check(Styx.Logic.Pathing.FlightorNavigation.BlackspotManager.IsInBlackspot(new(-700,2700,900)),"Horde flight did not exclude the source-proven Honor Hold footprint");});
  Case("friendly Swamprat Post remains reachable",()=>Check(!Styx.Logic.Pathing.FlightorNavigation.BlackspotManager.IsInBlackspot(new(100,5188,900)),"own quest giver was treated as an opposing base"));
  Case("failed polygon route never becomes a direct forbidden destination",()=>{
   var poly=Box(90,90,110,110);Styx.Logic.Pathing.FlightorNavigation.BlackspotManager.AddBlackspot(777,WoWFactionGroup.Horde,poly);Styx.StyxWoW.Me.MapId=777;
   Check(Flightor.Route(new(50,100),new(100,100)).Length==0,"failed path fell back to the interior of a forbidden polygon");});
  Case("map and faction jointly own the cached route",()=>{
   var poly=Box(90,90,110,110);Styx.Logic.Pathing.FlightorNavigation.BlackspotManager.AddBlackspot(778,WoWFactionGroup.Horde,poly);Styx.StyxWoW.Me.MapId=778;Styx.StyxWoW.Me.IsHorde=false;
   Check(Flightor.Route(new(50,100),new(100,100)).Length>0,"unrestricted control missing");Styx.StyxWoW.Me.IsHorde=true;
   Check(Flightor.Route(new(50,100),new(100,100)).Length==0,"faction switch reused an unrestricted cached route");});
  Case("removing an aerial exclusion invalidates route cache",()=>{
   var poly=Box(90,90,110,110);Styx.Logic.Pathing.FlightorNavigation.BlackspotManager.AddBlackspot(779,WoWFactionGroup.Horde,poly);Styx.StyxWoW.Me.MapId=779;
   Flightor.Route(new(50,100),new(150,100));int before=World.Clears;Styx.Logic.Pathing.FlightorNavigation.BlackspotManager.RemoveBlackspot(779,WoWFactionGroup.Horde,poly);
   Check(World.Clears>before,"removed exclusion retained its old route cache");});
  Case("physical line of sight cannot authorize a flight shortcut through an exclusion",()=>{
   Styx.StyxWoW.Me.MapId=780;Styx.StyxWoW.Me.Location=new(50,100,100);
   Styx.Logic.Pathing.FlightorNavigation.BlackspotManager.AddBlackspot(780,WoWFactionGroup.Horde,Box(90,90,110,110));
   var point=Flightor.FlightPoint(new(150,100,100));Check(point==WoWPoint.Empty||point.Y<89||point.Y>111||point.X<89,
    "collision-free ray crossed the hostile-base polygon");});
  foreach(int count in new[]{1,2,4})Case("multiple independent holes retain point membership/"+count,()=>{
   var holes=Enumerable.Range(0,count).Select(i=>Box(100+i*50,100,120+i*50,120)).ToArray();var nav=new PolyNav(Box(-1000,-1000,1000,1000),holes);
   for(int x=60;x<350;x+=5)for(int y=60;y<160;y+=5){bool expected=!Enumerable.Range(0,count).Any(i=>x>=100+i*50&&x<120+i*50&&y>=100&&y<120);Check(nav.ContainsPoint(new(x,y))==expected,"polygon membership borrowed another hole's winding");}
  });
  Case("complete avoidance path reaches its requested endpoint",()=>{
   var nav=new PolyNav(Box(-1000,-1000,1000,1000),new[]{Box(90,90,110,110)});var start=new Vector2(50,100);var end=new Vector2(150,100);var path=nav.FindPath(start,end);
   Check(path.Length>=3&&path[0]==start&&path[^1]==end,"path is missing, incomplete or bypasses its obstacle");
   for(int i=1;i<path.Length;i++)for(int n=1;n<100;n++){var p=path[i-1]+(path[i]-path[i-1])*(n/100f);Check(!(p.X>90&&p.X<110&&p.Y>90&&p.Y<110),"path crosses exclusion interior");}
  });
  Case("overlapping custom footprints still provide an outside detour",()=>{
   Styx.StyxWoW.Me.MapId=781;var first=Box(90,90,115,120);var second=Box(105,90,130,120);
   Styx.Logic.Pathing.FlightorNavigation.BlackspotManager.AddBlackspot(781,WoWFactionGroup.Horde,first);
   Styx.Logic.Pathing.FlightorNavigation.BlackspotManager.AddBlackspot(781,WoWFactionGroup.Horde,second);
   var path=Flightor.Route(new(50,105),new(170,105));Check(path.Length>=3&&path[^1]==new Vector2(170,105),"overlapping exclusions stranded an otherwise clear outside route");
  });
  Case("an actor inside an exclusion can route to its outside destination",()=>{
   Styx.StyxWoW.Me.MapId=782;Styx.Logic.Pathing.FlightorNavigation.BlackspotManager.AddBlackspot(782,WoWFactionGroup.Horde,Box(90,90,130,130));
   var path=Flightor.Route(new(92,110),new(200,110));Check(path.Length>=2&&path[^1]==new Vector2(200,110)&&path[0].X<90,
    "existing presence stalled or crossed deeper into the base instead of leaving through the nearby edge");
  });
  foreach(var region in AerialSettlementData.Regions)Case("source settlement is avoided for its opposing faction/"+region.Id,()=>{
   Styx.StyxWoW.Me.MapId=region.Map;Styx.StyxWoW.Me.IsHorde=region.Owner==WoWFactionGroup.Alliance;
   var center=new WoWPoint((region.MinX+region.MaxX)/2,(region.MinY+region.MaxY)/2,900);
   Check(Styx.Logic.Pathing.FlightorNavigation.BlackspotManager.IsInBlackspot(center),"source base is not protected at flying altitude");
   var from=new Vector2(region.MinX-100,center.Y);var to=new Vector2(region.MaxX+100,center.Y);
   for(int n=0;n<20&&Styx.Logic.Pathing.FlightorNavigation.BlackspotManager.IsInBlackspot(new(from.X,from.Y,0));n++)from.X-=100;
   for(int n=0;n<20&&Styx.Logic.Pathing.FlightorNavigation.BlackspotManager.IsInBlackspot(new(to.X,to.Y,0));n++)to.X+=100;
   var path=Flightor.Route(from,to);Check(path.Length>1&&path[^1]==to,"source exclusion has no complete outside route");
   var previous=new WoWPoint(from.X,from.Y,900);foreach(var waypoint in path){var next=new WoWPoint(waypoint.X,waypoint.Y,900);
    Check(Styx.Logic.Pathing.FlightorNavigation.BlackspotManager.IsSegmentAllowed(previous,next),"source detour crosses another excluded base");previous=next;}
  });
  Console.WriteLine($"Aerial route regression: {passed}/{total}; actual route builder, polygon pathfinder and flight-point chooser; controlled world and collision leaves.");
  if(errors.Count>0)throw new InvalidOperationException("aerial route regressions");
 }
}
""";
var sources = new List<string> { boundary + fields + pathClass + methods + "}}\n" + cases };
foreach (string file in new[] { "AerialBlackspotManager.cs", "PolyNav.cs", "AerialSettlementData.cs" })
{
    string path = Path.Combine(root.FullName, "Styx/Logic/Pathing/FlightorNavigation", file);
    if (File.Exists(path)) sources.Add(File.ReadAllText(path));
}
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
    .Append(typeof(Styx.StyxWoW).Assembly.Location).Distinct().Select(path => MetadataReference.CreateFromFile(path));
var compile = CSharpCompilation.Create("AerialRoute_" + Guid.NewGuid().ToString("N"), sources.Select(source => CSharpSyntaxTree.ParseText(source)),
    references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
using var bytes = new MemoryStream();var result = compile.Emit(bytes);
if (!result.Success) throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
var assembly=Assembly.Load(bytes.ToArray());
try { assembly.GetType("Cases",true)!.GetMethod("Run")!.Invoke(null,null); }
catch(TargetInvocationException error) when(error.InnerException!=null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw; }
