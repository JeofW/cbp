using System;
using System.Collections.Generic;
using System.Numerics;
using Styx.WoWInternals;
using Styx.WoWInternals.World;
using Tripper.Navigation;

namespace Styx.Logic.Pathing
{
    // A small walking connector across a navmesh seam is allowed only when the
    // live world proves the entire corridor is supported and unobstructed.
    internal static class LocalMeshConnector
    {
        internal delegate bool TraceBatch(WorldLine[] lines, GameWorld.CGWorldFrameHitFlags flags,
            out bool[] hits, out WoWPoint[] points);

        internal static IEnumerable<WoWPoint> Candidates(WoWPoint origin, WoWPoint destination)
        {
            double heading = Math.Atan2(destination.Y - origin.Y, destination.X - origin.X);
            foreach (float radius in new[] { 2f, 4f })
                for (int i = 0; i < 12; i++)
                {
                    double angle = heading + i * Math.PI / 6;
                    yield return origin.Add(radius * (float)Math.Cos(angle), radius * (float)Math.Sin(angle), 0);
                }
        }

        internal static bool IsCandidateWithinLimits(WoWPoint origin, WoWPoint requested, WoWPoint snapped)
        {
            return IsFinite(snapped) && snapped != WoWPoint.Zero
                && requested.Distance2DSqr(snapped) <= 1f
                && origin.Distance2DSqr(snapped) >= 0.75f * 0.75f
                && origin.Distance2DSqr(snapped) <= 25f
                && Math.Abs(origin.Z - snapped.Z) <= 2f;
        }

        internal static bool HasUsableOnwardPath(PathFindResult path, WoWPoint landing, WoWPoint destination)
        {
            if (!path.Succeeded || path.IsPartialPath || path.Points == null || path.Points.Length < 2)
                return false;
            return Vector3.DistanceSquared(path.Points[0], new Vector3(landing.X, landing.Y, landing.Z)) <= 1f
                && Vector3.DistanceSquared(path.Points[path.Points.Length - 1], new Vector3(destination.X, destination.Y, destination.Z)) <= 4f;
        }

        internal static bool ValidateGroundCorridor(WoWPoint origin, WoWPoint landing,
            TraceBatch trace, Func<WoWPoint, bool> isBlackspotted, float bodyRadius = 0.5f,
            float bodyHeight = 1.2f, float maxDistance = 5f)
        {
            if (!IsFinite(origin) || !IsFinite(landing) || landing == WoWPoint.Zero
                || origin.Distance2DSqr(landing) < 0.01f
                || origin.Distance2DSqr(landing) > maxDistance * maxDistance
                || Math.Abs(origin.Z - landing.Z) > 2f)
                return false;
            float distance = origin.Distance2D(landing);
            int steps = (int)Math.Ceiling(distance / 0.75f);
            int laneSize = steps + 1;
            var groundLines = new WorldLine[laneSize * 3];
            float lateralX = -(landing.Y - origin.Y) / distance * bodyRadius;
            float lateralY = (landing.X - origin.X) / distance * bodyRadius;
            for (int lane = 0; lane < 3; lane++)
                for (int step = 0; step <= steps; step++)
                {
                    float t = (float)step / steps;
                    WoWPoint p = new WoWPoint(origin.X + (landing.X - origin.X) * t + lateralX * (lane - 1),
                        origin.Y + (landing.Y - origin.Y) * t + lateralY * (lane - 1),
                        origin.Z + (landing.Z - origin.Z) * t);
                    if (isBlackspotted(p)) return false;
                    groundLines[lane * laneSize + step] = new WorldLine(p.Add(0, 0, 0.75f), p.Add(0, 0, -1.25f));
                }

            if (!trace(groundLines, GameWorld.CGWorldFrameHitFlags.HitTestGroundAndStructures, out bool[] groundHits, out WoWPoint[] ground)
                || groundHits.Length != groundLines.Length || ground.Length != groundLines.Length)
                return false;
            for (int lane = 0; lane < 3; lane++)
                for (int step = 0; step <= steps; step++)
                {
                    int index = lane * laneSize + step;
                    WoWPoint p = ground[index];
                    WoWPoint expected = groundLines[index].Start.Add(0, 0, -0.75f);
                    if (!groundHits[index] || !IsFinite(p) || p == WoWPoint.Zero
                        || p.Distance2DSqr(expected) > 0.04f || Math.Abs(p.Z - expected.Z) > 0.65f
                        || isBlackspotted(p)) return false;
                    if (step > 0 && Math.Abs(p.Z - ground[index - 1].Z) > distance / steps * 0.9f + 0.1f)
                        return false;
                }
            if (!trace(groundLines, GameWorld.CGWorldFrameHitFlags.HitTestLiquid, out bool[] liquidHits, out _)
                || liquidHits.Length != groundLines.Length || Array.Exists(liquidHits, hit => hit))
                return false;

            var clearance = new WorldLine[steps * 3 * 2];
            int lineIndex = 0;
            for (int lane = 0; lane < 3; lane++)
                for (int step = 0; step < steps; step++)
                    foreach (float height in new[] { 0.4f, Math.Max(1.2f, bodyHeight) })
                        clearance[lineIndex++] = new WorldLine(ground[lane * laneSize + step].Add(0, 0, height),
                            ground[lane * laneSize + step + 1].Add(0, 0, height));
            return trace(clearance, GameWorld.CGWorldFrameHitFlags.HitTestGroundAndStructures, out bool[] blocked, out _)
                && blocked.Length == clearance.Length && !Array.Exists(blocked, hit => hit);
        }

        private static bool IsFinite(WoWPoint p)
        {
            return float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
        }
    }
}
