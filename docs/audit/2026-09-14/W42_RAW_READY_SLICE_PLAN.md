# W42 raw observation and ready-history continuation

> Execute inline with systematic debugging, test-driven development and verification-before-completion. This is the next bounded part of the owner's approved continuation, not a broad audit restart. Keep PR42 draft and unmerged.

**Goal:** retain raw occupied quest identities independently of metadata, distinguish unsuccessful reads from genuine empty slots, and stop readiness changes or changed owners from fabricating quest-log departures.

**Architecture:** add a bounded observation owner beside the existing QuestLog, using the real Memory.ReadBytes success result and uncached reads of the existing player/descriptor/quest-slot addresses. Preserve legacy GetAllQuests and completion APIs. Consume raw accepted identity in the existing Wholesome readiness observer. Equal observations detect observed changes; they do not establish atomicity, same-character reconnect/ABA safety, or a native frame/session lease. Do not invent a generation counter and label it session provenance.

**Tech stack:** original WoW3.3.5a build12340, existing Windows x86/.NET10 workflows; Python artifact verification; no game attached.

**Specification:** NEXT_CHAT_PROMPT.md and W42_PUBLICATION_CHECKPOINT.md at86ba683215b72695ea0496536cb74c23fa279a84; the owner's request to proceed from that checkpoint. The publication repairc237ca5b and its30 passing cases already exist and must not be recreated.

## Established source and constraints

- [x] Re-read current PR42 and newest open PRs: head86ba6832, PR43 is only the separate capability test. Read current continuation and the supplied publication handoff. Re-execute its saved-evidence verifier; this is not new C# execution.
- [x] Re-read protected master8382a7ec05a64212ea0a237159dca427a0767425 and backupc43c50d8d5d6775055f19bf018b52930a264d4a4. Neither is a write target. Preserve PR34's prior merge,35/36 and the W42 stack; exclude25.
- [x] Trace GetAllQuests -> GetQuest -> PlayerQuest.FromId, real Memory.ReadBytes/ReadInternal, and Wholesome's current ready-set difference. The legacy list drops occupied unhydrated entries. ReadInternal can convert a failed byte read into zero. The ready observer compares prior readiness with current readiness, not accepted-log membership, and retains no owner provenance.
- [x] Observe exposed native GitHub writers. This plan's returned commit and readback, not account permissions or discovery, establish a new write operation.

No merge, force push, deployment, installed-bot/Navigation.dll/mesh/runtime-capture/Lua changes, or Work/Codex switch. Preserve all original assertions, the private deny-native-dispatch boundary, original23 sale cases, the30 publication cases and all17 combined entries. Native execution remains forbidden in the isolated owner fixtures.

## Task 1 — test-first ready behavior and raw owner contracts

Files: add Tools/QuestLogObservationRegressionTests/RawReadyRegressionTests.cs; extend only the external byte-read surface in BoundaryFixture.cs; add Tools/WholesomeQuestRecoveryRegressionTests/QuestRawLogMemoryRegressionTests.cs. Preserve original tests, extractors and normalization assertions.

- [ ] Add ready-observer cases through the extracted actual production body: raw departure once, accepted-but-not-ready, missing metadata without departure, GUID/descriptor/player/memory replacement, unavailable observation followed by recovery, multiple simultaneous departures and duplicate/invalid raw identities. Distinguish ordinary owner failures from fixture setup errors. Keep normal successful controls.
- [ ] Add actual Windows Memory.ReadBytes cases using test-process-owned allocated storage and the retained isolated fixture: real zero-filled readable log versus inaccessible memory returning null/default; missing metadata retains observed IDs; cache bypass/restoration; descriptor/raw GUID changes; duplicate/invalid IDs; snapshot immutability; revalidation without rematerialization; cancellation where controlled external boundaries can raise it.
- [ ] New CaptureSnapshot/IsSnapshotCurrent contracts are specifications until implemented, not reproduced production defects. Label missing-contract failures separately from actual ready-behavior assertions. No copied permission model or mocked production snapshot implementation.
- [ ] Publish test/support changes first. Inspect the complete current-variant Windows artifact for the exact test commit, all retained controls and intended failure categories before production changes. Do not substitute the separately pinned historical baseline.

## Task 2 — bounded raw observation and ready-owner repair

Files: Styx/Logic/Questing/QuestLog.cs (partial declaration only); new Styx/Logic/Questing/QuestLogSnapshot.cs; runtime-snapshot/Bots/WholesomeAutoQuest-master/WholesomeAutoQuest.cs (ready owner and its lifecycle reset).

Interfaces: QuestLog.CaptureSnapshot() returns an immutable QuestLogSnapshot with AcceptedQuestIds, ReadyQuestIds, Quests, IsIdentityComplete and IsComplete. QuestLog.IsSnapshotCurrent(QuestLogSnapshot) performs a fresh raw comparison without metadata hydration. QuestLogSnapshot.HasSameOwner(QuestLogSnapshot) compares the observed player/memory/base/descriptor/raw-GUID ownership only; document that this is not a continuous-session certificate.

- [ ] Capture the existing25 slots in a bounded500-byte read; require successful full-length bytes, valid nonzero matching raw owner GUIDs and descriptor pointers. Reject address overflow, invalid/duplicate occupied IDs and observed owner/content changes. Preserve already-observed IDs when metadata hydration fails. Do not treat default zero from a failed typed read as proof of emptiness.
- [ ] Scope cache bypass to the raw observation reads and restore the caller's cache setting. Preserve cancellation/interruption; ordinary unavailable reads/metadata produce incomplete observations rather than empty-log permission. Keep identity completeness distinct from metadata completeness.
- [ ] Keep the candidate non-complete when raw state changes during hydration; do not claim that matching before/after samples rule out ABA. Revalidation must neither hydrate again nor mutate old snapshots.
- [ ] Extract ObserveReadyQuestLog in the existing Pulse location. Compare prior ready IDs with current raw accepted IDs only for comparable observed owners. Missing/ambiguous observations and owner changes reset readiness history. Reset readiness history at the existing lifecycle reset. A departure requests a rescan; it does not prove a successful turn-in or mark a quest completed.
- [ ] Retain normal flag-ready observations and genuine departure behavior. Do not connect the new snapshot to sale/scheduler destructive permission in this slice without separate actual-consumer tests; those remain explicitly open.

## Task 3 — verification and durable checkpoint

- [ ] Use existing push-triggered normalized, combined17 and host workflows at the same repair commit; inspect completed runs and complete artifacts. No inferred dispatch tool or credential workaround.
- [ ] Verify authenticated outer digests, ZIP CRC, supplied internal manifests, exact input/fixture identities, every build/run result and original assertion retention. Report broader W42 red groups as red; missing APIs and overlapping cases are not unique bug counts.
- [ ] Save the exact source/evidence/fix graph, updated root handoff, self-review plus an independent-review request, and a downloadable verified transfer. Keep existing publication/scan/completion records and original attachments intact.

## Explicitly retained work

Scheduler completeness and already-running authorization after later raw changes; final sale freshness and metadata-safe protection; true frame/session provenance and reconnect/ABA; live native completion acceptance remain open. Preserve QUEST_TRAVEL_COVERAGE.md's phase-parity, XYZ/route-cost and lift-lifecycle requirements. This slice does not repair or certify the owner's live cliff/wind-rider reports. Follow the retained post-W42 dependency order afterward.

Recovery remains explicit @GitHub retry -> fresh ChatGPT conversation with GitHub selected -> reconnect only if that fresh conversation still lacks publishing. No repeated reconnect loop, token export or GET write.
