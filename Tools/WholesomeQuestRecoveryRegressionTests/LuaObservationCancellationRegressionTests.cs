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
        string[] methods = { "GetReturnValues", "GetObservedReturnValues", "GetReturnValuesCore", "EmitCursorItemGuard", "GetReturnVal", "IsLuaIntegerType", "ParseInteger",
            "ReadObservedLuaString", "ReadObservedLuaWord", "ReadObservedLuaValues", "BuildObservedReturnScript" };
        string owner = string.Join("\n", syntax.DescendantNodes().OfType<FieldDeclarationSyntax>()
            .Where(value => value.Declaration.Variables.Any(field => field.Identifier.ValueText is "_returnBuffer" or "_luaBuffer" or "ObservedReturnLimit" or "ObservedStringLimit")))
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
using System;using System.Collections.Generic;using System.Globalization;using System.Reflection;using System.Linq;
using System.Runtime.ExceptionServices;using System.Text;using System.Threading;
using Styx.Helpers;using Styx.Patchables; namespace LuaTransportProbe {
public static class Faults {
 public static string Stage;public static Exception Signal;public static bool Fired;public static int Executes;
 public static uint NextAddress,ResultAddress;public static bool Strict,ChangedWorld,ChangedHandle,BadClear,BadArgument;
 public static int Status,Count;public static readonly Dictionary<uint,byte[]> Storage=new();
 public static void At(string stage){if(Stage!=stage)return;Stage=null;Fired=true;ExceptionDispatchInfo.Capture(Signal).Throw();}
}
public sealed class AllocationScope:IDisposable {
 public void Dispose()=>Faults.At("cache-dispose");
}
public sealed class Memory {
 public IntPtr ProcessHandle=new IntPtr(1);
 public byte[] ReadBytes(uint pointer,int count){
  if(pointer==(uint)GlobalOffsets.LuaState){Faults.At("state");return BitConverter.GetBytes(4096U);}
  if(pointer==8192){Faults.At("status");return BitConverter.GetBytes(Faults.Status);}
  if(pointer==Faults.ResultAddress){Faults.At(Faults.Executes==0?"result-clear":"count");return BitConverter.GetBytes(Faults.Executes==0?(Faults.BadClear?7:0):Faults.Count);}
  if(pointer>Faults.ResultAddress&&pointer<Faults.ResultAddress+260){Faults.At("pointer");return BitConverter.GetBytes(32768U);}
  if(pointer>=32768&&pointer<33792){Faults.At("string");var data=new byte[count];if(pointer==32768){data[0]=49;data[1]=55;}return data;}
  Faults.At("argument-verification");if(!Faults.Storage.TryGetValue(pointer,out var value))return null;
  var reply=value.Take(count).ToArray();if(Faults.BadArgument&&reply.Length>0)reply[0]^=1;return reply;
 }
 public uint ReadState(uint pointer){Faults.At("state");return 4096;}
 public T Read<T>(uint pointer){if(pointer==8192){Faults.At("status");return (T)(object)0;}return (T)(object)ReadState(pointer);}
 public T Read<T>(IntPtr pointer){Faults.At("status");return (T)(object)0;}
 public string ReadString(uint pointer){Faults.At("string");return "17";}
 public IDisposable TemporaryCacheState(bool enabled){Faults.At("cache");return new AllocationScope();}
}
public static class RecoveryActions {
 public static bool Allow=true;public static int Entries;public static string LastScript;
 public static bool BeforeLuaSubmission(string script){Entries++;LastScript=script;Faults.At("recovery-marker");return Allow;}
 public static void RethrowControlFlow(Exception error)=>Styx.Logic.Combat.RecoveryActions.RethrowControlFlow(error);
}
public sealed class ExecutorRand {
 public readonly object AssemblyLock=new object();public readonly Memory Memory=new Memory();public uint ReturnPointer=>8192;
 public void Clear()=>Faults.At("clear");
 public void AddLine(string text,params object[] values)=>Faults.At("emit");
 public void Execute(){Faults.Executes++;Faults.At("execute");if(Faults.ChangedWorld)ObjectManager.Wow=new Memory();if(Faults.ChangedHandle)ObjectManager.Wow.ProcessHandle=new IntPtr(2);}
}
public sealed class AllocatedMemory:IDisposable {
 private readonly bool result;public uint Address{get;}
 public AllocatedMemory(int size){result=size==4000;Faults.At(result?"result-allocation":"allocation");Address=Faults.NextAddress;Faults.NextAddress+=8192;if(result)Faults.ResultAddress=Address;}
 public void WriteBytes(int offset,byte[] bytes){Faults.At(result?"result-write":"argument-write");Faults.Storage[Address]=bytes.ToArray();}
 public T Read<T>(int offset){Faults.At(offset==0?"count":"pointer");return typeof(T)==typeof(int)?(T)(object)2:(T)(object)32768U;}
 public void Dispose(){if(!result)Faults.At("argument-dispose");else if(Faults.Strict)Faults.At("observed-result-dispose");}
}
public static class ObjectManager {public static ExecutorRand Executor=new ExecutorRand();public static Memory Wow=new Memory();}
public static class StyxWoW {public static Memory Memory=>ObjectManager.Wow;}
public static class Logging {public static void WriteDebug(string text,params object[] values){}}
""";

    private const string Cases = """
public static class Cases {
 private sealed class Failure(string text):Exception(text){}
 private static void Check(bool condition,string text){if(!condition)throw new Failure(text);}
 private static void Reset(){Faults.Stage=null;Faults.Signal=null;Faults.Fired=false;Faults.Executes=0;Faults.Strict=false;Lua.Reset();Faults.NextAddress=16384;Faults.ResultAddress=0;Faults.ChangedWorld=Faults.ChangedHandle=Faults.BadClear=Faults.BadArgument=false;Faults.Status=0;Faults.Count=2;Faults.Storage.Clear();ObjectManager.Executor=new ExecutorRand();ObjectManager.Wow=new Memory();RecoveryActions.Allow=true;RecoveryActions.Entries=0;RecoveryActions.LastScript=null;}
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
  Case("observed transport healthy",()=>{Faults.Strict=true;Check(Lua.GetObservedReturnValues("return 17,17").SequenceEqual(new[]{"17","17"})&&Faults.Executes==1,"complete observed transport failed");});
  foreach(string stage in new[]{"state","allocation","argument-write","result-allocation","result-write","result-clear","argument-verification","clear","emit","recovery-marker","execute","cache","status","count","pointer","string","cache-dispose","argument-dispose","observed-result-dispose"}){
   foreach(string kind in new[]{"cancel","wrapped-cancel","interrupt","wrapped-interrupt"})
    Case("observed/"+stage+"/"+kind,()=>{Faults.Strict=true;Exception signal=kind.Contains("interrupt")?new ThreadInterruptedException("observed interruption"):new OperationCanceledException("observed cancellation");Faults.Stage=stage;Faults.Signal=kind.StartsWith("wrapped")?new TargetInvocationException(signal):signal;Exception caught=null;try{_=Lua.GetObservedReturnValues("return 17,17");}catch(Exception error){caught=error;}Check(Faults.Fired&&ReferenceEquals(caught,signal),"observed transport changed cancellation ownership");});
   Case("observed/"+stage+"/unavailable",()=>{Faults.Strict=true;Faults.Stage=stage;Faults.Signal=new InvalidOperationException("controlled read unavailable");bool unavailable=false;try{_=Lua.GetObservedReturnValues("return 17,17");}catch(ObservationUnavailableException){unavailable=true;}Check(Faults.Fired&&unavailable,"unavailable observed transport became an empty/default result");});
  }
  foreach(string change in new[]{"world","handle","missing-handle","clear","arguments"})
   Case("observed transport rejects "+change,()=>{Faults.Strict=true;Faults.ChangedWorld=change=="world";Faults.ChangedHandle=change=="handle";if(change=="missing-handle")ObjectManager.Wow.ProcessHandle=IntPtr.Zero;Faults.BadClear=change=="clear";Faults.BadArgument=change=="arguments";bool unavailable=false;try{_=Lua.GetObservedReturnValues("return 17,17");}catch(ObservationUnavailableException){unavailable=true;}Check(unavailable,"unowned or unverified observed reply published");if(change!="world"&&change!="handle")Check(Faults.Executes==0,"invalid prepared bytes reached native dispatch");});
  foreach(int status in new[]{-2,1,4})
   Case("observed failed status "+status,()=>{Faults.Strict=true;Faults.Status=status;bool unavailable=false;try{_=Lua.GetObservedReturnValues("return 17,17");}catch(ObservationUnavailableException){unavailable=true;}Check(unavailable,"failed status became observed empty result");});
  foreach(int count in new[]{-1,0,65,int.MaxValue})
   Case("observed invalid result count "+count,()=>{Faults.Strict=true;Faults.Count=count;bool unavailable=false;try{_=Lua.GetObservedReturnValues("return 17,17");}catch(ObservationUnavailableException){unavailable=true;}Check(unavailable,"malformed result count authorized a read");});
  Case("observed no-return status is known empty",()=>{Faults.Strict=true;Faults.Status=-1;Faults.Count=0;Check(Lua.GetObservedReturnValues("return").Count==0,"known empty observation lost");});
  Case("contradictory no-return status is unavailable",()=>{Faults.Strict=true;Faults.Status=-1;Faults.Count=2;bool unavailable=false;try{_=Lua.GetObservedReturnValues("return");}catch(ObservationUnavailableException){unavailable=true;}Check(unavailable,"contradictory return buffer was accepted as empty");});
  foreach(bool strict in new[]{false,true})Case("denied recovery entry prevents Lua submission / "+strict,()=>{Faults.Strict=strict;RecoveryActions.Allow=false;bool unavailable=false;var values=new List<string>();try{values=strict?Lua.GetObservedReturnValues("return 17,17"):Lua.GetReturnValues("return 17,17");}catch(ObservationUnavailableException){unavailable=true;}Check(RecoveryActions.Entries==1&&Faults.Executes==0&&(strict?unavailable:values.Count==0),"denied recovery request crossed native Lua entry");});
  Case("observed wrapper retains original submission identity",()=>{Faults.Strict=true;_=Lua.GetObservedReturnValues("return 17,17");Check(RecoveryActions.Entries==1&&RecoveryActions.LastScript=="return 17,17","wrapper changed the admitted container request identity");});
  foreach(bool wrapped in new[]{false,true})foreach(bool process in new[]{false,true})foreach(string stage in new[]{"execute","state","status","count","pointer","string"})Case("observed fatal native ownership / "+stage+" / "+process+" / "+wrapped,()=>{Faults.Strict=true;Exception signal=process?new Styx.InvalidProcessException("process lost"):new Styx.InvalidExecutorException("executor lost");Faults.Stage=stage;Faults.Signal=wrapped?new TargetInvocationException(signal):signal;Exception caught=null;try{_=Lua.GetObservedReturnValues("return 17,17");}catch(Exception error){caught=error;}Check(ReferenceEquals(caught,signal),"fatal ownership loss became optional observation");});
  Console.WriteLine($"Lua observation cancellation cases: {passed}/{cases}; assertions={failed}; unexpected={unexpected}; complete transport and conversion owners; controlled allocation/memory/executor failures; no native/game execution.");
  if(failed+unexpected!=0)throw new InvalidOperationException("Lua observation cancellation regression");
 }
}
""";
}
