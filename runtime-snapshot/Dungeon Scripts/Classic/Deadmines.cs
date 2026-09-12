using System;
using System.Linq;
using System.Threading;

using CommonBehaviors.Actions;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.POI;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using TreeSharp;
using Styx;
using Styx.WoWInternals.WoWObjects;
using System.Collections.Generic;
using Action = TreeSharp.Action;

#if USE_DUNGEONBUDDY_DLL
using Bots.DungeonBuddyDll;
using Bots.DungeonBuddyDll.Attributes;
using Bots.DungeonBuddyDll.Profiles;
using Bots.DungeonBuddyDll.Helpers;
namespace Bots.DungeonBuddyDll.Dungeon_Scripts.Classic
#else
    using Bots.DungeonBuddy.Attributes;
    using Bots.DungeonBuddy.Helpers;
    using Bots.DungeonBuddy.Profiles;
namespace Bots.DungeonBuddy.Dungeon_Scripts.Classic
#endif
{
    public class TheDeadmines : Dungeon
    {
        #region Overrides of Dungeon

        /// <summary> The mapid of this dungeon. </summary>
        /// <value>The map identifier.</value>
        public override uint DungeonId
        {
            get { return 6; }
        }

        public override WoWPoint Entrance { get { return new WoWPoint(-11208.21, 1680.011, 23.94507); } }
        public override WoWPoint ExitLocation { get { return new WoWPoint(-14.68, -388.10, 63.06); } }

        #endregion

        public override void IncludeTargetsFilter(List<WoWObject> incomingunits, HashSet<WoWObject> outgoingunits)
        {
            base.IncludeTargetsFilter(incomingunits, outgoingunits);

            foreach (var unit in incomingunits.Select(obj => obj.ToUnit()))
            {
                if (StyxWoW.Me.Combat)
                {
                    //Force it to attack Sneed when the shredder ejects him, and VanCleef's summoned Blackguards
                    if ((unit.DistanceSqr < 40 * 40 && !unit.IsTargetingMyPartyMember) && (unit.Entry == 643 || unit.Entry == 636))
                    {
                        outgoingunits.Add(unit);
                    }
                }

            }
        }

        public override void WeighTargetsFilter(List<Targeting.TargetPriority> units)
        {
            foreach (var t in units)
            {
                var prioObject = t.Object;

                //Target good ones first
                if (prioObject.Entry == 636)
                {
                    t.Score += 400;
                }
                //Sneed pops out of the dying shredder
                if (prioObject.Entry == 643)
                {
                    t.Score += 400;
                }
            }
        }

        public override void OnEnter()
        {
            LandedInWater = false;
        }

        #region Encounter Handlers

        public bool LandedInWater;

        private WoWGameObject Cannon
        {
            get
            {
                return ObjectManager.
                    GetObjectsOfType<
                        WoWGameObject>().
                    FirstOrDefault(
                        x =>
                        x.
                            Entry == 16398);
            }
        }


        private WoWGameObject Door
        {
            get
            {
                return ObjectManager.
                    GetObjectsOfType<
                        WoWGameObject>().
                    FirstOrDefault(
                        x =>
                        x.
                            Entry == 16397);
            }
        }

        private WoWGameObject Gunpowder
        {
            get
            {
                return ObjectManager.
                    GetObjectsOfType<
                        WoWGameObject>().
                    FirstOrDefault(
                        x =>
                        x.
                            Entry == 17155);
            }
        }

        private bool HasGunpowder
        {
            get { return StyxWoW.Me.CarriedItems.Any(i => i.Entry == 5397); }
        }

        [EncounterHandler(0)]
        public Composite RootLogic()
        {
            WoWUnit bestTarget = null;

            return
                new PrioritySelector(

                     // Water Handler for falling in

                     new Decorator(nat => StyxWoW.Me.IsSwimming,
                         new PrioritySelector(
                             new Decorator(nat => !LandedInWater,
                                 new Sequence(
                                     new Action(nat => LandedInWater = true),
                                     new Action(nat => Logging.Write("We landed in the water! Moving back to dock entrance.")))),

                              new Decorator(nat => StyxWoW.Me.Location.Distance(new WoWPoint(-81.09593, -695.2491, 0.0303379)) > 3,
                                     new Action(nat => WoWMovement.ClickToMove(new WoWPoint(-81.09593, -695.2491, 0.0303379)))))),

                     new Decorator(nat => !StyxWoW.Me.IsSwimming && LandedInWater && StyxWoW.Me.Location.Distance(new WoWPoint(-106.4652, -685.4393, 6.44209)) > 3,
                               new Action(nat => Navigator.MoveTo(new WoWPoint(-106.4652, -685.4393, 6.44209)))),

                     new Decorator(nat => !StyxWoW.Me.IsSwimming && LandedInWater && StyxWoW.Me.Location.Distance(new WoWPoint(-106.4652, -685.4393, 6.44209)) < 3,
                         new Sequence(
                               new Action(nat => LandedInWater = false),
                               new Action(nat => WoWMovement.ClickToMove(new WoWPoint(-102.3294, -684.3071, 7.425116))),
                               new Action(nat => Thread.Sleep(1000)))),

            //Safety Check for falling onto the ship side

                     new Decorator(nat => StyxWoW.Me.Location.Distance(new WoWPoint(-54.77596, -796.0432, 33.53532)) < 4,
                                     new Action(nat => WoWMovement.ClickToMove(new WoWPoint(-61.75788, -782.5929, 17.87629))))

                    );
        }

        [EncounterHandler(644, "Rhahk'Zor")]
        public Composite RhahkZorFight()
        {

            return new PrioritySelector(
                    ScriptHelpers.CreateTankFaceAwayGroupUnit(10));
        }

        [EncounterHandler(642, "Sneed's Shredder")]
        public Composite SneedsShredderFight()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }

        [EncounterHandler(643, "Sneed", Mode = CallBehaviorMode.CurrentBoss)]
        public Composite SneedFight()
        {
            var mastRoomCenter = new WoWPoint(-289.5, -513.0, 49.7);
            WoWUnit boss = null;

            return new PrioritySelector(ctx => boss = ctx as WoWUnit,
                // handle Sneed being ejected from the dying shredder.
                                        new Decorator(
                                            ctx => boss == null && StyxWoW.Me.Location.DistanceSqr(mastRoomCenter) < 30 * 30 && Targeting.Instance.FirstUnit == null,
                                            new Sequence(
                                                new WaitContinue(20, ctx => Targeting.Instance.FirstUnit != null,
                                                                 new Action(ret => Navigator.MoveTo(mastRoomCenter))),
                                                new DecoratorContinue(
                                                    ctx =>
                                                    Targeting.Instance.FirstUnit == null &&
                                                    !ObjectManager.GetObjectsOfType<WoWUnit>().Any(u => u.Entry == 642 || u.Entry == 643),
                                                    new Action(ctx => BossManager.CurrentBoss.MarkAsDead()))
                                                ))
                );
        }

        [EncounterHandler(622, Mode = CallBehaviorMode.Proximity)]
        public Composite GoblinFoundry() { return ScriptHelpers.CreateClearArea(()=>new WoWPoint(-209.3573,-567.5178,20.97694),50,u=>u.Entry == 622 || u.Entry == 1731); }

        [EncounterHandler(1763, "Gilnid")]
        public Composite GilnidFight()
        {
            WoWUnit bestTarget = null;
            return new PrioritySelector(
                    ScriptHelpers.CreateTankFaceAwayGroupUnit(10));
        }

        [ObjectHandler(17155, "Defias Gunpowder", ObjectRange = 30)]
        public Composite DefiasGunpowderHandler()
        {
            return new Decorator(r => Gunpowder != null && StyxWoW.Me.IsTank() && !StyxWoW.Me.Combat && !HasGunpowder && (Door == null || Door.State != WoWGameObjectState.ActiveAlternative),
                        new PrioritySelector(
                            new Decorator(r => Gunpowder.Distance > 5,
                                new Action(z => Navigator.MoveTo(Gunpowder.Location))),
                            new Action(z => Gunpowder.Interact())));
        }

        [ObjectHandler(16398, "Defias Cannon", ObjectRange = 30)]
        public Composite DefiasCannonHandler()
        {
            return new Decorator(r=>Cannon != null && Door != null && StyxWoW.Me.IsTank() && !StyxWoW.Me.Combat && HasGunpowder && Cannon.State == WoWGameObjectState.Ready && Door.State != WoWGameObjectState.ActiveAlternative,
                        new Action(z=>Cannon.Interact()));
        }

        [EncounterHandler(646, "Mr. Smite")]
        public Composite MrSmiteFight()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }

        [EncounterHandler(647, "Captain Greenskin")]
        public Composite CaptainGreenskinFight()
        {
            return new PrioritySelector(
                    ScriptHelpers.CreateTankFaceAwayGroupUnit(10));
        }

        [EncounterHandler(639, "Edwin VanCleef", Mode = CallBehaviorMode.CurrentBoss)]
        public Composite EdwinVanCleefFight()
        {
            WoWUnit boss = null;

            return new PrioritySelector(ctx => boss = ctx as WoWUnit,
                                    new Decorator(ctx => boss != null,
                                        ScriptHelpers.CreateTankFaceAwayGroupUnit(10))
                                    );
        }

        [EncounterHandler(645, "Cookie")]
        public Composite CookieFight()
        {
            WoWUnit boss = null;
            return new PrioritySelector(
                ctx => boss = ctx as WoWUnit
                );
        }


        #endregion
    }
}
