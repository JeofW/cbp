using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CommonBehaviors.Actions;
using Styx;
using Styx.Helpers;
using Styx.Logic;
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
    class BlackfathomDeeps : Dungeon
    {
        #region Overrides of Dungeon
        public override uint DungeonId { get { return 10; } }

        public override WoWPoint Entrance { get { return new WoWPoint(4247.842, 750.5999, -22.39564); } }
        public override WoWPoint ExitLocation { get { return new WoWPoint(-150.17, 113.01, -40.54); } }

        public override void RemoveTargetsFilter(List<WoWObject> units)
        {
            units.RemoveAll(
                ret =>
                {
                    var unit = ret.ToUnit();
                    if (unit != null)
                    {
                        if (!unit.Combat && (StyxWoW.Me.IsSwimming || !StyxWoW.Me.IsSwimming && unit.Z < -58))
                            return true;
                        if (unit.Entry == SkitteringCrustaceanId && !unit.Combat)
                            return true;
                    }
                    return false;
                });
        }

        #endregion

        private const uint SkitteringCrustaceanId = 4821;

        #region Encounter Handlers

        [EncounterHandler(0)]
        public Composite RootLogic()
        {
            return
                new PrioritySelector(
                    // don't drown..
                    new Decorator(
                        ctx =>
                        StyxWoW.Me.GetMirrorTimerInfo(MirrorTimerType.Breath).IsVisible && StyxWoW.Me.GetMirrorTimerInfo(MirrorTimerType.Breath).CurrentTime < 15000 &&
                        !StyxWoW.Me.MovementInfo.IsAscending,
                        new Action(ctx => WoWMovement.Move(WoWMovement.MovementDirection.JumpAscend))),
                    new Decorator(
                        ctx => !StyxWoW.Me.GetMirrorTimerInfo(MirrorTimerType.Breath).IsVisible && StyxWoW.Me.MovementInfo.IsAscending,
                        new Action(ctx => WoWMovement.MoveStop(WoWMovement.MovementDirection.JumpAscend))),
                     ScriptHelpers.CreateForceJump(nat => StyxWoW.Me.Location.Distance(new WoWPoint(-360.4622f, 35.82073f, -53.28525f)) < 3, true, (new WoWPoint(-360.4622f, 35.82073f, -53.28525f)), (new WoWPoint(-354.1001f, 35.91189f, -53.12907f))),
                     ScriptHelpers.CreateForceJump(nat => StyxWoW.Me.Location.Distance(new WoWPoint(-337.5258f, 43.51491f, -53.12798f)) < 3, true, (new WoWPoint(-337.5258f, 43.51491f, -53.12798f)), (new WoWPoint(-334.1969f, 48.62948f, -53.12798f))),
                     ScriptHelpers.CreateForceJump(nat => StyxWoW.Me.Location.Distance(new WoWPoint(-329.5626f, 49.79887f, -53.12798f)) < 3, true, (new WoWPoint(-329.5626f, 49.79887f, -53.12798f)), (new WoWPoint(-322.9993f, 49.80068f, -53.12935f))),
                     ScriptHelpers.CreateForceJump(nat => StyxWoW.Me.Location.Distance(new WoWPoint(-314.7013f, 62.1413f, -53.12996f)) < 3, true, (new WoWPoint(-314.7013f, 62.1413f, -53.12996f)), (new WoWPoint(-314.6349f, 68.79098f, -53.5784f))));

        }

        [ObjectHandler(21118, "Fire of Aku'mai")]
        [ObjectHandler(21119, "Fire of Aku'mai")]
        [ObjectHandler(21120, "Fire of Aku'mai")]
        [ObjectHandler(21121, "Fire of Aku'mai")]
        public Composite FireofAkumaiHandler()
        {
            WoWGameObject obj = null;
            var activationTimer = new WaitTimer(TimeSpan.FromSeconds(10));

            return new PrioritySelector(
                ctx => obj = ctx as WoWGameObject,
                new Decorator(
                    ctx => StyxWoW.Me.IsTank() && Targeting.Instance.IsEmpty() && obj.State == WoWGameObjectState.Ready,
                    new PrioritySelector(
                        new Decorator(ctx => !obj.WithinInteractRange && activationTimer.IsFinished, new Action(ctx => Navigator.MoveTo(obj.Location))),
                        new Decorator(
                            ctx => obj.WithinInteractRange && activationTimer.IsFinished,
                            new Sequence(new Action(ctx => obj.Interact()), new Action(ctx => activationTimer.Reset()))))),
                new Decorator(ctx => !activationTimer.IsFinished && Targeting.Instance.IsEmpty(), new ActionAlwaysSucceed()));
        }

        #endregion
    }
}
