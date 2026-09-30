using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Fresh-process execution avoids references to the intentionally shadowed types
// loaded by older aggregate fixtures. This does not alter the real compiler.
internal static class QuestClosureIsolation
{
    internal static void Run(Type type,string method)
    {
        string assemblyPath=type.Assembly.Location;
        string runtime=Environment.ProcessPath ?? throw new InvalidOperationException("dotnet runtime missing");
        if(!string.Equals(Path.GetFileNameWithoutExtension(runtime),"dotnet",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The verified x86 dotnet runtime is required");
        string path=Path.Combine(AppContext.BaseDirectory,"closure-child-"+Guid.NewGuid().ToString("N")+".dll");
        string launcher="using System;using System.Reflection;public static class Entry{public static int Main(string[] args){try{Assembly.LoadFrom(args[0]).GetType(args[1],true).GetMethod(args[2],BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).Invoke(null,null);return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}}";
        try
        {
            var references=((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? throw new InvalidOperationException("Framework references missing"))
                .Split(Path.PathSeparator).Distinct(StringComparer.OrdinalIgnoreCase).Select(p=>MetadataReference.CreateFromFile(p));
            var compilation=CSharpCompilation.Create(Path.GetFileNameWithoutExtension(path),new[]{CSharpSyntaxTree.ParseText(launcher)},references,
                new CSharpCompilationOptions(OutputKind.ConsoleApplication,platform:Platform.X86));
            using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write))
            {
                var emit=compilation.Emit(output);if(!emit.Success)throw new InvalidOperationException("Isolated launcher: "+string.Join(";",emit.Diagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error)));
            }
            var start=new ProcessStartInfo(runtime){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=AppContext.BaseDirectory};
            foreach(string arg in new[]{"exec","--runtimeconfig",Path.ChangeExtension(assemblyPath,".runtimeconfig.json"),"--depsfile",Path.ChangeExtension(assemblyPath,".deps.json"),path,assemblyPath,type.FullName!,method})start.ArgumentList.Add(arg);
            using var child=Process.Start(start) ?? throw new InvalidOperationException("Isolated child did not start");
            var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();
            if(!child.WaitForExit(180000)){child.Kill(entireProcessTree:true);child.WaitForExit();throw new InvalidOperationException("Isolated closure fixture exceeded its bound");}
            Console.Write(stdout.GetAwaiter().GetResult());Console.Error.Write(stderr.GetAwaiter().GetResult());
            if(child.ExitCode!=0)throw new InvalidOperationException("Isolated closure check failed: "+child.ExitCode);
        }
        finally {if(File.Exists(path))File.Delete(path);}
    }
}
