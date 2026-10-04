#nullable enable
using System;
using System.Threading;
using Styx;
using Styx.Logic;
using Styx.Logic.Combat;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Profiles;
using Styx.WoWInternals;
using Styx.WoWInternals.World;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

namespace CommonBehaviors.Actions;

/// <summary>
/// Retains a selected collection source while the shared GroundTransition owns motion.
/// Existing loot/frame/slot owners perform the interaction and collection.
/// Failure means "not collection work" or "ready for the next owner"; handled
/// travel is never quest progress. Every pending state has a bounded deadline.
/// </summary>
public sealed class GroundLootApproach : TreeSharp.Action
{
    private readonly Func<bool> _admitted;
    private readonly Func<WoWGameObject?>? _directSubject;
    private LocalPlayer? _actor;
    private WoWUnit? _mover;
    private WoWObject? _subject;
    private BotPoi? _poi;
    private object? _provider;
    private object? _profile;
    private long _poiGeneration;
    private ulong _actorGuid, _moverGuid, _guid;
    private uint _map, _entry;
    private PoiType _type;
    private WoWPoint _destination;
    private DateTime _startedUtc, _lastProgressUtc;
    private double _bestDistance = double.PositiveInfinity;
    private int _routeRetries;
    private readonly GroundTransition _transition = new(GroundTransitionPurpose.Interaction);
    private string _navigationResult = "not-dispatched";
    private static Observation? _lastObservation;

    public GroundLootApproach(Func<bool> admitted) => _admitted = admitted ?? throw new ArgumentNullException(nameof(admitted));

    // A typed direct objective owns its subject without publishing a loot POI.
    // Both modes reuse the same observed support/landing/dismount boundary.
    public GroundLootApproach(Func<bool> admitted, Func<WoWGameObject?> directSubject) : this(admitted)
        => _directSubject = directSubject ?? throw new ArgumentNullException(nameof(directSubject));

    // An immutable diagnostic receipt, not authority for later movement or loot.
    public sealed record Observation(DateTime ObservedUtc, ulong PlayerGuid, uint MapId,
        ulong ObjectGuid, uint ObjectEntry, string Phase, string Node, string Reason,
        double[] PlayerXYZ, double[] ObjectXYZ, double Distance, double ZDelta,
        double InteractRange, bool Mounted, bool Flying, bool MovementKnown,
        uint MovementFlags, string NavigationResult, int RouteRetries, int DismountAttempts,
        double PendingSeconds, string LastInteraction, DateTime? LastInteractionUtc);

    public static Observation? LastObservation => Volatile.Read(ref _lastObservation);

    public override void Start(object context)
    {
        var poi = BotPoi.Current;
        var direct = _directSubject?.Invoke();
        var actor = ObjectManager.Me;
        var mover = WoWMovement.ActiveMover;
        var subject = _directSubject == null ? poi?.AsObject : direct;
        if (subject is not WoWGameObject && subject is not WoWUnit { IsAlive: false }) subject = null;
        var provider = Navigator.NavigationProvider;
        var profile = ProfileManager.CurrentProfileSnapshot;
        long generation = BotPoi.CurrentGeneration;
        if (!ReferenceEquals(poi, _poi) || generation != _poiGeneration
            || !ReferenceEquals(actor, _actor) || actor?.Guid != _actorGuid || actor?.MapId != _map
            || !ReferenceEquals(mover, _mover) || mover?.Guid != _moverGuid
            || !ReferenceEquals(provider, _provider) || !ReferenceEquals(profile, _profile)
            || !ReferenceEquals(subject, _subject) || subject?.Guid != _guid || subject?.Entry != _entry
            || subject != null && !subject.Location.Equals(_destination)
            || poi?.Type != _type || _directSubject == null && (poi?.Guid != _guid || poi?.Entry != _entry))
        {
            ReleaseTransition();
            _poi = poi; _actor = actor; _mover = mover;
            _subject = subject;
            _provider = provider; _profile = profile; _poiGeneration = generation;
            _actorGuid = _actor?.Guid ?? 0; _moverGuid = _mover?.Guid ?? 0;
            _guid = _directSubject == null ? poi?.Guid ?? 0 : direct?.Guid ?? 0;
            _entry = _directSubject == null ? poi?.Entry ?? 0 : direct?.Entry ?? 0;
            _type = poi?.Type ?? PoiType.None;
            _map = _actor?.MapId ?? 0; _destination = _subject?.Location ?? WoWPoint.Empty;
            _startedUtc = _lastProgressUtc = DateTime.UtcNow;
            _routeRetries = 0; _bestDistance = double.PositiveInfinity;
            _navigationResult = "not-dispatched";
        }
        base.Start(context);
    }

