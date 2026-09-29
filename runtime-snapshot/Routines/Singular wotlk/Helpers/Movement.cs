using System;
using System.Linq;
using CommonBehaviors.Actions;
using Singular.Settings;
using Singular;

using Styx;
using Styx.Helpers;
using Styx.Logic.Combat;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

using TreeSharp;

using Action = TreeSharp.Action;

namespace Singular.Helpers
{
    internal static class Movement
    {
        /// <summary>
        ///  Creates a behavior that does nothing more than check if we're in Line of Sight of the target; and if not, move towards the target.
        /// </summary>
        /// <remarks>
        ///  Created 23/5/2011
        /// </remarks>
        /// <returns>.</returns>
        public static Composite CreateMoveToLosBehavior()
        {
            return CreateMoveToLosBehavior(null, true);
        }

       

        /// <summary>
        ///   Creates the ensure movement stopped behavior. Will return RunStatus.Success if it has stopped any movement, RunStatus.Failure otherwise.
        /// </summary>
        /// <remarks>
        ///   Created 5/1/2011.
        /// </remarks>
        /// <returns>.</returns>
        public static Composite CreateEnsureMovementStoppedBehavior()
        {
            return new Action(ret =>
            {
                var player = StyxWoW.Me;
                ulong playerGuid = player?.Guid ?? 0;
                if (!IsControlOwnerCurrent(player, playerGuid) || SingularSettings.Instance.DisableAllMovement)
                    return RunStatus.Failure;
                bool moving = player.IsMoving;
                if (!moving || SingularSettings.Instance.DisableAllMovement || !IsControlOwnerCurrent(player, playerGuid))
                    return RunStatus.Failure;
                Navigator.PlayerMover.MoveStop();
                return RunStatus.Success;
            });
        }

        /// <summary>
        /// Creates behavior to stop movement when within range of target.
        /// Essential for ranged classes to stop moving when in cast range.
        /// </summary>
        public static Composite CreateEnsureMovementStoppedWithinRange(float range)
        {
            return new Action(ret =>
            {
                var player = StyxWoW.Me;
                ulong playerGuid = player?.Guid ?? 0;
                if (!float.IsFinite(range) || range < 0 || !IsControlOwnerCurrent(player, playerGuid)
                    || SingularSettings.Instance.DisableAllMovement || !player.IsMoving)
                    return RunStatus.Failure;
                var target = player.CurrentTarget;
                ulong targetGuid = target?.Guid ?? 0;
                if (!IsControlUnitCurrent(target, targetGuid) || !(target.Distance <= range) ||
                    (!target.IsMe && !target.InLineOfSpellSight))
                    return RunStatus.Failure;

                // Range alone cannot authorize stopping an approach behind a wall.
                // Sight and target observations must not stop a replacement owner.
                if (!IsControlUnitCurrent(target, targetGuid) || !(target.Distance <= range)
                    || !IsDisplayedTargetCurrent(player, target, targetGuid) || !player.IsMoving
                    || SingularSettings.Instance.DisableAllMovement || !IsControlOwnerCurrent(player, playerGuid))
                    return RunStatus.Failure;
                Navigator.PlayerMover.MoveStop();
                return RunStatus.Success;
            });
        }

        /// <summary>
        ///   Creates a behavior that does nothing more than check if we're facing the target; and if not, faces the target. (Uses a hard-coded 70degree frontal cone)
        /// </summary>
        /// <remarks>
        ///   Created 5/1/2011.
        /// </remarks>
        /// <returns>.</returns>
        public static Composite CreateFaceTargetBehavior()
        {
            return CreateFaceTargetBehavior(null, true);
        }

        public static Composite CreateFaceTargetBehavior(UnitSelectionDelegate toUnit)
        {
            return CreateFaceTargetBehavior(toUnit, false);
        }

