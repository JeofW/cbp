# Native result ABI and caller-visible timing — test-first slice

The pinned C++ NavPathFindStep uses None=-1 and Start/End/Init/Update/Finalize/Straight=0..5. The public C# enum has different existing values, and Navigator.FindPath currently casts the raw integer directly. Native initialization failure 2 therefore appears as FindEndPoly. Preserve public enum values; explicitly translate native codes and retain the raw code. Unknown future codes must not masquerade as a known public stage.

Six regression scenarios use the actual checked-in x86 Navigation.dll with a deliberately absent map (999999), so no mesh download or attached client is needed. The mapping-table fixture uses the baseline source's direct-cast operation until the owning translation method exists; the missing-map fixture independently exercises the real native-to-managed boundary.

A controlled 250ms test-only lock hold demonstrates that existing Elapsed starts after the lock and excludes caller wait. Preserve this legacy measurement for compatibility; add explicit CallerElapsed, LockWaitElapsed, NativeCallElapsed and ManagedAndCleanupElapsed. The latter includes all non-native work/cleanup/callbacks and must reconcile to total; it is not claimed as fine-grained CPU profiling. Measure monotonic elapsed time and include every returned failure/unloaded outcome. No background game-object access or production sleep is introduced.

Test results are pending. This repairs diagnostic attribution and visibility, not native search speed or cancellation. The source-built format6 candidate must be tested with the same wrapper after integration.
