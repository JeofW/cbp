# W110 full-Wholesome CLR investigation

## Reproduction and retained evidence

The earlier clean-source comparison passed the default whole run and exposed five optimizing-JIT stack-frame attribution assertions. The recovered 6da1c368 harness correction replaced an incidental inlinable vendor-frame expectation with compiled call-chain evidence while retaining actual exception/read/lease assertions. Its later whole default and whole optimized runs passed. Those results were non-reproduction of the historical fatal failure, not a cause diagnosis.

The subsequent HIGHRISKROAM broad gate reproduced a fatal error: **16/17**, isolated constructor child **PID10572**, native **0xc0000005** in coreclr.dll at **0x000DAADC**, managed report **0x80131506 / Gen2GcCallback.Finalize**. Windows report ID **979a0e12-c8d7-404d-908f-779cd7a4a63b**. The child passed all twelve constructor cases and the first real matching-recipient case before failing during the next profile compilation.

Dump `D:\Dev\CopilotBuddy-Evidence\W110_HIGHRISK_ROAM_CLR_RECURRENCE_20260929\dotnet-10572.dmp` has SHA256 `cd1df9ca658de0e36186f1edcc4e4c893b35ed6e74b528edcc2f7c9e16b5b9f4`. The recurrence directory retains its Windows events, failed full package, 116 executable/PDB/dependency snapshots and runtime hashes. Offline analysis shows the finalizer at the SharedArrayPool<IntPtr> callback prestub and the main thread inside CompileBatch/AssemblyLoadContext during CheckMatchingRecipient. It exposes zero readable GC heaps/objects; the tool's 'no corruption' text therefore cannot certify heap integrity. The older PID12408 dump instead stopped near the matching-recipient argument comparison. Neither limited dump records the initiating write.

## Demonstrated harness defect and repair

Actual source chain: `QuestStrategyConstructorDispatchRegressionTests.CheckMatchingRecipient` -> retained shared `QuestPublicationRegressionTests.Fixture` -> real GossipEvent -> `WoWObject.TryInteractCore` -> `StyxWoW.ResetAfk` -> `LastHardwareAction` -> `Memory.Write<uint>` -> WriteProcessMemory. ResetAfk executes before the missing native-executor refusal. PerformanceCounter can fall back to the OS tick count, so that path does not require a game executor. The fixture supplied writable pseudo handle -1 for its own CLR process; address0x00B499A4 is a client global, not an allocation owned by that fixture.

A separate safe canary test used the exact fixture, real Memory and native access calls, targeting only four bytes allocated by the test. It demonstrated successful uint, byte-array and raw writes and the pseudo handle's lack of disposal lifetime. Five intended failures became8/8 after changing only the shared fixture to OpenProcess(PROCESS_VM_READ, current PID) and closing the owned handle after detaching it. Setup still writes exclusively to allocated fixture storage through Marshal. The actual constructor, matching-recipient, refusal and ownership assertions were not removed or replaced.

Windows documents separate read/write rights: https://learn.microsoft.com/en-us/windows/win32/procthread/process-security-and-access-rights . The test verifies enforcement rather than assuming the requested rights worked.

## Result and limits

The repaired source passes all17 ordinary local groups, the entire optimized Wholesome run and hosted17/17. Constructor12/12, matching-recipient13/13 and canary8/8 are included. This proves removal of the demonstrated unsafe write capability and successful broad execution of the repaired harness. It does **not** prove that a particular historical instruction performed the write in either limited dump, or that every possible CLR failure is resolved. A future recurrence requires the exact source/runtime/binaries/PDBs, a full memory/native-context dump and a trace identifying the first invalid write. No production clock/interaction logic or installed runtime configuration was changed.
