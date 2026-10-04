using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Styx.Helpers;
using Styx.Logic.Combat;
using Styx.WoWInternals;
using Styx.WoWInternals.World;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic.Pathing;

public sealed record GroundTransitionDiagnostic(long Owner, long PoiGeneration, string Purpose,
    string Phase, string Reason, ulong ActorGuid, uint Map, WoWPoint ActorPosition,
    ulong TargetGuid, uint TargetEntry, WoWPoint Destination, bool? TargetOutdoors,
    bool? Mounted, bool? Flying, uint? MovementFlags, WoWPoint FlightWaypoint,
    WoWPoint? Landing, string? MeshStatus, bool? MeshComplete, WoWPoint? MeshEndpoint,
    int MeshPoints, string[] MeshAreas, int CandidateAttempts, double ElapsedSeconds,
    bool? ActorOutdoors, bool? Supported, bool? Descending, bool? InteractionReady,
    bool? OnTransport, bool? Immobilized, double? Displacement, double? SecondsWithoutDisplacement,
    int FlightRequestId, int? MeshRequestId, IReadOnlyList<GroundApproachRayObservation> Rays, string? TravelModeReason = null);

internal static class GroundDismountDispatchContext
{
    [ThreadStatic] internal static string? Owner;
}

internal sealed class GroundTransitionRuntime : IGroundTransitionRuntime
{
    private const double PendingDismountSeconds = 12;
    private const double RecoveryCooldownSeconds = 5;
    private sealed record PendingDismountLease(GroundTransitionContext Context, double SubmittedAt);
    private sealed record RecoveryLease(GroundTransitionContext Context, double StartedAt, double Until);
    private sealed record DismountSessionLease(GroundTransitionContext Context, string Owner);
    private static GroundTransitionRuntime? _active;
    private static PendingDismountLease? _pendingDismount;
    private static RecoveryLease? _recovery;
    private static DismountSessionLease? _dismountSession;
    private static long _nextOwner;
    private static GroundTransitionDiagnostic? _lastObservation;
    internal static Func<double>? MonotonicClockOverride { get; set; }
    private static readonly JsonSerializerOptions DiagnosticJson = new()
    {
        Converters = { new PointDiagnosticConverter() }
    };
    public static GroundTransitionDiagnostic? LastObservation => Volatile.Read(ref _lastObservation);

    private readonly GroundTransitionPurpose _purpose;
    private readonly GroundTransitionContext _context;
    private readonly Func<bool>? _routeLease;
    private readonly MeshNavigator? _mesh;
    private readonly long _owner = Interlocked.Increment(ref _nextOwner);
    private readonly double _started;
    private object _flightToken;
    private object? _meshToken;
    private GroundApproachSearch? _search;
    private GroundApproachQueries? _queries;
    private bool _held, _cancelled, _groundTravelSelected;
    private bool _flightDepartureBlocked;
    private double _flightDepartureStarted = double.NaN;
    private WoWPoint _groundReviewPosition;
    private double _nextFlightReview;
    private string _travelModeReason = "not-evaluated";
    private readonly GroundTravelMount _groundMount = new();
    private float _radius, _height;
    private uint? _flags;
    private bool? _outdoors;
    private bool? _actorOutdoors;
    private readonly Queue<GroundApproachRayObservation> _rays = new(32);
    private WoWPoint? _progressPosition;
    private double _progressAt;
    private double? _displacement;
    private string _lastPhase = "";
    private double _lastLog = double.NegativeInfinity;

    static GroundTransitionRuntime()
    {
        BotEvents.OnBotStop += _ =>
        {
            _active?.Cancel();
            _pendingDismount = null;
            _recovery = null;
            _dismountSession = null;
        };
    }

