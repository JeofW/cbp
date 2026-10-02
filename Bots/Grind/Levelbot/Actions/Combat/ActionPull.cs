using System;
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
    public class ActionPull : TreeSharp.Action
    {
        private static LocalPlayer? Me => ObjectManager.Me;
        private readonly MountedCombatTransition _transition = new();

        protected override RunStatus Run(object context)
        {
            LocalPlayer actor = Me;
            WoWUnit target = actor?.CurrentTarget;
            if (actor == null || target == null)
                return RunStatus.Failure;
            string targetName = target.Name;
            // Preserve the legacy cleanup before acquiring movement ownership.
            if (target.Dead)
            {
                Blacklist.Add(actor.CurrentTargetGuid, TimeSpan.FromMinutes(5));
                actor.ClearTarget();
                return RunStatus.Failure;
            }
            if (target.TaggedByOther && !target.TaggedByMe && !actor.IsInParty && !actor.IsInRaid)
            {
                Logging.Write("{0} is tagged", targetName);
                Blacklist.Add(actor.CurrentTargetGuid, TimeSpan.FromMinutes(5));
                actor.ClearTarget();
                return RunStatus.Failure;
            }
            ulong actorGuid = actor.Guid, targetGuid = target.Guid;
            uint actorAddress = actor.BaseAddress, targetAddress = target.BaseAddress, map = actor.MapId;
            var poi = Styx.Logic.POI.BotPoi.Current;
            long poiWorkGeneration = Styx.Logic.POI.BotPoi.CurrentWorkGeneration;
            var poiType = poi.Type;
            ulong poiGuid = poi.Guid;
            uint poiEntry = poi.Entry;
            bool Current() => actorGuid != 0 && targetGuid != 0 && actorAddress != 0 && targetAddress != 0
                && ReferenceEquals(Me, actor) && actor.IsValid && actor.IsAlive && actor.Guid == actorGuid
                && actor.BaseAddress == actorAddress && actor.MapId == map
                && ReferenceEquals(actor.CurrentTarget, target) && actor.CurrentTargetGuid == targetGuid
                && target.IsValid && target.IsAlive && target.Guid == targetGuid && target.BaseAddress == targetAddress
                && ReferenceEquals(Styx.Logic.POI.BotPoi.Current, poi) && Styx.Logic.POI.BotPoi.CurrentWorkGeneration == poiWorkGeneration
                && poi.Type == poiType && poi.Guid == poiGuid && poi.Entry == poiEntry;

            GroundTransitionState transition = _transition.TickExplicit(target, Current);
            if (transition is GroundTransitionState.Pending or GroundTransitionState.Unavailable)
                return RunStatus.Running;
            if (transition != GroundTransitionState.Ready || !Current())
                return RunStatus.Failure;
            var lease = MountedCombatTransition.CaptureActionLease(target, Current);
            if (lease?.Current != true) return RunStatus.Failure;

            // If in combat, pull is done
            if (actor.Combat)
                return RunStatus.Success;

            // Apply pre-pull buffs if needed
            if (RoutineManager.Current.NeedPullBuffs)
            {
                TreeRoot.StatusText = "Pre-pull buffs";
                RoutineManager.Current.PullBuff();
                if (lease.Current != true) return RunStatus.Failure;
            }

            // Set status and pull
            if (!target.IsPlayer)
            {
                TreeRoot.StatusText = string.Format("Pulling {0} now...", targetName);
            }
            else
            {
                TreeRoot.StatusText = string.Format("Pulling level {0} {1} now...", target.Level, target.Class);
            }

            RoutineManager.Current.Pull();
            return lease.Current ? RunStatus.Success : RunStatus.Failure;
        }

        public override void Stop(object context)
        {
            _transition.Cancel();
            base.Stop(context);
        }
    }
}
