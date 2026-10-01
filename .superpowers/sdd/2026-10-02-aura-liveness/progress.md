# SDD ledger — plan: docs/superpowers/plans/2026-10-02-aura-liveness.md

Current iteration: repair shared aura UNKNOWN/liveness; broader user audit continues after this scope.

## Recovery and preserved work

Master/origin recovered at 59041ddea9bf0dae68589c2aaf492b9d2962d985. All 434 deployed payload files match PR87 d949ff16d6396fdda788268648799ac04349b89d. Seven unpublished collision files from D:/Dev/CB-CollisionRecovery-20261001 and both Git patches were preserved unchanged under D:/Dev/CopilotBuddy-Evidence/aura-liveness-20261002. All relevant worktrees were checked before editing; PR51's pre-existing two documentation changes remain untouched.

Candidate worktree: D:/Dev/CB-AuraLiveness-20261002, audit/next-aura-liveness-20261002. First test commit e03302d1bb770c366859e35d0f44114c89b2dffd. No push, PR, merge or production change yet.

## Evidence and gates

External immutable receipts: D:/Dev/CopilotBuddy-Evidence/postmerge-20260930-pr61/aura-*-20261002.

- aura-baseline: existing collection/Spell lookup/packed-row groups 3/3 passed.
- aura-worker-red3: actual aura reader -> real TreeRoot worker: 3/9 passed, 6 expected assertions, zero unexpected errors. First two runner attempts contained fixture compile/setup errors and are not behavioral-red evidence.
- aura-worker-green1: worker 9/9, Spell lookup and packed row 45/45; all three focused groups passed. Source snapshots stable.
- aura-consumer-red: actual CreateLiveWorkSample -> progress/death monitor: 1/7 passed, six failures for escaping UNKNOWN and missing explicit observation API. Source snapshots stable.
- aura-consumer-green1: all eight focused groups passed, including the seven consumer cases, actual worker, collection read validity, compiled rest call chain, rescan ownership, Spell lookup, packed decoder and execution watchdog. Source snapshots stable. Broader runtime compilation and hosted gates remain pending.

Exact-client IDA read-only evidence reconfirms original 680-byte Spell rows and the current bounded decoder. Client SHA256 bf644876709c591acc17c0da8cdf1814edcc9f1e6bc109a8c0d5c38c79dc953c. Actual live 61988/56817 lookup header/pointer/mode/bytes were not logged; the precise lookup failure stage remains unproven. Do not claim it was the historical 704/680 defect.

Full October 1 logs and summaries are retained under the new evidence directory. 21:11 has six aura-associated worker exits with no user Stop. 16:25 has 27,383 direct aura61988 exceptions plus four wrapped, and 2,040 aura56817 exceptions; its three worker exits follow user Stop. The older session precedes PR87 deployment and must not be assigned PR87 merely because those bytes are currently deployed.

## Rulings and pending work

Ruling: preserve legacy complete-collection rejection with a typed InvalidOperationException subtype; offer explicit TryGetAllAuras for deferrable consumers. This prevents both false absence and worker termination while retaining compatibility. Cost: legacy predicates remain conservatively unavailable until metadata recovers.

Ruling: extend existing extraction/IL tests through the actual new helper chain instead of removing the real aura requirement. The controlled actor/spell boundaries remain external observations, never substitutes for the behavior under test.

Task 1 and 2 now have focused green evidence, including owner replacement, unknown target publication, shared pulse/event starvation, wrapped cancellation, high-cardinality diagnostics and log-sink failures. Metadata failure-stage diagnostics are implemented without changing the decoder. The six new groups contain 96 scenarios. Further receipts: aura-metadata-green1 (8/8 groups), aura-shared-green1 (8/8 groups), aura-diagnostics-green1 (4/4 groups); their earlier failing-before counterparts are retained. Source snapshots remained stable during each run. Full local/runtime compilation and exact-SHA hosted gates remain required before publication/merge/deploy claims.

The current report is docs/audit/2026-10-02/aura-observation-liveness/REPORT.md. Source reviewed inline because no reviewer-agent tool is exposed; do not call this an independent review. Scope is one shared observation/liveness iteration. The metadata-read root cause for the historical live IDs is still unknown until the new diagnostic is captured live.

Full local `aura-final-b784223a-20261002` completed with source stable: 33/34 stages passed; the Wholesome aquatic-rest group exposed missing target coverage blocking pause release. All other stages, host and Singular compatibility passed. The fixture was updated to supply a complete healthy target observation at its existing world boundary, retaining every original assertion. Eight additional actual-Pulse cases then failed before the repair (19/27, eight assertions, zero unexpected errors); `aura-rest-target-green-20261002` passed all six groups after separating unsafe-pause release from target-dependent rest admission. New scenario total is 104. The full gate must be rerun on the corrected commit.

Read-only continued incident analysis retained 369 recovery records: 65 InvalidQuestData quarantines with six-hour probes, 13 LegacyUnknown quest-stage imports with no probe deadline, and no cross-quest SourceKey evidence. The full debris timeline contains one logged debris loot submission and a next distinct object selection, not proof of 30 authoritative progress increments. Dataset cardinality remains exactly 4,335 rows/IDs.

The persisted quest10161 ledger further records a progress increase at 21:15:15.409 local, with 14 in the first required-item count slot. Its prior vector and live item-ID array were not retained, so keep per-action attribution and full completion limits explicit. Source `QuestProgressObservation` is unchanged from deployed PR87.

Retain quest10161 objective acknowledgements, successor-node travel, defensive/heal arbitration, rest admission, death/resurrection and recovery quarantine as unresolved incident lanes; no simulation here proves live completion. Preserve the separate collision work until reconciling it in its own scope.
