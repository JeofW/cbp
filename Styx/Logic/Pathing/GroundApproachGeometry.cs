using System;
using System.Collections.Generic;
using System.Linq;
using Styx.Helpers;
using Styx.WoWInternals.World;
using Tripper.Navigation;

namespace Styx.Logic.Pathing;

/// <summary>Observed geometry only. An approach is permission to attempt a route, never arrival.</summary>
internal readonly record struct GroundSurface(WoWPoint Position, AreaType Area);
internal readonly record struct GroundRay(bool Hit, WoWPoint Point);

/// <summary>A bounded diagnostic copy of an actual query, never route or arrival authority.</summary>
public readonly record struct GroundApproachRayObservation(string Source, WoWPoint Start, WoWPoint End,
    uint Flags, bool? Hit, WoWPoint? Point);

internal sealed class GroundPath
{
    internal readonly bool Complete;
    internal readonly string Status;
    internal readonly IReadOnlyList<WoWPoint> Points;
    internal readonly IReadOnlyList<AreaType> Areas;
    internal GroundPath(bool complete, string status, IEnumerable<WoWPoint> points, IEnumerable<AreaType> areas)
    {
        Complete = complete; Status = status;
        Points = Array.AsReadOnly(points.ToArray()); Areas = Array.AsReadOnly(areas.ToArray());
    }
}

internal sealed record GroundApproachPlan(WoWPoint Landing, WoWPoint AirWaypoint, AreaType Area,
    GroundPath? OnwardPath, bool OpenColumn, string Source, bool ProgressOnly = false,
    WoWPoint? AirOrigin = null);

/// <summary>
/// Collision and mesh queries have separate authority. Implementations throw
/// ObservationUnavailableException for missing/short/stale observations; a null
/// surface or a failed path is an observed query result, not a substitute value.
/// </summary>
internal interface IGroundApproachQueries
{
    GroundRay[] Trace(WorldLine[] lines, GameWorld.CGWorldFrameHitFlags flags);
    GroundSurface? Snap(WoWPoint point);
    GroundPath Path(WoWPoint from, WoWPoint to);
    bool Forbidden(WoWPoint point, float radius);
    WoWPoint FlightGoal(WoWPoint from, WoWPoint destination) => destination;
    bool FlightSegmentAllowed(WoWPoint from, WoWPoint destination) => true;
}

/// <summary>
/// Incrementally searches actual mesh legs and collision-supported landing
/// regions. Radial coordinates are query inputs only; they never identify an
/// entrance. The ground navigator must still traverse and observe the doorway.
/// </summary>
internal sealed class GroundApproachSearch
{
    // Probe budget only: a finite airborne destination is not a ground-height
    // observation. Every hit still needs matching mesh, footprint and clearance
    // proof, and movement still needs observed landing before mount removal.
    private const float LocalSupportProbeDepth = 512f;
    private readonly WoWPoint _origin, _destination;
    private readonly float _radius, _height;
    private readonly float _interactionRange;
    private WoWPoint? _groundGoal;
    private bool _goalObserved;
    private readonly bool _requireOnward, _allowCoveredBelowActor, _progressLeg;
    private readonly IGroundApproachQueries _queries;
    private readonly Func<bool> _current;
    private readonly Func<WoWPoint>? _airPosition;
    private IEnumerator<(WoWPoint Point, string Source)>? _seeds;
    private readonly HashSet<(int X, int Y, int Z)> _visited = new();
    internal int Attempts { get; private set; }
    internal bool Exhausted { get; private set; }
    internal string LastReason { get; private set; } = "search-not-started";
    internal GroundPath? LastPath { get; private set; }
    internal GroundApproachPlan? Plan { get; private set; }

    internal GroundApproachSearch(WoWPoint origin, WoWPoint destination, float radius, float height,
        bool requireOnward, bool allowCoveredBelowActor, IGroundApproachQueries queries, Func<bool> current,
        float interactionRange = 0, Func<WoWPoint>? airPosition = null)
    {
        if (!Finite(origin) || !Finite(destination) || !float.IsFinite(radius) || radius <= 0 || radius > 8
            || !float.IsFinite(height) || height <= 0 || height > 24
            || !float.IsFinite(interactionRange) || interactionRange < 0)
            throw Unknown("invalid actor/destination/body observation");
        _origin = origin; _destination = destination; _radius = Math.Max(.6f, radius);
        _height = Math.Max(1.5f, height); _requireOnward = requireOnward;
        _allowCoveredBelowActor = allowCoveredBelowActor; _queries = queries; _current = current;
        _interactionRange = interactionRange;
        _progressLeg = requireOnward && origin.Distance2DSqr(destination) > 120f * 120f;
        _airPosition = _progressLeg ? airPosition : null;
    }

