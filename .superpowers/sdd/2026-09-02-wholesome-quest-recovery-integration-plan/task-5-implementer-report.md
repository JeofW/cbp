# Wholesome Task 5 Implementer Report

## Outcome

Implemented Task 5 diagnostics, manual controls, and legacy compatibility cleanup. The external Wholesome settings model no longer stores `BlacklistedQuests` or `BlacklistText`, and `SettingsForm` no longer accepts a `saveQuestBlacklist` callback. Its manual quest-ID field is populated from the core manager's `ManualBlacklist` records and applies an added/removed set difference exclusively through `SetManualBlacklist`.

The settings form now displays a read-only recovery grid with quest ID, stage, state, reason, episode, and either an exact UTC cooldown/half-open time or an explicit reset trigger. `Retry now`, `Mark permanent`, and `Clear exclusion` are disabled without a selected row, preserve `Completed` as terminal, log every explicit action with quest/stage/reason, and request one normal scheduler refresh after a change. Snapshot refreshes are periodically requested and marshalled onto the form's owning UI thread.

The manager gained the narrowly required `ClearExclusion(QuestRecoveryKey)` operation because the reviewed contracts could not express clearing automatic state: `RetryNow` creates a single half-open probe and `SetManualBlacklist(false)` removes only manual records. Clear removes the selected cooling/half-open/quarantined record, removes a quest-wide manual terminal when selected, and never changes a completed quest.

First successful legacy migration now logs exactly:

```text
Legacy migration: backup='<full quest_blacklist.legacy.bak path>', imported=<count>.
```

The message is emitted only after persistence and the one-time marker succeed. Imported records remain `Quarantined` with reason `LegacyUnknown`; no UI or log claims they were user-authored. The original blacklist is read-only after import, and pre-existing legacy backups are not overwritten or deleted.

## TDD Evidence

The production-linked Wholesome regression project was changed before production code. Initial RED failed with nine compiler errors for the wished-for production contracts: missing `RecoveryStatusFormatter`, `RecoverySettingsController`, `RecoveryActionAvailability`, the manager-aware settings-form constructor, and UI-thread refresh method. After the minimal status/controller/form/manager implementation, the suite was green.

The migration visibility regression was then added and run against the unmodified migration implementation. It exited 1 with:

```text
System.InvalidOperationException: the first successful migration must log the exact backup path and imported count once
```

Adding the one-time success diagnostic made it green. The linked tests cover every recovery state and finite reason, exact cooldown and half-open formatting, reset text, one controlled `RetryNow` probe, permanent/manual conversion, automatic/manual clear behavior, completed terminal behavior, manual textbox diffs that leave automatic state untouched, actual WinForms disabled-selection state, background-to-UI-thread snapshot refresh, legacy visibility/log-once behavior, and byte-for-byte preservation of the original and historical backup files through explicit actions and flush.

## Fresh Verification

All builds used `D:\World of Warcraft 3.3.5a\CB\.dotnet-sdk\dotnet.exe`, `Platform=x86`, `UseAppHost=false`, `--no-restore`, and `--no-incremental`. Tests were executed through the bundled dotnet host from the `bin/x86/Release/net10.0-windows7.0` DLL; no apphost was generated or launched.

```text
[Wholesome external bot/UI + linked regressions]
Build succeeded. 3261 Warning(s), 0 Error(s).
Wholesome scheduler recovery regression tests passed.
WHOLESOME_TEST_EXIT=0
SCOPED_COMPILER_DIAGNOSTICS=0

[QuestRecoveryCore]
Build succeeded. 3246 Warning(s), 0 Error(s).
Quest recovery regression tests passed.
CORE_TEST_EXIT=0

[QuestPickupCore]
Build succeeded. 3246 Warning(s), 0 Error(s).
Quest pickup policy regression tests passed.
PICKUP_TEST_EXIT=0

[FullReleaseX86]
Build succeeded. 3250 Warning(s), 0 Error(s).
FULL_BUILD_EXIT=0
```

