using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Actual Execute and its generation storage. Assembly injection/wait are the
// controlled leaves; this does not execute instructions in a game process.
internal static class ExecutorCommandGenerationRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var path=new DirectoryInfo(AppContext.BaseDirectory);
        while(path!=null&&!File.Exists(Path.Combine(path.FullName,"CopilotBuddy.csproj")))path=path.Parent;
        string root=path?.FullName??throw new InvalidOperationException("Checkout required");
        var source=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,"GreenMagic/ExecutorRand.cs"))).GetRoot();
        string execute=source.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m=>m.Identifier.ValueText=="Execute"&&m.ParameterList.Parameters.Count==0).ToString();
        string generation=source.DescendantNodes().OfType<PropertyDeclarationSyntax>().Single(p=>p.Identifier.ValueText=="ExecutionGeneration").ToString();
        string field=source.DescendantNodes().OfType<FieldDeclarationSyntax>().Single(f=>f.Declaration.Variables.Any(v=>v.Identifier.ValueText=="m_ExecutionGeneration")).ToString();
        string temp=Path.Combine(Path.GetTempPath(),"cb-executor-generation-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try
        {
            File.WriteAllText(Path.Combine(temp,"Probe.cs"),"using System;using System.Threading;public class ExecutorRand {"+field+generation+execute+Leaves+"}\n"+Cases);
            var type=typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler",true)!;
            var compiler=Activator.CreateInstance(type,new object[]{temp})!;
            var result=(CompilerResults)type.GetMethod("Compile")!.Invoke(compiler,null)!;
            var errors=result.Errors.Cast<CompilerError>().Where(e=>!e.IsWarning).ToArray();
            if(errors.Length!=0)throw new InvalidOperationException(string.Join("; ",errors.Select(e=>e.ToString())));
            var assembly=(Assembly)type.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            try{assembly.GetType("GenerationCases",true)!.GetMethod("Run")!.Invoke(null,null);}
            catch(TargetInvocationException e)when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
        }
        finally{Directory.Delete(temp,true);}
    }
    private const string Leaves="""
 public bool IsOpen=true,IsInitialized=true;private object thisLock=new();private uint m_InjectedCode=123;public Mem Memory=new();
 public Action Wait;private void SharedExecuteLogicEnd(int timeout){if(timeout!=15000)throw new InvalidOperationException("wait contract changed");Wait?.Invoke();}
 public class Mem {public Assembler Asm=new();}public class Assembler {public Action Injected;public void Inject(uint address){if(address!=123)throw new InvalidOperationException("instruction address changed");Injected?.Invoke();}}
""";
    private const string Cases="""
public class InvalidExecutorException:Exception {public InvalidExecutorException(string text):base(text){}}
public static class GenerationCases {
 public static void Run(){int count=0;
  void Check(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
  var e=new ExecutorRand();Check(e.ExecutionGeneration==0,"initial generation");
  for(int i=1;i<=3;i++){e.Execute();Check(e.ExecutionGeneration==i,"one command did not advance exactly once");}count++;
  e.IsOpen=false;try{e.Execute();throw new InvalidOperationException("closed execution accepted");}catch(Exception error)when(error.GetType()==typeof(Exception)&&error.Message.StartsWith("Cannot execute code while process")){}Check(e.ExecutionGeneration==3,"closed executor acquired a command generation");count++;
  e.IsOpen=true;var stop=new OperationCanceledException("native wait cancelled");e.Wait=()=>throw stop;try{e.Execute();throw new InvalidOperationException("cancel swallowed");}catch(OperationCanceledException actual){Check(ReferenceEquals(stop,actual),"cancellation identity changed");}Check(e.ExecutionGeneration==4,"entered cancelled command was omitted");count++;
  e.Wait=null;e.Memory.Asm.Injected=()=>{e.Memory.Asm.Injected=null;e.Execute();};e.Execute();Check(e.ExecutionGeneration==6,"nested command reused its parent's generation");count++;
  Console.WriteLine($"Executor command generation: {count}/{count}; exact Execute/storage; controlled injection/wait, no game instructions.");
 }
}
""";
}
