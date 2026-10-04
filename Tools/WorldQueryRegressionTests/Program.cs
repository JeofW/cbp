using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

string root = Path.GetFullPath(args.Length == 1 ? args[0] : Directory.GetCurrentDirectory());
if (!File.Exists(Path.Combine(root, "CopilotBuddy.csproj"))) throw new ArgumentException("Run from or specify the CopilotBuddy checkout.");
Console.WriteLine("MODE actual production members; controlled external leaves; no game attached");
string Read(string path)
{
    byte[] data = File.ReadAllBytes(Path.Combine(root, path));
    Console.WriteLine("SOURCE " + path + " " + Convert.ToHexString(SHA256.HashData(data)));
    return System.Text.Encoding.UTF8.GetString(data);
}
ClassDeclarationSyntax Class(string path, string name) => CSharpSyntaxTree.ParseText(Read(path)).GetRoot()
    .DescendantNodes().OfType<ClassDeclarationSyntax>().Single(x => x.Identifier.ValueText == name);
var obj = Class("Styx/WoWInternals/WoWObjects/WoWObject.cs", "WoWObject");
var player = Class("Styx/WoWInternals/WoWObjects/LocalPlayer.cs", "LocalPlayer");
var unit = Class("Styx/WoWInternals/WoWObjects/WoWUnit.cs", "WoWUnit");
var world = Class("Styx/WoWInternals/World/GameWorld.cs", "GameWorld");
var ground = Class("Styx/Logic/Pathing/GroundTransition.cs", "GroundTransition");
var levelBot = Class("Bots/Grind/LevelBot.cs", "LevelBot");
string corpseMembers = string.Join("\n", levelBot.Members.OfType<MethodDeclarationSyntax>()
    .Where(method => method.Identifier.ValueText is "FindSafeResPoint" or "IsResPointSafe" or "GetDistanceToNearestHostile"));
string Property(ClassDeclarationSyntax type) => type.Members.OfType<PropertyDeclarationSyntax>()
    .Single(x => x.Identifier.ValueText == "IsOutdoors").ToString();
string ray = world.Members.OfType<MethodDeclarationSyntax>().Single(x => x.Identifier.ValueText == "TraceLine"
    && x.ParameterList.Parameters.Count == 5).ToString();
string flags = world.Members.OfType<EnumDeclarationSyntax>().Single(x => x.Identifier.ValueText == "CGWorldFrameHitFlags").ToString();
string batch = world.Members.OfType<MethodDeclarationSyntax>().Single(x => x.Identifier.ValueText == "MassTraceLine"
    && x.ParameterList.Parameters.Count == 4 && x.ParameterList.Parameters[1].Type!.ToString() == "CGWorldFrameHitFlags[]").ToString();
string batchWrappers = string.Join("\n", world.Members.OfType<MethodDeclarationSyntax>().Where(x => x.Identifier.ValueText == "MassTraceLine"
    && x.ParameterList.Parameters.Count == 3 && x.ParameterList.Parameters[1].Type!.ToString().StartsWith("CGWorldFrameHitFlags")));
string objectProperty = Property(obj);
string playerProperty = Property(player);
string vehicleProperty = player.Members.OfType<PropertyDeclarationSyntax>().Single(x => x.Identifier.ValueText == "InVehicle").ToString();
string movementObservation = unit.Members.OfType<MethodDeclarationSyntax>().Single(x => x.Identifier.ValueText == "TryGetMovementState").ToString();
string descriptorObservation = string.Join("\n", obj.Members.Where(member =>
    member is PropertyDeclarationSyntax property && property.Identifier.ValueText == "Memory"
    || member is MethodDeclarationSyntax method && method.Identifier.ValueText == "GetDescriptorField"
    || member is FieldDeclarationSyntax field && field.Declaration.Variables.Any(v => v.Identifier.ValueText == "DescriptorOffset")));
string mountObservation = string.Join("\n", unit.Members.Where(member =>
    member is PropertyDeclarationSyntax property && property.Identifier.ValueText is "Flags" or "Mounted" or "MountDisplayId" or "Bytes2" or "Shapeshift" or "OnTaxi"
    || member is MethodDeclarationSyntax method && method.Identifier.ValueText is "GetDescriptor" or "HasUnitFlag"));
string groundMembers = string.Join("\n", ground.Members.OfType<MethodDeclarationSyntax>()
    .Where(method => method.Modifiers.Any(SyntaxKind.StaticKeyword)))
    + string.Join("\n", ground.Members.OfType<ClassDeclarationSyntax>().Where(type=>type.Identifier.ValueText=="InteractionObservation"));
