# W42 scan-entry checkpoint — PR #42

Recorded 14 September 2026. Resume existing draft PR #42, `audit/next-42-scan-entry-20260914`, based on PR #41 at `d08d0b20f6187e9a423489fbcfebed9d1b275537`. Re-read remote heads before writing. Full W42 and the exhaustive architecture/refactoring audit remain incomplete.

## Recovered work versus this verification continuation

The fifteen-case test commit `d9db8db451c1c399a74cdb54551d2985e144c532` and production repair `01ec5542448ae5b5087fccd4ca2f17c61906e6d5` already existed when this retry inspected GitHub. Their Windows runs also already existed. They were recovered and verified, not recreated, newly authored, or newly executed by this verification continuation. The PR body and root handovers had lagged behind those commits.

This continuation successfully updated `GITHUB_CONNECTION_RECOVERY.md` through the native connector as `b770d035b161ccee87233b9810f73ccac2f74039`, then read back PR42's head. It downloaded the four complete archives below, checked their evidence, and prepared this documentation-only checkpoint. No new C# execution, production edit, merge or deployment is claimed for this recovery step.

The uploaded PR39 completion report/prompt describes older work. PR39's repair, PR40's normalization and PR41's unavailable-input/retry/scan-exception repairs must not be replayed. PR40 branched from PR39's earlier repair `1dccead8`, not PR39's later supplemental runner/docs `3d849392` / `318c0a7d`; do not invent linear ancestry. The retained tests now execute in their original normalized project. PR39's separate supplemental runner was not recreated or newly run here.

## Bounded defect and existing repair

Before the repair, `DoScan` called `VendorDataLoader.GetNearestVendors` before entering the scheduler's invalidation point. A vendor-location exception could leave the previous selected or grind child authorized to continue ticking. The tests execute actual world/vendor/DoScan/refresh/execution-gate owners against controlled external player/memory observations; prior publication is seeded only to start the running child.

The repair adds three lines at `WholesomeAutoQuest.cs:1154-1156`: call existing `scheduler.InvalidatePublishedWork` inside the current-lease scan callback before vendor discovery. It preserves public signatures, existing unavailable-world/data handling, cancellation propagation, finally-based refresh-lease release, conservative ActiveQuestIds protection, and obsolete/replacement-generation controls. It does not introduce an unconditional late catch that can erase replacement work.

Counterevidence is retained: `ObjectManager.IsInGame` already converts failed memory observations to unavailable/false. Closed-memory world cases are compatibility controls, not newly reproduced world-read exception defects. The replacement-generation case remains passing. This is not proof of a whole-log atomic snapshot, session continuity, native frame lifetime, ABA immunity, successful publication freshness or live client/server acceptance.

## Exact source and recovered Windows evidence

Tested repaired commit: `01ec5542448ae5b5087fccd4ca2f17c61906e6d5`.

Tested tree: `542526f07b84bfcc4ab2671e5aa69c640afbc583`.

| Evidence | Run / artifact | Actual inspected result |
|---|---|---|
| Clean test-first current variant at d9db8db4 | 34806012900 / 10333380384 | Scan entry 10/15: five intended assertions, zero unexpected errors. Normal aggregate exit 1. |
| Repaired focused current at 01ec5542 | 34806763262 / 10333104684 | Scan entry 15/15; prior scan-failure14/14, revocation13/13 and retry6/6. Broader W42 remains red. |
| Actual combined at 01ec5542 | 34806763203 / 10333461502 | All17 build/setup0;14 run0, three W42-containing entries run1. Same scan-entry15/15 and retained groups execute. |
| Host compilation at 01ec5542 | 34806763318 / 10332224535 | Build0,3278 warnings/0errors; explicit tests_run:false and game_attached:false. |

Exact outer digests are in `W42_SCAN_ENTRY_EVIDENCE.json`. The red baseline is the current artifact of the test-first commit, NOT the normalized matrix's separate historical pinned-production variant.

All four outer SHA-256 values matched authenticated GitHub artifact metadata and all ZIP CRC checks passed. Red and repaired focused archives each contain47 file members,46 internally hash-listed; combined contains71 file members,70 internally hash-listed. Every listed internal digest matched, with the manifest itself the only excluded file. The host archive contains four files and no internal digest manifest; only its authenticated outer digest, CRC, source commit and supplied contents are asserted.

