using System;
using System.Linq;
using GreenMagic;
using Styx.Helpers;
using Styx.Logic.Pathing;
using Styx.Patchables;
using Styx.WoWInternals.WoWObjects;

namespace Styx.WoWInternals.World
{
    /// <summary>
    /// Fournit des méthodes pour interact avec le monde du jeu.
    /// WoW 3.3.5a build 12340.
    /// Ported from HB 4.3.4 with 3.3.5a offsets.
    /// </summary>
    public static class GameWorld
    {
        /// <summary>
        /// HB 3.3.5a exact CGWorldFrameHitFlags enum.
        /// Values verified from HB 3.3.5 obfuscated source (line 420-431).
        /// </summary>
        [Flags]
        public enum CGWorldFrameHitFlags : uint
        {
            HitTestNothing = 0,
            HitTestBoundingModels = 1,         // 0x1
            HitTestWMO = 16,                   // 0x10 - In WotLK, WMO is 0x10 (not 0x20 like Cata)
            HitTestUnknown = 64,               // 0x40
            HitTestGround = 256,               // 0x100
            HitTestLiquid = 65536,             // 0x10000
            HitTestLiquid2 = 131072,           // 0x20000
            HitTestMovableObjects = 1048576,   // 0x100000
            HitTestLOS = 1048593,              // 0x100011 = HitTestMovableObjects | HitTestWMO | HitTestBoundingModels
            HitTestSpellLoS = 16,              // 0x10 - Same as HitTestWMO for spell LoS checks
            HitTestGroundAndStructures = 1048849, // 0x100111 = HitTestMovableObjects | HitTestGround | HitTestWMO | HitTestBoundingModels
        }
        
        /// <summary>
        /// Legacy flags alias for backward compatibility.
        /// </summary>
        [Flags]
        public enum TraceLineHitFlags : uint
        {
            Nothing = 0,
            Terrain = 0x1,
            WMO = 0x10,
            Doodad = 0x8,
            Liquid = 0x10000,
            All = 0x100111,
            // HB 5.4.8+/6.2.3 alias; equal to All (0x100111) but makes the
            // targeting code more readable when asking only for collisions.
            Collision = All
        }

        private static TraceLineHitFlags MapFlags(CGWorldFrameHitFlags flags)
        {
            TraceLineHitFlags mapped = TraceLineHitFlags.Nothing;

            if ((flags & CGWorldFrameHitFlags.HitTestGround) != 0)
                mapped |= TraceLineHitFlags.Terrain;
            if ((flags & CGWorldFrameHitFlags.HitTestWMO) != 0)
                mapped |= TraceLineHitFlags.WMO;
            if ((flags & CGWorldFrameHitFlags.HitTestLiquid) != 0 || (flags & CGWorldFrameHitFlags.HitTestLiquid2) != 0)
                mapped |= TraceLineHitFlags.Liquid;
            if ((flags & CGWorldFrameHitFlags.HitTestBoundingModels) != 0)
                mapped |= TraceLineHitFlags.Doodad;

            if (mapped == TraceLineHitFlags.Nothing && flags != CGWorldFrameHitFlags.HitTestNothing)
                mapped = TraceLineHitFlags.All;

            return mapped;
        }

        /// <summary>
        /// Checks if two points are in line of sight.
        /// Uses native WoW CGWorldFrame::Intersect with HitTestLOS flag.
        /// Ported from HB 4.3.4.
        /// </summary>
        public static bool IsInLineOfSight(WoWPoint from, WoWPoint to)
        {
            return !TraceLine(from, to, CGWorldFrameHitFlags.HitTestLOS);
        }

        /// <summary>
        /// Checks if two points are in line of sight for spells.
        /// Uses native WoW CGWorldFrame::Intersect with HitTestSpellLoS flag.
        /// Ported from HB 4.3.4.
        /// </summary>
        public static bool IsInLineOfSpellSight(WoWPoint from, WoWPoint to)
        {
            return !TraceLine(from, to, CGWorldFrameHitFlags.HitTestSpellLoS);
        }

        /// <summary>
        /// Trace une ligne entre deux points pour détecter les collisions.
        /// Uses native WoW CGWorldFrame::Intersect function.
        /// Ported from HB 4.3.4.
        /// </summary>
        public static bool TraceLine(WoWPoint from, WoWPoint to, CGWorldFrameHitFlags flags)
        {
            return TraceLine(from, to, 1f, flags, out _);
        }

