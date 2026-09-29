using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Evaluates the actual emitted guard instructions against controlled words.
// This is an instruction-contract test, not execution in a WoW process.
internal static class EquipmentCursorGuidGuardRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        string source = File.ReadAllText(Path.Combine(root, "Styx/WoWInternals/Lua.cs"));
        var methods = CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
        var guard = methods.SingleOrDefault(m => m.Identifier.ValueText == "EmitCursorItemGuard");
        if (guard == null) throw new InvalidOperationException("Physical cursor GUID guard is missing");
        string core = methods.Single(m => m.Identifier.ValueText == "GetReturnValuesCore").ToFullString();
        int location = core.IndexOf("EmitCursorItemGuard(executor, expectedCursorGuid)", StringComparison.Ordinal);
        if (location <= core.IndexOf("GlobalOffsets.FrameScript_Load", StringComparison.Ordinal) ||
            location >= core.IndexOf("GlobalOffsets.FrameScript_PCall", StringComparison.Ordinal))
            throw new InvalidOperationException("Cursor guard must execute after load and before Lua dispatch in the same executor request");
        string directory = Path.Combine(Path.GetTempPath(), "cb-cursor-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        int passed = 0;
        try
        {
            File.WriteAllText(Path.Combine(directory, "Probe.cs"), """
using System; using System.Collections.Generic; using System.Globalization; using Styx.Patchables;
public class ExecutorRand {
 public List<string> Lines=new List<string>();
 public void AddLine(string format,params object[] args){Lines.Add(string.Format(CultureInfo.InvariantCulture,format,args));}
}
public static class Probe {
""" + guard.ToFullString() + """
 public static string[] Build(ulong guid){var e=new ExecutorRand();EmitCursorItemGuard(e,guid);return e.Lines.ToArray();}
}
""");
            Type type = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(type, new object[] { directory })!;
            var result = (CompilerResults)type.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException(string.Join(";", errors.Select(e => e.ToString())));
            var assembly = (Assembly)type.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            var build = assembly.GetType("Probe", true)!.GetMethod("Build")!;
            const ulong guid = 0xFEDCBA9876543210UL;
            var lines = (string[])build.Invoke(null, new object[] { guid })!;
            foreach (var c in new[] {
                (Kind:1U, Guid:guid, Expected:true),
                (Kind:0U, Guid:guid, Expected:false),
                (Kind:7U, Guid:guid, Expected:false),
                (Kind:9U, Guid:guid, Expected:false),
                (Kind:11U, Guid:guid, Expected:false),
                (Kind:1U, Guid:0UL, Expected:false),
                (Kind:1U, Guid:guid ^ 1UL, Expected:false),
                (Kind:1U, Guid:guid ^ (1UL << 32), Expected:false) })
            {
                var words = new Dictionary<uint, uint> {
                    [0xBD0748]=c.Kind, [0xBD0768]=(uint)c.Guid, [0xBD076C]=(uint)(c.Guid >> 32) };
                bool equal = false, accepted = true;
                int comparisons = 0;
                foreach (string line in lines)
                {
                    var match = Regex.Match(line, @"^cmp dword \[(\d+)\], (\d+)$");
                    if (match.Success)
                    {
                        comparisons++;
                        equal = words[uint.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)] ==
                            uint.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                    }
                    else if (line == "jne @FailNoRetValues") { if (!equal) { accepted = false; break; } }
                    else throw new InvalidOperationException("Unrecognized generated guard instruction: " + line);
                }
                if (accepted != c.Expected || accepted && comparisons != 3)
                    throw new InvalidOperationException("Native guard admitted wrong physical cursor or refused the exact GUID");
                passed++;
            }
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine($"Equipment physical cursor guard: {passed}/8; actual generated x86 comparison/branch contract; no game attached or native execution.");
    }
    private static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CopilotBuddy.csproj"))) return d.FullName;
        throw new InvalidOperationException("Tracked checkout required");
    }
}