    internal GroundApproachPlan? Step(int candidateBudget = 4)
    {
        RequireCurrent();
        if (Plan != null || Exhausted) return Plan;
        _seeds ??= Seeds().GetEnumerator();
        for (int n = 0; n < Math.Clamp(candidateBudget, 1, 8);)
        {
            RequireCurrent();
            if (Attempts >= 384 || !_seeds.MoveNext()) { Exhausted = true; LastReason = "no-proven-landing-and-onward-route"; break; }
            var seed = _seeds.Current;
            if (!_visited.Add(((int)MathF.Round(seed.Point.X * 2), (int)MathF.Round(seed.Point.Y * 2), (int)MathF.Round(seed.Point.Z)))) continue;
            Attempts++; n++;
            if (_airPosition != null)
            {
                var corridor = AirCorridor(seed.Point);
                RequireCurrent();
                if (corridor == null) continue;
                Plan = corridor;
                LastReason = "observed-air-corridor; landing-unobserved";
                return Plan;
            }
            var landing = Project(seed.Point);
            RequireCurrent();
            if (landing == null) continue;
            var plan = Validate(landing.Value, seed.Source);
            RequireCurrent();
            if (plan == null) continue;
            Plan = plan;
            LastReason = plan.ProgressOnly ? "supported-local-flight-leg; final-approach-unobserved"
                : "supported-approach-and-static-mesh-leg; traversal-unobserved";
            return Plan;
        }
        return null;
    }

    internal bool Revalidate(GroundApproachPlan plan)
    {
        RequireCurrent();
        if (plan.AirOrigin.HasValue)
            return _airPosition != null && plan.ProgressOnly && plan.Landing.Equals(WoWPoint.Empty)
                && AirCorridor(plan.AirWaypoint) != null && _current();
        var surface = _queries.Snap(plan.Landing);
        RequireCurrent();
        return surface != null && surface.Value.Position.DistanceSqr(plan.Landing) <= .25f
            && Validate(surface.Value, plan.Source) != null && _current();
    }

    private IEnumerable<(WoWPoint Point, string Source)> Seeds()
    {
        if (_progressLeg)
        {
            // The client's collision world is local. Remote misses cannot prove
            // a destination landing or justify downgrading an entire journey.
            // Already-airborne seeds need an observed swept air corridor. A
            // grounded departure still requires supported mesh and takeoff
            // clearance. Neither kind supplies final arrival authority.
            // Local collision planning follows the exclusion-aware route. A
            // necessary detour may initially increase distance to the NPC.
            WoWPoint goal = _queries.FlightGoal(_origin, _destination);
            RequireCurrent();
            if (!Finite(goal)) throw Unknown("aerial route has no finite next waypoint");
            double bearing = Math.Atan2(goal.Y - _origin.Y, goal.X - _origin.X);
            float goalDistance = MathF.Sqrt(_origin.Distance2DSqr(goal));
            foreach (float distance in new[] { 64f, 48f, 32f })
                foreach (double offset in new[] { 0d, -Math.PI / 8, Math.PI / 8, -Math.PI / 4, Math.PI / 4 })
                {
                    float leg = Math.Min(distance, goalDistance);
                    var point = _origin.Add(leg * (float)Math.Cos(bearing + offset),
                        leg * (float)Math.Sin(bearing + offset), 0);
                    if (point.Distance2DSqr(goal) < _origin.Distance2DSqr(goal))
                        yield return (point, "local-flight-leg");
                }
            yield break;
        }
        ObserveGroundGoal();
        // A selected object can be used from supported ground inside its actual
        // range. Its model origin is not necessarily a walkable mesh endpoint.
        if (_interactionRange > 0) yield return (_destination, "object-interaction-volume");
        // Combat can land near its current position without asserting that a
        // hostile target is reachable. Interaction requires the onward mesh leg.
        if (!_requireOnward) yield return (_origin, "actor-column");
        GroundSurface? originGround = Project(_origin);
        RequireCurrent();
        if (_requireOnward && originGround != null && _groundGoal.HasValue)
        {
            GroundPath path = LastPath = _queries.Path(originGround.Value.Position, _groundGoal.Value);
            RequireCurrent();
            if (Usable(path, originGround.Value.Position, _groundGoal.Value))
            {
                // Work outward from the interior end along an actual full path.
                // A roof hit or another floor is rejected again by projection,
                // exact mesh endpoints and positive support/clearance queries.
                for (int i = path.Points.Count - 1; i >= 0; i--)
                {
                    yield return (path.Points[i], "complete-ground-path");
                    if (i == 0) continue;
                    WoWPoint near = path.Points[i], far = path.Points[i - 1];
                    int samples = Math.Min(64, (int)Math.Ceiling(near.Distance(far) / 3f));
                    for (int j = 1; j < samples; j++)
                        yield return (Interpolate(near, far, (float)j / samples), "complete-ground-path-segment");
                }
            }
        }
        yield return (_destination, "destination-column");
        double heading = Math.Atan2(_origin.Y - _destination.Y, _origin.X - _destination.X);
        foreach (float radius in new[] { 4f, 8f, 16f, 32f, 64f, 96f })
            for (int i = 0; i < 16; i++)
            {
                double angle = heading + i * Math.PI / 8;
                yield return (_destination.Add(radius * (float)Math.Cos(angle), radius * (float)Math.Sin(angle), 0), "bounded-support-search");
            }
        yield return (_origin, "actor-column-fallback");
    }

