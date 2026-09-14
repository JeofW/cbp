# W42 scan-exception continuation of draft PR41

## Reconciliation and retained work

Read W42_PR39_TO_PR41_RECONCILIATION.md and GITHUB_CONNECTION_RECOVERY.md. PR39's nonzero raw-acceptance repair, PR40's post-initialization aggregation, and PR41's unavailable-owner revocation and timed observation retry already existed. This continuation does not recreate them. Master/backup and all exclusions remain unchanged; no merge or deployment is authorized.

## Source-confirmed owner chain

At dc441aaaf3817e23905d23d8bdad81f70c2af92b, QuestScheduler.ScanAndRefresh invalidates null/unavailable-player and missing-data early returns. A valid input that subsequently throws in QuestRecoveryRuntime.EnsureConfigured, quest observations, or position capture can bypass those invalidations. Its old LastSchedule and CurrentProfilePath remain visible to the already-running WholesomeExecutionGate. WholesomeAutoQuest.DoScan catches all Exception instances and returns false, also swallowing ThreadInterruptedException and OperationCanceledException. RunPendingRefresh already completes its RefreshGate lease in finally.

Competing hypotheses retained: an unavailable-world guard is not the same as failure during a valid-owner scan; returning false from DoScan does not itself revoke existing execution; blindly invalidating in a late catch can instead erase replacement-generation publication. A stale lease must not gain the right to mutate a newer generation. No raw quest-log completeness, native memory atomicity or session generation is inferred from these observations.

## Executed test-first record, not yet a clean final baseline

Commit eb236c453269d587ab0d0e4291f10f9753b8940f adds only QuestScanFailureRegressionTests.cs. Actual normalized run34801562658/artifact10331438498 and actual combined run34801562652/artifact10331548360 both build it successfully and execute all fourteen new cases: four pass, ten intended assertions fail, and no unexpected exception occurs inside that case group. Later retained groups and the original Wholesome Main finish. However both processes then terminate with a garbage-collected System.EventHandler callback error (0x80131623). That terminal failure is NOT a scheduler assertion or a clean red/green baseline. Preserve the complete logs; do not suppress the exit or classify the whole executable as a normal assertion-only failure.

Commit6877382d818cdf2f169bc34da301f562fe2f0c3d changes only test setup: allocate an uninitialized Memory object and explicitly seed its external handle/cache fields instead of JIT-compiling the constructor that references fasmdll_managed; keep all actual Memory.Read/QuestLog/host/scheduler methods. The running child now uses Composite.Execute, preserving Composite's normal LastStatus/Stop behavior. All fourteen case assertions and controls remain. It also records loaded assembler assembly names. Source confirms that Memory's constructor references the native assembler, but the exact process-exit crash cause is not established by that alone. Inspect the new Windows artifacts before claiming fixture isolation resolved it.

Controlled reads use a current-thread pseudo-handle, not a game/process handle, and seed the real Memory cache. Unseeded reads cannot access a game. The fixture does not establish real ReadProcessMemory success, native dispatch, frame ownership, client/server acceptance or whole-log completeness. It does exercise real QuestLog enumeration, ScanAndRefresh, DoScan, RefreshGate and the already-running WholesomeExecutionGate; no alternate scheduler model is substituted.

## Bounded implementation plan after clean baseline

1. Confirm all fourteen assertions still execute with unchanged production and no unclassified terminal failure. Treat missing contracts and fixture failures separately from behavior assertions.
2. Revoke old execution permission before potentially failing fresh observations at the actual ScanAndRefresh owner. Preserve conservative ActiveQuestIds item protection and public exceptions. Do not add an unconditional late revocation that can erase newer-generation publication.
3. At DoScan, propagate ThreadInterruptedException and OperationCanceledException rather than treating stop/cancel as ordinary scan failure. Preserve the existing finally-based refresh-lease release and stale-lease control.
4. Keep tests unchanged from the clean baseline; run affected plus the actual all-17 integrated workflow and host build at the same repaired commit. Verify complete archives, source identities/hashes and every case. Retain all historical14 integrated entries, original23 sale assertions and all pending W42 failures.
5. Update PR41 and root handover files with exact red/green evidence, graph links and the next unresolved owner. Do not label combined green while unrelated W42 assertions remain.

This plan does not close successful scan publication before ProfileBuilder.WriteProfile/ProfileManager.LoadNew, raw accepted identity completeness, cache hydration/loss, false-success memory reads, ABA, real reconnect/session provenance, ready-log history or sale dispatch freshness. Those remain the next W42 dependencies. The exhaustive architecture/refactoring audit and full W42 are incomplete. No Lua, native binary, mesh, installed-bot or deployment changes are part of this slice.