The warning totals are the accepted repository package/compiler baseline. The focused build emitted no warning or error naming `SettingsForm.cs`, `WholesomeAQSettings.cs`, `WholesomeAutoQuest.cs`, `QuestRecoveryManager.cs`, or the changed Wholesome regression program.

Static and integrity verification:

```text
QUEST_BLACKLIST_STORAGE_SCAN_EXIT=1 (zero matches)
TREE_ROOT_START_SCAN_EXIT=1 (zero matches)
LEGACY_DIRECT_WRITER_SCAN_EXIT=1 (zero matches)
WHOLESOME_APPHOST_EXISTS=False
CORE_APPHOST_EXISTS=False
PICKUP_APPHOST_EXISTS=False
BACKUP_HASH_MISMATCHES=0
INSTALLED_HASH_MISMATCHES=0
TRAILING_WHITESPACE=0
```

The legacy-direct-writer scan covered `File.WriteAllText`, `WriteAllBytes`, `Delete`, `Move`, and `Replace` calls targeting `legacyPath` or `quest_blacklist.txt` across the core recovery directory and the three Task 5 external files. The only legacy references are the intended read/import, one-time non-overwriting backup, marker, and evidence label.

## Files Changed

Repo-local files included in the Task 5 commit:

- `Styx/Logic/Questing/Recovery/QuestRecoveryManager.cs`
- `Tools/WholesomeQuestRecoveryRegressionTests/Program.cs`
- `.superpowers/sdd/2026-09-02-wholesome-quest-recovery-integration-plan/task-5-implementer-report.md`

External/local runtime files intentionally outside Git:

- `SettingsForm.cs` — `a6275aa618200966cbf0c9903ce300fea8888259663296c22674786e1aa22349`
- `WholesomeAQSettings.cs` — `2daca32c77e1abee73232534d4b1761f6fad409cd2a41a494596690b07d270ed`
- `WholesomeAutoQuest.cs` — `7e639e921dbb384de0462561bb42aacea4214927c6f5ba950e2bc4831fa4914c`

The other four post-change runtime hashes are:

- `DataLoader.cs` — `a85095519cd88211159366c1dade866e79b5522dfdcee6d5c96b0615a1c1f95c`
- `DataModels.cs` — `e118435d9ad5437e985710c80765e4e6c14f5a27824a36c6a38ef46c9b1992cb`
- `ProfileBuilder.cs` — `12660b3fa91ebc4b963f4bc37c19935f17d8d8301a78ecd77878ffaccead0e13`
- `QuestScheduler.cs` — `b621479733879722609aa7b1785ba29e9af85d97d898b3d2a82c5dfe1fa0b921`

These seven hashes were recorded in `D:\World of Warcraft 3.3.5a\CB\Backups\quest-recovery-wholesome-20260903-080658\installed.sha256.txt`. That file hashes to `0E9841957954B4769A258206ED2D003A7EB37338316634DD899A86B0D4F256DE`. The original `manifest.sha256.txt` still hashes to `C5787DD61F477ECB40D3366F1834B3CD61123806436BF96109B611855D106D56` and all seven original backup entries revalidated with zero mismatches.

## Preservation and Concerns

- Existing unrelated vendor/grind working-tree changes were untouched and will be excluded from the Task 5 commit.
- Existing historical backup files were preserved. `installed.sha256.txt` is new, local-only, and does not replace the original manifest.
- Vendor blacklist storage remains vendor-scoped and unchanged; the quest blacklist storage paths are removed.
- No push, deploy, live replacement, bot launch, or apphost execution was performed.

## Review Round 1 Corrections

Both review findings were reproduced with production-linked regressions before their minimal production changes.