    private bool Current() => _actor != null && _actorGuid != 0 && _actor.IsValid && _actor.IsAlive
        && ReferenceEquals(ObjectManager.Me, _actor) && _actor.Guid == _actorGuid && _actor.MapId == _map
        && _mover != null && _moverGuid == _actorGuid && _mover.IsValid && _mover.Guid == _moverGuid
        && ReferenceEquals(WoWMovement.ActiveMover, _mover) && ReferenceEquals(_mover, _actor)
        && ReferenceEquals(Navigator.NavigationProvider, _provider)
        && ReferenceEquals(ProfileManager.CurrentProfileSnapshot, _profile) && BotPoi.CurrentGeneration == _poiGeneration
        && _poi != null && ReferenceEquals(BotPoi.Current, _poi) && _poi.Type == _type
        && (_directSubject == null ? _poi.Guid == _guid && _poi.Entry == _entry
            && (_type == PoiType.Loot || _type == PoiType.Harvest || _type == PoiType.Skin) && ReferenceEquals(_poi.AsObject, _subject)
            : (_type == PoiType.None || _type == PoiType.Hotspot || _type == PoiType.Quest)
                && ReferenceEquals(_directSubject(), _subject))
        && _subject != null && _guid != 0 && _entry != 0
        && _subject.IsValid && (_subject is not WoWGameObject gameObject || !gameObject.IsDisabled)
        && (_subject is not WoWUnit unit || !unit.IsAlive) && _subject.Guid == _guid && _subject.Entry == _entry
        && _subject.Location.Equals(_destination) && _admitted()
        && ReferenceEquals(ObjectManager.Me, _actor) && _actor.Guid == _actorGuid;

