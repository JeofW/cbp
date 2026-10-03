#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Bots.Quest.Objectives;
using Bots.Quest.QuestOrder;
using Styx;
using Styx.Logic;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Questing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

namespace CommonBehaviors.Actions;

/// <summary>
/// Retains direct-object action identity through approach and a separate typed
/// quest-credit observation. Native use submission is never quest progress.
/// </summary>
public sealed class QuestGameObjectInteraction : TreeSharp.Action
{
    private readonly UseGameObjectObjective _owner;
    private readonly Dictionary<ulong, int> _attempts = new();
    private LocalPlayer? _actor;
    private WoWUnit? _mover;
    private ForcedQuestObjective? _behavior;
    private object? _provider;
    private BotPoi? _poi;
    private WoWGameObject? _subject;
    private GroundLootApproach? _approach;
    private ulong _actorGuid, _guid;
    private uint _map, _entry;
    private int? _lastCount;
    private int _beforeAction, _episodeDispatches;
    private bool _initialized, _pending, _approachStarted;
    private DateTime _attemptUtc, _lastTravelUtc;
    private string _navigationResult = "not-dispatched";
    private string _lastInteraction = "none";
    private DateTime? _lastInteractionUtc;
    private WoWPoint _staticDestination;
    private DateTime _staticSampleUtc;
    private double _staticBestDistance = double.PositiveInfinity, _staticNoProgressSeconds;
    private int _staticFailures, _staticReplans;
    private bool _staticSampleKnown, _staticStopped;

    public sealed record Observation(DateTime ObservedUtc, uint QuestId, int ObjectiveIndex, int ObjectiveEntry,
        ulong PlayerGuid, uint MapId, ulong? ObjectGuid, uint? ObjectEntry, string Phase, string Reason,
        string Node, int? Before, int? Current, int Required, double[]? PlayerXYZ, double[]? ObjectXYZ,
        double? Distance, double? ZDelta, double? InteractRange, bool? Mounted, bool? Flying,
        bool MovementKnown, uint? MovementFlags, string NavigationResult, int EpisodeDispatches,
        int SubjectAttempts, string LastInteraction, DateTime? LastInteractionUtc, bool PendingAcknowledgement)
    {
        public int StaticRouteFailures { get; init; }
        public int StaticRouteReplans { get; init; }
        public double StaticNoProgressSeconds { get; init; }
        public bool StaticTravelStopped { get; init; }
    }

    public Observation? LastObservation { get; private set; }

    public QuestGameObjectInteraction(UseGameObjectObjective owner)
        => _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    public override void Start(object context)
    {
        if (!_initialized)
        {
            _initialized = true;
            _actor = ObjectManager.Me;
            _actorGuid = _actor?.Guid ?? 0;
            _map = _actor?.MapId ?? 0;
            _mover = WoWMovement.ActiveMover;
            _provider = Navigator.NavigationProvider;
            _behavior = QuestOrder.Instance?.CurrentBehavior as ForcedQuestObjective;
            _lastTravelUtc = DateTime.MinValue;
        }
        base.Start(context);
    }

    private bool OwnsObjective() => _actor != null && _actorGuid != 0 && _actor.IsValid && _actor.IsAlive
        && ReferenceEquals(ObjectManager.Me, _actor) && _actor.Guid == _actorGuid && _actor.MapId == _map
        && ReferenceEquals(WoWMovement.ActiveMover, _mover) && ReferenceEquals(_mover, _actor)
        && ReferenceEquals(Navigator.NavigationProvider, _provider)
        && _behavior != null && ReferenceEquals(QuestOrder.Instance?.CurrentBehavior, _behavior)
        && ReferenceEquals(_behavior.Objective, _owner) && !_owner.Quest.IsFailed && _owner.DonePrerequisites;

    private bool Active() => OwnsObjective() && _actor != null && !_actor.IsActuallyInCombat && !_actor.PetInCombat
        && !_actor.IsGhost && !_actor.OnTaxi && !_actor.IsOnTransport
        && _actor.TryGetMovementState(out uint flags, out ulong transport) && transport == 0
        // Original movement flags: swimming or falling cannot establish a
        // supported grounded use. Flying is handled by the approach owner.
        && (flags & 0x00203000u) == 0 && OwnsObjective();

    private static bool IdlePoi(BotPoi? poi) => poi != null
        && (poi.Type == PoiType.None || poi.Type == PoiType.Hotspot || poi.Type == PoiType.Quest);