All1681 tracked input hashes match between repaired focused and combined. Red-to-green input sets are identical and only `runtime-snapshot/Bots/WholesomeAutoQuest-master/WholesomeAutoQuest.cs` changes. Both focused archives contain46 nested source files, all matching their working-source manifests; only that production file differs. All26 normalized output files/manifests are byte-identical red-to-green and focused-to-combined after removing the packaging-only `0` versus `W42Normalized` directory segment. The original test/fixture/extractor/normalizer/native-boundary sources were not weakened or replaced.

The scan-entry test blob is `0d3bfc1b7f743ad73c00e682d57d8a06237ea6c1` in both runs. Production blob changes from `7e6165d1b7d6845c2f68067ed57c03e4b89d8863` to `8e92bb6f4b7018e022a5ab335b204aee86db1e4a`.

All historical14 integrated entries plus3 W42 entries remain. Original sale23/23, relation23/23, completion-owner15/15,33 Python analyzer tests and99 compiled Singular source files are retained. Original Wholesome Main and later groups finish; their success text does not override the aggregate failure. Managed execution uses Windows x86/.NET10.0.12; analyzers use Python. No game attached. Build warnings remain; no warning-free claim.

## Remaining red evidence and limits

Observation main4/25 consists of19 missing proposed CaptureSnapshot contracts plus2 sale assertions. Ready1/5 retains4 assertions. Scheduler2/5 consists of2 missing HasCompleteQuestLog contracts plus1 null-accepted-to-pickup assertion; the older unavailable-world NullReferenceException is not the current result. Extracted boundary16/24 retains8 assertions and0 unexpected errors. Missing APIs are not reproduced production bugs; overlapping suite counts are not unique defects.

Preserve the explicit test-only deny-native-dispatch assembler boundary from PR41. It changes only private test output; repository Lib, host output, Navigation.dll and installed files are not replaced. Its sources/targets are unchanged in this comparison. No claim is made that separately rebuilt boundary binaries are byte-identical or that native shutdown is fixed. Older callback-at-exit crashes remain separate counterevidence, not clean baselines.

## Next bounded investigation — not another completed repair

At this exact source, `QuestScheduler.ScanAndRefresh` assigns `LastSchedule` around line143 before `ProfileBuilder.BuildProfileXml` and `WriteProfile` around lines170-173. `DoScan` then attempts `ProfileManager.LoadNew` around lines1174-1176. `WholesomeExecutionGate.Tick` around lines815-821 consumes current authorization for an already-running child. Source ordering is confirmed; clean adversarial reproduction of successful stale publication and XML/write/load failures remains open.

Trace and test that actual chain, including selected/grind running children, failure/cancellation during build/write/load, null/no-output behavior, obsolete leases and replacement generations. Establish the publication owner's compatibility and lifecycle before designing staged versus committed authorization; do not merely suppress the next scan or use a helper-only plan model. A late unconditional catch is not an acceptable substitute for ownership-safe revocation. Preserve normal complete/empty and validated-grind behavior and conservative item protection.

This does not replace the larger dependencies: trustworthy raw occupied IDs/slot identity and read-success separate from metadata completeness; real player/session/frame provenance and same-character reconnect/ABA; ready-history ownership; sale dispatch freshness; native completion and original-client acceptance. Memory.ReadInternal can return default after failed ReadBytes, so zero alone is not proof of an empty slot. Equal counts/scans and character names do not prove atomicity or session continuity. Unknown must not mean abandonment, completion, free capacity or permission for destructive side effects.

## Recovery and authorization

Read `GITHUB_CONNECTION_RECOVERY.md`. The owner reports reconnecting helped. The retry exposed89 actions and an actual native file write/readback succeeded; this does not prove token expiry or a connection root cause. If actions disappear, perform one bounded check, remind the user to reconnect, then rediscover/retry once. When they have already reconnected, retry rather than asking for the same step again. Container DNS/local .NET absence is separate from connector publishing. Never request/export tokens or misuse GET. Interrupted chat output is not evidence that remote writes failed: recheck heads before repeating work.

Preserve master `8382a7ec05a64212ea0a237159dca427a0767425` and backup `audit/backup-master-before-approved-merge-20260913` at `c43c50d8d5d6775055f19bf018b52930a264d4a4`. PR34 already merged,35/36 preserved,25 excluded. No merge, force push, deployment, installed-bot/native/mesh/runtime-capture changes. No Lua changed. Independent review and supervised original WoW3.3.5a build12340 acceptance remain pending.

After W42, retain: owner-scoped item protection; atomic FILE-protection reload (FILE clearing before parsing, not runtime/profile clearing); merchant/mail/discard/consumption; vendor discovery/settings; rest/restart/cancellation; special quests; roster; combat/support; native/lifts. Historical evidence and plan files are retained, not rewritten to make badges green.
