# Local constructor-child crash: retained dump analysis

The previously failed full-Wholesome invocation remains a **native CLR process failure**, not an assertion failure, a compiler failure, or evidence that a mount/quest production repair failed. Its original cause remains unresolved. New read-only dump analysis locates the failure in the fixture's managed argument-preservation check but does not establish whether the initiating fault was runtime code, earlier unsafe harness/native memory activity, or another process component. No production repair or assertion change is justified by the available evidence.

## Exact retained evidence

The Windows Application Error at28September2026,20:45:28+08 records `dotnet.exe`/`coreclr.dll` version10.0.1226.42308, native exception0xc0000005, module offset0x000d7ac6. The test parent retained CLR error0x80131506 and child exit-2146233082. Two matching Windows events, one WER report and one timestamp-matched dump were recovered without rerunning the unchanged failing suite.

The original dump was copied into the approved evidence directory and its bytes matched: **4,938,514 bytes**, SHA256 **5081a561f5892c95459cf37b0c0513e89db9949630a239e9eb493c691bcb8d87**. The copy and original were left unchanged. No crash configuration, game process, production installation or live debug session was changed.

Microsoft's documented Windows/x86 `dotnet-dump` direct download was retained under the evidence directory with a valid Microsoft Corporation Authenticode signature and SHA256 **212de78f29c248b65402925906065f5b8ba1dc1186805d2bd8c184236d7162de**. Analysis used the matching local10.0.12 DAC/DBI. The tool is a managed dump analyzer; its official documentation explicitly distinguishes it from a native debugger. See `https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-dump`.

External records:

- `W110_CONSTRUCTOR_CRASH_INSPECTION_20260928.json`: exact event/WER/dump metadata and current runtime hashes.
- `W110-constructor-crash-analysis/receipt.json`: copied dump and signed-tool provenance, successful analysis invocation and unchanged dump hash.
- `W110-constructor-crash-analysis/analysis.log`: CLR/thread/register/module and managed-stack output.
- `W110-constructor-crash-analysis/heap-and-arguments.log`: exception and additional stack/heap inspection.

The original WER event identifier and stored WER report identifier differ and both are preserved in the raw receipt. Matching used process/module/version/time and the actual dump; no report identity was invented to force equality.

## What the dump establishes

The selected fault thread has `System.ExecutionEngineException`, HResult0x80131506, with no managed exception stack. Its managed stack contains:

```
IEnumerable<KeyValuePair<...>>.GetEnumerator() [StubDispatchFrame]
Enumerable.SequenceEqual<KeyValuePair<...>>(...)
QuestStrategyConstructorDispatchRegressionTests.CheckMatchingRecipient()
QuestStrategyConstructorDispatchRegressionTests.RunMatchingRecipientCases()
QuestStrategyConstructorDispatchRegressionTests.RunIsolated()
```

This corrects the earlier inference based only on printed summaries: the child had reached a matching-recipient case after printing the12 constructor results, even though it never printed the final13-case matching summary. Source review locates `SequenceEqual` in the check that a generated behavior constructor did not mutate its captured arguments. The dump does not supply a complete native stack or all optimized managed arguments, so it does not identify the exact failing case or prove which earlier operation caused the invalid state.

SOS explicitly reports that the stack walk is incomplete. Its heap check reports **0 objects verified,0 errors** before printing “No heap corruption detected.” Zero inspected objects cannot establish a clean heap; that final phrase is not accepted as exoneration of prior memory corruption.

The current fixture's associated storage uses owned64KiB and16KiB allocations, seeded client observations, null native executor and restoration of the original globals/cache before freeing its storage. The source reviewed here does not demonstrate an out-of-range write or a live game dispatch at the failing comparison. This review is not a proof that all earlier unsafe/native/harness activity is harmless, and the current on-disk build is not assumed byte-identical to every missing image from the old dump.

## Disposition

An unchanged isolated constructor/matching run previously passed12/12 and13/13. A later meaningfully changed full local17-group gate and the published002682af hosted17-group gate also passed. These results are retained at their own source boundaries and **do not diagnose or repair the historical crash**.

No unchanged rerun, runtime downgrade, disabled assertion, replacement of `SequenceEqual`, speculative product patch, or fabricated behavioral red was used. This is an **unresolved runtime/harness/native diagnostic dependency**, with production causation unproven. A recurrence needs the exact failing binaries/PDBs plus a full heap/native-context dump and matching symbols to distinguish runtime defect, unsafe-memory corruption and environmental contribution. That diagnostic limit does not block independent source-actionable navigation repairs, and it remains explicit in final acceptance and handoff records.
