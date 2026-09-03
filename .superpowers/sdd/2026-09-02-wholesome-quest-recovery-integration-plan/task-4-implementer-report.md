# Wholesome Task 4 Implementer Report

## Outcome

Implemented the Task 4 progress monitor and coalesced refresh lifecycle in the external Wholesome runtime. The bot now translates pickup, navigation, hotspot, active-work, objective-progress, and attributable-death evidence into scoped recovery outcomes. It owns exact quest-stage attempt generations and uses those exact tokens for every success report.

The old restart timer, automatic quest blacklist writers, no-hotspot/death blacklist sets, arbitrary active-quest attribution, and scheduler compatibility adapters are removed. Recovery requests are coalesced through a thread-safe `RefreshGate` and consumed by the bot pulse. A successful rebuild loads its generated or caller-vetted profile exactly once; scan expansion and timed idle suppress the stale quest order, and timed idle waits for `EarliestRetryUtc`. No recovery path calls `TreeRoot.Start`.

The live active-work mapping excludes loading/out-of-world, dead/ghost, taxi/transport, Buy/Sell/Repair/Mail/Train and other non-quest POIs, resting, user pause, unrelated combat, and work owned by another quest or stage. Objective progress calls `ReportProgress`, resets stall/death escalation, releases the exact attempt generation, and permits the same live behavior to claim a later generation.

`Stop()` sets explicit stopped state, cancels refresh, resets monitor/ownership/scheduler state, detaches the death event through an exactly-once lifecycle gate, flushes recovery state, and calls `base.Stop()`. A later deliberate start creates one subscription set. The timer was removed rather than disposed because it no longer exists.

Task 5 UI/diagnostics were not implemented. Existing manual quest IDs are only translated through `SetManualBlacklist`; `SettingsForm.cs` and `WholesomeAQSettings.cs` were not changed.

## TDD Evidence

Production-linked regressions were added before each corresponding production contract. The observed RED sequence was:

1. Build failed on missing `RefreshGate`, `WholesomeLifecycleGate`, `WholesomeProgressMonitor`, `QuestWorkSample`, `WholesomeAttemptOwnership`, and `WholesomeAutoQuest.CreateWorkSample`.
2. After the pure seams were green, the linked DLL failed because `QuestScheduler.SyncBlacklist` and `BlacklistQuestGiver` still existed.
3. Build failed on missing `QuestScheduler.ReleaseActivation`, proving that progress could not yet let the same behavior acquire a later generation.
4. Build failed on missing `WholesomeAutoQuest.TryClearRecoveryOutcomePoi`, proving outcome-scope POI cleanup was not exposed to the production-linked test.
5. Build failed on missing `WholesomeAutoQuest.IsCompletedOwnedStage`, proving pre-order-exhaustion lifecycle refresh was absent.
6. Build failed on missing `WholesomeAutoQuest.ShouldExecuteQuestRoot`, proving timed-idle/scan-expansion suppression of an old order was absent.
7. Build failed on missing `WholesomeAutoQuest.PickupOutcomeFingerprint`, proving an observation and later pickup failure with identical dialog evidence were not safely distinguishable.
8. The linked DLL exited 1 on `path and hotspot probes captured outside active quest work must not create recovery failures`, proving excluded-state probes needed the same active-work gate as the clock.

After the minimal production changes and final explicit empty-hotspot/vendor-exclusion assertions, GREEN was:

```text
Build succeeded.
3278 Warning(s)
0 Error(s)
Wholesome scheduler recovery regression tests passed.
```

The linked tests cover 100 concurrent refresh requests producing one pending refresh, later completion, stopped callbacks, repeated start/stop handler counts, exact-generation ownership, same-behavior generation renewal, active-only work clocks, long no-pulse gaps, eight active minutes/two clusters, endpoint then all-hotspot escalation, zero-hotspot areas, three attributable deaths/15 minutes, progress reset, exact production snapshot mapping, scoped POI clearing, pickup coalescing, stage completion refresh, timed-idle order suppression, and removal of legacy adapters.

## Fresh Verification

