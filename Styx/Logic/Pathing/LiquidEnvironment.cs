using Styx.WoWInternals.WoWObjects;
using Styx.WoWInternals.World;

namespace Styx.Logic.Pathing
{
    /// <summary>
    /// Centralizes WotLK liquid detection. The swimming flag is cleared while a player
    /// stands on a submerged riverbed, so an eye-to-feet liquid trace is also required.
    /// </summary>
    public static class LiquidEnvironment
    {
        private const long ProbeIntervalMilliseconds = 200L;
        private static long _lastProbeTick;
        private static uint _lastProbeMapId;
        private static WoWPoint _lastProbeLocation = WoWPoint.Zero;
        private static bool _lastProbeResult;

        public static bool IsInLiquid(bool isSwimming, bool liquidBetweenEyeAndFeet)
        {
            return isSwimming || liquidBetweenEyeAndFeet;
        }

        public static bool IsPlayerInLiquid(LocalPlayer player)
        {
            if (player.IsSwimming)
                return true;

            long now = System.Environment.TickCount64;
            if (player.MapId == _lastProbeMapId &&
                now - _lastProbeTick < ProbeIntervalMilliseconds &&
                player.Location.Distance2DSqr(_lastProbeLocation) < 4f)
            {
                return _lastProbeResult;
            }

            bool liquidBetweenEyeAndFeet = GameWorld.TraceLine(
                player.GetTraceLinePos(),
                player.Location,
                GameWorld.CGWorldFrameHitFlags.HitTestLiquid);

            _lastProbeTick = now;
            _lastProbeMapId = player.MapId;
            _lastProbeLocation = player.Location;
            _lastProbeResult = IsInLiquid(false, liquidBetweenEyeAndFeet);
            return _lastProbeResult;
        }
    }
}
