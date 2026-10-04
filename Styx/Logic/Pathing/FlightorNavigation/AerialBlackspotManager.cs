// BlackspotManager.cs — Ported from HB 6.2.3 Styx/Pathing/FlightorNavigation/BlackspotManager.cs
// Manages polygon-based no-fly zones per map and faction (cities, phased areas, etc.).
// WotLK: only maps 0 (EK), 1 (Kalimdor), 530 (Outland), 571 (Northrend) are populated.

using System;
using System.Collections.Generic;
using System.Linq;
using Styx;
using Styx.Helpers;
using Tripper.XNAMath;

namespace Styx.Logic.Pathing.FlightorNavigation
{
    /// <summary>Player faction group — used as key for faction-specific aerial blackspots.</summary>
    public enum WoWFactionGroup
    {
        Neutral,
        Alliance,
        Horde
    }

    /// <summary>
    /// Manages polygon-based aerial blackspots that Flightor avoids during flight.
    /// Ported from HB 6.2.3 BlackspotManager; WotLK-only default data.
    /// </summary>
    public static class BlackspotManager
    {
        // (mapId, faction) → list of polygons
        private static readonly Dictionary<Tuple<uint, WoWFactionGroup>, List<Vector2[]>> _blackspots =
            new Dictionary<Tuple<uint, WoWFactionGroup>, List<Vector2[]>>();

        private static bool _initialized;
        private static long _revision;
        internal static long Revision { get { EnsureInitialized(); return _revision; } }
        internal static WoWFactionGroup CurrentFaction => PlayerFaction;
        internal static string ContextKey => StyxWoW.Me.MapId + ":" + PlayerFaction + ":" + Revision;

        /// <summary>Current player faction derived from race.</summary>
        private static WoWFactionGroup PlayerFaction
        {
            get
            {
                var me = StyxWoW.Me;
                if (me == null) return WoWFactionGroup.Neutral;
                if (me.IsAlliance) return WoWFactionGroup.Alliance;
                if (me.IsHorde)    return WoWFactionGroup.Horde;
                return WoWFactionGroup.Neutral;
            }
        }

        /// <summary>
        /// All aerial blackspot polygons for the current map and player faction.
        /// Includes neutral polygons + faction-specific polygons.
        /// </summary>
        public static IEnumerable<Vector2[]> Blackspots
        {
            get
            {
                EnsureInitialized();

                uint mapId = StyxWoW.Me.MapId;
                WoWFactionGroup faction = PlayerFaction;

                var result = new List<Vector2[]>();

                var neutralKey  = Tuple.Create(mapId, WoWFactionGroup.Neutral);
                var factionKey  = Tuple.Create(mapId, faction);

                if (_blackspots.TryGetValue(neutralKey, out var neutral))
                    result.AddRange(neutral);

                if (faction != WoWFactionGroup.Neutral && _blackspots.TryGetValue(factionKey, out var factioned))
                    result.AddRange(factioned);

                result.AddRange(AerialSettlementData.For(mapId, faction));

                return result;
            }
        }

        /// <summary>Returns true if <paramref name="point"/> falls inside any active aerial blackspot.</summary>
        public static bool IsInBlackspot(WoWPoint point)
        {
            var v = new Vector2(point.X, point.Y);
            return Blackspots.Any(poly => PointInPolygon(v, poly));
        }

