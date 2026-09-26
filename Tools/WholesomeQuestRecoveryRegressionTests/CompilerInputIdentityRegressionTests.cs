using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Styx.Plugins;

// Follow-up coverage for the actual compiler input identity. Public Refresh has
// no mutable compiler-options API; these cases configure SourceCompiler itself.
internal static class CompilerInputIdentityRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        int passed = 0, assertions = 0, unexpected = 0, total = 0;
        void Case(string name, Action test)
        {
            total++;
            try { test(); passed++; Console.WriteLine("PASS compiler input identity: " + name); }
            catch (Failure e) { assertions++; Console.Error.WriteLine("FAIL compiler input identity: " + name + ": " + e.Message); }
            catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR compiler input identity: " + name + ": " + e); }
        }

        Case("effective option changes alter identity without source changes", () =>
        {
            using var f = new Fixture();
            byte[] source = File.ReadAllBytes(f.Source);
            string initial = f.Identity();
            string originalOptions = f.Options.CompilerOptions;
            f.Options.CompilerOptions += " /optimise";
            string optimized = f.Identity();
            Check(initial != optimized, "effective optimization was omitted from input identity");
            f.Options.CompilerOptions = originalOptions;
            Check(f.Identity() == initial, "restoring effective options did not restore input identity");
            f.Options.CompilerOptions += " /optimise";
            Assembly assembly = f.Compile();
            Check(assembly.GetCustomAttribute<DebuggableAttribute>()?.IsJITOptimizerDisabled != true,
                "fingerprint changed but compilation did not consume the option");
            Check((int)assembly.GetType("IdentityProbe")!.GetMethod("Value")!.Invoke(null, null)! == 7 &&
                File.ReadAllBytes(f.Source).SequenceEqual(source), "option test changed source or result");
        });
        Case("output filenames do not invalidate otherwise identical compilation", () =>
        {
            using var f = new Fixture();
            string initial = f.Identity();
            f.Options.OutputAssembly = Path.Combine(f.Root, Guid.NewGuid().ToString("N") + ".dll");
            Check(f.Identity() == initial && f.Identity() == initial, "non-input output filename made cache identity unstable");
            Assembly assembly = f.Compile();
            Check((int)assembly.GetType("IdentityProbe")!.GetMethod("Value")!.Invoke(null, null)! == 7,
                "preparing identity interfered with actual compilation");
        });

        Console.WriteLine($"Compiler input identity scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; actual SourceCompiler identity/options/compilation; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Compiler input identity regression");
    }

    private static void Check(bool ok, string why) { if (!ok) throw new Failure(why); }
    private sealed class Fixture : IDisposable
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "cb-input-identity-" + Guid.NewGuid().ToString("N"));
        private readonly Type type = typeof(PluginManager).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
        private readonly object compiler;
        internal string Source => Path.Combine(Root, "Probe.cs");
        internal CompilerParameters Options => (CompilerParameters)type.GetProperty("Options")!.GetValue(compiler)!;
        internal Fixture()
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(Source, "public static class IdentityProbe { public static int Value() { int value = 7; return value; } }");
            compiler = Activator.CreateInstance(type, new object[] { Source })!;
        }
        internal string Identity() => (string)(type.GetMethod("ComputeCompilationInputFingerprint",
            BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new Failure("Actual compiler input identity is unavailable"))
            .Invoke(compiler, null)!;
        internal Assembly Compile()
        {
            var result = (CompilerResults)type.GetMethod("Compile")!.Invoke(compiler, null)!;
            string[] errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).Select(e => e.ToString()).ToArray();
            Check(errors.Length == 0, "Compilation after identity preparation failed: " + string.Join("; ", errors));
            return (Assembly)type.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
        }
        public void Dispose() { Directory.Delete(Root, true); }
    }
}