    private bool Current() => Active() && _poi != null && ReferenceEquals(BotPoi.Current, _poi) && IdlePoi(_poi)
        && _subject != null && _subject.Guid == _guid && _subject.Entry == _entry
        && _owner.IsCurrentGameObject(_subject) && !Blacklist.Contains(_guid) && OwnsObjective();

    private bool ReadCount(out int count) => QuestObjectiveCompletion.TryReadTypedNormalObjectiveProgress(
        _owner.Quest, unchecked((int)0x80000000) | _owner.Objective.ID, _owner.Objective.Count, out count);

    private bool UnchangedProgress() => ReadCount(out int count) && _lastCount.HasValue && count == _lastCount.Value;

    protected override RunStatus Run(object context)
    {
        try
        {
            if (!OwnsObjective()) return Paused("owner-revoked", "actor-map-provider-or-objective-owner-changed", context);
            if (!ReadCount(out int count)) return Paused("progress-unavailable", "typed-quest-credit-observation-is-unknown", context);
            if (!OwnsObjective()) return Paused("owner-revoked", "owner-changed-during-progress-observation", context);
            if (count >= _owner.Objective.Count)
            {
                _pending = false; ReleaseApproach(context);
                return Report("objective-completed", "separate-authoritative-typed-credit-reached-required-count", count);
            }
            if (_lastCount.HasValue && count > _lastCount.Value)
            {
                int before = _lastCount.Value;
                _lastCount = count; _pending = false; _episodeDispatches = 0; _attempts.Clear();
                _staticFailures = _staticReplans = 0; _staticNoProgressSeconds = 0;
                _staticSampleKnown = _staticStopped = false; _staticBestDistance = double.PositiveInfinity;
                ReleaseApproach(context);
                Report("authoritative-progress", "typed-credit-increased-after-a-separate-observation", count, before);
                _subject = null; _guid = 0; _entry = 0;
                return RunStatus.Running;
            }
            if (_lastCount.HasValue && count < _lastCount.Value)
                return Paused("progress-inconsistent", "typed-credit-decreased; retained-attempt-cannot-restart-itself", context);
            _lastCount = count;
            if (!Active()) return Paused("interrupted", "combat-death-transport-swim-fall-or-movement-observation", context);
            var poi = BotPoi.Current;
            if (!IdlePoi(poi)) return Paused("foreign-movement-owner", "current-point-of-interest-belongs-to-another-action", context);
            if (!ReferenceEquals(_poi, poi))
            {
                ReleaseApproach(context);
                if (!_pending) { _subject = null; _guid = 0; _entry = 0; }
                _poi = poi;
            }
            DateTime now = DateTime.UtcNow;
            if (_pending)
            {
                if ((now - _attemptUtc).TotalSeconds < 10)
                    return Report("interaction-issued-not-acknowledged", "waiting-for-separate-typed-quest-credit", count);
                _pending = false;
                _lastInteraction = "typed-credit-timeout";
                if (OwnsObjective() && _guid != 0) Blacklist.Add(_guid, TimeSpan.FromSeconds(15));
                ReleaseApproach(context);
                Report("waiting-for-rescan", "interaction-returned-without-typed-progress; short-subject-deferral", count);
                _subject = null; _guid = 0; _entry = 0;
                return RunStatus.Running;
            }
            if (_episodeDispatches >= 4)
                return Report("bounded-recovery-exhausted", "four-unacknowledged-requests-in-this-no-progress-episode", count);
            if (_subject != null && !Current())
            {
                ReleaseApproach(context); _subject = null; _guid = 0; _entry = 0;
                return Report("live-source-lost", "selected-object-despawned-changed-or-became-ineligible", count);
            }
            if (_subject == null)
            {
                var candidate = _owner.FindCurrentGameObject(_attempts.Where(pair => pair.Value >= 2).Select(pair => pair.Key));
                if (!Active() || !ReferenceEquals(BotPoi.Current, _poi))
                    return Paused("owner-revoked", "ownership-changed-during-live-acquisition", context);
                if (candidate == null)
                {
                    WoWPoint destination = _owner.GetObjectiveLocation();
                    if (!Active() || !ReferenceEquals(BotPoi.Current, _poi))
                        return Paused("owner-revoked", "ownership-changed-during-static-location-observation", context);
                    if (!Finite(destination)) return Report("logic-has-no-executable-target", "no-live-candidate-or-valid-static-location", count);
                    if (!Finite(_actor!.Location)) return Paused("observations-unavailable", "player-coordinates-unavailable-for-static-approach", context);
                    if (_actor.Location.Distance(destination) <= Navigator.PathPrecision)
                    {
                        _staticSampleKnown = false;
                        return Report("arrived-but-no-live-object", "static-position-is-not-a-loaded-usable-object; waiting-for-respawn", count);
                    }
                    if (_staticStopped)
                        return Report("static-travel-exhausted", "bounded-static-route-retries-exhausted; live-acquisition-remains-active", count);
                    double distance = _actor.Location.Distance(destination);
                    bool sameDestination = _staticDestination.Equals(destination);
                    bool displaced = sameDestination && distance + 0.25 < _staticBestDistance;
                    if (displaced)
                    {
                        _staticNoProgressSeconds = 0; _staticFailures = 0;
                        _staticBestDistance = distance;
                    }
                    else if (_staticSampleKnown)
                    {
                        double gap = (now - _staticSampleUtc).TotalSeconds;
                        if (gap > 0 && gap <= 10) _staticNoProgressSeconds += gap;
                    }
                    if (!sameDestination || double.IsPositiveInfinity(_staticBestDistance))
                    { _staticDestination = destination; _staticBestDistance = distance; }
                    _staticSampleUtc = now; _staticSampleKnown = true;
                    bool StaticCurrent() => Active() && ReferenceEquals(BotPoi.Current, _poi)
                        && IdlePoi(_poi) && UnchangedProgress() && Active();
                    if (_staticNoProgressSeconds >= 15)
                    {
                        if (_staticReplans >= 1)
                        {
                            _staticStopped = true;
                            return Report("static-travel-exhausted", "no-observed-displacement-after-one-static-route-recomputation", count);
                        }
                        if (!StaticCurrent()) return Paused("owner-revoked", "static-route-recovery-owner-changed", context);
                        // Spend the bounded recovery before an external callback;
                        // a failed read or paused pulse cannot replenish it.
                        _staticReplans++; _staticNoProgressSeconds = 0;
                        Navigator.Clear();
                        if (!StaticCurrent()) return Paused("owner-revoked", "static-route-clear-replaced-current-owner", context);
                        return Report("static-replanning", "one-bounded-static-route-recomputation; arrival-still-unobserved", count);
                    }
                    if ((now - _lastTravelUtc).TotalMilliseconds >= 250 && !_actor.IsCasting && _actor.ChanneledCastingSpellId == 0)
                    {
                        if (!StaticCurrent()) return Paused("owner-revoked", "static-travel-owner-changed-before-request", context);
                        _lastTravelUtc = now;
                        var result = Navigator.MoveTo(destination);
                        if (!StaticCurrent()) return Paused("owner-revoked", "static-travel-request-replaced-current-owner", context);
                        _navigationResult = "static-ground:" + result;
                        if (result == MoveResult.Failed || result == MoveResult.PathGenerationFailed)
                        {
                            if (++_staticFailures >= 3)
                            {
                                _staticStopped = true;
                                return Report("static-travel-exhausted", "three-failed-static-navigation-requests-without-displacement", count);
                            }
                        }
                    }
                    return Report("travelling", "static-source-travel; live-acquisition-still-required", count);
                }
                _subject = candidate; _guid = candidate.Guid; _entry = candidate.Entry;
                _approach = new GroundLootApproach(Current, () => _subject);
                _approachStarted = false;
            }
            if (!Current() || !UnchangedProgress() || !Current())
                return Paused("owner-revoked", "acquired-source-or-credit-observation-changed", context);
            if (_actor!.IsCasting || _actor.ChanneledCastingSpellId != 0)
                return Paused("casting", "wait-for-current-cast-before-direct-use", context);
            if (!_approachStarted) { _approach!.Start(context); _approachStarted = true; }
            RunStatus approach = _approach!.Tick(context);
            var observation = GroundLootApproach.LastObservation;
            if (observation != null && observation.PlayerGuid == _actorGuid && observation.ObjectGuid == _guid)
                _navigationResult = observation.NavigationResult;
            if (approach == RunStatus.Running)
                return Report(observation?.Phase ?? "approaching", observation?.Reason ?? "ground-approach-pending", count);
            ReleaseApproach(context);
            if (approach != RunStatus.Failure || !Current())
            {
                Report("waiting-for-rescan", "approach-deferred-or-owner-revoked", count);
                _subject = null; _guid = 0; _entry = 0;
                return RunStatus.Running;
            }
            if (!GroundLootApproach.CanInteractDirectlyNow(_subject!, Current))
                return Report("approached-but-cannot-interact", "ground-range-line-of-sight-or-movement-not-confirmed", count);
            // Build12340 Goober calls return AL. CanUse vetoes object state;
            // CanUseNow additionally checks actor state and the usable range.
            // Both are observations and neither submits or certifies quest credit.
            if (!_subject!.CanUse() || !Current()) return Defer("native-object-usability-veto", count, context);
            if (!_subject.CanUseNow() || !Current()) return Defer("native-current-usability-veto", count, context);
            if (!UnchangedProgress() || !Current()
                || !GroundLootApproach.CanInteractDirectlyNow(_subject, Current) || !Current())
                return Paused("owner-revoked", "late-object-actor-credit-or-approach-change", context);
            _beforeAction = count; _attemptUtc = now; _pending = true;
            _episodeDispatches++;
            _attempts[_guid] = _attempts.TryGetValue(_guid, out int attempts) ? attempts + 1 : 1;
            _lastInteraction = "native-request-not-quest-acknowledgement";
            _lastInteractionUtc = now;
            if (!GroundTransition.TryInteractWith(_subject, Current, true))
                _lastInteraction = "owned-native-entry-unavailable; awaiting-separate-credit-before-retry";
            return Report("interaction-issued-not-acknowledged", "native-return-is-not-typed-quest-credit", count);
        }
        catch (Exception error) when (error is not OperationCanceledException && error is not ThreadInterruptedException)
        {
            ReleaseApproach(context);
            // A failed native request can have unknown effects. Preserve a
            // pending acknowledgement budget rather than immediately retrying.
            _lastInteraction = "observation-or-request-error:" + error.GetType().Name;
            return Report("observations-unavailable", _lastInteraction, _lastCount);
        }
    }

