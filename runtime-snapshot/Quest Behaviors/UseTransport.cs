// Behavior originally contributed by Raphus.
//
// DOCUMENTATION:
//     
//
using System;
using System.Collections.Generic;
using System.Linq;

using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Pathing;
using Styx.Logic.Questing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Styx.WoWInternals.World;

using TreeSharp;
using Action = TreeSharp.Action;
using Matrix = Tripper.Tools.Math.Matrix;


namespace Styx.Bot.Quest_Behaviors
{
    public class UseTransport : CustomForcedBehavior
    {
        /// <summary>
        /// Allows you to use Transports.
        /// ##Syntax##
        /// TransportId: ID of the transport.
        /// TransportStart: Start point of the transport that we will get on when its close enough to that point.
        /// TransportEnd: End point of the transport that we will get off when its close enough to that point.
        /// WaitAt: Where you wish to wait the transport at
        /// ApproachAt: Optional safe staging point that must be reached before WaitAt.
        /// GetOff: Where you wish to end up at when transport reaches TransportEnd point
        /// StandOn: The point you wish the stand while you are in the transport
        /// BoardingDockTolerance: Maximum distance from a dock before movement is cancelled.
        /// DismountAtWait: Whether to dismount before boarding (legacy default: true).
        /// RequireObservedDeparture: Require seeing the transport away from the start before trusting its return.
        /// </summary>
        ///
        public UseTransport(Dictionary<string, string> args)
            : base(args)
        {
            try
            {
                // QuestRequirement* attributes are explained here...
                //    http://www.thebuddyforum.com/mediawiki/index.php?title=Honorbuddy_Programming_Cookbook:_QuestId_for_Custom_Behaviors
                // ...and also used for IsDone processing.
                WoWPoint? legacyEndLocation = LegacyGetAttributeAsWoWPoint("End", false, null, "TransportEndX/Y/Z");
                WoWPoint? legacyGetOffLocation = LegacyGetAttributeAsWoWPoint("Exit", false, null, "GetOffX/Y/Z");
                WoWPoint? legacyStartLocation = LegacyGetAttributeAsWoWPoint("Start", false, null, "TransportStartX/Y/Z");
                WoWPoint? legacyWaitAtLocation = LegacyGetAttributeAsWoWPoint("Entry", false, null, "WaitAtX/Y/Z");

                BoardingDockTolerance = (float)(GetAttributeAsNullable<double>(
                    "BoardingDockTolerance",
                    false,
                    new ConstrainTo.Domain<double>(0.1, 10000.0),
                    null) ?? 2.0);
                ApproachAtLocation = GetAttributeAsNullable<WoWPoint>(
                    "ApproachAt", false, ConstrainAs.WoWPointNonEmpty, null) ?? WoWPoint.Empty;
                DestName = GetAttributeAs<string>("DestName", false, ConstrainAs.StringNonEmpty, null) ?? "";
                bool? configuredDismountAtWait = GetAttributeAsNullable<bool>("DismountAtWait", false, null, null);
                DismountAtWait = configuredDismountAtWait ?? true;
                _dismountAtWaitWasSpecified = configuredDismountAtWait.HasValue;
                RequireObservedDeparture = GetAttributeAsNullable<bool>("RequireObservedDeparture", false, null, null) ?? false;
                EndLocation = GetAttributeAsNullable<WoWPoint>("TransportEnd", !legacyEndLocation.HasValue, ConstrainAs.WoWPointNonEmpty, null)
                                    ?? legacyEndLocation
                                    ?? WoWPoint.Empty;
                GetOffLocation = GetAttributeAsNullable<WoWPoint>("GetOff", !legacyGetOffLocation.HasValue, ConstrainAs.WoWPointNonEmpty, null)
                                    ?? legacyGetOffLocation
                                    ?? WoWPoint.Empty;
                StandLocation = GetAttributeAsNullable<WoWPoint>("StandOn", false, ConstrainAs.WoWPointNonEmpty, null) ?? WoWPoint.Empty;
                StartLocation = GetAttributeAsNullable<WoWPoint>("TransportStart", !legacyStartLocation.HasValue, ConstrainAs.WoWPointNonEmpty, null)
                                    ?? legacyStartLocation
                                    ?? WoWPoint.Empty;
                TransportId = GetAttributeAsNullable<int>("TransportId", true, ConstrainAs.MobId, new[] { "Transport" }) ?? 0;
                WaitAtLocation = GetAttributeAsNullable<WoWPoint>("WaitAt", !legacyWaitAtLocation.HasValue, ConstrainAs.WoWPointNonEmpty, null)
                                    ?? legacyWaitAtLocation
                                    ?? WoWPoint.Empty;
            }

            catch (Exception except)
            {
                // Maintenance problems occur for a number of reasons.  The primary two are...
                // * Changes were made to the behavior, and boundary conditions weren't properly tested.
                // * The Honorbuddy core was changed, and the behavior wasn't adjusted for the new changes.
                // In any case, we pinpoint the source of the problem area here, and hopefully it
                // can be quickly resolved.
                LogMessage("error", "BEHAVIOR MAINTENANCE PROBLEM: " + except.Message
                                        + "\nFROM HERE:\n"
                                        + except.StackTrace + "\n");
                IsAttributeProblem = true;
            }
        }


