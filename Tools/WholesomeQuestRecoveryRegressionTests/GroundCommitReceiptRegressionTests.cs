using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Actual complete ground factory, legacy adapter and core terrain request.
// Retained build12340 AL/argument vectors; controlled memory/executor/world.
// This proves receipt propagation, not exclusive pending-cursor/request origin.
internal static class GroundCommitReceiptRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        var routine = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,
            "runtime-snapshot/Routines/Singular wotlk/Helpers/Spell.cs"))).GetRoot();
        string factory = routine.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m =>
            m.Identifier.ValueText == "CastOnGround" && m.ParameterList.Parameters.Count == 3).ToFullString();
        var core = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,
            "Styx/Logic/Combat/SpellManager.cs"))).GetRoot();
        string click = core.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == "ClickRemoteLocation").ToFullString();
        string types = core.DescendantNodes().OfType<StructDeclarationSyntax>()
            .Single(t => t.Identifier.ValueText == "TerrainClickInfo").ToFullString() + "\n" +
            core.DescendantNodes().OfType<EnumDeclarationSyntax>()
            .Single(t => t.Identifier.ValueText == "MouseButton").ToFullString() + "\n" +
            core.DescendantNodes().OfType<FieldDeclarationSyntax>()
            .Single(f => f.Declaration.Variables.Any(v => v.Identifier.ValueText == "Spell_C__HandleTerrainClick")).ToFullString();
        var legacy = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,
            "Styx/Logic/Combat/LegacySpellManager.cs"))).GetRoot();
        string adapter = legacy.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == "ClickRemoteLocation").ToFullString();
        string directory = Path.Combine(Path.GetTempPath(), "cb-ground-commit-receipt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "Probe.cs"), Prefix +
                "public static class SpellManager {\n" + ManagerLeaves + types + click + "}\n" +
                "public static class LegacySpellManager {\n" + adapter + "}\n" +
                "public static class GroundProbe {\n" + factory + "}\n" + Cases);
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { directory })!;
            var result = (CompilerResults)compilerType.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException(string.Join("; ", errors.Select(e => e.ToString())));
            var assembly = (Assembly)compilerType.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            try { assembly.GetType("GroundCommitCases", true)!.GetMethod("Run")!.Invoke(null, null); }
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
using System;using System.Collections.Generic;using System.Linq;using System.Globalization;
using TreeSharp;using CommonBehaviors.Actions;using Action=TreeSharp.Action;
using LocalPlayer=Player;
public delegate WoWPoint LocationRetriever(object context);
public delegate bool SimpleBooleanDelegate(object context);
public struct WoWPoint {
 public float X,Y,Z;
 public static WoWPoint Empty=>default;public static WoWPoint Zero=>default;
 public static bool operator ==(WoWPoint a,WoWPoint b)=>a.X==b.X&&a.Y==b.Y&&a.Z==b.Z;
 public static bool operator !=(WoWPoint a,WoWPoint b)=>!(a==b);
 public override bool Equals(object value)=>value is WoWPoint other&&this==other;
 public override int GetHashCode()=>HashCode.Combine(X,Y,Z);
 public double Distance(WoWPoint other)=>Math.Sqrt((X-other.X)*(X-other.X)+(Y-other.Y)*(Y-other.Y)+(Z-other.Z)*(Z-other.Z));
}
public sealed class WoWSpell {public string Name="Selected";public double MaxRange=40;}
public static class World {
 public static string Mode;public static uint ReturnWord;
 public static bool Ready,Requirements,Safe,CastCompleted,Pending;
 public static int Casts,PendingReads,Resets,Exceptions;
 public static ExecutorRand Reset(string mode,uint word) {
  Mode=mode;ReturnWord=word;Ready=Requirements=Safe=CastCompleted=Pending=true;
  Casts=PendingReads=Resets=Exceptions=0;
  var executor=new ExecutorRand();ObjectManager.Executor=mode=="no-executor"?null:executor;
  StyxWoW.Memory=executor.Memory;StyxWoW.Me=new Player();SpellManager.Spells["Selected"].MaxRange=40;
  return executor;
 }
 public static void MaybeThrow(string stage){if(Mode==stage+"-exception")throw new InvalidOperationException("controlled "+stage+" failure");}
}
public sealed class ProbeMemory {
 public int Allocations,Writes,Frees;public readonly List<uint> Reads=new List<uint>();public object LastWrite;
 public uint AllocateMemory(int size){Allocations++;World.MaybeThrow("allocation");return World.Mode=="allocation-refused"?0U:0x6000U;}
 public void Write<T>(uint address,T value){if(address!=0x6000U)throw new InvalidOperationException("wrong allocation");Writes++;World.MaybeThrow("write");LastWrite=value;}
 public void FreeMemory(uint address){if(address!=0x6000U)throw new InvalidOperationException("wrong release");Frees++;World.MaybeThrow("free");}
 public T Read<T>(uint address){
  Reads.Add(address);World.MaybeThrow("read");
  if(address!=0x2000U||typeof(T)!=typeof(byte))throw new InvalidOperationException("wrong original AL observation");
  return (T)(object)(byte)World.ReturnWord;
 }
 public IDisposable TemporaryCacheState(bool enabled)=>new Cache();
 private sealed class Cache:IDisposable {public void Dispose(){}}
}
public sealed class ExecutorRand {
 public ProbeMemory Memory=new ProbeMemory();public object AssemblyLock=new object();
 public uint ReturnPointer=0x2000;public int Executions;public readonly List<string> Lines=new List<string>();
 public void Clear(){World.MaybeThrow("clear");Lines.Clear();}
 public void AddLine(string format,params object[] args){World.MaybeThrow("add");Lines.Add(string.Format(CultureInfo.InvariantCulture,format,args));}
 public void Execute(){Executions++;World.MaybeThrow("execute");}
}
public static class ObjectManager {public static ExecutorRand Executor;}
public static class StyxWoW {
 public static ProbeMemory Memory;public static Player Me;
 public static void ResetAfk(){World.Resets++;}
}
public sealed class Player {
 public ulong Guid=1;public bool IsValid=true,IsAlive=true;
 public WoWPoint Location;
 public bool HasPendingSpell(string name){World.PendingReads++;return World.Pending;}
}
public static class Logging {
 public static void WriteDebug(string format,params object[] args){}
 public static void WriteException(Exception exception){World.Exceptions++;}
}
public static class Logger {public static void Write(string format,params object[] args){}}
public static class Unit {public static bool IsAreaEffectSafe(string name,WoWPoint point)=>World.Safe;}
""";

    private const string ManagerLeaves = """
 public static Dictionary<string,WoWSpell> Spells=new Dictionary<string,WoWSpell>{{"Selected",new WoWSpell()}};
 public static bool CanCast(string name)=>World.Ready;
 public static bool Cast(string name){World.Casts++;return World.CastCompleted;}
