using System;
using System.Collections.Generic;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.WoWInternals;

namespace Styx.Logic.Profiles
{
    /// <summary>A captured service trip, not live objects retained across ticks.</summary>
    public sealed class VendorTravelObservation
    {
        public DateTime NowUtc { get; init; }
        public DateTime LastMoveAttemptUtc { get; init; }
        public TimeSpan StationaryFor { get; init; }
        public uint MapId { get; init; }
        public int Entry { get; init; }
        public PoiType Type { get; init; }
        public WoWPoint PlayerLocation { get; init; }
        public WoWPoint Destination { get; init; }
        public WoWPoint LastMoveDestination { get; init; }
        public MoveResult? LastMoveResult { get; init; }
        public bool IsActiveWorld { get; init; }
        public bool IsPaused { get; init; }
        public bool IsCombat { get; init; }
        public bool IsDead { get; init; }
        // Intentional routine/food/drink rest, NOT the inn's rested-XP player flag.
        public bool IsResting { get; init; }
        public bool IsOnTaxi { get; init; }
        public bool IsOnTransport { get; init; }
        public bool IsElevatorTransit { get; init; }
        public bool HasActivePath { get; init; }
        public bool IsServiceFrameOpen { get; init; }
    }

    /// <summary>
    /// Temporary endpoint evidence shared by profile and automatic vendor selectors.
    /// Never mutates explicit exclusions or persists travel failure to disk.
    /// </summary>
    public sealed class VendorTravelBackoff
    {
        private const int Capacity = 128;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan Freshness = TimeSpan.FromSeconds(3);
        private readonly object _sync = new();
        private readonly List<Lease> _leases = new();

        private sealed record Lease(Guid Owner, uint Map, int Entry, WoWPoint Destination,
            DateTime CreatedUtc, DateTime RetryUtc);

        public int Count { get { lock (_sync) return _leases.Count; } }

        public bool TryDefer(Guid owner, VendorTravelObservation sample)
        {
            if (owner == Guid.Empty || sample == null || !IsFailedTravel(sample)) return false;
            lock (_sync)
            {
                // Other owners expire their own records, so their next Pulse can
                // request a refresh exactly once. Never evict another active lease.
                _leases.RemoveAll(lease => lease.Owner == owner && !IsActive(lease, sample.NowUtc));
                if (_leases.Exists(lease => lease.Owner == owner && lease.Map == sample.MapId
                    && lease.Entry == sample.Entry && SameEndpoint(lease.Destination, sample.Destination)))
                    return false;
                // At capacity, decline new penalties rather than turning this into
                // an unbounded blacklist or invalidating another owner's evidence.
                if (_leases.Count >= Capacity) return false;
                _leases.Add(new Lease(owner, sample.MapId, sample.Entry, sample.Destination,
                    sample.NowUtc, sample.NowUtc + RetryDelay));
                return true;
            }
        }

        public bool IsDeferred(uint mapId, int entry, WoWPoint destination, DateTime nowUtc)
        {
            if (entry <= 0 || !IsFinite(destination)) return false;
            lock (_sync)
                return _leases.Exists(lease => lease.Map == mapId && lease.Entry == entry
                    && IsActive(lease, nowUtc) && SameEndpoint(lease.Destination, destination));
        }

        public bool ReleaseExpired(Guid owner, DateTime nowUtc)
        {
            lock (_sync)
                return _leases.RemoveAll(lease => lease.Owner == owner && !IsActive(lease, nowUtc)) != 0;
        }

        public void Reset(Guid owner)
        {
            lock (_sync) _leases.RemoveAll(lease => lease.Owner == owner);
        }

        private static bool IsActive(Lease lease, DateTime nowUtc) =>
            nowUtc >= lease.CreatedUtc && nowUtc < lease.RetryUtc;

        private static bool IsFailedTravel(VendorTravelObservation sample) =>
            sample.Entry > 0 && VendorSafetyPolicy.IsService(sample.Type)
            && sample.IsActiveWorld && !sample.IsPaused && !sample.IsCombat && !sample.IsDead
            && !sample.IsResting && !sample.IsOnTaxi && !sample.IsOnTransport
            && !sample.IsElevatorTransit && !sample.HasActivePath && !sample.IsServiceFrameOpen
            && sample.StationaryFor >= TimeSpan.FromSeconds(30)
            && sample.LastMoveResult is MoveResult.Failed or MoveResult.PathGenerationFailed
            && sample.NowUtc >= sample.LastMoveAttemptUtc
            && sample.NowUtc - sample.LastMoveAttemptUtc <= Freshness
            && sample.NowUtc <= DateTime.MaxValue - RetryDelay
            && IsFinite(sample.PlayerLocation) && IsFinite(sample.Destination)
            && IsFinite(sample.LastMoveDestination)
            && SameEndpoint(sample.Destination, sample.LastMoveDestination)
            && !SameEndpoint(sample.PlayerLocation, sample.Destination);

        public static bool SameEndpoint(WoWPoint first, WoWPoint second)
        {
            if (!IsFinite(first) || !IsFinite(second)) return false;
            double dx = (double)first.X - second.X, dy = (double)first.Y - second.Y;
            return dx * dx + dy * dy <= 4.5 * 4.5 && Math.Abs((double)first.Z - second.Z) < 3;
        }

        public static bool IsFinite(WoWPoint point) =>
            float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);
    }
}