string interactionMembers = string.Join("\n", obj.Members.Where(member =>
    member is MethodDeclarationSyntax method && method.Identifier.ValueText is "Interact" or "TryInteractCore" or "TryInteractOwned"
    || member is FieldDeclarationSyntax field && field.Declaration.Variables.Any(v => v.Identifier.ValueText is "InteractVtableOffset" or "_interactTimer")));
string source = Boundary.Prefix + "namespace Styx.WoWInternals.WoWObjects { public class WoWObject {"
    + Boundary.ObjectLeaves + GroundNativeProbe.ObjectLeaves + objectProperty + descriptorObservation + interactionMembers + "public bool SubmitInteraction()=>TryInteractCore(false);"
    + "} public class WoWUnit:WoWObject { public bool Dead,IsMoving,IsQuestGiver,IsHostile=true;public float MyAggroRange=20; } public class LocalPlayer:WoWUnit {public uint MapId=530;public WoWPoint CorpsePoint;" + playerProperty + vehicleProperty + GroundNativeProbe.PlayerLeaves + movementObservation + mountObservation + "}}\n"
    + "namespace Styx.WoWInternals.World { public static class GameWorld {" + flags + ray + batch + batchWrappers + Boundary.BatchFallback
    + " public static bool ReadRay(WoWPoint from,WoWPoint to,out WoWPoint hit)=>TraceLine(from,to,1f,CGWorldFrameHitFlags.HitTestGroundAndStructures,out hit); public static bool IsInLineOfSight(WoWPoint from,WoWPoint to)=>GroundSight.Clear; }}\n"
    + "namespace Styx.Logic.Pathing { public static class GroundTransition {" + groundMembers + "}}\n"
    + "namespace Bots.Grind {public static class LevelBot {" + corpseMembers + "public static WoWPoint Find()=>FindSafeResPoint();}}\n"
    + GroundNativeProbe.Leaves + Boundary.Cases + GroundNativeProbe.Cases + CorpseSearchProbe.Leaves;
