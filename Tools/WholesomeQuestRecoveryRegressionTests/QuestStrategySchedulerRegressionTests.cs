using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Xml.Linq;
using Styx.Logic.Questing;
using Styx.WoWInternals.WoWObjects;
using WholesomeAQ;

// Actual DataLoader -> allocated native-shaped QuestLog -> ScanAndRefresh ->
// MaterializeSchedule -> ProfileBuilder -> written XML. Only the external memory,
// metadata and navigation observations use the retained publication fixture.
// No recipe is installed, no custom behavior/native action is executed, no game attached.
internal static class QuestStrategySchedulerRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private sealed class Failure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        var tests = new List<(string Name, Action Test)>();
        foreach (string name in new[] { "UseItemOn", "GossipEvent" })
        {
            string kind = name;
            foreach (int index in new[] { 0, 3, 17 })
            {
                int datasetIndex = index;
                tests.Add(($"{kind}: loaded CAST recipe reaches XML at dataset index {index}", () =>
                {
                    using var c = new Case(kind, datasetIndex);
                    c.Scan(); c.Custom(kind, datasetIndex);
                }));
            }
            tests.Add((kind + ": unrelated packed count does not suppress whole-quest recipe", () =>
            {
                using var c = new Case(kind, 3, rawDone: true);
                c.Scan(); c.Custom(kind, 3);
            }));
            tests.Add((kind + ": unmapped normal-counter recipe stays deferred", () =>
            {
                using var c = new Case(kind, 0, castCredit: false, progress: true);
                c.Scan(); c.Deferred();
            }));
            tests.Add((kind + ": collection index cannot authorize a raw-counter recipe", () =>
            {
                using var c = new Case(kind, 17, castCredit: false, progress: true, collection: true);
                c.Scan(); c.Deferred();
            }));
            tests.Add((kind + ": mismatched recipient does not poison independent collection", () =>
            {
                using var c = new Case(kind, 0, castCredit: false, wrongTarget: true, independent: true);
                c.Scan(); c.IndependentOnly();
            }));
            tests.Add((kind + ": whole-quest collection recipe does not read unrelated packed counts", () =>
            {
                using var c = new Case(kind, 3, collection: true, rawDone: true);
                c.Scan(); c.Custom(kind, 3);
            }));
            tests.Add((kind + ": ready quest retains turn-in instead of executing a recipe", () =>
            {
                using var c = new Case(kind, 17, completed: true);
                c.Scan();
                Check(c.Xml().Descendants("TurnIn").Any(e => (string?)e.Attribute("QuestId") == "867"), "ready quest lost its turn-in");
                Check(!c.Xml().Descendants("CustomBehavior").Any(), "ready quest emitted a custom action");
            }));
            tests.Add((kind + ": changed raw log during navigation prevents recipe publication", () =>
            {
                using var c = new Case(kind, 17);
                int reached = 0;
                c.BeforeNavigation(() => { reached++; c.Write(c.Descriptor + 640U, 1U); });
                c.Scan();
                Check(reached == 1, "recipe never reached the actual navigation boundary");
                c.Deferred();
            }));
        }
        tests.Add(("no-pack CAST still cannot become ordinary killing", () =>
        {
            using var c = new Case(null, 0); c.Scan(); c.Deferred();
        }));
        tests.Add(("no-pack ordinary work retains legacy XML", () =>
        {
            using var c = new Case(null, 0, castCredit: false); c.Scan();
            Check(c.Xml().Descendants("Objective").Any(e => (string?)e.Attribute("Type") == "KillMob"), "ordinary control lost its objective");
            Check(!c.Xml().Descendants("CustomBehavior").Any(), "ordinary control acquired a recipe");
        }));
        tests.Add(("no-pack CAST quest retains independent collection work", () =>
        {
            using var c = new Case(null, 0, independent: true); c.Scan(); c.IndependentOnly();
        }));
        tests.Add(("declared Escort does not poison independent collection", () =>
        {
            using var c = new Case("Escort", 0, castCredit: false, independent: true); c.Scan(); c.IndependentOnly();
        }));
        tests.Add(("unimplemented GameObject item protocol stays deferred", () =>
        {
            using var c = new Case("UseItemOn", 0, castCredit: false, gameObject: true); c.Scan(); c.Deferred();
        }));
        tests.Add(("BelowHp without a source threshold stays deferred", () =>
        {
            using var c = new Case("UseItemOn", 0, castCredit: false, belowHp: true); c.Scan(); c.Deferred();
        }));
        tests.Add(("recipe for another dataset objective does not authorize CAST", () =>
        {
            using var c = new Case("UseItemOn", 17, recipeIndex: 18); c.Scan(); c.Deferred();
        }));
        tests.Add(("loader digest rejection cannot publish a partial recipe pack", () =>
        {
            bool refused = false;
            try { using var c = new Case("UseItemOn", 0, wrongDigest: true); }
            catch (InvalidDataException) { refused = true; }
            Check(refused, "mismatched dataset digest was accepted");
        }));

        int passed = 0, assertions = 0, unexpected = 0;
        bool logging = Styx.Helpers.Logging.FileLogging;
        try
        {
            Styx.Helpers.Logging.FileLogging = false;
            foreach (var test in tests)
            {
                try { test.Test(); passed++; Console.WriteLine("PASS quest strategy scheduler: " + test.Name); }
                catch (Failure e) { assertions++; Console.Error.WriteLine("FAIL quest strategy scheduler: " + test.Name + ": " + e.Message); }
                catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR quest strategy scheduler: " + test.Name + ": " + e); }
            }
        }
        finally { Styx.Helpers.Logging.FileLogging = logging; }
        Console.WriteLine($"Quest strategy scheduler scenarios: {passed}/{tests.Count}; assertions={assertions}; unexpected={unexpected}; actual loader/raw QuestLog/live scan/scheduler/XML; controlled external memory/navigation; no behavior dispatch or game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Quest strategy scheduler regression");
    }

    private sealed class Case : IDisposable
    {
        private readonly object fixture;
        private readonly string output;
        private readonly DataLoader loader;
        internal readonly QuestScheduler Scheduler;
        internal uint Descriptor => (uint)Get(fixture, "descriptor")!;
        private LocalPlayer Player => (LocalPlayer)Get(fixture, "Player")!;

        internal Case(string? kind, int index, bool castCredit = true, bool progress = false,
            bool collection = false, bool rawDone = false, bool completed = false,
            bool wrongTarget = false, bool independent = false, bool gameObject = false,
            bool belowHp = false, int? recipeIndex = null, bool wrongDigest = false)
        {
            fixture = Activator.CreateInstance(typeof(QuestPublicationRegressionTests)
                .GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
            try
            {
                string root = (string)Get(fixture, "Directory")!;
                output = Path.Combine(root, "strategy-profile.xml");
                Write(Descriptor + 636U, completed ? (uint)WoWDescriptorQuestFlags.Completed : 0U);
                Write(Descriptor + 640U, rawDone ? 0x00090009U : 0U);
                Write(Descriptor + 644U, rawDone ? 0x00090009U : 0U);
                var observation = Player.QuestLog.CaptureSnapshot();
                Check(observation.IsComplete && observation.Quests.Count == 1 &&
                    observation.Quests.Single().IsCompleted == completed, "controlled raw quest state did not reach the actual reader");
                var objective = new QuestObjective
                {
                    Index = index, Type = collection ? ObjectiveType.CollectItem : ObjectiveType.KillMob,
                    MobId = 70001, ItemId = collection ? 22222 : 0, KillCount = 1, CollectCount = collection ? 3 : 0
                };
                var quest = new QuestEntry
                {
                    Id = 867, Name = "Controlled source-bound scheduler", MinLevel = 1, QuestLevel = 20,
                    SpecialFlags = castCredit ? 0x20 : 0, Objectives = new List<QuestObjective> { objective }
                };
                if (independent) quest.Objectives.Add(new QuestObjective
                {
                    Index = 9, Type = ObjectiveType.CollectItem, MobId = 70002, ItemId = 33333, CollectCount = 2
                });
                var db = new QuestDatabase
                {
                    Quests = new List<QuestEntry> { quest },
                    QuestEnders = new List<QuestEnderEntry> { new QuestEnderEntry { QuestId = 867, EnderId = 77, EnderType = QuestObjectType.Creature } },
                    CreatureSpawns = new Dictionary<string, List<SpawnPoint>>
                    {
                        ["70001"] = new() { new SpawnPoint { Map = 1, X = 12, Y = 10, Z = 10 } },
                        ["70002"] = new() { new SpawnPoint { Map = 1, X = 14, Y = 10, Z = 10 } },
                        ["77"] = new() { new SpawnPoint { Map = 1, X = 10, Y = 10, Z = 10 } }
                    }
                };
                string dataPath = Path.Combine(root, "quest_data.json");
                byte[] data = JsonSerializer.SerializeToUtf8Bytes(db);
                File.WriteAllBytes(dataPath, data);
                if (kind != null)
                {
                    var recipe = new Dictionary<string, object>
                    {
                        ["QuestId"] = 867, ["ObjectiveIndex"] = recipeIndex ?? index, ["Kind"] = kind,
                        ["SourceRef"] = "controlled://scheduler/not-a-game-recipe", ["TargetType"] = gameObject ? "GameObject" : "Creature",
                        ["TargetId"] = wrongTarget ? 79999 : 70001, ["Range"] = 4, ["RequireLos"] = true,
                        ["MaxAttempts"] = 2, ["SuccessEvidence"] = progress ? "ObjectiveProgress" : "QuestComplete"
                    };
                    if (kind == "UseItemOn") { recipe["ItemId"] = 12345; recipe["TargetState"] = belowHp ? "BelowHp" : "Alive"; }
                    if (kind == "GossipEvent") recipe["GossipOptionIndex"] = 1;
                    var pack = new Dictionary<string, object>
                    {
                        ["Schema"] = "copilotbuddy-quest-strategies-v1", ["ClientBuild"] = 12340,
                        ["QuestDataSha256"] = wrongDigest ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(),
                        ["SourceKind"] = "curated-profile", ["SourceRevision"] = Guid.NewGuid().ToString("N"),
                        ["Recipes"] = new[] { recipe }
                    };
                    File.WriteAllBytes(Path.Combine(root, "quest_strategies.json"), JsonSerializer.SerializeToUtf8Bytes(pack));
                }
                loader = new DataLoader(dataPath);
                loader.Load();
                Scheduler = new QuestScheduler(loader, new ProfileBuilder(output), new WholesomeAQSettings());
            }
            catch { ((IDisposable)fixture).Dispose(); throw; }
        }

        internal void Write(uint address, uint value)
        {
            var cache = (ThreadLocal<Dictionary<IntPtr, byte[]>>)Get(fixture, "cache")!;
            Marshal.WriteInt32(new IntPtr(unchecked((int)address)), unchecked((int)value));
            foreach (IntPtr key in cache.Value!.Keys.Where(k => unchecked((uint)k.ToInt32()) >= Descriptor + 632U
                && unchecked((uint)k.ToInt32()) < Descriptor + 1132U).ToArray()) cache.Value.Remove(key);
        }
        internal void BeforeNavigation(Action action) => fixture.GetType().GetField("BeforeNavigation", Hidden)!
            .SetValue(fixture, (Action)(() => { fixture.GetType().GetField("BeforeNavigation", Hidden)!.SetValue(fixture, null); action(); }));
        internal void Scan()
        {
            try { Scheduler.ScanAndRefresh(Player); }
            catch (InvalidDataException e) { throw new Failure("unsupported strategy reached materialization instead of isolated deferral: " + e.Message); }
        }
        internal XDocument Xml()
        {
            Check(File.Exists(output), "no XML was published; " + Scheduler.LastStatus);
            return XDocument.Load(output);
        }
        internal void Custom(string kind, int datasetIndex)
        {
            XElement[] nodes = Xml().Descendants("CustomBehavior").ToArray();
            Check(nodes.Length == 1 && (string?)nodes[0].Attribute("File") == kind, "wrong or missing custom behavior");
            Check((string?)nodes[0].Attribute("QuestId") == "867" && (string?)nodes[0].Attribute("ObjectiveIndex") == datasetIndex.ToString()
                && (string?)nodes[0].Attribute("SuccessEvidence") == "QuestComplete" && (string?)nodes[0].Attribute("MobId") == "70001",
                "recipe identity or declared success contract changed");
            Check(!Xml().Descendants("Objective").Any(e => (string?)e.Attribute("Type") == "KillMob"), "CAST recipe became ordinary killing");
            Check(Scheduler.LastSchedule.Plan.Single().ObjectiveIndex == datasetIndex, "dataset recovery identity was renumbered");
            Check(loader.Database.Quests[0].SpecialFlags == 0x20, "imported cast-credit evidence was rewritten");
        }
        internal void Deferred() => Check(Scheduler.LastSchedule.Plan.Count == 0 && !File.Exists(output), "unsupported or stale work was published");
        internal void IndependentOnly()
        {
            Check(Scheduler.LastSchedule.Plan.Count == 1 && Scheduler.LastSchedule.Plan[0].ObjectiveIndex == 9,
                "unsupported recipe suppressed or borrowed independent objective work");
            Check(Xml().Descendants("Objective").Any(e => (string?)e.Attribute("Type") == "CollectItem" && (string?)e.Attribute("ItemId") == "33333")
                && !Xml().Descendants("CustomBehavior").Any(), "independent collection XML was replaced by unsupported custom work");
        }
        public void Dispose() => ((IDisposable)fixture).Dispose();
    }
    private static object? Get(object value, string name) => value.GetType().GetField(name, Hidden)!.GetValue(value);
    private static void Check(bool condition, string message) { if (!condition) throw new Failure(message); }
}
