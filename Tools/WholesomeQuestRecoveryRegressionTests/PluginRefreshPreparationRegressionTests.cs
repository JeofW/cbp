using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Styx.Plugins;
using Styx.Plugins.PluginClass;

// Runs only in the hosted test output directory. Uses public RefreshPlugins,
// its actual Roslyn compiler/cache and generated plugins with lifecycle counters.
internal static class PluginRefreshPreparationRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        int passed = 0, assertions = 0, unexpected = 0, total = 0;
        void Case(string name, Action test)
        {
            total++;
            try { test(); passed++; Console.WriteLine("PASS plugin refresh preparation: " + name); }
            catch (Failure e) { assertions++; Console.Error.WriteLine("FAIL plugin refresh preparation: " + name + ": " + e.Message); }
            catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR plugin refresh preparation: " + name + ": " + e); }
        }

        foreach (bool exists in new[] { false, true })
            Case((exists ? "empty" : "missing") + " source directory still publishes built-ins and retires the stale set", () =>
            {
                using var f = new Fixture(exists);
                PluginManager.RefreshPlugins();
                Check(!ReferenceEquals(PluginManager.Plugins, f.Previous), "stale list survived a valid empty discovery");
                Check(!PluginManager.Plugins.Contains(f.Old), "removed source plugin remained loaded");
                Check(PluginManager.Plugins.Any(p => p.Plugin.GetType().FullName == "PartyBot.LeaderPlugin"), "built-in plugin was discarded by early return");
                Check(!f.Old.Enabled && f.OldCount("Disabled") == 1 && f.OldCount("Disposed") == 1, "old enabled instance was not retired once");
                Check(!PluginManager.IsBuildingPlugins, "build flag remained set");
            });

        Case("name failure preserves the exact active set and disposes the rejected candidate", () =>
        {
            using var f = new Fixture(true);
            f.AddCandidate(true);
            Exception? error = Capture(() => PluginManager.RefreshPlugins(f.CandidateName));
            Check(error is InvalidOperationException && error.Message == "name-probe", "metadata failure was not propagated");
            Check(ReferenceEquals(PluginManager.Plugins, f.Previous) && f.Old.Enabled, "metadata failure replaced or disabled the active set");
            Check(f.OldCount("Disabled") == 0 && f.OldCount("Disposed") == 0, "metadata failure retired the old instance");
            Check(f.CandidateCount("Created") == 1 && f.CandidateCount("Disposed") == 1, "rejected candidate leaked");
            Check(f.CandidateCount("Initialized") == 0 && f.CandidateCount("Enabled") == 0, "candidate activated before preparation succeeded");
            Check(!PluginManager.IsBuildingPlugins, "failed preparation left build flag set");
        });

        Case("unchanged cached candidate can recover after a metadata failure", () =>
        {
            using var f = new Fixture(true);
            f.AddCandidate(true);
            Capture(() => PluginManager.RefreshPlugins(f.CandidateName));
            Type candidateType = f.CandidateType;
            candidateType.GetField("FailName")!.SetValue(null, false);
            PluginManager.RefreshPlugins(f.CandidateName);
            PluginContainer current = PluginManager.Plugins.Single(p => p.Plugin.GetType() == candidateType);
            Check(current.Enabled && f.CandidateCount("Initialized") == 1 && f.CandidateCount("Enabled") == 1, "retry did not activate one fresh cached instance");
            Check(f.CandidateCount("Created") == 2 && f.CandidateCount("Disposed") == 1, "retry leaked the rejected instance or recompiled its static state");
            Check(!f.Old.Enabled && f.OldCount("Disabled") == 1 && f.OldCount("Disposed") == 1, "retry did not retire the original instance exactly once");
        });

        Case("compiler failure preserves the active set without lifecycle callbacks", () =>
        {
            using var f = new Fixture(true);
            File.WriteAllText(Path.Combine(f.PluginDirectory, "Broken.cs"), "public class Broken { not CSharp; }");
            Check(Capture(() => PluginManager.RefreshPlugins()) is InvalidOperationException, "compiler failure was swallowed");
            Check(ReferenceEquals(PluginManager.Plugins, f.Previous) && f.Old.Enabled, "compile failure replaced the previous list");
            Check(f.OldCount("Disabled") == 0 && f.OldCount("Disposed") == 0, "compile failure retired old instance");
            Check(!PluginManager.IsBuildingPlugins, "compile failure left build flag set");
        });

        Case("healthy replacement evaluates names once and enables only the requested candidate", () =>
        {
            using var f = new Fixture(true);
            f.AddCandidate(false);
            PluginManager.RefreshPlugins(f.CandidateName.ToUpperInvariant());
            PluginContainer current = PluginManager.Plugins.Single(p => p.Plugin.GetType() == f.CandidateType);
            Check(current.Enabled && f.CandidateCount("Initialized") == 1 && f.CandidateCount("Enabled") == 1, "requested replacement was not activated");
            Check(f.CandidateCount("NameReads") == 1, "metadata was reevaluated during publication");
            Check(PluginManager.Plugins.Where(p => !ReferenceEquals(p, current)).All(p => !p.Enabled), "unrequested plugin was enabled");
            Check(f.OldCount("Disabled") == 1 && f.OldCount("Disposed") == 1, "healthy replacement did not retire old instance once");
        });

        Console.WriteLine($"Plugin refresh preparation scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; actual public refresh/compiler/cache; hosted output directory; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Plugin refresh preparation regression");
    }

    private static Exception? Capture(Action action)
    {
        try { action(); return null; }
        catch (Exception error) { return error; }
    }
    private static void Check(bool ok, string why) { if (!ok) throw new Failure(why); }

    private sealed class Fixture : IDisposable
    {
        internal readonly string PluginDirectory = Path.Combine(Path.GetDirectoryName(typeof(PluginManager).Assembly.Location)!, "Plugins");
        private readonly string root = Path.Combine(Path.GetTempPath(), "cb-refresh-preparation-" + Guid.NewGuid().ToString("N"));
        private readonly List<PluginContainer> saved = PluginManager.Plugins;
        private readonly bool savedTeardown = PluginManager.IsTearingDown;
        private readonly string candidateTypeName = "Candidate_" + Guid.NewGuid().ToString("N");
        internal readonly string CandidateName = "candidate-" + Guid.NewGuid().ToString("N");
        internal readonly PluginContainer Old;
        internal readonly List<PluginContainer> Previous;

        internal Fixture(bool createDirectory)
        {
            // Never rename, clear or overwrite an existing plugin installation.
            if (Directory.Exists(PluginDirectory) || File.Exists(PluginDirectory))
                throw new InvalidOperationException("Fixture requires an unoccupied test-output Plugins path.");
            if (PluginManager.IsBuildingPlugins) throw new InvalidOperationException("Refresh already in progress.");
            Directory.CreateDirectory(root);
            string oldType = "Old_" + Guid.NewGuid().ToString("N");
            File.WriteAllText(Path.Combine(root, "Old.cs"), Source(oldType, "old", false));
            HBPlugin old = PluginManager.CompileAndLoadFrom(root).Single();
            PluginManager.IsTearingDown = true; // Prevent settings writes from test setup/cleanup.
            Old = new PluginContainer(old, true);
            Previous = new List<PluginContainer> { Old };
            SetPlugins(Previous);
            if (createDirectory) Directory.CreateDirectory(PluginDirectory);
        }

        internal void AddCandidate(bool failName) => File.WriteAllText(
            Path.Combine(PluginDirectory, "Candidate.cs"), Source(candidateTypeName, CandidateName, failName));
        internal Type CandidateType => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(candidateTypeName, false)).First(t => t != null)!;
        internal int CandidateCount(string field) => Count(CandidateType, field);
        internal int OldCount(string field) => Count(Old.Plugin.GetType(), field);
        private static int Count(Type type, string field) => (int)type.GetField(field)!.GetValue(null)!;
        private static void SetPlugins(List<PluginContainer> plugins) => typeof(PluginManager)
            .GetProperty(nameof(PluginManager.Plugins))!.GetSetMethod(true)!.Invoke(null, new object[] { plugins });

        private static string Source(string type, string name, bool failName) =>
            "using System; using Styx.Plugins.PluginClass; public sealed class " + type + " : HBPlugin { " +
            "public static int Created, Disposed, Initialized, Enabled, Disabled, NameReads; public static bool FailName = " + (failName ? "true" : "false") + "; " +
            "public " + type + "() { Created++; } public override string Name { get { NameReads++; if (FailName) throw new InvalidOperationException(\"name-probe\"); return \"" + name + "\"; } } " +
            "public override string Author => \"test\"; public override Version Version => new Version(1,0); public override void Pulse() {} " +
            "public override void Initialize() { Initialized++; } public override void OnEnable() { Enabled++; } " +
            "public override void OnDisable() { Disabled++; } public override void Dispose() { Disposed++; } }";

        public void Dispose()
        {
            try
            {
                foreach (PluginContainer p in PluginManager.Plugins)
                    if (p.Enabled) p.Enabled = false;
                if (Old.Enabled) Old.Enabled = false;
                SetPlugins(saved);
                // Both directories were proven absent or freshly created by this fixture.
                if (Directory.Exists(PluginDirectory)) Directory.Delete(PluginDirectory, true);
                Directory.Delete(root, true);
            }
            finally { PluginManager.IsTearingDown = savedTeardown; }
        }
    }
}
