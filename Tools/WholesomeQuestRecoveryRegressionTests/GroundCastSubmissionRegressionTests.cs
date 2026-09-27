using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Complete tracked CastOnGround factory with real TreeSharp and its existing
// pending-name helper. Only spell/world/backend leaves are controlled. This
// verifies local receipt propagation, not native/request/server ownership.
internal static class GroundCastSubmissionRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        var source = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,
            "runtime-snapshot/Routines/Singular wotlk/Helpers/Spell.cs"))).GetRoot();
        string factory = source.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m =>
            m.Identifier.ValueText == "CastOnGround" && m.ParameterList.Parameters.Count == 3).ToFullString();
        var player = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,
            "Styx/WoWInternals/WoWObjects/LocalPlayer.cs"))).GetRoot();
        string helper = player.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m =>
            m.Identifier.ValueText == "HasPendingSpell" && m.ParameterList.Parameters.Count == 1 &&
            m.ParameterList.Parameters[0].Type!.ToString() == "string").ToFullString();
        string directory = Path.Combine(Path.GetTempPath(), "cb-ground-submission-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "Probe.cs"), Prefix + helper + "}\n" +
                "public static class GroundProbe {\n" + factory + Cases + "}\n");
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { directory })!;
            var result = (CompilerResults)compilerType.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException(string.Join("; ", errors.Select(e => e.ToString())));
            var assembly = (Assembly)compilerType.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            try { assembly.GetType("GroundProbe", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException e) when (e.InnerException != null)
            { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
        }
        finally { Directory.Delete(directory, true); }
    }
    private static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CopilotBuddy.csproj"))) return d.FullName;
        throw new InvalidOperationException("Tracked checkout required");
    }
    private const string Prefix = """
using System;using System.Collections.Generic;using TreeSharp;using CommonBehaviors.Actions;
using Action=TreeSharp.Action;
public delegate Point LocationRetriever(object context);
public delegate bool SimpleBooleanDelegate(object context);
public sealed class Point {public double X;public double Distance(Point other)=>Math.Abs(X-other.X);}
public sealed class WoWSpell {public string Name="Selected";public double MaxRange=40;}
public static class World {
 public static bool CanCast,Accepted,Safe,Requirements;public static string Mode;
 public static int Casts,Clicks,PendingReads;public static WoWSpell Pending;
 public static void Reset(string mode){
  Mode=mode;CanCast=mode!="cannot-cast";Safe=mode!="unsafe-area";Requirements=mode!="requirements-false";
  Accepted=!mode.StartsWith("refused-");Casts=Clicks=PendingReads=0;
  Pending=mode=="refused-empty"||mode=="success-delayed"?null:new WoWSpell{Name=mode=="refused-foreign"?"Other":"Selected"};
  SpellManager.Spells=new Dictionary<string,WoWSpell>{{"Selected",new WoWSpell{MaxRange=mode=="zero-range"?0:40}}};
  StyxWoW.Me=new Player();
 }
}
public static class SpellManager {
 public static Dictionary<string,WoWSpell> Spells;
 public static bool CanCast(string spell)=>World.CanCast;
 public static bool Cast(string spell){World.Casts++;return World.Accepted;}
}
public static class LegacySpellManager {public static void ClickRemoteLocation(Point location){World.Clicks++;}}
public static class Unit {public static bool IsAreaEffectSafe(string spell,Point location)=>World.Safe;}
public static class Logger {public static void Write(string format,params object[] args){}}
public static class StyxWoW {public static Player Me;}
public sealed class Player {
 public Point Location=new Point();
 public WoWSpell CurrentPendingCursorSpell {get{World.PendingReads++;return World.Pending;}}
""";
    private const string Cases = """
 private sealed class Failure(string reason):Exception(reason){}
 private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
 public static void Run(){
  int passed=0,assertions=0,unexpected=0,total=0;
  foreach(string mode in new[]{"refused-ready","refused-empty","refused-foreign","success-ready","success-delayed",
   "cannot-cast","requirements-false","unsafe-area","out-of-range","missing-location","zero-range"}){
   total++;Composite behavior=null;
   try{
    World.Reset(mode);var point=new Point{X=mode=="out-of-range"||mode=="zero-range"?100:1};
    LocationRetriever location=mode=="missing-location"?null:new LocationRetriever(_=>point);
    behavior=CastOnGround("Selected",location,_=>World.Requirements);behavior.Start(null);
    RunStatus status=behavior.Tick(null);
    bool admission=mode=="cannot-cast"||mode=="requirements-false"||mode=="unsafe-area"||mode=="out-of-range"||mode=="missing-location";
    if(admission){
     Check(status==RunStatus.Failure&&World.Casts==0&&World.Clicks==0&&World.PendingReads==0,"failed admission crossed a cast/cursor/terrain boundary");
    }else if(mode.StartsWith("refused-")){
     Check(status==RunStatus.Failure,"false Cast receipt did not terminate this sequence as Failure");
     Check(World.Casts==1&&World.Clicks==0&&World.PendingReads==0,"refused submission acquired pending-cursor or terrain authority");
    }else{
     if(mode=="success-delayed"){
      Check(status==RunStatus.Running&&World.Casts==1&&World.Clicks==0,"healthy delayed cursor no longer waits without recasting");
      World.Pending=new WoWSpell();status=behavior.Tick(null);
     }
     Check(status==RunStatus.Success&&World.Casts==1&&World.Clicks==1,"healthy ground submission lost its existing completion behavior");
    }
    passed++;Console.WriteLine("PASS ground cast submission: "+mode);
   }catch(Failure e){assertions++;Console.Error.WriteLine("FAIL ground cast submission: "+mode+": "+e.Message);}
   catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR ground cast submission: "+mode+": "+e);}
   finally{behavior?.Stop(null);}
  }
  Console.WriteLine($"Ground cast submission scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; complete tracked factory and real TreeSharp; controlled backend/world; no native/game/server execution.");
  if(assertions+unexpected!=0)throw new InvalidOperationException("Ground cast submission regression");
 }
""";
}
