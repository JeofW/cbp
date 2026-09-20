using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Xml.Linq;
using Bots.Quest.QuestOrder;
using Styx.Helpers;
using Styx.Logic.Profiles.Quest;
using Styx.Logic.Questing;

// Actual loaded scheduler XML -> CodeNode.FromXml -> QuestBehaviorHelper ->
// ForcedCodeBehavior's real constructor factory. No shadow behavior/world classes,
// injected assembly result, property rewriting, OnStart, ticks or native action.
// The retained Case supplies only the allocated quest-log/navigation observations.
internal static class QuestStrategyConstructorDispatchRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private sealed class Failure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
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
            {
                string name = kind + " generated dataset index " + index;
                try
                {
                    CheckConstructor(kind, index);
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
        Console.WriteLine($"Quest strategy constructor dispatch scenarios: {passed}/6; assertions={assertions}; unexpected={unexpected}; actual loader/scheduler/XML/CodeNode/runtime compiler/constructor factory; allocated observations; no OnStart, tick, native action or game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Quest strategy constructor dispatch regression");
    }

    private static void CheckConstructor(string kind, int datasetIndex)
    {
        Type fixtureType = typeof(QuestStrategySchedulerRegressionTests).GetNestedType("Case", BindingFlags.NonPublic)!;
        ConstructorInfo constructor = fixtureType.GetConstructors(Hidden).Single();
        object?[] inputs = constructor.GetParameters().Select(parameter => parameter.DefaultValue).ToArray();
        inputs[0] = kind;
        inputs[1] = datasetIndex;
        using var fixture = (IDisposable)Invoke(constructor, null, inputs)!;
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
        }
        finally
        {
            // Constructors only: no started behavior, ProfileBatch registration,
            // server acknowledgement or runtime completion is asserted here.
            if (owner != null) GC.SuppressFinalize(owner);
        }
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
