using System;
using System.Runtime.CompilerServices;
using WholesomeAQ;

internal static class RestLatencyRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        TestBalancedRestAndLootPolicy();
    }

    private static void TestBalancedRestAndLootPolicy()
    {
        var settings = new WholesomeAQSettings();
        Assert(settings.RestHealthPercent == 45 && settings.RestManaPercent == 30
               && settings.RestResumeHealthPercent == 75 && settings.RestResumeManaPercent == 60,
            "balanced rest defaults must use separate start and resume thresholds");
        Assert(!WholesomeRestPolicy.ShouldStartRest(44, 100, false, true, false, settings),
            "noncritical health must not preempt pending loot");
        Assert(WholesomeRestPolicy.ShouldStartRest(30, 100, false, true, false, settings),
            "critical health may preempt pending loot");
        Assert(!WholesomeRestPolicy.ShouldStartRest(20, 100, false, true, true, settings),
            "an immediate hostile threat must return control to combat safety");
        Assert(WholesomeRestPolicy.ShouldYieldRest(55, true, false),
            "pending loot above critical health must interrupt routine rest");
        Assert(WholesomeRestPolicy.IsRecovered(75, 60, true, settings),
            "mana users must resume at both recovery thresholds");
        Assert(!WholesomeRestPolicy.IsRecovered(75, 59, true, settings),
            "mana users must continue recovery below the resume mana threshold");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
