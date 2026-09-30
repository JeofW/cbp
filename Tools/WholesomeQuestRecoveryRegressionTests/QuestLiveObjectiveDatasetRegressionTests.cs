using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Bots.Quest.Objectives;
using Styx.Logic.Profiles.Quest;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;

// Every source-consistent row is tested against the actual loaded dataset and
// native objective readers. Supplied live positions do not certify world spawns.
internal static class QuestLiveObjectiveDatasetRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }
    private static readonly DateTime Now = new(2026, 9, 30, 10, 0, 0, DateTimeKind.Utc);
    private const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    [ModuleInitializer]
    internal static void Run()
    {
        string root = QuestTypedStrategyBehaviorRegressionTests.Root();
        string docs = Path.Combine(root, "docs/audit/2026-09-30/live-objectives");
        byte[] input = File.ReadAllBytes(Path.Combine(docs, "source-frontier.json"));
        using var binding = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(docs, "source-frontier-summary.json")));
        Check(Hash(input) == binding.RootElement.GetProperty("source_frontier_sha256").GetString(), "frontier fixture hash changed");
        string dataPath = Path.Combine(root, "runtime-snapshot/Bots/WholesomeAutoQuest-master/quest_data/quest_data.json");
        Check(Hash(File.ReadAllBytes(dataPath)) == binding.RootElement.GetProperty("dataset_sha256").GetString(), "dataset fixture is unbound");
        using var authority = new QuestDataRepairPackRegressionTests.Fixture();
        var loader = new DataLoader(dataPath); var db = loader.Load();
        using var source = JsonDocument.Parse(input);
        var rows = source.RootElement.EnumerateArray().Where(r => r.GetProperty("subject_primary_conflicts").GetArrayLength() == 0).ToArray();
        Check(rows.Length == binding.RootElement.GetProperty("source_consistent_objective_rows").GetInt32(), "source row membership changed");
        Check(rows.Select(r => (r.GetProperty("quest_id").GetInt32(), r.GetProperty("objective_row").GetInt32())).Distinct().Count() == rows.Length,
            "duplicate source objective fixture");
        using var world = new QuestDatasetObservationFixture();
        int passed = 0, failed = 0, unexpected = 0;
        foreach (var row in rows)
        {
            int id = row.GetProperty("quest_id").GetInt32(), ordinal = row.GetProperty("objective_row").GetInt32();
            try
            {
                var quest = db.Quests.Single(q => q.Id == id); var objective = quest.Objectives[ordinal];
                int entry = row.GetProperty("entry").GetInt32(), count = row.GetProperty("count").GetInt32();
                int[] Integers(string key) => row.GetProperty(key).EnumerateArray().Select(v => v.GetInt32()).ToArray();
                int[] normalIds = Integers("normal_ids"), normalCounts = Integers("normal_counts"), itemIds = Integers("item_ids"), itemCounts = Integers("item_counts");
                Check(objective.Type == WholesomeAQ.ObjectiveType.KillMob && objective.MobId == entry && objective.KillCount == count &&
                      objective.Index == row.GetProperty("objective_index").GetInt32(), "source fixture differs from effective objective");
                int slot = row.GetProperty("primary_slot").GetInt32() - 1;
                Check(normalIds[slot] == entry && normalCounts[slot] == count, "source slot identity/count changed");
                Check(!db.CreatureSpawns.TryGetValue(entry.ToString(), out var stored) || stored.Count == 0, "fixture no longer lacks static geometry");
                var isolated = new QuestDatabase { Quests = new() { quest }, CreatureSpawns = db.CreatureSpawns, GameObjectSpawns = db.GameObjectSpawns,
                    QuestGivers = db.QuestGivers.Where(q => q.QuestId == id).ToList(), QuestEnders = db.QuestEnders.Where(q => q.QuestId == id).ToList(),
                    ObjectiveCreditSources = db.ObjectiveCreditSources, DependencyMetadata = db.DependencyMetadata };
                QuestScheduleResult Plan(bool sample, int progress = 0) => QuestScheduler.MaterializeSchedule(isolated, new QuestSchedulerSnapshot
                {
                    UtcNow = Now, PlayerGuid = world.Player.Guid, PlayerLevel = Math.Clamp(quest.MinLevel, 1, 80), PlayerRaceId = 10, PlayerClassId = 2,
                    MapId = 1, X = 10, Y = 10, Z = 10, HasCompleteQuestLog = true, HasAuthoritativeCompletions = true,
                    CompletedQuestIds = Array.Empty<uint>(), CarriedItemCounts = new Dictionary<int, long>(),
                    AcceptedQuests = new[] { new QuestSchedulerAcceptedQuest { QuestId = (uint)id, NormalObjectiveIds = normalIds,
                        NormalObjectiveRequiredCounts = normalCounts, ObjectiveCounts = Enumerable.Range(0, 4).Select(i => i == slot ? progress : 0).ToArray() } },
                    CreatureCredits = sample ? new[] { new QuestCreatureCreditObservation { Entry = entry, Guid = 99001001, PlayerGuid = world.Player.Guid,
                        ObservedUtc = Now, MapId = 1, X = 12, Y = 10, Z = 10, AliveAttackableSelectable = true } } : Array.Empty<QuestCreatureCreditObservation>()
                }, _ => new QuestRecoveryDecision { MayAttempt = true, State = QuestRecoveryState.Eligible }, 50, 250, 80,
                    navigationAssessment: _ => new SpawnNavigationAssessment { IsKnownSafe = true, IsKnownReachable = true });
                bool Selected(QuestScheduleResult result) => result.Plan.Any(p => p.Quest.Id == id && p.ObjectiveIndex == objective.Index && p.Stage == QuestWorkStage.Objective);
                if (row.GetProperty("existing_credit_hint_records").GetInt32() == 0)
                    Check(!Selected(Plan(false)), "missing static objective unexpectedly has an unrecorded route");
                var selected = Plan(true); Check(Selected(selected), "source-matching live direct actor did not reach scheduling");
                Check(Selected(Plan(true, count - 1)), "partial source objective lost work");
                Check(!Selected(Plan(true, count)), "completed source objective was rescheduled");
                var plans = selected.Plan.Where(p => p.Quest.Id == id && p.ObjectiveIndex == objective.Index).ToArray();
                string xml = new ProfileBuilder().BuildProfileXml(plans, isolated, "Controlled live source fixture", "Fixture", Math.Clamp(quest.MinLevel, 1, 80));
                var element = XDocument.Parse(xml).Descendants("QuestOrder").Descendants("Objective").Single();
                Check((int?)element.Attribute("MobId") == entry, "profile changed required entry");
                world.SetQuest((uint)id, quest.Name, Math.Clamp(quest.MinLevel, 1, 80), normalIds, normalCounts, itemIds, itemCounts);
                world.SetAccepted(true); world.LoadProfile(xml);
                var behavior = world.CreateObjective(ObjectiveNode.FromXml(element));
                Check(behavior.Objective is GrindObjective && !behavior.IsDone, "source row fabricated completion or wrong behavior");
                typeof(QuestObservedDatasetRoutesRegressionTests).GetMethod("CheckActualAliasTarget", Hidden)!
                    .Invoke(null, new object[] { (GrindObjective)behavior.Objective, entry, 0 });
                world.SetProgress(Enumerable.Range(0, 4).Select(i => i == slot ? count - 1 : 0).ToArray());
                Check(!behavior.IsDone, "partial native progress completed source row");
                world.SetProgress(Enumerable.Range(0, 4).Select(i => i == slot ? count : 0).ToArray());
                Check(behavior.IsDone, "full native progress was not acknowledged");
                passed++;
            }
            catch (Failure error) { failed++; Console.Error.WriteLine($"FAIL live source row {id}/{ordinal}: {error.Message}"); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine($"ERROR live source row {id}/{ordinal}: {error}"); }
            finally { world.ReleaseOwners(); }
        }
        Console.WriteLine($"Live objective source rows: {passed}/{rows.Length}; assertions={failed}; unexpected={unexpected}; actual scheduler/profile/behavior/progress; controlled live positions; no quest classification promotion.");
        if (failed + unexpected != 0) throw new InvalidOperationException("Live objective source-row regression");
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void Check(bool result, string message) { if (!result) throw new Failure(message); }
}
