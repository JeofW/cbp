#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Bots.Quest.Objectives;
using Bots.Quest.QuestOrder;
using CommonBehaviors.Actions;
using Styx.Logic.Inventory.Frames.LootFrame;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Profiles.Quest;
using Styx.WoWInternals;
using Styx.WoWInternals.World;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

namespace Styx.Logic.Questing;

/// <summary>
/// Bounded, read-only diagnostic capture. It does not construct a behavior,
/// advance a hotspot, probe a new route, interact, or certify a quest primitive.
/// Null fields preserve unavailable observations rather than fabricating zeros.
/// </summary>
public static class QuestExecutionDiagnostics
{
    public static string Capture(ForcedQuestObjective behavior, IReadOnlyList<int> before,
        IReadOnlyList<int> current, double noProgressSeconds, int recoveryRequests)
    {
        var row = new Dictionary<string, object?>
        {
            ["schema"] = "quest-execution-diagnostic-335-v1", ["observed_utc"] = DateTime.UtcNow,
            ["quest_id"] = behavior?.Objective?.Quest?.Id, ["objective_index"] = null, ["objective_id"] = null,
            ["primitive"] = "unresolved", ["progress_before"] = before, ["progress_current"] = current,
            ["no_progress_seconds"] = noProgressSeconds, ["selected_guid"] = null, ["selected_entry"] = null,
            ["source_type"] = null, ["declared_sources"] = Array.Empty<object>(), ["static_coordinates"] = Array.Empty<object>(),
            ["live_coordinates"] = null, ["player_coordinates"] = null, ["map_id"] = null, ["z_delta"] = null,
            ["mounted"] = null, ["flying"] = null, ["moving"] = null, ["movement_flags"] = null, ["movement_known"] = false,
            ["navigation_destination"] = null, ["navigation_result"] = "unavailable", ["interaction_range"] = null,
            ["path_waypoint_index"] = null, ["path_waypoint_count"] = null, ["path_waypoint"] = null,
            ["collision_observation"] = "no-bound-collision-receipt", ["blackspot_observation"] = null,
            ["swimming"] = null, ["falling"] = null, ["pending_acknowledgement"] = null,
            ["selection_observation_source"] = "no-selected-object-observation",
            ["within_interaction_range"] = null, ["line_of_sight"] = null, ["inventory_status"] = "unavailable",
            ["inventory_count"] = null, ["free_normal_bag_slots"] = null, ["blacklisted"] = null,
            ["retry_state"] = new { watchdog_requests = recoveryRequests }, ["blocking_node"] = ExistingNode(behavior?.ExistingBranch),
            ["last_interaction"] = "unobserved", ["last_interaction_utc"] = null,
            ["phase"] = "observations-unavailable", ["reason"] = "current-observation-not-established",
            ["action_dispatch_is_not_progress"] = true
        };
        try
        {
            var owner = behavior?.Objective;
            var actor = ObjectManager.Me;
            if (owner == null || actor == null || !actor.IsValid || actor.Guid == 0) return Serialize(row);
            ulong actorGuid = actor.Guid;
            uint map = actor.MapId, questId = owner.Quest.Id;
            var poi = BotPoi.Current;
            var subject = poi?.AsObject;
            ulong subjectGuid = subject?.Guid ?? 0;
            row["player_guid"] = actorGuid;
            row["player_coordinates"] = Point(actor.Location);
            row["map_id"] = map;
            row["mounted"] = actor.Mounted;
            row["flying"] = actor.IsFlying;
            row["moving"] = actor.IsMoving;
            row["swimming"] = actor.IsSwimming;
            row["falling"] = actor.IsFalling;
            row["shapeshift"] = actor.Shapeshift.ToString();
            row["combat"] = actor.IsActuallyInCombat || actor.PetInCombat;
            row["dead"] = !actor.IsAlive;
            bool movementKnown = actor.TryGetMovementState(out uint movementFlags, out ulong transport);
            row["movement_known"] = movementKnown;
            row["movement_flags"] = movementKnown ? movementFlags : null;
            row["transport_guid"] = movementKnown ? transport : null;
            row["free_normal_bag_slots"] = actor.FreeNormalBagSlots;
            Quest.QuestObjective? typed = owner switch
            {
                CollectItemObjective collection => collection.Objective,
                GrindObjective kill => kill.Objective,
                UseGameObjectObjective use => use.Objective,
                _ => null
            };
            CollectFrom[] sources = Array.Empty<CollectFrom>();
            var staticPoints = new List<WoWPoint>();
            if (typed.HasValue)
            {
                var objective = typed.Value;
                row["objective_index"] = objective.Index;
                row["objective_id"] = objective.ID;
                row["required_count"] = objective.Count;
                row["objective_type"] = objective.Type.ToString();
                row["primitive"] = owner is CollectItemObjective ? "item-collection-source-unresolved"
                    : owner is GrindObjective ? "creature-kill" : "direct-gameobject-interaction";
                if (owner is CollectItemObjective)
                {
                    var profile = owner.OverridedQuestInfo?.FindCollectItem((uint)objective.ID);
                    sources = profile?.OverridedCollectFrom?.ToArray() ?? Array.Empty<CollectFrom>();
                    if (profile?.OverridedHotspots != null)
                        staticPoints.AddRange(profile.OverridedHotspots);
                    if (sources.Any(s => s.Type == CollectFromType.GameObject)) row["primitive"] = "ground-gameobject-loot";
                    else if (sources.Any(s => s.Type == CollectFromType.Mob)) row["primitive"] = "creature-loot-item";
                    else if (sources.Any(s => s.Type == CollectFromType.Vendor)) row["primitive"] = "vendor-item-acquisition";
                    var inventory = QuestInventorySnapshot.Capture(actor);
                    row["inventory_status"] = inventory.Status;
                    if (inventory.IsComplete && inventory.PlayerGuid == actorGuid && inventory.IsCurrent())
                        row["inventory_count"] = inventory.ItemCounts!.TryGetValue(objective.ID, out long quantity) ? quantity : 0L;
                }
                else if (QuestObjectiveCompletion.TryReadTypedNormalObjectiveProgress(owner.Quest,
                    owner is UseGameObjectObjective ? unchecked((int)0x80000000) | objective.ID : objective.ID,
                    objective.Count, out int progress)) row["objective_counter"] = progress;
                if (owner is UseGameObjectObjective)
                {
                    sources = new[] { new CollectFrom((uint)objective.ID, null, CollectFromType.GameObject) };
                    var directProfile = owner.OverridedQuestInfo?.FindUseGameObject((uint)objective.ID);
                    if (directProfile?.OverridedHotspots != null) staticPoints.AddRange(directProfile.OverridedHotspots);
                }
            }
            row["declared_sources"] = sources.Take(64).Select(source => new { entry = source.ID, type = source.Type.ToString() }).ToArray();
            row["declared_source_count"] = sources.Length;
            var area = StyxWoW.AreaManager.CurrentGrindArea;
            if (staticPoints.Count == 0 && area != null) staticPoints.AddRange(area.Hotspots.Select(h => h.Position));
            row["static_coordinates"] = staticPoints.Take(16).Select(Point).ToArray();
            row["static_coordinate_count"] = staticPoints.Count;
            row["ground_interaction_required"] = area?.RequiresGroundInteraction;
            if (Navigator.NavigationProvider is MeshNavigator navigation)
            {
                row["navigation_destination"] = Point(navigation.LastMoveDestination);
                row["navigation_result"] = navigation.LastMoveResult.ToString();
                row["navigation_failure"] = navigation.LastRouteFailure.ToString();
                row["navigation_attempt_utc"] = navigation.LastMoveAttemptUtc;
                row["active_path"] = navigation.HasActivePath;
                var path = navigation.CurrentPath;
                int index = navigation.CurrentPathIndex, count = path.Count;
                row["path_waypoint_index"] = index;
                row["path_waypoint_count"] = count;
                row["path_waypoint"] = index >= 0 && index < count ? Point(path[index]) : null;
                row["navigation_request_sequence"] = navigation.LastMoveAttemptSequence;
                row["riding_elevator"] = navigation.IsRidingElevator;
                // These are existing registered avoidance regions, not evidence
                // of a fresh TraceLine hit or a safe physical detour.
                var spots = BlackspotManager.Blackspots.ToArray();
                row["blackspot_observation"] = new { count = spots.Length, truncated = spots.Length > 16,
                    regions = spots.Take(16).Select(spot => new { coordinates = Point(spot.Location),
                        radius = Number(spot.Radius), height = Number(spot.Height) }).ToArray() };
            }
            row["poi_type"] = poi?.Type.ToString();
            row["poi_coordinates"] = poi == null ? null : Point(poi.Location);
            row["loot_window_guid"] = LootFrame.Instance.LootingObjectGuid;
            row["loot_window_visible"] = LootFrame.Instance.IsVisible;
            bool live = subject != null && subject.IsValid && !subject.IsDisabled && subjectGuid != 0;
            bool? range = null;
            if (live)
            {
                row["selected_guid"] = subjectGuid;
                row["selection_observation_source"] = "current-poi-object";
                row["selected_entry"] = subject!.Entry;
                row["source_type"] = subject is WoWGameObject ? "GameObject" : subject is WoWUnit ? "Creature" : subject.GetType().Name;
                var location = subject.Location;
                row["live_coordinates"] = Point(location);
                row["z_delta"] = Number(actor.Location.Z - location.Z);
                row["distance"] = Number(actor.Location.Distance(location));
                row["interaction_range"] = Number(subject.InteractRange);
                range = subject.WithinInteractRange;
                row["within_interaction_range"] = range;
                row["blacklisted"] = Blacklist.Contains(subjectGuid);
                if (Point(actor.Location) != null && Point(location) != null)
                    row["line_of_sight"] = GameWorld.IsInLineOfSight(actor.Location.Add(0, 0, 1), location.Add(0, 0, 1));
                if (subject is WoWGameObject gameObject)
                {
                    row["currently_lootable"] = gameObject.CanLoot;
                    row["gameobject_type"] = gameObject.SubType.ToString();
                    row["dynamic_flags"] = gameObject.FlagsDynamic;
                }
            }
            uint[] gameObjects = sources.Where(s => s.Type == CollectFromType.GameObject).Select(s => s.ID).ToArray();
            if (gameObjects.Length > 0)
            {
                var loaded = ObjectManager.GetObjectsOfType<WoWGameObject>().Where(go => go != null && go.IsValid
                    && !go.IsDisabled && gameObjects.Contains(go.Entry)).ToArray();
                row["matching_loaded_objects"] = loaded.Length;
                row["live_candidates"] = loaded.Take(8).Select(go => new { guid = go.Guid, entry = go.Entry,
                    coordinates = Point(go.Location), can_loot = go.CanLoot, blacklisted = Blacklist.Contains(go.Guid) }).ToArray();
            }
            var receipt = GroundLootApproach.LastObservation;
            var directReceipt = (owner as UseGameObjectObjective)?.ExecutionObservation;
            bool directMatched = directReceipt != null && typed.HasValue && owner is UseGameObjectObjective
                && directReceipt.QuestId == questId && directReceipt.ObjectiveIndex == typed.Value.Index
                && directReceipt.ObjectiveEntry == typed.Value.ID && directReceipt.Required == typed.Value.Count
                && directReceipt.PlayerGuid == actorGuid && directReceipt.MapId == map
                && (DateTime.UtcNow - directReceipt.ObservedUtc).TotalSeconds is >= 0 and <= 15;
            bool matched = receipt != null && receipt.PlayerGuid == actorGuid && receipt.MapId == map
                && receipt.ObjectGuid == subjectGuid && live && receipt.ObjectEntry == subject!.Entry
                && (DateTime.UtcNow - receipt.ObservedUtc).TotalSeconds is >= 0 and <= 15;
            if (directMatched)
            {
                // Direct interaction keeps its own subject while the loot POI
                // remains idle. Preserve its timestamped state without claiming
                // that a historical selection is a fresh live acquisition.
                var direct = directReceipt!;
                row["selection_observation_source"] = "direct-objective-receipt";
                row["selection_observed_utc"] = direct.ObservedUtc;
                row["selected_guid"] = direct.ObjectGuid;
                row["selected_entry"] = direct.ObjectEntry;
                row["source_type"] = "GameObject";
                row["live_coordinates"] = direct.ObjectXYZ;
                row["action_player_coordinates"] = direct.PlayerXYZ;
                row["distance"] = direct.Distance.HasValue ? Number(direct.Distance.Value) : null;
                row["z_delta"] = direct.ZDelta.HasValue ? Number(direct.ZDelta.Value) : null;
                row["interaction_range"] = direct.InteractRange.HasValue ? Number(direct.InteractRange.Value) : null;
                row["action_progress_before"] = direct.Before;
                row["action_progress_current"] = direct.Current;
                row["pending_acknowledgement"] = direct.PendingAcknowledgement;
                row["phase"] = direct.Phase;
                row["reason"] = direct.Reason;
                row["blocking_node"] = direct.Node;
                row["navigation_result"] = direct.NavigationResult;
                row["last_interaction"] = direct.LastInteraction;
                row["last_interaction_utc"] = direct.LastInteractionUtc;
                row["retry_state"] = new { watchdog_requests = recoveryRequests,
                    episode_dispatches = direct.EpisodeDispatches, subject_attempts = direct.SubjectAttempts,
                    static_route_failures = direct.StaticRouteFailures, static_route_replans = direct.StaticRouteReplans,
                    static_no_progress_seconds = direct.StaticNoProgressSeconds, static_travel_stopped = direct.StaticTravelStopped };
                row["blacklisted"] = direct.ObjectGuid.HasValue ? Blacklist.Contains(direct.ObjectGuid.Value) : null;
                // An unrelated POI cannot supply interaction/range/LoS facts
                // for the direct owner's subject. Unknown remains explicit.
                row["within_interaction_range"] = null;
                row["line_of_sight"] = null;
            }
            else if (matched)
            {
                row["phase"] = receipt!.Phase;
                row["reason"] = receipt.Reason;
                row["blocking_node"] = receipt.Node;
                row["navigation_result"] = receipt.NavigationResult;
                row["last_interaction"] = receipt.LastInteraction;
                row["last_interaction_utc"] = receipt.LastInteractionUtc;
                row["retry_state"] = new { watchdog_requests = recoveryRequests, route_retries = receipt.RouteRetries,
                    dismount_attempts = receipt.DismountAttempts, pending_seconds = receipt.PendingSeconds };
                if (receipt.Phase == "loot-slots-dispatched")
                {
                    row["phase"] = "loot-dispatched-objective-not-advanced";
                    row["reason"] = "slot-submission-is-not-inventory-or-quest-acknowledgement";
                }
                else if (receipt.Phase == "interaction-issued") row["phase"] = "interaction-issued-not-acknowledged";
            }
            else if (behavior!.ExistingBranch == null)
            { row["phase"] = "logic-has-no-executing-action"; row["reason"] = "objective-tree-not-created"; }
            else if (!live)
            {
                bool near = staticPoints.Any(p => p != WoWPoint.Zero && actor.Location.Distance(p) <= Navigator.PathPrecision);
                int? matches = row.TryGetValue("matching_loaded_objects", out var observedCount) && observedCount is int count ? count : null;
                row["phase"] = ClassifyUnselected(matches, near, noProgressSeconds);
                row["reason"] = matches > 0 ? "matching-live-sources-exist-but-selection-or-admission-did-not-choose-one"
                    : !matches.HasValue ? "no-complete-live-source-acquisition-observation"
                    : near ? "no-matching-live-source; static-spawn-is-not-an-object" : "no-live-object-selected; static-travel-not-completed";
            }
            else if (actor.IsFlying || !movementKnown || range != true)
            { row["phase"] = "live-object-found-cannot-approach"; row["reason"] = "flight-movement-or-range-not-ready"; }
            else
            { row["phase"] = "approached-but-cannot-interact"; row["reason"] = "no-current-ground-interaction-receipt; inspect-running-node-and-admission"; }
            if (current.Count == 0) row["progress_status"] = "unknown-no-completion-authority";
            else row["progress_status"] = "known-unchanged-observation";
            if (!ReferenceEquals(ObjectManager.Me, actor) || actor.Guid != actorGuid || actor.MapId != map
                || !ReferenceEquals(behavior?.Objective, owner) || owner.Quest.Id != questId || !ReferenceEquals(BotPoi.Current, poi))
            { row["phase"] = "observations-unavailable"; row["reason"] = "owner-or-poi-changed-during-diagnostic"; row["coherent"] = false; }
            else row["coherent"] = true;
        }
        catch (Exception error) when (error is not OperationCanceledException && error is not ThreadInterruptedException)
        { row["phase"] = "observations-unavailable"; row["reason"] = "capture-error:" + error.GetType().Name; row["coherent"] = false; }
        return Serialize(row);
    }

