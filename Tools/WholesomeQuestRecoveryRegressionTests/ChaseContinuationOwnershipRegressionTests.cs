using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

// Complete tracked chase owner, NavigationAction and Blacklist; real TreeSharp.
// Control only world observations, navigation/target effects and diagnostics.
internal static class ChaseContinuationOwnershipRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        string? root = null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CopilotBuddy.csproj"))) { root = d.FullName; break; }
        if (root == null) throw new InvalidOperationException("Tracked checkout required");
        string temp = Path.Combine(Path.GetTempPath(), "cb-chase-continuity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        bool logging = Styx.Helpers.Logging.FileLogging;
        try
        {
            Styx.Helpers.Logging.FileLogging = false;
            foreach (string path in new[] { "Bots/Grind/Levelbot/Actions/Combat/ActionMoveToTarget.cs", "CommonBehaviors/Actions/NavigationAction.cs", "CommonBehaviors/Actions/GetPointDelegate.cs", "Styx/Logic/Blacklist.cs" })
                File.Copy(Path.Combine(root, path), Path.Combine(temp, Path.GetFileName(path)));
            File.WriteAllText(Path.Combine(temp, "Boundary.cs"), Boundary);
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { temp })!;
            foreach (string path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
                compilerType.GetMethod("AddReference", flags)!.Invoke(compiler, new object[] { path });
            var result = (CompilerResults)compilerType.GetMethod("Compile", flags)!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Actual chase compilation: " + string.Join("; ", errors.Select(e => e.ToString())));
            var assembly = (Assembly)compilerType.GetProperty("CompiledAssembly", flags)!.GetValue(compiler)!;
            try { assembly.GetType("ChaseCases", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        finally { Styx.Helpers.Logging.FileLogging = logging; Directory.Delete(temp, true); }
    }
    private const string Boundary = """
#nullable disable
using System;using System.Collections.Generic;using System.Linq;using System.Reflection;
using TreeSharp;using Styx;using Styx.Logic;using Styx.Logic.Pathing;using Styx.WoWInternals;using Styx.WoWInternals.WoWObjects;using Levelbot.Actions.Combat;
public static class ChaseCases {
 sealed class Failure(string why):Exception(why){}
 public static LocalPlayer Player;public static WoWUnit Target;public static System.Action OnPath,OnMove,OnClear,OnStatus,OnBlacklist;
 public static readonly List<WoWPoint> Moves=new();public static int Clears,TargetClears,Paths;
 public static MoveResult MoveReceipt=MoveResult.Moved;public static string PathMode="healthy";
 static void Check(bool ok,string why){if(!ok)throw new Failure(why);}
 public static void Effect(ref System.Action callback){var action=callback;callback=null;action?.Invoke();}
 static void Reset(){
  OnPath=OnMove=OnClear=OnStatus=OnBlacklist=null;Moves.Clear();Clears=TargetClears=Paths=0;PathMode="healthy";MoveReceipt=MoveResult.Moved;
  Player=new LocalPlayer{Guid=1,Location=new WoWPoint(10,10,10)};Target=new WoWUnit{Guid=2,Location=new WoWPoint(100,10,10)};
  StyxWoW.Me=Player;Player.CurrentTarget=Target;Targeting.Instance.FirstUnit=Target;Targeting.PullDistance=30;
  Navigator.NavigationProvider=new object();Navigator.PathPrecision=5;BotEvents.Player.Reset();
  ((IDictionary<ulong,DateTime>)typeof(Blacklist).GetField("_blacklistedGuids",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null)).Clear();
 }
 static RunStatus Tick(ActionMoveToTarget owner){
  var errors=new List<string>();
  var logged=typeof(Composite).Assembly.GetType("Styx.Helpers.Logging",true).GetEvent("OnMessageLogged");
  System.Action<Styx.Helpers.LogLevel,string> handler=(_,text)=>{if(text.Contains("Exception")||text.Contains("Object reference not set"))errors.Add(text);};
  logged.AddEventHandler(null,handler);
  try{owner.Start(null);try{var status=owner.Tick(null);Check(errors.Count==0,"owner swallowed an exception: "+string.Join(";",errors));return status;}finally{owner.Stop(null);}}
  finally{logged.RemoveEventHandler(null,handler);}
 }
 static void Expire(ActionMoveToTarget owner)=>typeof(ActionMoveToTarget).GetField("_moveStartTime",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(owner,unchecked(Environment.TickCount-45001));
 static void Change(string mode){switch(mode){
  case "actor":StyxWoW.Me=new LocalPlayer{Guid=1,Location=Player.Location,CurrentTarget=Target};break;
  case "actor-guid":Player.Guid=7;break;case "actor-dead":Player.IsAlive=false;break;case "actor-invalid":Player.IsValid=false;break;
  case "target":Targeting.Instance.FirstUnit=new WoWUnit{Guid=7,Location=Target.Location};break;
  case "target-guid":Target.Guid=7;break;case "target-dead":Target.IsAlive=false;break;case "target-invalid":Target.IsValid=false;break;
  case "display":Player.CurrentTarget=new WoWUnit{Guid=8,Location=Target.Location};break;
  case "destination":Target.Location=Target.Location.Add(0,0,20);break;
  case "map":Player.MapId++;break;case "provider":Navigator.NavigationProvider=new object();break;
  case "casting":Player.IsCasting=true;break;case "channel":Player.ChanneledCastingSpellId=88;break;
  case "flying":Player.Flags=0x02000000u;break;case "falling":Player.Flags=0x00001000u;break;
  case "transport":Player.Transport=9;break;case "unknown-movement":Player.MovementKnown=false;break;
  default:throw new InvalidOperationException(mode);
 }}
 public static void Run(){
  var cases=new List<(string,System.Action)>();void Add(string name,System.Action test)=>cases.Add((name,()=>{Reset();test();}));
  Add("healthy chase",()=>{Check(Tick(new ActionMoveToTarget())==RunStatus.Success&&Moves.SequenceEqual(new[]{Target.Location})&&Paths==1,"healthy actual chase changed");});
  foreach(string state in new[]{"flying","falling","transport","unknown-movement"})
  {
   string movement=state;
   Add("unsupported "+movement+" cannot establish ground reachability or blacklist a mob",()=>{
    Change(movement);PathMode="empty";Tick(new ActionMoveToTarget());
    Check(Paths==0&&Moves.Count==0&&TargetClears==0&&!Blacklist.Contains(Target.Guid),"unsupported pursuit reached the ground pathfinder or blacklisted its target");
   });
   Add("ground path completion rechecks "+movement+" before movement or rejection",()=>{
    PathMode="empty";OnPath=()=>Change(movement);Tick(new ActionMoveToTarget());
    Check(Paths==1&&Moves.Count==0&&TargetClears==0&&!Blacklist.Contains(Target.Guid),"changed physical support borrowed an earlier ground query");
   });
  }
  Add("flight transition cannot consume an old ground chase timeout",()=>{
   var owner=new ActionMoveToTarget();Tick(owner);Expire(owner);Player.Flags=0x02000000u;Moves.Clear();
   Tick(owner);Check(!Blacklist.Contains(Target.Guid)&&Moves.Count==0,"airborne interruption consumed a ground timeout");
   Player.Flags=0;Check(Tick(owner)==RunStatus.Success&&Moves.Count==1&&!Blacklist.Contains(Target.Guid),"grounded successor inherited an expired aerial pause");
  });
  foreach(string mode in new[]{"actor","actor-guid","target","target-guid","map","provider"}){string change=mode;
   Add("timeout budget belongs to "+change,()=>{var owner=new ActionMoveToTarget();Tick(owner);Moves.Clear();Expire(owner);Change(change);Check(Tick(owner)==RunStatus.Success&&Moves.Count==1&&!Blacklist.Contains(Targeting.Instance.FirstUnit.Guid)&&TargetClears==0,"new chase inherited old timeout/blacklist");});
  }
  Add("owned timeout still rejects original target",()=>{var owner=new ActionMoveToTarget();Tick(owner);Expire(owner);Moves.Clear();Check(Tick(owner)==RunStatus.Failure&&Blacklist.Contains(Target.Guid)&&TargetClears==1&&Moves.Count==0,"owned timeout lost original rejection");});
  Add("owned timeout preserves unrelated selected target",()=>{var owner=new ActionMoveToTarget();Tick(owner);Expire(owner);Change("display");var displayed=Player.CurrentTarget;Tick(owner);Check(TargetClears==0&&ReferenceEquals(Player.CurrentTarget,displayed),"timeout cleared a replacement displayed target");});
  foreach(string phase in new[]{"path","status","move"})foreach(string mode in new[]{"actor","actor-guid","actor-dead","actor-invalid","target","target-guid","target-dead","target-invalid","display","destination","map","provider","casting","channel"}){
   string stage=phase,change=mode;
   Add(stage+" revokes "+change,()=>{if(stage=="path")OnPath=()=>Change(change);else if(stage=="status")OnStatus=()=>Change(change);else OnMove=()=>Change(change);
    var result=Tick(new ActionMoveToTarget());Check(result==RunStatus.Failure&&Moves.Count==(stage=="move"?1:0)&&Clears==0&&TargetClears==0&&!Blacklist.Contains(Target.Guid),"obsolete navigation result continued movement, success or cleanup");});
  }
  foreach(string mode in new[]{"actor-dead","actor-invalid","target-dead","target-invalid","casting","channel"}){string change=mode;Add("initial invalid "+change,()=>{Change(change);Check(Tick(new ActionMoveToTarget())==RunStatus.Failure&&Paths==0&&Moves.Count==0&&TargetClears==0,"unavailable owner reached navigation");});}
  Add("casting pause does not consume next chase timeout",()=>{var owner=new ActionMoveToTarget();Tick(owner);Expire(owner);Player.IsCasting=true;Tick(owner);Player.IsCasting=false;Moves.Clear();Check(Tick(owner)==RunStatus.Success&&Moves.Count==1&&!Blacklist.Contains(Target.Guid),"cast pause transferred an expired chase budget into resumed movement");});
  foreach(bool actor in new[]{false,true})Add("zero participant "+actor,()=>{if(actor)Player.Guid=0;else Target.Guid=0;Check(Tick(new ActionMoveToTarget())==RunStatus.Failure&&Paths==0&&Moves.Count==0,"unknown participant reached navigation");});
  foreach(string mode in new[]{"null","empty","bad-z","nan-middle","nan-end"}){string path=mode;Add("invalid path "+path,()=>{PathMode=path;Check(Tick(new ActionMoveToTarget())==RunStatus.Failure&&Moves.Count==0,"null/invalid route was followed or escaped");});}
  foreach(MoveResult value in new[]{MoveResult.Failed,MoveResult.PathGenerationFailed}){var result=value;Add("propagate "+result,()=>{MoveReceipt=result;Check(Tick(new ActionMoveToTarget())==RunStatus.Failure&&Moves.Count==1,"navigation failure hidden");});}
  foreach(string mode in new[]{"actor","display","target","map"}){string change=mode;Add("blacklist callback preserves "+change,()=>{PathMode="empty";OnBlacklist=()=>Change(change);Tick(new ActionMoveToTarget());Check(!Blacklist.Contains(Target.Guid)&&TargetClears==0,"blacklist diagnostic transferred mutation/cleanup");});}
  Add("in-range observed target remains ready",()=>{Target.Location=Player.Location.Add(5,0,0);Check(Tick(new ActionMoveToTarget())==RunStatus.Success&&Clears==1&&Paths==0,"in-range behavior changed");});
  Add("in-range cleanup cannot certify replacement actor",()=>{Target.Location=Player.Location.Add(5,0,0);OnClear=()=>Change("actor");Check(Tick(new ActionMoveToTarget())==RunStatus.Failure&&Clears==1,"old arrival cleanup reported replacement ready");});
  int passed=0,assertions=0,unexpected=0;foreach(var item in cases){try{item.Item2();passed++;Console.WriteLine("PASS chase ownership: "+item.Item1);}catch(Failure e){assertions++;Console.Error.WriteLine("FAIL chase ownership: "+item.Item1+": "+e.Message);}catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR chase ownership: "+item.Item1+": "+e);}}
  Console.WriteLine($"Chase ownership scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; complete tracked chase/Blacklist/NavigationAction and real TreeSharp; controlled paths; no physical route or live HoJ acceptance.");
  if(assertions+unexpected!=0)throw new InvalidOperationException("Chase ownership regression");
 }
}
/* Controlled observed objects. */ namespace Styx.WoWInternals.WoWObjects {
 public class WoWObject{public ulong Guid;}
 public class WoWUnit:WoWObject{public bool IsValid=true,IsAlive=true;public string Name="controlled";public virtual WoWPoint Location{get;set;}public bool InLineOfSpellSight=true;public WoWObjectType Type=>WoWObjectType.Unit;public int Level=80,Race,Class;}
 public class WoWPlayer:WoWUnit{}
 public class LocalPlayer:WoWPlayer{public uint MapId,Flags;public ulong Transport;public bool MovementKnown=true;public bool TryGetMovementState(out uint flags,out ulong transport){flags=Flags;transport=Transport;return MovementKnown;}public bool IsCasting;public int ChanneledCastingSpellId;public WoWUnit CurrentTarget;public ulong CurrentTargetGuid=>CurrentTarget?.Guid??0;public void ClearTarget(){ChaseCases.TargetClears++;CurrentTarget=null;}}
}
/* Controlled world boundary. */ namespace Styx {public static class StyxWoW{public static LocalPlayer Me;}public static class BotEvents{public static class Player{public class MobKilledEventArgs{}public static event System.Action<MobKilledEventArgs> OnMobKilled;public static void Reset(){OnMobKilled=null;}}}}
/* Controlled world boundary. */ namespace Styx.WoWInternals {public static class ObjectManager{public static LocalPlayer Me=>StyxWoW.Me;}}
/* Controlled targeting observations. */ namespace Styx.Logic {public class Targeting{public static Targeting Instance=new();public static float PullDistance=30;public WoWUnit FirstUnit;}}
/* Controlled diagnostics. */ namespace Styx.Helpers {public static class Logging{public static void Write(string format,params object[] args){}public static void WriteDebug(string format,params object[] args){if(format.StartsWith("Blacklisting",StringComparison.Ordinal))ChaseCases.Effect(ref ChaseCases.OnBlacklist);}}}
/* Controlled status callback. */ namespace Styx.Logic.BehaviorTree{public static class TreeRoot{public static string StatusText{set{ChaseCases.Effect(ref ChaseCases.OnStatus);}}}}
/* Controlled navigation effects, no route algorithm replica. */ namespace Styx.Logic.Pathing {
 public static class Navigator {
  public static object NavigationProvider;public static float PathPrecision=5;
  public static WoWPoint[] GeneratePath(WoWPoint from,WoWPoint to){ChaseCases.Paths++;ChaseCases.Effect(ref ChaseCases.OnPath);return ChaseCases.PathMode switch{"null"=>null,"empty"=>Array.Empty<WoWPoint>(),"bad-z"=>new[]{to.Add(0,0,20)},"nan-middle"=>new[]{from,new WoWPoint(float.NaN,0,0),to},"nan-end"=>new[]{new WoWPoint(float.NaN,to.Y,to.Z)},_=>new[]{from,to}};}
  public static MoveResult MoveTo(WoWPoint point){ChaseCases.Moves.Add(point);ChaseCases.Effect(ref ChaseCases.OnMove);return ChaseCases.MoveReceipt;}
  public static void Clear(){ChaseCases.Clears++;ChaseCases.Effect(ref ChaseCases.OnClear);}
  public static RunStatus GetRunStatusFromMoveResult(MoveResult result)=>result==MoveResult.Failed||result==MoveResult.PathGenerationFailed?RunStatus.Failure:RunStatus.Success;
 }
}
""";
}
