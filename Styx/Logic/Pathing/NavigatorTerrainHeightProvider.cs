using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Styx.WoWInternals;
using TripperNav = Tripper.Navigation;

namespace Styx.Logic.Pathing
{
    // HB 6.2.3 Class1050: queries navmesh polygons via QueryPolygons + GetPolyHeight.
    internal class NavigatorTerrainHeightProvider : ITerrainHeightProvider
    {
        // Native QueryPolygons converts WoW extents to Detour extents as (Y, Z, X),
        // so HB's Detour-space (1, 50000, 1) must be passed here as WoW-space (1, 1, 50000).
        private static readonly Vector3 HeightSearchExtents = new Vector3(1f, 1f, 50000f);

        public List<float> FindHeights(float x, float y)
        {
            var heights = new List<float>();
            if (!Navigator.IsNavigatorLoaded || !float.IsFinite(x) || !float.IsFinite(y))
                return heights;

            var actor = StyxWoW.Me;
            if (actor == null || !actor.IsValid || actor.Guid == 0)
                return heights;
            uint mapId = actor.MapId;
            ulong actorGuid = actor.Guid;
            var navigator = Navigator.TripperNavigator;
            bool Current() => ReferenceEquals(actor, StyxWoW.Me) && actor.IsValid
                && actor.Guid == actorGuid && actor.MapId == mapId && Navigator.IsNavigatorLoaded
                && ReferenceEquals(navigator, Navigator.TripperNavigator);
            if (navigator == null || !Current()) return heights;

            var center = new Vector3(x, y, 0f);
            TripperNav.PolygonReference[] polygons = navigator.QueryPolygons(mapId, center, HeightSearchExtents, 256);
            if (!Current() || polygons == null) return heights;
            foreach (TripperNav.PolygonReference polygon in polygons)
            {
                if (!Current()) return new List<float>();
                if (!navigator.ClosestPointOnPolyBoundary(mapId, polygon, center, out Vector3 boundaryPoint))
                    continue;

                if (!float.IsFinite(boundaryPoint.X) || !float.IsFinite(boundaryPoint.Y)
                    || !float.IsFinite(boundaryPoint.Z)
                    || Vector2.DistanceSquared(new Vector2(boundaryPoint.X, boundaryPoint.Y), new Vector2(x, y)) > 0.005f)
                    continue;

                if (!Current()) return new List<float>();
                if (!navigator.GetPolyHeight(mapId, polygon, center, out float height) || !float.IsFinite(height))
                    continue;

                if (heights.All(existingHeight => Math.Abs(existingHeight - height) > 1f))
                    heights.Add(height);
            }

            return Current() ? heights : new List<float>();
        }
    }
}
