using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Actual mount/flight decision entry points; only observed actors, routes and
// selected spell metadata are controlled. Estimates never acknowledge movement.
internal static class MountTimeCostRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "CopilotBuddy.csproj"))) root = root.Parent;
        if (root == null) throw new InvalidOperationException("Tracked checkout required.");
        string Methods(string relative, string owner, params string[] names)
        {
            var type = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName, relative)))
                .GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == owner);
            return string.Join("\n", type.Members.OfType<MethodDeclarationSyntax>()
                .Where(m => names.Contains(m.Identifier.ValueText) && (m.Identifier.ValueText != "MountUp"
                    || m.ParameterList.Parameters.Count == 1 && m.ParameterList.Parameters[0].Type?.ToString() == "LocationRetriever"))
                .Select(m => m.ToString()));
        }
        string source = Boundary + "\nnamespace Styx.Logic { public static class Mount { private static LocalPlayer Me=>StyxWoW.Me; private static float MountDistance=>CharacterSettings.Instance.MountDistance; "
            + "public delegate WoWPoint LocationRetriever();public delegate bool CanMountDelegate();private static LocationRetriever _currentDestinationRetriever;public static bool CanMount()=>true;private static bool MountUp(CanMountDelegate check){if(!check())return false;global::World.MountCalls++;return true;}"
            + Methods("Styx/Logic/Mount.cs", "Mount", "ShouldMount", "StateMount", "MountUp") + "}}\n"
            + "namespace Styx.Logic.Pathing { public static class Flightor {public static bool CanFly=>global::World.Flight;private static bool HasSeaLegs(LocalPlayer p)=>false;private static float FlySpeedMultiplier=>global::World.RidingFlight;public static bool Walk(WoWPoint p)=>ShouldWalk(p);public static class MountHelper{public static bool Mounted=>global::World.Actor.Mounted;public static WoWSpell FlyingMount=>global::World.Spell;}"
            + Methods("Styx/Logic/Pathing/Flightor.cs", "Flightor", "ShouldWalk", "PreferFlightForGroundInteraction", "IsFlightTravelCheaper") + "}}\n" + Cases;
        var trees = new System.Collections.Generic.List<SyntaxTree> { CSharpSyntaxTree.ParseText(source) };
        string helper = Path.Combine(root.FullName, "Styx/Logic/Pathing/TravelTimeEstimator.cs");
        if (File.Exists(helper)) trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(helper)));
        string trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? throw new InvalidOperationException("Runtime references required");
        var compilation = CSharpCompilation.Create("MountCost_" + Guid.NewGuid().ToString("N"), trees,
            trusted.Split(Path.PathSeparator).Distinct().Select(p => MetadataReference.CreateFromFile(p)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var bytes = new MemoryStream(); var built = compilation.Emit(bytes);
        if (!built.Success) throw new InvalidOperationException(string.Join("; ", built.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var assembly = Assembly.Load(bytes.ToArray());
        try { assembly.GetType("CostCases", true)!.GetMethod("Run")!.Invoke(null, null); }
        catch (TargetInvocationException error) when (error.InnerException != null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private const string Boundary = """
#nullable disable
using System;using System.Linq;using System.Collections.Generic;
using Styx;using Styx.Helpers;using Styx.Logic;using Styx.Logic.Combat;using Styx.Logic.Pathing;using Styx.Logic.POI;
using Styx.WoWInternals;using Styx.WoWInternals.WoWObjects;using Styx.WoWInternals.World;
public static class World{
 public static LocalPlayer Actor;public static WoWSpell Spell;public static bool Flight;public static float RidingFlight=3.8f;
 public static WoWPoint[] Route;public static int Queries,MountCalls;public static Action During;public static Exception Error;
 public static void Reset(){Actor=new();Spell=new(){Id=1,Name="Selected",CastTime=3000,SpellEffects=new[]{new SpellEffect{AuraType=WoWApplyAuraType.ModIncreaseMountedSpeed,BasePoints=99}}};Flight=false;RidingFlight=3.8f;Route=null;Queries=MountCalls=0;During=null;Error=null;ObjectManager.Wow=new();ObjectManager.Executor=new();BotPoi.Current=new();BotPoi.CurrentWorkGeneration++;Styx.Logic.BehaviorTree.TreeRoot.RunIdentity=new();Styx.Logic.Profiles.ProfileManager.CurrentProfileSnapshot=new();Navigator.NavigationProvider=new();CharacterSettings.Instance=new();LevelbotSettings.Instance=new();Targeting.Instance=new();}
 public static void Boundary(){var call=During;During=null;call?.Invoke();if(Error!=null)throw Error;}
}
/* Controlled external boundary. */ namespace Styx{public static class StyxWoW{public static LocalPlayer Me=>global::World.Actor;}public sealed class InvalidProcessException:Exception{}public sealed class InvalidExecutorException:Exception{}}
/* Controlled external boundary. */ namespace Styx.WoWInternals.WoWObjects{
 public class WoWObject{public ulong Guid=2;public uint BaseAddress=8192;public bool IsValid=true;public float InteractRange=4;}
 public class WoWGameObject:WoWObject{}
 public class WoWUnit:WoWObject{public float Distance;}
 public class LocalPlayer:WoWObject{public LocalPlayer(){Guid=1;BaseAddress=4096;}public uint MapId=530;public bool Mounted,IsFlying,IsSwimming,Combat,IsInInstance,OnTaxi,IsOnTransport;public bool IsAlive=true,IsGhost;public WoWPoint Location=new(10,10,10);public MoveInfo MovementInfo=new();}
 public sealed class MoveInfo{public float RunSpeed=7;public bool IsFlying;}
}
/* Controlled external boundary. */ namespace Styx.WoWInternals{public static class ObjectManager{public static object Wow=new(),Executor=new();public static LocalPlayer Me=>global::World.Actor;}}
/* Controlled external boundary. */ namespace Styx.Helpers{
 public sealed class ObservationUnavailableException:Exception{public ObservationUnavailableException(string a,string b):base(a+":"+b){}}
 public sealed class CharacterSettings{public static CharacterSettings Instance=new();public bool UseMount=true,FindMountAutomatically;public float MountDistance=30;public string MountName="Selected",FlyingMountName="Selected";}
 public sealed class LevelbotSettings{public static LevelbotSettings Instance=new();public string MountName=>CharacterSettings.Instance.MountName;public bool UseMount=>CharacterSettings.Instance.UseMount;}
 public static class Logging{public static void WriteDiagnostic(string t,params object[] args){}public static void WriteDebug(string t,params object[] args){}}
}
/* Controlled external boundary. */ namespace Styx.Logic{
 public static class Battlegrounds{public static bool IsInsideBattleground;}
 public sealed class Targeting{public static Targeting Instance=new();public WoWUnit FirstUnit;public static double PullDistance=5;}
 public static class MountHelper{public sealed class MountWrapper{public WoWSpell CreatureSpell=>global::World.Spell;public int CreatureSpellId=>global::World.Spell?.Id??0;public string Name=>global::World.Spell?.Name;}public static List<MountWrapper> Mounts=>global::World.Spell==null?new():new(){new()};public static List<MountWrapper> GroundMounts=>Mounts;}
}
/* Controlled external boundary. */ namespace Styx.Logic.Combat{
 public enum WoWApplyAuraType{ModIncreaseMountedSpeed=32,MountedFlight=207}
 public enum WoWSpellEffectType{ApplyAura=6}
 public sealed class SpellEffect{public WoWApplyAuraType AuraType;public WoWSpellEffectType EffectType=WoWSpellEffectType.ApplyAura;public int BasePoints,DieSides=1;public float RealPointsPerLevel;}
 public sealed class WoWSpell{public int Id;public string Name;public uint CastTime;public SpellEffect[] SpellEffects;}
 public static class RecoveryActions{public static void RethrowControlFlow(Exception e){if(e is OperationCanceledException or System.Threading.ThreadInterruptedException or Styx.InvalidProcessException or Styx.InvalidExecutorException)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e).Throw();}}
}
/* Controlled external boundary. */ namespace Styx.Logic.BehaviorTree{public static class TreeRoot{public static object RunIdentity=new(),Current=new();}}
/* Controlled external boundary. */ namespace Styx.Logic.Profiles{public static class ProfileManager{public static object CurrentProfileSnapshot=new();}}
/* Controlled external boundary. */ namespace Styx.Logic.POI{public enum PoiType{None,Kill,Loot,Skin,Harvest,QuestPickUp,QuestTurnIn,Hotspot}public sealed class BotPoi{public static BotPoi Current=new();public static long CurrentWorkGeneration;public PoiType Type;public WoWObject AsObject;}}
/* Controlled external boundary. */ namespace Styx.Logic.Pathing{
 public readonly record struct WoWPoint(float X,float Y,float Z){public static WoWPoint Empty=>default;public float Distance(WoWPoint p)=>(float)Math.Sqrt(DistanceSqr(p));public float DistanceSqr(WoWPoint p)=>(X-p.X)*(X-p.X)+(Y-p.Y)*(Y-p.Y)+(Z-p.Z)*(Z-p.Z);}
 public static class Navigator{public static object NavigationProvider=new();public static bool IsInNoFlyZone,IsRidingElevator;public static WoWPoint[] GeneratePath(WoWPoint a,WoWPoint b){global::World.Queries++;global::World.Boundary();return global::World.Route??new[]{a,b};}public static bool CanNavigateWithin(WoWPoint a,WoWPoint b,float p)=>true;}
}
/* Controlled external boundary. */ namespace Styx.WoWInternals.World{public static class WorldQueryObservation{public static Func<bool> CaptureLocalOwner(LocalPlayer p){var memory=ObjectManager.Wow;var exec=ObjectManager.Executor;var run=Styx.Logic.BehaviorTree.TreeRoot.RunIdentity;var guid=p.Guid;var address=p.BaseAddress;var map=p.MapId;return()=>ReferenceEquals(StyxWoW.Me,p)&&p.Guid==guid&&p.BaseAddress==address&&p.MapId==map&&p.IsValid&&p.IsAlive&&ReferenceEquals(ObjectManager.Wow,memory)&&ReferenceEquals(ObjectManager.Executor,exec)&&ReferenceEquals(Styx.Logic.BehaviorTree.TreeRoot.RunIdentity,run);}}}
""";
    private const string Cases = """
public static class CostCases{
 private sealed class Failure(string message):Exception(message){}
 private static void Check(bool value,string why){if(!value)throw new Failure(why);}
 private static WoWPoint At(float d)=>new(10+d,10,10);
 public static void Run(){int passed=0,total=0,failed=0,errors=0;void Case(string name,Action body){global::World.Reset();total++;try{body();passed++;Console.WriteLine("PASS mount time: "+name);}catch(Failure e){failed++;Console.Error.WriteLine("FAIL mount time: "+name+": "+e.Message);}catch(Exception e){errors++;Console.Error.WriteLine("ERROR mount time: "+name+": "+e);}}
 Case("nearby instance targets do not force mount",()=>{global::World.Actor.IsInInstance=true;Check(!Mount.ShouldMount(At(12)),"instance bypassed all travel costs");});
 Case("45 yard hop stays on foot after setup",()=>Check(!Mount.ShouldMount(At(45)),"cast/setup exceeds saved ground travel"));
 Case("long ground leg amortizes setup",()=>Check(Mount.ShouldMount(At(200)),"long route needlessly walks"));
 Case("fast on-foot movement beats selected slow mount",()=>{global::World.Actor.MovementInfo.RunSpeed=14;global::World.Spell.SpellEffects[0].BasePoints=59;Check(!Mount.ShouldMount(At(120)),"riding estimate multiplied the foot buff" );});
 Case("missing selected spell does not justify mounting",()=>{global::World.Spell=null;Check(!Mount.ShouldMount(At(200)),"unknown mount supplied free speed" );});
 Case("zero foot speed is unknown",()=>{global::World.Actor.MovementInfo.RunSpeed=0;Check(!Mount.ShouldMount(At(200)),"invalid speed became infinite walking cost" );});
 Case("partial ground mesh has no travel-time authority",()=>{global::World.Route=new[]{global::World.Actor.Location,At(50)};Check(!Mount.ShouldMount(At(200)),"partial route justified mounting" );});
 Case("winding complete mesh can justify mounting",()=>{global::World.Route=new[]{global::World.Actor.Location,new WoWPoint(10,110,10),new WoWPoint(55,110,10),At(45)};Check(Mount.ShouldMount(At(45)),"only straight separation was counted" );});
 Case("unknown speed aura does not use riding rank",()=>{global::World.Spell.SpellEffects=Array.Empty<SpellEffect>();Check(!Mount.ShouldMount(At(200)),"missing speed metadata was invented" );});
 Case("disabled mounting stays disabled",()=>{CharacterSettings.Instance.UseMount=false;Check(!Mount.ShouldMount(At(200)),"mount preference ignored" );});
 Case("same-cost tie remains walking",()=>{global::World.Actor.MovementInfo.RunSpeed=14;Check(!Mount.ShouldMount(At(500)),"tie paid an unnecessary cast" );});
 Case("path result cannot survive replaced actor",()=>{global::World.During=()=>global::World.Actor=new();Check(!Mount.ShouldMount(At(200)),"route transferred to successor actor" );});
 Case("path result cannot survive replaced work",()=>{global::World.During=()=>BotPoi.CurrentWorkGeneration++;Check(!Mount.ShouldMount(At(200)),"route transferred to successor work" );});
 Case("identical repeated decisions reuse only geometry",()=>{Check(Mount.ShouldMount(At(200))&&Mount.ShouldMount(At(200))&&global::World.Queries==1,"repeated decision re-queries mesh or loses valid travel" );});
 Case("different destination cannot reuse old route length",()=>{Check(Mount.ShouldMount(At(200)),"control");Check(!Mount.ShouldMount(At(45))&&global::World.Queries==2,"new destination inherited a long route" );});
 Case("changed current speed invalidates favorable estimate",()=>{Check(Mount.ShouldMount(At(200)),"control");global::World.Actor.MovementInfo.RunSpeed=21;Check(!Mount.ShouldMount(At(200)),"cached result ignored faster walking" );});
 Case("mount selection changes recompute speed",()=>{Check(Mount.ShouldMount(At(100)),"control");global::World.Spell=new(){Id=2,Name="Selected",CastTime=9000,SpellEffects=new[]{new SpellEffect{AuraType=WoWApplyAuraType.ModIncreaseMountedSpeed,BasePoints=59}}};Check(!Mount.ShouldMount(At(100)),"new slow mount inherited prior estimate" );});
 foreach(Exception signal in new Exception[]{new OperationCanceledException("cancel"),new Styx.InvalidProcessException(),new Styx.InvalidExecutorException()}){var error=signal;Case("real control loss is not ordinary walking/"+signal.GetType().Name,()=>{global::World.Error=error;Exception caught=null;try{Mount.ShouldMount(At(200));}catch(Exception e){caught=e;}Check(ReferenceEquals(caught,error),"control signal swallowed");});}
 Case("short kill hop includes full flight transition cost",()=>{global::World.Flight=true;global::World.Spell.SpellEffects=new[]{new SpellEffect{AuraType=WoWApplyAuraType.MountedFlight,BasePoints=149}};BotPoi.Current.Type=PoiType.Kill;Check(Flightor.Walk(At(60)),"mount/climb/landing overhead omitted between nearby mobs");});
 Case("long flight remains available",()=>{global::World.Flight=true;global::World.Spell.SpellEffects=new[]{new SpellEffect{AuraType=WoWApplyAuraType.MountedFlight,BasePoints=149}};BotPoi.Current.Type=PoiType.Kill;Check(!Flightor.Walk(At(400)),"long flight lost");});
 Case("already airborne is not redirected to walking by cost",()=>{global::World.Flight=true;global::World.Actor.MovementInfo.IsFlying=true;Check(!Flightor.Walk(At(8))&&global::World.Queries==0,"airborne route disturbed by remount economics");});
 Case("StateMount cannot bypass short-trip comparison",()=>{Mount.StateMount(()=>At(45));Check(global::World.MountCalls==0,"ordinary StateMount bypassed travel economics");});
 Case("direct destination MountUp cannot bypass short-trip comparison",()=>{Mount.MountUp(()=>At(45));Check(global::World.MountCalls==0,"destination mount wrapper bypassed travel economics");});
 Case("long StateMount still reaches existing cast owner",()=>{Mount.StateMount(()=>At(200));Check(global::World.MountCalls==1,"worthwhile normal mount was withheld");});
 Case("destination replacement during cost lookup cannot mount old work",()=>{var destination=At(200);global::World.During=()=>destination=At(45);Mount.MountUp(()=>destination);Check(global::World.MountCalls==0,"changed destination retained a predecessor's mount decision");});
 Case("explicit destination-free mount retains legacy contract",()=>{Mount.MountUp(()=>WoWPoint.Empty);Check(global::World.MountCalls==1,"explicit mount without a route was silently reclassified");});
 Case("ordinary mount compares the actual ground choice when flight name is empty",()=>{global::World.Flight=true;CharacterSettings.Instance.FlyingMountName="";Check(Mount.ShouldMount(At(200)),"ordinary action chooses ground companion but estimator priced an unrelated flight form");});
 Console.WriteLine($"Mount time cost: {passed}/{total}; assertions={failed}; unexpected={errors}; actual decision methods with controlled metadata/mesh/session leaves; no physical travel-time guarantee.");if(failed+errors!=0)throw new InvalidOperationException("mount travel cost regression");
 }}
""";
}