All builds used `D:\World of Warcraft 3.3.5a\CB\.codex-source\.dotnet-x86\dotnet.exe`, `Platform=x86`, `UseAppHost=false`, `--no-restore`, `--no-incremental`, and direct DLL execution. No x86 apphost was created or launched.

```text
[Wholesome]
BUILD_EXIT=0 TEST_EXIT=0 SCOPED_COMPILER_DIAGNOSTICS=0
Build succeeded.
3278 Warning(s)
0 Error(s)
Wholesome scheduler recovery regression tests passed.

[QuestRecoveryCore]
BUILD_EXIT=0 TEST_EXIT=0
Build succeeded.
3246 Warning(s)
0 Error(s)
Quest recovery regression tests passed.

[QuestPickupCore]
BUILD_EXIT=0 TEST_EXIT=0
Build succeeded.
3246 Warning(s)
0 Error(s)
Quest pickup policy regression tests passed.

[FullReleaseX86]
BUILD_EXIT=0
Build succeeded.
3250 Warning(s)
0 Error(s)
```

Scoped diagnostics counted actual compiler warning/error lines naming `WholesomeAutoQuest.cs`, `QuestScheduler.cs`, or the Wholesome regression `Program.cs`; the count was zero. Remaining diagnostics are the repository's existing package/compiler warning baseline, including offline NU1900 vulnerability-feed warnings.

Static and integrity checks:

```text
PLAN_FORBIDDEN_SCAN:
WholesomeAutoQuest.cs:433: forceStop: () => TreeRoot.Stop(),

AUGMENTED_FORBIDDEN_SCAN_MATCHES=0
TREE_ROOT_START_COUNT=0
LEGACY_ADAPTER_COUNT=0
PROFILE_LOAD_COUNT=1
BASE_STOP_COUNT=1
REPORT_PROGRESS_COUNT=1
SUCCESS_FACTORY_COUNT=1
BACKUP_HASH_MISMATCHES=0
TRAILING_WHITESPACE=0
WHOLESOME_APPHOST_EXISTS=False
CORE_APPHOST_EXISTS=False
PICKUP_APPHOST_EXISTS=False
```

The single plan-scan match is the pre-existing, explicit user-clicked Settings `Force Stop` callback. It is isolated from recovery/rescan paths and is the exception permitted by the Task 4 plan. The augmented scan checked restart timers, legacy quest blacklist file APIs/writers, quest abandonment, arbitrary `ActiveQuestIds.First`, grind-file discovery, and `TreeRoot.Start`; it returned no matches in the Task 4 runtime files.

## Files Changed

Repo-local files included in the Task 4 commit:

- `Tools/WholesomeQuestRecoveryRegressionTests/Program.cs`
- `.superpowers/sdd/2026-09-02-wholesome-quest-recovery-integration-plan/task-4-implementer-report.md`

External/local Wholesome runtime files intentionally left outside Git:

- `D:\World of Warcraft 3.3.5a\CB\Bots\WholesomeAutoQuest-master\WholesomeAutoQuest.cs`
  - SHA-256: `A54BBEBE88D09A3CDA444C5752435AD38C60E6E1EF2AE99B952EB184868219CA`
- `D:\World of Warcraft 3.3.5a\CB\Bots\WholesomeAutoQuest-master\QuestScheduler.cs`
  - SHA-256: `B621479733879722609AA7B1785BA29E9AF85D97D898B3D2A82C5DFE1FA0B921`

No core source was changed for Task 4.

## Preservation and Concerns

- The rollback backup at `D:\World of Warcraft 3.3.5a\CB\Backups\quest-recovery-wholesome-20260903-080658` was not modified; all seven manifest entries re-hashed successfully.
- Existing vendor blacklist behavior remains vendor-scoped. Its former global stop/restart handoff now clears the vendor/trainer POI and queues the same coalesced scheduler refresh because the global timer was removed.
- The visible Task 5 recovery diagnostics/actions remain deferred. The current manual-ID field is bridged to the core manager without adding new UI.
- Existing unrelated vendor/grind working-tree changes remain untouched and are excluded from the commit.
- No push, deployment, live binary replacement, backup mutation, or apphost execution occurred.

