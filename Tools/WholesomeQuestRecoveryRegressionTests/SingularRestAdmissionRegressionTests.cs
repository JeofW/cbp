using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class SingularRestAdmissionRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "CopilotBuddy.csproj"))) root = root.Parent;
        if (root == null) throw new InvalidOperationException("Tracked source required.");
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName, "runtime-snapshot/Routines/Singular wotlk/Helpers/Rest.cs")));
        var parsed = tree.GetRoot();
        string[] required = { "CanStartConsumable", "ContinueSupportedRest", "CanPrepareCannibalize", "SubmitCannibalize", "ContinueCannibalize", "CannibalizeOwnerCurrent", "ClearCannibalize" };
        var methods = parsed.DescendantNodes().OfType<MethodDeclarationSyntax>().Where(m => required.Contains(m.Identifier.ValueText)).ToArray();
        var lease = parsed.DescendantNodes().OfType<ClassDeclarationSyntax>().SingleOrDefault(c => c.Identifier.ValueText == "CannibalizeLease");
        if (lease == null || required.Any(name => !methods.Any(m => m.Identifier.ValueText == name)))
            throw new InvalidOperationException("Causal R1 red: actual Singular Cannibalize start/continuation lifetime owners are required.");
        var defaultRest = parsed.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.ValueText == "CreateDefaultRestBehaviour").ToString();
        if (!defaultRest.Contains("ContinueCannibalize()") || !defaultRest.Contains("CanPrepareCannibalize()") || !defaultRest.Contains("SubmitCannibalize()"))
            throw new InvalidOperationException("Cannibalize helpers are not wired into the actual default rest tree.");
        string source = Prefix + lease + "\n" + string.Join("\n", methods.Select(m => m.ToString())) + "}\n" + Cases;
        var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create("SingularRest_" + Guid.NewGuid().ToString("N"), new[] { CSharpSyntaxTree.ParseText(source) }, refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var bytes = new MemoryStream();
        var result = compilation.Emit(bytes);
        if (!result.Success) throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        try { Assembly.Load(bytes.ToArray()).GetType("RestCases", true)!.GetMethod("Run")!.Invoke(null, null); }
        catch (TargetInvocationException e) when (e.InnerException != null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
    private const string Prefix = """
using System;using System.Collections.Generic;
public enum WoWPowerType { Mana,Rage } public enum WoWClass { Paladin,Druid,Warrior }
public class SpellView {public string Name="";}
public class Player {public double HealthPercent=50,ManaPercent=20;public WoWPowerType PowerType=WoWPowerType.Mana;public WoWClass Class=WoWClass.Paladin;public bool IsValid=true,IsAlive=true,IsGhost,IsMoving,IsCasting,IsChanneling;public ulong Guid=1;public uint BaseAddress=100,MapId=1;public SpellView CastingSpell;}
public sealed class LocalPlayer:Player {}
public static class StyxWoW {public static LocalPlayer Me=new();}
public static class ObjectManager {public static LocalPlayer Me=>StyxWoW.Me;public static object Wow=new();}
/* Generated external boundary; the initializer itself is global. */ namespace Styx.Logic.BehaviorTree {public static class TreeRoot {public static object RunIdentity=new();}}
public enum RunStatus {Success,Failure,Running}
public static class SpellManager {public static bool CanCast(string name)=>World.CanCast;public static bool Cast(string name){World.Casts++;World.OnCast?.Invoke();return World.CastAccepted;}}
public class SingularSettings {public static SingularSettings Instance=new();public int MinHealth=65,MinMana=30;}
public class CharacterSettings {public static CharacterSettings Instance=new();public int FoodAmount=13,DrinkAmount=13;}
public static class Logger {public static void WriteDebug(string text,params object[] args){World.Diagnostics++;World.OnLog?.Invoke();}}
public static class RecoveryActions {public static bool CanPrepareRestConsumable(bool health,bool mana)=>!World.RecoveryBlocked;}
public static class Consumable {
 public class Observation {public object Item=new();public bool IsComplete=true;public string Reason="known-empty";}
 public static Observation ObserveBestFood(bool specialty)=>Observe();public static Observation ObserveBestDrink(bool specialty)=>Observe();
 static Observation Observe(){World.Lookups++;World.OnLookup?.Invoke();return new(){Item=World.HasItem?new object():null,IsComplete=World.InventoryKnown};}}
/* core safety and raw aura owners are independently tested */ namespace Styx.Logic.Common { public static class Rest {
 public static bool IsConsumableRetryReady(bool drinking)=>World.RetryReady;
 public static string GetAdmissionDenial(Player player,bool requireStationary=true,bool allowQueries=true){World.Admissions++;return World.Denial??(requireStationary?World.StartDenial:World.PrepareDenial);}
 public static string GetContinuationDenial(Player player,bool requireStationary=true,bool allowQueries=true){World.ContinuationAdmissions++;return World.ContinuationDenial;}
 public static bool TryObserveActivity(Player player,out bool food,out bool drink){World.Auras++;food=World.Food;drink=World.Drink;return World.AuraKnown;}}}
public static class World {public static string Denial,PrepareDenial,StartDenial,ContinuationDenial;public static bool Food,Drink,AuraKnown,InventoryKnown,HasItem,RetryReady,CanCast,CastAccepted,Corpse,RecoveryBlocked;public static int Admissions,ContinuationAdmissions,Auras,Lookups,Diagnostics,Casts;public static Action OnLookup,OnLog,OnCast;
 public static void Reset(){StyxWoW.Me=new();ObjectManager.Wow=new();Styx.Logic.BehaviorTree.TreeRoot.RunIdentity=new();SingularSettings.Instance=new();CharacterSettings.Instance=new();Denial=PrepareDenial=StartDenial=ContinuationDenial=null;Food=Drink=false;RetryReady=AuraKnown=InventoryKnown=HasItem=CanCast=CastAccepted=Corpse=true;Admissions=ContinuationAdmissions=Auras=Lookups=Diagnostics=Casts=0;OnLookup=OnLog=OnCast=null;ActualRest.Reset();}}
public static class ActualRest {private static DateTime _nextFoodDiagnostic,_nextDrinkDiagnostic;private static string _foodAdmission,_drinkAdmission;
 private static CannibalizeLease _cannibalize;private static bool CorpseAround=>World.Corpse;
 public static void Reset(){_nextFoodDiagnostic=_nextDrinkDiagnostic=DateTime.MinValue;_cannibalize=null;}
 public static bool Start(bool drink)=>CanStartConsumable(drink);public static bool Continue()=>ContinueSupportedRest();
 public static bool PrepareCannibalize()=>CanPrepareCannibalize();public static RunStatus SubmitCannibalizeTest()=>SubmitCannibalize();public static bool ContinueCannibalizeTest()=>ContinueCannibalize();
""";
    private const string Cases = """
public static class RestCases {
static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
public static void Run(){int pass=0;var errors=new List<string>();void Case(string name,Action test){World.Reset();World.RecoveryBlocked=false;try{test();pass++;Console.WriteLine("PASS Singular rest: "+name);}catch(Exception e){errors.Add(name+": "+e.Message);}}
foreach(bool drinking in new[]{false,true}){bool d=drinking;string kind=d?"drink/":"food/";
Case(kind+"low resource admitted",()=>Check(ActualRest.Start(d)&&World.Lookups==1,"eligible consumable not admitted"));
Case(kind+"pending recovery is distinct from absent inventory",()=>{World.RecoveryBlocked=true;Check(!ActualRest.Start(d)&&World.Lookups==0&&World.Auras==0,"conflicting recovery performed inventory/aura work");World.RecoveryBlocked=false;Check(ActualRest.Start(d)&&World.Lookups==1,"released owner could not retry immediately");});
Case(kind+"retry throttle avoids expensive observations",()=>{World.RetryReady=false;for(int i=0;i<20;i++)Check(!ActualRest.Start(d),"throttled attempt admitted");Check(World.Admissions==0&&World.Auras==0&&World.Lookups==0,"throttled resource queried group or inventory");});
Case(kind+"disabled amount has no costly safety observation",()=>{if(d)CharacterSettings.Instance.DrinkAmount=0;else CharacterSettings.Instance.FoodAmount=0;Check(!ActualRest.Start(d)&&World.Admissions==0&&World.Auras==0&&World.Lookups==0,"disabled resource queried group or inventory");});
Case(kind+"healthy resource has no costly safety observation",()=>{StyxWoW.Me.HealthPercent=StyxWoW.Me.ManaPercent=100;Check(!ActualRest.Start(d)&&World.Admissions==0&&World.Lookups==0,"healthy resource queried group");});
foreach(string denial in new[]{"combat","mounted","flying","transport","moving","casting","liquid","instance-roster-unknown","instance-group-moving","battleground"}){string why=denial;Case(kind+"denied/"+why,()=>{World.Denial=why;Check(!ActualRest.Start(d)&&World.Auras==0&&World.Lookups==0,"denied owner read optional aura/inventory");});}
Case(kind+"raw unknown stays conservative",()=>{World.AuraKnown=false;Check(!ActualRest.Start(d)&&World.Lookups==0,"raw UNKNOWN authorized inventory/use");});
Case(kind+"already recovering is not duplicated",()=>{if(d)World.Drink=true;else World.Food=true;Check(!ActualRest.Start(d)&&World.Lookups==0,"active aura duplicated");});
Case(kind+"inventory unknown cannot authorize action",()=>{World.InventoryKnown=World.HasItem=false;Check(!ActualRest.Start(d),"unknown inventory became a candidate");});
Case(kind+"owner replaced by lookup",()=>{World.OnLookup=()=>StyxWoW.Me=new();Check(!ActualRest.Start(d),"old actor admission survived lookup");});
Case(kind+"cancellation propagates",()=>{World.OnLookup=()=>throw new OperationCanceledException();try{ActualRest.Start(d);throw new InvalidOperationException("cancellation lost");}catch(OperationCanceledException){}});
}
Case("mana-less warrior does not drink",()=>{StyxWoW.Me.PowerType=WoWPowerType.Rage;StyxWoW.Me.Class=WoWClass.Warrior;Check(!ActualRest.Start(true),"mana-less drink admitted");});
Case("druid alternate power retains mana recovery",()=>{StyxWoW.Me.PowerType=WoWPowerType.Rage;StyxWoW.Me.Class=WoWClass.Druid;Check(ActualRest.Start(true),"druid mana recovery suppressed");});
Case("new group danger yields ongoing rest",()=>{World.Food=true;World.RetryReady=false;Check(ActualRest.Continue(),"active supported food not held");World.Denial="instance-group-combat";Check(!ActualRest.Continue(),"unsafe continuing rest retained ownership");});
Case("Cannibalize final start rechecks stationary safety",()=>{Check(ActualRest.PrepareCannibalize(),"Cannibalize owner not prepared");World.StartDenial="moving";Check(ActualRest.SubmitCannibalizeTest()==RunStatus.Failure&&World.Casts==0,"moving actor reached Cannibalize native submission");});
Case("Cannibalize preparation defers movement-state UNKNOWN",()=>{World.PrepareDenial="movement-state-unknown";Check(!ActualRest.PrepareCannibalize()&&World.Casts==0,"unknown movement state prepared Cannibalize ownership");});
Case("Cannibalize submission alone is not active-channel acknowledgement",()=>{Check(ActualRest.PrepareCannibalize(),"Cannibalize owner not prepared");Check(ActualRest.SubmitCannibalizeTest()==RunStatus.Success&&World.Casts==1,"Cannibalize submission missing");Check(!ActualRest.ContinueCannibalizeTest(),"submission receipt became active Cannibalize continuation");});
Case("Cannibalize spell metadata without active cast/channel is not continuation",()=>{Check(ActualRest.PrepareCannibalize(),"Cannibalize owner not prepared");Check(ActualRest.SubmitCannibalizeTest()==RunStatus.Success,"Cannibalize submission missing");StyxWoW.Me.CastingSpell=new SpellView{Name="Cannibalize"};Check(!ActualRest.ContinueCannibalizeTest(),"spell metadata alone became active Cannibalize authority");});
Case("observed Cannibalize channel ignores casting gate only",()=>{Check(ActualRest.PrepareCannibalize(),"Cannibalize owner not prepared");Check(ActualRest.SubmitCannibalizeTest()==RunStatus.Success,"Cannibalize submission missing");StyxWoW.Me.IsCasting=StyxWoW.Me.IsChanneling=true;StyxWoW.Me.CastingSpell=new SpellView{Name="Cannibalize"};Check(ActualRest.ContinueCannibalizeTest(),"valid active Cannibalize was rejected merely for channeling");});
Case("Cannibalize reentrant successor owns post-dispatch lifetime",()=>{Check(ActualRest.PrepareCannibalize(),"predecessor Cannibalize owner not prepared");bool nested=false;World.OnCast=()=>{World.OnCast=null;nested=ActualRest.PrepareCannibalize();};Check(ActualRest.SubmitCannibalizeTest()==RunStatus.Failure&&nested,"predecessor retained authority after reentrant successor acquisition");StyxWoW.Me.IsCasting=StyxWoW.Me.IsChanneling=true;StyxWoW.Me.CastingSpell=new SpellView{Name="Cannibalize"};Check(ActualRest.ContinueCannibalizeTest(),"predecessor post-dispatch cleanup clobbered the reentrant successor");});
foreach(string danger in new[]{"actor-or-pet-combat","instance-group-combat","instance-roster-unknown","moving","movement-state-unknown","ground-unit-state-unknown","liquid-or-dry-observation-unavailable","flying"}){string why=danger;Case("Cannibalize continuation denied/"+why,()=>{Check(ActualRest.PrepareCannibalize(),"Cannibalize owner not prepared");Check(ActualRest.SubmitCannibalizeTest()==RunStatus.Success,"Cannibalize submission missing");StyxWoW.Me.IsCasting=StyxWoW.Me.IsChanneling=true;StyxWoW.Me.CastingSpell=new SpellView{Name="Cannibalize"};World.ContinuationDenial=why;Check(!ActualRest.ContinueCannibalizeTest(),"unsafe Cannibalize lifetime retained ownership");});}
foreach(string replacement in new[]{"actor","memory","run"}){string what=replacement;Case("Cannibalize continuation rejects "+what+" replacement",()=>{Check(ActualRest.PrepareCannibalize(),"Cannibalize owner not prepared");Check(ActualRest.SubmitCannibalizeTest()==RunStatus.Success,"Cannibalize submission missing");StyxWoW.Me.IsCasting=StyxWoW.Me.IsChanneling=true;StyxWoW.Me.CastingSpell=new SpellView{Name="Cannibalize"};if(what=="actor")StyxWoW.Me=new LocalPlayer{Guid=1,BaseAddress=100,MapId=1,IsCasting=true,IsChanneling=true,CastingSpell=new SpellView{Name="Cannibalize"}};else if(what=="memory")ObjectManager.Wow=new();else Styx.Logic.BehaviorTree.TreeRoot.RunIdentity=new();Check(!ActualRest.ContinueCannibalizeTest(),"replacement inherited active Cannibalize lifetime");});}
Console.WriteLine($"Singular rest admission: {pass}/{pass+errors.Count}; complete production predicate owners, controlled observation leaves.");if(errors.Count!=0)throw new InvalidOperationException(string.Join("; ",errors));}}
""";
}
