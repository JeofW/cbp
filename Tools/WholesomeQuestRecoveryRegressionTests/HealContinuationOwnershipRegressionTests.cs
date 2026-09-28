using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// All five actual Heal factories with real TreeSharp and controlled cast submission.
// The boundary distinguishes a submitted cast from a later actor/recipient/spell;
// it does not assert native request provenance or execute StopCasting in a client.
internal static class HealContinuationOwnershipRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string? root = null;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "CopilotBuddy.csproj"))) { root = directory.FullName; break; }
        if (root == null) throw new InvalidOperationException("Tracked checkout required.");
        var methods = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "runtime-snapshot/Routines/Singular wotlk/Helpers/Spell.cs")))
            .GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Where(method => method.Identifier.ValueText == "Heal").ToArray();
        if (methods.Length != 5) throw new InvalidOperationException("Expected all five tracked Heal factories.");
        string temporary = Path.Combine(Path.GetTempPath(), "cb-heal-continuation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            File.WriteAllText(Path.Combine(temporary, "Probe.cs"), Prefix + string.Join("\n", methods.Select(method => method.ToString())) + CastLeaf + "}\n" + Cases);
            Type type = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(type, new object[] { temporary })!;
            var result = (CompilerResults)type.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(error => !error.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Actual Heal compile: " + string.Join("; ", errors.Select(error => error.ToString())));
            var assembly = (Assembly)type.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            try { assembly.GetType("HealCases", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        finally { Directory.Delete(temporary, true); }
    }
    private const string Prefix = """
using System;using System.Collections.Generic;using System.Linq;using TreeSharp;using CommonBehaviors.Actions;using Action=TreeSharp.Action;
public class WoWUnit {public ulong Guid;public bool IsValid=true,IsAlive=true,IsCasting;public int CastingSpellId,ChanneledCastingSpellId;}
public sealed class LocalPlayer:WoWUnit{}
public sealed class WoWSpell {public int Id=101,CastTime=1500;public bool IsFunnel,IsChanneled;}
public static class StyxWoW {public static LocalPlayer Me;}
public static class SpellManager {
 public static Dictionary<string,WoWSpell> Spells=new();
 public static void StopCasting(){HealCases.Stopped.Add(StyxWoW.Me);if(StyxWoW.Me!=null)StyxWoW.Me.IsCasting=false;}
}
public delegate WoWUnit UnitSelectionDelegate(object context);
public delegate bool SimpleBooleanDelegate(object context);
public static class Spell {
""";
    private const string CastLeaf = """
 public static Composite Cast(string name,SimpleBooleanDelegate movement,UnitSelectionDelegate select,SimpleBooleanDelegate requirements)=>new Action(context=>{
  var player=StyxWoW.Me;
  if(player==null||select==null||requirements==null||movement==null||!requirements(context))return RunStatus.Failure;
  var unit=select(context);if(unit==null)return RunStatus.Failure;
  movement(context);
  if(!HealCases.CastAccepted)return RunStatus.Failure;
  HealCases.Submitted++;HealCases.SubmittedRecipient=unit;
  player.IsCasting=!HealCases.Instant;player.CastingSpellId=HealCases.SubmittedSpellId;
  HealCases.OnSubmit?.Invoke();return RunStatus.Success;
 });
""";
    private const string Cases = """
public static class HealCases {
 private sealed class Failure(string reason):Exception(reason){}
 public static int Submitted,Selections,SubmittedSpellId;public static WoWUnit SubmittedRecipient;
 public static bool CastAccepted,Instant,Needed;public static System.Action OnSubmit,OnSelect,OnRequirements,OnMovement;
 public static List<WoWUnit> Stopped=new();private static LocalPlayer owner;private static WoWUnit recipient;
 private static readonly object Context=new();
 private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
 private static void Reset(){owner=new LocalPlayer{Guid=1};StyxWoW.Me=owner;recipient=new WoWUnit{Guid=2};SpellManager.Spells.Clear();SpellManager.Spells.Add("Heal",new WoWSpell());Submitted=Selections=0;SubmittedSpellId=101;SubmittedRecipient=null;CastAccepted=Needed=true;Instant=false;Stopped.Clear();OnSubmit=OnSelect=OnRequirements=OnMovement=null;}
 private static Composite Build(int variant){
  UnitSelectionDelegate selected=c=>{Selections++;OnSelect?.Invoke();return recipient;};
  SimpleBooleanDelegate needed=c=>{Check(ReferenceEquals(c,Context),"caller context was replaced");OnRequirements?.Invoke();return Needed;};
  return variant switch{0=>Spell.Heal("Heal"),1=>Spell.Heal("Heal",needed),2=>Spell.Heal("Heal",selected),3=>Spell.Heal("Heal",selected,needed),_=>Spell.Heal("Heal",c=>{OnMovement?.Invoke();return true;},selected,needed)};
 }
 private static RunStatus Begin(Composite tree){tree.Start(Context);return tree.Tick(Context);}
 private static RunStatus Step(Composite tree){try{return tree.Tick(Context);}catch(Exception error){throw new Failure("continuation escaped: "+error.GetType().Name);}}
 public static void Run(){
  int passed=0,assertions=0,unexpected=0,total=0;
  void Case(string name,System.Action body){total++;Reset();try{body();passed++;Console.WriteLine("PASS heal continuation: "+name);}catch(Failure error){assertions++;Console.Error.WriteLine("FAIL heal continuation: "+name+": "+error.Message);}catch(Exception error){unexpected++;Console.Error.WriteLine("ERROR heal continuation: "+name+": "+error);}}
  foreach(int index in Enumerable.Range(0,5)){
   int variant=index;
   Case("overload "+variant+" waits for its cast then completes",()=>{var tree=Build(variant);try{Check(Begin(tree)==RunStatus.Running&&Submitted==1,"cast did not enter an owned wait");owner.IsCasting=false;Check(Step(tree)==RunStatus.Success&&Stopped.Count==0,"normal completion changed");}finally{tree.Stop(Context);}});
   Case("overload "+variant+" rejects failed submission without waiting",()=>{CastAccepted=false;var tree=Build(variant);try{Check(Begin(tree)==RunStatus.Failure&&Submitted==0&&Stopped.Count==0,"failed cast entered the wait");}finally{tree.Stop(Context);}});
   Case("overload "+variant+" instant cast returns without cancelling",()=>{Instant=true;SpellManager.Spells["Heal"].CastTime=0;var tree=Build(variant);try{Check(Begin(tree)==RunStatus.Success&&Submitted==1&&Stopped.Count==0,"instant cast stalled or cancelled");}finally{tree.Stop(Context);}});
   Case("overload "+variant+" keeps a running channel",()=>{OnSubmit=()=>owner.ChanneledCastingSpellId=101;var tree=Build(variant);try{Check(Begin(tree)==RunStatus.Running,"channel did not enter wait");Needed=false;Check(Step(tree)==RunStatus.Running&&Stopped.Count==0,"channel was cancelled");owner.ChanneledCastingSpellId=0;owner.IsCasting=false;Check(Step(tree)==RunStatus.Success,"ended channel did not complete");}finally{tree.Stop(Context);}});
   foreach(string mode in new[]{"missing-actor","new-actor","actor-guid","actor-invalid","actor-dead","different-spell"}){
    string change=mode;
    Case("overload "+variant+" revokes wait on "+change,()=>{var tree=Build(variant);try{
     Check(Begin(tree)==RunStatus.Running,"expected a pending cast");
     if(change=="missing-actor")StyxWoW.Me=null;
     else if(change=="new-actor")StyxWoW.Me=new LocalPlayer{Guid=3,IsCasting=true,CastingSpellId=101};
     else if(change=="actor-guid")owner.Guid=3;
     else if(change=="actor-invalid")owner.IsValid=false;
     else if(change=="actor-dead")owner.IsAlive=false;
     else owner.CastingSpellId=202;
     Needed=false;
     Check(Step(tree)!=RunStatus.Running&&Stopped.Count==0,"stale heal kept waiting or cancelled a replacement cast");
    }finally{tree.Stop(Context);}});
   }
   Case("overload "+variant+" stopped tree does not leak its owner into reuse",()=>{var tree=Build(variant);try{Check(Begin(tree)==RunStatus.Running,"expected wait");}finally{tree.Stop(Context);}Reset();Instant=true;SpellManager.Spells["Heal"].CastTime=0;try{Check(Begin(tree)==RunStatus.Success&&Submitted==1&&Stopped.Count==0,"reused factory retained stale continuation");}finally{tree.Stop(Context);}});
   if(variant==1||variant==3||variant==4){
    Case("overload "+variant+" cancels its still-current heal once",()=>{var tree=Build(variant);try{Check(Begin(tree)==RunStatus.Running,"expected wait");Needed=false;Check(Step(tree)==RunStatus.Success&&Stopped.SequenceEqual(new[]{owner}),"ordinary owned cancellation changed");}finally{tree.Stop(Context);}});
    foreach(string mode in new[]{"actor-change","actor-guid","recipient-invalid","recipient-guid","cast-change"}){
     string change=mode;
     Case("overload "+variant+" requirement callback revokes "+change,()=>{var tree=Build(variant);try{
      Check(Begin(tree)==RunStatus.Running,"expected wait");Needed=false;
      OnRequirements=()=>{if(change=="actor-change")StyxWoW.Me=new LocalPlayer{Guid=3,IsCasting=true,CastingSpellId=101};else if(change=="actor-guid")owner.Guid=3;else if(change=="cast-change")owner.CastingSpellId=202;else if(variant==1){if(change=="recipient-invalid")owner.IsValid=false;else owner.Guid=9;}else{if(change=="recipient-invalid")recipient.IsValid=false;else recipient.Guid=9;}};
      Check(Step(tree)!=RunStatus.Running&&Stopped.Count==0,"callback revoked the request but global StopCasting still ran");
     }finally{tree.Stop(Context);}});
    }
   }
   if(variant>=2){
    Case("overload "+variant+" retains its selected recipient",()=>{var tree=Build(variant);try{Check(Begin(tree)==RunStatus.Running,"expected wait");var original=SubmittedRecipient;recipient=new WoWUnit{Guid=8};Step(tree);Check(Selections==1&&ReferenceEquals(original,SubmittedRecipient),"pending heal reselected its recipient");}finally{tree.Stop(Context);}});
    Case("overload "+variant+" rejects actor changed by recipient selector",()=>{OnSelect=()=>StyxWoW.Me=new LocalPlayer{Guid=9};var tree=Build(variant);try{Check(Begin(tree)==RunStatus.Failure&&Submitted==0,"selector transferred cast to a replacement actor");}finally{tree.Stop(Context);}});
   }
  }
  Console.WriteLine($"Heal continuation scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; all five tracked factories/real TreeSharp; controlled submission and actor state; no native request provenance or game acceptance.");
  if(assertions+unexpected!=0)throw new InvalidOperationException("Heal continuation regression failures.");
 }
}
""";
}
