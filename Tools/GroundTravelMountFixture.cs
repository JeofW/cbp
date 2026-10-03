// Mount acquisition is an external boundary in the linked transition suites.
// Its actual lifecycle and actual Lua submission are exercised separately.
namespace Styx.Logic.Pathing;
internal sealed class GroundTravelMount
{
    internal static System.Func<bool>? Waiter;
    internal bool Wait(GroundTransitionContext context, double now, System.Func<bool> current, System.Action stop)
        => current() && (Waiter?.Invoke() ?? false);
}
