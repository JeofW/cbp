using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WholesomeAQ;

// Actual loader calls with controlled temporary JSON. Parsing an Escort declaration
// is not permission to execute it. No profile, game, item or movement is invoked.
internal static class QuestStrategyKindValidationRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        int passed = 0, assertions = 0, unexpected = 0, total = 0;
        void Case(string name, Action test)
        {
            total++;
            try { test(); passed++; Console.WriteLine("PASS strategy kind admission: " + name); }
            catch (Failure e) { assertions++; Console.Error.WriteLine("FAIL strategy kind admission: " + name + ": " + e.Message); }
            catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR strategy kind admission: " + name + ": " + e); }
        }

        foreach (QuestStrategyKind kind in new[]
            { QuestStrategyKind.UseItemOn, QuestStrategyKind.GossipEvent, QuestStrategyKind.Escort })
        {
            Case("declared " + kind + " remains readable as data", () =>
            {
                using var fixture = new Fixture();
                fixture.WritePack(kind.ToString(), kind);
                QuestStrategyPack pack = QuestStrategyPackLoader.Load(fixture.PackPath, fixture.DataSha);
                Check(pack.Status == QuestStrategyPackStatus.DeclaredAndBound
                    && pack.Recipes.Count == 1 && pack.Recipes[0].Kind == kind,
                    "declared recipe kind changed while closing undefined-value admission");
            });
        }

        foreach (string kind in new[] { "98765", "-1", "2147483647", "NotARecipe" })
        {
            Case("undefined Kind " + kind + " cannot impersonate Escort", () =>
            {
                using var fixture = new Fixture();
                fixture.WritePack(kind, QuestStrategyKind.Escort);
                Reject(() => QuestStrategyPackLoader.Load(fixture.PackPath, fixture.DataSha));
            });
        }

        Case("missing pack remains explicit", () =>
        {
            using var fixture = new Fixture();
            QuestStrategyPack pack = QuestStrategyPackLoader.Load(fixture.PackPath, fixture.DataSha);
            Check(pack.Status == QuestStrategyPackStatus.Missing && pack.Recipes.Count == 0,
                "missing optional pack no longer preserves ordinary loading");
        });

        Case("invalid kind cannot publish and repaired bytes retry on same loader", () =>
        {
            using var fixture = new Fixture();
            fixture.WritePack("98765", QuestStrategyKind.Escort);
            var loader = new DataLoader(fixture.DataPath);
            Reject(() => loader.Load());
            Check(loader.Database == null && loader.DatasetFingerprint == "unknown"
                && loader.ExecutionFingerprint == "unknown"
                && loader.StrategyPack.Status == QuestStrategyPackStatus.Missing,
                "invalid strategy kind left partially published loader state");
            fixture.WritePack("GossipEvent", QuestStrategyKind.GossipEvent);
            Check(loader.Load() != null
                && loader.StrategyPack.Status == QuestStrategyPackStatus.DeclaredAndBound
                && loader.StrategyPack.Recipes[0].Kind == QuestStrategyKind.GossipEvent,
                "the same loader did not recover from repaired strategy bytes");
        });

        Console.WriteLine($"Strategy kind admission scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; actual pack/data loaders; controlled files; no game attached.");
        if (assertions + unexpected != 0)
            throw new InvalidOperationException("Strategy kind admission regression");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "cb-kind-admission-" + Guid.NewGuid().ToString("N"));
        internal string DataPath { get; }
        internal string PackPath { get; }
        internal string DataSha { get; }

        internal Fixture()
        {
            Directory.CreateDirectory(root);
            DataPath = Path.Combine(root, "quest_data.json");
            PackPath = Path.Combine(root, "quest_strategies.json");
            File.WriteAllText(DataPath,
                "{\"Quests\":[],\"QuestGivers\":[],\"QuestEnders\":[],\"CreatureSpawns\":{},\"GameObjectSpawns\":{}}",
                new UTF8Encoding(false));
            DataSha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(DataPath))).ToLowerInvariant();
        }

        internal void WritePack(string kindText, QuestStrategyKind shape)
        {
            var recipe = new Dictionary<string, object>
            {
                ["QuestId"] = 900001,
                ["ObjectiveIndex"] = 0,
                ["Kind"] = kindText,
                ["SourceRef"] = "controlled://kind-admission",
                ["TargetType"] = "Creature",
                ["TargetId"] = 2164,
                ["Range"] = 5,
                ["RequireLos"] = true,
                ["MaxAttempts"] = 3,
                ["SuccessEvidence"] = "ObjectiveProgress"
            };
            if (shape == QuestStrategyKind.UseItemOn)
            {
                recipe["ItemId"] = 7586;
                recipe["TargetState"] = "Alive";
            }
            else if (shape == QuestStrategyKind.GossipEvent)
                recipe["GossipOptionIndex"] = 0;

            File.WriteAllText(PackPath, JsonSerializer.Serialize(new
            {
                Schema = "quest-strategy-pack-335-v1",
                ClientBuild = 12340,
                QuestDataSha256 = DataSha,
                SourceKind = "curated-profile",
                SourceRevision = "controlled-kind-admission",
                Recipes = new[] { recipe }
            }), new UTF8Encoding(false));
        }

        public void Dispose()
        {
            Styx.Logic.Questing.Recovery.QuestPrerequisiteAuthority.ClearPublishedDependencyAuthority();
            Directory.Delete(root, true);
        }
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException e)
        {
            Check(e.Message.Contains("Kind", StringComparison.Ordinal),
                "input was rejected for an unrelated reason: " + e.Message);
            return;
        }
        throw new Failure("undefined recipe Kind was accepted");
    }

    private static void Check(bool condition, string reason)
    {
        if (!condition) throw new Failure(reason);
    }
}
