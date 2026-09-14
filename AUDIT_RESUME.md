# CopilotBuddy audit — W42 scan-exception checkpoint

Recorded 14 September 2026. Resume draft **PR #41**, branch **`audit/next-42-ready-owner-20260914`**, not the older W41 root instructions or uploaded PR39 ZIP. Re-read current remote heads before writing. The exhaustive architecture/refactoring audit and full W42 remain incomplete.

## Connection recovery: retained owner instruction

Read **[GITHUB_CONNECTION_RECOVERY.md](GITHUB_CONNECTION_RECOVERY.md)**. When GitHub actions unexpectedly disappear, become read-only or stall, perform one bounded check and remind the owner to **reconnect the GitHub plugin, then retry in this conversation**. Reconnection appeared to restore actions previously; the cause is not proven. Rediscover and retry once after reconnection. Do not repeat an extended read-only audit. Missing local gh/.NET or container DNS is not proof that native connector writes fail. Never request/export tokens or misuse GET for writes.

## Current verified source

**Production/test commit: `7bf54c1ede756867968b250e8472d9c9428337af`.**

**Tree: `8d9b058cdfe2a7e4edd16fb552c070a5f1227e8b`.**

A later checkpoint commit must be documentation-only relative to this source before inheriting its evidence. This continuation actually published native connector commits/ref updates and triggered Windows Actions. The test runner was GitHub Windows, not a local C# runtime. No new duplicate PR was created: the existing draft PR41 was extended.

The new bounded repair revokes previous execution permission before fresh identity/log/context reads in actual `QuestScheduler.ScanAndRefresh`, and preserves interruption/cancellation through `WholesomeAutoQuest.DoScan`. It uses the existing invalidation, timed retry and execution-gate owners. It does not clear conservative ActiveQuestIds item protection and does not introduce an unconditional late catch that erases newer-generation publication. Public signatures remain unchanged.

Clean unchanged-production baseline `c3d455d0d8c6988d8b3f796f02b7928053022b51` executes all fourteen new cases: four controls pass, ten intended assertions fail, zero unexpected case errors. At `7bf54c1e`, the same source assertions execute **14/14** in both focused and actual combined workflows. Existing thirteen revocation and six retry cases also pass. All 25 generated normalization outputs are byte-identical red-to-green; comparing all 1,680 tracked input hashes finds only the two intended production files changed.

## Actual combined status — NOT all green

Focused run **34803757188**, artifact **10332441880**. Combined run **34803757306**, artifact **10332172475**. Host run **34803757195**, artifact **10332122465**. Complete archives, hashes, source identities and every suite result were inspected. Exact SHA-256 digests and retained counterevidence are in the evidence file below.

The original combined workflow retains all historical14 entries plus three W42 entries: **17/17 build/setup exit0, 14/17 run exit0**. Three W42-containing entries remain red. The original Wholesome main and retained groups execute instead of being masked by an initializer; relation23/23, original sale23/23, completion-owner15/15, 33 analyzer tests and compilation of99 Singular files are retained. Managed execution is Windows x86/.NET10.0.12; analyzers are Python. Independent host compilation succeeds with3278 warnings/0errors and `tests_run:false`.

Remaining reported checks: observation main4/25 (nineteen missing proposed CaptureSnapshot contracts and two sale assertions), ready1/5, scheduler2/5 (two missing proposed HasCompleteQuestLog contracts and one null-log-to-pickup assertion), boundary16/24 with eight assertions/zero unexpected errors. Counts overlap and are not unique bugs. Do not implement a fake contract merely to change a badge.

## Reconciled existing work — do not recreate

PR39's completion-owner repair at `1dccead847f330430f148609858c6f89d5724ad8` was already done; its final source `3d8493927adb76e886b1e1ccca03a93cce0cfb8b` and documentation head `318c0a7d9e8518a468a52af05134aaad3e401eee` are historical sibling records. PR40 at `877de5cd9d332d10d3d1586a78be6b6cff2c504b` already normalized test execution. PR41 already contained unavailable-owner revocation at `61a2ff714a7b606b2ea2441f91e04d9b624c00ce` and timed retry at `dc441aaaf3817e23905d23d8bdad81f70c2af92b`. The earlier retry result was recovered and verified, not rerun under a different claim.

PR40 branched from the earlier PR39 repair, not its later supplemental runner/documentation. Do not assume linear ancestry or rebuild those independent records. The original retained tests now run inside their original normalized executable; the separate PR39 supplemental runner was not recreated or claimed newly executed.

## Read next and preserve these limits

Read [W42_SCAN_EXCEPTION_CHECKPOINT.md](docs/audit/2026-09-14/W42_SCAN_EXCEPTION_CHECKPOINT.md), [W42_SCAN_EXCEPTION_EVIDENCE.json](docs/audit/2026-09-14/W42_SCAN_EXCEPTION_EVIDENCE.json), [W42_SCAN_EXCEPTION_GRAPH.json](docs/audit/2026-09-14/W42_SCAN_EXCEPTION_GRAPH.json), and **[NEXT_CHAT_PROMPT.md](NEXT_CHAT_PROMPT.md)**. Keep W42_PR39_TO_PR41_RECONCILIATION.md, W42_SCAN_EXCEPTION_SLICE_PLAN.md, PR40 aggregation evidence, PR39 completion evidence and W41 behavior/graph records as historical evidence, not obsolete starting instructions. The prior root text is preserved unchanged in W41_ROOT_HANDOVER_RETAINED.md.

The new actual-memory fixture exposed a separate process-exit native callback failure. Clean managed testing uses an explicit deny-dispatch assembler type boundary only in the private Wholesome test output. The original repository Lib, host output and installed/native files are unchanged. This is not a production native fix. Boundary source/targets are identical across red/green; rebuilt binary hashes differ and are separately recorded, not asserted byte-identical. Native shutdown remains unverified.

Next close real read-success/raw accepted identity, metadata completeness and actual session/frame ownership, then actual scan/materialization/profile publication and current executing-plan authorization, ready history and sale freshness. Exceptions before the repaired capture point, vendor discovery before ScanAndRefresh, and failures after LastSchedule assignment/WriteProfile/LoadNew remain explicit gaps. Equal scans/counts or character names cannot prove atomicity, session continuity or ABA immunity.

Master remains `8382a7ec05a64212ea0a237159dca427a0767425`; backup `audit/backup-master-before-approved-merge-20260913` remains `c43c50d8d5d6775055f19bf018b52930a264d4a4`. PR34 is already merged;35/36 are inherited and25 excluded. No merge, deployment, installed-bot update, Navigation.dll replacement, runtime-capture rewrite or managed-test mesh download. Original WoW3.3.5a build12340, Windows x86/.NET10; execute Lua5.1 checks for any future Lua change. No Lua changed in this slice. Independent review and live acceptance remain pending.
