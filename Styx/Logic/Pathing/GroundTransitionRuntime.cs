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
    int FlightRequestId, int? MeshRequestId, IReadOnlyList<GroundApproachRayObservation> Rays);

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
        // Only a grounded chase can follow a live NPC's moving coordinate.
        // Airborne approach geometry remains bound to the sampled endpoint.
        bool groundChase = subject is WoWUnit && ObjectManager.Me != null
            && ObjectManager.Me.TryGetMovementState(out uint initialFlags, out ulong initialTransport)
            && initialTransport == 0 && (initialFlags & 0x02003000u) == 0;
        _context = new GroundTransitionContext(subject, destination, purpose == GroundTransitionPurpose.Interaction && !groundChase,
            admitted, purpose == GroundTransitionPurpose.Combat);
        // The route invalidators ask whether semantic combat work survives a
        // coordinate-only POI refresh. Do not use Runtime.Current here: that
        // also depends on the very route tokens the invalidators are deciding.
        _routeLease = purpose == GroundTransitionPurpose.Combat ? () => _context.Current : null;
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
    public bool SearchExhausted => _search?.Exhausted == true;
    internal bool Matches(WoWPoint destination, WoWObject? subject)
    {
        if (!Current || !ReferenceEquals(_context.Subject, subject)) return false;
        if (_purpose == GroundTransitionPurpose.Combat) return true;
        if (destination.Equals(_context.Destination)) return true;
        // A live NPC remains the same work while walking its patrol. Re-target
        // the owned mesh leg, rather than cancel/stop/recreate on every position
        // update. Flight landing plans retain their geometry until ground handoff.
        if (subject is WoWUnit)
            return !_groundTravelSelected || _context.RefreshGroundDestination(destination);
        return false;
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
        WoWPoint interactionPosition = actor.Location;
        bool interactionReady = _purpose == GroundTransitionPurpose.Interaction && !mounted && !flying && !falling
            && (_context.Subject != null
                ? GroundTransition.CanInteractWith(_context.Subject, () => Current)
                : interactionPosition.Distance2DSqr(_context.Destination) <= 2.25f && Math.Abs(interactionPosition.Z - _context.Destination.Z) <= .9f);
        bool preferFlight = !_groundTravelSelected && _purpose == GroundTransitionPurpose.Interaction && position.Distance(_context.Destination) > 60
            && !onTransport && !swimming && !immobile && (_actorOutdoors = actor.IsOutdoors) == true && !actor.Combat
            && Flightor.PreferFlightForGroundInteraction(_context.Destination, 3f);
        RequireCurrent();
        // Sample after optional Lua/travel observations. Sampling before them
        // makes ordinary forward motion stale the footprint on every tick.
        position = actor.Location;
        bool supported = !onTransport && !swimming && SupportedAt(position);
        RequireCurrent();
        if (!actor.TryGetMovementState(out uint finalFlags, out ulong finalTransport)
            || ((finalFlags ^ flags) & 0x02003000u) != 0 || finalTransport != transport
            || actor.IsSwimming != swimming || !WorldQueryObservation.ReadGroundUnitState(actor).Equals(groundState))
            throw Unknown("ground movement or mount state changed during observation");
        RequireCurrent();
        WoWPoint currentPosition = actor.Location;
        if (!GroundApproachSearch.Finite(currentPosition)) throw Unknown("actor position became unavailable during observation");
        if (currentPosition.DistanceSqr(position) > .25f) supported = false;
        interactionReady &= currentPosition.Equals(interactionPosition);
        position = currentPosition;
        _displacement = _progressPosition?.Distance(position);
        if (_progressPosition == null || _displacement >= .5)
        {
            _progressPosition = position; _progressAt = Now;
        }
        bool groundTravel = _purpose == GroundTransitionPurpose.Interaction && position.Distance(_context.Destination) > 12
            && (_groundTravelSelected || !preferFlight);
        return new GroundMotion(position, mounted, flying, falling, swimming, onTransport, immobile, supported,
            actor.MovementInfo.IsDescending, interactionReady, preferFlight, groundTravel);
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
            return hits[0] && !hits[1] && GroundApproachSearch.Finite(support) && support.Distance2DSqr(position) <= .01f
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
            _actorOutdoors = _context.Actor.IsOutdoors;
            bool covered = _actorOutdoors == false;
            RequireCurrent();
            _search = new GroundApproachSearch(position, _context.Destination, _radius, _height,
                _purpose == GroundTransitionPurpose.Interaction, covered, _queries, () => Current);
        }
        return _search.Step();
    }
    public bool Validate(GroundApproachPlan plan) { RequireCurrent(); return _search?.Revalidate(plan) == true && Current; }
    public void ResetSearch() { RequireCurrent(); _search = null; _groundTravelSelected = false; }

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
        _held = false;
        Flightor.MoveToOwnedExterior(plan.AirWaypoint, () => Current, token => _flightToken = token, _routeLease);
        RequireCurrent();
    }

    public void Descend(GroundApproachPlan plan)
    {
        RequireCurrent();
        var actor = _context.Actor;
        var position = actor.Location;
        if (!Validate(plan) || position.Distance2DSqr(plan.Landing) > .5625f || position.Z < plan.Landing.Z)
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

    public GroundDismountState Dismount()
    {
        RequireCurrent();
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
                () => Current && actor.Location.Equals(position) && Current,
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

    public void Walk()
    {
        RequireCurrent();
        if (_mesh == null) throw Unknown("ground navigator unavailable");
        _groundTravelSelected = true;
        if (_purpose == GroundTransitionPurpose.Interaction && _context.Actor.Location.Distance(_context.Destination) > 12
            && _groundMount.Wait(_context, Now, () => Current, Hold)) return;
        var actor = _context.Actor;
        if (actor.IsCasting || actor.ChanneledCastingSpellId != 0) return;
        if (WorldQueryObservation.ReadLocalVehicle(actor)) throw Unknown("ground route belongs to a vehicle");
        var state = WorldQueryObservation.ReadGroundUnitState(actor);
        WoWPoint travelDestination = _context.Destination;
        // One complete preflight, then pure memory/identity predicates. Calling
        // CanActUnmounted in every route predicate repeated Lua dozens of times
        // and could overwrite a prepared native movement command.
        bool Admitted() => Current && _context.Destination.Equals(travelDestination) && !actor.IsCasting && actor.ChanneledCastingSpellId == 0
            && (!state.Mounted || _purpose == GroundTransitionPurpose.Interaction && actor.Location.Distance(_context.Destination) > 12)
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
            Array.AsReadOnly(_rays.ToArray()));
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