    private static string ExistingNode(Composite? root)
    {
        if (root == null) return "objective-tree-not-created";
        string answer = root.GetType().FullName + ":" + root.LastStatus;
        var stack = new Stack<(Composite Node, int Depth)>();
        var seen = new HashSet<Composite>();
        stack.Push((root, 0)); int deepest = -1;
        while (stack.Count > 0 && seen.Count < 256)
        {
            var (node, depth) = stack.Pop();
            if (!seen.Add(node) || depth > 32) continue;
            if (node.IsRunning && depth > deepest) { deepest = depth; answer = node.GetType().FullName + ":Running"; }
            if (node is GroupComposite group)
                foreach (var child in group.Children) if (child != null) stack.Push((child, depth + 1));
        }
        return answer;
    }

    public static string ClassifyUnselected(int? matchingLiveObjects, bool arrived, double noProgressSeconds) =>
        matchingLiveObjects > 0 ? "live-object-found-but-not-selected"
        : !matchingLiveObjects.HasValue || matchingLiveObjects < 0 ? "live-acquisition-unobserved"
        : !arrived ? "travelling"
        : noProgressSeconds >= 60 ? "waiting-for-respawn" : "arrived-but-no-live-object";

    private static double? Number(double value) => double.IsFinite(value) ? value : null;
    private static double[]? Point(WoWPoint point) => point != WoWPoint.Zero && float.IsFinite(point.X)
        && float.IsFinite(point.Y) && float.IsFinite(point.Z) ? new double[] { point.X, point.Y, point.Z } : null;
    private static string Serialize(Dictionary<string, object?> row) => JsonSerializer.Serialize(row);
}
