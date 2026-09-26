using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Styx.Plugins;
using Styx.Plugins.PluginClass;

// Public refresh consumes a real generated reference DLL through SourceCompiler.
// Constants prove recompilation, not runtime replacement of an already loaded DLL.
internal static class PluginCompilationInputRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }
    private const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    [ModuleInitializer]
    internal static void Run()
    {
        int passed = 0, assertions = 0, unexpected = 0, total = 0;
        void Case(string name, Action test)
        {
            total++;
            try { test(); passed++; Console.WriteLine("PASS plugin compilation inputs: " + name); }
            catch (Failure e) { assertions++; Console.Error.WriteLine("FAIL plugin compilation inputs: " + name + ": " + e.Message); }
            catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR plugin compilation inputs: " + name + ": " + e); }
        }

        Case("unchanged actual inputs reuse statics but create fresh instances", () =>
        {
            using var f = new Fixture();
            PluginContainer first = f.Refresh();
            first.Plugin.GetType().GetField("InstanceValue")!.SetValue(first.Plugin, 99);
            PluginContainer second = f.Refresh();
            Check(!ReferenceEquals(first.Plugin, second.Plugin) && first.Plugin.GetType() == second.Plugin.GetType(), "unchanged inputs recompiled or reused an instance");
            Check(f.Read(second, "Created") == 2 && f.Read(second, "InstanceValue") == 7, "retained static/fresh instance policy changed");
            Check(f.Read(first, "Disposed") == 1 && second.Enabled, "old instance was not retired exactly once");
        });
        Case("DLL-only content change invalidates cached code even with restored timestamp", () =>
        {
            using var f = new Fixture();
            PluginContainer first = f.Refresh();
            byte[] source = File.ReadAllBytes(f.PluginSource);
            DateTime timestamp = File.GetLastWriteTimeUtc(f.ReferencePath);
            f.ReplaceDependency(29, false);
            File.SetLastWriteTimeUtc(f.ReferencePath, timestamp);
            PluginContainer second = f.Refresh();
            Check(File.ReadAllBytes(f.PluginSource).SequenceEqual(source), "fixture changed plugin source");
            Check(f.Value(first) == 11 && f.Value(second) == 29, "unchanged-source refresh retained the old referenced constant");
            Check(first.Plugin.GetType() != second.Plugin.GetType() && f.Read(second, "Created") == 1, "changed inputs did not load fresh plugin statics");
        });
        Case("missing required reference preserves the exact active set", () =>
        {
            using var f = new Fixture();
            PluginContainer first = f.Refresh();
            List<PluginContainer> previous = PluginManager.Plugins;
            File.Delete(f.ReferencePath);
            Check(Capture(() => f.Refresh()) is InvalidOperationException, "missing reference reused cached code as success");
            Check(ReferenceEquals(previous, PluginManager.Plugins) && first.Enabled && f.Read(first, "Disposed") == 0, "missing reference retired or replaced the active set");
        });
        Case("invalid reference bytes cannot authorize stale cached types", () =>
        {
            using var f = new Fixture();
            PluginContainer first = f.Refresh();
            List<PluginContainer> previous = PluginManager.Plugins;
            File.WriteAllBytes(f.ReferencePath, new byte[] { 1, 2, 3, 4 });
            Check(Capture(() => f.Refresh()) is InvalidOperationException, "invalid reference reused cached code as success");
            Check(ReferenceEquals(previous, PluginManager.Plugins) && first.Enabled && f.Read(first, "Disposed") == 0, "invalid reference changed the active set");
        });
        Case("failed dependency compilation preserves the last valid cache entry", () =>
        {
            using var f = new Fixture();
            PluginContainer first = f.Refresh();
            byte[] original = File.ReadAllBytes(f.ReferencePath);
            File.WriteAllBytes(f.ReferencePath, new byte[] { 0 });
            Capture(() => f.Refresh());
            File.WriteAllBytes(f.ReferencePath, original);
            PluginContainer restored = f.Refresh();
            Check(restored.Plugin.GetType() == first.Plugin.GetType() && f.Read(restored, "Created") == 2, "failed attempt replaced or bypassed the last valid cached type set");
            Check(first.Enabled == false && f.Read(first, "Disposed") == 1 && restored.Enabled, "restored dependency did not retire the original once");
        });
        Case("dependency-triggered constructor failure preserves old instances", () =>
        {
            using var f = new Fixture();
            PluginContainer first = f.Refresh();
            List<PluginContainer> previous = PluginManager.Plugins;
            f.ReplaceDependency(29, true);
            Check(Capture(() => f.Refresh()) is InvalidOperationException, "required constructor was bypassed by stale cache reuse");
            Check(ReferenceEquals(previous, PluginManager.Plugins) && first.Enabled && f.Read(first, "Disposed") == 0, "constructor failure replaced or disposed old instances");
        });
        Case("source edit resets plugin statics through a new compiled type", () =>
        {
            using var f = new Fixture();
            PluginContainer first = f.Refresh();
            File.AppendAllText(f.PluginSource, "\n// source revision\n");
            PluginContainer second = f.Refresh();
            Check(first.Plugin.GetType() != second.Plugin.GetType() && f.Read(second, "Created") == 1, "changed-source reload retained old plugin statics");
            Check(f.Value(second) == 11 && f.Read(first, "Disposed") == 1, "source reload changed dependency result or leaked the old instance");
        });
        Case("actual compiler honors effective optimization options without source edits", () =>
        {
            string root = NewRoot();
            try
            {
                string path = Path.Combine(root, "Options.cs");
                File.WriteAllText(path, "public static class OptionsProbe { public static int Value() { int value = 7; return value; } }");
                byte[] original = File.ReadAllBytes(path);
                Assembly debug = Compile(path, false);
                Assembly optimized = Compile(path, true);
                Check(debug.GetCustomAttribute<DebuggableAttribute>()?.IsJITOptimizerDisabled == true && optimized.GetCustomAttribute<DebuggableAttribute>()?.IsJITOptimizerDisabled != true, "effective optimization option was not applied by the compiler");
                Check((int)debug.GetType("OptionsProbe")!.GetMethod("Value")!.Invoke(null, null)! == 7 &&
                    (int)optimized.GetType("OptionsProbe")!.GetMethod("Value")!.Invoke(null, null)! == 7 && File.ReadAllBytes(path).SequenceEqual(original), "options fixture changed source or observable result");
            }
            finally { Directory.Delete(root, true); }
        });

        Console.WriteLine($"Plugin compilation input scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; actual public refresh/compiler/referenced DLL; optimization compiler control; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Plugin compilation input regression");
    }

    private static Exception? Capture(Action action) { try { action(); return null; } catch (Exception e) { return e; } }
    private static void Check(bool ok, string why) { if (!ok) throw new Failure(why); }
    private static string NewRoot()
    {
        string path = Path.Combine(Path.GetTempPath(), "cb-compilation-input-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
    private static Assembly Compile(string source, bool optimize = false)
    {
        Type type = typeof(PluginManager).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
        object compiler = Activator.CreateInstance(type, new object[] { source })!;
        if (optimize)
        {
            var options = (CompilerParameters)type.GetProperty("Options")!.GetValue(compiler)!;
            options.CompilerOptions += " /optimise";
        }
        var results = (CompilerResults)type.GetMethod("Compile")!.Invoke(compiler, null)!;
        string[] errors = results.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).Select(e => e.ToString()).ToArray();
        if (errors.Length != 0) throw new InvalidOperationException("Dependency/control compilation failed: " + string.Join("; ", errors));
        return (Assembly)type.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string root = NewRoot();
        private readonly string directory = Path.Combine(Path.GetDirectoryName(typeof(PluginManager).Assembly.Location)!, "Plugins");
        private readonly List<PluginContainer> saved = PluginManager.Plugins;
        private readonly bool savedTeardown = PluginManager.IsTearingDown;
        private readonly string helperName = "Reference_" + Guid.NewGuid().ToString("N");
        private readonly string pluginName = "InputPlugin_" + Guid.NewGuid().ToString("N");
        internal readonly string ReferencePath = Path.Combine(Environment.CurrentDirectory, "cb-reference-" + Guid.NewGuid().ToString("N") + ".dll");
        internal string PluginSource => Path.Combine(directory, "Probe.cs");
        internal Fixture()
        {
            if (Directory.Exists(directory) || File.Exists(directory) || File.Exists(ReferencePath))
                throw new InvalidOperationException("Fixture requires unoccupied generated paths.");
            ReplaceDependency(11, false);
            Directory.CreateDirectory(directory);
            File.WriteAllText(PluginSource, "//!CompilerOption:AddRef:" + Path.GetFileName(ReferencePath) + "\n" +
                "using System; using Styx.Plugins.PluginClass; public sealed class " + pluginName + " : HBPlugin { " +
                "public static int Created; public int Disposed, InstanceValue = 7; public int Value => " + helperName + ".Value; " +
                "public " + pluginName + "() { if (" + helperName + ".FailConstructor) throw new InvalidOperationException(\"dependency-constructor\"); Created++; } " +
                "public override string Name => GetType().Name; public override string Author => \"test\"; public override Version Version => new Version(1,0); " +
                "public override void Pulse() {} public override void Dispose() { Disposed++; } }");
            PluginManager.IsTearingDown = true;
            SetPlugins(new List<PluginContainer>());
        }
        internal void ReplaceDependency(int value, bool fail)
        {
            string source = Path.Combine(root, "Reference.cs");
            File.WriteAllText(source, "public static class " + helperName + " { public const int Value = " + value + "; public const bool FailConstructor = " + (fail ? "true" : "false") + "; }");
            Assembly assembly = Compile(source);
            File.Copy(assembly.Location, ReferencePath, true);
        }
        internal PluginContainer Refresh()
        {
            PluginManager.RefreshPlugins(pluginName);
            return PluginManager.Plugins.Single(p => p.Plugin.GetType().Name == pluginName);
        }
        internal int Read(PluginContainer container, string field) => (int)container.Plugin.GetType().GetField(field)!.GetValue(container.Plugin)!;
        internal int Value(PluginContainer container) => (int)container.Plugin.GetType().GetProperty("Value")!.GetValue(container.Plugin)!;
        private static void SetPlugins(List<PluginContainer> plugins) => typeof(PluginManager).GetProperty(nameof(PluginManager.Plugins))!
            .GetSetMethod(true)!.Invoke(null, new object[] { plugins });
        public void Dispose()
        {
            try
            {
                foreach (PluginContainer p in PluginManager.Plugins) if (p.Enabled) p.Enabled = false;
                SetPlugins(saved);
                // Only fixture-owned, previously absent paths are removed.
                Directory.Delete(directory, true);
                File.Delete(ReferencePath);
                Directory.Delete(root, true);
            }
            finally { PluginManager.IsTearingDown = savedTeardown; }
        }
    }
}
