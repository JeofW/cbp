using System;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Styx.WoWInternals.World;

namespace Styx.Logic.Pathing
{
    /// <summary>
    /// Conservative liquid admission from swimming and a local surface crossing.
    /// A failed trace is not proof of dry ground. This does not establish depth,
    /// a safe shoreline route, or a complete native frame/session identity.
    /// </summary>
    public static class LiquidEnvironment
    {
        private const long ProbeIntervalMilliseconds = 200L;
        private static long _lastProbeTick;
        private static uint _lastProbeMapId;
        private static WoWPoint _lastProbeLocation = WoWPoint.Zero;
        private static bool _lastProbeResult;
        private static bool _hasProbe;
        private static LocalPlayer? _lastProbePlayer;
        private static object? _lastProbeMemory;
        private static uint _lastProbeAddress;

        public static bool IsInLiquid(bool isSwimming, bool liquidBetweenEyeAndFeet)
        {
            return isSwimming || liquidBetweenEyeAndFeet;
        }

        public static bool IsPlayerInLiquid(LocalPlayer player)
        {
            var memory = ObjectManager.Wow;
            uint address = player?.BaseAddress ?? 0U;
            bool OwnsObservation() => player != null && ReferenceEquals(ObjectManager.Me, player)
                && ReferenceEquals(ObjectManager.Wow, memory) && player.BaseAddress == address;
            if (player == null || memory == null || address == 0U || !player.IsValid || !OwnsObservation())
            {
                _hasProbe = false;
                return true;
            }
            if (player.IsSwimming)
            {
                // Leaving and reentering a dry-looking pose cannot resurrect the
                // dry observation that preceded an observed swimming interval.
                _hasProbe = false;
                return true;
            }

            long now = Environment.TickCount64;
            WoWPoint location = player.Location;
            uint mapId = player.MapId;
            if (!OwnsObservation() || !float.IsFinite(location.X) || !float.IsFinite(location.Y) || !float.IsFinite(location.Z))
            {
                _hasProbe = false;
                return true;
            }
            long age = now - _lastProbeTick;
            if (_hasProbe && ReferenceEquals(_lastProbePlayer, player)
                && ReferenceEquals(_lastProbeMemory, memory) && _lastProbeAddress == address
                && mapId == _lastProbeMapId && age >= 0 && age < ProbeIntervalMilliseconds
                && location.Z == _lastProbeLocation.Z && location.DistanceSqr(_lastProbeLocation) < 4f)
                return _lastProbeResult;

            bool liquidBetweenEyeAndFeet = GameWorld.TraceLine(
                location.Add(0f, 0f, 2.132f), location,
                GameWorld.CGWorldFrameHitFlags.HitTestLiquid);
            if (!OwnsObservation())
            {
                _hasProbe = false;
                return true;
            }
            _lastProbeTick = now;
            _lastProbeMapId = mapId;
            _lastProbeLocation = location;
            _lastProbePlayer = player;
            _lastProbeMemory = memory;
            _lastProbeAddress = address;
            _lastProbeResult = IsInLiquid(false, liquidBetweenEyeAndFeet);
            _hasProbe = true;
            return _lastProbeResult;
        }
    }
}