    private void ObserveGroundGoal()
    {
        if (_goalObserved) return;
        RequireCurrent();
        if (_interactionRange == 0) _groundGoal = _destination;
        else
        {
            var surface = _queries.Snap(_destination);
            RequireCurrent();
            if (surface != null && Finite(surface.Value.Position)
                && surface.Value.Position.DistanceSqr(_destination) <= _interactionRange * _interactionRange
                && (LandingArea(surface.Value.Area) || surface.Value.Area == AreaType.KnownBuilding))
                _groundGoal = surface.Value.Position;
        }
        _goalObserved = true;
    }

    private GroundSurface? Project(WoWPoint seed)
    {
        RequireCurrent();
        bool coveredActorColumn = _allowCoveredBelowActor && !_requireOnward
            && _origin.Distance2DSqr(seed) <= .5625f;
        var from = coveredActorColumn
            ? new WoWPoint(seed.X, seed.Y, _origin.Z)
            : new WoWPoint(seed.X, seed.Y, Math.Max(_origin.Z + 2, seed.Z + 100));
        var to = new WoWPoint(seed.X, seed.Y,
            Math.Min(Math.Min(_destination.Z, seed.Z) - 40, _origin.Z - LocalSupportProbeDepth));
        GroundRay ray = Trace(new[] { new WorldLine(from, to) }, GameWorld.CGWorldFrameHitFlags.HitTestGroundAndStructures)[0];
        if (!ray.Hit || !OnVertical(from, to, ray.Point)) { LastReason = "no-solid-support-in-observed-column"; return null; }
        var surface = _queries.Snap(ray.Point);
        RequireCurrent();
        if (surface == null || !Finite(surface.Value.Position) || surface.Value.Position.Distance2DSqr(ray.Point) > .5625f
            || Math.Abs(surface.Value.Position.Z - ray.Point.Z) > .65f || !LandingArea(surface.Value.Area))
        { LastReason = "support-has-no-matching-safe-mesh-surface"; return null; }
        return surface;
    }