        private static Composite CreateFaceTargetBehavior(UnitSelectionDelegate toUnit, bool requireCurrentTarget)
        {
            return new Action(ret =>
            {
                var player = StyxWoW.Me;
                ulong playerGuid = player?.Guid ?? 0;
                if (!IsControlOwnerCurrent(player, playerGuid) || (!requireCurrentTarget && toUnit == null))
                    return RunStatus.Failure;
                var unit = requireCurrentTarget ? player.CurrentTarget : toUnit(ret);
                ulong unitGuid = unit?.Guid ?? 0;
                // Ordinary cast-time facing is intentional. Channels and movement
                // still veto turning, and an explicit recipient is not the display target.
                Func<bool> canTurn = () => IsControlUnitCurrent(unit, unitGuid) && !unit.IsMe
                    && (!requireCurrentTarget || IsDisplayedTargetCurrent(player, unit, unitGuid))
                    && !player.IsMoving && player.ChanneledCastingSpellId == 0
                    && !SingularSettings.Instance.DisableAllMovement && IsControlOwnerCurrent(player, playerGuid);
                if (!canTurn() || player.IsSafelyFacing(unit, 70f) || !canTurn())
                    return RunStatus.Failure;
                unit.Face();
                if (!canTurn()) return RunStatus.Failure;
                bool facing = player.IsSafelyFacing(unit, 150f);
                if (!canTurn()) return RunStatus.Failure;
                // Preserve the existing turn acknowledgement before lower-priority casts.
                return facing ? RunStatus.Failure : RunStatus.Success;
            });
        }

        /// <summary>
        /// True when a hostile cast target needs selection or a remaining facing step before casting.
        /// Fixes multi-dot spread (e.g. Balance Moonfire on mob behind player) where CreateFaceTargetBehavior
        /// only faces CurrentTarget. Skips self, friendlies, and in-progress casts.
        /// </summary>
        public static bool NeedsOffTargetCastSetup(WoWUnit unit)
        {
            var player = StyxWoW.Me;
            ulong playerGuid = player?.Guid ?? 0;
            ulong unitGuid = unit?.Guid ?? 0;
            if (!CanSetUpCast(player, playerGuid) || !IsControlUnitCurrent(unit, unitGuid) || unit.IsMe || unit.IsFriendly)
                return false;
            var displayed = player.CurrentTarget;
            ulong displayedGuid = displayed?.Guid ?? 0;
            // Acknowledging the target switch does not also acknowledge facing.
            bool needed = displayed != unit ||
                (!SingularSettings.Instance.DisableAllMovement && !player.IsMoving &&
                 !player.IsSafelyFacing(unit, 70f));
            return needed && IsControlUnitCurrent(unit, unitGuid)
                && IsDisplayedTargetCurrent(player, displayed, displayedGuid) && CanSetUpCast(player, playerGuid);
        }

        /// <summary>
        /// Target and face a hostile off-target unit before casting. Use only when NeedsOffTargetCastSetup is true.
        /// Success = ready to cast; Failure = retry next pulse (target switch or still turning).
        /// </summary>
        public static Composite CreateEnsureTargetAndFaceBehavior(UnitSelectionDelegate toUnit)
        {
            return new Action(ret =>
            {
                var player = StyxWoW.Me;
                ulong playerGuid = player?.Guid ?? 0;
                if (toUnit == null || !CanSetUpCast(player, playerGuid))
                    return RunStatus.Failure;
                var displayed = player.CurrentTarget;
                ulong displayedGuid = displayed?.Guid ?? 0;
                var unit = toUnit(ret);
                ulong unitGuid = unit?.Guid ?? 0;
                Func<bool> isCurrent = () => IsControlUnitCurrent(unit, unitGuid)
                    && IsDisplayedTargetCurrent(player, displayed, displayedGuid) && CanSetUpCast(player, playerGuid);
                if (!isCurrent()) return RunStatus.Failure;
                if (unit.IsMe || unit.IsFriendly) return isCurrent() ? RunStatus.Success : RunStatus.Failure;

                if (displayed != unit)
                {
                    Logger.WriteDebug("Off-target cast: switching to " + unit.SafeName());
                    if (!isCurrent()) return RunStatus.Failure;
                    unit.Target();
                    // Target acknowledgement belongs to the next pulse, as before.
                    return RunStatus.Failure;
                }
                if (SingularSettings.Instance.DisableAllMovement || player.IsMoving)
                    return isCurrent() ? RunStatus.Success : RunStatus.Failure;
                bool facing = player.IsSafelyFacing(unit, 70f);
                if (!isCurrent() || SingularSettings.Instance.DisableAllMovement || player.IsMoving)
                    return RunStatus.Failure;
                if (facing) return RunStatus.Success;
                Logger.WriteDebug("Off-target cast: facing " + unit.SafeName());
                if (SingularSettings.Instance.DisableAllMovement || player.IsMoving || !isCurrent())
                    return RunStatus.Failure;
                unit.Face();
                return RunStatus.Failure;
            });
        }

