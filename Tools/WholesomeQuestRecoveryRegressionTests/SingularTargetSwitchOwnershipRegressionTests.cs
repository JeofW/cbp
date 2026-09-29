using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

// Compile complete tracked Safers with real TreeSharp. Only world, selection,
// diagnostics and native Target effects are controlled; no replacement tree.
internal static class SingularTargetSwitchOwnershipRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        string? root = null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CopilotBuddy.csproj"))) { root = d.FullName; break; }
        if (root == null) throw new InvalidOperationException("Tracked checkout required");
        string temp = Path.Combine(Path.GetTempPath(), "cb-target-switch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        bool logging = Styx.Helpers.Logging.FileLogging;
        try
        {
            Styx.Helpers.Logging.FileLogging = false;
            File.Copy(Path.Combine(root, "runtime-snapshot/Routines/Singular wotlk/Helpers/Safers.cs"), Path.Combine(temp, "Safers.cs"));
            File.WriteAllText(Path.Combine(temp, "Boundary.cs"), Boundary);
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { temp })!;
            foreach (string path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
                compilerType.GetMethod("AddReference", flags)!.Invoke(compiler, new object[] { path });
            var result = (CompilerResults)compilerType.GetMethod("Compile", flags)!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Actual Safers compilation: " + string.Join("; ", errors.Select(e => e.ToString())));
            var assembly = (Assembly)compilerType.GetProperty("CompiledAssembly", flags)!.GetValue(compiler)!;
            try { assembly.GetType("SwitchCases", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        finally { Styx.Helpers.Logging.FileLogging = logging; Directory.Delete(temp, true); }
    }

    private const string Boundary = """
#nullable disable
using System;using System.Collections.Generic;using System.Linq;using System.Reflection;using System.Drawing;
using TreeSharp;using Styx;using Styx.Logic;using Styx.Logic.POI;using Styx.WoWInternals;using Styx.WoWInternals.WoWObjects;using Singular.Helpers;using Singular.Managers;using Singular.Settings;
public static class SwitchCases {
 sealed class Failure(string why):Exception(why){}
 public static System.Action OnLog;
 public static readonly List<ulong> Targets=new();
 public static bool Acknowledge=true;
 public static int TimerResets;
 public static LocalPlayer Player;public static WoWUnit Candidate,Original;
 static void Check(bool good,string why){if(!good)throw new Failure(why);}
 static void Reset(string mode){
  OnLog=null;Targets.Clear();Acknowledge=true;TimerResets=0;
  Player=new LocalPlayer{Guid=1,Combat=true};StyxWoW.Me=Player;
  Original=new WoWUnit{Guid=2};Candidate=new WoWUnit{Guid=3,Combat=true};
  Player.CurrentTarget=mode=="recovery"?null:Original;
  Targeting.Instance.FirstUnit=Candidate;BotPoi.Current=new BotPoi{Type=PoiType.Kill,AsObject=Candidate};
  TankManager.Instance.FirstUnit=Candidate;Group.MeIsTank=mode=="tank";
  SingularSettings.Instance.DisableAllTargeting=false;SingularSettings.Instance.DisableTankTargetSwitching=false;
  Unit.IsDungeonCombatBotTargetingRestricted=false;RaFHelper.Leader=null;ObjectManager.Units.Clear();
 }
 static void Change(string kind){
  switch(kind){
   case "actor-reference":StyxWoW.Me=new LocalPlayer{Guid=1,CurrentTarget=Player.CurrentTarget,Combat=true};break;
   case "actor-guid":Player.Guid=44;break;
   case "actor-dead":Player.IsAlive=false;break;
   case "actor-invalid":Player.IsValid=false;break;
   case "target-guid":Candidate.Guid=44;break;
   case "target-dead":Candidate.IsAlive=false;break;
   case "target-invalid":Candidate.IsValid=false;break;
   case "target-ineligible":Candidate.Eligible=false;break;
   case "display-replaced":Player.CurrentTarget=new WoWUnit{Guid=55,Combat=true};break;
   case "source-replaced":var next=new WoWUnit{Guid=77,Combat=true};Targeting.Instance.FirstUnit=next;TankManager.Instance.FirstUnit=next;BotPoi.Current.AsObject=next;break;
   case "targeting-disabled":SingularSettings.Instance.DisableAllTargeting=true;break;
   case "tank-role-changed":Group.MeIsTank=!Group.MeIsTank;break;
   default:throw new InvalidOperationException(kind);
  }
 }
 static RunStatus Tick(Composite tree){tree.Start(null);try{return tree.Tick(null);}finally{tree.Stop(null);}}
 static void Expire(Composite root){
  if(root is Wait wait)typeof(Wait).GetField("End",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(wait,DateTime.MinValue);
  if(root is GroupComposite group)foreach(var child in group.Children)if(child!=null)Expire(child);
  if(root is Decorator decorator && decorator.DecoratedChild!=null)Expire(decorator.DecoratedChild);
 }
 public static void Run(){
  var cases=new List<(string,System.Action)>();
  foreach(string mode in new[]{"preferred","recovery","tank"}){
   string family=mode;
   void Add(string label,System.Action body)=>cases.Add((family+"/"+label,()=>{Reset(family);body();}));
   Add("healthy selection",()=>{Tick(Safers.EnsureTarget());Check(Targets.SequenceEqual(new[]{3UL})&&ReferenceEquals(Player.CurrentTarget,Candidate),"healthy selected target lost");Check(TimerResets==(family=="tank"?1:0),"healthy tank timer changed");});
   foreach(string mutation in new[]{"actor-reference","actor-guid","actor-dead","actor-invalid","target-guid","target-dead","target-invalid","target-ineligible","display-replaced","source-replaced","targeting-disabled","tank-role-changed"}){
    string change=mutation;
    Add("diagnostic revokes "+change,()=>{OnLog=()=>Change(change);Tick(Safers.EnsureTarget());Check(Targets.Count==0&&TimerResets==0,"diagnostic callback transferred stale target selection or timer ownership");});
   }
   foreach(string mutation in new[]{"actor-reference","actor-guid","actor-dead","actor-invalid","target-guid","target-dead","target-invalid","target-ineligible","display-replaced","targeting-disabled","tank-role-changed"}){
    string change=mutation;
    Add("yield revokes "+change,()=>{
     Acknowledge=false;var tree=Safers.EnsureTarget();tree.Start(null);
     try{
      Check(tree.Tick(null)==RunStatus.Running&&Targets.Count==1,"did not reach actual acknowledgement wait");
      Change(change);Expire(tree);var status=tree.Tick(null);
      Check(status!=RunStatus.Success&&TimerResets==0&&Targets.Count==1,"obsolete or timeout-only wait reported target-ready or reset a replacement timer");
     }finally{tree.Stop(null);}
    });
   }
   Add("unacknowledged timeout",()=>{Acknowledge=false;var tree=Safers.EnsureTarget();tree.Start(null);try{Check(tree.Tick(null)==RunStatus.Running,"missing real wait");Expire(tree);Check(tree.Tick(null)!=RunStatus.Success&&TimerResets==0,"timeout became target acknowledgement");}finally{tree.Stop(null);}});
   Add("later acknowledged selection",()=>{Acknowledge=false;var tree=Safers.EnsureTarget();tree.Start(null);try{Check(tree.Tick(null)==RunStatus.Running,"missing wait");Player.CurrentTarget=Candidate;var result=tree.Tick(null);Check(result==(family=="preferred"?RunStatus.Failure:RunStatus.Success)&&Targets.Count==1,"valid acknowledgement lost or repeated dispatch");}finally{tree.Stop(null);}});
   Add("manual targeting off",()=>{SingularSettings.Instance.DisableAllTargeting=true;Tick(Safers.EnsureTarget());Check(Targets.Count==0,"manual targeting ignored");});
   Add("dead actor rejected",()=>{Player.IsAlive=false;Tick(Safers.EnsureTarget());Check(Targets.Count==0,"dead actor targeted");});
   Add("zero actor identity rejected",()=>{Player.Guid=0;Tick(Safers.EnsureTarget());Check(Targets.Count==0,"unknown actor targeted");});
  }
  cases.Add(("engaged stunned victim remains selected",()=>{Reset("preferred");Original.Combat=true;Original.Stunned=true;Original.CurrentTarget=null;Tick(Safers.EnsureTarget());Check(Targets.Count==0&&ReferenceEquals(Player.CurrentTarget,Original),"stun or missing victim target caused retarget");}));
  cases.Add(("idle absence still permits caller fallback",()=>{Reset("recovery");Targeting.Instance.FirstUnit=null;BotPoi.Current.Type=PoiType.None;Check(Tick(Safers.EnsureTarget())==RunStatus.Failure&&Targets.Count==0,"idle target search blocked fallback");}));
  cases.Add(("manual unengaged dungeon target stays suppressed",()=>{Reset("preferred");Original.Eligible=false;Unit.IsDungeonCombatBotTargetingRestricted=true;BotPoi.Current.Type=PoiType.None;Targeting.Instance.FirstUnit=null;Check(Tick(Safers.EnsureTarget())==RunStatus.Success&&Targets.Count==0,"manual dungeon protection changed");}));
  int passed=0,assertions=0,unexpected=0;
  foreach(var item in cases){try{item.Item2();passed++;Console.WriteLine("PASS target switch ownership: "+item.Item1);}catch(Failure e){assertions++;Console.Error.WriteLine("FAIL target switch ownership: "+item.Item1+": "+e.Message);}catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR target switch ownership: "+item.Item1+": "+e);}}
  Console.WriteLine($"Target switch ownership scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; complete tracked Safers and real TreeSharp; controlled observation/target command; no native or live HoJ proof.");
  if(assertions+unexpected!=0)throw new InvalidOperationException("Target switch ownership regression");
 }
}
/* Controlled observation boundary. */ namespace Styx.WoWInternals.WoWObjects {
 public class WoWObject{}
 public class WoWUnit:WoWObject {
  public ulong Guid;public bool IsValid=true,IsAlive=true,Combat,Aggro,Eligible=true,IsHostile=true,IsOnTransport,Mounted,Stunned;
  public float DistanceSqr=1;public WoWUnit CurrentTarget;public ulong CurrentTargetGuid=>CurrentTarget?.Guid??0;
  public bool Dead=>!IsAlive;public bool IsMe=>ReferenceEquals(this,StyxWoW.Me);
  public string SafeName()=>"controlled";
  public void Target(){SwitchCases.Targets.Add(Guid);if(SwitchCases.Acknowledge)StyxWoW.Me.CurrentTarget=this;}
 }
 public class LocalPlayer:WoWUnit{}
}
/* Controlled observation boundary. */ namespace Styx {public static class StyxWoW{public static LocalPlayer Me;}}
/* Controlled observation boundary. */ namespace Styx.Logic {
 public class Targeting{public static Targeting Instance=new();public WoWUnit FirstUnit;}
 public static class Blacklist{public static bool Contains(WoWUnit unit)=>false;}
 public static class RaFHelper{public static WoWUnit Leader;}
}
/* Controlled observation boundary. */ namespace Styx.Logic.POI {public enum PoiType{None,Kill}public class BotPoi{public static BotPoi Current=new();public PoiType Type;public WoWObject AsObject;}}
/* Controlled observation boundary. */ namespace Styx.WoWInternals {public static class ObjectManager{public static List<WoWUnit> Units=new();public static IEnumerable<T> GetObjectsOfType<T>(bool a=false,bool b=false)=>Units.OfType<T>();}}
/* Controlled observation boundary. */ namespace Singular.Settings {public class SingularSettings{public static SingularSettings Instance=new();public bool DisableAllTargeting,DisableTankTargetSwitching;}}
/* Controlled observation boundary. */ namespace Singular.Managers {
 public class Timer{public bool IsFinished=>true;public void Reset(){SwitchCases.TimerResets++;}}
 public class TankManager{public static TankManager Instance=new();public static Timer TargetingTimer=new();public WoWUnit FirstUnit;}
}
/* Controlled external helper boundary. */ namespace Singular.Helpers {
 public static class Group{public static bool MeIsTank;}
 public static class Unit{public static bool IsDungeonCombatBotTargetingRestricted;public static bool IsEligibleDungeonCombatTarget(this WoWUnit u)=>u!=null&&u.IsValid&&u.IsAlive&&u.Eligible;}
 public static class DungeonEngagementPolicy{public static float GetFallbackRange(bool restricted)=>30;}
 public static class Common{public static Composite CreateWaitForLagDuration()=>new WaitContinue(2,_=>SwitchCases.Acknowledge||ReferenceEquals(SwitchCases.Player.CurrentTarget,SwitchCases.Candidate),new TreeSharp.Action(_=>RunStatus.Success));}
}
/* Controlled diagnostic boundary. */ namespace Singular {public static class Logger{public static void WriteDebug(string text){var callback=SwitchCases.OnLog;SwitchCases.OnLog=null;callback?.Invoke();}public static void Write(Color color,string text)=>WriteDebug(text);}}
/* Controlled constant leaves. */ namespace CommonBehaviors.Actions {public class ActionAlwaysSucceed:TreeSharp.Action{public ActionAlwaysSucceed():base(_=>RunStatus.Success){}}public class ActionAlwaysFail:TreeSharp.Action{public ActionAlwaysFail():base(_=>RunStatus.Failure){}}}
""";
}