        public static bool TraceLine(WoWPoint from, WoWPoint to, TraceLineHitFlags flags)
        {
            return TraceLine(from, to, flags, out _);
        }

        public static bool TraceLine(WoWPoint from, WoWPoint to, CGWorldFrameHitFlags flags, out WoWPoint hitPoint)
        {
            return TraceLine(from, to, 1f, flags, out hitPoint);
        }

        /// <summary>
        /// Native WoW TraceLine using CGWorldFrame::Intersect.
        /// Ported from HB 3.3.5a - uses offset 0x0077F310.
        /// </summary>
        private static bool TraceLine(WoWPoint from, WoWPoint to, float distance, CGWorldFrameHitFlags flags, out WoWPoint hitPoint)
        {
            return WorldQueryObservation.TraceLine(from, to, distance, flags, out hitPoint);
        }

        /// <summary>
        /// Trace une ligne entre deux points et retourne le point de collision.
        /// Uses navmesh raycast for legacy TraceLineHitFlags.
        /// </summary>
        public static bool TraceLine(WoWPoint from, WoWPoint to, TraceLineHitFlags flags, out WoWPoint hitPoint)
        {
            hitPoint = to;
            
            // Pour les flags terrain/WMO, utiliser le navmesh raycast
            if ((flags & (TraceLineHitFlags.Terrain | TraceLineHitFlags.WMO)) != 0)
            {
                // Utiliser le Navigator de Styx qui wrape Tripper
                return Styx.Logic.Pathing.Navigator.Raycast(from, to, out hitPoint);
            }
            
            // Pas de collision détectée pour autres flags
            return false;
        }

        /// <summary>
        /// Trace plusieurs lignes en une seule opération (optimisé).
        /// </summary>
        public static void MassTraceLine(WorldLine[] lines, TraceLineHitFlags flag, out bool[] hitResults)
        {
            MassTraceLine(lines, Enumerable.Repeat(flag, lines.Length).ToArray(), out hitResults);
        }

        public static void MassTraceLine(WorldLine[] lines, CGWorldFrameHitFlags flag, out bool[] hitResults)
        {
            MassTraceLine(lines, Enumerable.Repeat(flag, lines.Length).ToArray(), out hitResults);
        }

        /// <summary>
        /// Trace plusieurs lignes avec flags différents.
        /// </summary>
        public static void MassTraceLine(WorldLine[] lines, TraceLineHitFlags[] flags, out bool[] hitResults)
        {
            MassTraceLine(lines, flags, out hitResults, out _);
        }

        public static void MassTraceLine(WorldLine[] lines, CGWorldFrameHitFlags[] flags, out bool[] hitResults)
        {
            MassTraceLine(lines, flags, out hitResults, out _);
        }

        /// <summary>
        /// Trace plusieurs lignes et retourne les points de collision.
        /// </summary>
        public static void MassTraceLine(WorldLine[] lines, TraceLineHitFlags flag, out bool[] hitResults, out WoWPoint[] hitPoints)
        {
            MassTraceLine(lines, Enumerable.Repeat(flag, lines.Length).ToArray(), out hitResults, out hitPoints);
        }

        public static void MassTraceLine(WorldLine[] lines, CGWorldFrameHitFlags flag, out bool[] hitResults, out WoWPoint[] hitPoints)
        {
            MassTraceLine(lines, Enumerable.Repeat(flag, lines.Length).ToArray(), out hitResults, out hitPoints);
        }

        /// <summary>
        /// Trace plusieurs lignes avec flags différents et retourne les points de collision.
        /// </summary>
        public static void MassTraceLine(WorldLine[] lines, TraceLineHitFlags[] flags, out bool[] hitResults, out WoWPoint[] hitPoints)
        {
            if (flags.Length != lines.Length)
                throw new ArgumentException("flags.Length is not the same as lines.Length!");

            hitResults = new bool[lines.Length];
            hitPoints = new WoWPoint[lines.Length];

            for (int i = 0; i < lines.Length; i++)
            {
                hitResults[i] = TraceLine(lines[i].Start, lines[i].End, flags[i], out hitPoints[i]);
            }
        }

        public static unsafe void MassTraceLine(WorldLine[] lines, CGWorldFrameHitFlags[] flags, out bool[] hitResults, out WoWPoint[] hitPoints)
        {
            WorldQueryObservation.TraceLines(lines, flags, out hitResults, out hitPoints);
        }
    }
}
