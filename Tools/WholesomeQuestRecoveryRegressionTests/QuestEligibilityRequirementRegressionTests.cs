using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;

// Predicates follow pinned TC335 Player::CanTakeQuest/SatisfyQuest*. Requirements
// and observations are explicit fixtures, not backfilled facts about this realm.
internal static class QuestEligibilityRequirementRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        var tests = new List<(string Name, Action Test)>();
        void Case(string name, Action<QuestEntry> edit, bool expected, int level = 60, int classId = 2,
            Dictionary<int, int>? skills = null, Dictionary<int, int>? reputations = null)
        {
            tests.Add((name, () =>
            {
                var quest = Quest(); edit(quest);
                bool selected = Plan(quest, level, classId, skills, reputations).Plan.Any(value => value.Stage == QuestWorkStage.Pickup);
                Check(selected == expected, "expected pickup=" + expected + "; actual=" + selected);
            }));
        }
        Case("missing optional metadata retains server-offer admission", _ => { }, true);
        Case("zero maximum means no upper level limit", q => q.MaxLevel = 0, true);
        Case("at the maximum level remains eligible", q => q.MaxLevel = 60, true);
        Case("above the explicit maximum is excluded", q => q.MaxLevel = 59, false);
        Case("negative maximum metadata is rejected", q => q.MaxLevel = -1, false);
        Case("zero class mask allows a paladin", q => q.AllowableClasses = 0, true);
        Case("paladin class mask admits a paladin", q => q.AllowableClasses = 2, true);
        Case("warrior-only mask excludes a paladin", q => q.AllowableClasses = 1, false);
        Case("multiple allowed classes include the current class", q => q.AllowableClasses = 3, true);
        Case("unknown class defers a constrained quest", q => q.AllowableClasses = 2, false, classId: 0);
        Case("invalid shifted class cannot alias a valid class", q => q.AllowableClasses = 2, false, classId: 34);
        Case("zero skill ID does not impose a requirement", q => { q.RequiredSkillID = 0; q.RequiredSkillPoints = 300; }, true);
        Case("required skill observation is not defaulted to zero", q => { q.RequiredSkillID = 164; q.RequiredSkillPoints = 300; }, false);
        Case("an unrelated observed skill does not satisfy the requirement", q => { q.RequiredSkillID = 164; q.RequiredSkillPoints = 300; }, false, skills: new() { [165] = 450 });
        Case("below the skill minimum is excluded", q => { q.RequiredSkillID = 164; q.RequiredSkillPoints = 300; }, false, skills: new() { [164] = 299 });
        Case("exact skill minimum is eligible", q => { q.RequiredSkillID = 164; q.RequiredSkillPoints = 300; }, true, skills: new() { [164] = 300 });
        Case("above the skill minimum is eligible", q => { q.RequiredSkillID = 164; q.RequiredSkillPoints = 300; }, true, skills: new() { [164] = 301 });
        Case("omitted skill threshold remains unknown", q => q.RequiredSkillID = 164, false, skills: new() { [164] = 300 });
        Case("known zero skill threshold does not require a trained skill", q => { q.RequiredSkillID = 164; q.RequiredSkillPoints = 0; }, true, skills: new() { [164] = 0 });
        Case("reputation minimum requires an observation", q => { q.RequiredMinRepFaction = 932; q.RequiredMinRepValue = 3000; }, false);
        Case("below reputation minimum is excluded", q => { q.RequiredMinRepFaction = 932; q.RequiredMinRepValue = 3000; }, false, reputations: new() { [932] = 2999 });
        Case("at reputation minimum is eligible", q => { q.RequiredMinRepFaction = 932; q.RequiredMinRepValue = 3000; }, true, reputations: new() { [932] = 3000 });
        Case("negative reputation thresholds retain their sign", q => { q.RequiredMinRepFaction = 932; q.RequiredMinRepValue = -3000; }, true, reputations: new() { [932] = -3000 });
        Case("below negative reputation threshold is excluded", q => { q.RequiredMinRepFaction = 932; q.RequiredMinRepValue = -3000; }, false, reputations: new() { [932] = -3001 });
        Case("just below reputation maximum is eligible", q => { q.RequiredMaxRepFaction = 934; q.RequiredMaxRepValue = 9000; }, true, reputations: new() { [934] = 8999 });
        Case("at reputation maximum is excluded", q => { q.RequiredMaxRepFaction = 934; q.RequiredMaxRepValue = 9000; }, false, reputations: new() { [934] = 9000 });
        Case("above reputation maximum is excluded", q => { q.RequiredMaxRepFaction = 934; q.RequiredMaxRepValue = 9000; }, false, reputations: new() { [934] = 9001 });
        Case("reputation maximum with missing threshold defers", q => q.RequiredMaxRepFaction = 934, false, reputations: new() { [934] = 0 });
        Case("zero reputation factions do not require observations", q => { q.RequiredMinRepFaction = 0; q.RequiredMaxRepFaction = 0; }, true);
        Case("first faction objective is not a pickup minimum", q => { q.RequiredFactionId1 = 932; q.RequiredFactionValue1 = 9000; }, true, reputations: new() { [932] = 0 });
        Case("second faction objective below cap retains pickup", q => { q.RequiredFactionId2 = 934; q.RequiredFactionValue2 = 9000; }, true, reputations: new() { [934] = 8999 });
        Case("second faction objective at cap excludes pickup", q => { q.RequiredFactionId2 = 934; q.RequiredFactionValue2 = 9000; }, false, reputations: new() { [934] = 9000 });
        Case("second faction objective without threshold is unknown", q => q.RequiredFactionId2 = 934, false, reputations: new() { [934] = 0 });
        tests.Add(("omitted JSON requirement is distinct from explicit zero", () =>
        {
            var missing = JsonSerializer.Deserialize<QuestEntry>("{\"Id\":1}")!;
            var zero = JsonSerializer.Deserialize<QuestEntry>("{\"Id\":1,\"AllowableClasses\":0,\"MaxLevel\":0}")!;
            Check(missing.AllowableClasses == null && missing.MaxLevel == null && zero.AllowableClasses == 0 && zero.MaxLevel == 0,
                "optional source evidence was collapsed into default unrestricted values");
        }));
        tests.Add(("class restriction also prevents ancestor correction", () =>
        {
            var child = Quest(); child.PrevQuestID = 930002; child.AllowableClasses = 1;
            var parent = Quest(); parent.Id = 930002; parent.Objectives = new() { new QuestObjective { Type = ObjectiveType.KillMob, MobId = 930010, KillCount = 2, Index = 0 } };
            var result = Plan(child, 60, 2, null, null, parent);
            Check(result.Plan.All(value => value.Stage != QuestWorkStage.AncestorCorrection), "ineligible child forced ancestor correction");
        }));
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var test in tests)
        {
            try { test.Test(); passed++; Console.WriteLine("PASS quest eligibility: " + test.Name); }
            catch (Failure error) { assertions++; Console.Error.WriteLine("FAIL quest eligibility: " + test.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR quest eligibility: " + test.Name + ": " + error); }
        }
        Console.WriteLine($"Quest eligibility scenarios: {passed}/{tests.Count}; assertions={assertions}; unexpected={unexpected}; actual scheduler; pinned TC335 predicates; controlled requirements; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Quest eligibility requirements regression");
    }

    private static QuestEntry Quest() => new() { Id = 930001, Name = "Requirement boundary", MinLevel = 58, QuestLevel = 62,
        Objectives = new() { new QuestObjective { Type = ObjectiveType.TurnInOnly, Index = 0 } } };
    private static QuestScheduleResult Plan(QuestEntry quest, int level, int classId,
        Dictionary<int, int>? skills, Dictionary<int, int>? reputations, QuestEntry? parent = null)
    {
        var db = new QuestDatabase
        {
            Quests = parent == null ? new() { quest } : new() { quest, parent },
            QuestGivers = new() { new QuestGiverEntry { QuestId = quest.Id, GiverId = 930010, GiverType = QuestObjectType.Creature, GiverName = "Giver" } },
            CreatureSpawns = new() { ["930010"] = new() { new SpawnPoint { Map = 530, X = 10, Y = 0, Z = 10 } } }
        };
        var snapshot = new QuestSchedulerSnapshot
        {
            UtcNow = new DateTime(2026, 9, 29, 8, 28, 0, DateTimeKind.Utc), PlayerLevel = level, PlayerRaceId = 10,
            PlayerClassId = classId, MapId = 530, HasAuthoritativeCompletions = true, SkillValues = skills, ReputationValues = reputations,
            AcceptedQuests = parent == null ? Array.Empty<QuestSchedulerAcceptedQuest>() : new[]
            { new QuestSchedulerAcceptedQuest { QuestId = (uint)parent.Id, ObjectiveCounts = new[] { 0, 0, 0, 0 },
                NormalObjectiveIds = new[] { 930010, 0, 0, 0 }, NormalObjectiveRequiredCounts = new[] { 2, 0, 0, 0 } } }
        };
        return QuestScheduler.MaterializeSchedule(db, snapshot, _ => new QuestRecoveryDecision
            { State = QuestRecoveryState.Eligible, MayAttempt = true, Status = "controlled" }, 20, 250, 7,
            navigationAssessment: _ => new SpawnNavigationAssessment { IsKnownSafe = true, IsKnownReachable = true });
    }
    private static void Check(bool value, string reason) { if (!value) throw new Failure(reason); }
}
