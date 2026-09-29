using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Compile the complete tracked getters, their declared offset, and the real
// asynchronous stop owner. Native byte reads/object lookup/dispatch are controlled.
// The independent input owner and interaction GUID addresses come from the
// hash-bound build12340 input dispatcher and its native writers, not this enum.
internal static class ActiveMoverObservationRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "CopilotBuddy.csproj"))) directory = directory.Parent;
        if (directory == null) throw new InvalidOperationException("Tracked checkout required.");
        SyntaxNode Read(string path) => CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(directory.FullName, path))).GetRoot();
        var owner = Read("Styx/WoWInternals/WoWMovement.cs").DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "WoWMovement");
        var getters = owner.Members.OfType<PropertyDeclarationSyntax>().Where(p => p.Identifier.ValueText is "ActiveMoverGuid" or "ActiveMover").ToArray();
        if (getters.Length != 2) throw new InvalidOperationException("Both complete movement-owner getters required.");
        var offset = Read("Styx/Offsets/GlobalOffsets.cs").DescendantNodes().OfType<FieldDeclarationSyntax>()
            .Single(f => f.Declaration.Variables.Any(v => v.Identifier.ValueText == "ActiveMoverGuid"));
        var stop = Read("Styx/CommonBot/Coroutines/CommonCoroutines.cs").DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == "StopMoving");
        string source = Prefix + "\nnamespace Styx.Offsets { public static class GlobalOffsets {\n" + offset + "\n}}\n" +
            "public static class WoWMovement {\n" + string.Join("\n", getters.Select(p => p.ToString())) +
            "\npublic static void MoveStop()=>World.NativeStop();\n}\npublic static class CommonCoroutines {\n" + stop + "\n}\n" + Cases;
        string trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? throw new InvalidOperationException("Runtime references required.");
        var compilation = CSharpCompilation.Create("W110ActiveMover_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(source) },
            trusted.Split(Path.PathSeparator).Distinct(StringComparer.OrdinalIgnoreCase).Select(p => MetadataReference.CreateFromFile(p)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        if (!result.Success) throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var assembly = Assembly.Load(output.ToArray());
        try { assembly.GetType("ActiveMoverCases", true)!.GetMethod("Run")!.Invoke(null, null); }
        catch (TargetInvocationException error) when (error.InnerException != null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }

    private const string Prefix = """
#nullable disable
using System;using System.Collections.Generic;using System.Linq;using System.Threading.Tasks;
public sealed class WoWUnit {public ulong Guid;public uint BaseAddress;public bool IsValid=true,IsMoving;}
public sealed class Memory {
 public bool CacheEnabled=true;public ulong InputGuid=123,InteractionGuid=999;public int Reads;
 public readonly Dictionary<uint,byte[]> Cache=new Dictionary<uint,byte[]>();
 public Func<uint,int,byte[]> Override;public Action<uint,int> AfterRead;
 public byte[] ReadBytes(uint address,int count){
  Reads++;if(CacheEnabled&&Cache.TryGetValue(address,out var cached))return cached;
  byte[] data=Override?.Invoke(address,count);
  if(data==null){ulong value=address==0xCA1238?InputGuid:address==0xBD07A8?InteractionGuid:
   World.Units.Values.FirstOrDefault(u=>u.BaseAddress+48==address)?.Guid??0;
   data=BitConverter.GetBytes(value);}
  AfterRead?.Invoke(address,Reads);return data;
 }
 public T Read<T>(params uint[] addresses) where T:struct {
  var bytes=ReadBytes(addresses[0],8);return bytes==null||bytes.Length<8?default:(T)(object)BitConverter.ToUInt64(bytes,0);
 }
 public IDisposable TemporaryCacheState(bool value)=>new CacheScope(this,value);
 private sealed class CacheScope:IDisposable {private readonly Memory owner;private readonly bool previous;
  public CacheScope(Memory owner,bool value){this.owner=owner;previous=owner.CacheEnabled;owner.CacheEnabled=value;}
  public void Dispose(){owner.CacheEnabled=previous;}}
}
public static class ObjectManager {
 public static Memory Wow;public static WoWUnit Me;
 public static T GetObjectByGuid<T>(ulong guid) where T:class {
  World.AfterLookup?.Invoke();if(World.LookupThrows)throw new InvalidOperationException("controlled lookup failure");
  return (World.LookupOverride??(World.Units.TryGetValue(guid,out var unit)?unit:null)) as T;
 }
}
public static class Logging {public static void WriteDiagnostic(string text){} }
public static class Coroutine {public static Task<bool> Wait(int milliseconds,Func<bool> condition){World.BeforeWait?.Invoke();return Task.FromResult(condition());}}
public static class World {
 public static readonly Dictionary<ulong,WoWUnit> Units=new Dictionary<ulong,WoWUnit>();
 public static readonly List<ulong> Stops=new List<ulong>();public static WoWUnit LookupOverride;
 public static bool LookupThrows,StopWorks;public static Action AfterLookup,BeforeWait;
 public static void Reset(){Units.Clear();Stops.Clear();LookupOverride=null;LookupThrows=false;StopWorks=true;AfterLookup=null;BeforeWait=null;
  ObjectManager.Wow=new Memory();ObjectManager.Me=new WoWUnit{Guid=123,BaseAddress=0x10000,IsMoving=true};
  Units[123]=ObjectManager.Me;Units[456]=new WoWUnit{Guid=456,BaseAddress=0x20000,IsMoving=true};
  Units[999]=new WoWUnit{Guid=999,BaseAddress=0x30000,IsMoving=false};}
 public static void NativeStop(){ulong guid=ObjectManager.Wow?.InputGuid??0;Stops.Add(guid);
  if(StopWorks&&Units.TryGetValue(guid,out var unit))unit.IsMoving=false;}
}
""";

    private const string Cases = """
public static class ActiveMoverCases {
 private sealed class Failure(string text):Exception(text){}
 private static void Check(bool value,string text){if(!value)throw new Failure(text);}
 private static ulong Guid(){try{return WoWMovement.ActiveMoverGuid;}catch(Exception error){throw new Failure("observation escaped "+error.GetType().Name);}}
 private static WoWUnit Mover(){try{return WoWMovement.ActiveMover;}catch(Exception error){throw new Failure("lookup escaped "+error.GetType().Name);}}
 public static void Run(){var tests=new List<(string Name,Action Body)>();
  foreach(ulong selected in new ulong[]{123,456}){ulong expected=selected;
   tests.Add(($"input owner {expected} differs from interaction target",()=>{World.Reset();ObjectManager.Wow.InputGuid=expected;
    Check(Guid()==expected&&ReferenceEquals(Mover(),World.Units[expected]),"interaction target replaced the actual movement owner");}));
   tests.Add(($"stop owner {expected} uses actual input recipient",()=>{World.Reset();ObjectManager.Wow.InputGuid=expected;
    Check(CommonCoroutines.StopMoving().GetAwaiter().GetResult()&&World.Stops.SequenceEqual(new[]{expected}),"stop command or acknowledgement used interaction state");}));
  }
  foreach(bool enabled in new[]{true,false}){bool cache=enabled;
   tests.Add(($"uncached observation preserves cache={cache}",()=>{World.Reset();var memory=ObjectManager.Wow;memory.CacheEnabled=cache;
    memory.Cache[0xCA1238]=BitConverter.GetBytes(999UL);memory.Cache[0xBD07A8]=BitConverter.GetBytes(456UL);
    Check(Guid()==123&&ReferenceEquals(Mover(),ObjectManager.Me),"cached interaction or stale control GUID was accepted");
    Check(memory.CacheEnabled==cache,"observation changed caller cache policy");}));
  }
  tests.Add(("zero control GUID is unavailable, not local player",()=>{World.Reset();ObjectManager.Wow.InputGuid=0;ObjectManager.Wow.InteractionGuid=0;
   Check(Guid()==0&&Mover()==null,"zero native control owner was fabricated as Me");}));
  tests.Add(("unresolved owner is unavailable, not local player",()=>{World.Reset();ObjectManager.Wow.InputGuid=888;ObjectManager.Wow.InteractionGuid=888;
   Check(Guid()==888&&Mover()==null,"unresolved control owner fell back to Me");}));
  tests.Add(("missing memory cannot resolve through Me",()=>{World.Reset();ObjectManager.Wow=null;Check(Guid()==0&&Mover()==null,"missing memory exposed a movement owner");}));
  foreach(int selected in new[]{0,1,4,7,9}){int length=selected;
   tests.Add(($"incomplete or oversized control bytes/{length}",()=>{World.Reset();ObjectManager.Wow.Override=(a,n)=>new byte[length];
    Check(Guid()==0&&Mover()==null,"invalid byte count authorized a fallback actor");}));
  }
  tests.Add(("read exception returns unavailable and restores cache",()=>{World.Reset();var memory=ObjectManager.Wow;
   memory.Override=(a,n)=>throw new InvalidOperationException("controlled read failure");Check(Guid()==0&&Mover()==null&&memory.CacheEnabled,"failed read escaped or changed cache state");}));
  tests.Add(("control changes during read revoke observation",()=>{World.Reset();var memory=ObjectManager.Wow;memory.InteractionGuid=123;
   memory.AfterRead=(a,n)=>{if(n==1)memory.InputGuid=456;};Check(Guid()==0,"changed native control GUID was accepted");}));
  tests.Add(("memory owner changes during read revoke observation",()=>{World.Reset();var memory=ObjectManager.Wow;memory.InteractionGuid=123;
   memory.AfterRead=(a,n)=>ObjectManager.Wow=new Memory();Check(Guid()==0&&memory.CacheEnabled,"replacement memory was accepted");}));
  foreach(string state in new[]{"control-change","memory-change","invalid-object","wrong-guid","zero-address","overflow-address","wrong-raw-guid","lookup-throws"}){
   string observed=state;tests.Add(("resolution revalidates "+observed,()=>{World.Reset();var memory=ObjectManager.Wow;memory.InteractionGuid=123;
    switch(observed){
     case "control-change":World.AfterLookup=()=>memory.InputGuid=456;break;
     case "memory-change":World.AfterLookup=()=>ObjectManager.Wow=new Memory();break;
     case "invalid-object":World.AfterLookup=()=>ObjectManager.Me.IsValid=false;break;
     case "wrong-guid":World.LookupOverride=World.Units[999];break;
     case "zero-address":ObjectManager.Me.BaseAddress=0;break;
     case "overflow-address":ObjectManager.Me.BaseAddress=uint.MaxValue;break;
     case "wrong-raw-guid":memory.Override=(a,n)=>a==ObjectManager.Me.BaseAddress+48?BitConverter.GetBytes(456UL):null;break;
     case "lookup-throws":World.LookupThrows=true;break;
    }
    Check(Mover()==null&&memory.CacheEnabled,"changed or unavailable actor was returned");
   }));
  }
  tests.Add(("unknown control owner issues no stop",()=>{World.Reset();ObjectManager.Wow.InputGuid=0;ObjectManager.Wow.InteractionGuid=0;
   Check(!CommonCoroutines.StopMoving().GetAwaiter().GetResult()&&World.Stops.Count==0,"missing native owner issued a stop for Me");}));
  tests.Add(("vehicle handoff while waiting cannot acknowledge old stop",()=>{World.Reset();ObjectManager.Wow.InteractionGuid=123;
   World.BeforeWait=()=>ObjectManager.Wow.InputGuid=456;
   Check(!CommonCoroutines.StopMoving().GetAwaiter().GetResult()&&World.Stops.SequenceEqual(new[]{123UL}),"replacement control owner acknowledged the old request");}));
  tests.Add(("same owner stop timeout stays failure",()=>{World.Reset();ObjectManager.Wow.InteractionGuid=123;World.StopWorks=false;
   Check(!CommonCoroutines.StopMoving().GetAwaiter().GetResult()&&World.Stops.SequenceEqual(new[]{123UL}),"unsuccessful stop became success");}));
  int passed=0,assertions=0,unexpected=0;
  foreach(var test in tests){try{test.Body();passed++;Console.WriteLine("PASS active mover: "+test.Name);}
   catch(Failure error){assertions++;Console.Error.WriteLine("FAIL active mover: "+test.Name+": "+error.Message);}
   catch(Exception error){unexpected++;Console.Error.WriteLine("ERROR active mover: "+test.Name+": "+error);}}
  Console.WriteLine($"Active mover observation scenarios: {passed}/{tests.Count}; assertions={assertions}; unexpected={unexpected}; complete tracked getters/offset and stop owner, controlled native bytes and dispatch; no game attached.");
  if(assertions+unexpected!=0)throw new InvalidOperationException($"Active mover observation regressions: assertions={assertions}; unexpected={unexpected}");
 }
}
""";
}
