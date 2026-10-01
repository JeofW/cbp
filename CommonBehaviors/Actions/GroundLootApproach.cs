#nullable enable
using System;
using System.Threading;
using Styx;
using Styx.Logic;
using Styx.Logic.Combat;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.WoWInternals;
using Styx.WoWInternals.World;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

namespace CommonBehaviors.Actions;

/// <summary>
/// Owns only the ground approach to an already selected lootable GameObject.
/// Existing loot/frame/slot owners perform the interaction and collection.
/// Failure means "not a ground object" or "ready for the next owner"; handled
/// travel is never quest progress. Every pending state has a bounded deadline.
/// </summary>
public sealed class GroundLootApproach : TreeSharp.Action
{
    private readonly Func<bool> _admitted;
    private readonly Func<WoWGameObject?>? _directSubject;
    private LocalPlayer? _actor;
    private WoWUnit? _mover;
    private WoWGameObject? _subject;
    private BotPoi? _poi;
    private object? _provider;
    private ulong _actorGuid, _moverGuid, _guid;
    private uint _map, _entry;
    private PoiType _type;
    private WoWPoint _destination;
    private DateTime _startedUtc, _lastProgressUtc, _lastDismountUtc, _lastMoveUtc;
    private double _bestDistance = double.PositiveInfinity;
    private int _dismountAttempts, _routeRetries;
    private bool _ownsDescent;
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
        if (!ReferenceEquals(poi, _poi) || _directSubject != null && !ReferenceEquals(direct, _subject))
        {
            ReleaseDescent();
            _poi = poi; _actor = ObjectManager.Me; _mover = WoWMovement.ActiveMover;
            _subject = _directSubject == null ? poi?.AsObject as WoWGameObject : direct;
            _provider = Navigator.NavigationProvider;
            _actorGuid = _actor?.Guid ?? 0; _moverGuid = _mover?.Guid ?? 0;
            _guid = _directSubject == null ? poi?.Guid ?? 0 : direct?.Guid ?? 0;
            _entry = _directSubject == null ? poi?.Entry ?? 0 : direct?.Entry ?? 0;
            _type = poi?.Type ?? PoiType.None;
            _map = _actor?.MapId ?? 0; _destination = _subject?.Location ?? WoWPoint.Empty;
            _startedUtc = _lastProgressUtc = DateTime.UtcNow; _lastDismountUtc = _lastMoveUtc = DateTime.MinValue;
            _dismountAttempts = _routeRetries = 0; _bestDistance = double.PositiveInfinity;
            _navigationResult = "not-dispatched";
        }
        base.Start(context);
    }

    private bool Current() => _actor != null && _actorGuid != 0 && _actor.IsValid && _actor.IsAlive
        && ReferenceEquals(ObjectManager.Me, _actor) && _actor.Guid == _actorGuid && _actor.MapId == _map
        && _mover != null && _moverGuid == _actorGuid && _mover.IsValid && _mover.Guid == _moverGuid
        && ReferenceEquals(WoWMovement.ActiveMover, _mover) && ReferenceEquals(_mover, _actor)
        && ReferenceEquals(Navigator.NavigationProvider, _provider)
        && _poi != null && ReferenceEquals(BotPoi.Current, _poi) && _poi.Type == _type
        && (_directSubject == null ? _poi.Guid == _guid && _poi.Entry == _entry
            && (_type == PoiType.Loot || _type == PoiType.Harvest) && ReferenceEquals(_poi.AsObject, _subject)
            : (_type == PoiType.None || _type == PoiType.Hotspot || _type == PoiType.Quest)
                && ReferenceEquals(_directSubject(), _subject))
        && _subject != null && _guid != 0 && _entry != 0
        && _subject.IsValid && !_subject.IsDisabled && _subject.Guid == _guid && _subject.Entry == _entry
        && _subject.Location.Equals(_destination) && _admitted()
        && ReferenceEquals(ObjectManager.Me, _actor) && _actor.Guid == _actorGuid;

    protected override RunStatus Run(object context)
    {
        if (_subject == null || _directSubject == null && _type != PoiType.Loot && _type != PoiType.Harvest)
            return RunStatus.Failure;
        try
        {
            if (!Current()) { ReleaseDescent(); return RunStatus.Success; }
            var actor = _actor!;
            var target = _subject!;
            DateTime now = DateTime.UtcNow;
            if ((now - _startedUtc).TotalSeconds >= 45) return Defer("ground-approach-deadline");
            if (!Finite(actor.Location) || !Finite(_destination)) return Pending("cannot-approach", "invalid-or-unknown-coordinates");
            double distance = actor.Location.Distance(_destination);
            if (distance + 0.25 < _bestDistance)
            {
                _bestDistance = distance; _lastProgressUtc = now;
            }
            if ((now - _lastProgressUtc).TotalSeconds >= 15)
            {
                if (_routeRetries == 0 && !actor.IsFlying)
                {
                    ReleaseDescent();
                    if (!Current()) return RunStatus.Success;
                    Navigator.Clear();
                    if (!Current()) return RunStatus.Success;
                    _routeRetries++; _lastProgressUtc = now;
                    return Pending("repositioning", "one-bounded-route-recomputation");
                }
                return Defer("no-approach-progress");
            }
            if (Blacklist.Contains(_guid)) return Defer("selected-object-is-blacklisted", alreadyBlacklisted: true);
            if (_directSubject == null && !target.CanLoot) return Defer("object-not-currently-lootable-or-consumed");
            if (!Current()) return RunStatus.Success;
            if (!actor.TryGetMovementState(out uint flags, out ulong transport) || transport != 0)
                return Pending("cannot-approach", "movement-or-transport-observation-unavailable");
            // Same original-client movement boundary used by Mount.Dismount;
            // the unrelated legacy MoveFlags aliases are not substituted here.
            if ((flags & 0x3000u) != 0)
                return Pending("landing", "falling-is-not-grounded");
            float range = target.InteractRange;
            if (!float.IsFinite(range) || range <= 0)
                return Pending("cannot-interact", "interaction-range-unavailable");
            if (!Current()) return RunStatus.Success;

            if (actor.IsFlying || (flags & 0x02000000u) != 0)
            {
                double horizontal = actor.Location.Distance2D(_destination);
                double dz = actor.Location.Z - _destination.Z;
                if (horizontal > Math.Min(2.0, range / 2.0) || dz < -3 || dz > 60)
                {
                    ReleaseDescent();
                    if (!Current()) return RunStatus.Success;
                    if ((now - _lastMoveUtc).TotalMilliseconds < 250)
                        return Pending("travelling", "awaiting-next-bounded-flight-request");
                    _lastMoveUtc = now;
                    _navigationResult = "Flightor-dispatched-not-arrival";
                    Flightor.MoveTo(_destination);
                    return Current() ? Pending("travelling", "approaching-live-object-before-vertical-landing") : RunStatus.Success;
                }
                var from = actor.Location.Add(0, 0, 1);
                var to = new WoWPoint(from.X, from.Y, _destination.Z - 4);
                bool hit = GameWorld.TraceLine(from, to, GameWorld.CGWorldFrameHitFlags.HitTestGroundAndStructures, out WoWPoint support);
                if (!Current()) { ReleaseDescent(); return RunStatus.Success; }
                // A fail-closed trace/error can return true with no valid hit.
                // Require a point on this ray and near the live object's floor;
                // a ceiling or a different floor is not a safe landing receipt.
                if (!hit || !Finite(support) || Math.Abs(support.X - from.X) > 0.5
                    || Math.Abs(support.Y - from.Y) > 0.5 || support.Z > from.Z || support.Z < to.Z
                    || Math.Abs(support.Z - _destination.Z) > 3)
                    return Pending("cannot-approach", "ground-support-unavailable-or-wrong-floor");
                if (!GameWorld.IsInLineOfSight(from, _destination.Add(0, 0, 1)))
                    return Pending("cannot-approach", "landing-line-of-sight-blocked");
                if (!Current()) { ReleaseDescent(); return RunStatus.Success; }
                if (!_ownsDescent)
                {
                    // Stop the earlier flight/CTM command before owning descent.
                    WoWMovement.MoveStop();
                    if (!Current()) return RunStatus.Success;
                    _ownsDescent = true;
                    WoWMovement.Move(WoWMovement.MovementDirection.Descend);
                }
                return Current() ? Pending("landing", "awaiting-grounded-movement-observation", retainDescent: true) : EndRevoked();
            }

            ReleaseDescent();
            if (!Current()) return RunStatus.Success;
            if (actor.MovementInfo.IsDescending)
                return Pending("landing", "awaiting-descent-stop-acknowledgement");
            if (HasMountOrFlightForm(actor))
            {
                if ((now - _lastDismountUtc).TotalSeconds >= 2)
                {
                    if (_dismountAttempts >= 2) return Defer("dismount-not-acknowledged");
                    if (!Current()) return RunStatus.Success;
                    _lastDismountUtc = now; _dismountAttempts++;
                    Mount.Dismount("Ground loot approach; confirmed grounded movement");
                }
                return Current() ? Pending("dismounting", "awaiting-unmounted-or-normal-form-observation") : RunStatus.Success;
            }
            bool sight = GameWorld.IsInLineOfSight(actor.Location.Add(0, 0, 1), _destination.Add(0, 0, 1));
            if (!Current()) return RunStatus.Success;
            if (!target.WithinInteractRange || !sight)
            {
                // Stay on the ground during final approach; Flightor may otherwise
                // remount/take off before a nearby object has been interacted with.
                if (!Current()) return RunStatus.Success;
                if ((now - _lastMoveUtc).TotalMilliseconds < 250)
                    return Pending("approaching", "awaiting-next-bounded-ground-request");
                _lastMoveUtc = now;
                var result = Navigator.MoveTo(_destination);
                _navigationResult = "ground:" + result;
                return Current() ? Pending("approaching", sight ? "outside-live-interaction-range" : "interaction-line-of-sight-blocked") : RunStatus.Success;
            }
            if (actor.IsMoving)
            {
                if (!Current()) return RunStatus.Success;
                WoWMovement.MoveStop();
                return Current() ? Pending("approaching", "awaiting-stationary-observation") : RunStatus.Success;
            }
            Report("ready-to-interact", "grounded-unmounted-in-range-and-visible");
            return Current() ? RunStatus.Failure : RunStatus.Success;
        }
        catch (Exception error) when (error is not OperationCanceledException && error is not ThreadInterruptedException)
        {
            ReleaseDescent();
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
            bool sight = GameWorld.IsInLineOfSight(position.Add(0, 0, 1), destination.Add(0, 0, 1));
            return sight && Ready();
        }
        catch (Exception error) when (error is not OperationCanceledException && error is not ThreadInterruptedException)
        { return false; }
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
    private RunStatus Pending(string phase, string reason, bool retainDescent = false)
    {
        if (!retainDescent) ReleaseDescent();
        Report(phase, reason);
        return RunStatus.Running;
    }
    private RunStatus EndRevoked() { ReleaseDescent(); return RunStatus.Success; }

    private RunStatus Defer(string reason, bool alreadyBlacklisted = false)
    {
        Report("waiting-for-rescan", reason);
        ReleaseDescent();
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
            known, flags, _navigationResult, _routeRetries, _dismountAttempts,
            Math.Max(0, (DateTime.UtcNow - _startedUtc).TotalSeconds), same ? previous!.LastInteraction : "none",
            same ? previous!.LastInteractionUtc : null));
    }

    private void ReleaseDescent()
    {
        if (!_ownsDescent) return;
        _ownsDescent = false;
        // Release only this action's input on the same controlled actor. A map,
        // POI, combat or death change cannot leave that key held by an old tree.
        if (_actor != null && ReferenceEquals(ObjectManager.Me, _actor) && _actor.Guid == _actorGuid
            && ReferenceEquals(WoWMovement.ActiveMover, _mover) && _mover?.Guid == _moverGuid)
            WoWMovement.MoveStop(WoWMovement.MovementDirection.Descend);
    }

    public override void Stop(object context)
    {
        try { ReleaseDescent(); }
        finally { base.Stop(context); }
    }
}
