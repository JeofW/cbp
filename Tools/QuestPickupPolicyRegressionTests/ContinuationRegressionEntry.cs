using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

// Execute all recovered groups before reporting failure. Individual groups still
// retain their assertions and failures; one group must not hide the next group.
internal static class ContinuationRegressionEntry
{
    [ModuleInitializer]
    internal static void Run()
    {
        var errors = new List<Exception>();
        try { PickupObservationRegressionTests.Run(); } catch (Exception error) { errors.Add(error); }
        try { MissingOfferEvidenceRegressionTests.Run(); } catch (Exception error) { errors.Add(error); }
        if (errors.Count != 0) throw new AggregateException("Continuation regression groups failed", errors);
    }
}
