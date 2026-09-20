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

        AddNormalCreditCases(tests);

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

    private static void AddNormalCreditCases(List<(string Name, Action Test)> tests)
    {
        // Dataset row, raw normal slot and credit entry are configured independently.
        // These are synthetic observations, not a new quest recipe or offset table.
        var cases = new[]
        {
            ("matching incomplete credit retains ordinary work", 0, new[] {70001,70002,0,0}, new[] {1,1,0,0}, new ushort[] {0,1,0,0}, true),
            ("matching complete credit suppresses ordinary work", 0, new[] {70001,70002,0,0}, new[] {1,1,0,0}, new ushort[] {1,0,0,0}, false),
            ("unrelated completed slot cannot suppress reordered credit", 0, new[] {70002,70001,0,0}, new[] {1,1,0,0}, new ushort[] {1,0,0,0}, true),
            ("reordered matching completion suppresses the dataset row", 0, new[] {70002,70001,0,0}, new[] {1,1,0,0}, new ushort[] {0,1,0,0}, false),
            ("sparse dataset row uses proven raw slot three", 17, new[] {0,0,0,70001}, new[] {0,0,0,1}, new ushort[] {0,0,0,1}, false),
            ("dataset row three cannot borrow unrelated slot three", 3, new[] {70001,0,0,70002}, new[] {1,0,0,1}, new ushort[] {0,0,0,1}, true),
            ("missing credit metadata is not completion", 0, new[] {0,0,0,0}, new[] {0,0,0,0}, new ushort[] {1,0,0,0}, true),
            ("different credit entry is not completion", 0, new[] {70002,0,0,0}, new[] {1,0,0,0}, new ushort[] {1,0,0,0}, true),
            ("same-numbered GameObject credit is not creature completion", 0, new[] {unchecked((int)(0x80000000U | 70001U)),0,0,0}, new[] {1,0,0,0}, new ushort[] {1,0,0,0}, true),
            ("disagreeing required count is not completion", 0, new[] {70001,0,0,0}, new[] {2,0,0,0}, new ushort[] {9,0,0,0}, true),
            ("zero required count is not completion", 0, new[] {70001,0,0,0}, new[] {0,0,0,0}, new ushort[] {9,0,0,0}, true),
            ("duplicate credit entries are ambiguous", 0, new[] {70001,70001,0,0}, new[] {1,1,0,0}, new ushort[] {1,0,0,0}, true),
            ("required count cannot disambiguate duplicate entries", 0, new[] {70001,70001,0,0}, new[] {1,2,0,0}, new ushort[] {1,0,0,0}, true)
        };
        foreach (var row in cases)
        {
            var test = row;
            tests.Add(("ordinary credit: " + test.Item1, () =>
            {
                using var c = new Case(null, test.Item2, castCredit: false);
                c.NormalCredit(test.Item3, test.Item4, test.Item5);
                c.Scan();
                if (test.Item6) c.OrdinaryOnly(test.Item2);
                else c.Deferred();
            }));
        }
        tests.Add(("ordinary credit: matched completion leaves independent collection", () =>
        {
            using var c = new Case(null, 0, castCredit: false, independent: true);
            c.NormalCredit(new[] {70002,70001,0,0}, new[] {1,1,0,0}, new ushort[] {0,1,0,0});
            c.Scan(); c.IndependentOnly();
        }));
        tests.Add(("ordinary credit: collection item identity cannot borrow normal credit", () =>
        {
            using var c = new Case(null, 0, castCredit: false, collection: true);
            c.NormalCredit(new[] {22222,0,0,0}, new[] {3,0,0,0}, new ushort[] {9,0,0,0});
            c.Scan();
            Check(c.Scheduler.LastSchedule.Plan.Count == 1 && c.Scheduler.LastSchedule.Plan[0].ObjectiveIndex == 0,
                "normal credit suppressed independent carried-item work");
            Check(c.Xml().Descendants("Objective").Any(e => (string?)e.Attribute("Type") == "CollectItem" &&
                (string?)e.Attribute("ItemId") == "22222"), "collection did not retain its own item identity");
        }));
        tests.Add(("ordinary credit: metadata change during navigation prevents publication", () =>
        {
            using var c = new Case(null, 0, castCredit: false);
            c.NormalCredit(new[] {70001,0,0,0}, new[] {1,0,0,0}, new ushort[] {0,0,0,0});
            int reached = 0;
            c.BeforeNavigation(() => { reached++; c.NormalMetadata(new[] {70002,0,0,0}, new[] {1,0,0,0}); });
            c.Scan();
            Check(reached == 1, "ordinary work did not reach controlled navigation");
            c.Deferred();
        }));
        tests.Add(("ordinary credit: required count change before XML prevents publication", () =>
        {
            using var c = new Case(null, 0, castCredit: false);
            c.NormalCredit(new[] {70001,0,0,0}, new[] {1,0,0,0}, new ushort[] {0,0,0,0});
            int reached = 0;
            c.BeforeProfileArguments(() => { reached++; c.NormalMetadata(new[] {70001,0,0,0}, new[] {2,0,0,0}); });
            c.Scan();
            Check(reached == 1, "ordinary work did not reach actual XML argument observation");
            c.Deferred();
        }));
        tests.Add(("ordinary credit: raw change during navigation still prevents publication", () =>
        {
            using var c = new Case(null, 0, castCredit: false);
            c.NormalCredit(new[] {70001,0,0,0}, new[] {1,0,0,0}, new ushort[] {0,0,0,0});
            int reached = 0;
            c.BeforeNavigation(() => { reached++; c.Write(c.Descriptor + 640U, 1U); });
            c.Scan();
            Check(reached == 1, "ordinary work did not reach controlled navigation");
            c.Deferred();
        }));
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
                        ["Schema"] = "quest-strategy-pack-335-v1", ["ClientBuild"] = 12340,
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

        internal void NormalCredit(int[] ids, int[] required, ushort[] counts)
        {
            Check(counts.Length == 4, "controlled normal counts must have four raw slots");
            NormalMetadata(ids, required);
            Write(Descriptor + 640U, (uint)counts[0] | ((uint)counts[1] << 16));
            Write(Descriptor + 644U, (uint)counts[2] | ((uint)counts[3] << 16));
            var observation = Player.QuestLog.CaptureSnapshot();
            Check(observation.IsComplete && observation.Quests.Count == 1, "actual quest observation was incomplete");
            var quest = observation.Quests.Single();
            Check(quest.NormalObjectiveIDs.SequenceEqual(ids) && quest.NormalObjectiveRequiredCounts.SequenceEqual(required),
                "configured credit metadata did not reach the actual PlayerQuest reader");
            Check(quest.GetData(out QuestDescriptorData data) && data.ObjectivesDone.SequenceEqual(counts) && !quest.IsCompleted,
                "configured packed counts did not reach the actual reader as an incomplete quest");
        }
        internal void NormalMetadata(int[] ids, int[] required)
        {
            Check(ids.Length == 4 && required.Length == 4, "controlled metadata must retain four raw slots");
            var block = Styx.StyxWoW.Cache[Styx.WoWInternals.WoWCache.CacheDb.Quest].GetInfoBlockById(867);
            Check(block != null && block.Address != 0, "retained allocated quest cache entry is missing");
            uint first = block!.Address;
            uint last = checked(first + (uint)Marshal.SizeOf<Styx.WoWInternals.WoWCache.WoWCache.QuestCacheEntry>());
            int idOffset = Marshal.OffsetOf<Styx.WoWInternals.WoWCache.WoWCache.QuestCacheEntry>("ObjectiveId").ToInt32();
            int requiredOffset = Marshal.OffsetOf<Styx.WoWInternals.WoWCache.WoWCache.QuestCacheEntry>("ObjectiveRequiredCount").ToInt32();
            for (int slot = 0; slot < 4; slot++)
            {
                Marshal.WriteInt32(new IntPtr(unchecked((int)first)), idOffset + slot * sizeof(int), ids[slot]);
                Marshal.WriteInt32(new IntPtr(unchecked((int)first)), requiredOffset + slot * sizeof(int), required[slot]);
            }
            var cache = (ThreadLocal<Dictionary<IntPtr, byte[]>>)Get(fixture, "cache")!;
            foreach (IntPtr key in cache.Value!.Keys.Where(k => unchecked((uint)k.ToInt32()) >= first &&
                unchecked((uint)k.ToInt32()) < last).ToArray()) cache.Value.Remove(key);
        }
        internal void OrdinaryOnly(int index)
        {
            Check(Scheduler.LastSchedule.Plan.Count == 1 && Scheduler.LastSchedule.Plan[0].ObjectiveIndex == index,
                "ordinary work was suppressed or its dataset identity changed; " + Scheduler.LastStatus);
            Check(Xml().Descendants("Objective").Any(e => (string?)e.Attribute("Type") == "KillMob" &&
                (string?)e.Attribute("MobId") == "70001") && !Xml().Descendants("CustomBehavior").Any(),
                "ordinary XML lost the dataset creature or acquired a recipe");
        }
        internal void BeforeProfileArguments(Action action)
        {
            var field = Player.GetType().GetField("BeforeProfileArguments", Hidden)!;
            field.SetValue(Player, (Action)(() => { field.SetValue(Player, null); action(); }));
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
