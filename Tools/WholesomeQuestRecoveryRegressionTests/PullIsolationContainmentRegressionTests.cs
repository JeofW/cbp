using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

// Executes the two tracked provider members, including their actual containment
// initializer when present. External observations are controlled; no game runs.
internal static class PullIsolationContainmentRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }
    [ModuleInitializer]
    internal static void Run()
    {
        string? root = null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CopilotBuddy.csproj"))) { root = d.FullName; break; }
        if (root == null) throw new InvalidOperationException("Tracked checkout required");
        string source = File.ReadAllText(Path.Combine(root,
            "runtime-snapshot/Routines/Singular wotlk/SingularRoutine.cs"));
        string initializer = Regex.Match(source,
            @"private static readonly bool DensePullIsolationValidated\s*=\s*(true|false)\s*;").Value;
        string temp = Path.Combine(Path.GetTempPath(), "cb-isolation-containment-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            File.WriteAllText(Path.Combine(temp, "Probe.cs"), Prefix + initializer + "\n"
                + Member(source, "public double IsolationPullDistance") + "\n"
                + Member(source, "public Composite CreateIsolationPullBehavior()") + Suffix);
            Type type = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(type, new object[] { temp })!;
            var result = (CompilerResults)type.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).Select(e => e.ToString()).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Provider compilation: " + string.Join(";", errors));
            var assembly = (Assembly)type.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            MethodInfo execute = assembly.GetType("ContainmentProbe", true)!.GetMethod("Execute")!;
            int passed = 0, assertions = 0, unexpected = 0;
            var cases = new[]
            {
                (Name: "eligible Ret known Exorcism range", Context: 0, Spec: 0, Learned: true, Range: 30d, Player: true),
                (Name: "eligible Ret missing spell range", Context: 0, Spec: 0, Learned: true, Range: 0d, Player: true),
                (Name: "eligible Ret short range", Context: 0, Spec: 0, Learned: true, Range: 10d, Player: true),
                (Name: "eligible Ret large range", Context: 0, Spec: 0, Learned: true, Range: 50d, Player: true),
                (Name: "no learned Exorcism", Context: 0, Spec: 0, Learned: false, Range: 30d, Player: true),
                (Name: "other specialization", Context: 0, Spec: 1, Learned: true, Range: 30d, Player: true),
                (Name: "instance context", Context: 1, Spec: 0, Learned: true, Range: 30d, Player: true),
                (Name: "battleground context", Context: 2, Spec: 0, Learned: true, Range: 30d, Player: true),
                (Name: "missing player", Context: 0, Spec: 0, Learned: true, Range: 30d, Player: false)
            };
            foreach (var c in cases)
            {
                try
                {
                    string actual = (string)execute.Invoke(null, new object[] { c.Context, c.Spec, c.Learned, c.Range, c.Player })!;
                    if (actual != "0|0|Failure") throw new Failure("range|factory calls|opener result=" + actual);
                    passed++; Console.WriteLine("PASS isolation containment: " + c.Name);
                }
                catch (Failure error) { assertions++; Console.Error.WriteLine("FAIL isolation containment: " + c.Name + ": " + error.Message); }
                catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR isolation containment: " + c.Name + ": " + error); }
            }
            Console.WriteLine($"Isolation containment scenarios: {passed}/{cases.Length}; assertions={assertions}; unexpected={unexpected}; verbatim provider members and real TreeSharp; no movement or game attached.");
            if (assertions + unexpected != 0) throw new InvalidOperationException("Isolation containment regression");
        }
        finally { Directory.Delete(temp, true); }
    }
    private static string Member(string source, string marker)
    {
        int start = source.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException("Missing tracked member " + marker);
        int brace = source.IndexOf('{', start), depth = 0;
        for (int i = brace; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source.Substring(start, i - start + 1);
        }
        throw new InvalidOperationException("Unclosed tracked member " + marker);
    }
    private const string Prefix = """
using System;
using System.Collections.Generic;
using System.Globalization;
using TreeSharp;
public enum WoWContext { Normal, Instances, Battlegrounds }
public enum TalentSpec { RetributionPaladin, Other }
public static class TalentManager { public static TalentSpec CurrentSpec; }
public static class StyxWoW { public static object Me; }
namespace Styx.Logic.Combat
{
    public sealed class Spell { public double MaxRange; }
    public static class SpellManager
    {
        public static bool Learned;
        public static Dictionary<string,Spell> Spells=new Dictionary<string,Spell>();
        public static bool HasSpell(string name){return Learned;}
    }
}
namespace Singular.ClassSpecific.Paladin
{
    public static class Retribution
    {
        public static int Calls;
        public static Composite CreateRetributionPaladinIsolationPull()
        {Calls++;return new TreeSharp.Action(_=>RunStatus.Success);}
    }
}
public class ContainmentProbe
{
    public static WoWContext CurrentWoWContext;
""";
    private const string Suffix = """
    public static string Execute(int context,int spec,bool learned,double range,bool player)
    {
        CurrentWoWContext=(WoWContext)context;TalentManager.CurrentSpec=(TalentSpec)spec;
        StyxWoW.Me=player?new object():null;
        Styx.Logic.Combat.SpellManager.Learned=learned;
        Styx.Logic.Combat.SpellManager.Spells.Clear();
        if(range>0)Styx.Logic.Combat.SpellManager.Spells["Exorcism"]=new Styx.Logic.Combat.Spell{MaxRange=range};
        Singular.ClassSpecific.Paladin.Retribution.Calls=0;
        var owner=new ContainmentProbe();double distance=owner.IsolationPullDistance;
        Composite tree=owner.CreateIsolationPullBehavior();RunStatus status;
        try{tree.Start(null);status=tree.Tick(null);}finally{tree.Stop(null);}
        return distance.ToString(CultureInfo.InvariantCulture)+"|"+Singular.ClassSpecific.Paladin.Retribution.Calls+"|"+status;
    }
}
""";
}
