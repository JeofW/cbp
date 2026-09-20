using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Bots.Quest.QuestOrder;
using Styx.Helpers;
using Styx.Logic.Profiles.Quest;
using Styx.Logic.Questing;
using Styx.Logic.BehaviorTree;
using Styx.Offsets;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

// Actual loaded scheduler XML -> CodeNode.FromXml -> QuestBehaviorHelper ->
// ForcedCodeBehavior's real constructor factory, then its real start/tick wrapper.
// No shadow behavior/world classes, injected assembly result or behavior-property
// rewriting. The retained Case supplies allocated observations; no game/executor.
// Lifecycle cases cover missing-recipient wait, raw-counter separation, ready
// transition and a fresh owner after completion, not a native item/gossip request.
internal static class QuestStrategyConstructorDispatchRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private sealed class Failure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        // Other retained groups deliberately load shadow host types into this
        // process. The real compiler discovers loaded assemblies, so execute this
        // check in a fresh process instead of altering its reference selection.
        string assemblyPath = typeof(QuestStrategyConstructorDispatchRegressionTests).Assembly.Location;
        string runtime = Environment.ProcessPath ?? throw new InvalidOperationException("Verified dotnet runtime required");
        if (!string.Equals(Path.GetFileNameWithoutExtension(runtime), "dotnet", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("This fixture requires the existing dotnet test runtime");
        string childPath = Path.Combine(AppContext.BaseDirectory, "constructor-dispatch-" + Guid.NewGuid().ToString("N") + ".dll");
        const string launcher = "using System;using System.Reflection;public static class Entry{public static int Main(string[] args){try{Assembly.LoadFrom(args[0]).GetType(\"QuestStrategyConstructorDispatchRegressionTests\",true).GetMethod(\"RunIsolated\",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).Invoke(null,null);return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}}";
        try
        {
            string trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
                ?? throw new InvalidOperationException("Trusted framework assemblies required");
            var references = trusted.Split(Path.PathSeparator).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path));
            var compilation = CSharpCompilation.Create(Path.GetFileNameWithoutExtension(childPath),
                new[] { CSharpSyntaxTree.ParseText(launcher) }, references,
                new CSharpCompilationOptions(OutputKind.ConsoleApplication, platform: Platform.X86));
            using (var stream = new FileStream(childPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var emit = compilation.Emit(stream);
                if (!emit.Success) throw new InvalidOperationException("Isolated launcher did not compile: " +
                    string.Join(";", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
            }
            var start = new ProcessStartInfo(runtime)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = AppContext.BaseDirectory
            };
            foreach (string argument in new[] { "exec", "--runtimeconfig", Path.ChangeExtension(assemblyPath, ".runtimeconfig.json"),
                "--depsfile", Path.ChangeExtension(assemblyPath, ".deps.json"), childPath, assemblyPath })
                start.ArgumentList.Add(argument);
            using var child = Process.Start(start) ?? throw new InvalidOperationException("Could not start isolated constructor check");
            var output = child.StandardOutput.ReadToEndAsync();
            var error = child.StandardError.ReadToEndAsync();
            if (!child.WaitForExit(120000))
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit();
                throw new InvalidOperationException("Isolated constructor check exceeded its two-minute bound");
            }
            Console.Write(output.GetAwaiter().GetResult());
            Console.Error.Write(error.GetAwaiter().GetResult());
            if (child.ExitCode != 0) throw new InvalidOperationException("Isolated constructor check failed: " + child.ExitCode);
        }
        finally { if (File.Exists(childPath)) File.Delete(childPath); }
    }

    internal static void RunIsolated()
    {
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
            assembly.GetName().Name?.StartsWith("cb-", StringComparison.Ordinal) == true),
            "isolated check inherited earlier shadow fixture assemblies");
        Console.WriteLine("CONSTRUCTOR_ISOLATION: fresh process; no prior shadow fixture assemblies; compiler references unchanged.");
        string root = Root();
        string behaviorRoot = Path.Combine(Logging.ApplicationPath, "Quest Behaviors");
        bool madeDirectory = !Directory.Exists(behaviorRoot);
        var created = new List<string>();
        var priorCache = new Dictionary<string, (bool Present, object? Value)>();
        var cache = (IDictionary)typeof(QuestBehaviorHelper).GetField("_assemblyCache", Hidden)!.GetValue(null)!;
        object gate = typeof(QuestBehaviorHelper).GetField("_lockObject", Hidden)!.GetValue(null)!;
        bool oldLogging = Logging.FileLogging;
        int passed = 0, assertions = 0, unexpected = 0;
        try
        {
            Logging.FileLogging = false;
            Directory.CreateDirectory(behaviorRoot);
            foreach (string kind in new[] { "UseItemOn", "GossipEvent" })
            {
                string path = Path.Combine(behaviorRoot, kind + ".cs");
                byte[] source = File.ReadAllBytes(Path.Combine(root, "runtime-snapshot", "Quest Behaviors", kind + ".cs"));
                if (File.Exists(path))
                {
                    if (!File.ReadAllBytes(path).SequenceEqual(source))
                        throw new InvalidOperationException("Refusing to replace a different behavior at " + path);
                }
                else
                {
                    using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        stream.Write(source, 0, source.Length);
                    created.Add(path);
                }
                lock (gate)
                {
                    priorCache.Add(path, (cache.Contains(path), cache[path]));
                    cache.Remove(path); // Prove actual compilation, not a prior assembly-cache hit.
                }
            }

            foreach (string kind in new[] { "UseItemOn", "GossipEvent" })
            foreach (int index in new[] { 0, 3, 17 })
            foreach (bool lifecycle in new[] { false, true })
            {
                string name = kind + " generated dataset index " + index + (lifecycle ? " lifecycle" : " constructor");
                try
                {
                    CheckConstructor(kind, index, lifecycle);
                    passed++;
                    Console.WriteLine("PASS quest strategy constructor dispatch: " + name);
                }
                catch (Failure e)
                {
                    assertions++;
                    Console.Error.WriteLine("FAIL quest strategy constructor dispatch: " + name + ": " + e.Message);
                }
                catch (Exception e)
                {
                    unexpected++;
                    Console.Error.WriteLine("ERROR quest strategy constructor dispatch: " + name + ": " + e);
                }
            }
        }
        finally
        {
            lock (gate)
                foreach (var entry in priorCache)
                {
                    cache.Remove(entry.Key);
                    if (entry.Value.Present) cache[entry.Key] = entry.Value.Value;
                }
            foreach (string path in created) File.Delete(path);
            if (madeDirectory && Directory.Exists(behaviorRoot) && !Directory.EnumerateFileSystemEntries(behaviorRoot).Any())
                Directory.Delete(behaviorRoot);
            Logging.FileLogging = oldLogging;
        }
        Console.WriteLine($"Quest strategy constructor dispatch scenarios: {passed}/12; assertions={assertions}; unexpected={unexpected}; actual loader/scheduler/XML/CodeNode/runtime compiler/factory; six constructor and six wrapper/start/branch-tick cases; allocated observations; no native executor, recipient action or game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Quest strategy constructor dispatch regression");
    }

    private static void CheckConstructor(string kind, int datasetIndex, bool lifecycle)
    {
        Type fixtureType = typeof(QuestStrategySchedulerRegressionTests).GetNestedType("Case", BindingFlags.NonPublic)!;
        ConstructorInfo constructor = fixtureType.GetConstructors(Hidden).Single();
        object?[] inputs = constructor.GetParameters().Select(parameter => parameter.DefaultValue).ToArray();
        inputs[0] = kind;
        inputs[1] = datasetIndex;
        using var fixture = (IDisposable)Invoke(constructor, null, inputs)!;
        if (lifecycle) PrepareAlivePlayer(fixtureType, fixture);
        Invoke(fixtureType.GetMethod("Scan", Hidden)!, fixture, null);
        XDocument xml = (XDocument)Invoke(fixtureType.GetMethod("Xml", Hidden)!, fixture, null)!;
        XElement element = xml.Descendants("CustomBehavior").Single();
        Check((string?)element.Attribute("File") == kind, "scheduler emitted the wrong behavior name");

        // File and every argument are the unmodified emitted XML attributes.
        CodeNode node;
        Assembly? assembly;
        Action<LogLevel, string> recordCompiler = (level, message) =>
            Console.WriteLine("CONSTRUCTOR_COMPILER_LOG: " + message);
        Logging.OnMessageLogged += recordCompiler;
        try
        {
            node = (CodeNode)CodeNode.FromXml(element);
            Check(node.Path == kind && !node.Arguments.ContainsKey("File"), "actual CodeNode did not preserve the emitted contract");
            assembly = node.AssemblyGetter();
        }
        finally { Logging.OnMessageLogged -= recordCompiler; }
        Check(assembly != null, "actual QuestBehaviorHelper could not compile the tracked behavior; inspect its compiler diagnostics");
        MethodInfo factory = typeof(ForcedCodeBehavior).GetMethod("CreateCustomBehaviorInstance", Hidden)!;
        CustomForcedBehavior? owner = null;
        try
        {
            owner = (CustomForcedBehavior?)Invoke(factory, null, new object[] { assembly!, node.Arguments });
            Check(owner != null, "actual behavior constructor factory returned no owner");
            GC.SuppressFinalize(owner!);
            Check(owner!.GetType().FullName == "Styx.Bot.Quest_Behaviors." + kind + "." + kind,
                "factory selected a different behavior type");
            Check(!owner.IsAttributeProblem, "actual owner rejected the unchanged generated attributes");
            Check((int)Read(owner, "QuestId")! == 867 && (int)Read(owner, "ObjectiveIndex")! == datasetIndex,
                "constructor renumbered the dataset/recovery identity");
            Check(Read(owner, "SuccessEvidence")!.ToString() == "QuestComplete", "constructor changed the declared success contract");
            Check(((int[])Read(owner, "MobIds")!).SequenceEqual(new[] { 70001 }), "constructor selected the wrong recipient");
            Check((int)Read(owner, "MaxAttempts")! == 2 && (double)Read(owner, "Range")! == 4d &&
                (bool)Read(owner, "RequireLos")! && (bool)Read(owner, "WaitForNpcs")!, "bounded action settings did not survive dispatch");
            if (kind == "UseItemOn")
                Check((int)Read(owner, "ItemId")! == 12345 && Read(owner, "MobType")!.ToString() == "Npc" &&
                    Read(owner, "NpcState")!.ToString() == "Alive", "item/recipient state did not survive dispatch");
            else
                Check((int)Read(owner, "GossipOptionIndex")! == 1, "zero-based gossip recipe index was changed by construction");
            if (lifecycle) CheckLifecycle(fixtureType, fixture, node, kind);
        }
        finally
        {
            // The factory-only owner is never started. The separate real wrapper
            // in CheckLifecycle owns its own start/tick/disposal.
            if (owner != null) GC.SuppressFinalize(owner);
        }
    }

    private static void PrepareAlivePlayer(Type fixtureType, object fixture)
    {
        // Use the tracked 12340 enum and the existing allocation, not new offsets
        // or a replacement IsAlive getter. Retain all controlled global-cache bytes.
        uint descriptor = (uint)Read(fixture, "Descriptor")!;
        object memoryOwner = fixtureType.GetField("fixture", Hidden)!.GetValue(fixture)!;
        var cache = (ThreadLocal<Dictionary<IntPtr, byte[]>>)memoryOwner.GetType()
            .GetField("cache", Hidden)!.GetValue(memoryOwner)!;
        foreach (WoWUnitFields field in new[] { WoWUnitFields.Health, WoWUnitFields.MaxHealth })
        {
            uint address = descriptor + (uint)field * 4U;
            Invoke(fixtureType.GetMethod("Write", Hidden)!, fixture, new object[] { address, 100U });
            cache.Value!.Remove(new IntPtr(unchecked((int)address)));
        }
        var player = (LocalPlayer)Read(fixture, "Player")!;
        Check(player.IsValid && player.IsAlive && player.Guid != 0,
            "allocated actor did not become valid/alive through the actual descriptor reader");
        Check(ObjectManager.Executor == null, "offline lifecycle fixture must not have a native executor");
        Check(!(ObjectManager.GetObjectsOfType<WoWUnit>() ?? Enumerable.Empty<WoWUnit>())
            .Any(unit => unit != null && unit.IsValid && unit.Entry == 70001),
            "missing-recipient lifecycle unexpectedly has a matching live recipient");
    }

    private static void CheckLifecycle(Type fixtureType, object fixture, CodeNode node, string kind)
    {
        string goal = TreeRoot.GoalText, status = TreeRoot.StatusText;
        FieldInfo batchField = typeof(ProfileBatchManager).GetField("_currentBatch", Hidden)!;
        object? batch = batchField.GetValue(null);
        ForcedCodeBehavior? wrapper = null, restarted = null;
        int starts = 0;
        Action<LogLevel, string> recordStart = (_, message) =>
        {
            Console.WriteLine("LIFECYCLE_LOG: " + message);
            if (message.Contains("[Code] Executing custom behavior: " + kind, StringComparison.Ordinal)) starts++;
        };
        Logging.OnMessageLogged += recordStart;
        try
        {
            wrapper = new ForcedCodeBehavior(node); // Real PendingElement/batch/owner path.
            var owner = (CustomForcedBehavior)typeof(ForcedCodeBehavior).GetField("customBehavior", Hidden)!.GetValue(wrapper)!;
            Check(ReferenceEquals(owner.Element, node.Element) && !owner.IsAttributeProblem,
                "real wrapper lost the generated XML element or rejected its attributes");
            wrapper.OnStart();
            wrapper.OnTick();
            Check(starts == 1 && (TreeRoot.GoalText?.StartsWith(kind, StringComparison.Ordinal) ?? false),
                "real wrapper did not start the behavior exactly once");
            Check(!wrapper.IsDone && !wrapper.IsExecutionDeferred && Read(owner, "InitialObjectiveCount") == null,
                "whole-quest lifecycle was completed/deferred or consumed an unmapped raw counter");
            RunStatus expected = kind == "UseItemOn" ? RunStatus.Success : RunStatus.Running;
            string waiting = kind == "UseItemOn" ? "Waiting for object to spawn" : "Waiting for source-bound gossip NPC";
            Check(Tick(wrapper) == expected && TreeRoot.StatusText == waiting && (int)Read(owner, "Counter")! == 0,
                "actual branch did not retain missing-recipient work without an item/gossip attempt");

            uint descriptor = (uint)Read(fixture, "Descriptor")!;
            MethodInfo write = fixtureType.GetMethod("Write", Hidden)!;
            Invoke(write, fixture, new object[] { descriptor + 640U, 0x00090009U });
            Invoke(write, fixture, new object[] { descriptor + 644U, 0x00090009U });
            wrapper.OnTick();
            Check(!wrapper.IsDone && !wrapper.IsExecutionDeferred && Tick(wrapper) == expected &&
                TreeRoot.StatusText == waiting && (int)Read(owner, "Counter")! == 0 && starts == 1,
                "unrelated packed counters completed/restarted the generated whole-quest behavior");

            Invoke(write, fixture, new object[] { descriptor + 636U, (uint)WoWDescriptorQuestFlags.Completed });
            Check(wrapper.IsDone && !wrapper.IsExecutionDeferred && Tick(wrapper) == RunStatus.Success &&
                (int)Read(owner, "Counter")! == 0, "raw ready transition did not stop the actual behavior body");
            restarted = new ForcedCodeBehavior(node);
            var nextOwner = (CustomForcedBehavior)typeof(ForcedCodeBehavior).GetField("customBehavior", Hidden)!.GetValue(restarted)!;
            restarted.OnStart();
            restarted.OnTick();
            Check(starts == 2 && restarted.IsDone && !restarted.IsExecutionDeferred &&
                Tick(restarted) == RunStatus.Success && (int)Read(nextOwner, "Counter")! == 0,
                "fresh wrapper repeated a behavior whose quest was already ready");
            Check(ObjectManager.Executor == null, "lifecycle test acquired an unexpected native executor");
        }
        finally
        {
            try { restarted?.Dispose(); }
            finally
            {
                try { wrapper?.Dispose(); }
                finally
                {
                    Logging.OnMessageLogged -= recordStart;
                    batchField.SetValue(null, batch);
                    TreeRoot.GoalText = goal;
                    TreeRoot.StatusText = status;
                }
            }
        }
    }

    private static RunStatus Tick(ForcedCodeBehavior wrapper)
    {
        Composite branch = wrapper.Branch;
        object context = new object();
        branch.Start(context);
        try { return branch.Tick(context); }
        finally { branch.Stop(context); }
    }

    private static object? Read(object owner, string property) => owner.GetType().GetProperty(property, Hidden)!.GetValue(owner);
    private static object? Invoke(MethodBase member, object? owner, object?[]? arguments)
    {
        try { return member is ConstructorInfo constructor ? constructor.Invoke(arguments) : member.Invoke(owner, arguments); }
        catch (TargetInvocationException e) when (e.InnerException != null)
        { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "CopilotBuddy.csproj"))) return directory.FullName;
        throw new InvalidOperationException("Tracked checkout required");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Failure(message); }
}