        // Attributes provided by caller
        public float BoardingDockTolerance { get; private set; }
        public WoWPoint ApproachAtLocation { get; private set; }
        public string DestName { get; private set; }
        public bool DismountAtWait { get; private set; }
        public bool RequireObservedDeparture { get; private set; }
        public WoWPoint EndLocation { get; private set; }
        public WoWPoint GetOffLocation { get; private set; }
        public int QuestId { get; private set; }
        public QuestCompleteRequirement QuestRequirementComplete { get; private set; }
        public QuestInLogRequirement QuestRequirementInLog { get; private set; }
        public WoWPoint StandLocation { get; private set; }
        public WoWPoint StartLocation { get; private set; }
        public int TransportId { get; private set; }
        public WoWPoint WaitAtLocation { get; private set; }

        // Private variables for internal state
        private static readonly TimeSpan DockConfirmationDuration = TimeSpan.FromMilliseconds(750);
        private static readonly TimeSpan DetachedExitConfirmationDuration = TimeSpan.FromMilliseconds(750);
        private const float DetachedExitMotionTolerance = 0.15f;
        private const float DockMotionTolerance = 0.05f;
        private const float MinimumAttachedExitClearance = 11f;
        private const float WaitLocationTolerance = 4.0f;
        private const float WaitLocationVerticalTolerance = 4.5f;
        private ConfigMemento _configMemento;
        private bool _detachedExitAuthorized;
        private WoWPoint _detachedExitCandidateOrigin = WoWPoint.Empty;
        private DateTime _detachedExitCandidateSinceUtc = DateTime.MinValue;
        private DateTime _dockCandidateSinceUtc = DateTime.MinValue;
        private bool _dismountAtWaitWasSpecified;
        private DateTime _exitDockCandidateSinceUtc = DateTime.MinValue;
        private bool _exitDockConfirmed;
        private WoWPoint _exitDockCandidateOrigin = WoWPoint.Empty;
        private bool _isBehaviorDone;
        private bool _isDisposed;
        private bool _reachedApproachLocation;
        private WoWPoint _lastDockCandidateLocation = WoWPoint.Empty;
        private DateTime _nextDiagnosticLogUtc = DateTime.MinValue;
        private bool _observedTransportAwayFromStart;
        private Composite _root;
        private ulong _selectedTransportGuid;
        private bool _usedTransport;
        private bool _wasOnWaitLocation;

        // Private properties
        private LocalPlayer Me { get { return (ObjectManager.Me); } }

        private static bool HasReachedWaitLocation(WoWPoint playerLocation, WoWPoint waitLocation)
        {
            return playerLocation.Distance2DSqr(waitLocation) <= WaitLocationTolerance * WaitLocationTolerance
                && Math.Abs(playerLocation.Z - waitLocation.Z) < WaitLocationVerticalTolerance;
        }

        private WoWPoint SelectApproachDestination(WoWPoint playerLocation)
        {
            if (!_reachedApproachLocation && ApproachAtLocation != WoWPoint.Empty)
            {
                if (!HasReachedWaitLocation(playerLocation, ApproachAtLocation))
                    return ApproachAtLocation;

                _reachedApproachLocation = true;
            }

            return WaitAtLocation;
        }

        private static bool CanMoveInsideTransport(
            bool isOnTransport,
            WoWPoint transportLocation,
            WoWPoint startLocation,
            float boardingDockTolerance)
        {
            return !isOnTransport
                && transportLocation != WoWPoint.Empty
                && transportLocation.Distance(startLocation) <= boardingDockTolerance;
        }

