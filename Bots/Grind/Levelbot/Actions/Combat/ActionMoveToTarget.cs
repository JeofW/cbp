using System;
using CommonBehaviors.Actions;
using Styx;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

namespace Levelbot.Actions.Combat
{
    public class ActionMoveToTarget : NavigationAction
    {
        private int _moveStartTime;
        private LocalPlayer _moveActor;
        private WoWUnit _moveTarget;
        private ulong _moveActorGuid, _moveTargetGuid;
        private uint _moveMap;
        private object _moveProvider;
        private object _moveOwner = new object();

        public ActionMoveToTarget()
        {
            BotEvents.Player.OnMobKilled += OnMobKilled;
        }

        private void OnMobKilled(BotEvents.Player.MobKilledEventArgs args)
        {
            ResetChase();
        }

        private void ResetChase()
        {
            _moveStartTime = 0;
            _moveActor = null;
            _moveTarget = null;
            _moveOwner = new object();
        }

        internal static bool CanPursueOnGround(LocalPlayer actor)
        {
            // A ground path result says nothing about reachability from mid-air.
            // Flight/landing owns that transition; neither its duration nor an
            // unavailable movement observation can spend this chase's blacklist.
            return actor != null && actor.IsValid && actor.IsAlive
                && actor.TryGetMovementState(out uint flags, out ulong transport)
                && (flags & 0x02C03000u) == 0 && transport == 0;
        }

        protected override RunStatus Run(object context)
        {
            LocalPlayer actor = StyxWoW.Me;
            WoWUnit target = Targeting.Instance.FirstUnit;
            ulong actorGuid = actor?.Guid ?? 0, targetGuid = target?.Guid ?? 0;
            if (actorGuid == 0 || targetGuid == 0 || actor == null || target == null
                || !actor.IsValid || !actor.IsAlive || !target.IsValid || !target.IsAlive
                || actor.IsCasting || actor.ChanneledCastingSpellId != 0 || !CanPursueOnGround(actor))
            {
                // A paused/absent actor cannot spend another chase's timeout.
                ResetChase();
                return RunStatus.Failure;
            }

            uint map = actor.MapId;
            object provider = Navigator.NavigationProvider;
            if (!ReferenceEquals(_moveActor, actor) || _moveActorGuid != actorGuid
                || !ReferenceEquals(_moveTarget, target) || _moveTargetGuid != targetGuid
                || _moveMap != map || !ReferenceEquals(_moveProvider, provider))
            {
                _moveActor = actor; _moveActorGuid = actorGuid;
                _moveTarget = target; _moveTargetGuid = targetGuid;
                _moveMap = map; _moveProvider = provider;
                _moveStartTime = Environment.TickCount;
                _moveOwner = new object();
            }
            object owner = _moveOwner;
            WoWUnit displayed = actor.CurrentTarget;
            ulong displayedGuid = displayed?.Guid ?? 0;
            bool Current() => ReferenceEquals(owner, _moveOwner) && ReferenceEquals(actor, StyxWoW.Me)
                && actor.IsValid && actor.IsAlive && actor.Guid == actorGuid && actor.MapId == map
                && !actor.IsCasting && actor.ChanneledCastingSpellId == 0 && CanPursueOnGround(actor)
                && target.IsValid && target.IsAlive && target.Guid == targetGuid
                && ReferenceEquals(Targeting.Instance.FirstUnit, target)
                && ReferenceEquals(provider, Navigator.NavigationProvider)
                && ReferenceEquals(actor.CurrentTarget, displayed) && actor.CurrentTargetGuid == displayedGuid;
            void ResetIfOwned() { if (ReferenceEquals(owner, _moveOwner)) ResetChase(); }
            RunStatus Invalidated() { ResetIfOwned(); return RunStatus.Failure; }
            if (!Current()) return Invalidated();

            RunStatus Reject(string reason)
            {
                TimeSpan duration = target is WoWPlayer ? TimeSpan.FromSeconds(45) : TimeSpan.FromMinutes(10);
                if (!Current() || !Blacklist.AddIfCurrent(targetGuid, duration, Current)) return Invalidated();
                // A failed approach is not authority to clear a manual/replacement target.
                if (Current() && ReferenceEquals(displayed, target) && actor.CurrentTargetGuid == targetGuid && Current())
                    actor.ClearTarget();
                ResetIfOwned();
                Logging.Write(reason, target.Name);
                return RunStatus.Failure;
            }

            // Timeout check - 45 seconds trying to reach target (HB 3.3.5a value)
            if (Environment.TickCount - _moveStartTime >= 45000)
            {
                return Reject("Tried to move to {0} for 45 seconds, blacklisting.");
            }

            WoWPoint destination = target.Location;
            WoWPoint from = actor.Location;
            double pullDistance = Targeting.PullDistance;
            bool DestinationCurrent() => Current() && target.Location == destination && Current();
            if (!IsFinitePoint(from) || !IsFinitePoint(destination) || double.IsNaN(pullDistance)
                || double.IsInfinity(pullDistance) || pullDistance < 0 || !DestinationCurrent()) return Invalidated();

            // HB 3.3.5a: If within PullDistance and line of sight, we're done
            if (destination.Distance(from) <= pullDistance && target.InLineOfSpellSight)
            {
                if (!DestinationCurrent()) return Invalidated();
                Navigator.Clear();
                bool current = DestinationCurrent();
                ResetIfOwned();
                return current ? RunStatus.Success : RunStatus.Failure;
            }

            // Generate path and check if reachable
            if (!DestinationCurrent()) return Invalidated();
            WoWPoint[] path = Navigator.GeneratePath(from, destination);
            if (!DestinationCurrent()) return Invalidated();
            bool finitePath = path != null && path.Length > 0;
            if (finitePath)
                foreach (WoWPoint point in path)
                    if (!IsFinitePoint(point)) { finitePath = false; break; }

            if (finitePath && IsPathEndCloseToTarget(path[path.Length - 1], destination))
            {
                if (target.Type == WoWObjectType.Player)
                {
                    TreeRoot.StatusText = string.Format("Moving towards level {0} {1} {2}",
                        target.Level, target.Race, target.Class);
                }
                else
                {
                    TreeRoot.StatusText = "Moving towards " + target.Name;
                }

                if (!DestinationCurrent()) return Invalidated();
                MoveResult result = Navigator.MoveTo(destination);
                return DestinationCurrent() ? Navigator.GetRunStatusFromMoveResult(result) : Invalidated();
            }

            // Cannot generate path - blacklist for 10 minutes (HB 3.3.5a value)
            if (!DestinationCurrent()) return Invalidated();
            return Reject("MoveToTarget: Could not generate path to target {0}, blacklisting.");
        }

        private static bool IsPathEndCloseToTarget(WoWPoint pathEnd, WoWPoint targetLocation)
        {
            float precision = Navigator.PathPrecision;
            if (!IsFinitePoint(pathEnd) || !IsFinitePoint(targetLocation) || float.IsNaN(precision)
                || float.IsInfinity(precision) || precision < 0)
                return false;
            if (pathEnd.Distance2DSqr(targetLocation) > precision * precision)
                return false;

            return Math.Abs(pathEnd.Z - targetLocation.Z) < 3f;
        }

        private static bool IsFinitePoint(WoWPoint point) =>
            !float.IsNaN(point.X) && !float.IsInfinity(point.X)
            && !float.IsNaN(point.Y) && !float.IsInfinity(point.Y)
            && !float.IsNaN(point.Z) && !float.IsInfinity(point.Z);
    }
}
