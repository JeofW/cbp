#nullable disable
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using Styx.Combat.CombatRoutine;
using Styx.Logic.Pathing;
using Styx.WoWInternals;

namespace Styx.Database
{
    /// <summary>
    /// Provides NPC lookup queries from the SQLite database.
    /// </summary>
    public static class NpcQueries
    {
        #region SQL Commands

        // SELECT * FROM npcs WHERE name = @name LIMIT 1
        private static SQLiteCommand _getNpcByNameCmd;

        // SELECT * FROM npcs WHERE entry = @entry LIMIT 1
        private static SQLiteCommand _getNpcByIdCmd;

        // SELECT * FROM npcs WHERE map = @map AND trainer_class = @class AND level >= @LEVEL 
        // ORDER BY VECTORDISTANCE(x, y, z, @x, @y, @z) ASC
        private static SQLiteCommand _getNearestTrainerCmd;

        // SELECT * FROM npcs WHERE map = @map AND (flag & @flags) != 0 
        // Read in distance order until an eligible, reachable NPC is found.
        private static SQLiteCommand _getNearestNpcCmd;

        private static bool _initialized = false;

        // Navigation caches - same as HB 4.3.4 dictionary_0 / dictionary_1
        private static readonly Dictionary<NpcResult, bool> _trainerNavCache = new Dictionary<NpcResult, bool>();
        private static readonly Dictionary<NpcResult, bool> _npcNavCache = new Dictionary<NpcResult, bool>();

        #endregion

        #region Initialization

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            // Access Instance to trigger initialization
            var conn = Connection.Instance;
            if (conn == null || !Connection.IsAvailable) return;

            // Initialize SQL commands - EXACT comme HB
            _getNpcByNameCmd = Connection.CreateCommand(
                "SELECT * FROM npcs WHERE name LIKE '%@NAME%' LIMIT 1");

            _getNpcByIdCmd = Connection.CreateCommand(
                "SELECT * FROM npcs WHERE entry = @ENTRY LIMIT 1");

            _getNearestTrainerCmd = Connection.CreateCommand(
                "SELECT * FROM npcs WHERE map = @MAP_ID AND trainer_class = @TRAINER_CLASS AND level >= @LEVEL ORDER BY VECTORDISTANCE(x,y,z,@X,@Y,@Z) ASC");

            _getNearestNpcCmd = Connection.CreateCommand(
                "SELECT * FROM npcs WHERE map = @MAP_ID AND flag & @FLAG ORDER BY VECTORDISTANCE(x,y,z,@X,@Y,@Z) ASC");
        }

        #endregion

        #region Public Methods

        internal static int GetFactionPreference(WoWUnitReaction reaction)
        {
            return reaction >= WoWUnitReaction.Friendly ? 1 : 0;
        }

        internal static IEnumerable<NpcResult> OrderByFactionPreference(
            IEnumerable<NpcResult> candidates,
            Func<NpcResult, WoWUnitReaction> relationSelector)
        {
            return candidates.OrderByDescending(
                candidate => GetFactionPreference(relationSelector(candidate)));
        }

        /// <summary>
        /// Gets an NPC by name.
        /// </summary>
        /// <param name="name">The name to search for.</param>
        /// <returns>The NPC result, or null if not found.</returns>
        public static NpcResult GetNpcByName(string name)
        {
            EnsureInitialized();
            if (_getNpcByNameCmd == null) return null;

            using var reader = Connection.ExecuteReader(_getNpcByNameCmd, name);
            if (reader != null && reader.Read())
            {
                return new NpcResult(reader);
            }
            return null;
        }

        /// <summary>
        /// Gets an NPC by entry ID.
        /// </summary>
        /// <param name="id">The entry ID.</param>
        /// <returns>The NPC result, or null if not found.</returns>
        public static NpcResult GetNpcById(uint id)
        {
            EnsureInitialized();
            if (_getNpcByIdCmd == null) return null;

            using var reader = Connection.ExecuteReader(_getNpcByIdCmd, id);
            if (reader != null && reader.Read())
            {
                return new NpcResult(reader);
            }
            return null;
        }

        /// <summary>
        /// Gets the nearest trainer of a specific class.
        /// </summary>
        /// <param name="myFaction">The player's faction.</param>
        /// <param name="mapId">The map ID.</param>
        /// <param name="searchLocation">The search location.</param>
        /// <param name="searchClass">The class to search for.</param>
        /// <returns>The nearest trainer, or null if not found.</returns>
        public static NpcResult GetNearestTrainer(WoWFaction myFaction, uint mapId, WoWPoint searchLocation, WoWClass searchClass)
        {
            return GetNearestTrainer(myFaction, mapId, searchLocation, searchClass, null);
        }

