using Styx;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Combat;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

namespace Levelbot.Actions.Combat
{
    public class ActionSetTarget : TreeSharp.Action
    {
        private readonly MountedCombatTransition _transition = new();

        protected override RunStatus Run(object context)
        {
            LocalPlayer me = StyxWoW.Me;
            if (me == null)
                return RunStatus.Failure;

            // Clear dead targets
            if (me.GotTarget && me.CurrentTarget != null && me.CurrentTarget.Dead)
            {
                me.ClearTarget();
                return RunStatus.Success;
            }

            WoWUnit firstUnit = Targeting.Instance.FirstUnit;
            if (firstUnit != null)
            {
                var targeting = Targeting.Instance;
                ulong actorGuid = me.Guid, targetGuid = firstUnit.Guid;
                uint actorAddress = me.BaseAddress, targetAddress = firstUnit.BaseAddress, map = me.MapId;
                var poi = Styx.Logic.POI.BotPoi.Current;
                long poiWorkGeneration = Styx.Logic.POI.BotPoi.CurrentWorkGeneration;
                var poiType = poi.Type;
                ulong poiGuid = poi.Guid;
                uint poiEntry = poi.Entry;
                bool Current(bool requireDisplayed) => actorGuid != 0 && targetGuid != 0 && actorAddress != 0 && targetAddress != 0
                    && ReferenceEquals(StyxWoW.Me, me) && me.IsValid && me.IsAlive && me.Guid == actorGuid
                    && me.BaseAddress == actorAddress && me.MapId == map
                    && ReferenceEquals(Targeting.Instance, targeting) && ReferenceEquals(targeting.FirstUnit, firstUnit)
                    && firstUnit.IsValid && firstUnit.IsAlive && firstUnit.Guid == targetGuid && firstUnit.BaseAddress == targetAddress
                    && (!requireDisplayed || ReferenceEquals(me.CurrentTarget, firstUnit) && me.CurrentTargetGuid == targetGuid)
                    && ReferenceEquals(Styx.Logic.POI.BotPoi.Current, poi) && Styx.Logic.POI.BotPoi.CurrentWorkGeneration == poiWorkGeneration
                    && poi.Type == poiType && poi.Guid == poiGuid && poi.Entry == poiEntry;

                if (!Current(false)) return RunStatus.Failure;
                if (!ReferenceEquals(me.CurrentTarget, firstUnit) || me.CurrentTargetGuid != targetGuid)
                    firstUnit.Target();
                if (!Current(true)) return RunStatus.Failure;

                GroundTransitionState transition = _transition.TickExplicit(firstUnit, () => Current(true));
                if (transition is GroundTransitionState.Pending or GroundTransitionState.Unavailable)
                    return RunStatus.Running;
                if (transition != GroundTransitionState.Ready || !Current(true))
                    return RunStatus.Failure;
                var lease = MountedCombatTransition.CaptureActionLease(firstUnit, () => Current(true));
                if (lease?.Current != true) return RunStatus.Failure;
                Navigator.Clear();
                if (lease.Current != true) return RunStatus.Failure;

                try
                {
                    if (firstUnit.IsPlayer)
                    {
                        TreeRoot.StatusText = string.Format(
                            "Setting level {0} {1} {2} at {3:F1} yards as your target",
                            firstUnit.Level, firstUnit.Race, firstUnit.Class, firstUnit.Distance);
                    }
                    else if (firstUnit.OwnedByUnit != null)
                    {
                        TreeRoot.StatusText = string.Format(
                            "Setting level {0} {1} {2}'s pet at {3:F1} yards as your target",
                            firstUnit.OwnedByUnit.Level, firstUnit.OwnedByUnit.Race,
                            firstUnit.OwnedByUnit.Class, firstUnit.Distance);
                    }
                    else
                    {
                        TreeRoot.StatusText = string.Format(
                            "Setting {0} at {1:F1} yards as your target",
                            firstUnit.Name, firstUnit.Distance);
                    }
                }
                catch
                {
                    TreeRoot.StatusText = "Setting target...";
                }
            }

            return RunStatus.Success;
        }

        public override void Stop(object context)
        {
            _transition.Cancel();
            base.Stop(context);
        }
    }
}
