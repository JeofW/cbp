using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using WholesomeAQ;

internal static class QuestGameObjectIdentityRepairRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }
    private const int OldEntry = 991100, NewEntry = 991101, Item = 991010;

    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, Action Test)>();
        void Case(string name, Action<QuestDataRepairPackRegressionTests.Fixture> body) =>
            cases.Add((name, () => { using var f = Fixture(); body(f); }));
        Case("bound chest identity reaches the effective objective", f =>
        {
            var db = Accepted(f);
            Check(db.Quests.Single().Objectives.Single().GameObjectId == NewEntry, "new entry was not applied");
        });
        Case("base dataset and original model remain unchanged", f =>
        {
            byte[] before = File.ReadAllBytes(f.DataPath);
            var original = JsonSerializer.Deserialize<QuestDatabase>(before)!;
            try { _ = QuestDataRepairPackLoader.Apply(System.Text.Encoding.UTF8.GetBytes(f.Pack.ToJsonString()), f.BaseHash, original, out _); }
            catch (InvalidDataException e) { throw new Failure("valid identity patch rejected: " + e.Message); }
            Check(original.Quests.Single().Objectives.Single().GameObjectId == OldEntry, "original model changed");
            Check(before.SequenceEqual(File.ReadAllBytes(f.DataPath)), "base dataset file changed");
        });
        Case("identity repair changes the execution fingerprint", f =>
        {
            string before = f.Loader().ExecutionFingerprint;
            _ = Accepted(f);
            Check(before != f.Loader().ExecutionFingerprint, "identity patch retained old execution identity");
        });
        Case("generated profile uses the new object and original item owner", f =>
        {
            var db = Accepted(f); var quest = db.Quests.Single();
            var point = new SpawnPoint { Map = 0, X = 10, Y = 20, Z = 30 };
            var plan = new QuestPlanEntry { Quest = quest, Stage = QuestWorkStage.Objective,
                ObjectiveIndex = 0, Hotspots = new List<SpawnPoint> { point } };
            string xml = new ProfileBuilder().BuildProfileXml(new[] { plan }, db, "Fixture", "Fixture", 20);
            Check(xml.Contains(NewEntry.ToString(), StringComparison.Ordinal) && !xml.Contains(OldEntry.ToString(), StringComparison.Ordinal),
                "profile emitted the old object identity");
            Check(xml.Contains(Item.ToString(), StringComparison.Ordinal), "required item ownership was lost");
        });
        Case("late rejection cannot mutate the caller's base model", f =>
        {
            var original = JsonSerializer.Deserialize<QuestDatabase>(File.ReadAllBytes(f.DataPath))!;
            f.Pack["DependencyMetadata"]![0]!["GroupMembers"] = new JsonArray(991099);
            bool rejected = false;
            try { _ = QuestDataRepairPackLoader.Apply(System.Text.Encoding.UTF8.GetBytes(f.Pack.ToJsonString()), f.BaseHash, original, out _); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && original.Quests.Single().Objectives.Single().GameObjectId == OldEntry, "partial mutation escaped validation");
        });
        foreach (string fault in new[] { "old-entry", "item", "count", "row", "index", "foreign-quest", "duplicate", "zero-target",
                                         "same-target", "kind", "source", "scripted", "count-overlap", "duplicate-target", "unknown-field" })
        {
            string current = fault;
            Case("reject " + current, f =>
            {
                var r = f.Pack["GameObjectObjectiveRepairs"]![0]!;
                switch (current)
                {
                    case "old-entry": r["ExpectedGameObjectId"] = 991199; break;
                    case "item": r["ItemId"] = Item + 1; break;
                    case "count": r["RequiredCount"] = 3; break;
                    case "row": r["RowIndex"] = 1; break;
                    case "index": r["ObjectiveIndex"] = 1; break;
                    case "foreign-quest": r["QuestId"] = 991002; break;
                    case "duplicate": f.Pack["GameObjectObjectiveRepairs"]!.AsArray().Add(r.DeepClone()); break;
                    case "zero-target": r["GameObjectId"] = 0; break;
                    case "same-target": r["GameObjectId"] = OldEntry; break;
                    case "kind": r["ObjectiveType"] = "CollectItem"; break;
                    case "source": r["SourceRef"] = ""; break;
                    case "scripted": f.SetBase(n => n["Quests"]![0]!["SpecialFlags"] = 32); break;
                    case "count-overlap":
                        f.Pack["ObjectiveCountRepairs"] = new JsonArray(new JsonObject { ["QuestId"] = 991001,
                            ["RowIndex"] = 0, ["ObjectiveIndex"] = 0, ["ObjectiveType"] = "CollectFromGameObject",
                            ["TargetId"] = OldEntry, ["ItemId"] = Item, ["ExpectedCount"] = 2, ["RequiredCount"] = 3,
                            ["SourceRef"] = "controlled://count" });
                        r["RequiredCount"] = 3; break;
                    case "duplicate-target":
                        f.SetBase(n => { var objectives = n["Quests"]![0]!["Objectives"]!.AsArray();
                            var duplicate = objectives[0]!.DeepClone(); duplicate["GameObjectId"] = NewEntry;
                            objectives.Add(duplicate); }); break;
                    case "unknown-field": r["ExecuteLua"] = "not executed"; break;
                }
                f.Write(); bool rejected = false;
                try { _ = f.Load(); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "unsafe identity repair accepted: " + current);
            });
        }
        int passed = 0, failures = 0, unexpected = 0;
        foreach (var test in cases)
        {
            try { test.Test(); passed++; Console.WriteLine("PASS object identity: " + test.Name); }
            catch (Failure e) { failures++; Console.Error.WriteLine("FAIL object identity: " + test.Name + ": " + e.Message); }
            catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR object identity: " + test.Name + ": " + e); }
        }
        Console.WriteLine($"Object identity scenarios: {passed}/{cases.Count}; assertions={failures}; unexpected={unexpected}; actual loader/profile; no game.");
        if (failures + unexpected != 0) throw new InvalidOperationException("GameObject identity repair regression");
    }

    private static QuestDatabase Accepted(QuestDataRepairPackRegressionTests.Fixture f)
    {
        f.Write();
        try { return f.Load(); }
        catch (InvalidDataException e) { throw new Failure("bound ordinary patch rejected: " + e.Message); }
    }
    private static void Check(bool value, string message) { if (!value) throw new Failure(message); }
    private static QuestDataRepairPackRegressionTests.Fixture Fixture()
    {
        var f = new QuestDataRepairPackRegressionTests.Fixture();
        f.SetBase(n =>
        {
            n["Quests"]![0]!["StartItem"] = 0;
            n["Quests"]![0]!["Objectives"] = new JsonArray(new JsonObject
            {
                ["Type"] = (int)ObjectiveType.CollectFromGameObject, ["Index"] = 0,
                ["GameObjectId"] = OldEntry, ["MobId"] = 0, ["ItemId"] = Item, ["CollectCount"] = 2, ["KillCount"] = 0
            });
        });
        f.Pack["QuestMetadata"] = new JsonArray();
        f.Pack["GameObjectObjectiveRepairs"] = new JsonArray(new JsonObject
        {
            ["QuestId"] = 991001, ["RowIndex"] = 0, ["ObjectiveIndex"] = 0,
            ["ObjectiveType"] = "CollectFromGameObject", ["ExpectedGameObjectId"] = OldEntry,
            ["GameObjectId"] = NewEntry, ["ItemId"] = Item, ["RequiredCount"] = 2,
            ["SourceRef"] = "controlled://gameobject_template/991101"
        });
        return f;
    }
}
