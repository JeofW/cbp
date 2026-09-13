using System;
using Styx.Helpers;
using Styx.WoWInternals.WoWObjects;
using TripperNav = Tripper.Navigation;

namespace Styx.Logic.Pathing
{
    /// <summary>Movement-route evidence, not a verdict that a quest is invalid.</summary>
    public enum RouteFailureReason
    {
        None,
        InvalidCoordinates,
        PathSearchFailed,
        PartialPath,
        VerticalAccessUnresolved,
        SearchResourceLimit
    }

    public partial class MeshNavigator
    {
        private static readonly TimeSpan TerminalRetryDelay = TimeSpan.FromSeconds(3);
        private const float TerminalContextToleranceSquared = 4f;
        private TripperNav.Status _lastPathStatus;
        private WoWPoint _terminalRetryOrigin;
        private WoWPoint _terminalRetryDestination;
        private uint _terminalRetryMapId;
        private DateTime _terminalRetryStartedUtc = DateTime.MinValue;

        public RouteFailureReason LastRouteFailure { get; private set; }
        public uint LastRouteNativeStatus => _lastPathStatus.Value;
        public DateTime NextRouteRetryUtc { get; private set; } = DateTime.MinValue;

        private static bool IsFiniteRoutePoint(WoWPoint point) =>
            float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);

        private bool MatchesTerminalContext(WoWPoint origin, WoWPoint destination, uint mapId) =>
            IsFiniteRoutePoint(origin) && IsFiniteRoutePoint(destination)
            && mapId == _terminalRetryMapId
            && origin.DistanceSqr(_terminalRetryOrigin) <= TerminalContextToleranceSquared
            && destination.DistanceSqr(_terminalRetryDestination) <= TerminalContextToleranceSquared;

        // A hold belongs only to one exhausted movement request. A probe for another
        // endpoint cannot create it. Re-reading the failure cannot renew the deadline.
        private bool ShouldDeferTerminalRetry(WoWPoint origin, WoWPoint destination, uint mapId, DateTime now)
        {
            if (NextRouteRetryUtc == DateTime.MinValue)
                return false;
            if (_ridingElevator || HasActivePath || now < _terminalRetryStartedUtc
                || !MatchesTerminalContext(origin, destination, mapId))
            {
                ResetTerminalRouteEvidence();
                return false;
            }
            return now < NextRouteRetryUtc;
        }

        private void ResetTerminalRouteEvidence()
        {
            LastRouteFailure = RouteFailureReason.None;
            NextRouteRetryUtc = DateTime.MinValue;
            _terminalRetryStartedUtc = DateTime.MinValue;
            _terminalRetryOrigin = WoWPoint.Zero;
            _terminalRetryDestination = WoWPoint.Zero;
            _terminalRetryMapId = 0;
            _lastPathStatus = default;
        }

        private RouteFailureReason ClassifyTerminalFailure(WoWPoint origin, bool partial)
        {
            if (!IsFiniteRoutePoint(origin) || !IsFiniteRoutePoint(_destination))
                return RouteFailureReason.InvalidCoordinates;
            // Detour detail bits: allocation failure, output truncation, node exhaustion.
            // These do not establish disconnected geometry or a truly unreachable goal.
            if ((_lastPathStatus.Value & ((1u << 2) | (1u << 4) | (1u << 5))) != 0)
                return RouteFailureReason.SearchResourceLimit;
            if (!partial)
                return RouteFailureReason.PathSearchFailed;
            return Math.Abs(origin.Z - _destination.Z) >= 4.5f
                ? RouteFailureReason.VerticalAccessUnresolved
                : RouteFailureReason.PartialPath;
        }

        private void RecordTerminalRouteFailure(LocalPlayer me, bool partial)
        {
            WoWPoint origin = me.Location;
            RouteFailureReason reason = ClassifyTerminalFailure(origin, partial);
            LastRouteFailure = reason;
            _commandedProgress.Reset();
            if (reason == RouteFailureReason.InvalidCoordinates)
            {
                NextRouteRetryUtc = DateTime.MinValue;
                return;
            }
            uint mapId = me.MapId;
            if (NextRouteRetryUtc != DateTime.MinValue && MatchesTerminalContext(origin, _destination, mapId))
                return;
            _terminalRetryOrigin = origin;
            _terminalRetryDestination = _destination;
            _terminalRetryMapId = mapId;
            _terminalRetryStartedUtc = DateTime.UtcNow;
            NextRouteRetryUtc = _terminalRetryStartedUtc + TerminalRetryDelay;
            Logging.WriteDiagnostic(
                "[Nav] Route terminal: reason={0} native=0x{1:X8} map={2} from={3} to={4} partial={5} retryUtc={6:O}; no physical recovery authorized.",
                reason, _lastPathStatus.Value, mapId, origin, _destination, partial, NextRouteRetryUtc);
        }

        private MoveResult CompletePathOrRecover(LocalPlayer me)
        {
            if (!IsFiniteRoutePoint(me.Location) || !IsFiniteRoutePoint(_destination))
            {
                RecordTerminalRouteFailure(me, _isPartialPath);
                return MoveResult.Failed;
            }
            if (!_isPartialPath)
            {
                ResetTerminalRouteEvidence();
                return MoveResult.ReachedDestination;
            }
            // A consumed path prefix establishes search exhaustion, not a blocked
            // ground-movement command. Physical recovery stays in MoveAlongGroundPath.
            RecordTerminalRouteFailure(me, partial: true);
            return MoveResult.Failed;
        }
    }
}
