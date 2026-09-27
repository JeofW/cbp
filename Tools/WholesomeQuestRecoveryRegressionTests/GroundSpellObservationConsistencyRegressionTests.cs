using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Executes the exact CastOnGround WaitContinue predicate and the existing
// LocalPlayer.HasPendingSpell(string) helper. Successive pending observations
// are controlled; this does not execute a terrain request or prove ownership.
internal static class GroundSpellObservationConsistencyRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        var routine = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,
            "runtime-snapshot/Routines/Singular wotlk/Helpers/Spell.cs"))).GetRoot();
        var method = routine.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m =>
            m.Identifier.ValueText == "CastOnGround" && m.ParameterList.Parameters.Count == 3);
        var wait = method.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
            .Single(n => n.Type.ToString() == "WaitContinue");
        string predicate = wait.ArgumentList!.Arguments[1].Expression.ToString();
        var player = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,
            "Styx/WoWInternals/WoWObjects/LocalPlayer.cs"))).GetRoot();
        string helper = player.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m =>
            m.Identifier.ValueText == "HasPendingSpell" && m.ParameterList.Parameters.Count == 1 &&
            m.ParameterList.Parameters[0].Type!.ToString() == "string").ToString();
        string directory = Path.Combine(Path.GetTempPath(), "cb-ground-observation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "Probe.cs"), Prefix + helper + "}\n" +
                "public static class Probe { public static bool Evaluate(string spell) { Func<object,bool> test = " +
                predicate + "; return test(null); } }\n" + Cases);
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { directory })!;
            var result = (CompilerResults)compilerType.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException(string.Join("; ", errors.Select(e => e.ToString())));
            var assembly = (Assembly)compilerType.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            try { assembly.GetType("GroundCases", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException e) when (e.InnerException != null)
            { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
        }
        finally { Directory.Delete(directory, true); }
    }
    private static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CopilotBuddy.csproj"))) return d.FullName;
        throw new InvalidOperationException("Tracked checkout required");
    }
    private const string Prefix = """
using System;
public sealed class WoWSpell { public string Name; }
public static class StyxWoW { public static PlayerProbe Me; }
public sealed class PlayerProbe {
 public WoWSpell[] Observations;private int index;
 public WoWSpell CurrentPendingCursorSpell { get {return Observations[Math.Min(index++,Observations.Length-1)];} }
""";
    private const string Cases = """
public static class GroundCases {
 private sealed class Failure(string reason):Exception(reason){}
 private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
 public static void Run(){
  int passed=0,assertions=0,unexpected=0,total=0;
  foreach(string mode in new[]{"stable-match","stable-foreign","absent","disappears","replaced","appears-next"}){
   total++;
   try{
    var matching=new WoWSpell{Name="Selected"};var foreign=new WoWSpell{Name="Other"};
    WoWSpell first=mode=="stable-foreign"?foreign:mode=="absent"||mode=="appears-next"?null:matching;
    WoWSpell second=mode=="disappears"||mode=="absent"?null:mode=="replaced"||mode=="stable-foreign"?foreign:matching;
    StyxWoW.Me=new PlayerProbe{Observations=new[]{first,second}};
    bool firstResult;
    try{firstResult=Probe.Evaluate("Selected");}
    catch(NullReferenceException){throw new Failure("a later absent observation was dereferenced after checking an earlier non-null spell");}
    Check(firstResult==(first!=null&&first.Name=="Selected"),"one predicate mixed different pending-spell observations");
    Check(Probe.Evaluate("Selected")== (second!=null&&second.Name=="Selected"),"next predicate did not evaluate the next current observation");
    passed++;Console.WriteLine("PASS ground spell observation: "+mode);
   }
   catch(Failure e){assertions++;Console.Error.WriteLine("FAIL ground spell observation: "+mode+": "+e.Message);}
   catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR ground spell observation: "+mode+": "+e);}
  }
  Console.WriteLine($"Ground spell observation scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; exact CastOnGround predicate and name-match helper; controlled successive observations; no terrain/native/game execution.");
  if(assertions+unexpected!=0)throw new InvalidOperationException("Ground spell observation regression");
 }
}
""";
}