- Recovery action availability is now quest-wide. If any same-quest record is `ManualBlacklist`, an automatic row retained after `Mark permanent` disables `Retry now` and duplicate `Mark permanent` while leaving `Clear exclusion` available. The form preserves the exact selected automatic row through refresh, so the action remains scoped and visible instead of silently targeting a stale/noncanonical record.
- `QuestRecoveryManager.ClearExclusion` now removes the same-quest manual terminal and the selected automatic cooling/half-open/quarantined record within one manager lock. The prior `if (!removed)` condition no longer suppresses the selected automatic removal after the manual record is removed, and a selected manual key is not double-removed. Other same-quest stages, unrelated quests, and completed terminal records remain unchanged.
- Configuration before first `Start` now uses the same idempotent `EnsureRecoveryConfigured` path as `Start`. It safely loads/reuses `DataLoader`, captures deterministic dataset/navigation fingerprints, constructs the current character/realm environment, and configures the manager before constructing `SettingsForm`. Repeated same-identity configuration retains the manager's existing richer-fingerprint merge rules.
- The initializer does not start the bot lifecycle, add subscriptions, or queue a refresh. Missing live identity and initialization exceptions return an unavailable result and a meaningful log instead of throwing. The settings form displays that status, makes manual recovery input read-only, disables recovery actions, omits manager mutation on Save, and does not start its refresh timer while unavailable. UI action/save exceptions are contained and surfaced through the visible recovery status label and log.

Review RED evidence:

```text
COMBINED_ACTION_RED: linked test exited 1 because a same-quest manual terminal left Retry now and duplicate Mark permanent enabled on the selected automatic row.
PRESTART_CONFIGURATION_RED: build failed with CS1061 for missing WholesomeAutoQuest.EnsureRecoveryConfigured and CS1739 for missing recoveryAvailable SettingsForm parameters.
```

Fresh review GREEN evidence:

```text
[Wholesome external bot/UI + linked regressions]
Build succeeded. 3261 Warning(s), 0 Error(s).
Wholesome scheduler recovery regression tests passed.
WHOLESOME_TEST_EXIT=0
SCOPED_COMPILER_DIAGNOSTICS=0

[QuestRecoveryCore]
Build succeeded. 3246 Warning(s), 0 Error(s).
Quest recovery regression tests passed.
CORE_TEST_EXIT=0

[QuestPickupCore]
Build succeeded. 3246 Warning(s), 0 Error(s).
Quest pickup policy regression tests passed.
PICKUP_TEST_EXIT=0

[FullReleaseX86]
Build succeeded. 3250 Warning(s), 0 Error(s).
FULL_BUILD_EXIT=0
```

The new UI/manager sequence test selects an actual automatic recovery-grid row, marks it permanent, verifies selection and button state, proves disabled retry cannot produce a half-open probe, then clears the manual terminal plus selected automatic record while retaining another same-quest stage, an unrelated quest, and `Completed`. The pre-Start test creates a real persisted store, invokes the production initializer on a new unconfigured manager, displays the persisted row, saves a manual-ID diff through the actual form, and verifies the lifecycle remains stopped with no queued refresh. A separate no-character/error test verifies read-only UI and contained/logged failures.

Review static/integrity results remain:

```text
QUEST_BLACKLIST_STORAGE/TREE_ROOT_START_SCAN_EXIT=1 (zero matches)
LEGACY_DIRECT_WRITER_SCAN_EXIT=1 (zero matches)
WHOLESOME_APPHOST_EXISTS=False
CORE_APPHOST_EXISTS=False
PICKUP_APPHOST_EXISTS=False
BACKUP_HASH_MISMATCHES=0
INSTALLED_HASH_MISMATCHES=0
TRAILING_WHITESPACE=0
```

Final review-round external hashes changed only for:

- `SettingsForm.cs` — `a6275aa618200966cbf0c9903ce300fea8888259663296c22674786e1aa22349`
- `WholesomeAutoQuest.cs` — `7e639e921dbb384de0462561bb42aacea4214927c6f5ba950e2bc4831fa4914c`

