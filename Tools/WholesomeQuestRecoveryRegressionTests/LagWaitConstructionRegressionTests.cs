using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class LagWaitConstructionRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory!=null&&!File.Exists(Path.Combine(directory.FullName,"CopilotBuddy.csproj")))directory=directory.Parent;
        string root=directory?.FullName??throw new InvalidOperationException("Checkout required");
        var source=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,"runtime-snapshot/Routines/Singular wotlk/Helpers/Common.cs"))).GetRoot();
        var owner=source.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c=>c.Identifier.ValueText=="Common");
        string members=string.Join("\n",owner.Members.Where(m=>m is MethodDeclarationSyntax method&&method.Identifier.ValueText=="CreateWaitForLagDuration"||m is ClassDeclarationSyntax cls&&cls.Identifier.ValueText=="LagWait"));
        string temp=Path.Combine(Path.GetTempPath(),"cb-lag-construction-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try
        {
            File.WriteAllText(Path.Combine(temp,"Probe.cs"),Prefix+"public static class Common {"+members+"}\n"+Cases);
            Type type=typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler",true)!;
            object compiler=Activator.CreateInstance(type,new object[]{temp})!;
            var result=(CompilerResults)type.GetMethod("Compile")!.Invoke(compiler,null)!;
            var errors=result.Errors.Cast<CompilerError>().Where(e=>!e.IsWarning).ToArray();
            if(errors.Length!=0)throw new InvalidOperationException(string.Join("; ",errors.Select(e=>e.ToString())));
            var assembly=(Assembly)type.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            try{assembly.GetType("LagCases",true)!.GetMethod("Run")!.Invoke(null,null);}
            catch(TargetInvocationException e)when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
        }
        finally{Directory.Delete(temp,true);}
    }
    private const string Prefix="""
using System;using TreeSharp;using Styx.Helpers;using CommonBehaviors.Actions;
public static class StyxWoW {public static Client WoWClient=new();}
public class Client {public uint Value=100;public int Reads;public Exception Error;public uint Latency{get{Reads++;if(Error!=null)throw Error;return Value;}}}
public static class Logging {public static void WriteDebug(string text,params object[] values){}}
""";
    private const string Cases="""
public static class LagCases {
 public static void Run(){int total=0,passed=0;
  void Check(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
  void Case(string name,System.Action body){total++;StyxWoW.WoWClient=new();try{body();passed++;Console.WriteLine("PASS lag wait: "+name);}catch(Exception e){Console.Error.WriteLine("FAIL lag wait: "+name+": "+e.Message);}}
  Case("construction is independent of unavailable client memory",()=>{StyxWoW.WoWClient.Error=new ObservationUnavailableException("network","unavailable");_=Common.CreateWaitForLagDuration();Check(StyxWoW.WoWClient.Reads==0,"construction queried live latency");});
  Case("each start observes its own current delay",()=>{var wait=(Wait)Common.CreateWaitForLagDuration();Check(StyxWoW.WoWClient.Reads==0,"eager read");wait.Start(null);Check(wait.Timeout.TotalMilliseconds==350&&StyxWoW.WoWClient.Reads==1,"current delay was not observed at start");wait.Stop(null);StyxWoW.WoWClient.Value=250;wait.Start(null);Check(wait.Timeout.TotalMilliseconds==650&&StyxWoW.WoWClient.Reads==2,"restarted wait reused construction-time latency");wait.Stop(null);});
  Case("extreme unsigned latency cannot create a multi-day wait",()=>{var wait=(Wait)Common.CreateWaitForLagDuration();StyxWoW.WoWClient.Value=uint.MaxValue;wait.Start(null);Check(wait.Timeout.TotalMilliseconds==2000,"latency wait was not bounded");wait.Stop(null);});
  Case("unknown timing gets bounded scheduling grace, not zero-latency authority",()=>{var wait=(Wait)Common.CreateWaitForLagDuration();StyxWoW.WoWClient.Error=new ObservationUnavailableException("network","unavailable");wait.Start(null);Check(wait.Timeout.TotalMilliseconds==500,"unknown scheduling delay was unbounded or zero");wait.Stop(null);});
  Case("explicit cancellation identity propagates",()=>{var wait=Common.CreateWaitForLagDuration();var stop=new OperationCanceledException("stop");StyxWoW.WoWClient.Error=stop;bool caught=false;try{wait.Start(null);}catch(OperationCanceledException e){caught=ReferenceEquals(e,stop);}Check(caught,"cancellation swallowed");});
  Console.WriteLine($"Lag wait construction: {passed}/{total}; actual factory and Wait lifetime, controlled latency only.");if(passed!=total)throw new InvalidOperationException("lag construction failures");
 }
}
""";
}
