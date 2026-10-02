using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Complete tracked immediate-use/admission owners. Only actor observations,
// inventory lookup, clock, logging and the native item-use leaf are controlled.
internal static class RestSubmissionRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "CopilotBuddy.csproj"))) root = root.Parent;
        if (root == null) throw new InvalidOperationException("Tracked source required.");
        var owner = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName, "Styx/Logic/Common/Rest.cs")))
            .GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(x => x.Identifier.ValueText == "Rest");
        var methods = owner.Members.OfType<MethodDeclarationSyntax>()
            .Where(x => x.Identifier.ValueText is "UseImmediate" or "CanUseConsumables" or "GetAdmissionDenial"
                or "GetContinuationDenial" or "GetAdmissionDenialCore" or "ReportAdmissionDenial").ToArray();
        if (!methods.Any(x => x.Identifier.ValueText == "GetContinuationDenial"))
            throw new InvalidOperationException("Causal R1 red: an explicit active-rest continuation safety owner is required.");
        string source = Prefix + "\npublic static class CoreRest {\n" +
            "private static Timer _drinkTimer=new(),_feedTimer=new();private static long _nextFoodAdmissionDiagnostic,_nextDrinkAdmissionDiagnostic;public static bool NoFood,NoDrink;\n" +
            "public static void Reset(){_drinkTimer=new();_feedTimer=new();NoFood=NoDrink=false;_nextFoodAdmissionDiagnostic=_nextDrinkAdmissionDiagnostic=0;}\n" +
            "public static Timer Timer(bool drink)=>drink?_drinkTimer:_feedTimer;\n" +
            "public static bool Invoke(bool drink)=>UseImmediate(drink);\n" +
            string.Join("\n", methods.Select(x => x.ToString())) + "}\n" + Cases;
        var trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? throw new InvalidOperationException("References missing.");
        var compilation = CSharpCompilation.Create("PostMergeRest_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(source) },
            trusted.Split(Path.PathSeparator).Distinct(StringComparer.OrdinalIgnoreCase).Select(x => MetadataReference.CreateFromFile(x)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        if (!result.Success) throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error)));
        try { Assembly.Load(output.ToArray()).GetType("RestCases", true)!.GetMethod("Run")!.Invoke(null, null); }
        catch (TargetInvocationException error) when (error.InnerException != null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }

    private const string Prefix = """
#nullable enable
using System;using System.Collections.Generic;using System.Reflection;
public sealed class Timer{public bool IsFinished=true;public int Resets;public void Reset(){IsFinished=false;Resets++;}}
public sealed class Map{public bool IsInstance,IsBattleground;}
public sealed class LocalPlayer{public Map CurrentMap=new();public ulong Guid=1;public uint MapId=1,BaseAddress=100;public bool IsValid=true,IsAlive=true,IsGhost,Combat,PetInCombat,Mounted,IsFlying,IsOnTransport,IsMoving,IsCasting,IsChanneling;public bool MovementKnown=true;public uint ObservedMovementFlags;public ulong ObservedTransport;public bool TryGetMovementState(out uint flags,out ulong transport){flags=ObservedMovementFlags;transport=ObservedTransport;return MovementKnown;}}
/* Generated external boundary; the initializer itself is global. */ namespace Styx.WoWInternals.World { public static class WorldQueryObservation { public readonly record struct GroundUnitState(uint Flags,bool Mounted,bool OnTaxi); public static GroundUnitState ReadGroundUnitState(global::LocalPlayer player){global::World.GroundQueries++;if(!global::World.GroundKnown)throw new Styx.Helpers.ObservationUnavailableException("ground-unit-state","controlled unavailable");return new(global::World.StrictUnitFlags,global::World.StrictMounted,global::World.StrictTaxi);}}}
/* Generated external boundary. */ namespace Styx.Helpers { public sealed class ObservationUnavailableException:stringException { public ObservationUnavailableException(string owner,string reason):base(owner+":"+reason){} } public class stringException:System.Exception { public stringException(string value):base(value){} } }
/* controlled complete-roster boundary */ namespace Styx.Logic {public static class GroupObservation{public static bool TryGetMembers(LocalPlayer player,out List<LocalPlayer> members,out string reason,bool allowQuery=true){members=World.Group;reason="fixture-roster";return World.GroupKnown;}}}
public static class ObjectManager{public static LocalPlayer? Me;public static object? Wow;}
public static class LiquidEnvironment{public static bool IsPlayerInLiquid(LocalPlayer p,bool allowQuery=true)=>World.Wet;}
public sealed class WoWItem{public string Name="current consumable";public bool Use(){World.Uses++;World.AfterUse?.Invoke();return World.AcceptUse;}}
/* controlled worker epoch */ namespace Styx.Logic.BehaviorTree {public static class TreeRoot{public static object RunIdentity=new();}}
/* shared adapter is tested by RecoveryActionAdapterRegressionTests */ namespace Styx.Logic.Combat {public static class RecoveryActions {public static bool TryUseRestConsumable(WoWItem item,bool health,bool mana,string owner,Func<bool>? admission=null)=>admission?.Invoke()!=false&&item.Use();}}
public static class Consumable{
 public sealed class SelectionObservation{public WoWItem? Item;public bool IsComplete;public string Reason="item-info-unavailable",Details="controlled inventory";}
 public static SelectionObservation ObserveBestDrink(bool specialty)=>Observe(true);
 public static SelectionObservation ObserveBestFood(bool specialty)=>Observe(false);
 public static SelectionObservation ObserveNamedRestItem(bool drinking,string name)=>Observe(drinking);
 static SelectionObservation Observe(bool drinking){World.Lookups++;World.AfterLookup?.Invoke();return new(){Item=(drinking?World.HasDrink:World.HasFood)&&!World.UnknownInventory?new WoWItem():null,IsComplete=!World.UnknownInventory};}}
public static class Logging{public static void Write(string text,params object[] args){World.AfterLog?.Invoke();}public static void WriteDebug(string text,params object[] args){World.DebugLogs++;}}
public static class World{
 public static bool Wet,HasFood,HasDrink,AcceptUse,UnknownInventory,GroupKnown,GroundKnown,StrictMounted,StrictTaxi;public static uint StrictUnitFlags;public static List<LocalPlayer> Group=new();public static int Uses,Lookups,DebugLogs,GroundQueries;public static Action? AfterLookup,AfterLog,AfterUse;
 public static void Reset(){ObjectManager.Me=new();ObjectManager.Wow=new();Wet=UnknownInventory=StrictMounted=StrictTaxi=false;StrictUnitFlags=0;GroundKnown=GroupKnown=HasFood=HasDrink=AcceptUse=true;Group=new(){ObjectManager.Me};Uses=Lookups=DebugLogs=GroundQueries=0;AfterLookup=AfterLog=AfterUse=null;CoreRest.Reset();}}
""";
    private const string Cases = """
public static class RestCases{
 static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
 public static void Run(){int passed=0;var errors=new List<string>();
 void Case(string name,Action test){World.Reset();try{test();passed++;Console.WriteLine("PASS rest submission: "+name);}catch(Exception e){errors.Add(name+": "+e.Message);Console.Error.WriteLine("FAIL rest submission: "+errors[^1]);}}
 foreach(bool drink in new[]{false,true}){
  bool d=drink;string kind=d?"drink/":"food/";
  Case(kind+"accepted native request has a receipt",()=>Check(CoreRest.Invoke(d)&&World.Uses==1,"successful submission was indistinguishable from rejection"));
  Case(kind+"declined request has no receipt",()=>{World.AcceptUse=false;Check(!CoreRest.Invoke(d)&&World.Uses==1,"failed use claimed consumption");});
  Case(kind+"current inventory clears a stale absence flag",()=>{CoreRest.NoFood=CoreRest.NoDrink=true;CoreRest.Invoke(d);Check(!(d?CoreRest.NoDrink:CoreRest.NoFood),"stale absence survived current consumable discovery");});
  Case(kind+"empty inventory retains bounded legacy absence",()=>{World.HasFood=World.HasDrink=false;Check(!CoreRest.Invoke(d)&&World.Uses==0&&(d?CoreRest.NoDrink:CoreRest.NoFood)&&CoreRest.Timer(d).Resets==1,"missing item admission changed");});
  Case(kind+"unknown item metadata is not absent inventory",()=>{World.UnknownInventory=true;Check(!CoreRest.Invoke(d)&&World.Uses==0&&!(d?CoreRest.NoDrink:CoreRest.NoFood),"unresolved inventory was reported as missing food or water");});
  Case(kind+"unknown inventory clears stale absence",()=>{World.UnknownInventory=true;CoreRest.NoFood=CoreRest.NoDrink=true;Check(!CoreRest.Invoke(d)&&!(d?CoreRest.NoDrink:CoreRest.NoFood),"old negative survived a new unknown observation");});
  Case(kind+"known safe instance between pulls",()=>{ObjectManager.Me!.CurrentMap.IsInstance=true;World.Group.Add(new());Check(CoreRest.Invoke(d)&&World.Uses==1,"safe between-pull rest prohibited");});
  foreach(string unsafeState in new[]{"unknown","combat","pet-combat","moving","dead","battleground","flying"}){string state=unsafeState;Case(kind+"instance denied/"+state,()=>{ObjectManager.Me!.CurrentMap.IsInstance=true;var member=new LocalPlayer();World.Group.Add(member);switch(state){case "unknown":World.GroupKnown=false;break;case "combat":member.Combat=true;break;case "pet-combat":member.PetInCombat=true;break;case "moving":member.IsMoving=true;break;case "dead":member.IsAlive=false;break;case "battleground":ObjectManager.Me.CurrentMap.IsBattleground=true;break;case "flying":ObjectManager.Me.IsFlying=true;ObjectManager.Me.ObservedMovementFlags|=0x02000000;break;}Check(!CoreRest.Invoke(d)&&World.Uses==0,"unsafe instance rest admitted");});}
  Case(kind+"hydration after an empty observation can recover",()=>{World.HasFood=World.HasDrink=false;CoreRest.Invoke(d);CoreRest.Timer(d).IsFinished=true;World.HasFood=World.HasDrink=true;Check(CoreRest.Invoke(d)&&!(d?CoreRest.NoDrink:CoreRest.NoFood),"empty observation poisoned the next attempt");});
  foreach(string state in new[]{"moving","mounted","combat","dead","ghost","transport","casting","channeling","wet","missing-player"}){
   string s=state;Case(kind+"denied/"+s,()=>{var p=ObjectManager.Me!;switch(s){case "moving":p.IsMoving=true;p.ObservedMovementFlags|=1;break;case "mounted":p.Mounted=true;World.StrictMounted=true;break;case "combat":p.Combat=true;World.StrictUnitFlags|=0x80000;break;case "dead":p.IsAlive=false;break;case "ghost":p.IsGhost=true;break;case "transport":p.IsOnTransport=true;p.ObservedTransport=1;break;case "casting":p.IsCasting=true;break;case "channeling":p.IsChanneling=true;break;case "wet":World.Wet=true;break;case "missing-player":ObjectManager.Me=null;break;}Check(!CoreRest.Invoke(d)&&World.Uses==0&&CoreRest.Timer(d).Resets==0&&!(d?CoreRest.NoDrink:CoreRest.NoFood),"ineligible actor spent retry budget, used an item, or claimed absence");});}
  Case(kind+"repeated denial diagnostics are bounded",()=>{ObjectManager.Me!.Combat=true;for(int i=0;i<50;i++)CoreRest.Invoke(d);Check(World.DebugLogs==1,"same denied pulse spammed diagnostics");});
  Case(kind+"throttle prevents duplicate request",()=>{CoreRest.Timer(d).IsFinished=false;Check(!CoreRest.Invoke(d)&&World.Uses==0&&World.Lookups==0,"throttle was bypassed");});
  foreach(string changed in new[]{"guid","map","run","memory-missing"}){string change=changed;Case(kind+"replacement context/"+change,()=>{World.AfterLookup=()=>{if(change=="guid")ObjectManager.Me!.Guid++;else if(change=="map")ObjectManager.Me!.MapId++;else if(change=="run")Styx.Logic.BehaviorTree.TreeRoot.RunIdentity=new();else ObjectManager.Wow=null;};Check(!CoreRest.Invoke(d)&&World.Uses==0,"old admission survived "+change);});}
  Case(kind+"replacement during lookup has no use",()=>{World.AfterLookup=()=>ObjectManager.Me=new();Check(!CoreRest.Invoke(d)&&World.Uses==0,"lookup handed use to a replacement actor");});
  Case(kind+"replacement during log has no use",()=>{World.AfterLog=()=>ObjectManager.Me=new();Check(!CoreRest.Invoke(d)&&World.Uses==0,"logging handed use to a replacement actor");});
  Case(kind+"memory replacement during use revokes receipt",()=>{World.AfterUse=()=>ObjectManager.Wow=new();Check(!CoreRest.Invoke(d)&&World.Uses==1,"receipt survived a world replacement");});
 }
 Case("active recovery continuation ignores casting gate only",()=>{var p=ObjectManager.Me!;p.IsCasting=p.IsChanneling=true;Check(CoreRest.GetContinuationDenial(p)==null,"valid active recovery was rejected only because it is casting/channeling");});
 foreach(string state in new[]{"combat","pet-combat","moving","mounted","flying","transport","dead","ghost","wet","instance-unknown","instance-combat","instance-moving","battleground"}){string s=state;Case("active recovery continuation denied/"+s,()=>{var p=ObjectManager.Me!;p.IsCasting=p.IsChanneling=true;switch(s){case "combat":p.Combat=true;World.StrictUnitFlags|=0x80000;break;case "pet-combat":p.PetInCombat=true;World.StrictUnitFlags|=0x800;break;case "moving":p.IsMoving=true;p.ObservedMovementFlags|=1;break;case "mounted":p.Mounted=true;World.StrictMounted=true;break;case "flying":p.IsFlying=true;p.ObservedMovementFlags|=0x02000000;break;case "transport":p.IsOnTransport=true;p.ObservedTransport=1;break;case "dead":p.IsAlive=false;break;case "ghost":p.IsGhost=true;break;case "wet":World.Wet=true;break;case "instance-unknown":p.CurrentMap.IsInstance=true;World.GroupKnown=false;break;case "instance-combat":p.CurrentMap.IsInstance=true;World.Group.Add(new LocalPlayer{Combat=true});break;case "instance-moving":p.CurrentMap.IsInstance=true;World.Group.Add(new LocalPlayer{IsMoving=true});break;case "battleground":p.CurrentMap.IsBattleground=true;break;}Check(CoreRest.GetContinuationDenial(p)!=null,"unsafe active recovery continuation was admitted: "+s);});}
 Case("strict ground UNKNOWN cannot become safe rest",()=>{var p=ObjectManager.Me!;p.Mounted=p.IsFlying=p.IsOnTransport=p.IsMoving=false;World.GroundKnown=false;Check(CoreRest.GetAdmissionDenial(p)!=null,"incomplete mount/form/taxi observation became safe rest");});
 Case("strict movement UNKNOWN cannot become safe rest",()=>{var p=ObjectManager.Me!;p.Mounted=p.IsFlying=p.IsOnTransport=p.IsMoving=false;p.MovementKnown=false;Check(CoreRest.GetAdmissionDenial(p)!=null,"incomplete movement/transport observation became safe rest");});
 Case("strict mount overrides legacy false",()=>{var p=ObjectManager.Me!;p.Mounted=false;World.StrictMounted=true;Check(CoreRest.GetAdmissionDenial(p)!=null,"strict mounted observation was ignored");});
 Case("strict taxi overrides legacy false",()=>{var p=ObjectManager.Me!;p.IsOnTransport=false;World.StrictTaxi=true;Check(CoreRest.GetAdmissionDenial(p)!=null,"strict taxi observation was ignored");});
 Case("strict flying flag overrides legacy false",()=>{var p=ObjectManager.Me!;p.IsFlying=false;p.ObservedMovementFlags=0x02000000u;Check(CoreRest.GetAdmissionDenial(p)!=null,"strict flying movement flag was ignored");});
 Case("strict transport GUID overrides legacy false",()=>{var p=ObjectManager.Me!;p.IsOnTransport=false;p.ObservedTransport=9;Check(CoreRest.GetAdmissionDenial(p)!=null,"strict transport identity was ignored");});
 Case("strict movement flags override legacy stationary",()=>{var p=ObjectManager.Me!;p.IsMoving=false;p.ObservedMovementFlags=1;Check(CoreRest.GetAdmissionDenial(p)!=null,"strict motion flag was ignored");});
 Case("strict actor combat overrides legacy false",()=>{var p=ObjectManager.Me!;p.Combat=false;World.StrictUnitFlags=0x80000;Check(CoreRest.GetAdmissionDenial(p)!=null,"strict actor combat flag was ignored");});
 Case("strict pet combat overrides legacy false",()=>{var p=ObjectManager.Me!;p.PetInCombat=false;World.StrictUnitFlags=0x800;Check(CoreRest.GetAdmissionDenial(p)!=null,"strict pet combat flag was ignored");});
 Console.WriteLine($"Rest submission scenarios: {passed}/{passed+errors.Count}; complete tracked owners; controlled item dispatch; no server consumption acknowledgement.");
 if(errors.Count!=0)throw new InvalidOperationException(string.Join("; ",errors));
 }
}
""";
}