        /// <summary>
        ///   Creates a move to target behavior. Will return RunStatus.Success if it has reached the location, or stopped in range. Best used at the end of a rotation.
        /// </summary>
        /// <remarks>
        ///   Created 5/1/2011.
        /// </remarks>
        /// <param name = "stopInRange">true to stop in range.</param>
        /// <param name = "range">The range.</param>
        /// <returns>.</returns>
        public static Composite CreateMoveToTargetBehavior(bool stopInRange, float range)
        {
            return CreateMoveToUnitBehavior(stopInRange, range, null, true, false);
        }

        /// <summary>
        ///   Creates a move to target behavior. Will return RunStatus.Success if it has reached the location, or stopped in range. Best used at the end of a rotation.
        /// </summary>
        /// <remarks>
        ///   Created 5/1/2011.
        /// </remarks>
        /// <param name = "stopInRange">true to stop in range.</param>
        /// <param name = "range">The range.</param>
        /// <param name="onUnit">The unit to move to.</param>
        /// <returns>.</returns>
        public static Composite CreateMoveToTargetBehavior(bool stopInRange, float range, UnitSelectionDelegate onUnit)
        {
            return CreateMoveToUnitBehavior(stopInRange, range, onUnit, false, false);
        }

        /// <summary>
        ///   Creates a move to melee range behavior. Will return RunStatus.Success if it has reached the location, or stopped in range. Best used at the end of a rotation.
        /// </summary>
        /// <remarks>
        ///   Created 5/1/2011.
        /// </remarks>
        /// <param name = "stopInRange">true to stop in range.</param>
        /// <returns>.</returns>
        public static Composite CreateMoveToMeleeBehavior(bool stopInRange)
        {
            return CreateMoveToUnitBehavior(stopInRange, 0f, null, true, true);
        }

        public static Composite CreateMoveToMeleeBehavior(LocationRetriever location, bool stopInRange)
        {
            return CreateMoveToLocationBehavior(location, stopInRange,
                ret => StyxWoW.Me.CurrentTarget?.IsPlayer == true ? 2f : Spell.MeleeRange);
        }

        private static Composite CreateMoveToUnitBehavior(bool stopInRange, float range,
            UnitSelectionDelegate onUnit, bool requireCurrentTarget, bool melee)
        {
            return new Action(ret =>
            {
                var player = StyxWoW.Me;
                ulong playerGuid = player?.Guid ?? 0;
                if (!IsMovementOwnerCurrent(player, playerGuid) || (!requireCurrentTarget && onUnit == null))
                    return RunStatus.Failure;

                // A selector may observe the world or run caller code. Resolve it
                // once and keep both identities through the final move/stop decision.
                var target = requireCurrentTarget ? player.CurrentTarget : onUnit(ret);
                ulong targetGuid = target?.Guid ?? 0;
                Func<bool> isCurrent = () => targetGuid != 0 && IsSightTargetUsable(target)
                    && !ReferenceEquals(target, player) && target.Guid == targetGuid
                    && (!requireCurrentTarget || ReferenceEquals(player.CurrentTarget, target))
                    && IsMovementOwnerCurrent(player, playerGuid);
                if (!isCurrent()) return RunStatus.Failure;

                var destination = target.Location;
                if (!isCurrent()) return RunStatus.Failure;
                float stopRange = stopInRange ? (melee ? (target.IsPlayer ? 2f : Spell.MeleeRange) : range) : 0f;
                return MoveToObservedLocation(player, destination, stopInRange, stopRange, isCurrent);
            });
        }

