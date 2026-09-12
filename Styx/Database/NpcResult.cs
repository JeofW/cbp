#nullable disable
using System.Data.SQLite;
using Styx;
using Styx.Logic.Pathing;
using Styx.WoWInternals;

namespace Styx.Database
{
    /// <summary>
    /// Represents an NPC result from the SQLite database.
    /// </summary>
    public class NpcResult : System.IEquatable<NpcResult>
    {
        /// <summary>
        /// Creates an NpcResult from a SQLiteDataReader (HB API).
        /// </summary>
        internal NpcResult(SQLiteDataReader reader)
        {
            Entry = reader.GetInt32(reader.GetOrdinal("entry"));
            Name = reader.GetString(reader.GetOrdinal("name"));
            if (!reader.IsDBNull(reader.GetOrdinal("title")))
            {
                Title = reader.GetString(reader.GetOrdinal("title"));
            }
            X = reader.GetFloat(reader.GetOrdinal("x"));
            Y = reader.GetFloat(reader.GetOrdinal("y"));
            Z = reader.GetFloat(reader.GetOrdinal("z"));
            NpcFlags = (uint)reader.GetInt32(reader.GetOrdinal("flag"));
            Faction = (uint)reader.GetInt32(reader.GetOrdinal("faction"));
            MapId = reader.GetInt32(reader.GetOrdinal("map"));
            TrainerType = reader.GetInt32(reader.GetOrdinal("trainer_type"));
            TrainerClass = reader.GetInt32(reader.GetOrdinal("trainer_class"));
        }

        /// <summary>
        /// Gets or sets the NPC entry ID.
        /// </summary>
        public int Entry { get; set; }

        /// <summary>
        /// Gets or sets the NPC name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the NPC title.
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Gets or sets the X coordinate.
        /// </summary>
        public float X { get; set; }

        /// <summary>
        /// Gets or sets the Y coordinate.
        /// </summary>
        public float Y { get; set; }

        /// <summary>
        /// Gets or sets the Z coordinate.
        /// </summary>
        public float Z { get; set; }

        /// <summary>
        /// Gets or sets the NPC flags.
        /// </summary>
        public uint NpcFlags { get; set; }

        /// <summary>
        /// Gets or sets the faction ID.
        /// </summary>
        public uint Faction { get; set; }

        /// <summary>
        /// Gets or sets the map ID.
        /// </summary>
        public int MapId { get; set; }

        /// <summary>
        /// Gets or sets the trainer type.
        /// </summary>
        public int TrainerType { get; set; }

        /// <summary>
        /// Gets or sets the trainer class.
        /// </summary>
        public int TrainerClass { get; set; }

        /// <summary>
        /// Gets the location as a WoWPoint.
        /// </summary>
        public WoWPoint Location => new WoWPoint(X, Y, Z);

        public bool IsHostile
        {
            get
            {
                if (Faction == 0) return false;
                var myTemplate = StyxWoW.Me?.FactionTemplate;
                if (myTemplate == null) return false;
                var npcTemplate = WoWFactionTemplate.FromId(Faction);
                if (npcTemplate == null) return false;
                return myTemplate.GetReactionTowards(npcTemplate) < WoWUnitReaction.Neutral;
            }
        }

        public bool Equals(NpcResult other)
        {
            return other != null &&
                   Entry == other.Entry &&
                   MapId == other.MapId &&
                   X.Equals(other.X) &&
                   Y.Equals(other.Y) &&
                   Z.Equals(other.Z);
        }

        public override bool Equals(object obj) => Equals(obj as NpcResult);

        public override int GetHashCode() => System.HashCode.Combine(Entry, MapId, X, Y, Z);
    }
}
