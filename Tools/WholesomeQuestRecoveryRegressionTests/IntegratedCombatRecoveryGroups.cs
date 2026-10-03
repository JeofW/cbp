using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

// A single ordinary initializer keeps the retained aggregate's reviewed entry
// contract. The focused runner invokes these same six actual-source tests.
internal static class IntegratedCombatRecoveryGroups
{
    [ModuleInitializer]
    internal static void Run()
    {
        var errors = new List<Exception>();
        foreach (Action test in new Action[]
        {
            IntegratedConsecrationAreaTests.Run, IntegratedControlAuraTests.Run,
            IntegratedPaladinHealContinuationTests.Run, IntegratedQuestTargetAcquisitionTests.Run,
            IntegratedRacialAdmissionTests.Run, IntegratedRestPauseTests.Run
        })
        {
            try { test(); }
            catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Connected combat/recovery regressions", errors);
    }
}