    protected override RunStatus Run(object context)
    {
        if (_subject == null || _directSubject == null && _type != PoiType.Loot && _type != PoiType.Harvest && _type != PoiType.Skin)
            return RunStatus.Failure;
        try
        {
            if (!Current()) { ReleaseTransition(); return RunStatus.Success; }
            var actor = _actor!;
            var target = _subject!;
            // A normal corpse reached on foot or while swimming needs no ground
            // projection or navigation round trip. Its interaction owner still
            // awaits a stopped observation and validates native entry. Mounted,
            // airborne and uncertain movement retain this shared transition.
            if (target is WoWUnit && !HasMountOrFlightForm(actor) && !actor.IsFlying && !actor.MovementInfo.IsDescending
                && actor.TryGetMovementState(out uint corpseFlags, out ulong corpseTransport)
                && corpseTransport == 0 && (corpseFlags & 0x02C03000u) == 0)
            {
                ReleaseTransition();
                return Current() ? RunStatus.Failure : RunStatus.Success;
            }
            DateTime now = DateTime.UtcNow;
            if ((now - _startedUtc).TotalSeconds >= 45) return Defer("ground-approach-deadline");
            if (!Finite(actor.Location) || !Finite(_destination)) return Pending("cannot-approach", "invalid-or-unknown-coordinates");
            double distance = actor.Location.Distance(_destination);
            if (distance + 0.25 < _bestDistance)
            {
                _bestDistance = distance; _startedUtc = _lastProgressUtc = now;
            }
            if ((now - _lastProgressUtc).TotalSeconds >= 15)
            {
                if (_routeRetries == 0 && !actor.IsFlying)
                {
                    ReleaseTransition();
                    if (!Current()) return RunStatus.Success;
                    // The shared owner releases only this route; a global clear could revoke successor work.
                    if (!Current()) return RunStatus.Success;
                    _routeRetries++; _lastProgressUtc = now;
                    return Pending("repositioning", "one-bounded-route-recomputation");
                }
                return Defer("no-approach-progress");
            }
            if (Blacklist.Contains(_guid)) return Defer("selected-object-is-blacklisted", alreadyBlacklisted: true);
            bool collectible = target is WoWGameObject gameObject ? gameObject.CanLoot
                : target is WoWUnit unit && !unit.IsAlive && (_type == PoiType.Skin ? unit.CanSkin : unit.CanLoot);
            if (_directSubject == null && !collectible) return Defer("object-not-currently-lootable-or-consumed");
            if (!Current()) return RunStatus.Success;
            if (!actor.TryGetMovementState(out uint flags, out ulong transport) || transport != 0)
                return Pending("cannot-approach", "movement-or-transport-observation-unavailable");
            // Same original-client movement boundary used by the shared transition;
            // the unrelated legacy MoveFlags aliases are not substituted here.
            if ((flags & 0x3000u) != 0)
                return Pending("landing", "falling-is-not-grounded");
            float range = target.InteractRange;
            if (!float.IsFinite(range) || range <= 0)
                return Pending("cannot-interact", "interaction-range-unavailable");
            if (!Current()) return RunStatus.Success;

            // One shared effect owner supplies travel, landing, removal and
            // readiness. This action retains selection and bounded rescan only.
            GroundTransitionState transition = _transition.Tick(_destination, target, Current);
            if (!Current() || transition == GroundTransitionState.Revoked)
                return EndRevoked();
            _navigationResult = "shared-ground-transition:" + _transition.Phase;
            if (transition == GroundTransitionState.Unavailable)
                return Defer("shared-ground-transition-unavailable");
            if (transition != GroundTransitionState.Ready)
                return Pending(_transition.Phase, "awaiting-shared-travel-or-ground-acknowledgement", retainTransition: true);
            if (actor.IsMoving)
            {
                // Ready already asked the shared owner to stop its input. Wait
                // for the client's movement observation instead of issuing a
                // competing stop from this selection wrapper.
                return Pending("approaching", "awaiting-stationary-observation", retainTransition: true);
            }
            if (!CanInteractNowCore(target, Current, _directSubject != null))
                return Pending("cannot-interact", "current-object-or-actor-usability-unavailable");
            Report("ready-to-interact", "shared-ground-ready-and-current-object-usable");
            return Current() ? RunStatus.Failure : RunStatus.Success;
        }
        catch (Exception error) when (error is not OperationCanceledException && error is not ThreadInterruptedException)
        {
            RecoveryActions.RethrowControlFlow(error);
            ReleaseTransition();
            _navigationResult = "observation-error:" + error.GetType().Name;
            // No failed read supplies movement, mount-removal or loot authority.
            return RunStatus.Success;
        }
    }

    public static bool CanInteractNow(WoWObject subject, Func<bool> current)
        => CanInteractNowCore(subject, current, false);

    public static bool CanInteractDirectlyNow(WoWGameObject subject, Func<bool> current)
        => CanInteractNowCore(subject, current, true);

