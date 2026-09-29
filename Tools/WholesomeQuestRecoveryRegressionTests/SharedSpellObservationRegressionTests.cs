using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Complete tracked wait/cancellation factories and named-cast admission, with
// the actual melee-range property. TreeSharp is real; observation changes and
// terminal commands are controlled. No native cast/request provenance is claimed.
internal static class SharedSpellObservationRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string? root = null;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "CopilotBuddy.csproj"))) { root = directory.FullName; break; }
        if (root == null) throw new InvalidOperationException("Tracked checkout required.");
        var source = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,
            "runtime-snapshot/Routines/Singular wotlk/Helpers/Spell.cs"))).GetRoot();
        string[] required = { "WaitForCast", "WaitForCastOrChannel", "PreventDoubleCast", "CanCastNamedSpell" };
        var methods = source.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(method => required.Contains(method.Identifier.ValueText) || method.Identifier.ValueText == "MeleeRangeFor").ToArray();
        foreach (string name in required)
            if (!methods.Any(method => method.Identifier.ValueText == name)) throw new InvalidOperationException("Missing owner: " + name);
        string melee = source.DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Single(property => property.Identifier.ValueText == "MeleeRange").ToString();
        string temporary = Path.Combine(Path.GetTempPath(), "cb-shared-spell-observation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        bool previousLogging = Styx.Helpers.Logging.FileLogging;
        try
        {
            Styx.Helpers.Logging.FileLogging = false;
            File.WriteAllText(Path.Combine(temporary, "Probe.cs"), Prefix + string.Join("\n", methods.Select(method => method.ToString())) + melee + NamedProbe + "}\n" + Cases);
            Type type = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(type, new object[] { temporary })!;
            var result = (CompilerResults)type.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(error => !error.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Actual shared spell owners compile: " + string.Join("; ", errors.Select(error => error.ToString())));
            var assembly = (Assembly)type.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            try { assembly.GetType("ObservationCases", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        finally { Styx.Helpers.Logging.FileLogging = previousLogging; Directory.Delete(temporary, true); }
    }

    private const string Prefix = """
using System;using System.Collections.Generic;using System.Linq;using TreeSharp;using CommonBehaviors.Actions;using Action=TreeSharp.Action;
public delegate WoWUnit UnitSelectionDelegate(object context);
public delegate bool SimpleBooleanDelegate(object context);
public sealed class WoWSpell {public int Id=101;public string Name="Chosen";public uint SpellRangeId=2;public float MinRange,MaxRange=100;}
public sealed class Aura {
 public string Name="Chosen";public int SpellId=101;public ulong Creator=1;public bool Active=true;
 public ulong CreatorGuid{get{World.Event("aura-owner");return Creator;}}
 public bool IsActive{get{World.Event("aura-active");return Active;}}
}
public class WoWUnit {
 public string Role="target";public ulong Guid;public bool IsValid=true,IsAlive=true,Player;public float Distance=3,Reach;
 public WoWUnit Selected;public bool Casting=true,Wanding;public ulong ObjectChannel;public uint ChannelId;
 public List<Aura> AuraList=new();public TimeSpan[] Times=new[]{TimeSpan.FromSeconds(2)};private int timeIndex;
 public bool IsPlayer=>Player;public bool IsMe=>ReferenceEquals(this,StyxWoW.Me);
 public bool InLineOfSpellSight=>true;
 public float CombatReach{get{World.Event("reach-"+Role);return Reach;}}
 public bool IsCasting{get{World.Event("casting");return Casting;}}
 public bool IsWanding(){World.Event("wanding");return Wanding;}
 public ulong ChannelObjectGuid{get{World.Event("object-channel");return ObjectChannel;}}
 public uint ChanneledCastingSpellId{get{World.Event("channel-id");return ChannelId;}}
 public TimeSpan CurrentCastTimeLeft{get{World.Event("time");World.TimeReads++;return Times[Math.Min(timeIndex++,Times.Length-1)];}}
 public WoWSpell CastingSpell{get{World.Event("spell-object");return World.Cast;}}
 public int CastingSpellId{get{World.Event("spell-id");return World.Cast?.Id??0;}}
 public WoWUnit CurrentTarget{get{World.Event("target");return Selected;}}
 public ulong CurrentTargetGuid=>Selected?.Guid??0;
 public Dictionary<string,Aura> Auras{get{World.Event("auras");var result=new Dictionary<string,Aura>();foreach(var aura in AuraList)result[aura.Name]=aura;return result;}}
 public IEnumerable<Aura> GetAllAuras(){World.Event("auras");return AuraList.ToArray();}
}
public static class StyxWoW {
 public static WoWUnit Me;
 public static class WoWClient{public static uint Latency{get{World.Event("latency");return 50;}}}
}
public static class SpellManager {
 public static Dictionary<string,WoWSpell> Spells=new();
 public static bool CanCast(string name,WoWUnit target,bool range,bool movement)=>World.Ready;
 public static void StopCasting(){World.Stops++;World.Stopped=StyxWoW.Me;}
}
public static class Unit {public static bool IsCombatActionSafe(string name,WoWUnit target)=>target!=null;}
public static class Movement {public static Composite CreateFaceTargetBehavior()=>new Action(_=>{World.Faces++;return RunStatus.Success;});}
public static class World {
 public static WoWUnit Owner,Target,Candidate,Stopped;public static WoWSpell Cast;
 public static bool Ready;public static int Stops,Faces,TimeReads,Selections;public static string Stage;public static System.Action Change;
 public static void Event(string stage){if(Stage==stage){var change=Change;Stage=null;Change=null;change?.Invoke();}}
 public static void Reset(){
  Owner=new WoWUnit{Role="owner",Guid=1};Target=new WoWUnit{Guid=2};Candidate=Target;Owner.Selected=Target;StyxWoW.Me=Owner;
  Target.AuraList.Add(new Aura());Cast=new WoWSpell();SpellManager.Spells.Clear();SpellManager.Spells.Add("Chosen",new WoWSpell());
  Ready=true;Stops=Faces=TimeReads=Selections=0;Stopped=null;Stage=null;Change=null;
 }
 public static void Revoke(string mode){
  if(mode=="actor")StyxWoW.Me=new WoWUnit{Role="owner",Guid=1,Selected=Target};
  else if(mode=="guid")Owner.Guid=9;
  else if(mode=="invalid")Owner.IsValid=false;
  else if(mode=="dead")Owner.IsAlive=false;
  else if(mode=="missing")StyxWoW.Me=null;
 }
}
public static class SpellProbe {
""";

    private const string NamedProbe = """
 public static bool Named(WoWUnit target){
  var owner=StyxWoW.Me;ulong actorGuid=owner?.Guid??0,targetGuid=target?.Guid??0;
  bool Current()=>actorGuid!=0&&targetGuid!=0&&owner!=null&&target!=null&&owner.IsValid&&owner.IsAlive&&target.IsValid
   &&owner.Guid==actorGuid&&target.Guid==targetGuid&&ReferenceEquals(owner,StyxWoW.Me);
  return CanCastNamedSpell("Chosen",target,_=>false,_=>true,null,Current);
 }
""";

    private const string Cases = """
public static class ObservationCases {
 private sealed class Failure(string reason):Exception(reason){}
 private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
 private static readonly object Context=new();
 private static RunStatus Tick(Composite tree){
  tree.Start(Context);try{return tree.Tick(Context);}finally{tree.Stop(Context);}
 }
 private static Composite Duplicate(bool explicitSelector=true)=>explicitSelector
  ?SpellProbe.PreventDoubleCast(_=>{World.Selections++;World.Event("select");return World.Candidate;},"Chosen")
  :SpellProbe.PreventDoubleCast("Chosen");
 private static void DenyCancel(){Check(Tick(Duplicate())==RunStatus.Failure&&World.Stops==0,"revoked observations authorized cast cancellation");}
 public static void Run(){
  int passed=0,assertions=0,unexpected=0,total=0;
  void Case(string name,System.Action body){
   total++;World.Reset();var messages=new List<string>();
   Styx.Helpers.Logging.LogMessageDelegate listener=batch=>messages.AddRange(batch.Select(message=>message.Message));
   Styx.Helpers.Logging.OnLogMessage+=listener;
   try{body();Check(!messages.Any(message=>message.Contains("Exception",StringComparison.OrdinalIgnoreCase)),"TreeSharp swallowed an observation exception");passed++;Console.WriteLine("PASS shared spell observation: "+name);}
   catch(Failure error){assertions++;Console.Error.WriteLine("FAIL shared spell observation: "+name+": "+error.Message);}
   catch(NullReferenceException){assertions++;Console.Error.WriteLine("FAIL shared spell observation: "+name+": missing observation dereferenced");}
   catch(Exception error){unexpected++;Console.Error.WriteLine("ERROR shared spell observation: "+name+": "+error);}
   finally{Styx.Helpers.Logging.OnLogMessage-=listener;}
  }
  Case("named melee uses the selected large NPC",()=>{World.Owner.Reach=1.5f;var target=new WoWUnit{Guid=3,Reach=10,Distance=10};Check(SpellProbe.Named(target),"displayed small target suppressed a reachable selected NPC");});
  Case("named melee rejects the selected small NPC beyond reach",()=>{World.Target.Reach=20;var target=new WoWUnit{Guid=3,Distance=10};Check(!SpellProbe.Named(target),"displayed large target inflated selected reach");});
  Case("named melee retains the selected player limit",()=>{World.Target.Reach=20;var target=new WoWUnit{Guid=3,Player=true,Distance=4};Check(!SpellProbe.Named(target),"selected player borrowed NPC reach");});
  Case("named melee does not borrow displayed player limit",()=>{World.Target.Player=true;var target=new WoWUnit{Guid=3,Reach=10,Distance=8};Check(SpellProbe.Named(target),"displayed player suppressed selected NPC reach");});
  Case("named explicit melee does not require displayed target",()=>{World.Owner.Selected=null;var target=new WoWUnit{Guid=3,Distance=3};Check(SpellProbe.Named(target),"explicit participant borrowed missing current target");});
  Case("named melee exact boundary remains exclusive",()=>{World.Target.Distance=5;Check(!SpellProbe.Named(World.Target),"existing strict melee boundary changed");});
  Case("named melee ordinary admission remains",()=>Check(SpellProbe.Named(World.Target),"ordinary melee admission lost"));
  foreach(float value in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity}){
   float reach=value;Case("named melee rejects invalid reach "+reach,()=>{World.Owner.Reach=reach;Check(!SpellProbe.Named(World.Target),"nonfinite reach admitted melee");});
  }
  Case("melee property missing actor",()=>{StyxWoW.Me=null;Check(SpellProbe.MeleeRange==0,"missing actor returned range");});
  Case("melee property missing target",()=>{World.Owner.Selected=null;Check(SpellProbe.MeleeRange==0,"missing target returned range");});
  Case("melee property target disappears during observation",()=>{World.Stage="target";World.Change=()=>World.Owner.Selected=null;Check(SpellProbe.MeleeRange==0,"disappearing target returned range");});
  Case("melee property actor changes during reach",()=>{World.Stage="reach-target";World.Change=()=>World.Revoke("actor");Check(SpellProbe.MeleeRange==0,"range mixed actor lifetimes");});
  Case("melee property target changes during reach",()=>{World.Stage="reach-target";World.Change=()=>World.Owner.Selected=new WoWUnit{Guid=3};Check(SpellProbe.MeleeRange==0,"range mixed selected participants");});
  Case("melee property ordinary NPC minimum",()=>Check(SpellProbe.MeleeRange==5,"existing NPC minimum changed"));
  Case("melee property ordinary player limit",()=>{World.Target.Player=true;Check(SpellProbe.MeleeRange==3.5f,"existing player limit changed");});
  Case("melee property original reach arithmetic",()=>{World.Owner.Reach=2;World.Target.Reach=10;Check(Math.Abs(SpellProbe.MeleeRange-13.3333334f)<0.0001,"existing NPC reach arithmetic changed");});

  Case("wait healthy cast",()=>Check(Tick(SpellProbe.WaitForCast(true,true))==RunStatus.Success&&World.Faces==1,"healthy cast wait changed"));
  Case("wait no cast",()=>{World.Owner.Casting=false;Check(Tick(SpellProbe.WaitForCast(true,true))==RunStatus.Failure&&World.Faces==0,"absent cast still blocked/faced");});
  Case("wait wand",()=>{World.Owner.Wanding=true;Check(Tick(SpellProbe.WaitForCast(true,true))==RunStatus.Failure&&World.Faces==0,"wand behavior changed");});
  Case("wait object channel",()=>{World.Owner.ObjectChannel=2;Check(Tick(SpellProbe.WaitForCast(true,true))==RunStatus.Failure&&World.Faces==0,"object channel policy changed");});
  Case("wait spell channel never faces",()=>{World.Owner.ChannelId=101;Check(Tick(SpellProbe.WaitForCast(true,true))==RunStatus.Success&&World.Faces==0,"spell channel faced");});
  Case("wait uses one remaining-time observation",()=>{World.Owner.Times=new[]{TimeSpan.FromSeconds(2),TimeSpan.Zero};Check(Tick(SpellProbe.WaitForCast(true,true))==RunStatus.Success&&World.Faces==1&&World.TimeReads==1,"wait mixed later remaining-time values");});
  Case("wait uses the first queue-window observation",()=>{World.Owner.Times=new[]{TimeSpan.FromMilliseconds(99),TimeSpan.FromSeconds(2)};Check(Tick(SpellProbe.WaitForCast(true,true))==RunStatus.Failure&&World.Faces==0&&World.TimeReads==1,"later time overrode the admitted queue window");});
  Case("wait zero time keeps existing unknown policy",()=>{World.Owner.Times=new[]{TimeSpan.Zero,TimeSpan.FromSeconds(2)};Check(Tick(SpellProbe.WaitForCast(true,true))==RunStatus.Success&&World.TimeReads==1,"zero-time policy changed");});
  Case("wait cast ends during time read",()=>{World.Stage="time";World.Change=()=>World.Owner.Casting=false;Check(Tick(SpellProbe.WaitForCast(true,true))==RunStatus.Failure&&World.Faces==0,"ended cast still authorized facing");});
  foreach(string mode in new[]{"missing","guid","invalid","dead"}){
   string change=mode;Case("wait initial "+change,()=>{if(change=="guid")World.Owner.Guid=0;else World.Revoke(change);Check(Tick(SpellProbe.WaitForCast(true,true))==RunStatus.Failure&&World.Faces==0,"invalid actor still waited/faced");});
  }
  foreach(string stage in new[]{"casting","wanding","object-channel","latency","time","channel-id"})
   foreach(string mode in new[]{"actor","guid","invalid","dead","missing"}){
    string boundary=stage,change=mode;Case("wait "+boundary+" revokes "+change,()=>{World.Stage=boundary;World.Change=()=>World.Revoke(change);Check(Tick(SpellProbe.WaitForCast(true,true))==RunStatus.Failure&&World.Faces==0,"mixed actor observations reached wait/facing");});
   }
  Case("channel fallback still blocks active channel",()=>{World.Owner.Casting=false;World.Owner.ChannelId=101;Check(Tick(SpellProbe.WaitForCastOrChannel())==RunStatus.Success&&World.Faces==0,"healthy channel fallback changed");});
  foreach(string mode in new[]{"missing","guid","invalid","dead"}){
   string change=mode;Case("channel fallback rejects "+change,()=>{World.Owner.Casting=false;World.Owner.ChannelId=101;if(change=="guid")World.Owner.Guid=0;else World.Revoke(change);Check(Tick(SpellProbe.WaitForCastOrChannel())==RunStatus.Failure&&World.Faces==0,"invalid channel actor blocked unrelated work");});
  }
  Case("channel fallback rejects replacement during observation",()=>{World.Owner.Casting=false;World.Owner.ChannelId=101;World.Stage="channel-id";World.Change=()=>World.Revoke("actor");Check(Tick(SpellProbe.WaitForCastOrChannel())==RunStatus.Failure,"channel observation transferred to another actor");});

  Case("duplicate own aura cancels once",()=>Check(Tick(Duplicate())==RunStatus.Success&&World.Stops==1&&ReferenceEquals(World.Stopped,World.Owner),"healthy duplicate cancellation changed"));
  Case("duplicate default selector retains current subject",()=>Check(Tick(Duplicate(false))==RunStatus.Success&&World.Stops==1,"default selector no longer cancels duplicate"));
  Case("duplicate foreign aura does not cancel",()=>{World.Target.AuraList[0].Creator=9;DenyCancel();});
  Case("duplicate expired own aura does not cancel",()=>{World.Target.AuraList[0].Active=false;DenyCancel();});
  Case("duplicate complete aura collection retains own copy",()=>{World.Target.AuraList.Add(new Aura{Creator=9});Check(Tick(Duplicate())==RunStatus.Success&&World.Stops==1,"foreign same-name aura hid an owned duplicate");});
  Case("duplicate no cast",()=>{World.Owner.Casting=false;DenyCancel();});
  Case("duplicate absent spell observation",()=>{World.Cast=null;DenyCancel();});
  Case("duplicate unrelated cast",()=>{World.Cast=new WoWSpell{Id=202,Name="Other"};DenyCancel();});
  Case("duplicate absent names",()=>Check(Tick(SpellProbe.PreventDoubleCast(_=>World.Target,(string[])null))==RunStatus.Failure&&World.Stops==0,"absent names authorized cancellation"));
  Case("duplicate missing selector",()=>Check(Tick(SpellProbe.PreventDoubleCast((UnitSelectionDelegate)null,"Chosen"))==RunStatus.Failure&&World.Stops==0,"missing selector authorized cancellation"));
  Case("duplicate missing selected unit",()=>{World.Candidate=null;DenyCancel();});
  foreach(string mode in new[]{"missing","guid","invalid","dead"}){
   string change=mode;Case("duplicate initial actor "+change,()=>{if(change=="guid")World.Owner.Guid=0;else World.Revoke(change);DenyCancel();});
  }
  foreach(string stage in new[]{"spell-object","select","auras","aura-owner"})
   foreach(string mode in new[]{"actor","guid","invalid","dead","missing"}){
    string boundary=stage,change=mode;Case("duplicate "+boundary+" revokes "+change,()=>{World.Stage=boundary;World.Change=()=>World.Revoke(change);DenyCancel();});
   }
  foreach(string mode in new[]{"guid","invalid","dead"}){
   string change=mode;Case("duplicate selected unit changes during aura read "+change,()=>{World.Stage="auras";World.Change=()=>{if(change=="guid")World.Target.Guid=9;else if(change=="invalid")World.Target.IsValid=false;else World.Target.IsAlive=false;};DenyCancel();});
  }
  Case("duplicate selector changes between observations",()=>{
   var other=new WoWUnit{Guid=3};other.AuraList.Add(new Aura());
   var tree=SpellProbe.PreventDoubleCast(_=>{World.Selections++;return World.Selections==1?World.Target:other;},"Chosen");
   Check(Tick(tree)==RunStatus.Failure&&World.Stops==0,"selector transferred cancellation to another subject");
  });
  Case("duplicate selector disappears between observations",()=>{
   var tree=SpellProbe.PreventDoubleCast(_=>{World.Selections++;return World.Selections==1?World.Target:null;},"Chosen");
   Check(Tick(tree)==RunStatus.Failure&&World.Stops==0,"missing later selector authorized cancellation");
  });
  Case("duplicate cast changes during creator observation",()=>{World.Stage="aura-owner";World.Change=()=>World.Cast=new WoWSpell{Id=202,Name="Other"};DenyCancel();});
  Case("duplicate cast ends during creator observation",()=>{World.Stage="aura-owner";World.Change=()=>World.Owner.Casting=false;DenyCancel();});
  Console.WriteLine($"Shared spell observation scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; actual wait/cancel factories, melee policy and named admission; real TreeSharp; controlled observations/commands; no native cast-instance or physical-range acceptance.");
  if(assertions+unexpected!=0)throw new InvalidOperationException("Shared spell observation regression failures.");
 }
}
""";
}