    private GroundApproachPlan? AirCorridor(WoWPoint destination)
    {
        // Passage through air and ground arrival have independent evidence. A
        // flying actor may cross water, roofs and unavailable ground mesh when
        // its local body corridor is clear. This plan makes no landing claim.
        RequireCurrent();
        WoWPoint origin = _airPosition!();
        if (!Finite(origin) || !Finite(destination) || origin.DistanceSqr(destination) > 100f * 100f)
        { LastReason = "air-corridor-outside-local-observation"; return null; }
        if (!_queries.FlightSegmentAllowed(origin, destination))
        { LastReason = "air-corridor-crosses-exclusion"; return null; }
        RequireCurrent();
        float padding = _radius + 1f;
        var offsets = new[] { (0f, 0f), (padding, 0f), (-padding, 0f), (0f, padding), (0f, -padding),
            (padding * .7071068f, padding * .7071068f), (-padding * .7071068f, padding * .7071068f),
            (padding * .7071068f, -padding * .7071068f), (-padding * .7071068f, -padding * .7071068f) };
        int samples = Math.Max(1, (int)Math.Ceiling(origin.Distance(destination) / 4f));
        for (int i = 0; i <= samples; i++)
        {
            if (_queries.Forbidden(Interpolate(origin, destination, (float)i / samples), padding))
            { LastReason = "air-corridor-crosses-exclusion"; return null; }
            RequireCurrent();
        }
        if (origin.DistanceSqr(destination) > .01f)
        {
            var lines = offsets.SelectMany(offset => new[] { .25f, _height / 2, _height }
                .Select(z => new WorldLine(origin.Add(offset.Item1, offset.Item2, z),
                    destination.Add(offset.Item1, offset.Item2, z)))).ToArray();
            if (Trace(lines, GameWorld.CGWorldFrameHitFlags.HitTestGroundAndStructures).Any(ray => ray.Hit))
            { LastReason = "air-body-corridor-blocked"; return null; }
        }
        WoWPoint currentPosition = _airPosition();
        RequireCurrent();
        // Forward movement within the measured corridor is still covered by
        // these rays. The extra clearance bounds lateral drift; unrelated motion
        // needs a new observation instead of borrowing this corridor.
        WoWPoint delta = new(destination.X - origin.X, destination.Y - origin.Y, destination.Z - origin.Z);
        float lengthSquared = origin.DistanceSqr(destination);
        float along = lengthSquared <= .01f ? 0 : Math.Clamp(((currentPosition.X - origin.X) * delta.X
            + (currentPosition.Y - origin.Y) * delta.Y + (currentPosition.Z - origin.Z) * delta.Z) / lengthSquared, 0, 1);
        if (!Finite(currentPosition) || currentPosition.DistanceSqr(Interpolate(origin, destination, along)) > 1f)
        { LastReason = "actor-left-observed-air-corridor"; return null; }
        return new GroundApproachPlan(WoWPoint.Empty, destination, (AreaType)0, null, false,
            "observed-air-corridor", true, origin);
    }