    internal GroundTransitionRuntime(GroundTransitionPurpose purpose, WoWPoint destination, WoWObject? subject, Func<bool> admitted)
    {
        _purpose = purpose;
        // The exact selected NPC remains one journey while its position changes.
        // Landing plans retain their separately observed geometry; neither this
        // route lease nor a new coordinate acknowledges arrival or interaction.
        _context = new GroundTransitionContext(subject, destination, purpose == GroundTransitionPurpose.Interaction && subject is not WoWUnit,
            admitted, purpose == GroundTransitionPurpose.Combat, purpose == GroundTransitionPurpose.Transit, journeyRoute: true);
        // The route invalidators ask whether semantic work survives a
        // coordinate-only POI refresh. Do not use Runtime.Current here: that
        // also depends on the very route tokens the invalidators are deciding.
        _routeLease = () => _context.Current;
        _mesh = _context.Provider as MeshNavigator;
        _flightToken = Flightor.RequestIdentity;
        _meshToken = _mesh?.RequestIdentity;
        _started = Now;
        // Finish only the old owned input before publishing this successor.
        _active?.Cancel();
        if (_active != null || !_context.Current) throw Unknown("owner changed while releasing previous transition");
        _flightToken = Flightor.RequestIdentity; _meshToken = _mesh?.RequestIdentity;
        _active = this;
    }

