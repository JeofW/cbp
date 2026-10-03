int failed = 0;
foreach (var (name, run) in new (string, Action)[] {
    ("CompiledHostMemory", NetworkClientEntryRegressionTests.RunCompiledHostMemory),
    ("NetworkClientEntry", NetworkClientEntryRegressionTests.Run),
    ("NetworkFlightIntegration", NetworkFlightIntegrationRegressionTests.Run),
    ("FlightorWaitContinuity", FlightorWaitContinuityRegressionTests.Run) })
{
    try { run(); Console.WriteLine("PASS full entry group: " + name); }
    catch (Exception error) { failed++; Console.Error.WriteLine("FAIL full entry group: " + name + ": " + error); }
}
return failed == 0 ? 0 : 1;