        private static WoWPoint SelectLiveTransportLocation(
            WoWPoint reportedLocation,
            Matrix worldMatrix,
            bool usesAnimatedTransportMatrix)
        {
            if (!usesAnimatedTransportMatrix)
                return reportedLocation;

            if (!IsUsableAffineTransportMatrix(worldMatrix))
                return WoWPoint.Empty;

            var liveLocation = new WoWPoint(worldMatrix.M41, worldMatrix.M42, worldMatrix.M43);
            return IsFinite(liveLocation)
                && Math.Abs(liveLocation.X) < 100000f
                && Math.Abs(liveLocation.Y) < 100000f
                && Math.Abs(liveLocation.Z) < 100000f
                && liveLocation.DistanceSqr(WoWPoint.Zero) > 0.01f
                && liveLocation != WoWPoint.Empty
                ? liveLocation
                : WoWPoint.Empty;
        }

        private static bool IsUsableAffineTransportMatrix(Matrix matrix)
        {
            float[] values =
            {
                matrix.M11, matrix.M12, matrix.M13, matrix.M14,
                matrix.M21, matrix.M22, matrix.M23, matrix.M24,
                matrix.M31, matrix.M32, matrix.M33, matrix.M34,
                matrix.M41, matrix.M42, matrix.M43, matrix.M44
            };
            if (values.Any(value => float.IsNaN(value) || float.IsInfinity(value)))
                return false;

            if (Math.Abs(matrix.M14) > 0.01f
                || Math.Abs(matrix.M24) > 0.01f
                || Math.Abs(matrix.M34) > 0.01f
                || Math.Abs(matrix.M44 - 1f) > 0.01f)
            {
                return false;
            }

            float determinant = ((System.Numerics.Matrix4x4)matrix).GetDeterminant();
            return !float.IsNaN(determinant)
                && !float.IsInfinity(determinant)
                && Math.Abs(determinant) > 0.000001f
                && Math.Abs(determinant) < 1000000f;
        }

        private static bool IsFinite(WoWPoint location)
        {
            return !float.IsNaN(location.X) && !float.IsInfinity(location.X)
                && !float.IsNaN(location.Y) && !float.IsInfinity(location.Y)
                && !float.IsNaN(location.Z) && !float.IsInfinity(location.Z);
        }

        private RunStatus BoardTransport()
        {
            WoWPoint transportLocation = TransportLocation;
            bool attachedToSelectedTransport = IsAttachedToSelectedTransport;
            if (IsWrongTransportAttachment(Me.IsOnTransport, attachedToSelectedTransport))
            {
                Navigator.PlayerMover.MoveStop();
                TreeRoot.StatusText = "Waiting after attachment to a different transport";
                return RunStatus.Failure;
            }

            return BoardTransport(attachedToSelectedTransport, transportLocation);
        }

        private bool IsAttachedToSelectedTransport
        {
            get
            {
                if (_selectedTransportGuid != 0UL)
                    return Me.WoWMovementInfo.TransportGuid == _selectedTransportGuid;

                WoWGameObject attached = Me.Transport;
                return attached != null && attached.Entry == TransportId;
            }
        }

        private static bool IsSelectedTransport(ulong selectedTransportGuid, ulong candidateGuid)
        {
            return selectedTransportGuid == 0UL || selectedTransportGuid == candidateGuid;
        }

        private static bool IsWrongTransportAttachment(
            bool isOnAnyTransport,
            bool isAttachedToSelectedTransport)
        {
            return isOnAnyTransport && !isAttachedToSelectedTransport;
        }

        private static bool UsesLockedTransportIdentity(WoWGameObjectType transportType)
        {
            return transportType == WoWGameObjectType.Transport;
        }

        private static bool ShouldDismountAtWait(
            bool configuredDismountAtWait,
            bool dismountWasExplicitlySpecified,
            WoWGameObjectType? transportType)
        {
            if (!configuredDismountAtWait)
                return false;
            if (dismountWasExplicitlySpecified)
                return true;

            // Preserve legacy boat/zeppelin behavior while keeping mounts on the
            // vertical TRANSPORT objects that can safely carry mounted players.
            return !transportType.HasValue || transportType.Value != WoWGameObjectType.Transport;
        }