        /// <summary>Validate the complete flight segment, including altitude-independent exclusions.</summary>
        public static bool IsSegmentAllowed(WoWPoint from, WoWPoint to)
        {
            if (!float.IsFinite(from.X) || !float.IsFinite(from.Y) || !float.IsFinite(from.Z)
                || !float.IsFinite(to.X) || !float.IsFinite(to.Y) || !float.IsFinite(to.Z)) return false;
            var a = new Vector2(from.X, from.Y); var b = new Vector2(to.X, to.Y);
            foreach (var polygon in Blackspots)
            {
                if (polygon == null || polygon.Length < 3) return false;
                bool leaving = StrictlyInside(a, polygon);
                if (StrictlyInside(b, polygon))
                {
                    // Existing presence permits movement toward the boundary,
                    // never a new flight deeper into the protected footprint.
                    if (!leaving || BoundaryDistanceSquared(b, polygon) >= BoundaryDistanceSquared(a, polygon)) return false;
                }
                var cuts = new List<float> { 0, 1 };
                float dx = b.X - a.X, dy = b.Y - a.Y;
                for (int i = 0; i < polygon.Length; i++)
                {
                    var c = polygon[i]; var d = polygon[(i + 1) % polygon.Length];
                    float ex = d.X - c.X, ey = d.Y - c.Y;
                    float denominator = dx * ey - dy * ex;
                    if (Math.Abs(denominator) < .00001f) continue;
                    float t = ((c.X - a.X) * ey - (c.Y - a.Y) * ex) / denominator;
                    float u = ((c.X - a.X) * dy - (c.Y - a.Y) * dx) / denominator;
                    if (t > 0 && t < 1 && u >= 0 && u <= 1) cuts.Add(t);
                }
                cuts.Sort();
                bool exited = !leaving;
                for (int i = 1; i < cuts.Count; i++)
                {
                    float t = (cuts[i - 1] + cuts[i]) / 2;
                    bool inside = StrictlyInside(new Vector2(a.X + dx * t, a.Y + dy * t), polygon);
                    if (inside && exited) return false;
                    if (!inside) exited = true;
                }
            }
            return true;
        }

        private static bool StrictlyInside(Vector2 point, Vector2[] polygon)
            => PointInPolygon(point, polygon) && BoundaryDistanceSquared(point, polygon) > .0001f;

