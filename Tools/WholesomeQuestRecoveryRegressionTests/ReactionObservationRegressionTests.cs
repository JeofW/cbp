using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Actual public reaction selection/cache and native adapter, controlled executor/bytes.
// No native game call or ABI emulation: emitted instruction sequence is asserted unchanged.
internal static class ReactionObservationRegressionTests
{
 [ModuleInitializer] internal static void Run()
 {
  string root=Root();var unit=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,"Styx/WoWInternals/WoWObjects/WoWUnit.cs"))).GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c=>c.Identifier.ValueText=="WoWUnit");
  string members=string.Join("\n",unit.Members.Where(m=>m is MethodDeclarationSyntax method&&(method.Identifier.ValueText is "GetReactionTowards" or "GetReactionTowardsNative"||method.Identifier.ValueText.StartsWith("Reaction"))
   ||m is ClassDeclarationSyntax cls&&cls.Identifier.ValueText.StartsWith("Reaction")
   ||m is FieldDeclarationSyntax field&&field.Declaration.Variables.Any(v=>v.Identifier.ValueText.StartsWith("_reaction")||v.Identifier.ValueText is "HardcodedReactions" or "HordeReactionsByEntry" or "HordeReactionsByFaction")).Select(m=>m.ToString()));
  string source=Prefix+"public class WoWUnit {"+Leaves+members+"}\n"+Cases;
  var references=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Append(typeof(Styx.Helpers.ObservationUnavailableException).Assembly.Location).Distinct().Select(p=>MetadataReference.CreateFromFile(p));
  var compilation=CSharpCompilation.Create("ReactionProbe_"+Guid.NewGuid().ToString("N"),new[]{CSharpSyntaxTree.ParseText(source)},references,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
  using var output=new MemoryStream();var result=compilation.Emit(output);if(!result.Success)throw new InvalidOperationException(string.Join("; ",result.Diagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error)));
  var assembly=Assembly.Load(output.ToArray());try{assembly.GetType("ReactionCases",true)!.GetMethod("Run")!.Invoke(null,null);}catch(TargetInvocationException error)when(error.InnerException!=null){ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
 }
 private static string Root(){for(var d=new DirectoryInfo(AppContext.BaseDirectory);d!=null;d=d.Parent)if(File.Exists(Path.Combine(d.FullName,"CopilotBuddy.csproj")))return d.FullName;throw new InvalidOperationException("checkout required");}
 private const string Prefix="""
#nullable disable
using System;using System.Linq;using System.Collections.Generic;using System.Reflection;using System.Threading;using Styx;using Styx.Helpers;using Styx.Logic.Combat;
public static class State{public static uint Value;public static int Calls,Bytes=4;public static Exception Error;public static Action During,DuringRead;public static List<string> Lines=new();}
public static class ObjectManager{public static Memory Wow;public static ExecutorRand Executor;public static WoWPlayer Me;}
public static class StyxWoW{public static WoWPlayer Me=>ObjectManager.Me;public static Memory Memory=>ObjectManager.Wow;}
public sealed class Memory{public IntPtr ProcessHandle=new(1);public byte[] ReadBytes(uint address,int count){var bytes=BitConverter.GetBytes(State.Value).Take(State.Bytes).ToArray();var callback=State.DuringRead;State.DuringRead=null;callback?.Invoke();return bytes;}public T Read<T>(uint address)=>(T)(object)State.Value;public IDisposable TemporaryCacheState(bool state)=>new Scope();}
public sealed class Scope:IDisposable{public void Dispose(){}}
public sealed class ExecutorRand{public bool IsOpen=true,IsInitialized=true;public Memory Memory;public uint FrameCount=1,ReturnPointer=8192;public long ExecutionGeneration;public object AssemblyLock=new();public void Clear(){State.Lines.Clear();}public void AddLine(string value)=>State.Lines.Add(value);public void Execute(){ExecutionGeneration++;State.Calls++;if(State.Error!=null)throw State.Error;var callback=State.During;State.During=null;callback?.Invoke();}}
public sealed class InjectionSEHException:Exception{public uint ExceptionCode=0xC0000005;}
public static class Logging{public static void WriteDebug(string value){}public static void WriteException(Exception error){}}
public class WoWPlayer:WoWUnit{}
""";
 private const string Leaves="""
 public uint BaseAddress=4096,Entry,FactionId,DuelTeam;public ulong Guid=1,DuelArbiterGuid;public bool IsValid=true,PlayerControlled,IsHorde,IsMe,InMyPartyOrRaid;public string Name="Fixture";public WoWPlayer ControllingPlayer;
""";
 private const string Cases="""
public static class ReactionCases{
 private sealed class Failure(string reason):Exception(reason){}
 private static WoWUnit receiver,other;
 private static void Reset(){ObjectManager.Wow=new();ObjectManager.Executor=new(){Memory=ObjectManager.Wow};ObjectManager.Me=new(){Guid=1};receiver=new(){Guid=2,BaseAddress=12288};other=new(){Guid=3,BaseAddress=16384,Entry=100};State.Value=1;State.Calls=0;State.Bytes=4;State.Error=null;State.During=State.DuringRead=null;}
 private static WoWUnitReaction Read()=>receiver.GetReactionTowards(other);
 private static void Check(bool ok,string why){if(!ok)throw new Failure(why);}
 private static void Unknown(){bool unknown=false;try{_=Read();}catch(ObservationUnavailableException){unknown=true;}Check(unknown,"unavailable reaction became an ordinary faction value");}
 public static void Run(){int passed=0,total=0,failed=0;void Case(string name,Action test){total++;Reset();try{test();passed++;Console.WriteLine("PASS reaction observation: "+name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL reaction observation: "+name+": "+e);}}
 Case("complete hostile observation retains ABI",()=>{Check(Read()==WoWUnitReaction.Hostile,"reaction changed");Check(State.Lines.SequenceEqual(new[]{"push 16384","mov ecx, 12288","call 7492032","retn"}),"native ABI changed");});
 Case("ordinary render progression does not corrupt a native reaction",()=>{State.DuringRead=()=>ObjectManager.Executor.FrameCount++;Check(Read()==WoWUnitReaction.Hostile,"render frame was mistaken for result replacement");});
 Case("new command during result read remains UNKNOWN",()=>{State.DuringRead=()=>ObjectManager.Executor.ExecutionGeneration++;Unknown();});
 Case("nested execution cannot donate its result",()=>{State.During=()=>ObjectManager.Executor.ExecutionGeneration++;Unknown();});
 Case("native VEH is UNKNOWN",()=>{State.Error=new InjectionSEHException();Unknown();});
 Case("missing executor is UNKNOWN",()=>{ObjectManager.Executor=null;Unknown();});
 Case("missing result memory is UNKNOWN",()=>{ObjectManager.Executor.Memory=null;Unknown();});
 Case("short return is UNKNOWN",()=>{State.Bytes=3;Unknown();});
 Case("out of range result is UNKNOWN",()=>{State.Value=8;Unknown();});
 Case("zero is a legitimate Hated reaction",()=>{State.Value=0;Check(Read()==WoWUnitReaction.Hated,"known zero erased");});
 Case("VEH cannot poison later healthy frame",()=>{State.Error=new InjectionSEHException();try{_=Read();}catch(ObservationUnavailableException){}ObjectManager.Executor.FrameCount++;State.Error=null;Check(Read()==WoWUnitReaction.Hostile,"cached neutral survived failure");});
 Case("known reaction expires on new frame",()=>{_=Read();ObjectManager.Executor.FrameCount++;State.Value=4;Check(Read()==WoWUnitReaction.Friendly,"indefinite faction cache");});
 Case("same-entry other GUID cannot borrow reaction",()=>{_=Read();other=new(){Guid=4,BaseAddress=20480,Entry=100};State.Value=4;Check(Read()==WoWUnitReaction.Friendly,"entry cache borrowed another actor");});
 Case("same-frame repeated owned pair is bounded",()=>{_=Read();_=Read();Check(State.Calls==1,"same frame repeated native work");});
 Case("frame zero cannot cache indefinitely",()=>{ObjectManager.Executor.FrameCount=0;_=Read();State.Value=4;Check(Read()==WoWUnitReaction.Friendly&&State.Calls==2,"zero epoch reused");});
 Case("target changes during execute cannot publish",()=>{State.During=()=>other.Guid=50;Unknown();});
 Case("receiver changes during execute cannot publish",()=>{State.During=()=>receiver.BaseAddress=20000;Unknown();});
 Case("actor replacement cannot publish",()=>{State.During=()=>ObjectManager.Me=new(){Guid=1};Unknown();});
 Case("executor replacement cannot publish",()=>{State.During=()=>ObjectManager.Executor=new(){Memory=ObjectManager.Wow};Unknown();});
 Case("memory replacement cannot publish",()=>{State.During=()=>ObjectManager.Wow=new();Unknown();});
 Case("invalid participant never invokes native",()=>{other.IsValid=false;Unknown();Check(State.Calls==0,"invalid target invoked native");});
 foreach(bool wrapped in new[]{false,true})Case("cancellation preserved/"+wrapped,()=>{var signal=new OperationCanceledException("owned cancel");State.Error=wrapped?new TargetInvocationException(signal):signal;Exception caught=null;try{_=Read();}catch(Exception e){caught=e;}Check(ReferenceEquals(caught,signal),"cancel swallowed/replaced");});
 foreach(Exception signal in new Exception[]{new ThreadInterruptedException("stop"),new InvalidProcessException("process"),new InvalidExecutorException("executor")})foreach(bool wrapped in new[]{false,true})Case("control signal preserved/"+signal.GetType().Name+"/"+wrapped,()=>{State.Error=wrapped?new TargetInvocationException(signal):signal;Exception caught=null;try{_=Read();}catch(Exception e){caught=e;}Check(ReferenceEquals(caught,signal),"control signal replaced");});
 Case("same-frame repeated UNKNOWN invokes native once",()=>{State.Error=new InjectionSEHException();Unknown();Unknown();Check(State.Calls==1,"unbounded failed native work");});
 Case("same-frame participant GUID mutation expires cache",()=>{_=Read();other.Guid++;State.Value=4;Check(Read()==WoWUnitReaction.Friendly&&State.Calls==2,"mutated GUID borrowed cache");});
 Case("same-frame actor replacement expires cache",()=>{_=Read();ObjectManager.Me=new(){Guid=1};State.Value=4;Check(Read()==WoWUnitReaction.Friendly&&State.Calls==2,"replaced actor borrowed cache");});
 Case("frame advances during Execute binds completed epoch",()=>{State.During=()=>ObjectManager.Executor.FrameCount++;_=Read();_=Read();Check(State.Calls==1,"completed epoch was not cached");});
 Case("zero return pointer remains UNKNOWN",()=>{ObjectManager.Executor.ReturnPointer=0;Unknown();});
 foreach(bool uninitialized in new[]{false,true})Case("closed or uninitialized executor remains fatal/"+uninitialized,()=>{if(uninitialized)ObjectManager.Executor.IsInitialized=false;else ObjectManager.Executor.IsOpen=false;bool fatal=false;try{_=Read();}catch(InvalidExecutorException){fatal=true;}Check(fatal&&State.Calls==0,"unusable executor invoked native or became optional unknown");});
 Case("closed process remains fatal",()=>{ObjectManager.Wow.ProcessHandle=IntPtr.Zero;bool fatal=false;try{_=Read();}catch(InvalidProcessException){fatal=true;}Check(fatal,"closed process became neutral/optional unknown");});
 Console.WriteLine($"Reaction observation: {passed}/{total}; failures={failed}; actual selector/cache/native adapter; controlled executor and memory leaves; no native game call.");if(failed!=0)throw new InvalidOperationException("reaction regressions");
 }
}
""";
}
