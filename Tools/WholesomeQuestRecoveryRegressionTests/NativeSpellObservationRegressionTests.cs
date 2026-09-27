using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Exact LocalPlayer observation and SpellManager terrain request methods.
// Fixed memory vectors and return words come from the retained build12340
// getter/targeting/terrain instruction evidence. No native code/game executes.
internal static class NativeSpellObservationRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        var player = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,
            "Styx/WoWInternals/WoWObjects/LocalPlayer.cs"))).GetRoot();
        var spell = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,
            "Styx/Logic/Combat/SpellManager.cs"))).GetRoot();
        string playerFields = string.Join("\n", player.DescendantNodes().OfType<FieldDeclarationSyntax>()
            .Where(f => f.Declaration.Variables.Any(v => v.Identifier.ValueText.StartsWith("SpellTarget", StringComparison.Ordinal) ||
                v.Identifier.ValueText.StartsWith("PendingCursorSpell", StringComparison.Ordinal))).Select(f => f.ToString()));
        string property = player.DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Single(p => p.Identifier.ValueText == "CurrentPendingCursorSpell").ToString();
        string aliases = string.Join("\n", player.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(m => m.Identifier.ValueText == "HasPendingSpell").Select(m => m.ToString()));
        string click = spell.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == "ClickRemoteLocation").ToString();
        string types = spell.DescendantNodes().OfType<StructDeclarationSyntax>()
            .Single(s => s.Identifier.ValueText == "TerrainClickInfo").ToString() + "\n" +
            spell.DescendantNodes().OfType<EnumDeclarationSyntax>()
            .Single(s => s.Identifier.ValueText == "MouseButton").ToString() + "\n" +
            spell.DescendantNodes().OfType<FieldDeclarationSyntax>()
            .Single(f => f.Declaration.Variables.Any(v => v.Identifier.ValueText == "Spell_C__HandleTerrainClick")).ToString();
        string directory = Path.Combine(Path.GetTempPath(), "cb-native-spell-observation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "Probe.cs"), Prefix +
                "public sealed class PlayerProbe {public ProbeMemory? Memory;\n" + playerFields + property + aliases + "}\n" +
                "public static class SpellProbe {\n" + types + click + "}\n" + Cases);
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { directory })!;
            var result = (CompilerResults)compilerType.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException(string.Join("; ", errors.Select(e => e.ToString())));
            var assembly = (Assembly)compilerType.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            try { assembly.GetType("NativeSpellCases", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException e) when (e.InnerException != null)
            { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
        }
        finally { Directory.Delete(directory, true); }
    }
    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "CopilotBuddy.csproj"))) return directory.FullName;
        throw new InvalidOperationException("Tracked checkout required");
    }
    private const string Prefix = """
using System;using System.Collections.Generic;using System.Linq;using System.Globalization;
public struct WoWPoint {public float X,Y,Z;}
public sealed class WoWSpell {
 public int Id;public string Name="Selected";
 public static int Lookups;
 public static WoWSpell FromId(int id){Lookups++;return new WoWSpell{Id=id};}
}
public sealed class ProbeMemory {
 public readonly Dictionary<uint,uint> Words=new Dictionary<uint,uint>();
 public readonly List<uint> Reads=new List<uint>();
 public bool AllocationFails,ThrowRead;public int Allocations,Writes,Frees;
 public uint AllocateMemory(int size){Allocations++;return AllocationFails?0U:0x6000U;}
 public void Write<T>(uint address,T value){if(address!=0x6000U)throw new InvalidOperationException("wrong allocation");Writes++;}
 public void FreeMemory(uint address){if(address!=0x6000U)throw new InvalidOperationException("wrong release");Frees++;}
 public T Read<T>(uint address){
  Reads.Add(address);if(ThrowRead)throw new InvalidOperationException("controlled read failure");
  uint value;Words.TryGetValue(address,out value);
  if(typeof(T)==typeof(uint))return (T)(object)value;
  if(typeof(T)==typeof(int))return (T)(object)unchecked((int)value);
  if(typeof(T)==typeof(byte))return (T)(object)(byte)value;
  throw new InvalidOperationException("unsupported observation type "+typeof(T));
 }
 public IDisposable TemporaryCacheState(bool enabled)=>new Cache();
 private sealed class Cache:IDisposable {public void Dispose(){}}
}
public sealed class ExecutorRand {
 public ProbeMemory Memory=new ProbeMemory();public object AssemblyLock=new object();
 public uint ReturnPointer=0x2000;public int Executions;public bool ThrowExecute;
 public readonly List<string> Lines=new List<string>();
 public void Clear(){Lines.Clear();}
 public void AddLine(string format,params object[] args){Lines.Add(string.Format(CultureInfo.InvariantCulture,format,args));}
 public void Execute(){Executions++;if(ThrowExecute)throw new InvalidOperationException("controlled executor failure");}
}
public static class ObjectManager {public static ExecutorRand? Executor;}
public static class StyxWoW {
 public static ProbeMemory Memory=new ProbeMemory();public static int Resets;
 public static void ResetAfk(){Resets++;}
}
public static class Logging {
 public static void WriteDebug(string format,params object[] args){}
 public static void WriteException(Exception exception){}
}
""";
    private const string Cases = """
public static class NativeSpellCases {
 private sealed class Failure(string message):Exception(message){}
 private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
 public static void Run(){
  int passed=0,assertions=0,unexpected=0,total=0;
  void Case(string name,Action body){
   total++;try{body();passed++;Console.WriteLine("PASS native spell observation: "+name);}
   catch(Failure e){assertions++;Console.Error.WriteLine("FAIL native spell observation: "+name+": "+e.Message);}
   catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR native spell observation: "+name+": "+e);}
  }
  foreach(string mode in new[]{"no-memory","no-pending","legacy-only","actual-pending","legacy-conflict","zero-id","negative-id","pointer-overflow","alias-id","alias-name","alias-object"}){
   Case("pending/"+mode,()=>{
    var memory=new ProbeMemory();var player=new PlayerProbe{Memory=memory};WoWSpell.Lookups=0;
    uint pointer=mode=="no-pending"||mode=="legacy-only"?0U:0x30000U;
    int id=mode=="zero-id"?0:mode=="negative-id"?-1:12345;
    memory.Words[0xD3F4E4]=mode=="pointer-overflow"?0xFFFFFFF0U:pointer;
    memory.Words[0x30020]=unchecked((uint)id);
    bool legacy=mode=="legacy-only"||mode=="legacy-conflict"||mode=="zero-id"||mode=="negative-id";
    memory.Words[0xCEC1CC]=legacy?1U:0U;memory.Words[0xCEC1D0]=legacy?999U:0U;
    if(mode=="no-memory")player.Memory=null;
    if(mode.StartsWith("alias-")){
     bool matching=mode=="alias-id"?player.HasPendingSpell(12345):mode=="alias-name"?player.HasPendingSpell("Selected"):player.HasPendingSpell(new WoWSpell{Id=12345});
     Check(matching,"matching current native spell was not observed");
     Check(!player.HasPendingSpell(999)&&!player.HasPendingSpell("Other")&&!player.HasPendingSpell((WoWSpell)null),"foreign/absent spell matched");
    }else{
     var spell=player.CurrentPendingCursorSpell;
     bool present=mode=="actual-pending"||mode=="legacy-conflict";
     Check(present?spell!=null&&spell.Id==12345:spell==null,"pending result used unrelated fields or missed native pointer-relative identity");
     Check(WoWSpell.Lookups==(present?1:0),"absent/invalid observation triggered a spell lookup");
    }
    Check(!memory.Reads.Contains(0x10U),"invalid pending pointer arithmetic wrapped around");
   });
  }
  foreach(uint raw in new uint[]{0,1,0x00000100,0x00000101,0x12345600,0x12345601,0xFF000000,0xFFFFFFFF}){
   Case("terrain-return/"+raw.ToString("X8"),()=>{
    var executor=new ExecutorRand();ObjectManager.Executor=executor;StyxWoW.Memory=executor.Memory;StyxWoW.Resets=0;
    executor.Memory.Words[executor.ReturnPointer]=raw;
    bool result=SpellProbe.ClickRemoteLocation(new WoWPoint{X=1,Y=2,Z=3});
    Check(result==((raw&255U)!=0),"undefined high return-register bits changed the AL boolean result");
    Check(executor.Lines.SequenceEqual(new[]{"push 24576","call 8438592","add esp, 4","retn"}),"native request call/stack convention changed");
    Check(executor.Executions==1&&executor.Memory.Writes==1&&executor.Memory.Frees==1&&StyxWoW.Resets==1,"request lifetime/cleanup changed");
   });
  }
  foreach(string mode in new[]{"no-executor","allocation-refused","execute-exception","read-exception"}){
   Case("terrain-failure/"+mode,()=>{
    var executor=new ExecutorRand();ObjectManager.Executor=mode=="no-executor"?null:executor;StyxWoW.Memory=executor.Memory;StyxWoW.Resets=0;
    executor.Memory.AllocationFails=mode=="allocation-refused";executor.ThrowExecute=mode=="execute-exception";executor.Memory.ThrowRead=mode=="read-exception";
    Check(!SpellProbe.ClickRemoteLocation(new WoWPoint()),"failed request became successful");
    Check(executor.Memory.Frees==(mode=="execute-exception"||mode=="read-exception"?1:0),"allocation cleanup changed on failure");
   });
  }
  Console.WriteLine($"Native spell observation scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; actual property and complete terrain request; controlled build12340 memory/return-register vectors; no native/game execution.");
  if(assertions+unexpected!=0)throw new InvalidOperationException("Native spell observation regression");
 }
}
""";
}
