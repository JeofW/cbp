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

- `SettingsForm.cs` — `14daf692a588784fe20af657e9a40dc82fa7b187664bf00e4bd9e6d90b0182cb`
- `WholesomeAQSettings.cs` — `2daca32c77e1abee73232534d4b1761f6fad409cd2a41a494596690b07d270ed`
- `WholesomeAutoQuest.cs` — `1e88dfe3843f350df3a5e4acb9f638ef8890932fe5eadb9e22b65987158b73ec`

The other four post-change runtime hashes are:

- `DataLoader.cs` — `a85095519cd88211159366c1dade866e79b5522dfdcee6d5c96b0615a1c1f95c`
- `DataModels.cs` — `e118435d9ad5437e985710c80765e4e6c14f5a27824a36c6a38ef46c9b1992cb`
- `ProfileBuilder.cs` — `12660b3fa91ebc4b963f4bc37c19935f17d8d8301a78ecd77878ffaccead0e13`
- `QuestScheduler.cs` — `b621479733879722609aa7b1785ba29e9af85d97d898b3d2a82c5dfe1fa0b921`

These seven hashes were recorded in `D:\World of Warcraft 3.3.5a\CB\Backups\quest-recovery-wholesome-20260903-080658\installed.sha256.txt`. That file hashes to `961CA5487B99C233DD08DE6E549BD0BA245AE6E771C5DF4B128411016FF46CD9`. The original `manifest.sha256.txt` still hashes to `C5787DD61F477ECB40D3366F1834B3CD61123806436BF96109B611855D106D56` and all seven original backup entries revalidated with zero mismatches.

## Preservation and Concerns

- Existing unrelated vendor/grind working-tree changes were untouched and will be excluded from the Task 5 commit.
- Existing historical backup files were preserved. `installed.sha256.txt` is new, local-only, and does not replace the original manifest.
- Vendor blacklist storage remains vendor-scoped and unchanged; the quest blacklist storage paths are removed.
- No push, deploy, live replacement, bot launch, or apphost execution was performed.
