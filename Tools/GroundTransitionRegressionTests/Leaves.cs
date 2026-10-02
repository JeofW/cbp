namespace Styx.Helpers
{
    public interface IRangeAble { Range GetRange(); }
    public readonly record struct Range(int Minimum, int Maximum);
    public sealed class ObservationUnavailableException(string observation, string reason) : InvalidOperationException(observation + ": " + reason);
}
namespace Styx.WoWInternals.World
{
    // Geometry queries are separately linked/tested by GroundApproachRegressionTests.
    public static class GameWorld
    {
        [Flags] public enum CGWorldFrameHitFlags : uint
        { HitTestGroundAndStructures = 0x100111, HitTestLiquid = 0x10000, HitTestLiquid2 = 0x20000 }
    }
}