## Review Round 1 Corrections

All six review findings were corrected with production-linked regressions before the minimal production changes:

- Failure reporting no longer synthesizes `Success`. `QuestAttemptOutcome` failures can carry an exact `AttemptKey` and `AttemptGeneration`; the manager rejects stale/ownerless failure callbacks against an active attempt, applies valid generated failure policy under the manager lock, and releases that exact ownership in the same transition. Endpoint failure releases the stage attempt while retaining the stage's earlier reason, episode count, and evidence.
- `WholesomeProgressMonitor` now resets active time, clusters, failed endpoints, deaths, and episode flags when the attempt generation changes, even for the same quest-stage key. The regression drives three same-key attempts through real cooldown/HalfOpen acquisition and reaches third-episode quarantine without calling `Reset` between attempts.
- Pickup mismatch evidence now requires exact local ownership, exact manager `Attempting` generation ownership, the exact pickup behavior/POI, and a non-excluded in-world snapshot. The former 60-second wall-clock path and all old pickup fields were removed. Only three distinct active `ForcedQuestPickUp.InteractionCycleId` cycles can emit a failure; excluded or duplicate cycles do not count.
- Death attribution is captured before death from the exact objective behavior, exact manager/local generation, active-work snapshot, and quest-owned combat target. The death event consumes that snapshot once; proximity, unrelated combat, excluded/inactive state, owner/generation mismatch, and stale snapshots are rejected.
- `RefreshGate` now has one coalesced rerun bit while running. Any number of running-state requests produce one follow-up pending refresh; `Stop` clears pending/rerun state and stale `Complete` callbacks cannot revive it.
- `Start` and `Stop` share `ResetRecoveryLifecycleState`, which clears progress, pickup target/generation/interaction token/count/reported state, death capture, local ownership, and the progress sampling deadline. A restarted identical pickup cannot inherit an earlier cycle.

The RED evidence observed for this review was:

```text
OWNED_FAILURE_RED: CS1501, no five-argument generated Failure overload.
WHOLESOME_FAILURE_BIND_RED: missing TryBind/TryGet-generation/Release and ReportOwnedFailure.
GENERATION_RESET_RED: QuestWorkSample had no AttemptGeneration.
REFRESH_RERUN_RED: running-state request coalescing assertion exited 1.
PICKUP_CYCLE_RED: missing WholesomePickupMonitor/active predicate and InteractionCycleId.
DEATH_CAPTURE_RED: missing CombatOwnedByQuest and WholesomeDeathMonitor.
LIFECYCLE_RESET_RED: CS1061, WholesomeAutoQuest had no ResetRecoveryLifecycleState.
MANAGER_ELIGIBILITY_RED: CS1061, QuestRecoveryManager had no OwnsAttempt.
PICKUP_MANAGER_ELIGIBILITY_RED: production pickup predicate lacked manager ownership input.
```

Fresh sequential GREEN evidence after the corrections:

```text
[Wholesome]
Build succeeded. 3278 Warning(s), 0 Error(s).
Wholesome scheduler recovery regression tests passed.
BUILD_EXIT=0 TEST_EXIT=0 SCOPED_COMPILER_DIAGNOSTICS=0

[QuestRecoveryCore]
Build succeeded. 3246 Warning(s), 0 Error(s).
Quest recovery regression tests passed.
BUILD_EXIT=0 TEST_EXIT=0

[QuestPickupCore]
Build succeeded. 3246 Warning(s), 0 Error(s).
Quest pickup policy regression tests passed.
BUILD_EXIT=0 TEST_EXIT=0

[FullReleaseX86]
Build succeeded. 3250 Warning(s), 0 Error(s).
BUILD_EXIT=0
```

Review verification used the bundled `D:\World of Warcraft 3.3.5a\CB\.dotnet-sdk\dotnet.exe`, direct test DLL execution, `Platform=x86`, and `UseAppHost=false`. One attempted parallel test-project build collided in the shared WPF intermediate directory; both projects were immediately rerun sequentially, producing the green results above.

Review static/integrity results:

