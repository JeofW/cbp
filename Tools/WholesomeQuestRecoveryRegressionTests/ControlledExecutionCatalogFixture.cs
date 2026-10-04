using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using WholesomeAQ;

/// <summary>
/// Supplies the external mechanism observation for existing small synthetic
/// data/availability/route fixtures. Never accepts the shipped dataset or writes
/// production knowledge. Actual extraction and missing-catalog refusal have a
/// separate exact4335-member source suite.
/// </summary>
internal static class ControlledExecutionCatalogFixture
{
    internal static void Prepare(string dataPath)
    {
        string full = Path.GetFullPath(dataPath), temporary = Path.GetFullPath(Path.GetTempPath());
        if (!full.StartsWith(temporary, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Controlled execution catalogs are restricted to temporary fixture inputs.");
        byte[] data = File.ReadAllBytes(full);
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var database = JsonSerializer.Deserialize<QuestDatabase>(File.ReadAllText(full), options)!;
        if (database?.Quests == null || database.Quests.Count > 64)
            throw new InvalidOperationException("Controlled mechanism fixture requires a bounded synthetic quest population.");
        string folder = Path.GetDirectoryName(full)!;
        string repairPath = Path.Combine(folder, "quest_data.repairs.json"), strategyPath = Path.Combine(folder, "quest_strategies.json");
        byte[] repair = File.Exists(repairPath) ? File.ReadAllBytes(repairPath) : Array.Empty<byte>();
        byte[] strategy = File.Exists(strategyPath) ? File.ReadAllBytes(strategyPath) : Array.Empty<byte>();
        if (repair.Length != 0) database = QuestDataRepairPackLoader.Apply(repair, Hash(data), database, out _);
        using var recipes = JsonDocument.Parse(strategy.Length == 0 ? "{\"Recipes\":[]}" : File.ReadAllText(strategyPath));
        var declared = recipes.RootElement.GetProperty("Recipes").EnumerateArray()
            .Select(row => (Quest: row.GetProperty("QuestId").GetInt32(), Objective: row.GetProperty("ObjectiveIndex").GetInt32())).ToHashSet();
        var quests = database.Quests.Select(quest =>
        {
            var rows = quest.Objectives.Select((objective, ordinal) =>
            {
                bool hasRecipe = declared.Contains((quest.Id, objective.Index));
                bool cast = objective.Type == ObjectiveType.KillMob && (quest.SpecialFlags & 0x20) != 0;
                return new
                {
                    RowIndex = ordinal, ObjectiveIndex = objective.Index, Type = objective.Type.ToString(),
                    objective.MobId, objective.ItemId, objective.GameObjectId, objective.KillCount, objective.CollectCount,
                    Driver = hasRecipe ? "DeclaredStrategy" : cast ? "Unsupported" : "Primitive",
                    Reasons = cast ? new[] { "controlled-cast-source" } : Array.Empty<string>(),
                    PrimaryActorSha256 = new string('a', 64), CreditEvidenceSha256 = new string('b', 64)
                };
            }).ToArray();
            return new
            {
                QuestId = quest.Id, Status = rows.Length == 0 ? "SourceUnresolved" : rows.Any(row => row.Driver == "Unsupported")
                    ? "HandlerOrSourceRequired" : rows.Any(row => row.Driver != "Primitive") ? "ImplementedStrategy" : "PrimitiveCandidate",
                PrimaryQuestSha256 = new string('c', 64), PrimaryAddonSha256 = new string('d', 64), Objectives = rows
            };
        }).ToArray();
        File.WriteAllBytes(Path.Combine(folder, "quest_execution_contracts.json"), JsonSerializer.SerializeToUtf8Bytes(new
        {
            SchemaVersion = 1, ClientBuild = 12340, SourceCore = "TrinityCore", SourceBranch = "3.3.5",
            SourceRevision = "95657f54779467effea8a1749a61ff93abc1d707", DatabaseRevision = "TDB335.25101",
            QuestDataSha256 = Hash(data), QuestDataRepairsSha256 = Hash(repair), StrategyPackSha256 = Hash(strategy),
            PrimarySqlSha256 = "e72c0105ca27779ea3b08792b247210a44d9004fc6ab55cd1b0099d3b10779a9", QuestCount = quests.Length, Quests = quests
        }));
    }
    private static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}
