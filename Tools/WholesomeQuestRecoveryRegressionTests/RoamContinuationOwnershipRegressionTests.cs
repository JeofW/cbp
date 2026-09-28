using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Complete tracked roam factory and its predicates, actual POI decorators,
// target admission and POI action, real TreeSharp/Wait and WoWPoint. Only
// external world, mount, navigation, diagnostics and target effects are controlled.
internal static class RoamContinuationOwnershipRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        string? root = null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CopilotBuddy.csproj"))) { root = d.FullName; break; }
        if (root == null) throw new InvalidOperationException("Tracked checkout required");
        var parsed = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Bots/Grind/LevelBot.cs"))).GetRoot();
        var names = new[] { "CreateRoamBehavior", "ShouldClearPoiForBetterTarget", "ShouldMoveToHotspot", "ShouldMoveCloserToTarget" };
        var methods = names.Select(name => parsed.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == name).ToString());
        string temp = Path.Combine(Path.GetTempPath(), "cb-roam-ownership-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        bool logging = Styx.Helpers.Logging.FileLogging;
        try
        {
            Styx.Helpers.Logging.FileLogging = false;
            File.WriteAllText(Path.Combine(temp, "Probe.cs"), Prefix + string.Join("\n", methods) + "}\n" + Boundary);
            foreach (string path in new[] {
                "CommonBehaviors/Decorators/DecoratorIsPoiType.cs",
                "CommonBehaviors/Decorators/DecoratorIsNotPoiType.cs",
                "Bots/Grind/Levelbot/Decorators/Combat/DecoratorNeedToFindTarget.cs",
                "CommonBehaviors/Actions/ActionSetPoi.cs",
                "CommonBehaviors/Actions/RetrieveBotPoiDelegate.cs",
                "CommonBehaviors/Actions/ActionIdle.cs",
                "CommonBehaviors/Actions/ActionAlwaysSucceed.cs" })
                File.Copy(Path.Combine(root, path), Path.Combine(temp, Path.GetFileName(path)));
            Type type = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(type, new object[] { temp })!;
            foreach (string path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
                type.GetMethod("AddReference", flags)!.Invoke(compiler, new object[] { path });
            var result = (CompilerResults)type.GetMethod("Compile", flags)!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Actual roam compilation: " + string.Join("; ", errors.Select(e => e.ToString())));
            var assembly = (Assembly)type.GetProperty("CompiledAssembly", flags)!.GetValue(compiler)!;
            try { assembly.GetType("RoamCases", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        finally { Styx.Helpers.Logging.FileLogging = logging; Directory.Delete(temp, true); }
    }

    private const string Prefix = """
#nullable disable
using System;using System.Collections.Generic;using System.Linq;using System.Reflection;
using TreeSharp;using Styx;using Styx.Helpers;using Styx.Logic;using Styx.Logic.AreaManagement;
using Styx.Logic.BehaviorTree;using Styx.Logic.Combat;using Styx.Logic.Pathing;using Styx.Logic.POI;
using Styx.Logic.Profiles;using Styx.WoWInternals;using Styx.WoWInternals.WoWObjects;
using CommonBehaviors.Actions;using CommonBehaviors.Decorators;using Levelbot.Actions.Combat;using Levelbot.Decorators.Combat;
public static class RoamProbe {
""";

    private const string Boundary = """
public static class RoamCases {
 sealed class Failure(string why):Exception(why){}
 public static LocalPlayer Actor;public static WoWUnit Selected;public static BotPoi OriginalPoi;
 public static bool Acknowledge=true,ShouldMount=true;public static string Stage;public static System.Action Callback;
 public static int Targets,Publications,Mounts,Moves,Clears;public static WoWPoint Moved,Supplied;
 public static MoveResult Movement=MoveResult.Moved;
 static void Check(bool good,string why){if(!good)throw new Failure(why);}
 public static void Event(string stage){if(Stage==stage){var call=Callback;Stage=null;Callback=null;call?.Invoke();}}
 static void Reset(){
  Stage=null;Callback=null;Targets=Publications=Mounts=Moves=Clears=0;Acknowledge=ShouldMount=true;Movement=MoveResult.Moved;
  Moved=Supplied=WoWPoint.Empty;Actor=new LocalPlayer{Guid=1,MapId=530,Location=new WoWPoint(10,10,10)};StyxWoW.Me=Actor;
  Selected=new WoWUnit{Guid=2,Entry=200,Location=new WoWPoint(15,10,10)};Targeting.Instance=new Targeting{FirstUnit=Selected};
  OriginalPoi=new BotPoi(PoiType.None);BotPoi.Seed(OriginalPoi);Navigator.NavigationProvider=new object();
  StyxWoW.AreaManager=new AreaManager{CurrentGrindArea=new GrindArea{CurrentHotSpot=new Hotspot{Position=new WoWPoint(40,10,10)},HotspotChanged=true}};
  ProfileManager.CurrentProfile=new Profile();LevelbotSettings.Instance.GroundMountFarmingMode=false;
 }
 static Composite Branch(int index)=>RoamProbe.CreateRoamBehavior().Children[index];
 static RunStatus Tick(Composite tree){try{return tree.Tick(null);}catch(NullReferenceException){throw new Failure("unavailable participant escaped as a null dereference");}}
 static RunStatus Once(int index){var tree=Branch(index);tree.Start(null);try{return Tick(tree);}finally{tree.Stop(null);}}
 static void Expire(Composite tree){
  if(tree is Wait wait)typeof(Wait).GetField("End",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(wait,DateTime.MinValue);
  if(tree is GroupComposite group)foreach(var child in group.Children)if(child!=null)Expire(child);
  if(tree is Decorator decorator&&decorator.DecoratedChild!=null)Expire(decorator.DecoratedChild);
 }
 static void Change(string kind){switch(kind){
  case "actor":StyxWoW.Me=new LocalPlayer{Guid=1,MapId=Actor.MapId,CurrentTarget=Selected};break;
  case "actor-missing":StyxWoW.Me=null;break;case "actor-guid":Actor.Guid=7;break;case "actor-dead":Actor.IsAlive=false;break;case "actor-invalid":Actor.IsValid=false;break;
  case "map":Actor.MapId++;break;case "cast":Actor.IsCasting=true;break;case "channel":Actor.ChanneledCastingSpellId=123;break;
  case "combat":Actor.Combat=true;break;case "pet-combat":Actor.Pet=new WoWUnit{Guid=27,Combat=true};break;
  case "taxi":Actor.OnTaxi=true;break;case "transport":Actor.IsOnTransport=true;break;
  case "target-guid":Selected.Guid=7;break;case "target-dead":Selected.IsAlive=false;break;case "target-invalid":Selected.IsValid=false;break;
  case "selection":Targeting.Instance.FirstUnit=new WoWUnit{Guid=3};break;
  case "display":Actor.CurrentTarget=new WoWUnit{Guid=4};break;
  case "poi":BotPoi.Seed(new BotPoi(PoiType.Repair));break;case "poi-type":OriginalPoi.Type=PoiType.Fly;break;
  case "provider":Navigator.NavigationProvider=new object();break;
  case "area-manager":StyxWoW.AreaManager=new AreaManager{CurrentGrindArea=StyxWoW.AreaManager.CurrentGrindArea};break;
  case "area":StyxWoW.AreaManager.CurrentGrindArea=new GrindArea{CurrentHotSpot=StyxWoW.AreaManager.CurrentGrindArea.CurrentHotSpot,HotspotChanged=true};break;
  case "hotspot":StyxWoW.AreaManager.CurrentGrindArea.CurrentHotSpot=new Hotspot{Position=new WoWPoint(80,10,10)};break;
  case "hotspot-position":StyxWoW.AreaManager.CurrentGrindArea.CurrentHotSpot.Position=new WoWPoint(80,10,10);break;
  case "profile":ProfileManager.CurrentProfile=new Profile();break;
  default:throw new InvalidOperationException(kind);
 }}
 public static void Run(){
  int pass=0,assertions=0,unexpected=0,total=0;
  void Case(string name,System.Action body){total++;Reset();try{body();pass++;Console.WriteLine("PASS roam ownership: "+name);}catch(Failure e){assertions++;Console.Error.WriteLine("FAIL roam ownership: "+name+": "+e.Message);}catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR roam ownership: "+name+": "+e);}}
  Case("healthy immediate target",()=>{Check(Once(0)==RunStatus.Success&&Targets==1&&Publications==1&&BotPoi.Current.Guid==2,"healthy selection was lost");});
  Case("healthy delayed target",()=>{Acknowledge=false;var tree=Branch(0);tree.Start(null);try{Check(Tick(tree)==RunStatus.Running,"no target wait");Actor.CurrentTarget=Selected;Check(Tick(tree)==RunStatus.Success&&Publications==1&&Targets==1&&BotPoi.Current.Guid==2,"valid acknowledgement failed");}finally{tree.Stop(null);}});
  Case("unacknowledged timeout",()=>{Acknowledge=false;var tree=Branch(0);tree.Start(null);try{Check(Tick(tree)==RunStatus.Running,"no wait");Expire(tree);Check(Tick(tree)==RunStatus.Failure&&Publications==0,"timeout granted target authority");}finally{tree.Stop(null);}});
  foreach(string kind in new[]{"actor","actor-missing","actor-guid","actor-dead","actor-invalid","map","target-guid","target-dead","target-invalid","selection","display","poi","poi-type"}){
   string change=kind;Case("target wait revokes "+change,()=>{Acknowledge=false;var tree=Branch(0);tree.Start(null);try{
    Check(Tick(tree)==RunStatus.Running&&Targets==1,"failed to reach acknowledgement boundary");Actor.CurrentTarget=Selected;Change(change);var prior=BotPoi.Current;
    Check(Tick(tree)==RunStatus.Failure&&Publications==0&&ReferenceEquals(prior,BotPoi.Current),"revoked wait published another owner's Kill POI");
   }finally{tree.Stop(null);}});
  }
  foreach(string kind in new[]{"actor-dead","actor-invalid","target-dead","target-invalid"}){string change=kind;Case("initial invalid "+change,()=>{Change(change);Check(Once(0)==RunStatus.Failure&&Targets==0&&Publications==0,"invalid participant was selected");});}
  Case("initial zero target",()=>{Selected.Guid=0;Check(Once(0)==RunStatus.Failure&&Targets==0&&Publications==0,"zero target identity was selected");});
  Case("initial zero actor",()=>{Actor.Guid=0;Check(Once(0)==RunStatus.Failure&&Targets==0&&Publications==0,"zero actor identity was selected");});
  foreach(PoiType type in new[]{PoiType.Repair,PoiType.Sell,PoiType.Mail,PoiType.Fly}){var poi=type;Case("service intent preserves "+poi,()=>{OriginalPoi.Type=poi;Once(0);Check(Targets==0&&Publications==0,"roam replaced service/taxi intent");});}
  foreach(PoiType type in new[]{PoiType.Repair,PoiType.Sell,PoiType.Mail,PoiType.Fly}){var poi=type;Case("hotspot preserves service "+poi,()=>{OriginalPoi.Type=poi;Check(Once(1)==RunStatus.Failure&&Moves==0&&Mounts==0,"roam displaced an existing service/taxi route");});}
  Case("target idle callback revokes",()=>{Stage="idle";Callback=()=>Change("poi");Once(0);Check(Publications==0&&BotPoi.Current.Type==PoiType.Repair,"acknowledgement leaf callback replaced newer work");});
  Case("healthy mounted hotspot dispatch",()=>{var expected=StyxWoW.AreaManager.CurrentGrindArea.CurrentHotSpot.Position;Check(Once(1)==RunStatus.Success&&Mounts==1&&Moves==1&&Moved==expected,"healthy mount/move changed");});
  Case("healthy unmounted hotspot dispatch",()=>{ShouldMount=false;Check(Once(1)==RunStatus.Success&&Mounts==0&&Moves==1,"ordinary no-mount route changed");});
  foreach(MoveResult value in new[]{MoveResult.Failed,MoveResult.PathGenerationFailed,MoveResult.Moved,MoveResult.ReachedDestination}){var result=value;Case("navigation receipt "+result,()=>{Movement=result;Check(Once(1)==Navigator.GetRunStatusFromMoveResult(result)&&Moves==1,"move receipt was ignored");});}
  foreach(string stage in new[]{"mount-query","mount","status"})foreach(string kind in new[]{"actor","actor-missing","actor-guid","actor-dead","actor-invalid","map","cast","channel","combat","pet-combat","taxi","transport","poi","poi-type","provider","area-manager","area","hotspot","hotspot-position","profile"}){
   string boundary=stage,change=kind;Case("hotspot "+boundary+" revokes "+change,()=>{Stage=boundary;Callback=()=>Change(change);Check(Once(1)==RunStatus.Failure&&Moves==0,"revoked hotspot reached movement");if(boundary=="mount-query")Check(Mounts==0,"revoked mount query still mounted");if(boundary=="mount")Check(Supplied==WoWPoint.Empty,"mount supplier retained stale destination");});
  }
  foreach(string kind in new[]{"cast","channel","combat","pet-combat","taxi","transport","actor-dead","actor-invalid"}){string change=kind;Case("hotspot initial "+change,()=>{Change(change);Check(Once(1)==RunStatus.Failure&&Moves==0&&Mounts==0,"unavailable movement actor admitted");});}
  Case("dead pet does not block healthy roaming",()=>{Actor.Pet=new WoWUnit{Guid=27,Combat=true,IsAlive=false};Check(Once(1)==RunStatus.Success&&Moves==1,"dead pet retained combat movement ownership");});
  foreach(WoWPoint invalid in new[]{WoWPoint.Empty,new WoWPoint(float.PositiveInfinity,10,10),new WoWPoint(10,float.NaN,10),new WoWPoint(10,10,float.NegativeInfinity)}){var point=invalid;Case("invalid hotspot "+point,()=>{StyxWoW.AreaManager.CurrentGrindArea.CurrentHotSpot.Position=point;Check(Once(1)==RunStatus.Failure&&Moves==0&&Mounts==0,"invalid point admitted");});}
  Case("new activation can select its new actor",()=>{Check(Once(0)==RunStatus.Success,"initial selection failed");Reset();Actor.Guid=9;Check(Once(0)==RunStatus.Success&&Targets==1&&BotPoi.Current.Guid==2,"new activation was permanently revoked");});
  Console.WriteLine($"Roam ownership scenarios: {pass}/{total}; assertions={assertions}; unexpected={unexpected}; complete tracked factory/predicates/admission/POI action and real TreeSharp; controlled world/mount/navigation; no native atomicity or physical-route acceptance.");
  if(assertions+unexpected!=0)throw new InvalidOperationException("Roam ownership regression");
 }
}
/* Controlled world. */ namespace Styx.WoWInternals.WoWObjects {
 public class WoWUnit {public ulong Guid;public uint Entry=200,FactionId=1;public int Level=80;public bool IsValid=true,IsAlive=true,Combat;public bool Dead=>!IsAlive;public WoWPoint Location;public WoWUnit CurrentTarget;public ulong CurrentTargetGuid=>CurrentTarget?.Guid??0;public bool GotTarget=>CurrentTarget!=null;public double Distance=>5;public double DistanceSqr=>25;public bool InLineOfSpellSight=>true;
  public void Target(){RoamCases.Targets++;if(RoamCases.Acknowledge)Styx.StyxWoW.Me.CurrentTarget=this;}
 }
 public class LocalPlayer:WoWUnit {public uint MapId;public bool Mounted,IsCasting,IsOnTransport,OnTaxi;public int ChanneledCastingSpellId;public WoWUnit Pet;public bool GotAlivePet=>Pet?.IsAlive==true;}
}
/* Controlled world. */ namespace Styx {public static class StyxWoW {public static LocalPlayer Me;public static AreaManager AreaManager;public static void ResetAfk()=>RoamCases.Event("idle");}}
/* Controlled registry. */ namespace Styx.WoWInternals {public static class ObjectManager {public static LocalPlayer Me=>StyxWoW.Me;}}
/* Controlled area observations. */ namespace Styx.Logic.AreaManagement {public class Hotspot {public WoWPoint Position;}public class GrindArea {public Hotspot CurrentHotSpot;public bool HotspotChanged;public List<int> MobIDs=new(),Factions=new();public int TargetMinLevel=0,TargetMaxLevel=int.MaxValue;}public class AreaManager {public GrindArea CurrentGrindArea;}}
/* Controlled profile observation. */ namespace Styx.Logic.Profiles {public class Profile {public List<uint> Factions=new();}public static class ProfileManager {public static Profile CurrentProfile;}}
/* Controlled targeting and mount boundary. */ namespace Styx.Logic {
 public class Targeting {public static Targeting Instance=new();public WoWUnit FirstUnit;public bool KillBetweenHotspots;public static double PullDistance=30,PullDistanceSqr=900,CollectionRange=100;}
 public static class Battlegrounds {public static bool IsInsideBattleground=>false;}
 public delegate WoWPoint LocationRetriever();
 public static class Mount {public static bool ShouldMount(WoWPoint point){RoamCases.Event("mount-query");return RoamCases.ShouldMount;}public static void MountUp(LocationRetriever point){RoamCases.Mounts++;RoamCases.Event("mount");RoamCases.Supplied=point();}}
}
/* Controlled POI publication, no replacement policy. */ namespace Styx.Logic.POI {
 public enum PoiType {None,Kill,Loot,Skin,Harvest,Sell,Repair,Train,Buy,Mail,Fly}
 public class BotPoi {static BotPoi current;public PoiType Type;public ulong Guid;public uint Entry;public BotPoi(PoiType type){Type=type;}public BotPoi(WoWUnit target,PoiType type){Type=type;Guid=target.Guid;Entry=target.Entry;}
  public static BotPoi Current {get=>current;set{RoamCases.Publications++;current=value;}}public static void Seed(BotPoi value)=>current=value;public static void Clear(string reason){RoamCases.Clears++;current=new BotPoi(PoiType.None);}
 }
}
/* Controlled external diagnostics/settings. */ namespace Styx.Helpers {public static class Logging {public static void Write(string text,params object[] values){}public static void WriteDebug(string text,params object[] values){}}public class LevelbotSettings {public static LevelbotSettings Instance=new();public bool GroundMountFarmingMode;}}
/* Controlled status callback. */ namespace Styx.Logic.BehaviorTree {public static class TreeRoot {public static string StatusText {set{RoamCases.Event("status");}}}}
/* Controlled navigation boundary. */ namespace Styx.Logic.Pathing {public static class Navigator {public static object NavigationProvider;public static MoveResult MoveTo(WoWPoint point){RoamCases.Moves++;RoamCases.Moved=point;return RoamCases.Movement;}public static RunStatus GetRunStatusFromMoveResult(MoveResult result)=>result==MoveResult.Moved||result==MoveResult.ReachedDestination?RunStatus.Success:RunStatus.Failure;}}
/* Unselected external routine leaf. */ namespace Styx.Logic.Combat {public static class RoutineManager {public static Routine Current=new();}public class Routine {public Composite MoveToTargetBehavior=>null;}}
/* Unselected chase leaf; tested separately on its real owner. */ namespace Levelbot.Actions.Combat {public class ActionMoveToTarget:TreeSharp.Action {public ActionMoveToTarget():base(_=>RunStatus.Failure){}}}
/* Unselected final cleanup leaf. */ namespace CommonBehaviors.Actions {public class ActionClearPoi:TreeSharp.Action {public ActionClearPoi(string reason):base(_=>{BotPoi.Clear(reason);return RunStatus.Failure;}){}}}
""";
}
