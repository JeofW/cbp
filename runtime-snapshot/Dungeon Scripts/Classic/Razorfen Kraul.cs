using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Styx.Logic.Pathing;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

#if USE_DUNGEONBUDDY_DLL
using Bots.DungeonBuddyDll;
using Bots.DungeonBuddyDll.Profiles;
using Bots.DungeonBuddyDll.Attributes;
using Bots.DungeonBuddyDll.Helpers;
namespace Bots.DungeonBuddyDll.Dungeon_Scripts.Classic
#else
    using Bots.DungeonBuddy.Profiles;
    using Bots.DungeonBuddy.Attributes;
    using Bots.DungeonBuddy.Helpers;
namespace Bots.DungeonBuddy.Dungeon_Scripts.Classic
#endif
{
    public class RazorfenKraul:Dungeon
    {
        #region Overrides of Dungeon

        public override uint DungeonId
        {
            get { return 16; }
        }

        public override WoWPoint Entrance { get { return new WoWPoint(-4456.700, -1655.990, 86.109); } }
        public override WoWPoint ExitLocation { get { return new WoWPoint(1942.270, 1544.230, 83.305); } }

        #endregion

        [EncounterHandler(6168, "Roogug")]
        public Composite RoogugEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }

        [EncounterHandler(4424, "Aggem Thorncurse")]
        public Composite AggemThorncurseEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }

        [EncounterHandler(4428, "Death Speaker Jargba")]
        public Composite DeathSpeakerJargbaEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }

        [EncounterHandler(4420, "Overlord Ramtusk")]
        public Composite OverlordRamtuskEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }

        [EncounterHandler(4422, "Agathelos the Raging")]
        public Composite AgathelosTheRagingEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }

        [EncounterHandler(4421, "Charlga Razorflank")]
        public Composite CharlgaRazorflankEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }
    }
}
