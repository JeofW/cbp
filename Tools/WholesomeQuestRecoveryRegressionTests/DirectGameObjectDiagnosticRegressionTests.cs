using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Bots.Quest.Objectives;
using Bots.Quest.QuestOrder;
using CommonBehaviors.Actions;
using Styx.Logic.Questing;

// Exercise the actual diagnostic capture with the actual direct objective and
// an externally supplied immutable owner observation. No tree is started and
// no read can authorize a move, native interaction, credit or retry.
internal static class DirectGameObjectDiagnosticRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        using var fixture = new QuestDatasetObservationFixture();
        fixture.SetQuest(13084, "Vandalizing Jotunheim", 80,
            new[] { unchecked((int)0x80000000) | 191820, 0, 0, 0 }, new[] { 10, 0, 0, 0 }, new int[6], new int[6]);
        fixture.SetProgress(new[] { 2, 0, 0, 0 });
        using var owner = new UseGameObjectObjective(fixture.Quest, new(),
            fixture.Quest.GetObjectives().Single(item => item.ID == 191820), new());
        using var behavior = new ForcedQuestObjective(owner);
        var action = (QuestGameObjectInteraction)owner.CreateBranch();
        var property = typeof(QuestGameObjectInteraction).GetProperty("LastObservation")!;
        var baseline = new QuestGameObjectInteraction.Observation(DateTime.UtcNow,
            13084, owner.Objective.Index, 191820, fixture.Player.Guid, fixture.Player.MapId,
            4411, 191820, "interaction-issued-not-acknowledged", "waiting-for-separate-typed-credit",
            "QuestGameObjectInteraction", 2, 2, 10, new[] { 10d, 10d, 10d }, new[] { 12d, 10d, 10d },
            2d, 0d, 4.75d, false, false, true, 0, "ground:ReachedDestination", 1, 1,
            "native-request-not-credit", DateTime.UtcNow, true);
        int passed = 0, failed = 0;
        void Check(string name, System.Action test)
        {
            try { test(); passed++; }
            catch (Exception error) { failed++; Console.WriteLine("FAIL DirectGO diagnostic " + name + ": " + error.Message); }
        }
        JsonDocument Capture(QuestGameObjectInteraction.Observation value)
        {
            property.SetValue(action, value);
            return JsonDocument.Parse(QuestExecutionDiagnostics.Capture(behavior, new[] { 2, 0, 0, 0 },
                new[] { 2, 0, 0, 0 }, 60, 1));
        }
        Check("pending direct use survives an idle loot POI", () =>
        {
            using var document = Capture(baseline);
            var row = document.RootElement;
            Require(row.GetProperty("phase").GetString() == baseline.Phase, "active direct-use state was replaced by missing-POI diagnostics");
            Require(row.GetProperty("selected_guid").GetUInt64() == 4411 && row.GetProperty("selected_entry").GetUInt32() == 191820,
                "direct objective identity was lost");
            Require(row.GetProperty("blocking_node").GetString() == baseline.Node, "the direct owner was omitted");
            Require(row.GetProperty("pending_acknowledgement").GetBoolean(), "pending request became an acknowledged result");
            Require(row.GetProperty("selection_observation_source").GetString() == "direct-objective-receipt",
                "a recent receipt was falsely labelled a current ObjectManager acquisition");
        });
        Check("old and current counter evidence remain separate", () =>
        {
            using var document = Capture(baseline);
            var row = document.RootElement;
            Require(row.GetProperty("action_progress_before").GetInt32() == 2 && row.GetProperty("action_progress_current").GetInt32() == 2,
                "action-bound counter evidence is missing");
            Require(row.GetProperty("objective_counter").GetInt32() == 2 && row.GetProperty("required_count").GetInt32() == 10,
                "diagnostic did not retain the actual native objective counter");
            Require(row.GetProperty("retry_state").GetProperty("episode_dispatches").GetInt32() == 1,
                "direct request budget is missing");
        });
        Check("supported direct coordinates and last request are retained", () =>
        {
            using var document = Capture(baseline);
            var row = document.RootElement;
            Require(row.GetProperty("live_coordinates")[0].GetDouble() == 12 && row.GetProperty("z_delta").GetDouble() == 0,
                "selected-object geometry is missing");
            Require(row.GetProperty("last_interaction").GetString() == baseline.LastInteraction,
                "last native request is absent");
            Require(row.GetProperty("navigation_result").GetString() == baseline.NavigationResult,
                "approach result was lost");
        });
        foreach (var sample in new[] {
            ("expired", baseline with { ObservedUtc = DateTime.UtcNow.AddMinutes(-1) }),
            ("future", baseline with { ObservedUtc = DateTime.UtcNow.AddMinutes(1) }),
            ("different quest", baseline with { QuestId = 1 }),
            ("different objective", baseline with { ObjectiveEntry = 1 }),
            ("different index", baseline with { ObjectiveIndex = owner.Objective.Index + 1 }),
            ("different actor", baseline with { PlayerGuid = fixture.Player.Guid + 1 }),
            ("different map", baseline with { MapId = fixture.Player.MapId + 1 }),
            ("different required count", baseline with { Required = 11 }) })
        {
            Check(sample.Item1 + " receipt cannot replace current evidence", () =>
            {
                using var document = Capture(sample.Item2);
                Require(document.RootElement.GetProperty("phase").GetString() != baseline.Phase,
                    "foreign or stale direct receipt was accepted");
            });
        }
        Check("missing navigation remains explicitly unknown", () =>
        {
            using var document = Capture(baseline);
            foreach (string field in new[] { "path_waypoint_index", "path_waypoint_count", "path_waypoint",
                "swimming", "falling", "collision_observation", "blackspot_observation", "pending_acknowledgement" })
                Require(document.RootElement.TryGetProperty(field, out _), "missing bounded navigation diagnostic: " + field);
        });
        Check("diagnostics never create or start the forced tree", () =>
        {
            Require(behavior.ExistingBranch == null && !action.IsRunning, "capture started or created work");
            Require(!behavior.IsDone, "capture fabricated completion");
        });
        Console.WriteLine($"Direct GameObject diagnostic cases: {passed}/{passed + failed}; actual capture and owner; immutable controlled receipts; no game action.");
        if (failed != 0) throw new InvalidOperationException("Direct objective diagnostics have " + failed + " failing cases");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
