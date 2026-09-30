using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using Bots.Quest.Objectives;
using Styx.Logic.Profiles.Quest;
using WholesomeAQ;
using DataKind = WholesomeAQ.ObjectiveType;

// Exact reviewed data through the actual loader, profile parser and item owner.
// Item observations are allocated test memory; no game or native dispatch.
internal static class QuestReviewedDonorKnowledgeRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        using var authority = new QuestDataRepairPackRegressionTests.Fixture();
        string root = QuestTypedStrategyBehaviorRegressionTests.Root();
        string folder = Path.Combine(root, "runtime-snapshot/Bots/WholesomeAutoQuest-master/quest_data");
        byte[] original = File.ReadAllBytes(Path.Combine(folder, "quest_data.json"));
        var database = new DataLoader(Path.Combine(folder, "quest_data.json")).Load();
        using var expected = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,
            "docs/audit/2026-10-01/collection-donors/route-patch.json")));
        var cases = new List<(string Name, Action Body)>();
        foreach (JsonElement element in expected.RootElement.GetProperty("CollectionRouteRepairs").EnumerateArray())
        {
            JsonElement contract = element.Clone();
            int id = contract.GetProperty("QuestId").GetInt32();
            int index = contract.GetProperty("NewObjectiveIndex").GetInt32();
            int entry = contract.GetProperty("CreatureId").GetInt32();
            int item = contract.GetProperty("ItemId").GetInt32();
            int count = contract.GetProperty("RequiredCount").GetInt32();
            cases.Add(($"{id}/{index}: reviewed source reaches real item behavior and receipt", () =>
            {
                QuestEntry quest = database.Quests.Single(value => value.Id == id);
                var objective = quest.Objectives.SingleOrDefault(value => value.Index == index);
                Check(objective != null && objective.Type == DataKind.CollectItem && objective.MobId == entry
                    && objective.GameObjectId == 0 && objective.ItemId == item && objective.CollectCount == count,
                    "reviewed collector missing or changed in actual loaded knowledge");
                if (contract.GetProperty("Operation").GetString() == "Append")
                {
                    var retained = quest.Objectives[contract.GetProperty("RowIndex").GetInt32()];
                    Check(retained.Index == contract.GetProperty("ObjectiveIndex").GetInt32()
                        && retained.MobId == contract.GetProperty("ExpectedTargetId").GetInt32()
                        && retained.ItemId == item && retained.CollectCount == count,
                        "append discarded the original valid source");
                }
                Check(database.CreatureSpawns.TryGetValue(entry.ToString(), out var points) && points.Count > 0,
                    "reviewed source has no loaded locations");
                var plan = new[] { new QuestPlanEntry { Quest = quest, Stage = QuestWorkStage.Objective,
                    ObjectiveIndex = index, Hotspots = new[] { points![0] } } };
                string xml = new ProfileBuilder().BuildProfileXml(plan, database, "Controlled source", "Fixture", 80);
                var parsed = XDocument.Parse(xml).Root!.Elements("Quest").Select(QuestInfo.FromXML)
                    .Single(value => value.ID == (uint)id).FindCollectItem((uint)item);
                Check(parsed?.OverridedCollectFrom?.ContainsMob((uint)entry) == true,
                    "reviewed creature was lost by the generated definition/parser");
                using var fixture = new QuestDatasetObservationFixture();
                fixture.SetQuest((uint)id, quest.Name, 80, new int[4], new int[4],
                    new[] { item, 0, 0, 0, 0, 0 }, new[] { count, 0, 0, 0, 0, 0 });
                fixture.LoadProfile(xml);
                using var owner = new CollectItemObjective(fixture.Quest, new(),
                    fixture.Quest.GetObjectives().Single(value => value.ID == (uint)item), new());
                var info = (CollectItemObjectiveInfo?)typeof(CollectItemObjective).GetField("_collectItemInfo",
                    BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner);
                Check(info?.OverridedCollectFrom?.ContainsMob((uint)entry) == true,
                    "actual item behavior did not consume the reviewed source");
                fixture.SetInventory(new()); Check(!owner.IsCompleted, "source metadata became an item receipt");
                fixture.SetInventory(new() { [item] = count - 1 }); Check(!owner.IsCompleted, "partial quantity completed");
                fixture.SetInventory(new() { [item] = count }); Check(owner.IsCompleted, "full observed quantity was not acknowledged");
            }));
        }
        cases.Add(("all original quest identities and base bytes are preserved", () =>
            Check(database.Quests.Count == 4335 && database.Quests.Select(q => q.Id).Distinct().Count() == 4335
                && original.SequenceEqual(File.ReadAllBytes(Path.Combine(folder, "quest_data.json"))),
                "loader changed the original dataset or quest membership")));
        int passed = 0, assertions = 0, errors = 0;
        foreach (var test in cases)
        {
            try { test.Body(); passed++; Console.WriteLine("PASS reviewed donor: " + test.Name); }
            catch (Failure error) { assertions++; Console.Error.WriteLine("FAIL reviewed donor: " + test.Name + ": " + error.Message); }
            catch (Exception error) { errors++; Console.Error.WriteLine("ERROR reviewed donor: " + test.Name + ": " + error); }
        }
        Console.WriteLine($"Reviewed donor scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={errors}; actual knowledge/profile/behavior, controlled inventory, no game.");
        if (assertions + errors != 0) throw new InvalidOperationException("Reviewed donor knowledge regression");
    }

    private static void Check(bool value, string message) { if (!value) throw new Failure(message); }
}