        private RunStatus BoardTransport(bool isOnTransport, WoWPoint transportLocation)
        {
            if (isOnTransport)
            {
                ResetDockConfirmation();
                _usedTransport = true;
                Navigator.PlayerMover.MoveStop();
                TreeRoot.StatusText = "Waiting for the end location";
                return RunStatus.Failure;
            }

            if (!CanMoveInsideTransport(false, transportLocation, StartLocation, BoardingDockTolerance))
            {
                if (RequireObservedDeparture
                    && transportLocation != WoWPoint.Empty
                    && transportLocation.Distance(StartLocation) > BoardingDockTolerance + 0.5f)
                {
                    _observedTransportAwayFromStart = true;
                }
                ResetDockConfirmation();
                Navigator.PlayerMover.MoveStop();
                TreeRoot.StatusText = "Waiting for transport";
                return RunStatus.Failure;
            }

            if (RequireObservedDeparture && !_observedTransportAwayFromStart)
            {
                ResetDockConfirmation();
                Navigator.PlayerMover.MoveStop();
                TreeRoot.StatusText = "Waiting to observe a complete transport cycle";
                return RunStatus.Failure;
            }

            if (!HasStableDockObservation(transportLocation, DateTime.UtcNow))
            {
                Navigator.PlayerMover.MoveStop();
                TreeRoot.StatusText = "Confirming transport is docked";
                return RunStatus.Failure;
            }

            TreeRoot.StatusText = "Moving inside transport";
            MoveCollisionAware(transportLocation);
            return RunStatus.Failure;
        }

        private bool HasStableDockObservation(WoWPoint transportLocation, DateTime observedAtUtc)
        {
            bool firstObservation = _dockCandidateSinceUtc == DateTime.MinValue
                || _lastDockCandidateLocation == WoWPoint.Empty;
            bool transportMoved = !firstObservation
                && _lastDockCandidateLocation.Distance(transportLocation) > DockMotionTolerance;

            if (firstObservation || transportMoved)
            {
                _dockCandidateSinceUtc = observedAtUtc;
                _lastDockCandidateLocation = transportLocation;
                return false;
            }

            return observedAtUtc - _dockCandidateSinceUtc >= DockConfirmationDuration;
        }

        private void ResetDockConfirmation()
        {
            _dockCandidateSinceUtc = DateTime.MinValue;
            _lastDockCandidateLocation = WoWPoint.Empty;
        }

        private RunStatus ExitTransport()
        {
            WoWPoint transportLocation = TransportLocation;
            bool attachedToSelectedTransport = IsAttachedToSelectedTransport;
            bool isFalling = Me.IsFalling || Me.MovementInfo.IsFalling;
            bool hasGroundSupport = attachedToSelectedTransport || HasGroundSupport(Me);
            return ExitTransport(
                attachedToSelectedTransport,
                Me.IsOnTransport,
                isFalling,
                hasGroundSupport,
                Me.Location,
                transportLocation);
        }

        private static bool HasGroundSupport(LocalPlayer player)
        {
            if (ObjectManager.Executor == null)
                return false;

            try
            {
                WoWPoint origin = player.Location;
                bool hit = GameWorld.TraceLine(
                    origin.Add(0f, 0f, 0.5f),
                    origin.Add(0f, 0f, -1.5f),
                    GameWorld.CGWorldFrameHitFlags.HitTestGroundAndStructures,
                    out WoWPoint ground);
                return hit
                    && ground != WoWPoint.Zero
                    && ground.Distance2D(origin) <= Math.Max(0.5f, player.BoundingRadius)
                    && Math.Abs(ground.Z - origin.Z) <= 0.75f;
            }
            catch
            {
                return false;
            }
        }

        private RunStatus ExitTransport(
            bool isOnTransport,
            bool isFalling,
            WoWPoint playerLocation,
            WoWPoint transportLocation)
        {
            return ExitTransport(
                isOnTransport,
                isOnTransport,
                isFalling,
                true,
                playerLocation,
                transportLocation);
        }

        private RunStatus ExitTransport(
            bool isOnTransport,
            bool isFalling,
            bool hasGroundSupport,
            WoWPoint playerLocation,
            WoWPoint transportLocation)
        {
            return ExitTransport(
                isOnTransport,
                isOnTransport,
                isFalling,
                hasGroundSupport,
                playerLocation,
                transportLocation);
        }