```text
PLAN_FORBIDDEN_SCAN:
WholesomeAutoQuest.cs:664: forceStop: () => TreeRoot.Stop(),
AUGMENTED_FORBIDDEN_SCAN_MATCHES=0
TREE_ROOT_START_COUNT=0
LEGACY_ADAPTER_COUNT=0
PROFILE_LOAD_COUNT=1
BASE_STOP_COUNT=1
REPORT_PROGRESS_COUNT=1
SUCCESS_FACTORY_COUNT=1
BACKUP_HASH_MISMATCHES=0
SCOPED_DIFF_CHECK_LINES=0
WHOLESOME_APPHOST_EXISTS=False
CORE_APPHOST_EXISTS=False
PICKUP_APPHOST_EXISTS=False
```

The sole plan-scan match remains the explicit user-clicked Settings `Force Stop` action allowed by the plan. The augmented scan included restart timers, automatic blacklist APIs/writers, quest abandonment, arbitrary `ActiveQuestIds.First`, level/grind file discovery, `TreeRoot.Start`, and all removed pickup wall-clock/state fields.

Additional repo-local files required by the review correction:

- `Bots/Quest/QuestOrder/ForcedQuestPickUp.cs`
- `Styx/Logic/Questing/Recovery/QuestRecoveryManager.cs`
- `Styx/Logic/Questing/Recovery/QuestRecoveryTypes.cs`
- `Tools/QuestRecoveryRegressionTests/Program.cs`
- `Tools/WholesomeQuestRecoveryRegressionTests/Program.cs`
- `.superpowers/sdd/2026-09-02-wholesome-quest-recovery-integration-plan/task-4-implementer-report.md`

External runtime review change:

- `D:\World of Warcraft 3.3.5a\CB\Bots\WholesomeAutoQuest-master\WholesomeAutoQuest.cs`
  - SHA-256: `D56582964843651204A216E5A793BD3E2EA4420C57A88EDCF53CBA6A7B149A2C`
- `QuestScheduler.cs` remains at Task 4 SHA-256 `B621479733879722609AA7B1785BA29E9AF85D97D898B3D2A82C5DFE1FA0B921`.

The rollback backup still verifies all seven manifest entries with zero mismatches. Unrelated vendor/grind working-tree changes remain untouched and excluded. No push, deployment, live replacement, backup mutation, or apphost execution occurred.

## Review Round 2 Corrections

All six round-two findings were implemented with production-linked tests before their corresponding production changes:

- `QuestRecoveryManager.AbandonAttempt` neutrally releases only the exact current `Attempting` generation to `Eligible`. It adds no success, failure, evidence, or escalation. Wholesome `Stop` invokes this manager release while local ownership still exists, then clears local lifecycle state and flushes. Same-identity restart can immediately acquire a newer generation; a stale generation cannot release it.
- `ForcedQuestPickUp` begins its interaction cycle and atomically clears the published result before calling the real giver/item interaction, which precedes the existing 1.5-second dialog wait. Outcomes carry `InteractionCycleId`, are published only for the still-current cycle, and `TryConsumeOutcome` removes them once. Wholesome no longer reads persistent `LastOutcome`; its monitor accepts only a result tagged for the exact current cycle, so a prior observation cannot burn a later result.
- A positively loaded gossip/native offered list that excludes the target now produces a tagged `PickupTargetNotOffered` observation/failure through the same three-cycle tracker. Unknown, unloaded, empty-transition, or ambiguous evidence remains `Wait`.
- Final path exhaustion is reported as an ordered generated-failure batch: subordinate endpoint `PathGenerationFailed` first while the stage owner remains `Attempting`, then stage `NoNavigableHotspot`, which releases the exact stage generation. Both endpoint and stage enter their scoped policy state; the endpoint is no longer demoted to an observation.
- `RefreshGate.Begin` returns a lease containing lifecycle epoch and run identity. `Complete` requires that exact lease. `Stop` and `Start` advance the epoch, clear pending/rerun state, and an old completion arriving after a restarted `Begin` cannot alter the new running state or its one-bit latch.
- Generated-failure authority is validated in both the public factory and manager. Exact-source generation, same-quest hierarchy, and target availability are required. Cross-quest and structurally unrelated targets are rejected, stale sources are inert, and a distinct target already `Attempting` under another generation is never overwritten. The permitted distinct relationship is objective-stage owner to same-quest navigation endpoint; manager-locked batches require subordinate failures first and the source-stage failure last.