        #region Move Behind

        /// <summary>
        ///   Creates a move behind target behavior. If it cannot fully navigate will move to target location
        /// </summary>
        /// <remarks>
        ///   Created 2/12/2011.
        /// </remarks>
        /// <returns>.</returns>
        public static Composite CreateMoveBehindTargetBehavior()
        {
            return CreateMoveBehindTargetBehavior(ret => true);
        }

        /// <summary>
        ///   Creates a move behind target behavior. If it cannot fully navigate will move to target location
        /// </summary>
        /// <remarks>
        ///   Created 2/12/2011.
        /// </remarks>
        /// <param name="requirements">Aditional requirments.</param>
        /// <returns>.</returns>
        public static Composite CreateMoveBehindTargetBehavior(SimpleBooleanDelegate requirements)
        {
            return new Action(ret =>
            {
                var player = StyxWoW.Me;
                ulong playerGuid = player?.Guid ?? 0;
                if (requirements == null || !IsMovementOwnerCurrent(player, playerGuid))
                    return RunStatus.Failure;
                var target = player.CurrentTarget;
                ulong targetGuid = target?.Guid ?? 0;
                Func<bool> isCurrent = () => IsControlUnitCurrent(target, targetGuid) && target.IsAlive
                    && IsDisplayedTargetCurrent(player, target, targetGuid)
                    && SingularRoutine.CurrentWoWContext != WoWContext.Battlegrounds && !Group.MeIsTank
                    && !target.MeIsBehind && (target.CurrentTarget == null || target.CurrentTarget != player || target.Stunned)
                    && IsMovementOwnerCurrent(player, playerGuid);
                if (!isCurrent() || !requirements(ret) || !isCurrent()) return RunStatus.Failure;

                var location = target.Location;
                if (!isCurrent()) return RunStatus.Failure;
                float rotation = target.Rotation;
                float distance = Spell.MeleeRange - 2f;
                if (!float.IsFinite(rotation) || !float.IsFinite(distance) || distance < 0f || !isCurrent())
                    return RunStatus.Failure;
                var destination = location.RayCast(rotation + WoWMathHelper.DegreesToRadians(150), distance);
                return MoveToObservedLocation(player, destination, false, 0f, isCurrent);
            });
        }

        #endregion

        #region Root Move To Location

        /// <summary>
        ///   Creates a move to location behavior. Will return RunStatus.Success if it has reached the location, or stopped in range. Best used at the end of a rotation.
        /// </summary>
        /// <remarks>
        ///   Created 5/1/2011.
        /// </remarks>
        /// <param name = "location">The location.</param>
        /// <param name = "stopInRange">true to stop in range.</param>
        /// <param name = "range">The range.</param>
        /// <returns>.</returns>
        public static Composite CreateMoveToLocationBehavior(LocationRetriever location, bool stopInRange, DynamicRangeRetriever range)
        {
            return new Action(ret =>
            {
                var player = StyxWoW.Me;
                ulong playerGuid = player?.Guid ?? 0;
                if (location == null || (stopInRange && range == null) || !IsMovementOwnerCurrent(player, playerGuid))
                    return RunStatus.Failure;
                var destination = location(ret);
                if (!IsMovementOwnerCurrent(player, playerGuid)) return RunStatus.Failure;
                // Continuous pursuit has no stop range and must not evaluate an
                // unused caller delegate. Zero remains a valid never-stop range.
                float stopRange = stopInRange ? range(ret) : 0f;
                return MoveToObservedLocation(player, destination, stopInRange, stopRange,
                    () => IsMovementOwnerCurrent(player, playerGuid));
            });
        }

        private static bool IsMovementOwnerCurrent(WoWUnit player, ulong guid) =>
            guid != 0 && CanRecoverSight(player) && player.Guid == guid
            && ReferenceEquals(player, StyxWoW.Me);