        private RunStatus ExitTransport(
            bool isOnTransport,
            bool isOnAnyTransport,
            bool isFalling,
            bool hasGroundSupport,
            WoWPoint playerLocation,
            WoWPoint transportLocation)
        {
            if (IsWrongTransportAttachment(isOnAnyTransport, isOnTransport))
            {
                ResetExitDockConfirmation();
                Navigator.PlayerMover.MoveStop();
                TreeRoot.StatusText = "Waiting after exit attachment to a different transport";
                return RunStatus.Failure;
            }

            WoWPoint exitMovementTarget = SelectExitMovementTarget(
                _selectedTransportGuid != 0UL,
                EndLocation,
                GetOffLocation);
            if (exitMovementTarget == WoWPoint.Empty)
            {
                ResetDetachedExitConfirmation();
                Navigator.PlayerMover.MoveStop();
                TreeRoot.StatusText = "Waiting for a valid transport exit direction";
                return RunStatus.Failure;
            }
            if (isOnTransport)
            {
                ResetDetachedExitConfirmation();
                if (transportLocation == WoWPoint.Empty
                    || transportLocation.Distance(EndLocation) > BoardingDockTolerance)
                {
                    ResetExitDockConfirmation();
                    Navigator.PlayerMover.MoveStop();
                    TreeRoot.StatusText = "Waiting for transport to reach the end location";
                    return RunStatus.Failure;
                }

                if (!HasStableExitDockObservation(transportLocation, DateTime.UtcNow))
                {
                    Navigator.PlayerMover.MoveStop();
                    TreeRoot.StatusText = "Confirming transport is stopped at the end location";
                    return RunStatus.Failure;
                }

                _exitDockConfirmed = true;
                TreeRoot.StatusText = "Moving out of transport";
                MoveCollisionAware(exitMovementTarget);
                return RunStatus.Failure;
            }

            Navigator.PlayerMover.MoveStop();
            bool atDestinationElevation = Math.Abs(playerLocation.Z - EndLocation.Z)
                <= WaitLocationVerticalTolerance;
            bool atConfiguredExit = playerLocation.Distance2D(exitMovementTarget) <= 3f
                && Math.Abs(playerLocation.Z - exitMovementTarget.Z) <= WaitLocationVerticalTolerance;
            if (isFalling)
            {
                ResetDetachedExitConfirmation();
                TreeRoot.StatusText = "Waiting for a safe transport exit";
                return RunStatus.Failure;
            }

            // A higher-priority service interaction can legitimately move the player
            // down a ramp after the transport has docked.  Ground support plus the
            // confirmed destination dock is stronger evidence than the old deck Z.
            if (!atDestinationElevation && hasGroundSupport && _exitDockConfirmed)
            {
                _detachedExitAuthorized = true;
                _isBehaviorDone = true;
                TreeRoot.StatusText = "Transport complete";
                return RunStatus.Failure;
            }

            if (hasGroundSupport)
                _detachedExitAuthorized = true;
            else if (!_detachedExitAuthorized
                     && HasStableDetachedExitObservation(playerLocation, DateTime.UtcNow))
                _detachedExitAuthorized = true;
            if (!_detachedExitAuthorized)
            {
                TreeRoot.StatusText = "Confirming stable transport detachment";
                return RunStatus.Failure;
            }

            if (atConfiguredExit)
            {
                _isBehaviorDone = true;
                TreeRoot.StatusText = "Transport complete";
                return RunStatus.Failure;
            }

            if (!_exitDockConfirmed)
            {
                TreeRoot.StatusText = "Waiting for a safe transport exit";
                return RunStatus.Failure;
            }

            TreeRoot.StatusText = "Moving to safe exit location";
            MoveCollisionAware(exitMovementTarget);
            return RunStatus.Failure;
        }

        private void MoveCollisionAware(WoWPoint destination)
        {
            WoWPoint playerLocation = Me.Location;
            WoWPoint movementTarget = SelectCollisionAwareMovementTarget(
                playerLocation,
                destination,
                (from, to) => IsMovementCorridorClear(from, to));
            Navigator.PlayerMover.MoveTowards(movementTarget);
        }

        private static bool IsMovementCorridorClear(WoWPoint from, WoWPoint to)
        {
            try
            {
                WoWPoint hit;
                return !GameWorld.TraceLine(
                    from.Add(0f, 0f, 1f),
                    to.Add(0f, 0f, 1f),
                    GameWorld.CGWorldFrameHitFlags.HitTestGroundAndStructures,
                    out hit);
            }
            catch
            {
                return true;
            }
        }