    private GroundApproachPlan? Validate(GroundSurface surface, string source)
    {
        RequireCurrent();
        bool progressOnly = _progressLeg && source == "local-flight-leg";
        WoWPoint p = surface.Position;
        if (!LandingArea(surface.Area) || _queries.Forbidden(p, _radius)) { LastReason = "landing-area-forbidden"; return null; }
        RequireCurrent();
        var footprint = new[] { p, p.Add(_radius, 0, 0), p.Add(-_radius, 0, 0), p.Add(0, _radius, 0), p.Add(0, -_radius, 0) };
        var groundLines = footprint.Select(q => new WorldLine(q.Add(0, 0, .75f), q.Add(0, 0, -1.25f))).ToArray();
        GroundRay[] support = Trace(groundLines, GameWorld.CGWorldFrameHitFlags.HitTestGroundAndStructures);
        for (int i = 0; i < support.Length; i++)
            if (!support[i].Hit || !OnVertical(groundLines[i].Start, groundLines[i].End, support[i].Point)
                || Math.Abs(support[i].Point.Z - p.Z) > .55f || _queries.Forbidden(support[i].Point, .1f))
            { LastReason = "landing-footprint-not-supported"; return null; }
        RequireCurrent();
        GroundRay[] liquid = Trace(groundLines, GameWorld.CGWorldFrameHitFlags.HitTestLiquid | GameWorld.CGWorldFrameHitFlags.HitTestLiquid2);
        for (int i = 0; i < support.Length; i++)
            if (!DrySupport(groundLines[i], support[i], liquid[i]))
            { LastReason = "landing-footprint-intersects-liquid"; return null; }
        float approachHeight = progressOnly ? Math.Max(40, _origin.Z - p.Z) : Math.Max(4, _height + 1);
        var openColumn = new[] { new WorldLine(p.Add(0, 0, .25f), p.Add(0, 0, Math.Max(250, _origin.Z - p.Z + 20))) };
        bool open = !Trace(openColumn, GameWorld.CGWorldFrameHitFlags.HitTestGroundAndStructures)[0].Hit;
        bool belowActor = !progressOnly && _allowCoveredBelowActor && _origin.Distance2DSqr(p) <= .5625f && _origin.Z >= p.Z;
        if (!open && !belowActor) { LastReason = "landing-column-covered"; return null; }
        if (!open) approachHeight = Math.Max(.75f, Math.Min(approachHeight, _origin.Z - p.Z));
        var clearance = footprint.Select(q => new WorldLine(q.Add(0, 0, .25f), q.Add(0, 0, Math.Max(_height, approachHeight + _height)))).ToArray();
        if (Trace(clearance, GameWorld.CGWorldFrameHitFlags.HitTestGroundAndStructures).Any(r => r.Hit))
        { LastReason = "landing-body-clearance-blocked"; return null; }
        GroundPath? onward = null;
        bool inObjectRange = _interactionRange > 0 && p.DistanceSqr(_destination) <= _interactionRange * _interactionRange;
        if (_requireOnward && !progressOnly && !inObjectRange)
        {
            ObserveGroundGoal();
            if (!_groundGoal.HasValue) { LastReason = "no-observed-ground-goal-in-object-range"; return null; }
            onward = LastPath = _queries.Path(p, _groundGoal.Value);
            RequireCurrent();
            if (!Usable(onward, p, _groundGoal.Value)) { LastReason = "onward-mesh-incomplete-or-wrong-floor"; return null; }
        }
        RequireCurrent();
        return new GroundApproachPlan(p, p.Add(0, 0, approachHeight), surface.Area, onward, open, source, progressOnly);
    }

    // Both rays use the same observed column. A liquid plane underneath a
    // positively observed solid surface is occluded by that surface; exposed
    // liquid at/above it (or an invalid hit) never grants dry-ground authority.
    internal static bool DrySupport(WorldLine line, GroundRay solid, GroundRay liquid)
    {
        if (!solid.Hit || !OnVertical(line.Start, line.End, solid.Point)) return false;
        if (!liquid.Hit) return true;
        if (!OnVertical(line.Start, line.End, liquid.Point))
            throw Unknown("liquid support hit is outside its observed column");
        return liquid.Point.Z < solid.Point.Z - .05f;
    }

    internal static bool Usable(GroundPath path, WoWPoint from, WoWPoint to) => path.Complete
        && path.Points.Count >= 2 && path.Points.All(Finite) && path.Areas.Count != 0
        && path.Areas.All(a => LandingArea(a) || a == AreaType.KnownBuilding || a == AreaType.Gate || a == AreaType.Elevator)
        && path.Points[0].Distance2DSqr(from) <= .5625f && Math.Abs(path.Points[0].Z - from.Z) <= .65f
        && path.Points[^1].Distance2DSqr(to) <= 2.25f && Math.Abs(path.Points[^1].Z - to.Z) <= .9f;

    private GroundRay[] Trace(WorldLine[] lines, GameWorld.CGWorldFrameHitFlags flags)
    {
        RequireCurrent();
        GroundRay[] result = _queries.Trace(lines, flags);
        RequireCurrent();
        if (result == null || result.Length != lines.Length || result.Any(r => r.Hit && !Finite(r.Point)))
            throw Unknown("incomplete collision observation");
        return result;
    }
    private void RequireCurrent() { if (!_current()) throw Unknown("ground approach owner changed"); }
    private static ObservationUnavailableException Unknown(string reason) => new("ground-approach", reason);
    internal static bool Finite(WoWPoint p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
    private static bool LandingArea(AreaType a) => a is AreaType.Ground or AreaType.Road or AreaType.Horde or AreaType.Alliance;
    private static bool OnVertical(WoWPoint from, WoWPoint to, WoWPoint point) => Finite(point)
        && point.Distance2DSqr(from) <= .01f && point.Z <= from.Z + .01f && point.Z >= to.Z - .01f;
    private static WoWPoint Interpolate(WoWPoint a, WoWPoint b, float t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
}
