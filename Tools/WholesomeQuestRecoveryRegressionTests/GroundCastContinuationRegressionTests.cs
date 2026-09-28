using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Execute the complete tracked ground-cast factory and pending-name observer with
// real TreeSharp. World, spell submission and placement are controlled leaves.
// Matching pending names are observations, not proof of native request ownership.
internal static class GroundCastContinuationRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string? root = null;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "CopilotBuddy.csproj"))) { root = directory.FullName; break; }
        if (root == null) throw new InvalidOperationException("Tracked checkout required.");
        var spell = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,
            "runtime-snapshot/Routines/Singular wotlk/Helpers/Spell.cs"))).GetRoot();
        string factory = spell.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method =>
            method.Identifier.ValueText == "CastOnGround" && method.ParameterList.Parameters.Count == 3).ToFullString();
        var player = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,
            "Styx/WoWInternals/WoWObjects/LocalPlayer.cs"))).GetRoot();
        string observer = player.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method =>
            method.Identifier.ValueText == "HasPendingSpell" && method.ParameterList.Parameters.Count == 1 &&
            method.ParameterList.Parameters[0].Type!.ToString() == "string").ToFullString();
        string temporary = Path.Combine(Path.GetTempPath(), "cb-ground-continuation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            File.WriteAllText(Path.Combine(temporary, "Probe.cs"), Prefix + observer + "}\npublic static class GroundProbe {\n" + factory + "}\n" + Cases);
            Type type = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(type, new object[] { temporary })!;
            var result = (CompilerResults)type.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(error => !error.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Actual ground factory compile: " + string.Join("; ", errors.Select(error => error.ToString())));
            var assembly = (Assembly)type.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            try { assembly.GetType("GroundCases", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        finally { Directory.Delete(temporary, true); }
    }

    private const string Prefix = """
using System;using System.Collections.Generic;using System.Threading;using TreeSharp;using CommonBehaviors.Actions;
using Action=TreeSharp.Action;
public delegate WoWPoint LocationRetriever(object context);
public delegate bool SimpleBooleanDelegate(object context);
public struct WoWPoint {
 public double X,Y,Z;
 public static WoWPoint Empty=>default;public static WoWPoint Zero=>default;
 public double Distance(WoWPoint other)=>Math.Sqrt((X-other.X)*(X-other.X)+(Y-other.Y)*(Y-other.Y)+(Z-other.Z)*(Z-other.Z));
 public static bool operator ==(WoWPoint a,WoWPoint b)=>a.X==b.X&&a.Y==b.Y&&a.Z==b.Z;
 public static bool operator !=(WoWPoint a,WoWPoint b)=>!(a==b);
 public override bool Equals(object value)=>value is WoWPoint other&&this==other;
 public override int GetHashCode()=>HashCode.Combine(X,Y,Z);
}
public sealed class WoWSpell {public string Name="Selected";public double MaxRange=40;}
public static class World {
 public static bool Allowed,Accepted,Safe,Needed,ClickAccepted;
 public static int Selections,Casts,Clicks;public static WoWSpell Pending;
 public static LocalPlayer Owner;public static WoWPoint Destination,Clicked;
 public static string Stage;public static System.Action Callback;
 public static void Event(string stage){if(Stage==stage){var callback=Callback;Stage=null;Callback=null;callback?.Invoke();}}
 public static void Reset(){
  Allowed=Accepted=Safe=Needed=ClickAccepted=true;Selections=Casts=Clicks=0;
  Owner=new LocalPlayer{Guid=1,Location=new WoWPoint{X=1,Y=1,Z=1}};StyxWoW.Me=Owner;
  Destination=new WoWPoint{X=10,Y=1,Z=1};Clicked=default;Pending=new WoWSpell();Stage=null;Callback=null;
  SpellManager.Spells=new Dictionary<string,WoWSpell>{{"Selected",new WoWSpell()}};
 }
 public static void Change(string mode){
  if(mode=="actor")StyxWoW.Me=new LocalPlayer{Guid=1,Location=Owner.Location};
  else if(mode=="guid")Owner.Guid=9;
  else if(mode=="missing")StyxWoW.Me=null;
  else if(mode=="invalid")Owner.IsValid=false;
  else if(mode=="dead")Owner.IsAlive=false;
  else if(mode=="requirements")Needed=false;
  else if(mode=="unsafe")Safe=false;
  else if(mode=="range")Owner.Location=new WoWPoint{X=200,Y=1,Z=1};
 }
}
public static class SpellManager {
 public static Dictionary<string,WoWSpell> Spells;
 public static bool CanCast(string spell){World.Event("can-cast");return World.Allowed;}
 public static bool Cast(string spell){World.Casts++;World.Event("submit");return World.Accepted;}
 public static bool ClickRemoteLocation(WoWPoint point){World.Clicks++;World.Clicked=point;return World.ClickAccepted;}
}
public static class Unit {public static bool IsAreaEffectSafe(string spell,WoWPoint point){World.Event("safety");return World.Safe;}}
public static class Logger {public static void Write(string format,params object[] args){World.Event("log");}}
public static class StyxWoW {public static LocalPlayer Me;}
public sealed class LocalPlayer {
 public ulong Guid;public bool IsValid=true,IsAlive=true;public WoWPoint Location;
 public WoWSpell CurrentPendingCursorSpell {get{World.Event("pending");return World.Pending;}}
""";

    private const string Cases = """
public static class GroundCases {
 private sealed class Failure(string message):Exception(message){}
 private static readonly object Context=new();
 private static Composite tree;
 private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
 private static Composite Build()=>GroundProbe.CastOnGround("Selected",context=>{
  Check(ReferenceEquals(context,Context),"location context changed");World.Selections++;World.Event("select");return World.Destination;
 },context=>{Check(ReferenceEquals(context,Context),"requirements context changed");return World.Needed;});
 private static RunStatus Tick(){
  try{return tree.Tick(Context);}
  catch(NullReferenceException){throw new Failure("missing actor or metadata escaped instead of refusing placement");}
  catch(KeyNotFoundException){throw new Failure("missing spell metadata escaped instead of refusing placement");}
 }
 private static RunStatus Begin(){tree=Build();tree.Start(Context);return Tick();}
 public static void Run(){
  int passed=0,assertions=0,unexpected=0,total=0;
  void Case(string name,System.Action body){
   total++;World.Reset();tree=null;
   try{body();passed++;Console.WriteLine("PASS ground continuation: "+name);}
   catch(Failure error){assertions++;Console.Error.WriteLine("FAIL ground continuation: "+name+": "+error.Message);}
   catch(Exception error){unexpected++;Console.Error.WriteLine("ERROR ground continuation: "+name+": "+error);}
   finally{tree?.Stop(Context);}
  }
  Case("healthy immediate placement",()=>Check(Begin()==RunStatus.Success&&World.Casts==1&&World.Clicks==1,"healthy cast did not place once"));
  Case("destination selected once",()=>{var original=World.Destination;World.Stage="submit";World.Callback=()=>World.Destination=new WoWPoint{X=25,Y=1,Z=1};Check(Begin()==RunStatus.Success&&World.Selections==1&&World.Clicked==original,"cast reselected a different destination");});
  Case("delayed pending spell survives own GCD",()=>{World.Pending=null;var original=World.Destination;Check(Begin()==RunStatus.Running&&World.Clicks==0,"cast did not await pending observation");World.Allowed=false;World.Destination=new WoWPoint{X=25,Y=1,Z=1};World.Pending=new WoWSpell();Check(Tick()==RunStatus.Success&&World.Casts==1&&World.Clicked==original&&World.Selections==1,"wait recast, followed another point, or rejected its own GCD");});
  Case("false cast receipt",()=>{World.Accepted=false;Check(Begin()==RunStatus.Failure&&World.Casts==1&&World.Clicks==0,"refused cast still clicked");});
  Case("false placement receipt",()=>{World.ClickAccepted=false;Check(Begin()==RunStatus.Failure&&World.Clicks==1,"placement refusal became success");});
  Case("unavailable spell",()=>{World.Allowed=false;Check(Begin()==RunStatus.Failure&&World.Casts==0&&World.Clicks==0,"unavailable cast dispatched");});
  Case("missing metadata",()=>{SpellManager.Spells.Clear();Check(Begin()==RunStatus.Failure&&World.Casts==0&&World.Clicks==0,"missing metadata admitted a cast");});
  Case("zero range preserves existing policy",()=>{SpellManager.Spells["Selected"].MaxRange=0;World.Destination=new WoWPoint{X=200,Y=1,Z=1};Check(Begin()==RunStatus.Success&&World.Clicks==1,"zero-range policy changed");});
  foreach(double value in new[]{-1d,double.NaN,double.PositiveInfinity}){
   double invalid=value;Case("invalid maximum range "+invalid,()=>{SpellManager.Spells["Selected"].MaxRange=invalid;Check(Begin()==RunStatus.Failure&&World.Casts==0&&World.Clicks==0,"invalid range admitted placement");});
  }
  foreach(string axis in new[]{"empty","x","y","z"}){
   string coordinate=axis;Case("invalid destination "+coordinate,()=>{SpellManager.Spells["Selected"].MaxRange=0;World.Destination=coordinate switch{"empty"=>default,"x"=>new WoWPoint{X=double.NaN,Y=1,Z=1},"y"=>new WoWPoint{X=1,Y=double.PositiveInfinity,Z=1},_=>new WoWPoint{X=1,Y=1,Z=double.NegativeInfinity}};Check(Begin()==RunStatus.Failure&&World.Casts==0&&World.Clicks==0,"invalid point reached cast/placement");});
  }
  foreach(string stage in new[]{"select","safety","can-cast","log","submit","pending"})
   foreach(string mode in new[]{"actor","guid","missing","invalid","dead"}){
    string boundary=stage,change=mode;
    Case(boundary+" revokes "+change,()=>{World.Stage=boundary;World.Callback=()=>World.Change(change);Check(Begin()==RunStatus.Failure&&World.Clicks==0,"changed owner reached terrain dispatch");if(boundary!="submit"&&boundary!="pending")Check(World.Casts==0,"changed owner reached spell submission");});
   }
  foreach(string mode in new[]{"actor","guid","missing","invalid","dead","requirements","unsafe","range"}){
   string change=mode;Case("delayed wait revokes "+change,()=>{World.Pending=null;Check(Begin()==RunStatus.Running,"expected waiting cast");World.Change(change);World.Pending=new WoWSpell();Check(Tick()==RunStatus.Failure&&World.Clicks==0&&World.Casts==1,"revoked continuation still placed");});
  }
  foreach(string mode in new[]{"requirements","unsafe","range"}){
   string change=mode;Case("logging revokes "+change,()=>{World.Stage="log";World.Callback=()=>World.Change(change);Check(Begin()==RunStatus.Failure&&World.Casts==0&&World.Clicks==0,"stale placement admission survived logging");});
  }
  foreach(string mode in new[]{"missing","foreign"}){
   string observation=mode;Case("timeout with "+observation+" pending spell",()=>{World.Pending=observation=="missing"?null:new WoWSpell{Name="Foreign"};Check(Begin()==RunStatus.Running,"expected bounded pending wait");Thread.Sleep(1150);Check(Tick()==RunStatus.Failure&&World.Casts==1&&World.Clicks==0,"timeout created terrain authority without matching observation");});
  }
  Case("reused factory captures the next actor",()=>{Check(Begin()==RunStatus.Success,"first activation failed");tree.Stop(Context);World.Reset();World.Owner.Guid=8;World.Destination=new WoWPoint{X=20,Y=1,Z=1};tree.Start(Context);Check(Tick()==RunStatus.Success&&World.Clicks==1&&World.Clicked==World.Destination,"factory retained a prior actor or destination");});
  Console.WriteLine($"Ground cast continuation scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; complete factory, pending observer and real TreeSharp; controlled placement/world; no native request provenance or route acceptance.");
  if(assertions+unexpected!=0)throw new InvalidOperationException("Ground cast continuation regression failures.");
 }
}
""";
}