Round-two RED evidence:

```text
NEUTRAL_ABANDON_RED: CS1061, QuestRecoveryManager had no AbandonAttempt.
STOP_ABANDON_RED: CS0117, WholesomeAutoQuest had no AbandonOwnedAttempt production seam.
PICKUP_TAGGED_RESULT_RED: missing TryConsumeOutcome, InteractionCycleId, and offeredQuestListLoaded contract.
PICKUP_LOADED_LIST_RED: linked test exited 1 because a positively loaded missing-target list still returned Wait/wrong reason.
PICKUP_EXACT_TAG_RED: linked test exited 1 because an earlier observation was paired with and consumed a new cycle.
GENERATED_FAILURE_AUTHORITY_RED: CS1061, manager had no ReportGeneratedFailures atomic contract.
FINAL_ENDPOINT_STAGE_RED: CS0117, WholesomeAutoQuest had no ReportOwnedFailures ordered production path.
REFRESH_EPOCH_RED: compile failed because Begin still returned bool and Complete accepted no lease.
```

Fresh sequential GREEN evidence:

```text
[QuestRecoveryCore]
Build succeeded. 3246 Warning(s), 0 Error(s).
Quest recovery regression tests passed.
BUILD_EXIT=0 TEST_EXIT=0

[QuestPickupCore]
Build succeeded. 3246 Warning(s), 0 Error(s).
Quest pickup policy regression tests passed.
BUILD_EXIT=0 TEST_EXIT=0

[Wholesome]
BUILD_EXIT=0 TEST_EXIT=0 SCOPED_COMPILER_DIAGNOSTICS=0
Wholesome scheduler recovery regression tests passed.

[FullReleaseX86]
Build succeeded. 3250 Warning(s), 0 Error(s).
BUILD_EXIT=0
```

All builds used `D:\World of Warcraft 3.3.5a\CB\.dotnet-sdk\dotnet.exe`, `Platform=x86`, `UseAppHost=false`, `--no-restore`, `--no-incremental`, and direct test DLL execution.

Round-two static/integrity evidence:

```text
PLAN_FORBIDDEN_SCAN:
WholesomeAutoQuest.cs:682: forceStop: () => TreeRoot.Stop(),
AUGMENTED_FORBIDDEN_SCAN_MATCHES=0
TREE_ROOT_START_COUNT=0
LEGACY_ADAPTER_COUNT=0
PROFILE_LOAD_COUNT=1
BASE_STOP_COUNT=1
REPORT_PROGRESS_COUNT=1
SUCCESS_FACTORY_COUNT=1
PERSISTENT_PICKUP_OUTER_READ_COUNT=0
TRY_CONSUME_PICKUP_COUNT=1
NO_ARG_REFRESH_COMPLETE_COUNT=0
BACKUP_HASH_MISMATCHES=0
SCOPED_DIFF_CHECK_LINES=0
WHOLESOME_APPHOST_EXISTS=False
CORE_APPHOST_EXISTS=False
PICKUP_APPHOST_EXISTS=False
```

The only plan-scan match is still the explicit user-clicked Settings `Force Stop` action. The augmented scan again covered restart timers, automatic blacklist writers/APIs, abandonment, arbitrary active-quest selection, level/grind file discovery, `TreeRoot.Start`, and removed pickup wall-clock/state fields.

Repo-local round-two files:

- `Bots/Quest/QuestOrder/ForcedQuestPickUp.cs`
- `Bots/Quest/QuestOrder/QuestPickupDialogPolicy.cs`
- `Styx/Logic/Questing/Recovery/QuestRecoveryManager.cs`
- `Styx/Logic/Questing/Recovery/QuestRecoveryTypes.cs`
- `Tools/QuestPickupPolicyRegressionTests/Program.cs`
- `Tools/QuestRecoveryRegressionTests/Program.cs`
- `Tools/WholesomeQuestRecoveryRegressionTests/Program.cs`
- `.superpowers/sdd/2026-09-02-wholesome-quest-recovery-integration-plan/task-4-implementer-report.md`