        private static WoWPoint SelectCollisionAwareMovementTarget(
            WoWPoint playerLocation,
            WoWPoint destination,
            Func<WoWPoint, WoWPoint, bool> isClear)
        {
            if (isClear == null || isClear(playerLocation, destination))
                return destination;

            float dx = destination.X - playerLocation.X;
            float dy = destination.Y - playerLocation.Y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            if (length < 0.1f)
                return destination;

            float forwardX = dx / length;
            float forwardY = dy / length;
            float sideX = -forwardY;
            float sideY = forwardX;
            const float forwardProbe = 2f;
            const float sideProbe = 3.5f;
            for (int direction = 1; direction >= -1; direction -= 2)
            {
                var candidate = new WoWPoint(
                    playerLocation.X + forwardX * forwardProbe + sideX * sideProbe * direction,
                    playerLocation.Y + forwardY * forwardProbe + sideY * sideProbe * direction,
                    playerLocation.Z);
                if (isClear(playerLocation, candidate) && isClear(candidate, destination))
                    return candidate;
            }

            return destination;
        }

        private bool HasStableDetachedExitObservation(
            WoWPoint playerLocation,
            DateTime observedAtUtc)
        {
            bool firstObservation = _detachedExitCandidateSinceUtc == DateTime.MinValue
                || _detachedExitCandidateOrigin == WoWPoint.Empty;
            bool playerMoved = !firstObservation
                && _detachedExitCandidateOrigin.Distance(playerLocation) > DetachedExitMotionTolerance;
            if (firstObservation || playerMoved)
            {
                _detachedExitCandidateSinceUtc = observedAtUtc;
                _detachedExitCandidateOrigin = playerLocation;
                return false;
            }

            return observedAtUtc - _detachedExitCandidateSinceUtc
                >= DetachedExitConfirmationDuration;
        }

        private void ResetDetachedExitConfirmation()
        {
            _detachedExitAuthorized = false;
            _detachedExitCandidateSinceUtc = DateTime.MinValue;
            _detachedExitCandidateOrigin = WoWPoint.Empty;
        }

        private static WoWPoint SelectExitMovementTarget(
            bool requiresLiftClearance,
            WoWPoint transportLocation,
            WoWPoint getOffLocation)
        {
            if (!requiresLiftClearance || transportLocation == WoWPoint.Empty)
                return getOffLocation;

            float deltaX = getOffLocation.X - transportLocation.X;
            float deltaY = getOffLocation.Y - transportLocation.Y;
            float distance = (float)Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
            if (distance < 0.01f)
                return WoWPoint.Empty;
            if (distance >= MinimumAttachedExitClearance)
                return getOffLocation;

            float scale = MinimumAttachedExitClearance / distance;
            return new WoWPoint(
                transportLocation.X + deltaX * scale,
                transportLocation.Y + deltaY * scale,
                getOffLocation.Z);
        }

        private bool HasStableExitDockObservation(WoWPoint transportLocation, DateTime observedAtUtc)
        {
            bool firstObservation = _exitDockCandidateSinceUtc == DateTime.MinValue
                || _exitDockCandidateOrigin == WoWPoint.Empty;
            bool transportMoved = !firstObservation
                && _exitDockCandidateOrigin.Distance(transportLocation) > DockMotionTolerance;

            if (firstObservation || transportMoved)
            {
                _exitDockCandidateSinceUtc = observedAtUtc;
                _exitDockCandidateOrigin = transportLocation;
                _exitDockConfirmed = false;
                return false;
            }

            return observedAtUtc - _exitDockCandidateSinceUtc >= DockConfirmationDuration;
        }

        private void ResetExitDockConfirmation()
        {
            _exitDockCandidateSinceUtc = DateTime.MinValue;
            _exitDockCandidateOrigin = WoWPoint.Empty;
            _exitDockConfirmed = false;
            ResetDetachedExitConfirmation();
        }

        private void LogTransportDiagnostics(string stage)
        {
            DateTime now = DateTime.UtcNow;
            if (now < _nextDiagnosticLogUtc)
                return;

            _nextDiagnosticLogUtc = now.AddSeconds(3);
            WoWGameObject transport = TransportObject;
            WoWPoint transportLocation = transport == null
                ? WoWPoint.Empty
                : SelectLiveTransportLocation(
                    transport.WorldLocation,
                    transport.GetWorldMatrix(),
                    transport.SubType == WoWGameObjectType.Transport
                        || transport.SubType == WoWGameObjectType.MapObjectTransport);
            string transportText = transport == null
                ? "missing"
                : string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "live={0}; anchor={1}; startDistance={2:F2}; type={3}; state={4}; animation={5}; observedAway={6}; dockStableMs={7:F0}",
                    transportLocation,
                    transport.WorldLocation,
                    transportLocation == WoWPoint.Empty ? double.PositiveInfinity : transportLocation.Distance(StartLocation),
                    transport.SubType,
                    (byte)transport.State,
                    transport.AnimationProgress,
                    _observedTransportAwayFromStart,
                    _dockCandidateSinceUtc == DateTime.MinValue
                        ? 0.0
                        : Math.Max(0.0, (now - _dockCandidateSinceUtc).TotalMilliseconds));
            bool attachedToSelectedTransport = IsAttachedToSelectedTransport;
            bool falling = Me.IsFalling || Me.MovementInfo.IsFalling;
            bool groundSupport = attachedToSelectedTransport || HasGroundSupport(Me);
            WoWPoint exitMovementTarget = SelectExitMovementTarget(
                _selectedTransportGuid != 0UL,
                EndLocation,
                GetOffLocation);