""";

    private const string Cases = """
public static class GroundCommitCases {
 private sealed class Failure(string reason):Exception(reason){}
 private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
 private static void CheckBackend(ExecutorRand executor,string mode,bool entered,WoWPoint point){
  bool allocated=entered&&mode!="no-executor"&&mode!="allocation-refused"&&mode!="allocation-exception";
  bool executed=allocated&&(mode=="success"||mode=="execute-exception"||mode=="read-exception"||mode=="free-exception");
  Check(World.Resets==(entered?1:0),"terrain dispatch was repeated or reached after failed admission");
  Check(executor.Memory.Frees==(allocated?1:0),"terrain allocation cleanup changed");
  Check(executor.Executions==(executed?1:0),"terrain execution attempt count changed");
  if(entered&&mode=="success"){
   Check(executor.Lines.SequenceEqual(new[]{"push 24576","call 8438592","add esp, 4","retn"}),"original terrain argument/stack sequence changed");
   Check(executor.Memory.Reads.SequenceEqual(new[]{0x2000U}),"AL was not observed exactly once");
   object click=executor.Memory.LastWrite;Type type=click.GetType();
   var observed=(WoWPoint)type.GetField("Location").GetValue(click);
   Check(observed.X==point.X&&observed.Y==point.Y&&observed.Z==point.Z,
    "terrain point was flattened or changed before dispatch");
   Check((ulong)type.GetField("TargetGuid").GetValue(click)==0UL&&Convert.ToUInt32(type.GetField("Button").GetValue(click))==1U,
    "original terrain target/button changed");
  }
 }
 private static void RunGround(string mode,uint word,bool delayed){
  var executor=World.Reset(mode,word);World.Pending=!delayed;
  var point=new WoWPoint{X=1,Y=2,Z=3};
  Composite behavior=GroundProbe.CastOnGround("Selected",_=>point,_=>World.Requirements);behavior.Start(null);
  try{
   RunStatus status=behavior.Tick(null);
   if(delayed){
    Check(status==RunStatus.Running&&World.Casts==1&&World.Resets==0,"delayed pending spell no longer waits without terrain dispatch");
    World.Pending=true;status=behavior.Tick(null);
   }
   Check(status==(mode=="success"&&(word&255U)!=0?RunStatus.Success:RunStatus.Failure),
    "failed actual terrain request became successful ground completion");
   Check(World.Casts==1,"terrain result caused an extra initial spell cast");
   CheckBackend(executor,mode,true,point);
  }finally{behavior.Stop(null);}
 }
 public static void Run(){
  int passed=0,assertions=0,unexpected=0,total=0;
  void Case(string name,System.Action body){
   total++;try{body();passed++;Console.WriteLine("PASS ground commit receipt: "+name);}
   catch(Failure e){assertions++;Console.Error.WriteLine("FAIL ground commit receipt: "+name+": "+e.Message);}
   catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR ground commit receipt: "+name+": "+e);}
  }
  foreach(uint word in new uint[]{0,1,0x100,0x101,0x12345600,0x12345601,0xFF000000,0xFFFFFFFF})
  foreach(bool delayed in new[]{false,true})
   Case("actual-AL/"+word.ToString("X8")+"/"+(delayed?"delayed":"ready"),()=>RunGround("success",word,delayed));
  foreach(string mode in new[]{"no-executor","allocation-refused","allocation-exception","write-exception","clear-exception","add-exception","execute-exception","read-exception","free-exception"})
  foreach(bool delayed in new[]{false,true})
   Case(mode+"/"+(delayed?"delayed":"ready"),()=>RunGround(mode,1,delayed));
  foreach(string mode in new[]{"cannot-cast","requirements-false","unsafe-area","out-of-range","missing-location","cast-refused"}){
   Case("existing-admission/"+mode,()=>{
    var executor=World.Reset("success",1);
    World.Ready=mode!="cannot-cast";World.Requirements=mode!="requirements-false";World.Safe=mode!="unsafe-area";World.CastCompleted=mode!="cast-refused";
    var point=new WoWPoint{X=mode=="out-of-range"?100:1};
    LocationRetriever location=mode=="missing-location"?null:new LocationRetriever(_=>point);
    Composite behavior=GroundProbe.CastOnGround("Selected",location,_=>World.Requirements);behavior.Start(null);
    try{
     Check(behavior.Tick(null)==RunStatus.Failure,"failed admission became success");
     Check(World.Casts==(mode=="cast-refused"?1:0)&&World.PendingReads==0,"failed admission crossed cast/pending boundary");
     CheckBackend(executor,"success",false,point);
    }finally{behavior.Stop(null);}
   });
  }
  Case("legacy-void-signature-preserved",()=>
   Check(typeof(LegacySpellManager).GetMethod("ClickRemoteLocation").ReturnType==typeof(void),"legacy public API changed"));
  foreach(string mode in new[]{"success","no-executor","allocation-refused","execute-exception","read-exception"}){
   Case("legacy-void-call/"+mode,()=>{
    var executor=World.Reset(mode,0x12345600U);var point=new WoWPoint{X=3,Y=4,Z=5};
    LegacySpellManager.ClickRemoteLocation(point);CheckBackend(executor,mode,true,point);
   });
  }
  Console.WriteLine($"Ground commit receipt scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; complete tracked ground factory, legacy adapter and core terrain request; real TreeSharp; controlled original AL/XYZ/backend; no current native/game/server or exclusive-origin proof.");
  if(assertions+unexpected!=0)throw new InvalidOperationException("Ground commit receipt regression");
 }
}
""";
}
