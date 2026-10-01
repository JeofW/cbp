# Full-routine distance type correction

Candidate1a690878211592001cfc63f8dc09645cbc6410eb was never accepted or published. Its full local gate caught a source compilation error: actual WoWObject.DistanceSqr is double, while the initial controlled observation was float. That allowed float.IsFinite to compile in the focused dynamic fixture but not in the complete Singular routine.

Use double.IsFinite in the Hunter predicate and represent Distance/DistanceSqr as double in the fixture. The fixture now reflects the actual public WoWObject property types before compilation, so an incompatible boundary cannot silently pass again. All582 Hunter cases and the existing97 dispatch and53 sight cases pass on the corrected source. No earlier assertion or infrastructure check was removed. The failed aggregate/compatibility receipts remain bound in test-receipts.json and require a new clean final-SHA gate.

The438-case historical baseline still documents376 intended rank/dispatch/ownership assertions; it is not relabelled as the corrected582-case fixture. Complete-routine compilation remains an independent acceptance gate.
