using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

// Complete ForcedMoveTo and its real ForcedBehavior base/TreeSharp. Controlled
// mount/navigation/world leaves do not certify landing or physical path safety.
internal static class ForcedMoveArrivalOwnershipRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        string? root = null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CopilotBuddy.csproj"))) { root = d.FullName; break; }
        if (root == null) throw new InvalidOperationException("Tracked checkout required");
        string temp = Path.Combine(Path.GetTempPath(), "cb-forced-arrival-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        bool logging = Styx.Helpers.Logging.FileLogging;
        try
        {
            Styx.Helpers.Logging.FileLogging = false;
            foreach (string path in new[] { "Bots/Quest/QuestOrder/ForcedMoveTo.cs", "Bots/Quest/QuestOrder/ForcedBehavior.cs" })
                File.Copy(Path.Combine(root, path), Path.Combine(temp, Path.GetFileName(path)));
            File.WriteAllText(Path.Combine(temp, "Boundary.cs"), Boundary);
            Type type = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(type, new object[] { temp })!;
            foreach (string path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
                type.GetMethod("AddReference", flags)!.Invoke(compiler, new object[] { path });
            var result = (CompilerResults)type.GetMethod("Compile", flags)!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Forced arrival compilation: " + string.Join("; ", errors.Select(e => e.ToString())));
            var assembly = (Assembly)type.GetProperty("CompiledAssembly", flags)!.GetValue(compiler)!;
            try { assembly.GetType("ArrivalCases", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        finally { Styx.Helpers.Logging.FileLogging = logging; Directory.Delete(temp, true); }
    }
    private const string Boundary = """
#nullable disable
using System;using System.Collections.Generic;using System.Linq;using TreeSharp;using Styx;using Styx.Logic;using Styx.Logic.Pathing;using Styx.WoWInternals;using Styx.WoWInternals.WoWObjects;using Bots.Quest.QuestOrder;
public static class ArrivalCases {
 sealed class Failure(string why):Exception(why){}
 public static LocalPlayer Player;public static System.Action OnCanFly,OnDismount,OnShouldMount,OnMount,OnGround,OnFlight;
 public static int Dismounts,Mounts,Ground,Flight;public static bool AllowDismount=true,ShouldMount=false;public static MoveResult Result=MoveResult.Moved;
 public static void Effect(ref System.Action effect){var callback=effect;effect=null;callback?.Invoke();}
 static void Check(bool ok,string why){if(!ok)throw new Failure(why);}
 static void Reset(){
  Player=new LocalPlayer{Guid=1,Location=new WoWPoint(10,10,10)};StyxWoW.Me=Player;
  OnCanFly=OnDismount=OnShouldMount=OnMount=OnGround=OnFlight=null;
  Dismounts=Mounts=Ground=Flight=0;AllowDismount=true;ShouldMount=false;Result=MoveResult.Moved;
  QuestOrder.Instance=new QuestOrder();Flightor.Flyable=false;Navigator.NavigationProvider=new object();
 }
 static ForcedMoveTo Make(WoWPoint destination,NavType nav=NavType.Run,float precision=1.5f,uint quest=0){var owner=new ForcedMoveTo(destination,"controlled",precision,quest,nav);QuestOrder.Instance.CurrentBehavior=owner;return owner;}
 static RunStatus Tick(ForcedMoveTo owner){var root=owner.Branch;root.Start(null);try{return root.Tick(null);}finally{root.Stop(null);}}
 static void Change(string kind){switch(kind){case "actor":StyxWoW.Me=new LocalPlayer{Guid=1,Location=Player.Location};break;case "guid":Player.Guid=9;break;case "dead":Player.IsAlive=false;break;case "invalid":Player.IsValid=false;break;case "map":Player.MapId++;break;case "provider":Navigator.NavigationProvider=new object();break;case "casting":Player.IsCasting=true;break;default:throw new InvalidOperationException(kind);}}
 public static void Run(){
  var cases=new List<(string,System.Action)>();void Add(string name,System.Action test)=>cases.Add((name,()=>{Reset();test();}));
  foreach(float distance in new[]{2f,5f,9.9f})foreach(bool vertical in new[]{false,true})foreach(bool dismount in new[]{false,true}){float d=distance;bool z=vertical,allowed=dismount;
   Add("near flight/"+d+"/"+z+"/"+allowed,()=>{Player.Mounted=true;Player.MovementInfo.IsFlying=!allowed;AllowDismount=allowed;var destination=Player.Location.Add(z?0:d,0,z?d:0);var owner=Make(destination,NavType.Fly);Tick(owner);Check(!owner.IsDone,"ten-yard dismount attempt became exact waypoint completion");Check(Ground+Flight==1,"incomplete waypoint did not continue approach");if(!allowed)Check(Ground==0,"airborne refused dismount started ground navigation");});
  }
  foreach(NavType mode in new[]{NavType.Run,NavType.Fly}){var nav=mode;
   Add("actual precision/"+nav,()=>{var owner=Make(Player.Location.Add(1,0,0),nav);Check(Tick(owner)==RunStatus.Success&&owner.IsDone&&Ground+Flight+Dismounts==0,"actual finite arrival changed");});
   Add("far dispatch is not arrival/"+nav,()=>{var owner=Make(Player.Location.Add(100,0,0),nav);Tick(owner);Check(!owner.IsDone&&Ground+Flight==1,"far dispatch reported arrival");});
   Add("later observed arrival/"+nav,()=>{var destination=Player.Location.Add(100,0,0);var owner=Make(destination,nav);Tick(owner);Player.Location=destination;Check(Tick(owner)==RunStatus.Success&&owner.IsDone,"later actual position did not complete");});
  }
  foreach(MoveResult value in new[]{MoveResult.Failed,MoveResult.PathGenerationFailed}){var result=value;Add("navigation refusal/"+result,()=>{Result=result;var owner=Make(Player.Location.Add(100,0,0));Check(Tick(owner)==RunStatus.Failure&&!owner.IsDone&&Ground==1,"actual navigator refusal ignored");});}
  foreach(string stage in new[]{"dismount","should-mount","mount","ground","flight"})foreach(string mutation in new[]{"actor","guid","dead","invalid","map","provider","casting"}){string phase=stage,change=mutation;
   Add("callback/"+phase+"/"+change,()=>{
    bool fly=phase is "dismount" or "flight";var owner=Make(Player.Location.Add(phase=="dismount"?5:100,0,0),fly?NavType.Fly:NavType.Run);
    if(phase=="dismount"){Player.Mounted=true;OnDismount=()=>Change(change);}else if(phase=="should-mount")OnShouldMount=()=>Change(change);else if(phase=="mount"){ShouldMount=true;OnMount=()=>Change(change);}else if(phase=="ground")OnGround=()=>Change(change);else OnFlight=()=>Change(change);
    Check(Tick(owner)==RunStatus.Failure&&!owner.IsDone,"obsolete callback continuation was accepted");Check(Ground+Flight==(phase=="ground"||phase=="flight"?1:0),"movement continued after callback invalidation");
   });
  }
  foreach(string invalid in new[]{"dead","invalid","guid","casting"}){string kind=invalid;Add("invalid initial actor/"+kind,()=>{Change(kind);if(kind=="guid")Player.Guid=0;var owner=Make(Player.Location.Add(100,0,0));Check(Tick(owner)==RunStatus.Failure&&!owner.IsDone&&Ground+Flight==0,"unknown actor moved");});}
  foreach(float precision in new[]{float.NaN,float.PositiveInfinity,-1f}){float p=precision;Add("invalid precision/"+p,()=>{var owner=Make(Player.Location.Add(100,0,0),NavType.Run,p);Check(Tick(owner)==RunStatus.Failure&&!owner.IsDone&&Ground+Flight==0,"invalid precision became movement/arrival permission");});}
  Add("nonfinite destination",()=>{var owner=Make(new WoWPoint(float.NaN,10,10));Check(Tick(owner)==RunStatus.Failure&&!owner.IsDone&&Ground+Flight==0,"invalid destination dispatched");});
  Add("completed waypoint cannot transfer actors",()=>{var owner=Make(Player.Location);Tick(owner);Change("actor");Check(!owner.IsDone,"old actor arrival completed replacement work");});
  Add("quest completion remains independent arrival criterion",()=>{var owner=Make(Player.Location.Add(100,0,0),NavType.Run,1.5f,123);Player.QuestLog.Quest.IsCompleted=true;Check(owner.IsDone,"existing owned quest completion was lost");});
  int passed=0,assertions=0,unexpected=0;foreach(var item in cases){try{item.Item2();passed++;Console.WriteLine("PASS forced arrival: "+item.Item1);}catch(Failure e){assertions++;Console.Error.WriteLine("FAIL forced arrival: "+item.Item1+": "+e.Message);}catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR forced arrival: "+item.Item1+": "+e);}}
  Console.WriteLine($"Forced arrival scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; complete tracked ForcedMoveTo/base and real TreeSharp; controlled mount/path/world; no physical landing acceptance.");
  if(assertions+unexpected!=0)throw new InvalidOperationException("Forced arrival regression");
 }
}
/* Controlled actor observations. */ namespace Styx.WoWInternals.WoWObjects{public class Movement{public bool IsFlying;}public class LocalPlayer{public ulong Guid;public uint MapId;public bool IsValid=true,IsAlive=true,IsCasting,IsFalling,Mounted;public int ChanneledCastingSpellId;public Movement MovementInfo=new();public WoWPoint Location;public QuestLog QuestLog=new();}public class QuestLog{public Styx.Logic.Questing.PlayerQuest Quest=new();public Styx.Logic.Questing.PlayerQuest GetQuestById(uint id)=>Quest;}}
/* Controlled quest observation. */ namespace Styx.Logic.Questing{public class PlayerQuest{public bool IsCompleted;}}
/* Controlled world boundary. */ namespace Styx{public static class StyxWoW{public static LocalPlayer Me;}}
/* Controlled world boundary. */ namespace Styx.WoWInternals{public static class ObjectManager{public static LocalPlayer Me=>StyxWoW.Me;}}
/* Controlled order selection. */ namespace Bots.Quest.QuestOrder{public class QuestOrder{public static QuestOrder Instance=new();public ForcedBehavior CurrentBehavior;public NavType NavType=>CurrentBehavior?.NavType??(Flightor.CanFly?Styx.NavType.Fly:Styx.NavType.Run);}}
/* Controlled navigation and flight. */ namespace Styx.Logic.Pathing{public static class Navigator{public static object NavigationProvider;public static MoveResult MoveTo(WoWPoint p){ArrivalCases.Ground++;ArrivalCases.Effect(ref ArrivalCases.OnGround);return ArrivalCases.Result;}public static RunStatus GetRunStatusFromMoveResult(MoveResult r)=>r is MoveResult.Failed or MoveResult.PathGenerationFailed?RunStatus.Failure:RunStatus.Success;}public static class Flightor{public static bool Flyable;public static bool CanFly{get{ArrivalCases.Effect(ref ArrivalCases.OnCanFly);return Flyable;}}public static void MoveTo(WoWPoint p,float height){ArrivalCases.Flight++;ArrivalCases.Effect(ref ArrivalCases.OnFlight);}}}
/* Controlled safe dismount receipt/state. */ namespace Styx.Logic{public static class Mount{public static void Dismount(string why){ArrivalCases.Dismounts++;ArrivalCases.Effect(ref ArrivalCases.OnDismount);if(ArrivalCases.AllowDismount)StyxWoW.Me.Mounted=false;}public static bool ShouldMount(WoWPoint p){ArrivalCases.Effect(ref ArrivalCases.OnShouldMount);return ArrivalCases.ShouldMount;}public static void StateMount(LocationRetriever destination){ArrivalCases.Mounts++;ArrivalCases.Effect(ref ArrivalCases.OnMount);}}public static class MountHelper{public static bool Mounted=>StyxWoW.Me.Mounted;}}
/* Controlled diagnostics. */ namespace Styx.Helpers{public static class Logging{public static void Write(string text,params object[] args){}}}
/* Controlled status. */ namespace Styx.Logic.BehaviorTree{public static class TreeRoot{public static string GoalText;}}
""";
}