    private static bool CanInteractNowCore(WoWObject subject, Func<bool> current, bool directUse)
    {
        if (subject is not WoWGameObject target) return true;
        try
        {
            var actor = ObjectManager.Me;
            if (actor == null || !current()) return false;
            ulong actorGuid = actor.Guid, objectGuid = target.Guid;
            uint map = actor.MapId;
            WoWPoint position = actor.Location, destination = target.Location;
            bool Ready() => current() && ReferenceEquals(ObjectManager.Me, actor) && actor.Guid == actorGuid && actor.MapId == map
                && actor.IsValid && actor.IsAlive && !actor.IsMoving && !actor.IsFlying && !actor.IsCasting
                && actor.ChanneledCastingSpellId == 0 && !HasMountOrFlightForm(actor)
                && actor.TryGetMovementState(out uint flags, out ulong transport) && transport == 0
                && (flags & 0x02003000u) == 0 && !actor.MovementInfo.IsDescending
                && target.IsValid && !target.IsDisabled && objectGuid != 0 && target.Guid == objectGuid
                && !Blacklist.Contains(objectGuid) && (directUse || target.CanLoot)
                && float.IsFinite(target.InteractRange) && target.InteractRange > 0
                && target.WithinInteractRange && Finite(position) && Finite(destination)
                && actor.Location.Equals(position) && target.Location.Equals(destination) && current();
            if (!Ready()) return false;
            return GroundTransition.CanInteractWith(target, Ready) && Ready()
                && target.CanUse() && Ready() && target.CanUseNow() && Ready();
        }
        catch (Exception error) when (error is not OperationCanceledException && error is not ThreadInterruptedException)
        { RecoveryActions.RethrowControlFlow(error); return false; }
    }

    public static void ObserveInteraction(WoWObject subject, string phase, string result)
    {
        var previous = LastObservation;
        var actor = ObjectManager.Me;
        if (subject is not WoWGameObject || previous == null || actor == null
            || previous.PlayerGuid != actor.Guid || previous.MapId != actor.MapId
            || previous.ObjectGuid != subject.Guid || previous.ObjectEntry != subject.Entry
            || !ReferenceEquals(BotPoi.Current.AsObject, subject)) return;
        Volatile.Write(ref _lastObservation, previous with { ObservedUtc = DateTime.UtcNow, Phase = phase,
            Node = "LevelBot.CreateOwnedLootInteraction", LastInteraction = result,
            LastInteractionUtc = phase == "interaction-issued" ? DateTime.UtcNow : previous.LastInteractionUtc });
    }

    private static bool HasMountOrFlightForm(LocalPlayer actor) => actor.Mounted
        || actor.Shapeshift == ShapeshiftForm.FlightForm || actor.Shapeshift == ShapeshiftForm.EpicFlightForm;
    private static bool Finite(WoWPoint p) => p != WoWPoint.Zero && float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
    private RunStatus Pending(string phase, string reason, bool retainTransition = false)
    {
        if (!retainTransition) ReleaseTransition();
        Report(phase, reason);
        return RunStatus.Running;
    }
    private RunStatus EndRevoked() { ReleaseTransition(); return RunStatus.Success; }

    private RunStatus Defer(string reason, bool alreadyBlacklisted = false)
    {
        Report("waiting-for-rescan", reason);
        ReleaseTransition();
        if (!Current()) return RunStatus.Success;
        if (!alreadyBlacklisted) Blacklist.Add(_guid, TimeSpan.FromSeconds(15));
        if (_directSubject == null && Current()) BotPoi.Clear("Ground collection bounded recovery: " + reason);
        return RunStatus.Success;
    }

    private void Report(string phase, string reason)
    {
        if (_actor == null) return;
        var position = _actor.Location;
        bool known = _actor.TryGetMovementState(out uint flags, out _);
        var previous = LastObservation;
        bool same = previous != null && previous.PlayerGuid == _actorGuid && previous.ObjectGuid == _guid && previous.MapId == _map;
        Volatile.Write(ref _lastObservation, new Observation(DateTime.UtcNow, _actorGuid, _map, _guid, _entry,
            phase, "GroundLootApproach", reason, new double[] { position.X, position.Y, position.Z },
            new double[] { _destination.X, _destination.Y, _destination.Z }, position.Distance(_destination),
            position.Z - _destination.Z, _subject?.InteractRange ?? 0, _actor.Mounted, _actor.IsFlying,
            known, flags, _navigationResult, _routeRetries, 0,
            Math.Max(0, (DateTime.UtcNow - _startedUtc).TotalSeconds), same ? previous!.LastInteraction : "none",
            same ? previous!.LastInteractionUtc : null));
    }

    private void ReleaseTransition() => _transition.Cancel();

    public override void Stop(object context)
    {
        try { ReleaseTransition(); }
        finally { base.Stop(context); }
    }
}