The other five installed hashes are unchanged. `installed.sha256.txt` was updated to the final seven-file state; `manifest.sha256.txt` remains unchanged. No push, deploy, live replacement, bot launch, or apphost execution occurred.

## Review Round 2 Persistence Correction

The pre-`Start` settings UI now persists each successful explicit recovery mutation immediately. A manual textbox Save applies the complete added/removed set difference and performs one flush after the batch, rather than one write per quest ID. `Retry now`, `Mark permanent`, and `Clear exclusion` each flush after their successful manager mutation. Completed and unavailable/read-only paths remain non-mutating and do not flush.

`QuestRecoveryManager.TryFlush()` exposes the existing `FlushCore()` success result while preserving the original `Flush()` API. A failed persistence write therefore keeps `_dirty` set under the existing manager contract. The settings controller converts that false result into a contained UI failure: action/Save errors are logged, the visible recovery status states that changes were not persisted, and Save leaves the form open. Retrying Save performs one flush even if the textbox has no further diff, allowing the retained dirty state to persist after the storage target recovers.

Round 2 strict-TDD evidence:

```text
PRESTART_PERSISTENCE_RED: exited 1 at "a pre-Start textbox Save containing both removal and addition must survive a fresh manager reload".
FLUSH_FAILURE_RED: exited 1 at "a failed recovery Save flush must remain visibly open and must not claim persistence".
GREEN: Wholesome scheduler recovery regression tests passed.
```

The production-linked persistence test configures through the actual pre-`Start` `EnsureRecoveryConfigured` seam, drives the real WinForms textbox and action buttons, discards the active manager after each Save/Retry/Mark/Clear, and reloads the same temporary store into a fresh manager. It verifies the combined textbox add/remove result, `RetryNow` half-open state, manual terminal creation, combined manual-plus-selected-automatic clear, standalone automatic clear, and preservation of `Completed`. The failure regression blocks the JSON temporary path, verifies the form remains visibly retryable without a false persistence claim, restores the path, and proves a second no-diff Save persists the retained dirty state. The missing-character regression verifies the unavailable form does not enter the recovery Save/flush path.

Fresh Round 2 verification:

```text
[Wholesome external bot/UI + linked regressions]
Build succeeded. 3261 Warning(s), 0 Error(s).
Wholesome scheduler recovery regression tests passed.
SCOPED_COMPILER_DIAGNOSTICS=0

[QuestRecoveryCore]
Build succeeded. 3246 Warning(s), 0 Error(s).
Quest recovery regression tests passed.

[QuestPickupCore]
Build succeeded. 3246 Warning(s), 0 Error(s).
Quest pickup policy regression tests passed.

[FullReleaseX86]
Build succeeded. 3250 Warning(s), 0 Error(s).

QUEST_BLACKLIST_STORAGE_SCAN_EXIT=1 (zero matches)
TREE_ROOT_START_SCAN_EXIT=1 (zero matches)
LEGACY_DIRECT_WRITER_SCAN_EXIT=1 (zero matches)
WHOLESOME_APPHOST_EXISTS=False
CORE_APPHOST_EXISTS=False
PICKUP_APPHOST_EXISTS=False
BACKUP_HASH_MISMATCHES=0
INSTALLED_HASH_MISMATCHES=0
TRAILING_WHITESPACE=0
```

Round 2 changes only one external runtime file:

- `SettingsForm.cs` — `b0a8c42f5b5f56d604d4617c8137e2fab0ef7d0093edc7bf8dfe8b5e51da5d91`

All other external hashes remain as listed above. The final seven-file `installed.sha256.txt` hashes to `B80854622FCA3B58E6024C2C5338AD0703242F446104A7F2310F321D9D217414`; the preserved original `manifest.sha256.txt` still hashes to `C5787DD61F477ECB40D3366F1834B3CD61123806436BF96109B611855D106D56`. No external deployment, live replacement, push, apphost generation, or bot launch was performed.

## Review Round 3 Stale-Action Correction

