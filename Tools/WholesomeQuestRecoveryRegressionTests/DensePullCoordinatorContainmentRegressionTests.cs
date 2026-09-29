using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

// Complete coordinator and provider interface, real TreeSharp and WoWPoint.
// Positive third-party providers are spell capability, never world acceptance.
internal static class DensePullCoordinatorContainmentRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        string? root = null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CopilotBuddy.csproj"))) { root = d.FullName; break; }
        if (root == null) throw new InvalidOperationException("Tracked checkout required");
        string temp = Path.Combine(Path.GetTempPath(), "cb-coordinator-containment-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        bool logging = Styx.Helpers.Logging.FileLogging;
        try
        {
            Styx.Helpers.Logging.FileLogging = false;
            foreach (string path in new[] { "Bots/Grind/Levelbot/Actions/Combat/PullIsolationCoordinator.cs", "Styx/Combat/CombatRoutine/IIsolationPullProvider.cs" })
                File.Copy(Path.Combine(root, path), Path.Combine(temp, Path.GetFileName(path)));
            File.WriteAllText(Path.Combine(temp, "Boundary.cs"), Boundary);
            Type type = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(type, new object[] { temp })!;
            foreach (string path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
                type.GetMethod("AddReference", flags)!.Invoke(compiler, new object[] { path });
            var result = (CompilerResults)type.GetMethod("Compile", flags)!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Actual coordinator compile: " + string.Join("; ", errors.Select(e => e.ToString())));
            var assembly = (Assembly)type.GetProperty("CompiledAssembly", flags)!.GetValue(compiler)!;
            try { assembly.GetType("DenseCases", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        finally { Styx.Helpers.Logging.FileLogging = logging; Directory.Delete(temp, true); }
    }
    private const string Boundary = """
#nullable disable
using System;using System.Collections.Generic;using System.Linq;using System.Reflection;using System.Runtime.ExceptionServices;
using TreeSharp;using Styx;using Styx.Logic;using Styx.Logic.Pathing;using Styx.Logic.POI;using Styx.Logic.Combat;using Styx.WoWInternals;using Styx.WoWInternals.WoWObjects;using Styx.Combat.CombatRoutine;using Levelbot.Actions.Combat;
public static class DenseCases {
 sealed class Failure(string why):Exception(why){}
 public static readonly List<string> Effects=new();public static double Range=30;
 public static LocalPlayer Player;public static WoWUnit Target;
 const BindingFlags Hidden=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 static void Check(bool ok,string why){if(!ok)throw new Failure(why);}
 static object Invoke(string method,params object[] arguments){try{return typeof(PullIsolationCoordinator).GetMethod(method,Hidden).Invoke(null,arguments);}catch(TargetInvocationException e)when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}}
 static void Reset(){
  typeof(PullIsolationCoordinator).GetField("_plan",Hidden).SetValue(null,null);Effects.Clear();Range=30;
  Player=new LocalPlayer{Guid=1,Location=new WoWPoint(70,10,10)};Target=new WoWUnit{Guid=2,Entry=200,Location=new WoWPoint(100,10,10)};
  Player.CurrentTarget=Target;StyxWoW.Me=Player;RoutineManager.Current=new Provider();BotPoi.Current=new BotPoi{Type=PoiType.Kill,Guid=2};
  ObjectManager.CachedUnits=new List<WoWUnit>{Target,new WoWUnit{Guid=3,Entry=300,Location=new WoWPoint(105,15,10)}};
 }
 static void SeedPlan(bool awaiting=false){
  Type type=typeof(PullIsolationCoordinator).GetNestedType("PullPlan",Hidden);object plan=Activator.CreateInstance(type,true);
  void Set(string field,object value)=>type.GetField(field,Hidden).SetValue(plan,value);
  Set("TargetGuid",Target.Guid);Set("TargetEntry",Target.Entry);Set("PackOrigin",Target.Location);Set("PullPoint",Player.Location);Set("RetreatAnchor",new WoWPoint(60,10,10));Set("PullRange",30d);
  Set("Provider",RoutineManager.Current);Set("Opener",new Opener());Set("OpenerStarted",true);Set("Retreating",!awaiting);Set("AwaitingEngagement",awaiting);Set("DeadlineUtc",DateTime.UtcNow.AddMinutes(1));
  Target.Location=new WoWPoint(95,10,10);Target.Combat=true;Player.Combat=true;
  typeof(PullIsolationCoordinator).GetField("_plan",Hidden).SetValue(null,plan);
 }
 static RunStatus Tick(Composite root){root.Start(null);try{return root.Tick(null);}finally{root.Stop(null);}}
 static void NoEffects()=>Check(Effects.Count==0,"unvalidated coordinator reached effects: "+string.Join(",",Effects));
 static void Change(string name){switch(name){case "healthy":break;case "actor":StyxWoW.Me=new LocalPlayer{Guid=1,Location=Player.Location,CurrentTarget=Target};break;
  case "actor-guid":Player.Guid=9;break;case "actor-dead":Player.IsAlive=false;break;case "target-dead":Target.IsAlive=false;break;
  case "target-guid":Target.Guid=9;break;case "target-reference":Player.CurrentTarget=new WoWUnit{Guid=2,Entry=200,Location=Target.Location};break;
  case "transport":Player.IsOnTransport=true;break;case "map":Player.CurrentMap.IsDungeon=true;break;case "z":Player.Location=Player.Location.Add(0,0,30);break;
  case "provider":RoutineManager.Current=new Provider();break;default:throw new InvalidOperationException(name);}}
 public static void Run(){
  var cases=new List<(string,System.Action)>();void Add(string label,System.Action test)=>cases.Add((label,()=>{Reset();test();}));
  foreach(double range in new[]{8d,10d,30d,40d}){double r=range;Add("positive foreign provider "+r,()=>{Range=r;Check(Tick(PullIsolationCoordinator.CreatePreCombatBehavior())==RunStatus.Failure,"spell capability became world acceptance");NoEffects();Check(typeof(PullIsolationCoordinator).GetField("_plan",Hidden).GetValue(null)==null,"unvalidated plan published");});}
  foreach(string entry in new[]{"precombat","retreat","approach"})foreach(string change in new[]{"healthy","actor","actor-guid","actor-dead","target-dead","target-guid","target-reference","transport","map","z","provider"}){
   string path=entry,mutation=change;Add("retained-plan/"+path+"/"+mutation,()=>{SeedPlan(path=="precombat");Change(mutation);
    RunStatus result=path=="precombat"?Tick(PullIsolationCoordinator.CreatePreCombatBehavior()):path=="retreat"?Tick(PullIsolationCoordinator.CreateRetreatBehavior()):(RunStatus)Invoke("ContinueApproachAndOpen",null,StyxWoW.Me,Player.CurrentTarget);
    Check(result==RunStatus.Failure,"unvalidated retained plan resumed");NoEffects();});
  }
  foreach(string shape in new[]{"straight","vertical","unknown-hostile-z","unknown-aggro","unknown-record","engaged-nearby","empty-observations","null-observations"}){string form=shape;Add("route-evidence/"+form,()=>{
   var from=Player.Location;var to=from.Add(-10,0,0);IReadOnlyList<PullIsolationObservation> observed=Array.Empty<PullIsolationObservation>();
   if(form=="vertical")to=from.Add(0,0,100);
   if(form=="unknown-hostile-z")observed=new[]{new PullIsolationObservation{Guid=3,IsAttackable=true,Location=new WoWPoint(65,10,float.NaN),AggroRange=3}};
   if(form=="unknown-aggro")observed=new[]{new PullIsolationObservation{Guid=3,IsAttackable=true,Location=to,AggroRange=float.NaN}};
   if(form=="unknown-record")observed=new PullIsolationObservation[]{null};
   if(form=="engaged-nearby")observed=new[]{new PullIsolationObservation{Guid=3,IsAttackable=true,IsEngaged=true,Location=to,AggroRange=30}};
   if(form=="null-observations")observed=null;
   Check(!(bool)Invoke("IsRoutePointSafe",from,to,Target.Guid,observed),"geometry/reachability alone certified physical safety");NoEffects();
  });}
  foreach(bool retained in new[]{false,true}){bool old=retained;Add("reset-without-accepted-movement/"+old,()=>{if(old)SeedPlan();PullIsolationCoordinator.Reset();NoEffects();Check(typeof(PullIsolationCoordinator).GetField("_plan",Hidden).GetValue(null)==null,"reset did not detach unsupported plan");});}
  Add("rejection cannot punish unvalidated target",()=>{SeedPlan();Check((RunStatus)Invoke("RejectCurrentTarget",null,Target,"controlled")==RunStatus.Failure,"unsupported isolation blocked ordinary fallback");NoEffects();});
  Add("coordinator owns immutable disabled acceptance",()=>{var gate=typeof(PullIsolationCoordinator).GetField("DensePullIsolationValidated",Hidden);Check(gate!=null&&gate.IsInitOnly&&Equals(gate.GetValue(null),false),"coordinator delegates world acceptance to a spell provider");});
  int passed=0,assertions=0,unexpected=0;foreach(var item in cases){try{item.Item2();passed++;Console.WriteLine("PASS coordinator containment: "+item.Item1);}catch(Failure e){assertions++;Console.Error.WriteLine("FAIL coordinator containment: "+item.Item1+": "+e.Message);}catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR coordinator containment: "+item.Item1+": "+e);}}
  Console.WriteLine($"Coordinator containment scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; complete tracked coordinator/interface and real TreeSharp; positive provider and controlled routes; no physical-world acceptance.");
  if(assertions+unexpected!=0)throw new InvalidOperationException("Coordinator containment regression");
 }
 public class Provider:IIsolationPullProvider{public double IsolationPullDistance{get{Effects.Add("provider-range");return Range;}}public Composite CreateIsolationPullBehavior(){Effects.Add("provider-factory");return new Opener();}}
 public class Opener:Composite{protected override IEnumerable<RunStatus> Execute(object c){Effects.Add("opener-tick");yield return RunStatus.Success;}public override void Stop(object c){Effects.Add("opener-stop");base.Stop(c);}}
}
/* Controlled world. */ namespace Styx.WoWInternals.WoWObjects{
 public class Map{public bool IsDungeon,IsBattleground,IsArena;}
 public class WoWUnit{public ulong Guid;public uint Entry;public string Name="controlled";public WoWPoint Location;public bool IsValid=true,IsAlive=true,Attackable=true,IsHostile=true,TaggedByOther,Fleeing,IsPlayer,Elite,IsOnTransport,Combat,Aggro,PetAggro,IsTargetingMeOrPet,IsTargetingAnyMinion,TappedByAllThreatLists,IsPet,IsNonCombatPet,IsCritter,IsMoving;public int MaxMana;public float MyAggroRange=3;public bool InLineOfSpellSight=true;public double Distance=>Location.Distance(Styx.StyxWoW.Me.Location);}
 public class LocalPlayer:WoWUnit{public Map CurrentMap=new();public bool IsInParty,IsInRaid;public WoWUnit CurrentTarget;public void ClearTarget(){DenseCases.Effects.Add("clear-target");CurrentTarget=null;}}
}
/* Controlled world. */ namespace Styx{public static class StyxWoW{public static LocalPlayer Me;}}
/* Controlled world. */ namespace Styx.WoWInternals{public static class ObjectManager{public static List<WoWUnit> CachedUnits=new();}public static class WoWMovement{public static void MoveStop()=>DenseCases.Effects.Add("stop");}}
/* Controlled provider. */ namespace Styx.Logic.Combat{public static class RoutineManager{public static object Current;}}
/* Controlled targeting leaves. */ namespace Styx.Logic{public static class Targeting{public static int GetAggroOnMeWithin(WoWPoint p,float r){DenseCases.Effects.Add("aggro-count");return 1;}public static bool IsTooNearBlackspot(object spots,WoWPoint point){DenseCases.Effects.Add("blackspot");return false;}}public static class Blacklist{public static void Add(ulong id,TimeSpan time)=>DenseCases.Effects.Add("blacklist");}}
/* Controlled POI. */ namespace Styx.Logic.POI{public enum PoiType{None,Kill}public class BotPoi{public static BotPoi Current=new();public PoiType Type;public ulong Guid;public static void Clear(string reason){DenseCases.Effects.Add("clear-poi");Current=new();}}}
/* Controlled profile observations. */ namespace Styx.Logic.Profiles{public class Profile{public object Blackspots;}public static class ProfileManager{public static Profile CurrentProfile=new();}}
/* Controlled navigation effects only. */ namespace Styx.Logic.Pathing{public static class Navigator{public static float PathPrecision=5;public static bool CanNavigateFully(WoWPoint a,WoWPoint b){DenseCases.Effects.Add("reachability");return true;}public static MoveResult MoveTo(WoWPoint p){DenseCases.Effects.Add("move");return MoveResult.Moved;}public static void Clear()=>DenseCases.Effects.Add("clear-navigation");}}
/* Controlled diagnostics. */ namespace Styx.Helpers{public static class Logging{public static void Write(string message,params object[] values)=>DenseCases.Effects.Add("log");public static void WriteDebug(string message,params object[] values)=>DenseCases.Effects.Add("log");}}
/* Controlled status. */ namespace Styx.Logic.BehaviorTree{public static class TreeRoot{public static string StatusText{set{DenseCases.Effects.Add("status");}}}}
""";
}
