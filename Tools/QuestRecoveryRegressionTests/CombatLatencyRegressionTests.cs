using System.Runtime.CompilerServices;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Combat;

internal static class CombatLatencyRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        TestSpellCooldownMemoryPolicy();
        TestTrackedCooldownPolicy();
        TestSlowBotRootTickThreshold();
    }

    private static void TestSlowBotRootTickThreshold()
    {
        Assert(!TreeRoot.IsSlowBotRootTick(99),
            "normal root evaluation must not produce per-tick log noise");
        Assert(TreeRoot.IsSlowBotRootTick(100),
            "a blocking 100ms root evaluation must be visible");
    }

    private static void TestSpellCooldownMemoryPolicy()
    {
        Assert(SpellManager.CalculateCooldownRemaining(1000, 8000, 1500, 4000)
                   == TimeSpan.FromMilliseconds(5000),
            "spell cooldown must win when it exceeds the global cooldown");
        Assert(SpellManager.CalculateCooldownRemaining(1000, 0, 1500, 1200)
                   == TimeSpan.FromMilliseconds(1300),
            "global cooldown must still block a spell with no longer specific cooldown");
        Assert(SpellManager.CalculateCooldownRemaining(1000, 1500, 1500, 3000)
                   == TimeSpan.Zero,
            "expired cooldown records must return zero");
    }

    private static void TestTrackedCooldownPolicy()
    {
        Assert(SpellManager.CalculateTrackedCooldownRemaining(1_000, 1_250)
                   == TimeSpan.FromMilliseconds(250),
            "a cast attempt must suppress retries during its confirmation window");
        Assert(SpellManager.CalculateTrackedCooldownRemaining(1_250, 1_250)
                   == TimeSpan.Zero,
            "an elapsed tracked deadline must become immediately probeable");
        Assert(!SpellManager.IsCooldownReady(0.250, 100, true),
            "an observed cooldown outside lag tolerance must remain blocked");
        Assert(SpellManager.IsCooldownReady(0.075, 100, true),
            "lag-tolerant casting must become eligible close to the cooldown boundary");
        Assert(!SpellManager.IsCooldownReady(0.075, 100, false),
            "strict callers must wait for the full cooldown");
        Assert(!SpellManager.IsCooldownReady(-1, 100, true),
            "an unusable spell must never be treated as ready");
        Assert(SpellManager.IsTrackedCooldownBlocking(
                TimeSpan.FromMilliseconds(75), 100, true, true),
            "lag tolerance must not shorten the post-cast confirmation gate");
        Assert(!SpellManager.IsTrackedCooldownBlocking(
                TimeSpan.FromMilliseconds(75), 100, true, false),
            "an observed cooldown may use the configured lag tolerance");
        Assert(!SpellManager.TryParseAvailability(Array.Empty<string>(), out _),
            "a Lua execution failure must not be confused with a ready spell");
        Assert(SpellManager.TryParseAvailability(new[] { "ok", "0" }, out double ready) && ready == 0,
            "only a marked zero result may report an authoritative ready spell");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