The public `RetryNow`, `ClearExclusion`, and `SetManualBlacklist` methods remain source-compatible wrappers. New `TryRetryNow`, `TryClearExclusion`, and `TrySetManualBlacklist` variants return whether the manager actually changed state under its lock. Retry accepts only a current cooling/quarantined exclusion; missing rows, newer terminal/eligible/half-open state, and completed quests return false. Clear returns false when neither the selected exclusion nor a same-quest manual terminal can be removed. Manual toggles now report and dirty only real changes.

`RecoverySettingsController` uses these mutation results before persistence. A stale or availability-raced Retry/Mark/Clear logs an explicit unavailable/no-change status, refreshes the grid, does not invoke the recovery-changed callback, and never enters `TryFlush`; unrelated dirty manager state therefore remains unpersisted. Successful actions retain Round 2 failure handling and execute exactly one flush transaction. A narrow injectable flush function defaults to `manager.TryFlush` and lets production-linked tests count the actual controller persistence boundary without replacing manager behavior.

Round 3 strict-TDD evidence:

```text
STALE_ACTION_RED: exited 1 at "a stale action must report no change and refresh current status without signaling a mutation".
COUNTING_FLUSH_RED: build failed with CS1729 because RecoverySettingsController lacked the counting persistence seam.
GREEN: Wholesome scheduler recovery regression tests passed.
```

The real WinForms race regression selects cooling Retry/Clear rows, changes manager state after selection, adds unrelated dirty state, clicks the still-visible button, and proves zero callback, accurate no-change logging, status-grid refresh, no JSON write, no unrelated persistence, and preserved `Completed`. It also covers a same-quest manual-terminal availability race. The counting regression verifies zero flush attempts for stale Retry/Clear, raced Mark, and all Completed actions, and exactly one attempt for successful Retry/Clear/Mark. The existing unavailable-character form regression continues to prove its read-only Save path never attempts recovery persistence.

Fresh Round 3 verification:

```text
[Wholesome external bot/UI + linked regressions]
Build succeeded. 3261 Warning(s), 0 Error(s).
Wholesome scheduler recovery regression tests passed.
SCOPED_COMPILER_DIAGNOSTICS=0

[QuestRecoveryCore]
Build succeeded. 3246 Warning(s), 0 Error(s).
Quest recovery regression tests passed.

[QuestPickupCore]
Build succeeded. 3246 Warning(s), 0 Error(s).
Quest pickup policy regression tests passed.

[FullReleaseX86]
Build succeeded. 3250 Warning(s), 0 Error(s).

QUEST_BLACKLIST_STORAGE_SCAN_EXIT=1 (zero matches)
TREE_ROOT_START_SCAN_EXIT=1 (zero matches)
TASK5_LEGACY_DIRECT_WRITER_SCAN_EXIT=1 (zero matches)
WHOLESOME_APPHOST_EXISTS=False
CORE_APPHOST_EXISTS=False
PICKUP_APPHOST_EXISTS=False
BACKUP_HASH_MISMATCHES=0
INSTALLED_HASH_MISMATCHES=0
TRAILING_WHITESPACE=0
```

Round 3 changes only one external runtime file:

- `SettingsForm.cs` — `1457666c7061676f11c33352ed401d9c8d6d1092c1b8ad8c726ff2321fdd651b`

The final seven-file `installed.sha256.txt` hashes to `4256988A2E6A7A7ED1DAED6A4835218C9F1557BDF7D6E00B90C7D162643AD630`; the original manifest remains `C5787DD61F477ECB40D3366F1834B3CD61123806436BF96109B611855D106D56` with zero backup mismatches.

Per the review ruling, the separate legacy blacklist readers/writers in `Quest Behaviors/SafePickUp.cs`, `Quest Behaviors/SafeTurnIn.cs`, and `Plugins/ZygorProfileRecovery/ZygorProfileRecovery.cs` were inventoried but deliberately not edited here. They are deferred to Tasks 2–4 of the next approved adapters rollout plan and are outside Wholesome Task 5 scope. No push, deployment, live replacement, apphost, or bot launch occurred.