        private static RunStatus MoveToObservedLocation(WoWUnit player, WoWPoint destination,
            bool stopInRange, float range, Func<bool> isCurrent)
        {
            if (destination == WoWPoint.Empty || destination == WoWPoint.Zero
                || !float.IsFinite(destination.X) || !float.IsFinite(destination.Y) || !float.IsFinite(destination.Z)
                || (stopInRange && (!float.IsFinite(range) || range < 0f)) || !isCurrent())
                return RunStatus.Failure;

            if (stopInRange)
            {
                float distance = player.Location.Distance(destination);
                if (!float.IsFinite(distance) || !isCurrent()) return RunStatus.Failure;
                if (distance < range)
                {
                    bool moving = player.IsMoving;
                    if (!isCurrent()) return RunStatus.Failure;
                    if (moving) Navigator.PlayerMover.MoveStop();
                    return RunStatus.Success;
                }
            }

            if (!isCurrent()) return RunStatus.Failure;
            var result = Navigator.MoveTo(destination);
            return result == MoveResult.Moved || result == MoveResult.PathGenerated
                || result == MoveResult.UnstuckAttempt || result == MoveResult.ReachedDestination
                ? RunStatus.Success : RunStatus.Failure;
        }

        #endregion

        public static Composite CreateMoveToLosBehavior(UnitSelectionDelegate toUnit)
        {
            return CreateMoveToLosBehavior(toUnit, false);
        }

        private static Composite CreateMoveToLosBehavior(UnitSelectionDelegate toUnit, bool requireCurrentTarget)
        {
            return new Action(ret =>
            {
                var player = StyxWoW.Me;
                ulong playerGuid = player?.Guid ?? 0;
                if ((!requireCurrentTarget && toUnit == null) || !IsMovementOwnerCurrent(player, playerGuid))
                    return RunStatus.Failure;

                // Select once per decision; repeated selectors can observe different
                // units or needlessly repeat target-list/native observations.
                var target = requireCurrentTarget ? player.CurrentTarget : toUnit(ret);
                ulong targetGuid = target?.Guid ?? 0;
                Func<bool> isCurrent = () => IsControlUnitCurrent(target, targetGuid) && !target.IsMe
                    && (!requireCurrentTarget || IsDisplayedTargetCurrent(player, target, targetGuid))
                    && IsMovementOwnerCurrent(player, playerGuid);
                if (!isCurrent() || target.InLineOfSpellSight || !isCurrent())
                    return RunStatus.Failure;

                var destination = target.Location;
                return MoveToObservedLocation(player, destination, false, 0f, isCurrent);
            });
        }

        private static bool IsControlOwnerCurrent(WoWUnit player, ulong guid) =>
            guid != 0 && player != null && ReferenceEquals(player, StyxWoW.Me)
            && player.IsValid && player.IsAlive && player.Guid == guid && ReferenceEquals(player, StyxWoW.Me);

        private static bool IsControlUnitCurrent(WoWUnit unit, ulong guid) =>
            guid != 0 && IsSightTargetUsable(unit) && unit.Guid == guid;

        private static bool IsDisplayedTargetCurrent(WoWUnit player, WoWUnit target, ulong guid) =>
            ReferenceEquals(player.CurrentTarget, target) && (target == null ? guid == 0 : target.Guid == guid);

        private static bool CanSetUpCast(WoWUnit player, ulong guid) =>
            IsControlOwnerCurrent(player, guid) && !player.IsCasting && player.ChanneledCastingSpellId == 0
            && IsControlOwnerCurrent(player, guid);

        private static bool IsSightOwnerCurrent(WoWUnit player) =>
            player != null && ReferenceEquals(player, StyxWoW.Me) &&
            !SingularSettings.Instance.DisableAllMovement && player.IsValid && player.IsAlive;

        private static bool CanRecoverSight(WoWUnit player) =>
            IsSightOwnerCurrent(player) && !player.IsCasting && player.ChanneledCastingSpellId == 0;

        // Druid Rebirth uses this helper for dead friendly players. Rejecting every
        // dead unit here would break that legitimate resurrection approach.
        private static bool IsSightTargetUsable(WoWUnit target) =>
            target != null && target.IsValid && (target.IsAlive || target.IsFriendly);

    }

    public delegate WoWPoint LocationRetriever(object context);

    public delegate float DynamicRangeRetriever(object context);
}