var trees = new List<SyntaxTree> { CSharpSyntaxTree.ParseText(source) };
trees.Add(CSharpSyntaxTree.ParseText(Read("Styx/Helpers/AllocatedMemory.cs")));
trees.Add(CSharpSyntaxTree.ParseText(Read("Styx/WoWInternals/World/WorldLine.cs")));
trees.Add(CSharpSyntaxTree.ParseText(Read("Styx/Patchables/GlobalOffsets.cs")));
trees.Add(CSharpSyntaxTree.ParseText(Read("Styx/Offsets/GlobalOffsets.cs")));
string helper = "Styx/WoWInternals/World/WorldQueryObservation.cs";
trees.Add(CSharpSyntaxTree.ParseText(Read(helper)));
trees.Add(CSharpSyntaxTree.ParseText(Read("Styx/Logic/Pathing/GroundTransitionContext.cs")));
trees.Add(CSharpSyntaxTree.ParseText(Read("Styx/Logic/GrindSafetyPolicy.cs")));
trees.Add(CSharpSyntaxTree.ParseText(Read("Styx/Offsets/WoWUnitFields.cs")));
trees.Add(CSharpSyntaxTree.ParseText(Read("Styx/Offsets/UnitFlags.cs")));
trees.Add(CSharpSyntaxTree.ParseText(Read("Styx/ShapeshiftForm.cs")));
string trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? throw new InvalidOperationException("Reference set unavailable.");
var references = trusted.Split(Path.PathSeparator).Distinct().Select(x => MetadataReference.CreateFromFile(x));
var compilation = CSharpCompilation.Create("WorldProbe_" + Guid.NewGuid().ToString("N"), trees, references,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
using var output = new MemoryStream();
var emitted = compilation.Emit(output);
if (!emitted.Success) throw new InvalidOperationException("FIXTURE COMPILE: " + string.Join("; ", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
try { Assembly.Load(output.ToArray()).GetType("WorldCases", true)!.GetMethod("Run")!.Invoke(null, null); }
catch (TargetInvocationException error) when (error.InnerException != null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); }

internal static class Boundary
{
    internal const string ObjectLeaves = "public uint BaseAddress=4096; public ulong Guid=1;public bool IsValid=true,IsAlive=true,IsGhost;";
    internal const string Prefix = """
#nullable disable
using System;using System.Collections.Generic;using System.Collections.Specialized;using System.Linq;using System.Reflection;using System.Threading;
using GreenMagic;using Styx;using Styx.Helpers;using Styx.Logic;using Styx.Logic.Combat;using Styx.Logic.Pathing;
using Styx.Offsets;using Styx.WoWInternals;using Styx.WoWInternals.World;using Styx.WoWInternals.WoWObjects;
public static class Probe {
 public static byte Value;public static int Bytes,Calls;public static Exception Error;public static Action During,DuringRead;
 public static string[] LuaValues;public static WoWPoint Hit;public static readonly List<string> Instructions=new();
 public static Action Allocate,Write;public static Exception FreeError;public static bool FreeSucceeds=true;public static uint Allocation=12288;
 public static readonly List<Memory> Freed=new();
 public static string Stage;public static Action StageAction;
 public static void At(string stage){if(stage!=Stage)return;var action=StageAction;Stage=null;StageAction=null;action?.Invoke();}
 public static bool Batch;public static byte[] BatchHits;public static WoWPoint[] BatchPoints;public static bool FallbackUsed,ShortBatchHits,ShortBatchPoints;
 public static void Execute(){Calls++;if(Error!=null)throw Error;if(Instructions.Contains("call eax")){GroundProbe.Interactions++;GroundProbe.InteractionInstructions=Instructions.ToArray();}var action=During;During=null;action?.Invoke();}
 public static void AfterRead(){var action=DuringRead;DuringRead=null;action?.Invoke();}
}
namespace Styx {
 public static class StyxWoW {public static LocalPlayer Me=>ObjectManager.Me;public static Memory Memory=>ObjectManager.Wow;public static void ResetAfk()=>Probe.At("afk");}
 public class InvalidProcessException:Exception{public InvalidProcessException(string message):base(message){}}
 public class InvalidExecutorException:Exception{public InvalidExecutorException(string message):base(message){}}
}
namespace GreenMagic {
 public sealed class Memory {
  public int ProcessId=1;public IntPtr ProcessHandle=new(1);
  public uint AllocateMemory(int count){var callback=Probe.Allocate;Probe.Allocate=null;callback?.Invoke();return Probe.Allocation;}
  public bool FreeMemory(uint address){Probe.Freed.Add(this);if(Probe.FreeError!=null)throw Probe.FreeError;return Probe.FreeSucceeds;}
  public void Write<T>(uint address,T value)where T:struct{var callback=Probe.Write;Probe.Write=null;callback?.Invoke();}
  public void WriteBytes(uint address,byte[] value){}
  public byte[] ReadBytes(uint address,int count){byte[] result;if(GroundProbe.TryMovementBytes(address,out result)||GroundProbe.TryDescriptorBytes(address,out result))return result;if(Probe.Batch){result=address==Probe.Allocation?Probe.BatchHits.Take(Math.Max(0,count-(Probe.ShortBatchHits?1:0))).ToArray():Probe.BatchPoints.SelectMany(p=>BitConverter.GetBytes(p.X).Concat(BitConverter.GetBytes(p.Y)).Concat(BitConverter.GetBytes(p.Z))).Take(Math.Max(0,count-(Probe.ShortBatchPoints?1:0))).ToArray();}else if(count==12){result=BitConverter.GetBytes(Probe.Hit.X).Concat(BitConverter.GetBytes(Probe.Hit.Y)).Concat(BitConverter.GetBytes(Probe.Hit.Z)).Take(Probe.Bytes<0?11:12).ToArray();}else result=Enumerable.Repeat(Probe.Value,Math.Max(0,Math.Min(count,Probe.Bytes<0?count:Probe.Bytes))).ToArray();Probe.AfterRead();return result;}
  public unsafe void ReadBytes(uint address,void* output,int count){var bytes=ReadBytes(address,count);for(int i=0;i<bytes.Length;i++)((byte*)output)[i]=bytes[i];}
  public T Read<T>(uint address){if(GroundProbe.TryDescriptorBytes(address,out var bytes)){if(bytes.Length!=4)throw new InvalidOperationException("incomplete controlled typed read");if(typeof(T)==typeof(BitVector32))return (T)(object)new BitVector32(BitConverter.ToInt32(bytes,0));return typeof(T)==typeof(int)?(T)(object)BitConverter.ToInt32(bytes,0):(T)(object)BitConverter.ToUInt32(bytes,0);}Probe.AfterRead();return typeof(T)==typeof(WoWPoint)?(T)(object)Probe.Hit:(T)(object)Probe.Value;}
  public IDisposable TemporaryCacheState(bool enabled){if(GroundProbe.MovementError!=null)throw GroundProbe.MovementError;return new EmptyScope();}
 }
 public sealed class EmptyScope:IDisposable{public void Dispose(){}}
 public sealed class ExecutorRand {
  public Memory Memory;public bool IsOpen=true,IsInitialized=true;public uint ReturnPointer=8192,FrameCount=1;public long ExecutionGeneration;
  public object AssemblyLock=new();public void Clear(){Probe.At("clear");Probe.Instructions.Clear();}
  public void AddLine(string format,params object[] args){Probe.At("instruction");Probe.Instructions.Add(string.Format(format,args));}public void Execute(){ExecutionGeneration++;Probe.Execute();}
 }
 public class InjectionSEHException:Exception{}
 public static class FastSize<T>where T:struct{public static readonly int Size=System.Runtime.InteropServices.Marshal.SizeOf<T>();}
}
namespace Styx.Helpers {
 public sealed class ObservationUnavailableException:InvalidOperationException {public string Observation;public ObservationUnavailableException(string observation,string reason):base(reason){Observation=observation;}}
 public sealed class WaitTimer{public WaitTimer(TimeSpan duration){}public bool IsFinished=>true;public void Reset()=>Probe.At("timer");}
 public static class Logging{public static void WriteDebug(string value,params object[] args)=>Probe.At("log");public static void Write(string value,params object[] args){}public static void WriteException(Exception e){}}
}
namespace Styx.Logic.Combat {
 public static class RecoveryActions{public static void RethrowControlFlow(Exception error){while(error is TargetInvocationException{InnerException:not null} wrapped)error=wrapped.InnerException;if(error is OperationCanceledException or ThreadInterruptedException or InvalidProcessException or InvalidExecutorException)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();}}
}
namespace Styx.Logic.BehaviorTree {public static class TreeRoot {public static object RunIdentity=new(),Current=new();public static bool IsRunning=true;}}
namespace Styx.Logic.Pathing {
 public record struct WoWPoint(float X,float Y,float Z){public static readonly WoWPoint Zero=default,Empty=new(float.NaN,float.NaN,float.NaN);public WoWPoint Add(float x,float y,float z)=>new(X+x,Y+y,Z+z);public float DistanceSqr(WoWPoint p)=>(X-p.X)*(X-p.X)+(Y-p.Y)*(Y-p.Y)+(Z-p.Z)*(Z-p.Z);public float Distance(WoWPoint p)=>MathF.Sqrt(DistanceSqr(p));public float Distance2DSqr(WoWPoint p)=>(X-p.X)*(X-p.X)+(Y-p.Y)*(Y-p.Y);public float Distance2D(WoWPoint p)=>MathF.Sqrt(Distance2DSqr(p));public WoWPoint RayCast(float angle,float distance)=>new(X+MathF.Cos(angle)*distance,Y+MathF.Sin(angle)*distance,Z);}
}
namespace Styx.WoWInternals {
 public static class ObjectManager {public static Memory Wow;public static ExecutorRand Executor;public static LocalPlayer Me;public static bool IsInGame=true;public static List<WoWUnit> Units=new();public static IEnumerable<T> GetObjectsOfType<T>(bool first,bool second)=>Units.OfType<T>();}
 public static class Lua {
  public static T GetReturnVal<T>(string code,int index){GroundProbe.PrepareLua(code);Probe.Execute();var values=code.Contains("UnitInVehicle")?GroundProbe.VehicleValues:Probe.LuaValues;return (T)(object)(values!=null&&values.Length>1&&values[1]=="1");}
  public static List<string> GetObservedReturnValues(string code){GroundProbe.PrepareLua(code);Probe.Execute();var values=code.Contains("UnitInVehicle")?GroundProbe.VehicleValues:Probe.LuaValues;return values?.ToList()??new();}
 }
}
""";
    internal const string BatchFallback = """
public enum TraceLineHitFlags{Legacy}
private static TraceLineHitFlags MapFlags(CGWorldFrameHitFlags flags)=>TraceLineHitFlags.Legacy;
public static void MassTraceLine(WorldLine[] lines,TraceLineHitFlags[] flags,out bool[] hits,out WoWPoint[] points){Probe.FallbackUsed=true;hits=new bool[lines.Length];points=new WoWPoint[lines.Length];}
""";
    internal const string Cases = """
public static class WorldCases {
 sealed class Assertion(string message):Exception(message){}
 static WoWObject subject;static LocalPlayer actor;static WoWPoint start=new(1,2,10),end=new(1,2,0);
 static void Reset(){ObjectManager.Wow=new();ObjectManager.Executor=new(){Memory=ObjectManager.Wow};ObjectManager.Me=actor=new();subject=new(){BaseAddress=16384,Guid=2};Probe.Value=1;Probe.Bytes=4;Probe.Calls=0;Probe.Error=null;Probe.During=null;Probe.DuringRead=null;Probe.Hit=new(1,2,5);Probe.LuaValues=new[]{"world-outdoors","1"};Probe.Allocate=null;Probe.Write=null;Probe.FreeError=null;Probe.FreeSucceeds=true;Probe.Allocation=12288;Probe.Freed.Clear();Probe.Batch=false;Probe.BatchHits=new byte[]{1,0};Probe.BatchPoints=new[]{Probe.Hit,new WoWPoint(float.NaN,float.NaN,float.NaN)};Probe.FallbackUsed=false;Probe.ShortBatchHits=false;Probe.ShortBatchPoints=false;Probe.Stage=null;Probe.StageAction=null;Styx.Logic.BehaviorTree.TreeRoot.RunIdentity=new();Styx.Logic.BehaviorTree.TreeRoot.Current=new();Styx.Logic.BehaviorTree.TreeRoot.IsRunning=true;}
 static void Check(bool value,string why){if(!value)throw new Assertion(why);}
 static bool Read(string kind){if(kind=="object")return subject.IsOutdoors;if(kind=="player")return actor.IsOutdoors;if(kind=="batch"){Probe.Batch=true;GameWorld.MassTraceLine(new[]{new WorldLine(start,end),new WorldLine(start,end)},new[]{GameWorld.CGWorldFrameHitFlags.HitTestGroundAndStructures,GameWorld.CGWorldFrameHitFlags.HitTestGroundAndStructures},out var hits,out var points);return hits[0];}return GameWorld.ReadRay(start,end,out _);}
 static void Unknown(string kind){try{_=Read(kind);}catch(ObservationUnavailableException){return;}throw new Assertion("unavailable "+kind+" observation became ordinary true/false");}
 public static void Run(){int passed=0,total=0,assertions=0,unexpected=0;
  void Case(string name,Action test){total++;Reset();GroundProbe.Reset(actor,subject);try{test();passed++;Console.WriteLine("PASS world query: "+name);}catch(Assertion e){assertions++;Console.Error.WriteLine("FAIL world query: "+name+": "+e.Message);}catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR world query: "+name+": "+e);}}
  foreach(string kind in new[]{"object","player","ray","batch"}){string k=kind;
   Case(k+" complete true",()=>Check(Read(k),"valid positive observation lost"));
   Case(k+" complete false",()=>{Probe.Value=0;Probe.BatchHits=new byte[]{0,0};Probe.LuaValues=new[]{"world-outdoors","0"};Check(!Read(k),"valid negative observation lost");});
   Case(k+" native failure remains UNKNOWN",()=>{Probe.Error=new InjectionSEHException();Unknown(k);});
   Case(k+" missing executor remains UNKNOWN",()=>{ObjectManager.Executor=null;Unknown(k);});
   Case(k+" missing memory remains UNKNOWN",()=>{ObjectManager.Wow=null;Unknown(k);});
   foreach(string change in new[]{"actor","actor-guid","actor-base","map","memory","process","executor","run"}){string c=change;
    Case(k+" replacement during execute/"+c,()=>{Probe.During=()=>{switch(c){case "actor":ObjectManager.Me=new();break;case "actor-guid":actor.Guid++;break;case "actor-base":actor.BaseAddress++;break;case "map":actor.MapId++;break;case "memory":ObjectManager.Wow=new();break;case "process":ObjectManager.Wow.ProcessId++;break;case "executor":ObjectManager.Executor=new(){Memory=ObjectManager.Wow};break;case "run":Styx.Logic.BehaviorTree.TreeRoot.RunIdentity=new();break;}};Unknown(k);});
   }
   foreach(Exception signal in new Exception[]{new OperationCanceledException("cancel"),new ThreadInterruptedException("stop"),new InvalidProcessException("process lost"),new InvalidExecutorException("executor lost")})foreach(bool wrapped in new[]{false,true}){var captured=signal;var wrap=wrapped;
    Case(k+" preserves "+captured.GetType().Name+"/"+wrap,()=>{Probe.Error=wrap?new TargetInvocationException(captured):captured;Exception caught=null;try{_=Read(k);}catch(Exception e){caught=e;}Check(ReferenceEquals(caught,captured),"control signal was swallowed or replaced");});
   }
  }
  foreach(string kind in new[]{"object","ray"}){string k=kind;
   Case(k+" nested execution cannot relabel the returned buffer",()=>{Probe.During=()=>ObjectManager.Executor.ExecutionGeneration++;Unknown(k);});
   Case(k+" short result remains UNKNOWN",()=>{Probe.Bytes=0;Unknown(k);});
   Case(k+" zero return pointer remains UNKNOWN",()=>{ObjectManager.Executor.ReturnPointer=0;Unknown(k);});
   Case(k+" invalid bool remains UNKNOWN",()=>{Probe.Value=2;Unknown(k);});
   Case(k+" actual execution generation changes during read",()=>{Probe.DuringRead=()=>ObjectManager.Executor.ExecutionGeneration++;Unknown(k);});
   Case(k+" ordinary render frame does not replace a result",()=>{Probe.DuringRead=()=>ObjectManager.Executor.FrameCount++;bool value=false;try{value=Read(k);}catch(ObservationUnavailableException){}Check(value,"normal render-frame progression invalidated completed native data");});
   Case(k+" result pointer changes during read",()=>{Probe.DuringRead=()=>ObjectManager.Executor.ReturnPointer++;Unknown(k);});
   Case(k+" executor closure is fatal",()=>{ObjectManager.Executor.IsOpen=false;Exception caught=null;try{_=Read(k);}catch(Exception e){caught=e;}Check(caught is InvalidExecutorException&&Probe.Calls==0,"closed executor produced geometry");});
   Case(k+" process closure is fatal",()=>{ObjectManager.Wow.ProcessHandle=IntPtr.Zero;Exception caught=null;try{_=Read(k);}catch(Exception e){caught=e;}Check(caught is InvalidProcessException&&Probe.Calls==0,"closed process produced geometry");});
  }
  Case("object GUID changes during execution",()=>{Probe.During=()=>subject.Guid++;Unknown("object");});
  Case("object base changes during execution",()=>{Probe.During=()=>subject.BaseAddress++;Unknown("object");});
  Case("invalid object never invokes native",()=>{subject.IsValid=false;Unknown("object");Check(Probe.Calls==0,"invalid subject invoked native");});
  Case("player empty Lua response",()=>{Probe.LuaValues=Array.Empty<string>();Unknown("player");});
  Case("player invalid Lua envelope",()=>{Probe.LuaValues=new[]{"other","0"};Unknown("player");});
  Case("ray nonfinite hit",()=>{Probe.Hit=new(1,2,float.NaN);Unknown("ray");});
  Case("ray hit outside requested segment",()=>{Probe.Hit=new(20,20,5);Unknown("ray");});
  Case("ray incomplete point bytes",()=>{Probe.Bytes=-1;Unknown("ray");});
  Case("ray buffer allocation unavailable",()=>{Probe.Allocation=0;Unknown("ray");Check(Probe.Calls==0,"zero allocation was dispatched");});
  Case("ray buffer writes revoke replaced actor",()=>{Probe.Write=()=>ObjectManager.Me=new();Unknown("ray");Check(Probe.Calls==0,"stale writes reached dispatch");});
  Case("ray buffer frees original memory after replacement",()=>{var original=ObjectManager.Wow;Probe.During=()=>{ObjectManager.Wow=new();ObjectManager.Executor=new(){Memory=ObjectManager.Wow};};Unknown("ray");Check(Probe.Freed.Count==1&&ReferenceEquals(Probe.Freed[0],original),"buffer was freed through replacement memory");});
  Case("ray allocation replacement frees original memory",()=>{var original=ObjectManager.Wow;Probe.Allocate=()=>{ObjectManager.Wow=new();ObjectManager.Executor=new(){Memory=ObjectManager.Wow};};Unknown("ray");Check(Probe.Calls==0&&Probe.Freed.Count==1&&ReferenceEquals(Probe.Freed[0],original),"allocation cleanup changed process");});
  Case("ray same memory process replacement never frees there",()=>{Probe.During=()=>ObjectManager.Wow.ProcessId++;Unknown("ray");Check(Probe.Freed.Count==0,"old address was freed in a replacement process");});
  Case("ray ordinary cleanup failure remains UNKNOWN",()=>{Probe.FreeSucceeds=false;Unknown("ray");});
  Case("ray cancellation outranks cleanup rejection",()=>{var signal=new OperationCanceledException("cancel while query pending");Probe.Error=signal;Probe.FreeSucceeds=false;Exception caught=null;try{_=Read("ray");}catch(Exception e){caught=e;}Check(ReferenceEquals(caught,signal),"cleanup erased query cancellation");});
  Case("ray constructor executor loss outranks cleanup rejection",()=>{Probe.Allocate=()=>ObjectManager.Executor.IsOpen=false;Probe.FreeSucceeds=false;Exception caught=null;try{_=Read("ray");}catch(Exception e){caught=e;}Check(caught is InvalidExecutorException&&Probe.Calls==0,"constructor cleanup erased executor loss");});
  Case("batch empty does not dispatch",()=>{Probe.Batch=true;GameWorld.MassTraceLine(Array.Empty<WorldLine>(),Array.Empty<GameWorld.CGWorldFrameHitFlags>(),out var hits,out var points);Check(hits.Length==0&&points.Length==0&&Probe.Calls==0,"empty native batch dispatched");});
  Case("batch partial hit result remains UNKNOWN",()=>{Probe.ShortBatchHits=true;Unknown("batch");});
  Case("batch partial point result remains UNKNOWN",()=>{Probe.ShortBatchPoints=true;Unknown("batch");});
  Case("batch invalid boolean remains UNKNOWN",()=>{Probe.BatchHits[0]=2;Unknown("batch");});
  Case("batch nonfinite hit remains UNKNOWN",()=>{Probe.BatchPoints[0]=new(1,2,float.NaN);Unknown("batch");});
  Case("batch off-segment hit remains UNKNOWN",()=>{Probe.BatchPoints[0]=new(10,20,5);Unknown("batch");});
  Case("batch execution generation changes during read",()=>{Probe.DuringRead=()=>ObjectManager.Executor.ExecutionGeneration++;Unknown("batch");});
  Case("batch nested execution cannot relabel outputs",()=>{Probe.During=()=>ObjectManager.Executor.ExecutionGeneration++;Unknown("batch");});
  Case("batch survives ordinary render frame progression",()=>{Probe.DuringRead=()=>ObjectManager.Executor.FrameCount++;bool value=false;try{value=Read("batch");}catch(ObservationUnavailableException){}Check(value,"render frame replaced a retained batch result without an execution");});
  Case("batch missing executor never invokes alternate provider",()=>{ObjectManager.Executor=null;try{_=Read("batch");}catch(ObservationUnavailableException){}Check(!Probe.FallbackUsed,"missing collision silently became another provider query");});
  Case("batch actor replacement before dispatch",()=>{Probe.Write=()=>ObjectManager.Me=new();Unknown("batch");Check(Probe.Calls==0,"batch wrote then dispatched under replaced actor");});
  Case("batch memory replacement frees original buffer",()=>{var original=ObjectManager.Wow;Probe.During=()=>{ObjectManager.Wow=new();ObjectManager.Executor=new(){Memory=ObjectManager.Wow};};Unknown("batch");Check(Probe.Freed.Count==1&&ReferenceEquals(Probe.Freed[0],original),"batch freed replacement memory");});
  Case("batch input mutation revokes outputs",()=>{Probe.Batch=true;var lines=new[]{new WorldLine(start,end),new WorldLine(start,end)};var hitFlags=new[]{GameWorld.CGWorldFrameHitFlags.HitTestGroundAndStructures,GameWorld.CGWorldFrameHitFlags.HitTestGroundAndStructures};Probe.During=()=>lines[0].End=new(30,30,30);bool unknown=false;try{GameWorld.MassTraceLine(lines,hitFlags,out _,out _);}catch(ObservationUnavailableException){unknown=true;}Check(unknown,"changed batch input received old collision output");});
  Case("batch keeps one native round trip",()=>{_=Read("batch");Check(Probe.Calls==1&&!Probe.FallbackUsed,"batch executed per-ray or alternate provider");});
  Case("object original ABI",()=>{_=subject.IsOutdoors;Check(Probe.Instructions.SequenceEqual(new[]{"mov ecx, 16384","call 7452656","retn"}),"original object calling convention changed");});
  Case("ray original ABI",()=>{_=Read("ray");Check(Probe.Instructions.SequenceEqual(new[]{"push 0","push 1048849","push 12312","push 12316","push 12300","push 12288","call 7861008","add esp, 0x18","retn"}),"original ray calling convention changed");});
  void ReplaceInteractionOwner(string change){switch(change){
   case "actor":ObjectManager.Me=new();break;case "actor-guid":actor.Guid++;break;case "actor-base":actor.BaseAddress++;break;
   case "map":actor.MapId++;break;case "memory":ObjectManager.Wow=new();break;case "process":ObjectManager.Wow.ProcessId++;break;
   case "executor":ObjectManager.Executor=new(){Memory=ObjectManager.Wow};break;case "run":Styx.Logic.BehaviorTree.TreeRoot.RunIdentity=new();break;
   case "subject-guid":subject.Guid++;break;case "subject-base":subject.BaseAddress++;break;case "death":actor.IsAlive=false;break;
   default:throw new InvalidOperationException(change);
  }}
  Case("interaction preserves original local executor receipt and ABI",()=>{
   Check(subject.SubmitInteraction()&&Probe.Calls==1,"healthy local interaction dispatch changed");
   Check(Probe.Instructions.SequenceEqual(new[]{"mov ecx, 16384","mov eax, [ecx]","add eax, 176","mov eax, [eax]","call eax","retn"}),"original 12340 interaction ABI changed");
  });
  Case("interaction permits a stable ghost healer context",()=>{actor.IsAlive=false;actor.IsGhost=true;Check(subject.SubmitInteraction()&&Probe.Calls==1,"ground transition liveness requirements leaked into the ghost interaction boundary");});
  foreach(string stage in new[]{"timer","afk","log","clear","instruction"})foreach(string change in new[]{"actor","actor-guid","actor-base","map","memory","process","executor","run","subject-guid","subject-base","death"}){
   string boundary=stage,mutation=change;
   Case("interaction rejects "+boundary+" replacement/"+mutation,()=>{
    bool fired=false;Probe.Stage=boundary;Probe.StageAction=()=>{fired=true;ReplaceInteractionOwner(mutation);};
    bool completed=false;Exception observed=null;try{completed=subject.SubmitInteraction();}catch(Exception error){observed=error;}
    Check(fired,"interaction callback boundary was not exercised");
    Check(Probe.Calls==0&&!completed&&observed is ObservationUnavailableException,"obsolete interaction reached native entry or hid unavailable ownership");
   });
  }
  foreach(string change in new[]{"actor","actor-guid","actor-base","map","memory","process","executor","run","subject-guid","subject-base","death"}){
   string mutation=change;Case("interaction rejects a stale post-entry receipt/"+mutation,()=>{
    Probe.During=()=>ReplaceInteractionOwner(mutation);bool completed=false;Exception observed=null;
    try{completed=subject.SubmitInteraction();}catch(Exception error){observed=error;}
    Check(Probe.Calls==1&&!completed&&observed is ObservationUnavailableException,"post-entry ownership change supplied a valid current receipt");
   });
  }
  foreach(Exception signal in new Exception[]{new OperationCanceledException("cancel interaction"),new ThreadInterruptedException("stop interaction"),new InvalidProcessException("interaction process lost"),new InvalidExecutorException("interaction executor lost")})foreach(bool wrapped in new[]{false,true}){
   var captured=signal;bool wrap=wrapped;Case("interaction preserves "+captured.GetType().Name+"/"+wrap,()=>{
    Probe.Error=wrap?new TargetInvocationException(captured):captured;Exception observed=null;
    try{subject.SubmitInteraction();}catch(Exception error){observed=error;}
    Check(ReferenceEquals(observed,captured),"interaction swallowed or replaced cancellation/process/executor loss");
   });
  }
  Case("interaction native failure remains UNKNOWN",()=>{Probe.Error=new InjectionSEHException();Exception observed=null;try{subject.SubmitInteraction();}catch(Exception error){observed=error;}Check(observed is ObservationUnavailableException&&Probe.Calls==1,"ambiguous interaction dispatch became an ordinary false receipt");});
  Case("interaction missing executor remains UNKNOWN",()=>{ObjectManager.Executor=null;Exception observed=null;try{subject.SubmitInteraction();}catch(Exception error){observed=error;}Check(observed is ObservationUnavailableException&&Probe.Calls==0,"missing executor was silently replaced with a false receipt");});
  GroundNativeCases.Run(Case, Check);
  CorpseSearchCases.Run(Case, Check);
  Console.WriteLine($"World query observations: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; actual production outdoors/ray/ground admission/context/interaction members, controlled executor/memory/Lua only; no native or live collision proof.");
  if(assertions+unexpected!=0)throw new InvalidOperationException("world query regression");
 }
}
""";
}
