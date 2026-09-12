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
    using Bots.DungeonBuddy;
    using Bots.DungeonBuddy.Profiles;
    using Bots.DungeonBuddy.Attributes;
    using Bots.DungeonBuddy.Helpers;
    namespace Bots.DungeonBuddy.Dungeon_Scripts.Classic
#endif
{
    public class LowerBlackrockSpire:Dungeon
    {
        #region Overrides of Dungeon
        public override uint DungeonId
        {
            get { return 32; }
        }

        public override WoWPoint Entrance { get { return new WoWPoint(-7522.93, -1232.999, 285.74); } }
        public override WoWPoint ExitLocation { get { return new WoWPoint(77.55, -223.18, 49.84); } }

        #endregion

        [EncounterHandler(9196, "Highlord Omokk")]
        public Composite HighlordOmokkEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }

        [EncounterHandler(9236, "Shadow Hunter Vosh'gajin")]
        public Composite ShadowHunterVoshgajinEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }

        [EncounterHandler(9237, "War Master Voone")]
        public Composite WarMasterVooneEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }

        [EncounterHandler(10596, "Mother Smolderweb")]
        public Composite MotherSmolderwebEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }

        [EncounterHandler(10584, "Urok Doomhowl")]
        public Composite UrokDoomhowlEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }

        [EncounterHandler(9736, "Quartermaster Zigris")]
        public Composite QuartermasterZigrisEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }

        [EncounterHandler(10220, "Halycon")]
        public Composite HalyconEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }

        [EncounterHandler(10268, "Gizrul the Slavener")]
        public Composite GizrulTheSlavenerEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }

        [EncounterHandler(9568, "Overlord Wyrmthalak")]
        public Composite OverlordWyrmthalakEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }
    }
}
