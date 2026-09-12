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
    public class Scholomance:Dungeon
    {
        #region Overrides of Dungeon
        public override uint DungeonId
        {
            get { return 2; }
        }

        public override WoWPoint Entrance { get { return new WoWPoint(1279.48, -2551.52,87.41); } }
        public override WoWPoint ExitLocation { get { return new WoWPoint(188.05, 126.49, 138.82); } }

        #endregion

        [EncounterHandler(10506, "Kirtonos the Herald")]
        public Composite KirtonosTheHeraldEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }

        [EncounterHandler(10503, "Jandice Barov")]
        public Composite JandiceBarovEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }

        [EncounterHandler(11622, "Rattlegore")]
        public Composite RattlegoreEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }

        [EncounterHandler(10433, "Marduk Blackpool")]
        public Composite MardukBlackpoolEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }

        [EncounterHandler(10432, "Vectus")]
        public Composite VectusEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }

        [EncounterHandler(10508, "Ras Frostwhisper")]
        public Composite RasFrostwhisperEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }

        [EncounterHandler(10505, "Instructor Malicia")]
        public Composite InstructorMaliciaEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }

        [EncounterHandler(11261, "Doctor Theolen Krastinov")]
        public Composite DoctorTheolenKrastinovEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }

        [EncounterHandler(10901, "Lorekeeper Polkelt")]
        public Composite LorekeeperPolkeltEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }

        [EncounterHandler(10507, "The Ravenian")]
        public Composite TheRavenianEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }

        [EncounterHandler(10504, "Lord Alexei Barov")]
        public Composite LordAlexeiBarovEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }

        [EncounterHandler(10502, "Lady Illucia Barov")]
        public Composite LadyIlluciaBarovEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }

        [EncounterHandler(1853, "Darkmaster Gandling")]
        public Composite DarkmasterGandlingEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }
    }
}