## Review Round 4 Atomic Mark Correction

`Mark permanent` now uses `QuestRecoveryManager.TrySetManualBlacklistIfCurrentAutomatic` instead of the explicit textbox API. Under the manager lock, the conditional operation requires the exact selected recovery key, an excluded automatic state (`CoolingDown`, `HalfOpen`, or `Quarantined`), the selected row's monotonic attempt generation, and no same-quest `Completed` or `ManualBlacklist` terminal. Only after all conditions still match does it install the existing quest-wide manual terminal. The original `TrySetManualBlacklist(uint, bool)` and void wrapper remain unchanged for explicit manual textbox semantics.

Action availability now requires a current manager snapshot record with the same key, state, and attempt generation as the selected row. Missing, `Eligible`, `Completed`, manual, changed-state, and same-state/later-generation rows cannot fall back to stale row data. A false conditional result logs and refreshes without callback or flush; a true result retains exactly one flush.

Round 4 strict-TDD evidence:

```text
CONDITIONAL_MARK_RED: build failed with CS1061 because QuestRecoveryManager lacked TrySetManualBlacklistIfCurrentAutomatic.
ABA_MARK_RED: build failed with CS1061 because RecoveryStatusRow lacked the stable AttemptGeneration token.
GREEN: Wholesome scheduler recovery regression tests passed.
```

Production-linked regressions select real cooling and quarantined rows and race them to removal, `Eligible`, `Completed`, and a different automatic state before clicking `Mark permanent`. Each stale path produces zero callback, zero manual mutation, and zero flush, then refreshes to the current manager snapshot. Counted controller coverage proves an unchanged quarantined row installs one quest-wide `ManualBlacklist` and attempts exactly one flush. A same-key, same-state ABA regression proves a later attempt generation is rejected. Existing combined clear, retry, persistence-failure, and `Completed` terminal tests remain green.

Fresh Round 4 verification:

```text
[Wholesome external bot/UI + linked regressions]
Build succeeded. 3261 Warning(s), 0 Error(s).
Wholesome scheduler recovery regression tests passed.

[QuestRecoveryCore]
Build succeeded. 3246 Warning(s), 0 Error(s).
Quest recovery regression tests passed.

[QuestPickupCore]
Build succeeded. 3246 Warning(s), 0 Error(s).
Quest pickup policy regression tests passed.

[FullReleaseX86]
Build succeeded. 3250 Warning(s), 0 Error(s).

QUEST_BLACKLIST_STORAGE_SCAN_EXIT=1 (zero matches)
TREE_ROOT_START_SCAN_EXIT=1 (zero matches)
TASK5_LEGACY_DIRECT_WRITER_SCAN_EXIT=1 (zero matches)
WHOLESOME_APPHOST_EXISTS=False
CORE_APPHOST_EXISTS=False
PICKUP_APPHOST_EXISTS=False
BACKUP_HASH_MISMATCHES=0
INSTALLED_HASH_MISMATCHES=0
TRAILING_WHITESPACE_SCAN_EXIT=1 (zero matches)
DIFF_CHECK_EXIT=0
```

Round 4 changes only one external runtime file:

- `SettingsForm.cs` — `72c9a14b09df4bef60dd74d7360edfa8899062d1d0b5e4bd77f76c18912afc8f`

The final seven-file `installed.sha256.txt` hashes to `FB31439C6D3C4F998DC8B19CA8869DFF273F2F4D91C24AADD6AE1C5C3E2F43C5`; the preserved original `manifest.sha256.txt` remains `C5787DD61F477ECB40D3366F1834B3CD61123806436BF96109B611855D106D56` with zero mismatches. Existing unrelated vendor/grind worktree changes were not edited or staged. Adapters remain deferred. No push, deployment, live replacement, apphost generation, or bot launch occurred.
