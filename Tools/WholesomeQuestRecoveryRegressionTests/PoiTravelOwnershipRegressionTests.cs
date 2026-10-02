using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

// Execute the complete shared travel action and its actual base. Flightor is
// a controlled void dispatch boundary, not an invented success/arrival receipt.
internal static class PoiTravelOwnershipRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        string? root = null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CopilotBuddy.csproj"))) { root = d.FullName; break; }
        if (root == null) throw new InvalidOperationException("Tracked checkout required");
        string temp = Path.Combine(Path.GetTempPath(), "cb-poi-travel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        bool logging = Styx.Helpers.Logging.FileLogging;
        try
        {
            Styx.Helpers.Logging.FileLogging = false;
            foreach (string path in new[] { "CommonBehaviors/Actions/ActionMoveToPoi.cs", "CommonBehaviors/Actions/NavigationAction.cs", "CommonBehaviors/Actions/GetPointDelegate.cs" })
                File.Copy(Path.Combine(root, path), Path.Combine(temp, Path.GetFileName(path)));
            File.WriteAllText(Path.Combine(temp, "Boundary.cs"), Boundary);
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { temp })!;
            foreach (string path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
                compilerType.GetMethod("AddReference", flags)!.Invoke(compiler, new object[] { path });
            var result = (CompilerResults)compilerType.GetMethod("Compile", flags)!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(error => !error.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Actual POI travel compile: " + string.Join("; ", errors.Select(error => error.ToString())));
            var assembly = (Assembly)compilerType.GetProperty("CompiledAssembly", flags)!.GetValue(compiler)!;
            try { assembly.GetType("PoiTravelCases", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        finally { Styx.Helpers.Logging.FileLogging = logging; Directory.Delete(temp, true); }
    }
    private const string Boundary = """
#nullable disable
using System;using System.Collections.Generic;using System.Linq;
using TreeSharp;using Styx;using Styx.Logic.POI;using Styx.Logic.Pathing;using Styx.WoWInternals;using Styx.WoWInternals.WoWObjects;using CommonBehaviors.Actions;
public static class PoiTravelCases {
 sealed class Failure(string why):Exception(why){}
 public static LocalPlayer Actor;public static WoWUnit Subject;public static BotPoi Poi;
 public static string Stage;public static System.Action Callback;public static readonly List<WoWPoint> Moves=new(),Approaches=new();public static int Logs;
 static IEnumerable<WoWPoint> Dispatched=>Moves.Concat(Approaches);
 static RunStatus ExpectedStatus(PoiType type)=>type is PoiType.QuestPickUp or PoiType.QuestTurnIn or PoiType.Sell or PoiType.Buy or PoiType.Repair or PoiType.Mail or PoiType.Train or PoiType.Fly or PoiType.InnKeeper?RunStatus.Running:RunStatus.Success;
 static void Check(bool value,string why){if(!value)throw new Failure(why);}
 public static void Event(string stage){if(Stage==stage){var action=Callback;Stage=null;Callback=null;action?.Invoke();}}
 static void Reset(){
  Stage=null;Callback=null;Moves.Clear();Approaches.Clear();Logs=0;
  Actor=new LocalPlayer{Guid=1,LocationValue=new WoWPoint(10,10,10)};ObjectManager.Me=Actor;WoWMovement.ActiveMover=Actor;
  Subject=new WoWUnit{Guid=2,LocationValue=new WoWPoint(200,10,10)};
  Poi=new BotPoi{Type=PoiType.Loot,Guid=2,Entry=70,ObjectValue=Subject,LocationValue=Subject.LocationValue};BotPoi.Current=Poi;
  Navigator.NavigationProvider=new object();
 }
 static RunStatus Tick(ActionMoveToPoi action){
  var errors=new List<string>();void Log(Styx.Helpers.LogLevel level,string text){if(text.Contains("Exception")||text.Contains("Object reference not set"))errors.Add(text);}
  var logged=typeof(Composite).Assembly.GetType("Styx.Helpers.Logging",true).GetEvent("OnMessageLogged");
  System.Action<Styx.Helpers.LogLevel,string> handler=Log;logged.AddEventHandler(null,handler);
  try{action.Start(null);try{var result=action.Tick(null);Check(errors.Count==0,"travel swallowed an exception: "+string.Join(";",errors));return result;}finally{action.Stop(null);}}
  finally{logged.RemoveEventHandler(null,handler);}
 }
 static void Change(string mode){switch(mode){
  case "actor":ObjectManager.Me=new LocalPlayer{Guid=1,LocationValue=Actor.LocationValue};break;
  case "actor-guid":Actor.Guid=9;break;case "actor-missing":ObjectManager.Me=null;break;
  case "dead":Actor.IsAlive=false;break;case "invalid":Actor.IsValid=false;break;case "map":Actor.MapId++;break;
  case "control":WoWMovement.ActiveMover=new WoWUnit{Guid=8,LocationValue=Actor.LocationValue};break;
  case "control-missing":WoWMovement.ActiveMover=null;break;
  case "poi":BotPoi.Current=new BotPoi{Type=PoiType.Fly,Guid=9,Entry=8,LocationValue=new WoWPoint(700,10,10)};break;
  case "type":Poi.Type=PoiType.Repair;break;case "poi-guid":Poi.Guid++;break;case "entry":Poi.Entry++;break;
  case "object":Poi.ObjectValue=new WoWUnit{Guid=2,LocationValue=Subject.LocationValue};break;
  case "object-guid":Subject.Guid++;break;case "object-invalid":Subject.IsValid=false;break;
  case "despawn":Poi.ObjectValue=null;break;case "target-life":Subject.IsAlive=!Subject.IsAlive;break;
  case "destination":Subject.LocationValue=Subject.LocationValue.Add(0,0,20);Poi.LocationValue=Subject.LocationValue;break;
  case "provider":Navigator.NavigationProvider=new object();break;
  case "taxi":Actor.OnTaxi=true;break;case "transport":Actor.IsOnTransport=true;break;
  case "casting":Actor.IsCasting=true;break;case "channel":Actor.ChanneledCastingSpellId=4;break;
  default:throw new InvalidOperationException(mode);
 }}
 public static void Run(){
  var cases=new List<(string,System.Action)>();void Add(string name,System.Action body)=>cases.Add((name,()=>{Reset();body();}));
  foreach(PoiType type in new[]{PoiType.Loot,PoiType.Skin,PoiType.Harvest,PoiType.Kill,PoiType.Quest,PoiType.QuestPickUp,PoiType.QuestTurnIn,PoiType.Sell,PoiType.Buy,PoiType.Repair,PoiType.Mail,PoiType.Train,PoiType.Fly,PoiType.Hotspot}){
   var kind=type;Add("healthy "+kind,()=>{Poi.Type=kind;Subject.IsAlive=kind!=PoiType.Loot&&kind!=PoiType.Skin;Check(Tick(new ActionMoveToPoi())==ExpectedStatus(kind)&&Dispatched.SequenceEqual(new[]{Subject.LocationValue}),"healthy travel dispatch changed or acquired unobserved arrival");});
  }
  Add("coordinate destination without observed NPC",()=>{Poi.ObjectValue=null;Check(Tick(new ActionMoveToPoi())==RunStatus.Success&&Moves.SequenceEqual(new[]{Poi.LocationValue}),"coordinate approach requires an already loaded NPC");});
  Add("game-object destination",()=>{Poi.ObjectValue=new WoWObject{Guid=2,LocationValue=Poi.LocationValue};Check(Tick(new ActionMoveToPoi())==RunStatus.Success&&Moves.SequenceEqual(new[]{Poi.LocationValue}),"game-object destination changed");});
  foreach(string mode in new[]{"taxi","transport","casting","channel","dead","invalid","actor-missing","control-missing","object-invalid"}){
   string change=mode;Add("initial "+change,()=>{Change(change);Check(Tick(new ActionMoveToPoi())==RunStatus.Failure&&Moves.Count==0,"unavailable travel reached Flightor");});
  }
  foreach(string mode in new[]{"zero","empty","nan-x","infinity-z"}){string invalid=mode;Add("invalid destination "+invalid,()=>{
   Subject.LocationValue=invalid=="zero"?WoWPoint.Zero:invalid=="empty"?WoWPoint.Empty:invalid=="nan-x"?new WoWPoint(float.NaN,10,10):new WoWPoint(10,10,float.PositiveInfinity);Poi.LocationValue=Subject.LocationValue;
   Check(Tick(new ActionMoveToPoi())==RunStatus.Failure&&Moves.Count==0,"invalid coordinate dispatched");
  });}
  foreach(string stage in new[]{"object","location","log","move"})foreach(string mode in new[]{"actor","actor-guid","actor-missing","dead","invalid","map","control","control-missing","poi","type","poi-guid","entry","object","object-guid","object-invalid","despawn","target-life","destination","provider","taxi","transport","casting","channel"}){
   string boundary=stage,change=mode;Add(boundary+" revokes "+change,()=>{
    bool fired=false;Stage=boundary;Callback=()=>{fired=true;Change(change);};var result=Tick(new ActionMoveToPoi());
    Check(fired,"callback boundary was not exercised");
    // First discovery does not yet own this object's life/position. These two
    // controls must admit its current observation; later changes must revoke it.
    if(boundary=="object"&&(change=="target-life"||change=="destination"))
     Check(result==RunStatus.Success&&Moves.SequenceEqual(new[]{Subject.LocationValue}),"first object discovery rejected current life/location without an earlier observation");
    else Check(result==RunStatus.Failure&&Moves.Count==(boundary=="move"?1:0),"obsolete travel dispatched or reported current success");
   });
  }
  foreach(string mode in new[]{"actor","map","provider","poi","destination"}){string change=mode;Add("moving cached route after "+change,()=>{
   Subject.IsMoving=true;var owner=new ActionMoveToPoi();Check(Tick(owner)==RunStatus.Success,"initial moving travel failed");Moves.Clear();Approaches.Clear();
   if(change=="actor"){ObjectManager.Me=new LocalPlayer{Guid=1,LocationValue=Actor.LocationValue};WoWMovement.ActiveMover=ObjectManager.Me;Actor=ObjectManager.Me;}
   else if(change=="poi"){Poi=new BotPoi{Type=PoiType.QuestPickUp,Guid=2,Entry=70,ObjectValue=Subject};BotPoi.Current=Poi;}
   else if(change!="destination")Change(change);
   Subject.LocationValue=new WoWPoint(500,10,40);Poi.LocationValue=Subject.LocationValue;
   Check(Tick(owner)==ExpectedStatus(Poi.Type)&&Dispatched.SequenceEqual(new[]{Subject.LocationValue}),"new context inherited a stale moving-unit destination");
  });}
  Add("stable destination suppresses duplicate log only",()=>{var owner=new ActionMoveToPoi();Tick(owner);Tick(owner);Check(Moves.Count==2&&Logs==1,"log suppression changed movement or logs");});
  foreach(string mode in new[]{"flying","swimming","mounted"}){string travel=mode;Add("existing travel mode "+travel,()=>{Actor.IsFlying=travel=="flying";Actor.IsSwimming=travel=="swimming";Actor.Mounted=travel=="mounted";Check(Tick(new ActionMoveToPoi())==RunStatus.Success&&Moves.Count==1,"legitimate flight/water/mount mode blocked");});}
  foreach(var example in new[]{("inside doorway",new WoWPoint(200,10,10)),("deep interior",new WoWPoint(230,15,10)),("upper floor",new WoWPoint(200,10,30)),("lower floor",new WoWPoint(200,10,-10)),("roof separation",new WoWPoint(205,18,15)),("cave interior",new WoWPoint(240,30,5))}){
   var sample=example;Add("indoor destination requires exterior approach/"+sample.Item1,()=>{
    Poi.Type=PoiType.QuestPickUp;Actor.Mounted=true;Actor.IsFlying=true;
    Subject.IsOutdoors=false;Subject.LocationValue=sample.Item2;Poi.LocationValue=sample.Item2;
    Tick(new ActionMoveToPoi());
    Check(!Moves.Contains(sample.Item2),"known indoor target coordinate was sent directly to Flightor; no exterior/landing/ground handoff exists");
    Check(Approaches.SequenceEqual(new[]{sample.Item2}),"indoor target did not acquire the shared approach owner");
   });
  }
  int passed=0,assertions=0,unexpected=0;
  foreach(var item in cases){try{item.Item2();passed++;Console.WriteLine("PASS POI travel: "+item.Item1);}catch(Failure e){assertions++;Console.Error.WriteLine("FAIL POI travel: "+item.Item1+": "+e.Message);}catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR POI travel: "+item.Item1+": "+e);}}
  Console.WriteLine($"POI travel ownership scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; complete action/base and real TreeSharp; controlled world/Flightor/approach leaves; no arrival, route-result or native acceptance. Actual ground transition integration is covered by IndoorApproachRegressionTests.");
  if(assertions+unexpected!=0)throw new InvalidOperationException("POI travel regression");
 }
}
/* Controlled world objects inside the compiled probe. */ namespace Styx.WoWInternals.WoWObjects {
 public class WoWObject {public ulong Guid=2;public uint Entry=70;public bool IsValid=true,IsOutdoors=true;public WoWPoint LocationValue;public virtual WoWPoint Location{get{var observed=LocationValue;PoiTravelCases.Event("location");return observed;}}public WoWUnit ToUnit()=>this as WoWUnit;}
 public class WoWUnit:WoWObject {public bool IsAlive=true,IsMoving;}
 public class LocalPlayer:WoWUnit {public uint MapId;public bool OnTaxi,IsOnTransport,IsCasting,IsFlying,IsSwimming,Mounted,Combat,IsGhost;public int ChanneledCastingSpellId;}
}
/* Controlled runtime observations. */ namespace Styx.WoWInternals {public static class ObjectManager{public static LocalPlayer Me;}public static class WoWMovement{public static WoWUnit ActiveMover;}}
/* Controlled runtime observations. */ namespace Styx {public static class StyxWoW{public static LocalPlayer Me=>ObjectManager.Me;}}
/* Controlled current work and callback observations. */ namespace Styx.Logic.POI {
 public enum PoiType {None,Loot,Skin,Harvest,Kill,Quest,QuestPickUp,QuestTurnIn,Sell,Buy,Mail,Repair,Train,Fly,Hotspot,InnKeeper}
 public class BotPoi {
  public static BotPoi Current;public PoiType Type;public ulong Guid;public uint Entry;public WoWObject ObjectValue;public WoWPoint LocationValue;
  public WoWObject AsObject{get{var observed=ObjectValue;PoiTravelCases.Event("object");return observed;}}
  public WoWPoint Location{get{var observed=LocationValue;PoiTravelCases.Event("location");return observed;}}
 }
}
/* Controlled diagnostics. */ namespace Styx.Helpers {public static class Logging{public static void Write(string format,params object[] args){PoiTravelCases.Logs++;PoiTravelCases.Event("log");}}}
/* Controlled external dispatch only. */ namespace Styx.Logic.Pathing {
 public static class Navigator{public static object NavigationProvider;public static MoveResult MoveTo(WoWPoint point)=>throw new InvalidOperationException("unexpected base navigator");public static RunStatus GetRunStatusFromMoveResult(MoveResult result)=>RunStatus.Success;}
 public static class Flightor{public static void MoveTo(WoWPoint point){PoiTravelCases.Moves.Add(point);PoiTravelCases.Event("move");}}
 // This fixture verifies the complete action's admission boundary. Geometry,
 // landing and observation acknowledgement execute in the separately linked
 // IndoorApproachRegressionTests; this leaf never supplies Ready on dispatch.
 public enum GroundTransitionPurpose{Interaction,Combat}
 public enum GroundTransitionState{Pending,Ready,Unavailable,Revoked}
 public sealed class GroundTransition{
  public GroundTransition(GroundTransitionPurpose purpose){}
  public GroundTransitionState Tick(WoWPoint destination,WoWObject subject,Func<bool> admitted){if(!admitted())return GroundTransitionState.Revoked;PoiTravelCases.Approaches.Add(destination);PoiTravelCases.Event("move");return admitted()?GroundTransitionState.Pending:GroundTransitionState.Revoked;}
  public void Cancel(){}
 }
}
""";
}
