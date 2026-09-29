# W110 retained publication diagnostic - actual recurrence and bounded repair plan

The saved W110 goal required investigation of the retained intermittent publication assertion on a real recurrence. Such a recurrence occurred during seal-candidate5b41250c validation. This is distinct from the missing PaladinSupport test-settings adapter and from the new78/78passing seal behavior. No passing rerun is substituted for a root-cause explanation.

## Actual observation

Integrated36393191843/job108833430296/artifact10957043036 has SHA256d1c892d3aef730128ccab9a37b625e29b798ce35e7a9cba7be5483f55cb02287. Its retained Wholesome log4109-4122 records the intended real UnauthorizedAccessException from writing to a directory. The top frame is System.IO.File.WriteToFile; the next frames are the actual QuestScheduler publication lambdas and ScanAndRefresh. The expected ProfileBuilder.WriteProfile wrapper frame is absent. The original assertion at QuestPublicationRegressionTests.cs113 therefore fails. Other write-failure ownership/idle-gate controls pass, and publication has20/21,1assertion/0unexpected.

The inspected source unambiguously calls _profileBuilder.WriteProfile(xml) at QuestScheduler.cs292. ProfileBuilder.cs508-514 is a short null-output guard around File.WriteAllText and the path return. There is no alternative direct writer in that scheduler branch or swallowed exception in the wrapper. The missing frame is consistent with optimization/inlining of this small method, not a failure to exercise filesystem writing. This narrows the earlier unknown diagnostic without pretending that source inspection alone gives a full JIT-disassembly record.

Microsoft's primary StackTrace documentation states that optimization transformations may omit method calls; the primary MethodImplOptions documentation defines NoInlining as forbidding inlining. References reviewed28September2026:
- https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.stacktrace
- https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.methodimploptions

## Bounded experiment, not assertion removal

New test-only baseline a1a330e7b9444d058f8e52a22823101ad94c8a69 retains all original publication assertions and adds ProfileWriterDiagnosticRegressionTests. It calls the actual writer with direct and explicitly optimized callers, real directory/missing-parent/invalid-name failures, and positive successful-write/overwrite/null-output cases. It also asserts an explicit optimization-stable diagnostic-owner contract. Behavioral failure must appear in the actual error-path controls, not only in the metadata-flag assertion, before the planned boundary annotation is treated as verified.

The separate PaladinSupport configuration fake gains only four disabled-default getters, restoring its ability to compile the linked new Ret source. This missed compatibility adapter changes no old test assertion, weight, role, buff or decision. The first seal red-to-candidate comparison remains frozen and verified: all183normalized members unchanged, only the two seal production paths changed; actual78/78passed but aggregate15/17didnot. This test-only successor is a new baseline for the independently observed diagnostic defect, not a claim that the earlier candidate was fully green.

After meaningful hosted diagnostic red, the minimal planned source change is a NoInlining annotation on the real WriteProfile boundary, preserving all statements, return values and exception types. It makes an existing diagnostic requirement explicit instead of altering the original assertion, fabricating a stack frame, swallowing the filesystem error, disabling optimization globally or modifying any permissions. Cost: a managed call boundary remains around a filesystem operation; no throughput/latency improvement is claimed or measured.

The source change must then pass the new diagnostic group, original21publicationcases, seal78 and all17integratedgroups, with a new exact artifact/source/hash comparison and unchanged fixtures across that diagnostic repair. The same original error should still be an UnauthorizedAccessException; a stable owner frame is the desired result. Actual hosted results and precise publication identities will be added to the execution/evidence record after they exist.

## Executed experiment and result

The diagnostic baseline a1a330e7 compiled successfully and executed all13cases: **6pass,7intendedassertions,0unexpected**. Six failures were actual optimized/direct-after-success error-path frame losses; the seventh was the explicit runtime-contract assertion. Direct-call and successful-write/overwrite/null-output controls passed. Thus the experiment reproduced behavior, not merely a missing annotation. The original intermittent21case group happened to pass in that baseline; that does not erase the previously retained failure.

After this red, source **a199953f7b465fd91d0ad7faa10252e6605ae8b2** added only NoInlining plus its explanatory comment to the existing writer. Its method body, return and exception types remain byte-for-byte unchanged. Hosted final **13/13diagnostic,21/21originalpublication,78/78seal and17/17integrated** all pass, host0errors/3344warnings. All184normalizedmembers and the wholeTools tree are unchanged from diagnostic red to green; only the three-line production annotation/comment changes. Exact source/artifact/hash evidence is in W110_SEAL_EVIDENCE.json and the retained execution receipts.

This closes the now-reproduced writer-frame diagnostic stability problem while preserving the original assertion. It does not claim a repair to file permissions, operating-system access, arbitrary future publication errors, or all sources of missing stack frames. No global optimization setting or artificial exception was introduced.
