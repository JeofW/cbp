using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Styx.Helpers;
using Styx.Plugins;
using Styx.Plugins.PluginClass;

// The public PluginManager entry point compiles the temporary source itself.
// No substitute compiler, installed plugin directory, or game is involved.
internal static class PluginFreshCompilationRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        int passed = 0, assertions = 0, unexpected = 0, total = 0;
        void Case(string name, Action test)
        {
            total++;
            try { test(); passed++; Console.WriteLine("PASS plugin fresh compile: " + name); }
            catch (Failure e) { assertions++; Console.Error.WriteLine("FAIL plugin fresh compile: " + name + ": " + e.Message); }
            catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR plugin fresh compile: " + name + ": " + e); }
        }

        Case("empty source directory remains a valid no-plugin result", () =>
        {
            using var f = new Fixture(null);
            Check(PluginManager.CompileAndLoadFrom(f.Root).Count == 0, "empty directory did not remain a no-op");
        });
        Case("missing path keeps its existing file-not-found result", () =>
        {
            string missing = Path.Combine(Path.GetTempPath(), "cb-plugin-missing-" + Guid.NewGuid().ToString("N"));
            Check(Capture(() => PluginManager.CompileAndLoadFrom(missing)) is FileNotFoundException,
                "missing path lost its existing error contract");
        });
        Case("source without plugin types remains a valid no-plugin result", () =>
        {
            using var f = new Fixture("public sealed class Plain_" + Guid.NewGuid().ToString("N") + " {}");
            Check(PluginManager.CompileAndLoadFrom(f.Root).Count == 0, "ordinary helper source was treated as a failed plugin");
        });
        Case("healthy public compilation returns both fresh instances", () =>
        {
            using var f = Fixture.Plugins(false, false, false);
            List<HBPlugin> plugins = PluginManager.CompileAndLoadFrom(f.Root);
            Check(plugins.Count == 2 && plugins[0] != plugins[1], "healthy compiled set is incomplete");
            Check(f.ReadCounter("Created") == 2 && f.ReadCounter("Disposed") == 0, "healthy instances were disposed before return");
            foreach (HBPlugin plugin in plugins) plugin.Dispose();
        });
        Case("all constructors failing is an error rather than an empty plugin set", () =>
        {
            using var f = Fixture.Plugins(true, false, true);
            Exception? error = Capture(() => PluginManager.CompileAndLoadFrom(f.Root));
            Check(error is InvalidOperationException && error.Message == "fresh-constructor-probe",
                "the public loader swallowed all constructor failures as empty success");
        });
        Case("partial fresh construction cleans up predecessors and propagates the failure", () =>
        {
            using var f = Fixture.Plugins(true, false, false);
            Exception? error = Capture(() => PluginManager.CompileAndLoadFrom(f.Root));
            Check(error is InvalidOperationException && error.Message == "fresh-constructor-probe",
                "the public loader returned a partial plugin set");
            Check(f.ReadCounter("Created") == f.ReadCounter("Disposed"), "constructed predecessors leaked on rejection");
        });
        Case("fresh cleanup failure does not replace the constructor error", () =>
        {
            using var f = Fixture.Plugins(true, true, false);
            Exception? error = Capture(() => PluginManager.CompileAndLoadFrom(f.Root));
            Check(error is InvalidOperationException && error.Message == "fresh-constructor-probe",
                "cleanup hid the first constructor failure");
            Check(f.ReadCounter("Created") == f.ReadCounter("Disposed"), "cleanup was not attempted for all new predecessors");
        });
        Case("CSharp compilation errors keep their existing diagnostic exception", () =>
        {
            using var f = new Fixture("public sealed class Broken { this is not CSharp; }");
            Check(Capture(() => PluginManager.CompileAndLoadFrom(f.Root)) is CompilerErrorsException,
                "public loader lost the compiler diagnostic exception");
        });

        Console.WriteLine($"Plugin fresh compilation scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; actual public PluginManager and SourceCompiler; temporary source; no installed plugins or game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Plugin fresh compilation regression");
    }

    private static Exception? Capture(Action action)
    {
        try { action(); return null; }
        catch (Exception error)
        {
            while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
            return error;
        }
    }
    private static void Check(bool ok, string why) { if (!ok) throw new Failure(why); }

    private sealed class Fixture : IDisposable
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "cb-plugin-fresh-" + Guid.NewGuid().ToString("N"));
        private string? stateType;
        internal Fixture(string? source)
        {
            Directory.CreateDirectory(Root);
            if (source != null) File.WriteAllText(Path.Combine(Root, "Probe.cs"), source);
        }
        internal static Fixture Plugins(bool failSecond, bool failDispose, bool onlyFailing)
        {
            string id = Guid.NewGuid().ToString("N");
            string state = "FreshState_" + id;
            string properties = "public override string Name => GetType().Name; public override string Author => \"test\"; " +
                "public override Version Version => new Version(1,0); public override void Pulse() {} ";
            string source = "using System; using Styx.Plugins.PluginClass; public static class " + state +
                " { public static int Created, Disposed; } ";
            if (!onlyFailing)
                source += "public sealed class FreshFirst_" + id + " : HBPlugin { public FreshFirst_" + id +
                    "() { " + state + ".Created++; } " + properties +
                    "public override void Dispose() { " + state + ".Disposed++; " +
                    (failDispose ? "throw new InvalidOperationException(\"fresh-dispose-probe\");" : "") + " } } ";
            source += "public sealed class FreshSecond_" + id + " : HBPlugin { public FreshSecond_" + id +
                "() { " + (failSecond ? "throw new InvalidOperationException(\"fresh-constructor-probe\");" : state + ".Created++;") +
                " } " + properties + "public override void Dispose() { " + state + ".Disposed++; } }";
            return new Fixture(source) { stateType = state };
        }
        internal int ReadCounter(string field)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(stateType!, false)).First(t => t != null)!;
            return (int)type.GetField(field)!.GetValue(null)!;
        }
        public void Dispose() { Directory.Delete(Root, true); }
    }
}
