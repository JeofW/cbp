# Aura observation and worker liveness implementation plan

**Goal:** An unavailable aura observation must remain UNKNOWN without terminating the worker or preventing independent death/recovery work.

**Architecture:** Keep the legacy complete-collection contract, identify unavailable observations with a specific exception, and add a nonthrowing observation boundary for consumers that can defer a sample. Contain that specific condition at the worker's optional pulse boundary and summarize repeated diagnostics. Do not change the verified 680-byte Spell row or invent missing metadata.

**Tech stack:** C# / .NET 10, Windows x86, original WoW 3.3.5a build 12340; existing deny-native-dispatch regression harness.

**Spec:** User opening instruction, October 1 logs retained under `D:/Dev/CopilotBuddy-Evidence/aura-liveness-20261002`; research policy `docs/audit/WOTLK_335A_RESEARCH_POLICY.md`.

## Constraints and review focus

- Preserve current master 59041dde, deployed PR87 d949ff16, and the separately preserved collision work. Do not attribute the pre-deployment 16:25 log to PR87.
- Complete aura coverage is required for negative buff/proc/dispel decisions. Empty or partial collections cannot represent an unavailable valid actor.
- Cancellation, explicit Stop and replacement worker/bot ownership must win over recovery.
- A missing telemetry sample must suspend work/death attribution instead of charging active time or quarantining work.
- Diagnostics must retain first occurrence, last occurrence, count and owning consumer within bounded memory/output; throwing log subscribers must not turn an unavailable observation into a worker failure.
- Missing active spell metadata must report its lookup stage. Recovered metadata must be retryable; no negative cache or fabricated spell.

## Task 1: Real worker regression and controlled observation boundary

Files: `Tools/WholesomeQuestRecoveryRegressionTests/AuraWorkerLivenessRegressionTests.cs`, `Styx/WoWInternals/WoWObjects/WoWUnit.cs`, `Styx/Logic/BehaviorTree/TreeRoot.cs`, new `Styx/Helpers/ObservationUnavailableException.cs`.

- [ ] Use the existing allocated-memory Spell-row/world fixture to drive real `GetAllAuras` and `TreeRoot.WorkerThread`, with a controlled bot pulse and independent root/death probe. Reproduce IDs 61988 and 56817, then known/empty collections, repeat ticks, cancellation and Stop/owner replacement.
- [ ] Record the failing-before run through `PostMergeAuditRegressionTests` before editing production.
- [ ] Add a typed unavailable exception preserving `InvalidOperationException` compatibility; contain only understood observation failures and preserve Stop/cancellation. Recheck bot/root/run identity after external callbacks.
- [ ] Run focused worker and existing aura/row/composite ownership groups.

## Task 2: Consumer deferral, bounded diagnostics and metadata failure evidence

Files: `WholesomeAutoQuest.cs`, `Logging.cs`, `WoWDb.cs`, `WoWSpell.cs`, `WoWAura.cs`, focused regression groups and existing source-extraction harness dependencies.

- [ ] Add failing cases for unknown rest coverage suspending objective progress/death attribution, repeated legacy-reader failures retaining conservative behavior with bounded logs, and metadata recovery after failure.
- [ ] Provide an explicit `TryGetAllAuras` result and migrate material Wholesome rest/progress/death consumers. UNKNOWN must not claim an aura absent, completed work, or safe action.
- [ ] Summarize typed observation failures by consumer with count/first/last and bounded storage; retain ordinary exception and cancellation behavior.
- [ ] Carry a concrete metadata failure reason from localized row lookup to the unavailable aura diagnostic while leaving decoder geometry unchanged.
- [ ] Run existing extraction tests with their real newly introduced dependencies; do not weaken assertions.

## Task 3: Evidence, publication and continuation

- [ ] Retain exact full-log counts/chronology, deployed identity and proof limits. Bind source/test/IDA evidence to the candidate revision.
- [ ] Run focused and relevant full local suites, runtime-source compilation and exact dataset accounting. Publish a scoped PR, verify remote diff/head and Windows/x86 hosted receipts, merge only on success.
- [ ] Build/smoke merged master and verify intervening runtime changes before deploying changed payload files while preserving protected settings/state; verify every payload hash.
- [ ] Keep quest10161, subsequent-node travel, duplicate defensive/heal requests, food/drink, death and quarantine as explicit continuing incidents. Reconcile the preserved collision work on the correct baseline in its own scope.

The aura work is one iteration of the continuing audit, not completion of the broader request. No game is attached during deterministic tests, and live acceptance must be recorded separately.
