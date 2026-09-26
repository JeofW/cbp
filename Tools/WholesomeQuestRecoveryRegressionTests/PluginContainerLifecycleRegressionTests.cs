using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Styx.Plugins;
using Styx.Plugins.PluginClass;

// Actual public container, refresh and pulse paths; generated plugins only.
// No plugin subclass is added to this test assembly's cached-construction set.
internal static class PluginContainerLifecycleRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }
    private static Type probeType = null!;
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;

    [ModuleInitializer]
    internal static void Run()
    {
        int passed = 0, assertions = 0, unexpected = 0, total = 0;
        string root = Path.Combine(Path.GetTempPath(), "cb-container-lifecycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string typeName = "Lifecycle_" + Guid.NewGuid().ToString("N");
            File.WriteAllText(Path.Combine(root, "Probe.cs"), Source(typeName, false));
            HBPlugin compiled = PluginManager.CompileAndLoadFrom(root).Single();
            probeType = compiled.GetType();
            compiled.Dispose();

            void Case(string name, Action test)
            {
                total++;
                try { test(); passed++; Console.WriteLine("PASS plugin container lifecycle: " + name); }
                catch (Failure e) { assertions++; Console.Error.WriteLine("FAIL plugin container lifecycle: " + name + ": " + e.Message); }
                catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR plugin container lifecycle: " + name + ": " + e); }
            }

            Case("healthy repeated assignments run each transition once", () =>
            {
                using var f = new Fixture();
                f.Container.Enabled = true;
                f.Container.Enabled = true;
                Check(f.Container.Enabled && f.Trace == "init;enable;", "healthy enable was skipped or duplicated");
                f.Container.Enabled = false;
                f.Container.Enabled = false;
                Check(!f.Container.Enabled && f.Trace == "init;enable;disable;dispose;", "healthy disable was skipped or duplicated");
            });
            Case("Initialize failure stays disabled and releases partial resources", () =>
            {
                using var f = new Fixture();
                f.Flag("FailInitialize", true);
                f.Container.Enabled = true;
                Check(!f.Container.Enabled, "failed initialization was advertised as enabled");
                Check(f.Trace == "init;disable;dispose;", "partial initialization was not cleaned up or OnEnable ran");
                Pulse();
                Check(f.Count("Pulses") == 0, "failed plugin was pulsed");
            });
            Case("OnEnable failure stays disabled and revokes partial activation", () =>
            {
                using var f = new Fixture();
                f.Flag("FailEnable", true);
                f.Container.Enabled = true;
                Check(!f.Container.Enabled && f.Trace == "init;enable;disable;dispose;", "failed activation remained live or leaked");
                Pulse();
                Check(f.Count("Pulses") == 0, "failed activation reached Pulse");
            });
            Case("failed activation can be retried without an extra disable", () =>
            {
                using var f = new Fixture();
                f.Flag("FailEnable", true);
                f.Container.Enabled = true;
                f.Flag("FailEnable", false);
                f.Container.Enabled = true;
                Check(f.Container.Enabled && f.Trace == "init;enable;disable;dispose;init;enable;", "retry did not execute a fresh activation attempt");
            });
            Case("OnDisable failure cannot skip Dispose", () =>
            {
                using var f = new Fixture();
                f.Container.Enabled = true;
                f.Flag("FailDisable", true);
                f.Container.Enabled = false;
                f.Container.Enabled = false;
                Check(!f.Container.Enabled && f.Trace == "init;enable;disable;dispose;", "disable failure skipped or duplicated disposal");
            });
            Case("failed activation cleanup still disposes when OnDisable throws", () =>
            {
                using var f = new Fixture();
                f.Flag("FailEnable", true);
                f.Flag("FailDisable", true);
                f.Container.Enabled = true;
                Check(!f.Container.Enabled && f.Trace == "init;enable;disable;dispose;", "cleanup exception kept a failed plugin active or leaked it");
            });
            Case("enabled notifications describe completed activation", () =>
            {
                using var f = new Fixture();
                var observed = new List<string>();
                f.Container.PropertyChanged += (_, e) => { if (e.PropertyName == "Enabled") observed.Add(f.Container.Enabled + ":" + f.Trace); };
                f.Container.Enabled = true;
                Check(observed.SequenceEqual(new[] { "True:init;enable;" }), "observer saw an enabled plugin before activation completed");
            });
            Case("failed activation notifies a disabled state for binding recovery", () =>
            {
                using var f = new Fixture();
                f.Flag("FailInitialize", true);
                var observed = new List<bool>();
                f.Container.PropertyChanged += (_, e) => { if (e.PropertyName == "Enabled") observed.Add(f.Container.Enabled); };
                f.Container.Enabled = true;
                Check(observed.SequenceEqual(new[] { false }), "failed enable notification did not restore the disabled state");
            });
            Case("throwing change observer cannot prevent disable cleanup", () =>
            {
                using var f = new Fixture();
                f.Container.Enabled = true;
                f.Container.PropertyChanged += (_, _) => throw new InvalidOperationException("observer-probe");
                try { f.Container.Enabled = false; } catch (InvalidOperationException e) when (e.Message == "observer-probe") { }
                Check(!f.Container.Enabled && f.Trace == "init;enable;disable;dispose;", "observer interrupted plugin cleanup");
            });
            Case("Dispose failure leaves the plugin disabled and does not repeat cleanup", () =>
            {
                using var f = new Fixture();
                f.Container.Enabled = true;
                f.Flag("FailDispose", true);
                f.Container.Enabled = false;
                f.Container.Enabled = false;
                Check(!f.Container.Enabled && f.Trace == "init;enable;disable;dispose;", "Dispose failure kept enabled state or repeated cleanup");
            });
            Case("public refresh does not pulse a replacement whose activation failed", () =>
            {
                using var f = new Fixture();
                using var sources = new RefreshSource(true);
                PluginManager.RefreshPlugins(sources.TypeName);
                PluginContainer candidate = PluginManager.Plugins.Single(p => p.Plugin.GetType().Name == sources.TypeName);
                Check(!candidate.Enabled, "refresh published a failed replacement as enabled");
                Pulse();
                Check((int)candidate.Plugin.GetType().GetField("Pulses")!.GetValue(candidate.Plugin)! == 0, "refresh pulsed failed replacement");
                Check((string)candidate.Plugin.GetType().GetField("Trace")!.GetValue(candidate.Plugin)! == "init;disable;dispose;", "refresh activation cleanup was incomplete");
            });
            Case("public refresh disposes old plugin despite its OnDisable exception", () =>
            {
                using var f = new Fixture();
                f.Container.Enabled = true;
                f.Flag("FailDisable", true);
                using var sources = new RefreshSource(false);
                PluginManager.RefreshPlugins(sources.TypeName);
                Check(f.Trace == "init;enable;disable;dispose;", "refresh leaked the retired plugin");
                Check(PluginManager.Plugins.Single(p => p.Plugin.GetType().Name == sources.TypeName).Enabled, "old cleanup failure prevented healthy replacement activation");
            });

            Console.WriteLine($"Plugin container lifecycle scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; actual container/refresh/pulse and SourceCompiler; generated plugins; no game attached.");
            if (assertions + unexpected != 0) throw new InvalidOperationException("Plugin container lifecycle regression");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Check(bool ok, string why) { if (!ok) throw new Failure(why); }
    private static void Pulse() => typeof(PluginManager).GetMethod("Pulse", Hidden)!.Invoke(null, null);
    private static void SetPlugins(List<PluginContainer> plugins) => typeof(PluginManager).GetProperty(nameof(PluginManager.Plugins))!
        .GetSetMethod(true)!.Invoke(null, new object[] { plugins });
    private static string Source(string name, bool failInitialize) =>
        "using System; using Styx.Plugins.PluginClass; public sealed class " + name + " : HBPlugin { " +
        "public string Trace = \"\"; public int Pulses; public bool FailInitialize = " + (failInitialize ? "true" : "false") + "; public bool FailEnable, FailDisable, FailDispose; " +
        "public override string Name => GetType().Name; public override string Author => \"test\"; public override Version Version => new Version(1,0); public override void Pulse() { Pulses++; } " +
        "public override void Initialize() { Trace += \"init;\"; if (FailInitialize) throw new InvalidOperationException(\"init-probe\"); } " +
        "public override void OnEnable() { Trace += \"enable;\"; if (FailEnable) throw new InvalidOperationException(\"enable-probe\"); } " +
        "public override void OnDisable() { Trace += \"disable;\"; if (FailDisable) throw new InvalidOperationException(\"disable-probe\"); } " +
        "public override void Dispose() { Trace += \"dispose;\"; if (FailDispose) throw new InvalidOperationException(\"dispose-probe\"); } }";

    private sealed class Fixture : IDisposable
    {
        private readonly List<PluginContainer> saved = PluginManager.Plugins;
        private readonly bool savedTeardown = PluginManager.IsTearingDown;
        internal readonly PluginContainer Container;
        internal Fixture()
        {
            PluginManager.IsTearingDown = true;
            Container = new PluginContainer((HBPlugin)Activator.CreateInstance(probeType)!, false);
            SetPlugins(new List<PluginContainer> { Container });
        }
        internal void Flag(string name, bool value) => probeType.GetField(name)!.SetValue(Container.Plugin, value);
        internal int Count(string name) => (int)probeType.GetField(name)!.GetValue(Container.Plugin)!;
        internal string Trace => (string)probeType.GetField("Trace")!.GetValue(Container.Plugin)!;
        public void Dispose()
        {
            try
            {
                foreach (PluginContainer p in PluginManager.Plugins)
                    if (p.Enabled) { try { p.Enabled = false; } catch { } }
                if (Container.Enabled) { try { Container.Enabled = false; } catch { } }
                SetPlugins(saved);
            }
            finally { PluginManager.IsTearingDown = savedTeardown; }
        }
    }

    private sealed class RefreshSource : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetDirectoryName(typeof(PluginManager).Assembly.Location)!, "Plugins");
        internal readonly string TypeName = "RefreshLifecycle_" + Guid.NewGuid().ToString("N");
        internal RefreshSource(bool failInitialize)
        {
            if (Directory.Exists(directory) || File.Exists(directory)) throw new InvalidOperationException("Fixture requires an unoccupied test-output Plugins path.");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "Probe.cs"), Source(TypeName, failInitialize));
        }
        public void Dispose() { Directory.Delete(directory, true); }
    }
}