        private static NpcResult GetNearestTrainer(WoWFaction myFaction, uint mapId,
            WoWPoint searchLocation, WoWClass searchClass, Func<NpcResult, bool> extraConditions)
        {
            EnsureInitialized();
            if (_getNearestTrainerCmd == null) return null;

            int minLevel = StyxWoW.Me.Level > 10 ? 10 : 0;
            WoWPoint location = StyxWoW.Me.Location;

            using var reader = Connection.ExecuteReader(_getNearestTrainerCmd,
                mapId,
                (int)searchClass,
                minLevel,
                searchLocation.X,
                searchLocation.Y,
                searchLocation.Z);

            if (reader == null) return null;

            var candidates = new List<NpcResult>();
            while (reader.Read())
            {
                NpcResult result = new NpcResult(reader);
                if (Styx.Logic.Profiles.VendorSafetyPolicy.IsRejected(result.Entry) ||
                    (extraConditions != null && !extraConditions(result)))
                    continue;
                WoWUnitReaction reaction = myFaction.RelationTo(new WoWFaction(result.Faction));
                if ((result.NpcFlags & 32U) != 0U && reaction >= WoWUnitReaction.Neutral)
                {
                    candidates.Add(result);
                }
            }

            foreach (NpcResult result in OrderByFactionPreference(
                         candidates,
                         candidate => myFaction.RelationTo(new WoWFaction(candidate.Faction))))
            {
                if (_trainerNavCache.TryGetValue(result, out bool cached))
                {
                    if (!cached) continue;
                }
                else
                {
                    bool canNav = Navigator.CanNavigateFully(location, result.Location);
                    _trainerNavCache.Add(result, canNav);
                    if (!canNav) continue;
                }
                return result;
            }
            return null;
        }

        /// <summary>
        /// Gets the nearest NPC with specific flags.
        /// </summary>
        /// <param name="myFaction">The player's faction.</param>
        /// <param name="mapId">The map ID.</param>
        /// <param name="searchLocation">The search location.</param>
        /// <param name="npcFlags">The NPC flags to search for.</param>
        /// <returns>The nearest NPC, or null if not found.</returns>
        public static NpcResult GetNearestNpc(
            WoWFaction myFaction,
            uint mapId,
            WoWPoint searchLocation,
            UnitNPCFlags npcFlags,
            Func<NpcResult, bool> extraConditions = null)
        {
            EnsureInitialized();
            if (_getNearestNpcCmd == null) return null;

            WoWClass myClass = StyxWoW.Me.Class;

            // If looking for class trainer, use specialized query
            if ((npcFlags & UnitNPCFlags.ClassTrainer) != UnitNPCFlags.None)
            {
                return GetNearestTrainer(myFaction, mapId, searchLocation, myClass, extraConditions);
            }

            using var reader = Connection.ExecuteReader(_getNearestNpcCmd,
                mapId,
                (uint)npcFlags,
                searchLocation.X,
                searchLocation.Y,
                searchLocation.Z);

            if (reader == null) return null;

            WoWPoint location = StyxWoW.Me.Location;

            var candidates = new List<NpcResult>();
            while (reader.Read())
            {
                NpcResult result = new NpcResult(reader);
                
                // Skip NPCs with invalid faction
                if (result.Faction == 0)
                    continue;
                
                // Check if class trainer matches our class (if applicable) and faction is friendly
                WoWUnitReaction reaction = myFaction.RelationTo(new WoWFaction(result.Faction));
                if (((npcFlags & UnitNPCFlags.ClassTrainer) == UnitNPCFlags.None || result.TrainerClass == (int)myClass) &&
                    reaction >= WoWUnitReaction.Neutral)
                {
                    if (extraConditions != null && !extraConditions(result))
                        continue;
                    candidates.Add(result);
                }
            }

            foreach (NpcResult result in OrderByFactionPreference(
                         candidates,
                         candidate => myFaction.RelationTo(new WoWFaction(candidate.Faction))))
            {
                if (_npcNavCache.TryGetValue(result, out bool cached))
                {
                    if (!cached) continue;
                }
                else
                {
                    bool canNav = Navigator.CanNavigateFully(location, result.Location);
                    _npcNavCache.Add(result, canNav);
                    if (!canNav) continue;
                }
                return result;
            }
            return null;
        }

        #endregion
    }
}