        public static bool IsRecoveryRegionClear(WoWPoint point, float radius)
        {
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z)
                || !float.IsFinite(radius) || radius <= 0) return false;
            var center = new Vector2(point.X, point.Y);
            return !Blackspots.Any(polygon => PointInPolygon(center, polygon)
                || BoundaryDistanceSquared(center, polygon) <= radius * radius);
        }

        private static float BoundaryDistanceSquared(Vector2 point, Vector2[] polygon)
        {
            float minimum = float.PositiveInfinity;
            for (int i = 0; i < polygon.Length; i++)
            {
                var a = polygon[i]; var b = polygon[(i + 1) % polygon.Length];
                float dx = b.X - a.X, dy = b.Y - a.Y, length = dx * dx + dy * dy;
                float t = length <= .0001f ? 0 : Math.Clamp(((point.X - a.X) * dx + (point.Y - a.Y) * dy) / length, 0, 1);
                float x = point.X - a.X - t * dx, y = point.Y - a.Y - t * dy;
                minimum = Math.Min(minimum, x * x + y * y);
            }
            return minimum;
        }

        // Planning clearance keeps route vertices outside protected footprints.
        // Rectangles conservatively enclose custom shapes; the final segment
        // admission always evaluates the actual supplied polygon.
        internal static IEnumerable<Vector2[]> RoutingBlackspots
        {
            get
            {
                const float clearance = 6;
                var boxes = Blackspots.Select(polygon => (MinX: polygon.Min(point => point.X) - clearance,
                    MinY: polygon.Min(point => point.Y) - clearance, MaxX: polygon.Max(point => point.X) + clearance,
                    MaxY: polygon.Max(point => point.Y) + clearance)).ToList();
                // PolyNav holes are independent simple polygons. Unite touching
                // footprints before building its graph so overlapping legacy,
                // generated or user regions cannot cancel each other's interior.
                bool changed = true;
                while (changed)
                {
                    changed = false;
                    for (int i = 0; i < boxes.Count && !changed; i++)
                        for (int j = i + 1; j < boxes.Count; j++)
                        {
                            var a = boxes[i]; var b = boxes[j];
                            if (a.MinX > b.MaxX || b.MinX > a.MaxX || a.MinY > b.MaxY || b.MinY > a.MaxY) continue;
                            boxes[i] = (Math.Min(a.MinX, b.MinX), Math.Min(a.MinY, b.MinY), Math.Max(a.MaxX, b.MaxX), Math.Max(a.MaxY, b.MaxY));
                            boxes.RemoveAt(j); changed = true; break;
                        }
                }
                return boxes.Select(box => new[] { new Vector2(box.MinX, box.MinY), new Vector2(box.MaxX, box.MinY),
                    new Vector2(box.MaxX, box.MaxY), new Vector2(box.MinX, box.MaxY) }).ToArray();
            }
        }

        internal static bool TryGetFlightExit(Vector2 from, out Vector2 exit)
        {
            var boxes = RoutingBlackspots.ToArray();
            var candidates = new List<Vector2>();
            foreach (var box in boxes)
            {
                if (!PointInPolygon(from, box)) continue;
                candidates.Add(new Vector2(box[0].X - 4, from.Y));
                candidates.Add(new Vector2(box[1].X + 4, from.Y));
                candidates.Add(new Vector2(from.X, box[0].Y - 4));
                candidates.Add(new Vector2(from.X, box[2].Y + 4));
            }
            foreach (var candidate in candidates.OrderBy(point => (point.X - from.X) * (point.X - from.X)
                + (point.Y - from.Y) * (point.Y - from.Y)))
                if (!boxes.Any(box => PointInPolygon(candidate, box))
                    && IsSegmentAllowed(new WoWPoint(from.X, from.Y, 0), new WoWPoint(candidate.X, candidate.Y, 0)))
                { exit = candidate; return true; }
            exit = default;
            return false;
        }

        // ── Public add/remove API ──────────────────────────────────────────────

        public static void AddBlackspot(uint mapId, WoWFactionGroup faction, Vector2[] polygon)
        {
            var key = Tuple.Create(mapId, faction);
            if (!_blackspots.TryGetValue(key, out var list))
            {
                list = new List<Vector2[]>();
                _blackspots[key] = list;
            }
            if (!list.Contains(polygon))
            {
                list.Add(polygon);
                _revision++;
                Flightor.Clear(); // invalidate cached PolyNav
            }
        }

        public static void AddBlackspots(uint mapId, WoWFactionGroup faction, IEnumerable<Vector2[]> polygons)
        {
            foreach (var poly in polygons)
                AddBlackspot(mapId, faction, poly);
        }

        public static void RemoveBlackspot(uint mapId, WoWFactionGroup faction, Vector2[] polygon)
        {
            var key = Tuple.Create(mapId, faction);
            if (_blackspots.TryGetValue(key, out var list) && list.Remove(polygon))
            {
                _revision++;
                Flightor.Clear();
            }
        }

        public static void RemoveBlackspots(uint mapId, WoWFactionGroup faction, IEnumerable<Vector2[]> polygons)
        {
            foreach (var poly in polygons)
                RemoveBlackspot(mapId, faction, poly);
        }

        // ── Point-in-polygon test (ray-casting) ───────────────────────────────

        private static bool PointInPolygon(Vector2 point, Vector2[] polygon)
        {
            bool inside = false;
            int i = 0, j = polygon.Length - 1;
            while (i < polygon.Length)
            {
                float xi = polygon[i].X, yi = polygon[i].Y;
                float xj = polygon[j].X, yj = polygon[j].Y;
                if ((yi > point.Y) != (yj > point.Y) &&
                    point.X < (xj - xi) * (point.Y - yi) / (yj - yi) + xi)
                    inside = !inside;
                j = i++;
            }
            return inside;
        }

        // ── Default WotLK blackspots ──────────────────────────────────────────

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            LoadDefaults();
        }

        /// <summary>
        /// Default aerial no-fly polygons for WotLK maps.
        /// Ported verbatim from HB 6.2.3 BlackspotManager.smethod_2().
        /// Maps 870/1116/1464 (post-WotLK) are intentionally excluded.
        /// </summary>
        private static void LoadDefaults()
        {
            // ── Eastern Kingdoms (mapId 0) ─────────────────────────────────

            // Ironforge approach — neutral
            AddBlackspots(0U, WoWFactionGroup.Neutral, new List<Vector2[]>
            {
                new Vector2[]
                {
                    new Vector2(-7546.737f, -1368.555f),
                    new Vector2(-7344.393f, -1330.537f),
                    new Vector2(-7317.034f, -1210.047f),
                    new Vector2(-7336.192f, -955.021f),
                    new Vector2(-7335.584f, -797.5026f),
                    new Vector2(-7734.629f, -844.2428f),
                    new Vector2(-7796.103f, -1083.706f),
                    new Vector2(-7787.898f, -1264.005f)
                }
            });

            // Undercity approach — Horde
            AddBlackspots(0U, WoWFactionGroup.Horde, new List<Vector2[]>
            {
                new Vector2[]
                {
                    new Vector2(-9089.791f, 403.6077f),
                    new Vector2(-9019.003f, 310.1484f),
                    new Vector2(-8884.419f, 370.0167f),
                    new Vector2(-8817.055f, 288.0225f),
                    new Vector2(-8567.823f, 237.9563f),
                    new Vector2(-8372.982f, 74.27246f),
                    new Vector2(-8067.143f, 259.4106f),
                    new Vector2(-7942.156f, 896.7501f),
                    new Vector2(-8023.603f, 1172.448f),
                    new Vector2(-7952.16f, 1459.715f),
                    new Vector2(-8071.096f, 1412.827f),
                    new Vector2(-8175.608f, 1279.717f),
                    new Vector2(-8626.808f, 1248.927f),
                    new Vector2(-9274.56f, 958.4016f)
                }
            });

            // ── Kalimdor (mapId 1) ────────────────────────────────────────

            // Teldrassil / Darnassus — Alliance
            AddBlackspots(1U, WoWFactionGroup.Alliance, new List<Vector2[]>
            {
                new Vector2[]
                {
                    new Vector2(1348.726f, -4423.85f),
                    new Vector2(1406.258f, -4513.621f),
                    new Vector2(1611.74f, -4531.624f),
                    new Vector2(1730.437f, -4574.095f),
                    new Vector2(1909.313f, -4823.78f),
                    new Vector2(2036.518f, -4903.274f),
                    new Vector2(2197.151f, -4912.26f),
                    new Vector2(2506.568f, -4971.552f),
                    new Vector2(2640.775f, -4802.936f),
                    new Vector2(2431.873f, -4646.192f),
                    new Vector2(2161.392f, -4476.597f),
                    new Vector2(2129.687f, -4190.782f),
                    new Vector2(2075.95f, -4076.873f),
                    new Vector2(1893.812f, -4042.157f),
                    new Vector2(1792.939f, -3811.462f),
                    new Vector2(1385.134f, -4023.502f),
                    new Vector2(1338.027f, -4225.851f)
                }
            });

            // ── Outland (mapId 530) ───────────────────────────────────────

            // The Exodar — Alliance
            AddBlackspots(530U, WoWFactionGroup.Alliance, new List<Vector2[]>
            {
                new Vector2[]
                {
                    new Vector2(-517.6315f, 4136.477f),
                    new Vector2(-529.9949f, 4182.617f),
                    new Vector2(-563.7723f, 4216.395f),
                    new Vector2(-609.9131f, 4228.758f),
                    new Vector2(-656.0539f, 4216.395f),
                    new Vector2(-689.8313f, 4182.617f),
                    new Vector2(-702.1947f, 4136.477f),
                    new Vector2(-689.8313f, 4090.336f),
                    new Vector2(-656.0539f, 4056.558f),
                    new Vector2(-609.9131f, 4044.195f),
                    new Vector2(-563.7723f, 4056.558f),
                    new Vector2(-529.9948f, 4090.336f)
                }
            });

            // ── Northrend (mapId 571) ─────────────────────────────────────

            // Dalaran — neutral (the floating city is a no-fly zone interior)
            AddBlackspots(571U, WoWFactionGroup.Neutral, new List<Vector2[]>
            {
                new Vector2[]
                {
                    new Vector2(2786.607f, 6277.201f),
                    new Vector2(2772.39f, 6290.771f),
                    new Vector2(2739.774f, 6255.286f),
                    new Vector2(2735.843f, 6129.687f),
                    new Vector2(2766.215f, 6093.395f),
                    new Vector2(2790.594f, 6104.951f),
                    new Vector2(2901.596f, 6077.773f),
                    new Vector2(2942.01f, 6117.438f),
                    new Vector2(2907.869f, 6155.327f),
                    new Vector2(2925.325f, 6202.404f),
                    new Vector2(2908.878f, 6251.97f),
                    new Vector2(2845.846f, 6265.337f),
                    new Vector2(2807.114f, 6254.024f)
                }
            });
        }
    }
}