External runtime round-two state:

- `D:\World of Warcraft 3.3.5a\CB\Bots\WholesomeAutoQuest-master\WholesomeAutoQuest.cs`
  - SHA-256: `8342453B47508491D8DD7EB302152AF035E1F1150C3385A20757D3C7A867176C`
- `QuestScheduler.cs` remains unchanged at SHA-256 `B621479733879722609AA7B1785BA29E9AF85D97D898B3D2A82C5DFE1FA0B921`.

The rollback backup still has zero manifest mismatches. Existing unrelated vendor/grind dirt remains untouched and excluded. No push, deployment, live replacement, backup mutation, or apphost execution occurred.

## Review Round 3 Corrections

All four round-three findings were implemented under production-linked RED tests before their minimal production changes:

- A refresh lease now fences the full scheduler operation, not only completion. The callback captures its scheduler with the lease, validates before scanning and again before profile/status application, and may request `runAgain` only with that same lease. `Stop`/`Start` invalidates old work before scan, after scan, and before a follow-up can enter the new epoch.
- `SetManualBlacklist` now neutrally normalizes every same-quest `Attempting` record under the manager lock before installing the quest-wide terminal. It retains each generation fence, evidence, and escalation history and persists those records. Removal retains non-synthetic history as eligible, removes only a synthetic manual record, and permits a newer exact owner. Wholesome also clears a matching local activation when a manual exclusion is installed.
- Generated failure contention now uses `TryReportGeneratedFailures(..., out decisions)`. Stale source generations, user Stop/manual replacement, and separately owned targets return `false` without throwing or mutating the batch. Wholesome retains local ownership until the result is known; on rejection it neutrally abandons the exact source when still owned, clears the matching local activation, and requests one rebuild without marking a failure episode applied.
- Lifecycle subscription state uses a synchronized epoch and two-phase transition. Callback actions run outside the state lock; an in-flight stale Start compensates its subscription after Stop, and new transitions wait for that compensation. Concurrent/repeated Start and Stop finish with exactly one handler while started and none while stopped.

Round-three RED evidence:

```text
REFRESH_FULL_FENCE_RED: missing RunLeaseFencedRefresh, lease-aware TryRequest, and IsCurrent; build exit 1.
MANUAL_NORMALIZE_RED: regression failed because same-quest objective ownership remained Attempting under a pickup manual terminal.
MANUAL_REMOVE_IDEMPOTENCE_RED: regression failed because an idempotent removal also normalized a newly acquired owner; normalization was narrowed to terminal installation.
GENERATED_TRY_RED: CS1061, QuestRecoveryManager had no TryReportGeneratedFailures (build exit 1).
GENERATED_PRODUCTION_RED: ReportOwnedFailures still accepted a throwing Func and RecoverRejectedOwnedFailures was missing (build exit 1).
LIFECYCLE_ATOMIC_RED: deterministic blocked-Start/concurrent-Stop regression ended stopped with a handler installed (test exit 1).
MANUAL_LOCAL_OWNER_RED: ReleaseManuallyExcludedOwnership production seam was missing (build exit 1).
```

Fresh sequential GREEN evidence:

```text
[QuestRecoveryCore]
Build succeeded. 3246 Warning(s), 0 Error(s).
Quest recovery regression tests passed.
BUILD_EXIT=0 TEST_EXIT=0

[QuestPickupCore]
Build succeeded. 3246 Warning(s), 0 Error(s).
Quest pickup policy regression tests passed.
BUILD_EXIT=0 TEST_EXIT=0

[Wholesome]
Build succeeded. 3278 Warning(s), 0 Error(s).
Wholesome scheduler recovery regression tests passed.
BUILD_EXIT=0 TEST_EXIT=0 SCOPED_COMPILER_DIAGNOSTICS=0

[FullReleaseX86]
Build succeeded. 3250 Warning(s), 0 Error(s).
BUILD_EXIT=0
```

