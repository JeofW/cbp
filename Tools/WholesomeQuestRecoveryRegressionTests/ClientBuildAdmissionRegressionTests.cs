using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Execute the complete tracked initialization owner with controlled process,
// version-resource, event setup and hook leaves. Discovery checks execute the
// actual three UI acceptance expressions. No process is opened or game hooked.
internal static class ClientBuildAdmissionRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "CopilotBuddy.csproj")))
            directory = directory.Parent;
        if (directory == null) throw new InvalidOperationException("Tracked checkout required.");
        var manager = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(directory.FullName, "Styx/WoWInternals/ObjectManager.cs")))
            .GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "ObjectManager");
        var methods = manager.Members.OfType<MethodDeclarationSyntax>().Where(m => m.Identifier.ValueText is
            "Initialize" or "IsSupportedClientVersion").ToArray();
        if (methods.Count(m => m.Identifier.ValueText == "Initialize") != 1)
            throw new InvalidOperationException("Complete initialization owner required.");
        var main = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(directory.FullName, "UI/MainWindow.xaml.cs")))
            .GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.ValueText == "FindAndAttachToWoW");
        var selector = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(directory.FullName, "UI/ProcessSelectorWindow.xaml.cs")))
            .GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.ValueText == "RefreshProcesses");
        static bool IsVersionCheck(IfStatementSyntax node) => node.Condition.ToString().Contains("ObjectManager.SupportedBuild", StringComparison.Ordinal)
            || node.Condition.ToString().Contains("ObjectManager.IsSupportedClientVersion", StringComparison.Ordinal);
        var mainChecks = main.DescendantNodes().OfType<IfStatementSyntax>().Where(IsVersionCheck).ToArray();
        var selectorChecks = selector.DescendantNodes().OfType<IfStatementSyntax>().Where(IsVersionCheck).ToArray();
        if (mainChecks.Length != 2 || selectorChecks.Length != 1)
            throw new InvalidOperationException("All three current UI version checks required.");
        string source = Prefix + "\npublic static class ObjectManager { internal const int SupportedBuild=12340;\n" +
            "public static Memory Wow; public static Process WoWProcess;\n" +
            "public static bool HookEndscene(){World.Commands.Add(\"hook\");World.OnHook?.Invoke();return true;}\n" +
            "public static void Update(){World.Commands.Add(\"update\");}\n" +
            string.Join("\n", methods.Select(m => m.ToString())) + "}\npublic static class Discovery {\n" +
            "public static bool Automatic(int build,FileVersionInfo version)=>" + mainChecks[0].Condition + ";\n" +
            "public static bool ExplicitPid(int build,FileVersionInfo version)=>" + mainChecks[1].Condition + ";\n" +
            "public static bool Selector(int build,FileVersionInfo version)=>!(" + selectorChecks[0].Condition + ");}\n" + Cases;
        var trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("Runtime references required.");
        var compilation = CSharpCompilation.Create("W110ClientAdmission_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(source) },
            trusted.Split(Path.PathSeparator).Distinct(StringComparer.OrdinalIgnoreCase).Select(p => MetadataReference.CreateFromFile(p)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        if (!result.Success) throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var assembly = Assembly.Load(output.ToArray());
        try { assembly.GetType("ClientAdmissionCases", true)!.GetMethod("Run")!.Invoke(null, null); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }

    private const string Prefix = """
#nullable disable
using System;using System.Collections.Generic;using System.Linq;
public sealed class FileVersionInfo {
 public int FileMajorPart=3,FileMinorPart=3,FileBuildPart=5,Revision=12340;
 public int FilePrivatePart {get{World.AfterVersionRead?.Invoke();if(World.FailVersionRead)throw new InvalidOperationException("controlled version failure");return Revision;}}
 public string FileVersion=>$"{FileMajorPart}.{FileMinorPart}.{FileBuildPart}.{Revision}";
}
public sealed class ProcessModule {public FileVersionInfo FileVersionInfo=new FileVersionInfo();}
public sealed class Process {
 public int Id=1234;public bool HasExited;public ProcessModule Module=new ProcessModule();
 public ProcessModule MainModule {get{if(World.FailModuleRead)throw new Exception("controlled module failure");return Module;}}
 public bool EnableRaisingEvents {set{if(World.FailEventSetup)throw new InvalidOperationException("controlled event setup failure");World.Commands.Add("events");}}
 public static Process[] GetProcesses(){if(World.FailLookup)throw new InvalidOperationException("controlled lookup failure");return World.Processes;}
}
public sealed class Memory {public int ProcessId=1234;}
public static class Logging {public static void Write(string message){}public static void WriteDebug(string message){} }
public static class World {
 public static Process[] Processes;public static readonly List<string> Commands=new List<string>();
 public static bool FailLookup,FailModuleRead,FailVersionRead,FailEventSetup;
 public static System.Action AfterVersionRead,OnHook;
 public static void Reset(){Processes=new[]{new Process()};Commands.Clear();FailLookup=false;FailModuleRead=false;
  FailVersionRead=false;FailEventSetup=false;AfterVersionRead=null;OnHook=null;
  ObjectManager.Wow=new Memory{ProcessId=70};ObjectManager.WoWProcess=new Process{Id=70};}
}
""";

    private const string Cases = """
public static class ClientAdmissionCases {
 private sealed class Failure(string reason):Exception(reason){}
 private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
 private static FileVersionInfo Version(string state){
  var version=new FileVersionInfo();switch(state){case "missing":return null;case "zero":version.Revision=0;break;
   case "wrong-build":version.Revision=12341;break;case "wrong-major":version.FileMajorPart=4;break;
   case "wrong-minor":version.FileMinorPart=4;break;case "wrong-patch":version.FileBuildPart=6;break;}
  return version;
 }
 private static void Rejected(string state){
  World.Reset();var previousMemory=ObjectManager.Wow;var previousProcess=ObjectManager.WoWProcess;
  var memory=new Memory();var candidate=World.Processes[0];
  switch(state){
   case "null-memory":memory=null;break;
   case "missing-process":World.Processes=Array.Empty<Process>();break;
   case "different-pid":candidate.Id++;break;
   case "exited":candidate.HasExited=true;break;
   case "exit-during-version":World.AfterVersionRead=()=>candidate.HasExited=true;break;
   case "missing-module":candidate.Module=null;break;
   case "lookup-failure":World.FailLookup=true;break;
   case "module-failure":World.FailModuleRead=true;break;
   case "version-failure":World.FailVersionRead=true;break;
   case "event-setup-failure":World.FailEventSetup=true;break;
   default:candidate.Module.FileVersionInfo=Version(state);break;
  }
  Exception rejection=null;
  try{ObjectManager.Initialize(memory);}catch(Exception error){rejection=error;}
  Check(rejection!=null,"unsupported or unobservable process was accepted");
  Check(!World.Commands.Contains("hook")&&!World.Commands.Contains("update"),"rejected process reached native hook/update");
  Check(ReferenceEquals(ObjectManager.Wow,previousMemory)&&ReferenceEquals(ObjectManager.WoWProcess,previousProcess),"failed admission published global memory/process state");
 }
 public static void Run(){
  var tests=new List<(string Name,System.Action Body)>();
  foreach(string state in new[]{"null-memory","missing-process","different-pid","exited","exit-during-version","missing-module",
   "lookup-failure","module-failure","version-failure","event-setup-failure","missing","zero","wrong-build","wrong-major","wrong-minor","wrong-patch"}){
   string observed=state;tests.Add(("initialize rejects "+state,()=>Rejected(observed)));
  }
  tests.Add(("matching process is published before hook",()=>{
   World.Reset();var memory=new Memory();var candidate=World.Processes[0];
   World.Processes=new[]{new Process{Id=999,Module=null},candidate};
   World.OnHook=()=>Check(ReferenceEquals(ObjectManager.Wow,memory)&&ReferenceEquals(ObjectManager.WoWProcess,candidate),"hook used a different memory/process owner");
   ObjectManager.Initialize(memory);
   Check(World.Commands.SequenceEqual(new[]{"events","hook","update"}),"verified initialization order or normal success changed");
  }));
  foreach(var selected in new (string Name,Func<int,FileVersionInfo,bool> Accept)[]{("automatic",Discovery.Automatic),("explicit-pid",Discovery.ExplicitPid),("selector",Discovery.Selector)}){
   var gate=selected;
   foreach(string state in new[]{"supported","missing","zero","wrong-build","wrong-major","wrong-minor","wrong-patch"}){
    string observed=state;tests.Add((gate.Name+" version "+state,()=>{
     World.Reset();var version=Version(observed);int build=version?.Revision??0;
     Check(gate.Accept(build,version)==(observed=="supported"),"UI discovery admitted an unverified client or rejected build12340");
    }));
   }
  }
  int passed=0,assertions=0,unexpected=0;
  foreach(var test in tests){try{test.Body();passed++;Console.WriteLine("PASS client build admission: "+test.Name);}
   catch(Failure error){assertions++;Console.Error.WriteLine("FAIL client build admission: "+test.Name+": "+error.Message);}
   catch(Exception error){unexpected++;Console.Error.WriteLine("ERROR client build admission: "+test.Name+": "+error);}}
  Console.WriteLine($"Client build admission scenarios: {passed}/{tests.Count}; assertions={assertions}; unexpected={unexpected}; complete tracked initialization and actual UI gate expressions, controlled process/version/hooks; no game attached.");
  if(assertions+unexpected!=0)throw new InvalidOperationException($"Client build admission regressions: assertions={assertions}; unexpected={unexpected}");
 }
}
""";
}
