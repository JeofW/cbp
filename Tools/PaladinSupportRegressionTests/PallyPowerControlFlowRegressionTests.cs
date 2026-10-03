using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Singular.ClassSpecific.Paladin;
using Singular.Settings;
using Styx;

internal static class PallyPowerControlFlowRegressionTests
{
    internal static Exception? Signal;

    [ModuleInitializer]
    internal static void Run()
    {
        int total = 0, failed = 0;
        foreach (bool wrapped in new[] { false, true })
        foreach (bool interrupted in new[] { false, true })
        foreach (string policy in new[] { "SelectNormalBlessing", "SelectAura" })
        {
            total++;
            Fixture.Reset();
            SingularSettings.Instance.Paladin.UsePallyPowerAssignments = true;
            Signal = interrupted ? new ThreadInterruptedException("owned interruption") : new OperationCanceledException("owned cancellation");
            Fixture.LuaResult = _ => throw (wrapped ? new TargetInvocationException(Signal) : Signal);
            Exception? caught = null;
            try { typeof(Common).GetMethod(policy, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { StyxWoW.Me }); }
            catch (TargetInvocationException error) { caught = error.InnerException; }
            if (!ReferenceEquals(caught, Signal))
            {
                failed++;
                Console.Error.WriteLine($"FAIL PallyPower control: {policy}/wrapped={wrapped}/interrupted={interrupted}: signal swallowed or changed");
            }
        }
        Signal = null; Fixture.Reset();
        Console.WriteLine($"PallyPower control scenarios: {total - failed}/{total}; linked support owner; shared control helper is an external boundary already covered by recovery adapter tests.");
        if (failed != 0) Environment.ExitCode = 1;
    }
}

namespace Styx.Logic.Combat
{
    // This suite owns support selection, not the recovery adapter. Model the
    // shared helper's already-tested contract to detect support swallowing its
    // signal; RecoveryActionAdapterRegressionTests exercises the actual helper.
    public static class RecoveryActions
    {
        public static void ReportDeferral(Exception error, string owner) => RethrowControlFlow(error);
        public static void RethrowControlFlow(Exception error)
        {
            while (error is TargetInvocationException { InnerException: not null } wrapped) error = wrapped.InnerException!;
            if (ReferenceEquals(error, PallyPowerControlFlowRegressionTests.Signal)) ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
