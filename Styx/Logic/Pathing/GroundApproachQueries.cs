using System;
using System.Linq;
using System.Numerics;
using Styx.Helpers;
using Styx.Logic.Combat;
using Styx.WoWInternals;
using Styx.WoWInternals.World;
using Tripper.Navigation;

namespace Styx.Logic.Pathing;

/// <summary>The production collision/mesh adapter. It never substitutes one proof provider for another.</summary>
internal sealed class GroundApproachQueries : IGroundApproachQueries
{
    private readonly MeshNavigator _mesh;
    private readonly uint _map;
    private readonly Func<bool> _current;
    internal Action<GroundApproachRayObservation>? TraceObserved { get; set; }
    internal GroundApproachQueries(MeshNavigator mesh, uint map, Func<bool> current)
    { _mesh = mesh; _map = map; _current = current; }

    private T Observe<T>(Func<T> query)
    {
        try
        {
            RequireCurrent();
            T result = query();
            RequireCurrent();
            return result;
        }
        catch (Exception error)
        {
            RecoveryActions.RethrowControlFlow(error);
            if (error is ObservationUnavailableException) throw;
            throw new ObservationUnavailableException("ground-approach-query", error.GetType().Name + ": " + error.Message);
        }
    }

    private void RequireCurrent()
    {
        if (!_current() || !ReferenceEquals(Navigator.NavigationProvider, _mesh)
            || ObjectManager.Me?.MapId != _map || !Navigator.IsNavigatorLoaded)
            throw new ObservationUnavailableException("ground-approach-query", "mesh/provider/map/owner unavailable");
    }

    public GroundRay[] Trace(WorldLine[] lines, GameWorld.CGWorldFrameHitFlags flags) => Observe(() =>
    {
        try
        {
            GameWorld.MassTraceLine(lines, Enumerable.Repeat(flags, lines.Length).ToArray(), out bool[] hits, out WoWPoint[] points);
            RequireCurrent();
            if (hits == null || points == null || hits.Length != lines.Length || points.Length != lines.Length)
                throw new ObservationUnavailableException("ground-approach-query", "incomplete collision batch");
            for (int i = 0; i < lines.Length; i++)
                TraceObserved?.Invoke(new("approach", lines[i].Start, lines[i].End, (uint)flags, hits[i], hits[i] ? points[i] : null));
            return hits.Select((hit, index) => new GroundRay(hit, points[index])).ToArray();
        }
        catch (ObservationUnavailableException)
        {
            if (_current())
                foreach (var line in lines) TraceObserved?.Invoke(new("approach", line.Start, line.End, (uint)flags, null, null));
            throw;
        }
    });

    public GroundSurface? Snap(WoWPoint point) => Observe<GroundSurface?>(() =>
    {
        var native = Navigator.TripperNavigator;
        var position = new Vector3(point.X, point.Y, point.Z);
        native.EnsureTilesAroundPosition(_map, position, Navigator.LoadTilesAroundRadius);
        RequireCurrent();
        if (!native.FindNearestPolyRef(_map, position, out var polygon, out var snapped)) return null;
        RequireCurrent();
        var status = new Status(native.GetPolyArea(_map, polygon, out byte area));
        if (!status.Succeeded) throw new ObservationUnavailableException("ground-approach-query", "mesh polygon area unavailable");
        return new GroundSurface(new WoWPoint(snapped.X, snapped.Y, snapped.Z), (AreaType)area);
    });

    public GroundPath Path(WoWPoint from, WoWPoint to) => Observe(() =>
    {
        PathFindResult result = _mesh.FindPath(from, to);
        RequireCurrent();
        bool complete = result.Succeeded && !result.IsPartialPath && !result.Aborted;
        var points = (result.Points ?? Array.Empty<Vector3>()).ToArray();
        var areas = (result.PolyTypes ?? Array.Empty<AreaType>()).ToArray();
        // Detour's terminal End vertex can have no polygon reference/area.
        // The retained native replay contains Ground/0 with Start/End and a
        // terminal zero reference. Resolve that one vertex from a separate
        // positive mesh query; an interior/missing/partial area stays UNKNOWN.
        if (complete && points.Length >= 2 && areas.Length == points.Length && areas[^1] == 0
            && result.Flags?.Length == points.Length && result.Flags[^1] == StraightPathFlags.End
            && result.Polygons?.Length == points.Length && !result.Polygons[^1].IsValid)
        {
            var endpoint = new WoWPoint(points[^1].X, points[^1].Y, points[^1].Z);
            if (GroundApproachSearch.Finite(endpoint))
            {
                var surface = Snap(endpoint);
                RequireCurrent();
                if (surface.HasValue && GroundApproachSearch.Finite(surface.Value.Position)
                    && surface.Value.Position.DistanceSqr(endpoint) <= .25f)
                    areas[^1] = surface.Value.Area;
            }
        }
        return new GroundPath(complete,
            result.Status + "/" + result.FailStep,
            points.Select(p => new WoWPoint(p.X, p.Y, p.Z)), areas);
    });

    public bool Forbidden(WoWPoint point, float radius) => Observe(() => BlackspotManager.IsBlackspotted(point, radius));
    public WoWPoint FlightGoal(WoWPoint from, WoWPoint destination)
        => Observe(() => Flightor.GetFlightRouteWaypoint(from, destination));
    public bool FlightSegmentAllowed(WoWPoint from, WoWPoint destination)
        => Observe(() => Flightor.CanFollowFlightSegment(from, destination));
}