            LogMessage(
                "debug",
                "{0}: player={1}; wait2D={2:F2}; waitZ={3:F2}; pathPrecision={4:F2}; transport={5}; onTransport={6}; selectedGuid={7:X}; attachedGuid={8:X}; usedTransport={9}; falling={10}; groundSupport={11}; exitDockConfirmed={12}; detachedExitAuthorized={13}; exitTarget={14}; exit2D={15:F2}",
                stage,
                Me.Location,
                Me.Location.Distance2D(WaitAtLocation),
                Math.Abs(Me.Location.Z - WaitAtLocation.Z),
                Navigator.PathPrecision,
                transportText,
                Me.IsOnTransport,
                _selectedTransportGuid,
                Me.WoWMovementInfo.TransportGuid,
                _usedTransport,
                falling,
                groundSupport,
                _exitDockConfirmed,
                _detachedExitAuthorized,
                exitMovementTarget,
                Me.Location.Distance2D(exitMovementTarget));
        }

        // DON'T EDIT THESE--they are auto-populated by Subversion
        public override string SubversionId { get { return ("$Id: UseTransport.cs 217 2012-02-11 16:52:02Z Nesox $"); } }
        public override string SubversionRevision { get { return ("$Revision: 217 $"); } }


        ~UseTransport()
        {
            Dispose(false);
        }

        public void Dispose(bool isExplicitlyInitiatedDispose)
        {
            if (!_isDisposed)
            {
                // NOTE: we should call any Dispose() method for any managed or unmanaged
                // resource, if that resource provides a Dispose() method.

                // Clean up managed resources, if explicit disposal...
                if (isExplicitlyInitiatedDispose)
                {
                    // empty, for now
                }

                // Clean up unmanaged resources (if any) here...
                if (_configMemento != null)
                { _configMemento.Dispose(); }

                _configMemento = null;

                BotEvents.OnBotStop -= BotEvents_OnBotStop;
                TreeRoot.GoalText = string.Empty;
                TreeRoot.StatusText = string.Empty;

                // Call parent Dispose() (if it exists) here ...
                base.Dispose();
            }

            _isDisposed = true;
        }


        public void BotEvents_OnBotStop(EventArgs args)
        {
            Dispose();
        }


        private WoWGameObject TransportObject
        {
            get
            {
                if (_selectedTransportGuid != 0UL)
                {
                    WoWGameObject selected = ObjectManager.GetObjectByGuid<WoWGameObject>(
                        _selectedTransportGuid);
                    return selected != null && selected.Entry == TransportId
                        ? selected
                        : null;
                }

                WoWGameObject attached = Me.Transport;
                if (attached != null && attached.Entry == TransportId)
                {
                    if (UsesLockedTransportIdentity(attached.SubType))
                        _selectedTransportGuid = attached.Guid;
                    return attached;
                }

                WoWGameObject candidate = ObjectManager.GetObjectsOfType<WoWGameObject>(true, false)
                    .Where(o => o.Entry == TransportId && IsSelectedTransport(_selectedTransportGuid, o.Guid))
                    .OrderBy(o => o.Location.Distance2DSqr(Me.Location))
                    .FirstOrDefault();
                if (candidate != null && UsesLockedTransportIdentity(candidate.SubType))
                    _selectedTransportGuid = candidate.Guid;
                return candidate;
            }
        }

        private WoWPoint TransportLocation
        {
            get
            {
                WoWGameObject transport = TransportObject;

                if (transport == null)
                    return WoWPoint.Empty;

                bool usesAnimatedTransportMatrix = transport.SubType == WoWGameObjectType.Transport
                    || transport.SubType == WoWGameObjectType.MapObjectTransport;
                return SelectLiveTransportLocation(
                    transport.WorldLocation,
                    transport.GetWorldMatrix(),
                    usesAnimatedTransportMatrix);
            }
        }


