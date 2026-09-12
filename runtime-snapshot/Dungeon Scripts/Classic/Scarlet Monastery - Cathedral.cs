using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Styx;
using Styx.Logic;
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
    public class ScarletMonasteryCathedral : Dungeon
    {
        #region Overrides of Dungeon

        public override uint DungeonId
        {
            get { return 164; }
        }

        public override WoWPoint Entrance { get { return new WoWPoint(2919.21, -821.85, 160.3331); } }
        public override WoWPoint ExitLocation { get { return new WoWPoint(853.05, 1314.95, 18.68); } }

        public override void WeighTargetsFilter(List<Targeting.TargetPriority> units)
        {
            foreach (var priority in units)
            {
                var unit = priority.Object as WoWUnit;
                if (unit != null)
                {
                    if (unit.Entry == 3977 && StyxWoW.Me.IsDps()) // High Inquisitor Whitemane
                        priority.Score += 200;
                }
            }
        }

        public override void RemoveTargetsFilter(List<WoWObject> units) { units.RemoveAll(unit => unit is WoWPlayer); }

        #endregion

        [EncounterHandler(4542, "High Inquisitor Fairbanks")]
        public Composite HighInquisitorFairbanksEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }

        [EncounterHandler(3976, "Scarlet Commander Mograine")]
        public Composite ScarletCommanderMograineEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }

        [EncounterHandler(3977, "High Inquisitor Whitemane")]
        public Composite HighInquisitorWhitemaneEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }
    }
}