    public bool Current => !_cancelled && OwnsInput && _context.Current;
    private bool OwnsInput => ReferenceEquals(_active, this) && _context.InputCurrent
        && ReferenceEquals(Flightor.RequestIdentity, _flightToken)
        && (_mesh == null || ReferenceEquals(_mesh.RequestIdentity, _meshToken));
    public double Now => MonotonicClockOverride?.Invoke() ?? (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    public bool SearchExhausted => _flightDepartureBlocked || _search?.Exhausted == true;
    internal bool Matches(WoWPoint destination, WoWObject? subject)
    {
        if (!Current || !ReferenceEquals(_context.Subject, subject)) return false;
        if (_purpose == GroundTransitionPurpose.Combat) return true;
        if (_purpose == GroundTransitionPurpose.Transit)
        {
            if (destination.Equals(_context.Destination)) return true;
            if (subject != null || !GroundApproachSearch.Finite(destination)
                || !_context.Actor.TryGetMovementState(out uint flags, out ulong transport)
                || transport != 0 || (flags & 0x02003000u) != 0 || !Current) return false;
            // Retire the old endpoint's mesh predicate before changing it, but
            // do not stop input or discard this journey's pending mount owner.
            if (_mesh != null && !_mesh.ReleaseOwned(_meshToken!, () => Current, token => _meshToken = token)) return false;
            return Current && _context.RefreshTransitDestination(destination);
        }
        if (destination.Equals(_context.Destination)) return true;
        // A live NPC remains the same work while walking its patrol. Re-target
        // the owned mesh leg, rather than cancel/stop/recreate on every position
        // update. Flight landing plans retain their geometry until ground handoff.
        if (subject is WoWUnit && GroundApproachSearch.Finite(destination))
        {
            // Retire the old endpoint's predicate without stopping its already
            // owned movement. The next mesh request reads the new destination;
            // an active flight/landing plan remains a fixed geometry observation.
            if (_mesh != null && !_mesh.ReleaseOwned(_meshToken!, () => Current, token => _meshToken = token)) return false;
            return Current && _context.RefreshGroundDestination(destination);
        }
        return false;
    }

    internal void SetTransitDistanceEstimate(double? distance)
    {
        RequireCurrent();
        _context.RemainingGroundTravelDistance = _purpose == GroundTransitionPurpose.Transit
            && distance.HasValue && double.IsFinite(distance.Value) && distance.Value >= 0 ? distance : null;
    }

    private void RequireCurrent() { if (!Current) throw Unknown("transition owner changed"); }

    public GroundMotion Observe()
    {
        RequireCurrent();
        var actor = _context.Actor;
        if (!actor.TryGetMovementState(out uint flags, out ulong transport)) throw Unknown("movement flags/transport observation unavailable");
        RequireCurrent();
        var groundState = WorldQueryObservation.ReadGroundUnitState(actor);
        RequireCurrent();
        _flags = flags;
        WoWPoint position = actor.Location;
        bool mounted = groundState.Mounted;
        if (!mounted) ObserveUnmounted(_context);
        bool flying = (flags & 0x02000000u) != 0;
        bool falling = (flags & 0x00003000u) != 0;
        bool swimming = actor.IsSwimming;
        bool onTransport = transport != 0 || groundState.OnTaxi || WorldQueryObservation.ReadLocalVehicle(actor);
        bool immobile = groundState.Rooted || groundState.Stunned;
        _radius = actor.BoundingRadius;
        _height = actor.BoundingHeight;
        if (!float.IsFinite(_radius) || _radius <= 0 || !float.IsFinite(_height) || _height <= 0)
            throw Unknown("actor body geometry unavailable");
        // These optional queries are not inputs to an already-selected foot
        // route. Repeating them can park walking behind unused client work.
        _outdoors = _groundTravelSelected ? null : _context.Subject?.IsOutdoors;
        RequireCurrent();
        bool preferFlight = ReviewFlight(position, flying, onTransport, swimming, immobile);
        RequireCurrent();
        // Sample after optional Lua/travel observations. Sampling before them
        // makes ordinary forward motion stale the footprint on every tick.
        position = actor.Location;
        bool supported = !onTransport && !swimming && SupportedAt(position);
        RequireCurrent();
        WoWPoint interactionPosition = actor.Location;
        bool interactionApproachReady = _purpose == GroundTransitionPurpose.Interaction && !mounted && !flying && !falling
            && (_context.Subject != null
                ? GroundTransition.CanPrepareInteraction(_context.Subject, () => Current)
                : interactionPosition.Distance2DSqr(_context.Destination) <= 2.25f && Math.Abs(interactionPosition.Z - _context.Destination.Z) <= .9f);
        RequireCurrent();
        if (!actor.TryGetMovementState(out uint finalFlags, out ulong finalTransport)
            || ((finalFlags ^ flags) & 0x02003000u) != 0 || finalTransport != transport
            || actor.IsSwimming != swimming || !WorldQueryObservation.ReadGroundUnitState(actor).Equals(groundState))
            throw Unknown("ground movement or mount state changed during observation");
        RequireCurrent();
        WoWPoint currentPosition = actor.Location;
        if (!GroundApproachSearch.Finite(currentPosition)) throw Unknown("actor position became unavailable during observation");
        if (currentPosition.DistanceSqr(position) > .25f) supported = false;
        bool interactionReady = interactionApproachReady && !actor.IsMoving;
        position = currentPosition;
        _displacement = _progressPosition?.Distance(position);
        if (_progressPosition == null || _displacement >= .5)
        {
            _progressPosition = position; _progressAt = Now;
        }
        bool groundTravel = _purpose == GroundTransitionPurpose.Transit && !preferFlight
            || _purpose == GroundTransitionPurpose.Interaction && position.Distance(_context.Destination) > 12
                && (_groundTravelSelected || !preferFlight);
        return new GroundMotion(position, mounted, flying, falling, swimming, onTransport, immobile, supported,
            actor.MovementInfo.IsDescending, interactionReady, preferFlight, groundTravel, interactionApproachReady);
    }

    private bool ReviewFlight(WoWPoint position, bool flying, bool onTransport, bool swimming, bool immobile)
    {
        var actor = _context.Actor;
        if (_purpose == GroundTransitionPurpose.Combat || flying || position.Distance(_context.Destination) <= 60
            || onTransport || swimming || immobile || actor.Combat
            || _groundTravelSelected && _search?.Plan is { ProgressOnly: false })
            return false;
        bool reviewingGround = _groundTravelSelected;
        double now = Now;
        float reviewDistance = _flightDepartureBlocked ? 4 : 16;
        if (reviewingGround && (now < _nextFlightReview || position.Distance2DSqr(_groundReviewPosition) < reviewDistance * reviewDistance))
            return false;
        try
        {
            bool outdoors = actor.IsOutdoors;
            // A blocked takeoff is still the same preferred flight. Retain its
            // foot departure briefly while seeking open ground; do not insert a
            // different mount between the departure and its flight.
            // Mount acknowledgement changes RunSpeed and the zero-cast-cost
            // estimate. Neither observation cancels a flight already selected
            // and admitted by this journey's geometry. Capability is still
            // checked by PreferFlightForGroundInteraction on every departure.
            bool retainedDeparture = !_groundTravelSelected && _search?.Plan != null
                || RetainsPreferredDeparture(now);
            bool eligible = outdoors && Flightor.PreferFlightForGroundInteraction(_context.Destination, 3f, retainedDeparture);
            RequireCurrent();
            _actorOutdoors = outdoors;
            _travelModeReason = eligible ? "eligible-flight-awaiting-local-geometry"
                : outdoors ? "flight-unavailable-or-uneconomical" : "indoor-ground-departure";
            if (!eligible && !retainedDeparture) { _flightDepartureBlocked = false; _flightDepartureStarted = double.NaN; }
            if (reviewingGround)
            {
                _groundReviewPosition = position; _nextFlightReview = now + (_flightDepartureBlocked ? 1 : 3);
                if (eligible)
                {
                    _groundTravelSelected = false;
                    // The old obstruction belongs to the previous footprint.
                    // Await a stopped observation before testing this new one.
                    _flightDepartureBlocked = false;
                }
            }
            return eligible;
        }
        catch (ObservationUnavailableException) when (Current)
        {
            // An optional mode review cannot park an already-authorized ground
            // journey. UNKNOWN does not authorize a new flight or mount action.
            _groundReviewPosition = position; _nextFlightReview = now + 3;
            _travelModeReason = "optional-flight-review-unknown; ground-owner-retained";
            return false;
        }
    }

    private bool SupportedAt(WoWPoint position)
    {
        RequireCurrent();
        if (!GroundApproachSearch.Finite(position)) throw Unknown("actor position unavailable");
        var line = new WorldLine(position.Add(0, 0, .75f), position.Add(0, 0, -1.25f));
        var flags = new[] { GameWorld.CGWorldFrameHitFlags.HitTestGroundAndStructures,
            GameWorld.CGWorldFrameHitFlags.HitTestLiquid | GameWorld.CGWorldFrameHitFlags.HitTestLiquid2 };
        try
        {
            GameWorld.MassTraceLine(new[] { line, line }, flags, out bool[] hits, out WoWPoint[] points);
            RequireCurrent();
            if (hits == null || points == null || hits.Length != 2 || points.Length != 2)
                throw Unknown("ground support collision batch was incomplete");
            for (int i = 0; i < 2; i++)
                RecordRay(new("support", line.Start, line.End, (uint)flags[i], hits[i], hits[i] ? points[i] : null));
            WoWPoint support = points[0];
            return GroundApproachSearch.DrySupport(line, new GroundRay(hits[0], support), new GroundRay(hits[1], points[1]))
                && GroundApproachSearch.Finite(support) && support.Distance2DSqr(position) <= .01f
                && Math.Abs(support.Z - position.Z) <= .7f;
        }
        catch (ObservationUnavailableException)
        {
            foreach (var flag in flags) RecordRay(new("support", line.Start, line.End, (uint)flag, null, null));
            throw;
        }
    }

    private void RecordRay(GroundApproachRayObservation observation)
    {
        if (!Current) return;
        if (_rays.Count == 32) _rays.Dequeue();
        _rays.Enqueue(observation);
    }

    private bool Trace(string source, WoWPoint from, WoWPoint to, GameWorld.CGWorldFrameHitFlags flags, out WoWPoint point)
    {
        RequireCurrent();
        try
        {
            bool hit = GameWorld.TraceLine(from, to, flags, out point);
            RequireCurrent();
            RecordRay(new(source, from, to, (uint)flags, hit, hit ? point : null));
            return hit;
        }
        catch (ObservationUnavailableException)
        {
            RecordRay(new(source, from, to, (uint)flags, null, null));
            throw;
        }
    }

    public GroundApproachPlan? Search()
    {
        RequireCurrent();
        if (_mesh == null) throw Unknown("mesh provider unavailable for landing/onward proof");
        _queries ??= new GroundApproachQueries(_mesh, _context.Map, () => Current) { TraceObserved = RecordRay };
        if (_search == null)
        {
            WoWPoint position = _context.Actor.Location;
            if (_purpose != GroundTransitionPurpose.Combat && (_flags.GetValueOrDefault() & 0x02000000u) == 0)
            {
                // Hold() precedes Search(). Wait for that stop observation before
                // using the actor's current footprint as takeoff permission.
                if (_context.Actor.IsMoving) return null;
                var offsets = new[] { (0f, 0f), (_radius, 0f), (-_radius, 0f), (0f, _radius), (0f, -_radius) };
                var columns = offsets.Select(offset => new WorldLine(position.Add(offset.Item1, offset.Item2, .25f),
                    position.Add(offset.Item1, offset.Item2, Math.Max(40, _height + 1)))).ToArray();
                var overhead = _queries.Trace(columns, GameWorld.CGWorldFrameHitFlags.HitTestGroundAndStructures);
                RequireCurrent();
                if (!_context.Actor.Location.Equals(position)) throw Unknown("actor moved during takeoff column observation");
                _flightDepartureBlocked = overhead.Any(ray => ray.Hit);
                if (_flightDepartureBlocked)
                {
                    if (double.IsNaN(_flightDepartureStarted)) _flightDepartureStarted = Now;
                    _travelModeReason = "takeoff-column-blocked; walk-to-open-ground";
                    return null;
                }
            }
            else _flightDepartureBlocked = false;
            _actorOutdoors = _context.Actor.IsOutdoors;
            bool covered = _actorOutdoors == false;
            RequireCurrent();
            var gameObject = _context.Subject as WoWGameObject;
            float objectRange = gameObject?.InteractRange ?? 0;
            if (gameObject != null && (!float.IsFinite(objectRange) || objectRange <= 0))
                throw Unknown("selected object's interaction range unavailable");
            _search = new GroundApproachSearch(position, _context.Destination, _radius, _height,
                _purpose != GroundTransitionPurpose.Combat, covered, _queries,
                () => Current && (gameObject == null || gameObject.InteractRange == objectRange), objectRange,
                _purpose != GroundTransitionPurpose.Combat && _context.Actor.IsFlying ? ReadAirPosition : null);
        }
        return _search.Step();
    }
    private GroundApproachSearch? _nextFlightSearch;

    public GroundApproachPlan? PrepareNextFlightLeg(GroundApproachPlan active)
    {
        RequireCurrent();
        if (!active.ProgressOnly || _queries == null || !_context.Actor.IsFlying) return null;
        if (_nextFlightSearch == null)
        {
            var gameObject = _context.Subject as WoWGameObject;
            float range = gameObject?.InteractRange ?? 0;
            if (gameObject != null && (!float.IsFinite(range) || range <= 0))
                throw Unknown("selected object's interaction range unavailable");
            _nextFlightSearch = new GroundApproachSearch(_context.Actor.Location, _context.Destination,
                _radius, _height, true, false, _queries,
                () => Current && (gameObject == null || gameObject.InteractRange == range), range, ReadAirPosition);
        }
        try
        {
            var candidate = _nextFlightSearch.Step();
            RequireCurrent();
            if (candidate == null || !_nextFlightSearch.Revalidate(candidate)) return null;
            RequireCurrent();
            _search = _nextFlightSearch; _nextFlightSearch = null;
            return candidate;
        }
        catch (ObservationUnavailableException) when (Current)
        {
            // The current leg was independently revalidated before lookahead.
            // An unavailable future query cannot authorize a successor, but it
            // need not revoke movement that is still bounded by that old leg.
            _travelModeReason = "future-flight-region-unobserved; current-endpoint-retained";
            return null;
        }
    }

    public bool Validate(GroundApproachPlan plan) { RequireCurrent(); return _search?.Revalidate(plan) == true && Current; }

    private WoWPoint ReadAirPosition()
    {
        RequireCurrent();
        var actor = _context.Actor;
        if (!actor.TryGetMovementState(out uint flags, out ulong transport) || transport != 0
            || (flags & 0x02000000u) == 0 || (flags & 0x00003000u) != 0 || actor.IsSwimming
            || actor.OnTaxi || actor.IsOnTransport || !WorldQueryObservation.ReadGroundUnitState(actor).Mounted)
            throw Unknown("air-corridor owner is not an observed flying mounted actor");
        var position = actor.Location;
        RequireCurrent();
        return position;
    }
    public void ResetSearch() { RequireCurrent(); _search = null; _nextFlightSearch = null; _groundTravelSelected = false; }

    public void Hold()
    {
        if (_held || !OwnsInput) return;
        if (!Flightor.ReleaseOwned(_flightToken, () => OwnsInput, owner => _flightToken = owner)) return;
        if (!OwnsInput) return;
        if (_mesh != null && !_mesh.ReleaseOwned(_meshToken!, () => OwnsInput, owner => _meshToken = owner)) return;
        if (!OwnsInput) return;
        _context.Input.MoveStop();
        if (OwnsInput) _held = true;
    }

    public void Fly(GroundApproachPlan plan)
    {
        RequireCurrent();
        if (!Validate(plan)) throw Unknown("exterior approach observation changed");
        var actor = _context.Actor;
        if (!actor.IsFlying && WorldQueryObservation.ReadGroundUnitState(actor).Mounted
            && !Flightor.MountHelper.Mounted)
        {
            RequireCurrent();
            if (_purpose == GroundTransitionPurpose.Combat || actor.Combat) return;
            // A ground mount cannot acknowledge preparation of the selected
            // flying mount. Reuse the observed-support and actor/session lease
            // for one removal, then await a later unmounted observation. This
            // optional upgrade never authorizes dismounting for incidental aggro.
            Hold();
            RequireCurrent();
            GroundDismountState state = DismountCore(() => !actor.Combat);
            _travelModeReason = "ground-to-flight-upgrade; unmount-unobserved";
            if (state is GroundDismountState.Expired or GroundDismountState.Rejected)
            {
                _groundReviewPosition = actor.Location; _nextFlightReview = Now + 3;
                _groundTravelSelected = true;
                _travelModeReason = "flight-upgrade-deferred; ground-owner-retained";
                Walk();
            }
            return;
        }
        RequireCurrent();
        _held = false;
        Flightor.MoveToOwnedExterior(plan.AirWaypoint, () => Current, token => _flightToken = token, _routeLease);
        RequireCurrent();
    }

    public void Descend(GroundApproachPlan plan)
    {
        RequireCurrent();
        var actor = _context.Actor;
        var position = actor.Location;
        if (plan.ProgressOnly || plan.AirOrigin.HasValue || !Validate(plan)
            || position.Distance2DSqr(plan.Landing) > .5625f || position.Z < plan.Landing.Z)
            throw Unknown("descent is no longer above the validated landing footprint");
        var offsets = new[] { (0f, 0f), (_radius, 0f), (-_radius, 0f), (0f, _radius), (0f, -_radius) };
        var lines = offsets.Select(offset => new WorldLine(position.Add(offset.Item1, offset.Item2, .35f),
            plan.Landing.Add(offset.Item1, offset.Item2, .35f))).Where(line => line.Start.DistanceSqr(line.End) > .01f).ToArray();
        if (lines.Length != 0)
        {
            const GameWorld.CGWorldFrameHitFlags flags = GameWorld.CGWorldFrameHitFlags.HitTestGroundAndStructures;
            bool[] hits;
            try
            {
                GameWorld.MassTraceLine(lines, Enumerable.Repeat(flags, lines.Length).ToArray(), out hits, out var points);
                RequireCurrent();
                if (hits == null || points == null || hits.Length != lines.Length || points.Length != lines.Length)
                    throw Unknown("descent collision batch was incomplete");
                for (int i = 0; i < lines.Length; i++)
                    RecordRay(new("descent", lines[i].Start, lines[i].End, (uint)flags, hits[i], hits[i] ? points[i] : null));
            }
            catch (ObservationUnavailableException)
            {
                foreach (var line in lines) RecordRay(new("descent", line.Start, line.End, (uint)flags, null, null));
                throw;
            }
            if (hits.Any(hit => hit)) throw Unknown("descent footprint acquired an obstruction");
        }
        RequireCurrent();
        WoWPoint currentPosition = actor.Location;
        if (currentPosition.Distance2DSqr(position) > .01f || currentPosition.Z > position.Z + .25f
            || currentPosition.Z < plan.Landing.Z || currentPosition.Distance2DSqr(plan.Landing) > .5625f)
            throw Unknown("actor left the observed descent corridor");
        _held = false;
        WoWMovement.Move(WoWMovement.MovementDirection.Descend);
        RequireCurrent();
    }

    public GroundDismountState Dismount() => DismountCore(null);

    private GroundDismountState DismountCore(Func<bool>? additionalAdmission)
    {
        RequireCurrent();
        if (additionalAdmission?.Invoke() == false) return GroundDismountState.Rejected;
        double now = Now;
        if (!double.IsFinite(now)) throw Unknown("dismount monotonic clock unavailable");
        var pending = _pendingDismount;
        if (pending != null && pending.Context.SameActorSession(_context))
        {
            if (now < pending.SubmittedAt) throw Unknown("dismount monotonic clock moved backward");
            if (now - pending.SubmittedAt < PendingDismountSeconds) return GroundDismountState.Pending;
            _pendingDismount = null;
            return GroundDismountState.Expired;
        }

        var actor = _context.Actor;
        WoWPoint position = actor.Location;
        if (!SupportedAt(position)) throw Unknown("mount removal lacks current positive support");
        RequireCurrent();
        if (!actor.Location.Equals(position)) throw Unknown("actor moved after mount-removal support observation");
        var leaseContext = _context;
        string owner = DismountSessionOwner();
        string? previousOwner = GroundDismountDispatchContext.Owner;
        bool submitted;
        GroundDismountDispatchContext.Owner = owner;
        try
        {
            // The native entry must still refer to the footprint observed above.
            // Checking position is pure; a new collision query here would replace
            // the Lua action being prepared by the shared executor.
            submitted = Mount.TryDismountOwned("Ground transition: " + _purpose,
                () => Current && actor.Location.Equals(position) && additionalAdmission?.Invoke() != false && Current,
                () => _pendingDismount = new PendingDismountLease(leaseContext, now));
        }
        finally { GroundDismountDispatchContext.Owner = previousOwner; }
        if (!submitted && _pendingDismount is { } rejected
            && ReferenceEquals(rejected.Context, leaseContext) && rejected.SubmittedAt == now)
            _pendingDismount = null;
        return submitted ? GroundDismountState.Submitted : GroundDismountState.Rejected;
    }

    private string DismountSessionOwner()
    {
        var session = _dismountSession;
        if (session != null && session.Context.SameActorSession(_context)) return session.Owner;
        string owner = Guid.NewGuid().ToString("N");
        _dismountSession = new DismountSessionLease(_context, owner);
        return owner;
    }

    internal static void ObserveUnmounted(GroundTransitionContext current)
    {
        if (!current.Current) return;
        if (_pendingDismount?.Context.SameActorSession(current) == true) _pendingDismount = null;
        if (_recovery?.Context.SameActorSession(current) == true) _recovery = null;
        if (_dismountSession?.Context.SameActorSession(current) == true) _dismountSession = null;
    }

    public bool RecoveryDeferred(double now)
    {
        RequireCurrent();
        if (!double.IsFinite(now)) throw Unknown("recovery monotonic clock unavailable");
        var lease = _recovery;
        if (lease == null || !lease.Context.SameActorSession(_context)) return false;
        if (now < lease.StartedAt) throw Unknown("recovery monotonic clock moved backward");
        if (now < lease.Until) return true;
        _recovery = null;
        return false;
    }

    public void DeferRecovery(double now)
    {
        RequireCurrent();
        if (!double.IsFinite(now)) throw Unknown("recovery monotonic clock unavailable");
        var lease = _recovery;
        if (lease != null && lease.Context.SameActorSession(_context))
        {
            if (now < lease.StartedAt) throw Unknown("recovery monotonic clock moved backward");
            if (now < lease.Until) return;
        }
        _recovery = new RecoveryLease(_context, now, now + RecoveryCooldownSeconds);
    }

    private bool RetainsPreferredDeparture(double now) => !double.IsNaN(_flightDepartureStarted)
        && now >= _flightDepartureStarted && now - _flightDepartureStarted < 15;

    public void Walk()
    {
        RequireCurrent();
        if (_mesh == null) throw Unknown("ground navigator unavailable");
        if (!_groundTravelSelected)
        {
            _groundReviewPosition = _context.Actor.Location;
            _nextFlightReview = Now + (_flightDepartureBlocked ? 1 : 3);
        }
        _groundTravelSelected = true;
        if (!_context.Actor.IsSwimming && !_flightDepartureBlocked && !RetainsPreferredDeparture(Now) && _purpose != GroundTransitionPurpose.Combat
            && _context.Actor.Location.Distance(_context.Destination) > 12
            && _groundMount.Wait(_context, Now, () => Current, Hold)) return;
        var actor = _context.Actor;
        if (actor.IsCasting || actor.ChanneledCastingSpellId != 0) return;
        if (WorldQueryObservation.ReadLocalVehicle(actor)) throw Unknown("ground route belongs to a vehicle");
        var state = WorldQueryObservation.ReadGroundUnitState(actor);
        bool swimming = actor.IsSwimming;
        WoWPoint travelDestination = _context.Destination;
        // One complete preflight, then pure memory/identity predicates. Calling
        // CanActUnmounted in every route predicate repeated Lua dozens of times
        // and could overwrite a prepared native movement command.
        bool Admitted() => Current && _context.Destination.Equals(travelDestination) && !actor.IsCasting && actor.ChanneledCastingSpellId == 0
            && actor.IsSwimming == swimming && (!swimming || !state.Mounted)
            && (!state.Mounted || _purpose == GroundTransitionPurpose.Transit
                || _purpose == GroundTransitionPurpose.Interaction && actor.Location.Distance(_context.Destination) > 12)
            && actor.TryGetMovementState(out uint flags, out ulong transport) && transport == 0
            && (flags & 0x02003000u) == 0 && !state.OnTaxi && !state.Rooted && !state.Stunned
            && WorldQueryObservation.ReadGroundUnitState(actor).Equals(state) && Current;
        if (!Admitted()) throw Unknown("ground route movement observation changed");
        _held = false;
        _mesh.MoveToOwned(travelDestination, Math.Min(1.5f, _mesh.PathPrecision), "Ground interaction approach",
            Admitted, token => _meshToken = token, _routeLease);
        RequireCurrent();
    }

    public void Report(string phase, string reason, GroundMotion? observation, GroundApproachPlan? plan)
    {
        RequireCurrent();
        if (phase == _lastPhase && Now - _lastLog < 5) return;
        var path = plan?.OnwardPath ?? _search?.LastPath;
        var record = new GroundTransitionDiagnostic(_owner, _context.PoiGeneration, _purpose.ToString(), phase, reason,
            _context.ActorGuid, _context.Map, observation?.Position ?? _context.Actor.Location,
            _context.SubjectGuid, _context.SubjectEntry, _context.Destination, _outdoors, observation?.Mounted, observation?.Flying,
            _flags, Flightor.LastFlightWaypoint, plan?.Landing, path?.Status, path?.Complete,
            path?.Points.Count > 0 ? path.Points[^1] : null, path?.Points.Count ?? 0,
            path?.Areas.Select(area => area.ToString()).Distinct().ToArray() ?? Array.Empty<string>(), _search?.Attempts ?? 0, Now - _started,
            _actorOutdoors, observation?.Supported, observation?.Descending, observation?.InteractionReady,
            observation?.OnTransport, observation?.Immobilized, _displacement, _progressPosition == null ? null : Now - _progressAt,
            RuntimeHelpers.GetHashCode(_flightToken), _meshToken == null ? null : RuntimeHelpers.GetHashCode(_meshToken),
            Array.AsReadOnly(_rays.ToArray()), _travelModeReason);
        RequireCurrent();
        _lastPhase = phase; _lastLog = Now;
        Volatile.Write(ref _lastObservation, record);
        Logging.WriteDiagnostic("[GroundTransition] {0}", JsonSerializer.Serialize(record, DiagnosticJson));
        RequireCurrent();
    }

    private sealed class PointDiagnosticConverter : JsonConverter<WoWPoint>
    {
        public override WoWPoint Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            => throw new NotSupportedException("Ground transition diagnostics are write-only.");

        public override void Write(Utf8JsonWriter writer, WoWPoint point, JsonSerializerOptions options)
        {
            // WoWPoint exposes fields plus computed properties. Serializing it
            // by default loses XYZ and throws on Empty's nonfinite properties.
            // Unavailable geometry stays null; it never becomes a zero point.
            if (!GroundApproachSearch.Finite(point)) { writer.WriteNullValue(); return; }
            writer.WriteStartObject();
            writer.WriteNumber("X", point.X);
            writer.WriteNumber("Y", point.Y);
            writer.WriteNumber("Z", point.Z);
            writer.WriteEndObject();
        }
    }

    internal void Cancel()
    {
        if (_cancelled) return;
        try { Hold(); }
        finally
        {
            _cancelled = true;
            if (ReferenceEquals(_active, this)) _active = null;
        }
    }
    private static ObservationUnavailableException Unknown(string reason) => new("ground-transition", reason);
}
