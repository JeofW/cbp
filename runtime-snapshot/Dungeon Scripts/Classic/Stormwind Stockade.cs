using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CommonBehaviors.Actions;
using Styx;
using Styx.Helpers;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
using Action = TreeSharp.Action;

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
    class StormwindStockade : Dungeon
    {
        #region Overrides of Dungeon
        public override uint DungeonId
        {
            get { return 12; }
        }

        public override WoWPoint Entrance { get { return new WoWPoint(-8763.035,847.2657,86.97466); } }
        public override WoWPoint ExitLocation { get { return new WoWPoint(44.81386f, 0.273018f, -20.83632f); } }

        #endregion

        [EncounterHandler(1696, "Targorr the Dread")]
        public Composite TargorrTheDreadEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }

        [EncounterHandler(1666, "Kam Deepfury")]
        public Composite KamDeepfuryEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }

        [EncounterHandler(1717, "Hamhock")]
        public Composite HamhockEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }

        [EncounterHandler(1663, "Dextren Ward")]
        public Composite DextrenWardEncounter()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }

        [EncounterHandler(1716, "Bazil Thredd", Mode =  CallBehaviorMode.CurrentBoss)]
        public Composite BazilThreddEncounter()
        {
            var bossRoom = new WoWPoint(89.55, -136.92, -33.94);

            WoWUnit boss = null;
            return new PrioritySelector(ctx => boss = ctx as WoWUnit,
                new Decorator(ctx =>
                    {
                        List<WoWUnit> listOfHostileMob = ObjectManager.GetObjectsOfType<WoWUnit>()
                                                 .Where(ret => ret.IsAlive && ret.IsHostile && ret.Location.Distance(bossRoom) < 15)
                                                 .OrderBy(u => u.Distance)
                                                 .ToList();
                        return boss == null && ScriptHelpers.IsBossAlive("Bazil Thredd") && StyxWoW.Me.Location.DistanceSqr(bossRoom) < 40 * 40 && ScriptHelpers.Tank == StyxWoW.Me && listOfHostileMob.Count == 0;
                    },
                    new Sequence( // handle flee for assist at low health.
                        new WaitContinue(7,ctx => StyxWoW.Me.IsActuallyInCombat,new ActionAlwaysSucceed()),
                        new DecoratorContinue(ctx => !ObjectManager.GetObjectsOfType<WoWUnit>().Any(u => u.Entry == 1716),
                            new Action(ctx => Navigator.MoveTo(bossRoom)))
                        ))
                );
        }
    }
}
