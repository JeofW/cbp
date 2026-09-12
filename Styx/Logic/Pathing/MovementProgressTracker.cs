using System;

namespace Styx.Logic.Pathing
{
    // Observes actual ground-navigation requests, not the transient client moving bit.
    // A blocked CTM can cancel before the next pulse; genuine arrivals are consumed
    // by MeshNavigator before this tracker is consulted.
    internal sealed class CommandedMovementProgressTracker
    {
        private WoWPoint _origin;
        private WoWPoint _destination;
        private DateTime _startedUtc;
        private DateTime _lastCommandUtc;
        private int _commands;

        internal bool ObserveCommand(WoWPoint location, WoWPoint destination, DateTime utcNow)
        {
            if (_commands == 0 || utcNow < _lastCommandUtc
                || utcNow - _lastCommandUtc > TimeSpan.FromSeconds(2.5)
                || destination.DistanceSqr(_destination) > 4f
                || location.DistanceSqr(_origin) >= 0.25f)
            {
                _origin = location;
                _destination = destination;
                _startedUtc = utcNow;
                _commands = 0;
            }
            _lastCommandUtc = utcNow;
            _commands++;
            if (_commands < 3 || utcNow - _startedUtc < TimeSpan.FromSeconds(3))
                return false;
            Reset();
            return true;
        }

        internal void Reset() => _commands = 0;
    }

    // Tracks navigation intent that cannot issue another movement command because
    // the navmesh returned a partial route whose final point has already been consumed.
    internal sealed class TerminalPartialPathRecovery
    {
        private WoWPoint _origin;
        private WoWPoint _destination;
        private DateTime _startedUtc;
        private DateTime _lastObservationUtc;
        private int _observations;

        internal bool Observe(WoWPoint location, WoWPoint destination, DateTime utcNow)
        {
            if (_observations == 0 || utcNow < _lastObservationUtc
                || utcNow - _lastObservationUtc > TimeSpan.FromSeconds(2.5)
                || destination.DistanceSqr(_destination) > 4f
                || location.DistanceSqr(_origin) >= 0.25f)
            {
                _origin = location;
                _destination = destination;
                _startedUtc = utcNow;
                _observations = 0;
            }

            _lastObservationUtc = utcNow;
            _observations++;
            if (_observations < 3 || utcNow - _startedUtc < TimeSpan.FromSeconds(3))
                return false;

            Reset();
            return true;
        }

        internal void Reset() => _observations = 0;
    }

    internal sealed class MovementProgressTracker
    {
        private readonly int _requiredFailures;
        private int _consecutiveFailures;

        internal MovementProgressTracker(int requiredFailures)
        {
            _requiredFailures = requiredFailures < 1 ? 1 : requiredFailures;
        }

        internal bool Observe(bool insufficientProgress)
        {
            if (!insufficientProgress)
            {
                Reset();
                return false;
            }

            if (_consecutiveFailures < _requiredFailures)
                _consecutiveFailures++;
            if (_consecutiveFailures < _requiredFailures)
                return false;

            Reset();
            return true;
        }

        internal void Reset()
        {
            _consecutiveFailures = 0;
        }
    }
}
