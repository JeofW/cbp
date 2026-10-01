using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Execute the exact Targeting publication, filter invocation and public readers.
// Frame/world candidate enumeration and individual filter observations are the
// controlled leaves. No native frame lock, game process or server is invoked.
internal static class TargetObservationPublicationRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "CopilotBuddy.csproj"))) root = root.Parent;
        if (root == null) throw new InvalidOperationException("Tracked checkout required");
        var type = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName, "Styx/Logic/Targeting.cs")))
            .GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "Targeting");
        var methods = new[] { "Pulse", "InvokeFilterDelegate", "Clear", "MarkObservationUnavailable", "InitializeFilters", "CreateTargetPriority", "GetScore", "GetObject", "ToWoWUnit" };
        var properties = new[] { "ObjectList", "FirstUnit", "TargetList", "MaxTargets", "DisplayTargetingExceptions" };
        string members = string.Join("\n", type.Members.Where(member =>
            member is FieldDeclarationSyntax or ConstructorDeclarationSyntax or EventDeclarationSyntax ||
            member is ClassDeclarationSyntax nested && nested.Identifier.ValueText == "TargetPriority" ||
            member is MethodDeclarationSyntax method && methods.Contains(method.Identifier.ValueText) ||
            member is PropertyDeclarationSyntax property && properties.Contains(property.Identifier.ValueText)).Select(member => member.ToString()));
        string folder = Path.Combine(Path.GetTempPath(), "cb-target-observation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "Probe.cs"), Prefix + members + Leaves + Cases);
            foreach (string helper in new[] { "ObservationUnavailableException", "ObservationFailureDiagnostics" })
                File.Copy(Path.Combine(root.FullName, "Styx/Helpers", helper + ".cs"), Path.Combine(folder, helper + ".cs"));
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            var compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            var compiler = Activator.CreateInstance(compilerType, new object[] { folder })!;
            foreach (string path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
                compilerType.GetMethod("AddReference", flags)!.Invoke(compiler, new object[] { path });
            var result = (CompilerResults)compilerType.GetMethod("Compile", flags)!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(error => !error.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Actual targeting owner compilation: " + string.Join(";", errors.Select(error => error.ToString())));
            var assembly = (Assembly)compilerType.GetProperty("CompiledAssembly", flags)!.GetValue(compiler)!;
            try { assembly.GetType("Cases", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        finally
        {
            string full = Path.GetFullPath(folder);
            string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("cb-target-observation-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected targeting fixture cleanup path");
            Directory.Delete(full, true);
        }
    }
    private const string Prefix = """
#nullable disable
using System;using System.Linq;using System.Collections.Generic;using System.Reflection;using System.Threading;using Styx.Helpers;
public delegate void TargetListUpdateFinishedDelegate(List<string> targets);
public delegate void IncludeTargetsFilterDelegate(List<WoWObject> incoming,HashSet<WoWObject> outgoing);
public delegate void RemoveTargetsFilterDelegate(List<WoWObject> units);
public delegate void WeighTargetsDelegate(List<Targeting.TargetPriority> targets);
public class Targeting {
""";
    private const string Leaves = """
 protected virtual List<WoWObject> GetInitialObjectList()=>Cases.Candidates.ToList();
 protected virtual void DefaultRemoveTargetsFilter(List<WoWObject> units){Cases.OnRemove?.Invoke();if(Cases.Phase=="remove"){if(units.Count!=0)units.RemoveAt(0);throw Cases.Fault;}}
 protected virtual void DefaultIncludeTargetsFilter(List<WoWObject> incoming,HashSet<WoWObject> outgoing){outgoing.UnionWith(incoming);if(Cases.Phase=="include")throw Cases.Fault;}
 protected virtual void DefaultTargetWeight(List<TargetPriority> targets){foreach(var target in targets)target.Score=100-target.Object.Id;if(Cases.Phase=="weigh")throw Cases.Fault;}
}
public class WoWObject {public int Id;public string Name=>"candidate-"+Id;public float Distance=>Id;public WoWUnit ToUnit()=>this as WoWUnit;}
public class WoWUnit:WoWObject{}
public static class StyxWoW {public static object Me=new();public static bool IsInGame=true;public static Memory Memory=new();}
public class Memory {public IDisposable AcquireFrame()=>new Frame();private class Frame:IDisposable{public void Dispose(){}}}
/* Controlled diagnostics sink only. */ namespace Styx.Helpers {
 public enum LogLevel{Diagnostic}
 public static class Logging {public static bool FileLogging;public static void WriteException(Exception error)=>Cases.Messages.Add(error.Message);public static void WriteDiagnostic(string text)=>Cases.Messages.Add(text);public static void WriteToFileSync(LogLevel level,string text)=>Cases.Messages.Add(text);}
}
""";
    private const string Cases = """
public static class Cases {
 public static string Phase;public static Exception Fault;public static Action OnRemove;
 public static List<WoWObject> Candidates=new();public static List<string> Messages=new();
 private static Targeting owner;
 private static void Reset(){Phase=null;Fault=null;OnRemove=null;StyxWoW.Me=new object();StyxWoW.IsInGame=true;Messages.Clear();Candidates=new(){new WoWUnit{Id=1},new WoWUnit{Id=2}};owner=new Targeting();owner.Pulse();Check(owner.TargetList.Count==2&&owner.FirstUnit.Id==1,"healthy fixture did not publish observed targets");}
 private static void Check(bool value,string why){if(!value)throw new InvalidOperationException(why);}
 private static void Unknown(Func<object> read){bool unknown=false;try{read();}catch(ObservationUnavailableException){unknown=true;}Check(unknown,"incomplete filtering published a usable old, partial or empty target list");}
 public static void Run(){var cases=new List<(string,Action)>();
 cases.Add(("an unobserved target provider is unknown",()=>{Reset();owner=new Targeting();Unknown(()=>owner.TargetList);Unknown(()=>owner.FirstUnit);}));
 cases.Add(("complete target publication",()=>{Reset();owner.Pulse();Check(owner.TargetList.Count==2&&owner.FirstUnit.Id==1,"healthy publication changed");}));
 cases.Add(("complete empty target publication",()=>{Reset();Candidates.Clear();owner.Pulse();Check(owner.TargetList.Count==0&&owner.FirstUnit==null,"known empty scan was not authoritative");}));
 foreach(string phase in new[]{"remove","include","weigh"})foreach(bool typed in new[]{true,false}){
  string stage=phase;bool unavailable=typed;cases.Add(($"{stage} rejects incomplete publication typed={unavailable}",()=>{Reset();Phase=stage;Fault=unavailable?new ObservationUnavailableException("auras","active aura 61988 unavailable"):new InvalidOperationException("malformed filter metadata");owner.Pulse();Unknown(()=>owner.TargetList);Unknown(()=>owner.FirstUnit);}));
 }
 cases.Add(("a failed filter does not run later filters",()=>{Reset();int later=0;owner.RemoveTargetsFilter+=units=>later++;Phase="remove";Fault=new ObservationUnavailableException("auras","61988");owner.Pulse();Check(later==0,"continued filters consumed partially mutated observations");}));
 cases.Add(("silent diagnostics cannot authorize incomplete targets",()=>{Reset();owner.DisplayTargetingExceptions=false;Phase="weigh";Fault=new ObservationUnavailableException("auras","56817");owner.Pulse();Unknown(()=>owner.TargetList);}));
 cases.Add(("a successful later pulse recovers publication",()=>{Reset();Phase="include";Fault=new ObservationUnavailableException("auras","61988");owner.Pulse();Unknown(()=>owner.TargetList);Phase=null;owner.Pulse();Check(owner.TargetList.Count==2,"later complete filtering remained poisoned");}));
 cases.Add(("Clear cannot turn unavailable coverage into known empty",()=>{Reset();Phase="remove";Fault=new ObservationUnavailableException("auras","61988");owner.Pulse();owner.Clear();Unknown(()=>owner.TargetList);}));
 cases.Add(("missing world invalidates the old publication",()=>{Reset();StyxWoW.IsInGame=false;owner.Pulse();Unknown(()=>owner.TargetList);}));
 cases.Add(("reentrant readers cannot consume an in-flight old publication",()=>{Reset();bool seen=false;OnRemove=()=>{Unknown(()=>owner.FirstUnit);seen=true;};owner.Pulse();Check(seen&&owner.TargetList.Count==2,"publication was not isolated across reentrant filter observations");}));
 foreach(string phase in new[]{"remove","include","weigh"})foreach(bool wrapped in new[]{false,true})foreach(bool interrupted in new[]{false,true}){
  string stage=phase;bool reflection=wrapped,stop=interrupted;cases.Add(($"{stage} preserves stop wrapped={reflection} interruption={stop}",()=>{Reset();Exception signal=stop?new ThreadInterruptedException("controlled stop"):new OperationCanceledException("controlled cancel");Phase=stage;Fault=reflection?new TargetInvocationException(signal):signal;Exception observed=null;try{owner.Pulse();}catch(Exception error){observed=error;}Check(ReferenceEquals(observed,signal),"reflection/targeting swallowed or replaced cancellation");Unknown(()=>owner.TargetList);}));
 }
 int passed=0,failed=0;foreach(var test in cases){try{test.Item2();passed++;}catch(Exception error){failed++;Console.Error.WriteLine("FAIL target observation: "+test.Item1+": "+error.Message);}}
 Console.WriteLine($"Target observation publication: {passed}/{cases.Count}; failed={failed}; actual filters/publication/readers; controlled frame/world leaves; no native dispatch.");if(failed!=0)throw new InvalidOperationException("Target observation publication regressions");
 }
}
""";
}
