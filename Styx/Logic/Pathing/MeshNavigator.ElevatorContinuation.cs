using System;
using System.Collections.Generic;
using System.Linq;
using TripperNav = Tripper.Navigation;

namespace Styx.Logic.Pathing
{
    public partial class MeshNavigator
    {
        // A synthetic transition can replace the approach/lift portion only. The
        // native path still owns every onward corner, transition and end verdict.
        private static bool TryCreateSafeElevatorContinuation(
            WoWPoint player, WoWPoint destination, WoWPoint transport,
            IReadOnlyList<WoWPoint> originalMeshPath,
            out WoWPoint[] shortcut, out int sourceExitIndex)
        {
            shortcut = Array.Empty<WoWPoint>();
            sourceExitIndex = -1;
            if (!IsFiniteRoutePoint(player) || !IsFiniteRoutePoint(destination)
                || !IsFiniteRoutePoint(transport) || originalMeshPath == null
                || originalMeshPath.Count < 2 || originalMeshPath.Any(point => !IsFiniteRoutePoint(point)))
                return false;

            WoWPoint wait = SelectDirectionalMeshLanding(transport, player, player.Z,
                originalMeshPath, 3f, 15f);
            if (wait == WoWPoint.Empty) return false;
            int waitIndex = IndexOfMeshPoint(originalMeshPath, wait, 0);
            if (waitIndex < 0) return false;
            var following = originalMeshPath.Skip(waitIndex + 1).ToArray();
            WoWPoint exit = SelectDirectionalMeshLanding(transport, destination, destination.Z,
                following, 5f, 15f);
            if (exit == WoWPoint.Empty) return false;
            sourceExitIndex = IndexOfMeshPoint(originalMeshPath, exit, waitIndex + 1);
            if (sourceExitIndex < 0) return false;
            shortcut = new[] { wait }.Concat(originalMeshPath.Skip(sourceExitIndex)).ToArray();
            return true;
        }

        private static int IndexOfMeshPoint(IReadOnlyList<WoWPoint> points, WoWPoint point, int start)
        {
            for (int index = start; index < points.Count; index++)
                if (points[index] == point) return index;
            return -1;
        }

        // Validate before mutating any active state. Missing metadata is unknown,
        // not permission to synthesize Ground/None or an unconditional destination.
        private bool TryInstallElevatorContinuation(WoWPoint[] shortcut, int sourceExitIndex)
        {
            int count = _currentPath.Count;
            if (shortcut == null || shortcut.Length < 2 || sourceExitIndex <= 0 || sourceExitIndex >= count
                || shortcut.Length != 1 + count - sourceExitIndex
                || _currentFlags == null || _currentFlags.Length != count
                || _currentPolyTypes == null || _currentPolyTypes.Length != count
                || _currentAbilityFlags == null || _currentAbilityFlags.Length != count
                || _currentPath.Any(point => !IsFiniteRoutePoint(point))
                || shortcut.Any(point => !IsFiniteRoutePoint(point))
                || !_currentPath.Take(sourceExitIndex).Contains(shortcut[0])
                || !shortcut.Skip(1).SequenceEqual(_currentPath.Skip(sourceExitIndex)))
                return false;

            var points = shortcut.ToArray();
            var flags = new[] { TripperNav.StraightPathFlags.OffMeshConnection }
                .Concat(_currentFlags.Skip(sourceExitIndex)).ToArray();
            var areas = new[] { TripperNav.AreaType.Elevator }
                .Concat(_currentPolyTypes.Skip(sourceExitIndex)).ToArray();
            var abilities = new[] { TripperNav.AbilityFlags.Transport }
                .Concat(_currentAbilityFlags.Skip(sourceExitIndex)).ToArray();
            _currentPath.Clear();
            _currentPath.AddRange(points);
            _currentFlags = flags;
            _currentPolyTypes = areas;
            _currentAbilityFlags = abilities;
            _currentPathIndex = 1;
            _cachedPushAheadIndex = -1;
            // Preserve _isPartialPath, _lastPathStatus and requested destination.
            return true;
        }
    }
}
