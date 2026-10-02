using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Execute the complete tracked Lua transport and numeric reader. Allocations,
// memory transfers and executor calls are controlled leaves in this process;
// emitted assembly is not dispatched to a game or used as ABI evidence.
internal static class LuaObservationCancellationRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "CopilotBuddy.csproj"))) root = root.Parent;
        if (root == null) throw new InvalidOperationException("A source checkout is required.");
        var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName, "Styx/WoWInternals/Lua.cs"))).GetRoot();
        string[] methods = { "GetReturnValues", "GetReturnValuesCore", "EmitCursorItemGuard", "GetReturnVal", "IsLuaIntegerType", "ParseInteger" };
        string owner = string.Join("\n", syntax.DescendantNodes().OfType<FieldDeclarationSyntax>()
            .Where(value => value.Declaration.Variables.Any(field => field.Identifier.ValueText is "_returnBuffer" or "_luaBuffer")))
            + string.Join("\n", syntax.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Where(value => methods.Contains(value.Identifier.ValueText)).Select(value => value.ToFullString()));
        string source = Prefix + "public static class Lua {\n" + owner + "\npublic static void Reset(){_returnBuffer?.Dispose();_returnBuffer=null;}\n}\n" + Cases + "\n}";
        string directory = Path.Combine(Path.GetTempPath(), "cb-lua-cancellation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "Probe.cs"), source);
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { directory })!;
            var result = (CompilerResults)compilerType.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(value => !value.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Lua transport fixture compile: " + string.Join("; ", errors.Select(value => value.ToString())));
            var assembly = (Assembly)compilerType.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            try { assembly.GetType("LuaTransportProbe.Cases", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
            Console.WriteLine("LUA_TRANSPORT_SOURCE_SHA256=" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant());
        }
        finally { Directory.Delete(directory, true); }
    }

    private const string Prefix = """
#nullable disable
using System;using System.Collections.Generic;using System.Globalization;using System.Reflection;
using System.Runtime.ExceptionServices;using System.Text;using System.Threading;
using Styx.Helpers;using Styx.Patchables; namespace LuaTransportProbe {
public static class Faults {
 public static string Stage;public static Exception Signal;public static bool Fired;public static int Executes;
 public static void At(string stage){if(Stage!=stage)return;Stage=null;Fired=true;ExceptionDispatchInfo.Capture(Signal).Throw();}
}
public sealed class AllocationScope:IDisposable {
 public void Dispose()=>Faults.At("cache-dispose");
}
public sealed class Memory {
 public uint ReadState(uint pointer){Faults.At("state");return 4096;}
 public T Read<T>(uint pointer)=>(T)(object)ReadState(pointer);
 public T Read<T>(IntPtr pointer){Faults.At("status");return (T)(object)0;}
 public string ReadString(uint pointer){Faults.At("string");return "17";}
 public IDisposable TemporaryCacheState(bool enabled){Faults.At("cache");return new AllocationScope();}
}
public sealed class ExecutorRand {
 public readonly object AssemblyLock=new object();public readonly Memory Memory=new Memory();public IntPtr ReturnPointer=>new IntPtr(8192);
 public void Clear()=>Faults.At("clear");
 public void AddLine(string text,params object[] values)=>Faults.At("emit");
 public void Execute(){Faults.Executes++;Faults.At("execute");}
}
public sealed class AllocatedMemory:IDisposable {
 private readonly bool result;public uint Address=>16384;
 public AllocatedMemory(int size){result=size==4000;Faults.At(result?"result-allocation":"allocation");}
 public void WriteBytes(int offset,byte[] bytes)=>Faults.At(result?"result-write":"argument-write");
 public T Read<T>(int offset){Faults.At(offset==0?"count":"pointer");return typeof(T)==typeof(int)?(T)(object)2:(T)(object)32768U;}
 public void Dispose(){if(!result)Faults.At("argument-dispose");}
}
public static class ObjectManager {public static ExecutorRand Executor=new ExecutorRand();public static Memory Wow=new Memory();}
public static class StyxWoW {public static Memory Memory=>ObjectManager.Wow;}
public static class Logging {public static void WriteDebug(string text,params object[] values){}}
""";

    private const string Cases = """
public static class Cases {
 private sealed class Failure(string text):Exception(text){}
 private static void Check(bool condition,string text){if(!condition)throw new Failure(text);}
 private static void Reset(){Faults.Stage=null;Faults.Signal=null;Faults.Fired=false;Faults.Executes=0;Lua.Reset();ObjectManager.Executor=new ExecutorRand();ObjectManager.Wow=new Memory();}
 public static void Run(){
  int cases=0,passed=0,failed=0,unexpected=0;
  void Case(string name,Action work){cases++;Reset();try{work();passed++;}catch(Failure error){failed++;Console.WriteLine("FAIL Lua cancellation: "+name+": "+error.Message);}catch(Exception error){unexpected++;Console.WriteLine("ERROR Lua cancellation: "+name+": "+error);}finally{Reset();}}
  foreach(bool numeric in new[]{false,true}){
   Case("healthy/numeric="+numeric,()=>{if(numeric)Check(Lua.GetReturnVal<int>("return 17,17",0U)==17,"valid numeric result changed");else Check(Lua.GetReturnValues("return 17,17").Count==2,"valid vector result changed");Check(Faults.Executes==1,"healthy request was not executed exactly once");});
   foreach(string stage in new[]{"state","allocation","argument-write","result-allocation","result-write","clear","emit","execute","cache","status","count","pointer","string","cache-dispose","argument-dispose"}){
    foreach(string kind in new[]{"cancel","wrapped-cancel","interrupt","wrapped-interrupt"})
     Case(stage+"/"+kind+"/numeric="+numeric,()=>{
      Exception signal=kind.Contains("interrupt")?new ThreadInterruptedException("controlled Lua interruption"):new OperationCanceledException("controlled Lua cancellation");
      Faults.Stage=stage;Faults.Signal=kind.StartsWith("wrapped")?new TargetInvocationException(signal):signal;
      Exception caught=null;try{if(numeric)_=Lua.GetReturnVal<int>("return 17,17",0U);else _=Lua.GetReturnValues("return 17,17");}catch(Exception error){caught=error;}
      Check(Faults.Fired,"controlled transport boundary did not execute");Check(ReferenceEquals(caught,signal),"the owned signal became an empty/default result or another exception");
     });
    Case(stage+"/ordinary/numeric="+numeric,()=>{Faults.Stage=stage;Faults.Signal=new InvalidOperationException("ordinary unavailable transport");if(numeric)Check(Lua.GetReturnVal<int>("return 17,17",0U)==0,"ordinary failure compatibility changed");else Check(Lua.GetReturnValues("return 17,17").Count==0,"ordinary failure compatibility changed");Check(Faults.Fired,"ordinary transport boundary was not exercised");});
   }
  }
  Console.WriteLine($"Lua observation cancellation cases: {passed}/{cases}; assertions={failed}; unexpected={unexpected}; complete transport and conversion owners; controlled allocation/memory/executor failures; no native/game execution.");
  if(failed+unexpected!=0)throw new InvalidOperationException("Lua observation cancellation regression");
 }
}
""";
}