All builds used bundled `D:\World of Warcraft 3.3.5a\CB\.dotnet-sdk\dotnet.exe`, direct test DLL execution, `Platform=x86`, `UseAppHost=false`, `--no-restore`, and `--no-incremental`.

Round-three static/integrity evidence:

```text
PLAN_FORBIDDEN_SCAN:
WholesomeAutoQuest.cs:793: forceStop: () => TreeRoot.Stop(),
AUGMENTED_FORBIDDEN_SCAN_MATCHES=0
TREE_ROOT_START_COUNT=0
LEGACY_ADAPTER_COUNT=0
PROFILE_LOAD_COUNT=1
BASE_STOP_COUNT=1
REPORT_PROGRESS_COUNT=1
SUCCESS_FACTORY_COUNT=1
NO_ARG_REFRESH_COMPLETE_COUNT=0
TRY_GENERATED_BATCH_COUNT=1
REFRESH_LEASE_CHECK_COUNT=4
BACKUP_HASH_MISMATCHES=0
SCOPED_DIFF_CHECK_EXIT=0
WHOLESOME_APPHOST_EXISTS=False
CORE_APPHOST_EXISTS=False
PICKUP_APPHOST_EXISTS=False
```

The only plan-scan match remains the explicit user-clicked Settings `Force Stop` action. The augmented scan covered restart timers, automatic blacklist writers, quest abandonment, arbitrary active-quest selection, grind/level-file discovery, `TreeRoot.Start`, removed pickup wall-clock fields, and the superseded throwing generated-batch production call.

Repo-local round-three files:

- `Styx/Logic/Questing/Recovery/QuestRecoveryManager.cs`
- `Tools/QuestRecoveryRegressionTests/Program.cs`
- `Tools/WholesomeQuestRecoveryRegressionTests/Program.cs`
- `.superpowers/sdd/2026-09-02-wholesome-quest-recovery-integration-plan/task-4-implementer-report.md`

External runtime round-three state:

- `D:\World of Warcraft 3.3.5a\CB\Bots\WholesomeAutoQuest-master\WholesomeAutoQuest.cs`
  - SHA-256: `DE94FA2BECA00540F9A9DED3A361A751FF7F4573F74E5091C64206A3011C533C`
- `QuestScheduler.cs` remains unchanged at SHA-256 `B621479733879722609AA7B1785BA29E9AF85D97D898B3D2A82C5DFE1FA0B921`.

The rollback backup again verifies all seven manifest entries with zero mismatches. Existing unrelated vendor/grind working-tree changes remain untouched and excluded. No push, deployment, live replacement, backup mutation, or apphost execution occurred.

## Review Round 4 Corrections

All three round-four findings were reproduced through production-linked regressions before the corresponding production changes:

- `ProcessProgressUpdate` now returns immediately when the atomic generated-failure batch rejects expected authority contention. The existing recovery path still neutrally abandons only the exact source generation, clears matching local ownership/activation, and queues one rebuild; rejected stage and endpoint failures can no longer fall through and be persisted as observations.
- Refresh application is now an atomic lease-aware operation. `TryApply` validates the current running lease and executes the scheduler apply callback under the refresh synchronization boundary. The lifecycle gate shares that reentrant synchronization root, so `Stop`/`Start` cannot advance the lifecycle between authorization and the side effect; concurrent lifecycle transitions wait for an already-authorized apply, old leases remain fenced afterward, and callback reentrancy does not deadlock.
- Completed is authoritative over manual blacklist state. `MarkCompleted` normalizes every same-quest record to `Completed`, clears failure reason/cooldowns, and always installs a canonical TurnIn sentinel carrying generation/cycle high-water marks. Manual enable/disable is redundant for completed quests, while disabling a real manual terminal removes only `ManualBlacklist` records. Completion compaction retains the bounded merged evidence set plus generation/cycle high-water marks on the canonical TurnIn sentinel.

Round-four RED evidence:

