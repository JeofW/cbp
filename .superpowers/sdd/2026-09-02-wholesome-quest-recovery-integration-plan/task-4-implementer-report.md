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