        #region Overrides of CustomForcedBehavior

        protected override Composite CreateBehavior()
        {
            return _root ?? (_root =
                new PrioritySelector(
                    new Decorator(
                        ret => !_wasOnWaitLocation,
                        new PrioritySelector(
                            new Decorator(
                                ret => !HasReachedWaitLocation(Me.Location, WaitAtLocation),
                                new Sequence(
                                    new Action(ret => TreeRoot.StatusText = "Moving to wait location"),
                                    new Action(ret => LogTransportDiagnostics("Approaching wait location")),
                                    new Action(ret => Navigator.MoveTo(SelectApproachDestination(Me.Location))))),
                            new Sequence(
                                new Action(ret => Navigator.PlayerMover.MoveStop()),
                                new Action(ret =>
                                {
                                    WoWGameObject transport = TransportObject;
                                    if (ShouldDismountAtWait(
                                            DismountAtWait,
                                            _dismountAtWaitWasSpecified,
                                            transport == null ? (WoWGameObjectType?)null : transport.SubType))
                                        Mount.Dismount();
                                }),
                                new Action(ret => _wasOnWaitLocation = true),
                                new Action(ret => TreeRoot.StatusText = "Waiting for transport")))),
                    new Decorator(
                        ret => _usedTransport,
                        new Action(ret => ExitTransport())),
                    new Decorator(
                        ret => IsAttachedToSelectedTransport && StandLocation != WoWPoint.Empty && !_usedTransport,
                        new PrioritySelector(
                            new Decorator(
                                ret => Me.Location.Distance2D(StandLocation) > 2,
                                new Sequence(
                                    new Action(ret => TreeRoot.StatusText = "Moving to stand location"),
                                    new Action(ret => MoveCollisionAware(StandLocation)))),
                            new Sequence(
                                new Action(ret => _usedTransport = true),
                                new Action(ret => Navigator.PlayerMover.MoveStop()),
                                new Action(ret => TreeRoot.StatusText = "Waiting for the end location"))
                        )),
                    new Decorator(
                        ret => !_usedTransport,
                        new Action(ret => BoardTransport())),
                    new Action(ret =>
                    {
                        LogTransportDiagnostics("Waiting for transport");
                        return RunStatus.Failure;
                    })));
        }


        public override void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }


        public override bool IsDone
        {
            get { return (_isBehaviorDone); }
        }


        public override void OnStart()
        {
            // This reports problems, and stops BT processing if there was a problem with attributes...
            // We had to defer this action, as the 'profile line number' is not available during the element's
            // constructor call.
            OnStart_HandleAttributeProblem();

            // If the quest is complete, this behavior is already done...
            // So we don't want to falsely inform the user of things that will be skipped.
            if (!IsDone)
            {
                // The ConfigMemento() class captures the user's existing configuration.
                // After its captured, we can change the configuration however needed.
                // When the memento is dispose'd, the user's original configuration is restored.
                // More info about how the ConfigMemento applies to saving and restoring user configuration
                // can be found here...
                //     http://www.thebuddyforum.com/mediawiki/index.php?title=Honorbuddy_Programming_Cookbook:_Saving_and_Restoring_User_Configuration
                _configMemento = new ConfigMemento();

                BotEvents.OnBotStop += BotEvents_OnBotStop;

                // Disable any settings that may cause distractions --
                // When we use transport, we don't want to be distracted by other things.
                // We also set PullDistance to its minimum value.
                // NOTE: these settings are restored to their normal values when the behavior completes
                // or the bot is stopped.
                CharacterSettings.Instance.HarvestHerbs = false;
                CharacterSettings.Instance.HarvestMinerals = false;
                CharacterSettings.Instance.LootChests = false;
                CharacterSettings.Instance.LootMobs = false;
                CharacterSettings.Instance.NinjaSkin = false;
                CharacterSettings.Instance.SkinMobs = false;
                CharacterSettings.Instance.PullDistance = 1;


                PlayerQuest quest = StyxWoW.Me.QuestLog.GetQuestById((uint)QuestId);

                TreeRoot.GoalText = this.GetType().Name + ": " + ((!string.IsNullOrEmpty(DestName)) ? DestName :
                                                                  (quest != null) ? ("\"" + quest.Name + "\"") :
                                                                  "In Progress");
            }
        }


        #endregion
    }
}
