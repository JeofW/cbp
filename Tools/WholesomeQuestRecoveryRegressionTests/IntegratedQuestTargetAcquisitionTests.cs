using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Real target publication + LevelBot's complete include filter + actual quest
// include callback + actual segment test. Source relation and native hostility /
// aura observations are controlled leaves; no guessed aggro radius is admitted.
internal static class IntegratedQuestTargetAcquisitionTests
{
    internal static void Run()
    {
        var targeting = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(IntegratedRegressionFixture.Root,
            "Styx/Logic/Targeting.cs"))).GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(type => type.Identifier.ValueText == "Targeting");
        string[] methods = { "Pulse", "InvokeFilterDelegate", "Clear", "MarkObservationUnavailable", "InitializeFilters",
            "CreateTargetPriority", "GetScore", "GetObject", "ToWoWUnit" };
        string[] properties = { "ObjectList", "FirstUnit", "TargetList", "MaxTargets", "DisplayTargetingExceptions" };
        string members = string.Join("\n", targeting.Members.Where(member =>
            member is FieldDeclarationSyntax or ConstructorDeclarationSyntax or EventDeclarationSyntax
            || member is ClassDeclarationSyntax nested && nested.Identifier.ValueText == "TargetPriority"
            || member is MethodDeclarationSyntax method && methods.Contains(method.Identifier.ValueText)
            || member is PropertyDeclarationSyntax property && properties.Contains(property.Identifier.ValueText)));
        string level = IntegratedRegressionFixture.Methods("Bots/Grind/LevelBot.cs", "LevelBotIncludeTargetsFilter", "IsTooNearBlackspot");
        string quest = IntegratedRegressionFixture.Methods("Bots/Quest/Objectives/CollectItemObjective.cs", "IncludeTargets");
        var geometry = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(IntegratedRegressionFixture.Root,
            "Styx/Helpers/WoWMathHelper.cs"))).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "IsInPath" && method.ParameterList.Parameters.Count == 3);
        IntegratedRegressionFixture.Run("Quest target acquisition", Prefix + members + TargetLeaves
            + "\npublic static class LevelBot {" + level + "}\npublic static class WoWMathHelper {" + geometry + "}\n"
            + "public sealed class QuestSource {" + quest + QuestLeaves + "}\n" + Boundary + Scenarios,
            "Styx/Helpers/ObservationUnavailableException.cs");
    }

    private const string Prefix = """
#nullable disable
using System;using System.Linq;using System.Collections.Generic;using System.Threading;using Styx.Helpers;using Styx.Logic.Combat;
public delegate void TargetListUpdateFinishedDelegate(List<string> targets);
public delegate void IncludeTargetsFilterDelegate(List<WoWObject> incoming,HashSet<WoWObject> outgoing);
public delegate void RemoveTargetsFilterDelegate(List<WoWObject> units);
public delegate void WeighTargetsDelegate(List<Targeting.TargetPriority> targets);
public class Targeting {
""";
    private const string TargetLeaves = """
 public static Targeting Instance=>Cases.Owner;
 public bool KillBetweenHotspots=true;
 protected virtual List<WoWObject> GetInitialObjectList()=>Cases.Candidates.ToList();
 protected virtual void DefaultRemoveTargetsFilter(List<WoWObject> units){}
 protected virtual void DefaultIncludeTargetsFilter(List<WoWObject> incoming,HashSet<WoWObject> outgoing){}
 protected virtual void DefaultTargetWeight(List<TargetPriority> targets){foreach(var target in targets)target.Score=100-target.Object.Guid;}
}
""";
    private const string QuestLeaves = """
 public bool IsCompleted;
 public bool IsValidMobTarget(WoWUnit unit)=>unit!=null&&unit.Entry==17014;
 public void Attach(Targeting targeting)=>targeting.IncludeTargetsFilter+=IncludeTargets;
""";
    private const string Boundary = """
public readonly record struct WoWPoint(float X,float Y,float Z){
 public float Distance2D(WoWPoint other)=>MathF.Sqrt((X-other.X)*(X-other.X)+(Y-other.Y)*(Y-other.Y));
}
public class WoWObject {
 public ulong Guid;public uint Entry;public WoWPoint Location;
 public string Name=>"unit-"+Entry;public float Distance=>MathF.Sqrt(Location.X*Location.X+Location.Y*Location.Y+Location.Z*Location.Z);
 public WoWUnit ToUnit()=>this as WoWUnit;
}
public enum DifficultyColor {Gray,Green,Yellow,Orange,Red}
public enum WoWUnitReaction {Hostile=2,Neutral=4,Friendly=5}
public class WoWUnit:WoWObject {
 public bool Dead,IsPlayer,IsFlightMaster,IsCritter,TaggedByOther,Combat,Mounted,IsActuallyInCombat;
 public int Level=62;public uint FactionId=14;public ulong CurrentTargetGuid;
 public WoWUnit OwnedByRoot;public DifficultyColor Difficulty=DifficultyColor.Yellow;public bool InLineOfSpellSight=true;
 public WoWUnitReaction MyReaction {get {if(Cases.ReactionUnknown)throw new ObservationUnavailableException("reaction","unknown relevant hostility");return WoWUnitReaction.Hostile;}}
 public float MyAggroRange {get {Cases.RangeReads++;if(Cases.RangeError!=null)throw Cases.RangeError;return 10;}}
}
public sealed class Profile {
 public List<uint> Factions=new();public List<object> AvoidMobs=new();public List<Blackspot> Blackspots=new();
 public int TargetMinLevel=1,TargetMaxLevel=80;
}
public sealed class Blackspot {public WoWPoint Location;public float Radius;}
public sealed class GrindArea {public List<int> MobIDs=new(),Factions=new();public int TargetMinLevel=1,TargetMaxLevel=80;}
public static class ProfileManager {public static Profile CurrentProfile=new();}
public sealed class Areas {public GrindArea CurrentGrindArea=new();}
public static class StyxWoW {public static WoWUnit Me=new();public static Areas AreaManager=new();public static bool IsInGame=true;public static Memory Memory=new();}
public sealed class Memory {public IDisposable AcquireFrame()=>new Frame();private sealed class Frame:IDisposable{public void Dispose(){}}}
public enum PoiType {None,Kill,Buy,Mail,Repair,Sell}
public sealed class BotPoi {public static BotPoi Current=new();public PoiType Type;}
public static class Navigator {public static object NavigationProvider=new MeshNavigator();}
public sealed class MeshNavigator {public bool HasActivePath=true;public List<WoWPoint> CurrentPath=new(){new(0,0,0),new(50,0,0)};}
public static class ObservationFailureDiagnostics {public static void Report(Exception error,string consumer=null){Cases.Diagnostics++;}}
""";
    private const string Scenarios = """
public static class Cases {
 private sealed class Failure(string message):Exception(message){}
 public static Targeting Owner;public static List<WoWObject> Candidates=new();
 public static Exception RangeError;public static bool ReactionUnknown;public static int RangeReads,Diagnostics;
 private static WoWUnit source;
 private static void Reset(){RangeError=null;ReactionUnknown=false;RangeReads=Diagnostics=0;StyxWoW.Me=new();StyxWoW.IsInGame=true;StyxWoW.AreaManager=new();ProfileManager.CurrentProfile=new();Navigator.NavigationProvider=new MeshNavigator();BotPoi.Current=new();
  source=new(){Guid=3,Entry=17014,Location=new(3,0,0)};Candidates=new(){new WoWUnit{Guid=2,Entry=999,Location=new(20,0,0)},source};Owner=new();Owner.IncludeTargetsFilter+=LevelBot.LevelBotIncludeTargetsFilter;new QuestSource().Attach(Owner);}
 private static void Check(bool condition,string message){if(!condition)throw new Failure(message);}
 private static void Acquired(){try{Check(Owner.TargetList.Contains(source),"required source 17014 was omitted");}catch(ObservationUnavailableException e){throw new Failure("optional travel observation poisoned required creature-loot acquisition: "+e.Message);}}
 private static void Unknown(){bool rejected=false;try{_ = Owner.TargetList;}catch(ObservationUnavailableException){rejected=true;}Check(rejected,"required unavailable observation published a usable target list");}
 public static void Run(){int total=0,passed=0,failures=0,errors=0;
  void Case(string name,System.Action test){total++;Reset();try{test();passed++;}catch(Failure e){failures++;Console.Error.WriteLine("FAIL integrated target acquisition: "+name+": "+e.Message);}catch(Exception e){errors++;Console.Error.WriteLine("ERROR integrated target acquisition: "+name+": "+e);}}
  foreach(bool mounted in new[]{false,true})foreach(bool route in new[]{false,true}){
   Case($"quest 10294 survives missing aura 61988 mounted={mounted} route={route}",()=>{StyxWoW.Me.Mounted=mounted;((MeshNavigator)Navigator.NavigationProvider).HasActivePath=route;RangeError=new ObservationUnavailableException("auras","Could not resolve metadata for active aura 61988");Owner.Pulse();Acquired();});
  }
  Case("known route still admits the optional on-path hostile",()=>{Owner.Pulse();Acquired();Check(Owner.TargetList.Any(u=>u.Entry==999),"known optional candidate was lost");});
  Case("zero-length route requires no aura observation",()=>{((MeshNavigator)Navigator.NavigationProvider).HasActivePath=false;Owner.Pulse();Acquired();Check(RangeReads==0,"empty path queried optional aggro modifiers");});
  Case("primary profile target survives another candidate's optional UNKNOWN",()=>{StyxWoW.AreaManager.CurrentGrindArea.MobIDs.Add(17014);RangeError=new ObservationUnavailableException("auras","61988");Owner.Pulse();Acquired();});
  Case("known-empty required source does not invent an optional target",()=>{Candidates.Remove(source);RangeError=new ObservationUnavailableException("auras","61988");Owner.Pulse();try{Check(Owner.TargetList.Count==0,"unknown route was assumed to contain a hostile");}catch(ObservationUnavailableException e){throw new Failure("optional absence erased a completed source scan: "+e.Message);}});
  Case("unknown relevant hostility retains incomplete publication",()=>{ReactionUnknown=true;Owner.Pulse();Unknown();});
  foreach(bool wrapped in new[]{false,true})foreach(bool interruption in new[]{false,true}){
   Case($"optional observation preserves stop wrapped={wrapped} interrupted={interruption}",()=>{Exception signal=interruption?new ThreadInterruptedException("stop"):new OperationCanceledException("stop");RangeError=wrapped?new System.Reflection.TargetInvocationException(signal):signal;Exception observed=null;try{Owner.Pulse();}catch(Exception error){observed=error;}Check(ReferenceEquals(signal,observed),"optional fallback swallowed a stop signal");Unknown();});
  }
  Console.WriteLine($"Integrated quest target acquisition: {passed}/{total}; assertions={failures}; unexpected={errors}; actual target publisher, complete LevelBot filter, quest include callback and segment geometry; controlled source relation and native observations.");
  if(failures+errors!=0)throw new InvalidOperationException("Integrated quest acquisition failures");
 }
}
""";
}