    private RunStatus Paused(string phase, string reason, object context)
    {
        _staticSampleKnown = false;
        ReleaseApproach(context);
        return Report(phase, reason, _lastCount);
    }

    private RunStatus Defer(string reason, int count, object context)
    {
        ReleaseApproach(context);
        if (Current()) Blacklist.Add(_guid, TimeSpan.FromSeconds(15));
        Report("approached-but-cannot-interact", reason, count);
        _subject = null; _guid = 0; _entry = 0;
        return RunStatus.Running;
    }

    private RunStatus Report(string phase, string reason, int? count, int? before = null)
    {
        try
        {
            bool known = _actor != null && _actor.TryGetMovementState(out _, out _);
            uint flags = 0; if (known) _actor!.TryGetMovementState(out flags, out _);
            var position = _actor?.Location;
            var target = _subject?.Location;
            LastObservation = new Observation(DateTime.UtcNow, _owner.Quest.Id, _owner.Objective.Index,
                _owner.Objective.ID, _actorGuid, _map, _guid == 0 ? null : _guid, _entry == 0 ? null : _entry,
                phase, reason, "QuestGameObjectInteraction", before ?? (_pending ? _beforeAction : _lastCount),
                count, _owner.Objective.Count, Point(position), Point(target),
                position.HasValue && target.HasValue ? position.Value.Distance(target.Value) : null,
                position.HasValue && target.HasValue ? position.Value.Z - target.Value.Z : null,
                _subject?.InteractRange, _actor?.Mounted, _actor?.IsFlying, known, known ? flags : null,
                _navigationResult, _episodeDispatches, _attempts.TryGetValue(_guid, out int attempts) ? attempts : 0,
                _lastInteraction, _lastInteractionUtc, _pending)
            {
                StaticRouteFailures = _staticFailures, StaticRouteReplans = _staticReplans,
                StaticNoProgressSeconds = _staticNoProgressSeconds, StaticTravelStopped = _staticStopped
            };
        }
        catch (Exception error) when (error is not OperationCanceledException && error is not ThreadInterruptedException)
        { /* Diagnostic failure must not grant action authority. */ }
        return RunStatus.Running;
    }

    private static double[]? Point(WoWPoint? point) => point.HasValue && Finite(point.Value)
        ? new double[] { point.Value.X, point.Value.Y, point.Value.Z } : null;
    private static bool Finite(WoWPoint point) => point != WoWPoint.Zero
        && float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);

    private void ReleaseApproach(object context)
    {
        if (_approachStarted && _approach != null) _approach.Stop(context);
        _approachStarted = false;
    }

    public override void Stop(object context)
    {
        try { ReleaseApproach(context); }
        finally { base.Stop(context); }
    }
}