```text
REFRESH_ATOMIC_APPLY_RED: CS1061, RefreshGate had no TryApply (build exit 1).
REJECTED_PROCESS_FALLTHROUGH_RED: the actual ProcessProgressUpdate regression exited 1 because rejected generated failures were subsequently reported as observations.
LIFECYCLE_REENTRANCY_RED: the apply callback/concurrent lifecycle Stop regression exited 1 because the initial separate-lock implementation deadlocked.
COMPLETED_MANUAL_RED: the core regression exited 1 because MarkCompleted did not retain all same-quest completion evidence/high-water records or always add a canonical TurnIn sentinel.
```

Fresh sequential GREEN evidence:

```text
[QuestRecoveryCore]
Build succeeded. 3246 Warning(s), 0 Error(s).
Quest recovery regression tests passed.
BUILD_EXIT=0 TEST_EXIT=0

[QuestPickupCore]
Build succeeded. 3246 Warning(s), 0 Error(s).
Quest pickup policy regression tests passed.
BUILD_EXIT=0 TEST_EXIT=0

[Wholesome]
Build succeeded. 3278 Warning(s), 0 Error(s).
Wholesome scheduler recovery regression tests passed.
BUILD_EXIT=0 TEST_EXIT=0

[FullReleaseX86]
Build succeeded. 3250 Warning(s), 0 Error(s).
BUILD_EXIT=0
```

All builds used bundled `D:\World of Warcraft 3.3.5a\CB\.dotnet-sdk\dotnet.exe`, direct test DLL execution, `Platform=x86`, `UseAppHost=false`, `--no-restore`, and `--no-incremental`. The package-vulnerability audit warning remained the known network-unavailable warning; there were no compiler errors.

Round-four static/integrity evidence:

```text
PLAN_FORBIDDEN_SCAN:
WholesomeAutoQuest.cs:809: forceStop: () => TreeRoot.Stop(),
AUGMENTED_FORBIDDEN_SCAN_MATCHES=0
TREE_ROOT_START_COUNT=0
LEGACY_ADAPTER_COUNT=0
PROFILE_LOAD_COUNT=1
BASE_STOP_COUNT=1
REPORT_PROGRESS_COUNT=1
SUCCESS_FACTORY_COUNT=1
NO_ARG_REFRESH_COMPLETE_COUNT=0
TRY_GENERATED_BATCH_COUNT=1
TRY_APPLY_COUNT=2
REJECTED_RECOVERY_COUNT=2
BACKUP_HASH_MISMATCHES=0
SCOPED_DIFF_CHECK_EXIT=0
WHOLESOME_APPHOST_EXISTS=False
CORE_APPHOST_EXISTS=False
PICKUP_APPHOST_EXISTS=False
```

The only prescribed plan-scan match remains the explicitly user-clicked Settings `Force Stop`. The augmented scan covered restart timers, automatic blacklist writers, arbitrary active-quest selection, grind/level-file discovery, `TreeRoot.Start`, removed pickup wall-clock fields, and the superseded throwing generated-batch call.

Repo-local round-four files:

- `Styx/Logic/Questing/Recovery/QuestRecoveryManager.cs`
- `Tools/QuestRecoveryRegressionTests/Program.cs`
- `Tools/WholesomeQuestRecoveryRegressionTests/Program.cs`
- `.superpowers/sdd/2026-09-02-wholesome-quest-recovery-integration-plan/task-4-implementer-report.md`

External runtime round-four state:

- `D:\World of Warcraft 3.3.5a\CB\Bots\WholesomeAutoQuest-master\WholesomeAutoQuest.cs`
  - SHA-256: `AFF843D5A2D51916E352C1F9FEC446DB6B40E657D3A5CE4B329DFAAE10279D05`
- `QuestScheduler.cs` remains unchanged at SHA-256 `B621479733879722609AA7B1785BA29E9AF85D97D898B3D2A82C5DFE1FA0B921`.

The rollback backup still verifies all seven manifest entries with zero mismatches. Existing unrelated vendor/grind working-tree changes remain untouched and excluded. No push, deployment, live replacement, backup mutation, or apphost execution occurred.
