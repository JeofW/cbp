using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Styx.Plugins;
using Styx.Plugins.PluginClass;

internal static class PluginCachedConstructionRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }
    private const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    [ModuleInitializer]
    internal static void Run()
    {
        MethodInfo instantiate = typeof(PluginManager).GetMethod("InstantiatePluginTypes", Hidden)
            ?? throw new InvalidOperationException("Tracked constructor boundary missing");
        MethodInfo load = typeof(PluginManager).GetMethod("LoadPluginPathWithCache", Hidden)
            ?? throw new InvalidOperationException("Tracked cache boundary missing");
        using var fixture = new CompiledTypes();
        int passed = 0, assertions = 0, unexpected = 0, total = 0;
        void Case(string name, Action test)
        {
            total++;
            fixture.Reset();
            try { test(); passed++; Console.WriteLine("PASS plugin construction: " + name); }
            catch (Failure e) { assertions++; Console.Error.WriteLine("FAIL plugin construction: " + name + ": " + e.Message); }
            catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR plugin construction: " + name + ": " + e); }
        }

        Case("healthy constructor set returns complete fresh instances", () =>
        {
            List<HBPlugin> first = Instantiate(instantiate, fixture.Types);
            List<HBPlugin> second = Instantiate(instantiate, fixture.Types);
            Check(first.Count == 2 && second.Count == 2, "healthy type set was incomplete");
            Check(!ReferenceEquals(first[0], second[0]) && !ReferenceEquals(first[1], second[1]),
                "constructor boundary reused plugin instances");
        });
        Case("failed constructor escapes instead of returning a subset", () =>
        {
            fixture.FailConstructor = true;
            Exception? error = Capture(() => Instantiate(instantiate, fixture.Types));
            Check(error is InvalidOperationException && error.Message == "constructor-probe",
                "failed constructor was swallowed or replaced with another error");
        });
        Case("failed construction disposes successful predecessors only", () =>
        {
            HBPlugin existing = fixture.CreateFirst();
            fixture.FailConstructor = true;
            Capture(() => Instantiate(instantiate, fixture.Types));
            Check(fixture.Disposals == 1, "newly constructed predecessor was not disposed exactly once");
            Check(!fixture.WasDisposed(existing), "an existing working instance was disposed");
        });
        Case("cleanup failure cannot hide the original constructor failure", () =>
        {
            fixture.FailConstructor = true;
            fixture.FailDispose = true;
            Exception? error = Capture(() => Instantiate(instantiate, fixture.Types));
            Check(error is InvalidOperationException && error.Message == "constructor-probe",
                "cleanup masked or swallowed the constructor failure");
            Check(fixture.Disposals == 1, "cleanup was not attempted before propagating failure");
        });
        Case("cached constructor failure preserves prior instances and retryable type set", () =>
        {
            string path = fixture.NewInput();
            int compiles = 0;
            Func<string, List<HBPlugin>> compiler = _ => { compiles++; return fixture.CreateBoth(); };
            List<HBPlugin> original = Load(load, path, compiler);
            fixture.FailConstructor = true;
            Exception? error = Capture(() => Load(load, path, compiler));
            Check(error is InvalidOperationException && error.Message == "constructor-probe",
                "cache hit returned a partial replacement after a constructor failed");
            Check(!fixture.WasDisposed(original[0]), "cached reconstruction disposed an existing instance");
            fixture.FailConstructor = false;
            List<HBPlugin> retry = Load(load, path, compiler);
            Check(compiles == 1 && retry.Count == 2 && !ReferenceEquals(retry[0], original[0]),
                "constructor failure poisoned the complete cached type set or reused old instances");
        });
        Case("partial fresh compiler result cannot be returned as a complete replacement", () =>
        {
            string path = fixture.NewInput();
            HBPlugin partial = fixture.CreateFirst();
            Exception? error = Capture(() => Load(load, path, _ => new List<HBPlugin> { partial }));
            Check(error is InvalidOperationException, "partial compiled type set was published without failure");
            Check(fixture.WasDisposed(partial), "partial newly constructed result was leaked on rejection");
        });
        Case("partial changed-source result does not discard a valid earlier cache", () =>
        {
            string path = fixture.NewInput();
            int compiles = 0;
            Func<string, List<HBPlugin>> compiler = _ => { compiles++; return fixture.CreateBoth(); };
            List<HBPlugin> original = Load(load, path, compiler);
            string file = Path.Combine(path, "Input.cs");
            string before = File.ReadAllText(file);
            File.AppendAllText(file, "\n// changed");
            HBPlugin partial = fixture.CreateFirst();
            Exception? error = Capture(() => Load(load, path, _ => new List<HBPlugin> { partial }));
            Check(error is InvalidOperationException && fixture.WasDisposed(partial),
                "partial changed-source construction was not rejected and cleaned up");
            File.WriteAllText(file, before);
            List<HBPlugin> restored = Load(load, path, compiler);
            Check(compiles == 1 && restored.Count == 2 && !fixture.WasDisposed(original[0]),
                "rejecting a partial replacement invalidated or disposed the previous working set");
        });

        Console.WriteLine($"Plugin cached construction scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; actual PluginManager methods and separately compiled types; controlled compiler results; no installed plugins or game attached.");
        if (assertions + unexpected != 0)
            throw new InvalidOperationException("Plugin cached construction regression");
    }

    private static List<HBPlugin> Instantiate(MethodInfo method, Type[] types) =>
        (List<HBPlugin>)method.Invoke(null, new object[] { types })!;
    private static List<HBPlugin> Load(MethodInfo method, string path, Func<string, List<HBPlugin>> compile) =>
        (List<HBPlugin>)method.Invoke(null, new object[] { path, compile })!;
    private static Exception? Capture(Action action)
    {
        try { action(); return null; }
        catch (Exception e)
        {
            while (e is TargetInvocationException && e.InnerException != null) e = e.InnerException;
            return e;
        }
    }
    private static void Check(bool ok, string why) { if (!ok) throw new Failure(why); }

    private sealed class CompiledTypes : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "cb-plugin-construct-" + Guid.NewGuid().ToString("N"));
        private readonly Type first;
        private readonly Type second;
        internal Type[] Types => new[] { first, second };
        internal bool FailConstructor { set => second.GetField("Fail")!.SetValue(null, value); }
        internal bool FailDispose { set => first.GetField("FailDispose")!.SetValue(null, value); }
        internal int Disposals => (int)first.GetField("Disposals")!.GetValue(null)!;
        internal bool WasDisposed(HBPlugin plugin) => (bool)first.GetField("WasDisposed")!.GetValue(plugin)!;
        internal HBPlugin CreateFirst() => (HBPlugin)Activator.CreateInstance(first)!;
        internal List<HBPlugin> CreateBoth() => new() { CreateFirst(), (HBPlugin)Activator.CreateInstance(second)! };
        internal void Reset() { FailConstructor = false; FailDispose = false; first.GetField("Disposals")!.SetValue(null, 0); }

        internal CompiledTypes()
        {
            Directory.CreateDirectory(root);
            string sourceRoot = Path.Combine(root, "types");
            Directory.CreateDirectory(sourceRoot);
            string suffix = Guid.NewGuid().ToString("N");
            string firstName = "ConstructionFirst_" + suffix;
            string secondName = "ConstructionSecond_" + suffix;
            string source = "using System; using Styx.Plugins.PluginClass; " +
                "public sealed class " + firstName + " : HBPlugin { " +
                "public static int Disposals; public static bool FailDispose; public bool WasDisposed; " +
                "public override string Name => \"first\"; public override string Author => \"test\"; " +
                "public override Version Version => new Version(1,0); public override void Pulse() {} " +
                "public override void Dispose() { WasDisposed=true; Disposals++; if(FailDispose) throw new InvalidOperationException(\"dispose-probe\"); } } " +
                "public sealed class " + secondName + " : HBPlugin { public static bool Fail; " +
                "public " + secondName + "() { if(Fail) throw new InvalidOperationException(\"constructor-probe\"); } " +
                "public override string Name => \"second\"; public override string Author => \"test\"; " +
                "public override Version Version => new Version(1,0); public override void Pulse() {} }";
            File.WriteAllText(Path.Combine(sourceRoot, "Probes.cs"), source);
            try
            {
                Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
                object compiler = Activator.CreateInstance(compilerType, new object[] { sourceRoot })!;
                var result = (CompilerResults?)compilerType.GetMethod("Compile")!.Invoke(compiler, null);
                string[] errors = result == null ? new[] { "missing compilation result" }
                    : result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).Select(e => e.ToString()).ToArray();
                if (errors.Length != 0) throw new InvalidOperationException(string.Join(" | ", errors));
                var assembly = (Assembly?)compilerType.GetProperty("CompiledAssembly")!.GetValue(compiler);
                first = assembly!.GetType(firstName, true)!;
                second = assembly.GetType(secondName, true)!;
            }
            catch { Directory.Delete(root, true); throw; }
        }
        internal string NewInput()
        {
            string path = Path.Combine(root, "input-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "Input.cs"), "// controlled fingerprint input\n");
            return path;
        }
        public void Dispose() { Directory.Delete(root, true); }
    }
}
