using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class NetworkLatencyBoundRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory!=null&&!File.Exists(Path.Combine(directory.FullName,"CopilotBuddy.csproj")))directory=directory.Parent;
        string root=directory?.FullName??throw new InvalidOperationException("Checkout required");
        var source=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,"Styx/WoWInternals/Misc/WoWClient.cs"))).GetRoot();
        var method=source.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m=>m.Identifier.ValueText=="GetNetStats");
        string temp=Path.Combine(Path.GetTempPath(),"cb-network-ring-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try
        {
            File.WriteAllText(Path.Combine(temp,"Probe.cs"),Prefix+"public class Reader {public NetStats NetStats=new();public ulong PerformanceCounter()=>15000;"+method+"}\n"+Cases);
            Type type=typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler",true)!;
            object compiler=Activator.CreateInstance(type,new object[]{temp})!;
            var result=(CompilerResults)type.GetMethod("Compile")!.Invoke(compiler,null)!;
            var errors=result.Errors.Cast<CompilerError>().Where(e=>!e.IsWarning).ToArray();
            if(errors.Length!=0)throw new InvalidOperationException(string.Join("; ",errors.Select(e=>e.ToString())));
            var assembly=(Assembly)type.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            try{assembly.GetType("LatencyRingCases",true)!.GetMethod("Run")!.Invoke(null,null);}
            catch(TargetInvocationException e)when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
        }
        finally{Directory.Delete(temp,true);}
    }
    private const string Prefix="""
using System;using System.Linq;using Styx.Helpers;
// A read budget turns the original infinite loop into a deterministic failing
// assertion. It is a controlled memory leaf, not a replacement ring algorithm.
public class Slots {public uint[] Values=Enumerable.Range(1,16).Select(i=>(uint)(i*10)).ToArray();public int Reads;public int Length=>Values.Length;public uint this[uint index]{get{if(++Reads>16)throw new InvalidOperationException("ring exceeded sixteen reads");return Values[index];}}}
public class NetStats {public Slots Latencies=new();public uint LatencyIndex,LatencyCount,StartTime=1000,BytesSent=10000,BytesReceived=20000;}
""";
    private const string Cases="""
public static class LatencyRingCases {
 public static void Run(){int total=0,passed=0;void Check(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
  void Case(string name,Action body){total++;try{body();passed++;}catch(Exception e){Console.Error.WriteLine("FAIL latency ring: "+name+": "+e.Message);}}
  for(uint first=0;first<=16;first++)for(uint end=0;end<=16;end++){uint a=first,b=end;Case($"valid ring/{a}/{b}",()=>{
   var r=new Reader();r.NetStats.LatencyIndex=a;r.NetStats.LatencyCount=b;
   int[] indices=a==b?Array.Empty<int>():a<b?Enumerable.Range((int)a,(int)(b-a)).ToArray():Enumerable.Range((int)a,16-(int)a).Concat(Enumerable.Range(0,(int)b)).ToArray();
   uint expected=indices.Length==0?0:(uint)(indices.Sum(i=>(long)r.NetStats.Latencies.Values[i])/indices.Length);
   r.GetNetStats(out _,out _,out uint latency);Check(latency==expected&&r.NetStats.Latencies.Reads<=16,"valid original ring policy changed");
  });}
  foreach(string fault in new[]{"index17","count17","count-max","index-max","null","short"}){string mode=fault;Case(mode,()=>{
   var r=new Reader();r.NetStats.LatencyCount=4;
   if(mode=="index17")r.NetStats.LatencyIndex=17;if(mode=="count17")r.NetStats.LatencyCount=17;if(mode=="count-max")r.NetStats.LatencyCount=uint.MaxValue;if(mode=="index-max")r.NetStats.LatencyIndex=uint.MaxValue;if(mode=="null")r.NetStats.Latencies=null;if(mode=="short")r.NetStats.Latencies.Values=new uint[3];
   bool unavailable=false;try{r.GetNetStats(out _,out _,out _);}catch(ObservationUnavailableException){unavailable=true;}
   Check(unavailable,"invalid current ring became valid or unbounded latency");
  });}
  Case("sum cannot overflow into an artificially short delay",()=>{var r=new Reader();r.NetStats.LatencyCount=16;Array.Fill(r.NetStats.Latencies.Values,uint.MaxValue);r.GetNetStats(out _,out _,out uint value);Check(value==uint.MaxValue,"latency accumulator overflowed");});
  Console.WriteLine($"Network latency ring: {passed}/{total}; complete production reader, bounded memory/index leaf; no live network observation.");if(passed!=total)throw new InvalidOperationException("network latency ring failures");
 }
}
""";
}
