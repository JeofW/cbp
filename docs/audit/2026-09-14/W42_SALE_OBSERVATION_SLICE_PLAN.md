# W42 sale-observation implementation plan

> Execute inline with systematic debugging, test-driven development and verification-before-completion. This continues the owner's approved next step; keep PR42 draft and unmerged.

**Goal:** prevent Wholesome merchant dispatch when accepted quest observations are incomplete or change while exclusions are being collected.

**Architecture:** consume the existing real QuestLog.CaptureSnapshot and IsSnapshotCurrent owners in the actual SellByQuality body. Preserve accepted/scheduled/shared/consumable exclusions and ordinary stable sales. The final raw comparison detects observed changes; it is not a native transaction or continuous-session/ABA guarantee.

**Tech stack:** original WoW3.3.5a build12340; existing Windows x86/.NET10 normalized, combined17 and host Actions; Python evidence verification. No game attached.

**Spec:** NEXT_CHAT_PROMPT.md, W42_RAW_READY_SLICE_PLAN.md, W42_PUBLICATION_CHECKPOINT.md, and the owner's continue request. The already-published raw/readiness repair1bee278caad627ff3fc06b499e691bf008f6f27c and publication repairc237ca5b are retained, not recreated.

## Reconciled starting point

The interrupted continuation actually published nine commits after86ba6832, ending at1bee278c. Its original30 publication cases, raw-memory18/18, raw-ready16/16 and lifecycle3/3 pass in the recovered current artifact10337699577/run34820810995. That archive's authenticated SHA256 is68b2fa413f67771f86dce2b1c9167189cfd33de16a4def93776882ed41073e63. These are recovered executions, not new work by this retry. Complete recovery verification and updated handovers remain to be checkpointed.

The same actual observation runner still reports two sale assertions: occupied cache miss authorizes selling, and same-count replacement during inventory observation authorizes selling. Its full QuestLog/PlayerQuest owners and extracted actual sale body establish a concrete consumer gap. The sale code still calls GetAllQuests and does not use the new raw snapshot. Scheduler integration remains separate.

## Constraints

Preserve master8382a7ec05a64212ea0a237159dca427a0767425 and backupc43c50d8d5d6775055f19bf018b52930a264d4a4; both were reread before this write. Preserve PR34's prior approved merge,35/36, the W42 stack and separate capability-test43; exclude25. No merge, force push, deployment, installed/native/mesh/runtime-capture/Lua changes or Work/Codex switch. Recovery is explicit @GitHub retry, fresh selected conversation, reconnect only if that fresh conversation lacks publishing. Never export tokens or use GET for writes.

## Task 1 — test-first actual sale/observation reproduction

- [ ] Add Tools/QuestLogObservationRegressionTests/SaleObservationRegressionTests.cs with28 cases through the extracted real sale body and the full real QuestLog/PlayerQuest/snapshot owners. External raw bytes, cache, inventory and merchant are controlled; no copied production snapshot model.
- [ ] Cover missing metadata, failed/partial raw reads, duplicate/invalid IDs, hydration changes, acceptance/departure/progress changes during inventory observation, player/GUID/descriptor/memory/world changes, final cancellation identity, and stable/empty/disabled/closed-merchant controls. Ordinary metadata failure propagation or safe deferral are both accepted if no sale occurs; do not mislabel a legacy exception as fixture failure.
- [ ] Extend only the historical23 sale unit-test fixture's external QuestLog double with a explicitly scripted stable observation. Its existing inputs/assertions remain unchanged. This double is not raw-read/freshness evidence; the new owner suite links the actual snapshot implementation. Publish this compatibility support before the repair and keep its bytes identical red-to-green.
- [ ] Commit tests/support/plan only; inspect the complete current-variant Windows artifact. Require intended assertion failures and zero unexpected errors. Retain every old group and the aggregate nonzero status. Do not substitute the historical pinned baseline.

## Task 2 — narrow consumer repair

Only production target: runtime-snapshot/Bots/WholesomeAutoQuest-master/WholesomeAutoQuest.cs, private SellByQuality.

- [ ] Replace the lossy list read with the existing snapshot, veto incomplete observations, then retain the current exclusion-building logic over its materialized quest handles.
- [ ] Immediately before merchant dispatch, alongside existing player/frame guards, call the same quest log owner's IsSnapshotCurrent(snapshot). Do not swallow cancellation or recapture a different snapshot and apply exclusions from the old one.
- [ ] Preserve original23 sale assertions, all30 publication cases and successful ordinary sales. Do not clear conservative scheduler protection or bypass missing/ambiguous database vetoes. Do not claim inventory/merchant acknowledgment or atomicity from an offline dispatch count.

## Task 3 — verification and durable recovery checkpoint

- [ ] Execute focused current, actual combined17 and host workflows at the same repaired commit. Verify complete archives, authenticated outer digests/CRC/internal manifests, source identities, all suite results and fixture invariance. Retain broader scheduler/boundary failures rather than weakening them.
- [ ] Reconcile the already-published raw/readiness repair and inspect its saved combined/host/red evidence without attributing it to this retry.
- [ ] Publish updated root pointers, bounded checkpoint/evidence/graph and independent-review request; retain earlier plans and evidence. Provide a verified downloadable handoff.

Open dependencies: scheduler completeness and already-running authority after later raw changes; real frame/session ownership and reconnect/ABA; protection lifetime and shared reload; merchant batch/acknowledgment and other destructive callers. Retain QUEST_TRAVEL_COVERAGE.md's XYZ, phase-parity, wind-rider and lift-lifecycle requirements; this slice does not fix or certify those live symptoms.
