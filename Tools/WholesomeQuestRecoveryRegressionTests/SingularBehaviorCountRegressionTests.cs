using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Styx.Helpers;
using Styx.Combat.CombatRoutine;
using TreeSharp;

// Compile the complete tracked Singular source plus test-only attributed factories.
// Execute the real builder/attributes/selectors, not a replacement matching algorithm.
// No routine rotation, native movement, spell dispatch or live-client acceptance.
internal static class SingularBehaviorCountRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string text) : base(text) { } }
    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Singular discovery tests require Windows x86.");
        using var f = new Fixture();
        var cases = new List<(string Name, System.Action Test)>();
        var specs = Enum.GetValues(f.SpecType).Cast<object>().Where(s => s.ToString() != "Lowbie" && s.ToString() != "Any").ToArray();
        Check(specs.Length == 30, "tracked specialization inventory changed; review test coverage");
        foreach (var spec in specs)
        {
            var s = spec; var c = (WoWClass)(Convert.ToInt32(s) >> 8);
            foreach (string context in new[] { "Normal", "Instances", "Battlegrounds" })
            {
                var x = context;
                cases.Add(($"{c}/{s}/{x}: selected support counts once", () => f.Verify(c, s, x, new[] { "Valid" }, 1, "Valid;")));
                cases.Add(($"{c}/{s}/{x}: filtered factories cannot fabricate support", () => f.Filtered(c, s, x)));
            }
        }
        object arms = Enum.Parse(f.SpecType, "ArmsWarrior");
        cases.Add(("valid support excludes a foreign-class factory", () => f.Verify(WoWClass.Warrior, arms, "Normal", new[] { "MageOnly", "Valid" }, 1, "Valid;")));
        cases.Add(("ignored helper cannot borrow foreign-class support", () => f.Verify(WoWClass.Warrior, arms, "Normal", new[] { "MageOnly", "Ignored" }, 0, "Ignored;", true)));
        cases.Add(("ignored helper retains a zero-count executable tree", () => f.Verify(WoWClass.Warrior, arms, "Normal", new[] { "Ignored" }, 0, "Ignored;", true)));
        cases.Add(("two selected factories retain two support contributions", () => f.Verify(WoWClass.Warrior, arms, "Normal", new[] { "Valid", "Second" }, 2, "Valid;Second;")));
        cases.Add(("failed factory cannot claim installed support", () => f.Verify(WoWClass.Warrior, arms, "Normal", new[] { "Throws" }, 0, "Throws;")));
        cases.Add(("failed factory cannot inflate surviving support", () => f.Verify(WoWClass.Warrior, arms, "Normal", new[] { "Throws", "Valid" }, 1, "Throws;Valid;")));
        cases.Add(("different behavior remains unselected and uncounted", () => f.Verify(WoWClass.Warrior, arms, "Normal", new[] { "RestOnly" }, 0, "")));
        cases.Add(("instance override excludes Normal-only count", () => f.Override("Normal")));
        cases.Add(("instance override excludes Battleground-only count", () => f.Override("Battlegrounds")));
        cases.Add(("priority ordering remains executable and highest first", () => f.Priority(arms)));
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { item.Test(); passed++; Console.WriteLine("PASS Singular behavior count: " + item.Name); }
            catch (AssertionFailure e) { assertions++; Console.Error.WriteLine("FAIL Singular behavior count assertion: " + item.Name + ": " + e.Message); }
            catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR Singular behavior count fixture/owner: " + item.Name + ": " + e); }
        }
        Console.WriteLine($"Singular behavior-count scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; full tracked source compilation, actual discovery owner, controlled factories; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException($"Singular behavior-count regressions: assertions={assertions}; unexpected={unexpected}");
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string temp = Path.Combine(Path.GetTempPath(), "cb-w47-builder-" + Guid.NewGuid().ToString("N"));
        private readonly bool fileLogging = Logging.FileLogging;
        private readonly MethodInfo build;
        private readonly FieldInfo methods, calls, ticks, instance;
        private readonly object settings;
        private readonly PropertyInfo useInstance;
        private readonly object? previousSettings, previousMethods;
        private readonly Type factories, behaviorType, contextType;
        internal readonly Type SpecType;
        internal Fixture()
        {
            string source = FindTrackedSource();
            Directory.CreateDirectory(temp);
            try
            {
                int count = 0;
                foreach (string file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
                {
                    string target = Path.Combine(temp, Path.GetRelativePath(source, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target); count++;
                }
                File.WriteAllText(Path.Combine(temp, "W47BuilderControlledFactories.cs"), FactorySource());
                Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
                object compiler = Activator.CreateInstance(compilerType, new object[] { temp })!;
                foreach (string path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
                    compilerType.GetMethod("AddReference", Hidden)!.Invoke(compiler, new object[] { path });
                var result = (CompilerResults)Invoke(compilerType.GetMethod("Compile", Hidden)!, compiler, Array.Empty<object>())!;
                var errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).ToArray();
                if (errors.Length != 0) throw new InvalidOperationException("Actual Singular compile failed: " + string.Join("; ", errors.Select(e => e.ToString())));
                var assembly = (Assembly?)compilerType.GetProperty("CompiledAssembly", Hidden)!.GetValue(compiler)
                    ?? throw new InvalidOperationException("Actual Singular compile produced no assembly");
                Console.WriteLine($"Singular behavior-count source compilation: {count} tracked files plus one controlled-factory source; no installed source used.");
                Type builder = assembly.GetType("Singular.Dynamics.CompositeBuilder", true)!;
                factories = assembly.GetType("Singular.Dynamics.W47BuilderControlledFactories", true)!;
                methods = builder.GetField("_methods", Hidden)!; previousMethods = methods.GetValue(null);
                build = builder.GetMethod("GetComposite", Hidden)!;
                calls = factories.GetField("Calls", Hidden)!; ticks = factories.GetField("Ticks", Hidden)!;
                SpecType = assembly.GetType("Singular.Managers.TalentSpec", true)!;
                behaviorType = assembly.GetType("Singular.BehaviorType", true)!; contextType = assembly.GetType("Singular.WoWContext", true)!;
                Type settingsType = assembly.GetType("Singular.Settings.SingularSettings", true)!;
                instance = settingsType.GetField("_instance", Hidden)!; previousSettings = instance.GetValue(null);
                settings = RuntimeHelpers.GetUninitializedObject(settingsType); instance.SetValue(null, settings);
                useInstance = settingsType.GetProperty("UseInstanceRotation", Hidden)!;
                Logging.FileLogging = false;
            }
            catch { if (Directory.Exists(temp)) Directory.Delete(temp, true); throw; }
        }
        internal void Filtered(WoWClass c, object spec, string context)
        {
            string foreignClass = c == WoWClass.Mage ? "WarriorOnly" : "MageOnly";
            string foreignSpec = spec.ToString() == "ArmsWarrior" ? "ArcaneOnly" : "ArmsOnly";
            string foreignContext = context == "Normal" ? "InstanceOnly" : "NormalOnly";
            Verify(c, spec, context, new[] { foreignClass, foreignSpec, foreignContext }, 0, "");
        }
        internal void Verify(WoWClass c, object spec, string context, string[] selected, int expectedCount, string expectedCalls, bool ignoredTree = false)
        {
            useInstance.SetValue(settings, false);
            var tree = Build(c, spec, context, selected, out int count);
            Check((tree != null) == (expectedCount > 0 || ignoredTree), "selected executable tree does not match controls");
            Check((string)calls.GetValue(null)! == expectedCalls, "builder invoked an excluded factory or lost a selected factory");
            Check(count == expectedCount, $"support count {count} does not equal installed eligible contributions {expectedCount}");
        }
        private Composite? Build(WoWClass c, object spec, string context, string[] selected, out int count)
        {
            methods.SetValue(null, selected.Select(n => factories.GetMethod(n, Hidden)!).ToList());
            calls.SetValue(null, ""); ticks.SetValue(null, "");
            var args = new object[] { c, spec, Enum.Parse(behaviorType, "Combat"), Enum.Parse(contextType, context), 0 };
            var tree = (Composite?)Invoke(build, null, args); count = (int)args[4]; return tree;
        }
        internal void Override(string context)
        {
            useInstance.SetValue(settings, true);
            try
            {
                string wrong = context == "Normal" ? "NormalOnly" : "BattlegroundOnly";
                var tree = Build(WoWClass.Warrior, Enum.Parse(SpecType, "ArmsWarrior"), context, new[] { wrong, "InstanceOnly" }, out int count);
                Check(tree != null && (string)calls.GetValue(null)! == "InstanceOnly;", "instance override selection changed");
                Check(count == 1, $"override support count {count} includes an excluded context");
            }
            finally { useInstance.SetValue(settings, false); }
        }
        internal void Priority(object arms)
        {
            useInstance.SetValue(settings, false);
            var tree = Build(WoWClass.Warrior, arms, "Normal", new[] { "Valid", "High" }, out int count);
            Check(tree != null && count == 2, "priority control lost selected contributions");
            try { tree!.Start(null!); Check(tree.Tick(null!) == RunStatus.Success && (string)ticks.GetValue(null)! == "High;", "priority tree selected a lower-priority leaf first"); }
            finally { tree!.Stop(null!); }
        }
        public void Dispose()
        {
            methods.SetValue(null, previousMethods); instance.SetValue(null, previousSettings); Logging.FileLogging = fileLogging;
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
        }
    }
    private static object? Invoke(MethodInfo method, object? target, object[] args)
    {
        try { return method.Invoke(target, args); }
        catch (TargetInvocationException e) when (e.InnerException != null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
    private static string FindTrackedSource()
    {
        for (DirectoryInfo? p = new DirectoryInfo(AppContext.BaseDirectory); p != null; p = p.Parent)
        {
            if (!File.Exists(Path.Combine(p.FullName, "CopilotBuddy.csproj"))) continue;
            string path = Path.Combine(p.FullName, "runtime-snapshot", "Routines", "Singular wotlk");
            if (!Directory.Exists(path)) throw new DirectoryNotFoundException("Tracked Singular source is missing");
            return path;
        }
        throw new DirectoryNotFoundException("No repository checkout found; installed-copy fallback is forbidden");
    }
    private static string FactorySource()
    {
        string header = "using System; using Singular.Managers; using Styx.Combat.CombatRoutine; using TreeSharp; namespace Singular.Dynamics { public static class W47BuilderControlledFactories { public static string Calls=\"\", Ticks=\"\";";
        string Factory(string name, string cls = "None", string spec = "Any", string context = "All", string behavior = "Combat", string extra = "") =>
            $"[Class(WoWClass.{cls})][Spec(TalentSpec.{spec})][Context(WoWContext.{context})][Behavior(BehaviorType.{behavior})]{extra} public static Composite {name}() {{ Calls+=\"{name};\"; " +
            (name == "Throws" ? "throw new InvalidOperationException(\"controlled factory failure\");" : $"return new TreeSharp.Action(_=>{{Ticks+=\"{name};\";return RunStatus.Success;}});") + "}";
        return header + Factory("Valid") + Factory("Second") + Factory("High", extra: "[Priority(100)]") + Factory("Throws")
            + Factory("MageOnly", cls: "Mage") + Factory("WarriorOnly", cls: "Warrior")
            + Factory("ArmsOnly", spec: "ArmsWarrior") + Factory("ArcaneOnly", spec: "ArcaneMage")
            + Factory("NormalOnly", context: "Normal") + Factory("InstanceOnly", context: "Instances")
            + Factory("BattlegroundOnly", context: "Battlegrounds") + Factory("RestOnly", behavior: "Rest")
            + Factory("Ignored", extra: "[IgnoreBehaviorCount(BehaviorType.Combat)]") + "}}";
    }
    private static void Check(bool condition, string text) { if (!condition) throw new AssertionFailure(text); }
}
