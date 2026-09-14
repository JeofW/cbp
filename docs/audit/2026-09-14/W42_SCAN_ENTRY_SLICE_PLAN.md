# W42 scan-entry authorization implementation plan

> Execute inline with systematic debugging, test-driven development and verification-before-completion. This is a bounded dependency slice, not full W42 or exhaustive-audit closure.

**Goal:** prevent a failed vendor-observation phase in a current refresh from leaving the previous quest/grind child authorized to tick.

**Architecture:** retain the actual `DoScan -> RunLeaseFencedRefresh -> VendorDataLoader.GetNearestVendors -> ScanAndRefresh` ownership chain. Reuse existing `InvalidatePublishedWork` and `WholesomeExecutionGate`; do not invent a helper-only permission model or an observation/session API. Revoke within the current-lease callback before fallible vendor discovery, not in a late catch that can revoke a replacement generation.

**Tech stack:** original WoW 3.3.5a build12340; Windows x86/.NET10 managed execution; Python test normalization; GitHub native GitDB writes and push-triggered Actions. No game attached.

**Spec and recovered parent:** `NEXT_CHAT_PROMPT.md` and `W42_SCAN_EXCEPTION_CHECKPOINT.md` at PR41 checkpoint `d08d0b20f6187e9a423489fbcfebed9d1b275537`. Its tested source is `7bf54c1ede756867968b250e8472d9c9428337af`. The uploaded PR39 files are historical and do not reset this frontier. Preserve PR39 completion repair, PR40 normalization, and PR41 unavailable-input/retry/scan-exception repairs.

## Constraints and counterevidence

- Preserve master8382a7ec and backupc43c50d8, all prior branches/PRs, inherited35/36 and excluded25. No merge, force push, deployment, native DLL, mesh or runtime-capture changes.
- Keep every old assertion, original23 sale cases, historical14 integrated entries plus3 W42 entries, and the existing test-only deny-native-dispatch boundary.
- `ObjectManager.IsInGame` already catches memory-read exceptions and returns false. Closed-handle world observations therefore remain unavailable-world controls, not a newly reproduced exception defect. Do not change that owner or broaden the production patch based on the earlier hypothesis.
- `VendorDataLoader.GetNearestVendors` evaluates map/class/location before selecting vendors. A controlled throwing Location getter must be shown in that actual call chain before the scheduler identity getter runs.
- Source-confirmed ordering is not yet a reproduced failure. Compilation errors, unexpected fixture failures and missing proposed APIs are not production reproductions.
- Read and preserve `GITHUB_CONNECTION_RECOVERY.md`. Reconnection appeared to restore missing actions; no root cause is proven. If actions disappear, one bounded check, remind the owner to reconnect, rediscover/retry once. Never request/export credentials or use GET for writes.

## Task 1 — executable failing-before evidence

Files: add `Tools/WholesomeQuestRecoveryRegressionTests/QuestScanEntryRegressionTests.cs`; extend only the branch allowlist in `.github/workflows/audit-w42-normalized.yml`.

- [ ] Publish fifteen new cases with production unchanged. Existing normalization discovers the new group automatically; no saved test source is rewritten.
- [ ] Execute actual world and vendor owners with controlled external memory/player state. Seed prior scheduler publication only to initialize an already-running production execution gate.
- [ ] Retain unavailable-world, exact exception identity, ordinary-error return, obsolete-lease, replacement-generation, normal vendor-to-scheduler and normal-running controls.
- [ ] Inspect the new group through every result and final process status. Intended vendor assertions include selected/grind running children, thread interruption, cancellation and finally-based refresh-lease release.

Representative assertion, using helpers defined in the committed test file:

```csharp
EnableVendors(bot);
player.PositionFailure = new InvalidOperationException("controlled vendor failure");
Invoke(bot, "DoScan", scheduler, CurrentLease(bot));
Stopped(root, child, context);
Idle(scheduler);
```

Run the existing `Audit W42 normalized baseline and current owners` workflow through an authenticated branch push. The new clean red baseline is the **current** artifact from this test-only commit. The matrix's separate pinned historical baseline is not interchangeable with that source.

## Task 2 — smallest owner repair, only after clean red

File: `runtime-snapshot/Bots/WholesomeAutoQuest-master/WholesomeAutoQuest.cs`, the current-lease scan callback in `DoScan`.

- [ ] Once the vendor gap is reproduced, invoke existing scheduler invalidation before vendor observations. Keep public signatures and existing specific unavailable-world/data handling.
- [ ] Preserve cancellation propagation, bounded observation retry and conservative `ActiveQuestIds` sale protection.
- [ ] Do not add unconditional catch/finally invalidation. The retained replacement-generation control must stay executable.
- [ ] Leave all test/fixture/normalizer/native-boundary bytes identical to the clean red baseline.

## Task 3 — exact-source verification and durable handoff

- [ ] Run affected normalized/current, the actual17-entry integrated workflow and host compilation on the same repaired commit.
- [ ] Download complete archives; compare authenticated outer digests, internal manifests, embedded commit/tree, normalization outputs and all suite results. Inspect existing red groups, not just the new group or badges.
- [ ] Record the evidence/test/fix/PR graph and preserve world-read counterevidence. Distinguish controlled reproduction and regression verification from live acceptance.
- [ ] Update root resume/next-prompt pointers and save a downloadable checkpoint/handoff. Preserve the connection-recovery reminder in the handoff.

## Remaining dependencies, explicitly outside this slice

Successful stale publication and XML build/write/load failures; LastSchedule assignment before XML completion; raw occupied identity/read-success versus optional metadata; actual session/frame lifetime, same-character reconnect and ABA; readiness/history transfer; sale freshness; complete scan-to-profile publication and executing-plan ownership. No whole-log atomicity, native completion or client/server acceptance claim is permitted.

After W42, retain the saved dependency order: owner-scoped item protection; atomic FILE reload (not runtime/profile clearing); merchant/mail/discard/consumption; vendor discovery/settings; rest/restart/cancellation; special quests; roster; combat/support; native/lift work.
