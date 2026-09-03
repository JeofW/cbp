# Task 2 Implementer Report

Date: 2026-09-03
Task: Stage-Aware Scheduler and Recovery Context
Starting HEAD: `ec9fe117fb4e8fc876e04f87f2c1f062d7b0362a`

## Implemented

- Added deterministic quest-data metadata hashing to external `DataLoader.cs`; the fingerprint is populated only after a successful load.
- Replaced automatic scheduler blacklist ownership in external `QuestScheduler.cs` with pure, in-memory materialization through the core recovery manager's scoped decisions.
- Configured the recovery manager with dataset and navigation fingerprints, captured objective context, and marked authoritative completions before candidate filtering.
- Prioritized accepted pickup/objective/turn-in work, deferred new pickup and prerequisite-negative paths when completion authority is unknown, and added data-driven accepted-ancestor correction without quest-ID constants.
- Evaluated quest-stage, objective, NPC-relation, and quantized endpoint keys independently. Eligible alternate relations and objective clusters remain available; endpoint selection counts at most five distinct 80-yard map-cell keys per episode.
- Added narrow endpoint-failure reporting and quest-stage escalation only after every known cluster is tried or excluded.
- Exposed the resulting schedule and earliest retry, accepted only an explicit caller-vetted grind fallback, and added an exact-key first-activation claim seam without implementing Task 4 lifecycle ownership.
- Corrected the core recovery-context equipment fingerprint to preserve equipped-slot enumeration order and encode only item entry plus the existing critical/healthy durability class.
- Expanded both production-linked regression projects to cover the Task 2 scheduler and core context contracts.

## Exact Production Files Changed

External/local Wholesome runtime sources (not in the Git checkout):

```text
D:\World of Warcraft 3.3.5a\CB\Bots\WholesomeAutoQuest-master\DataLoader.cs
D:\World of Warcraft 3.3.5a\CB\Bots\WholesomeAutoQuest-master\QuestScheduler.cs
```

Core production source in the Git checkout:

```text
Styx/Logic/Questing/Recovery/QuestRecoveryRuntime.cs
```

No Task 3-5 production files were changed.

## TDD Evidence

### Scheduler RED

The new in-memory scheduler assertions were added before implementation. A non-incremental x86 build exited 1 with 29 expected missing-contract errors, including absent `DataLoader(string)`, `DatasetFingerprint`, `QuestScheduler.MaterializeSchedule`, scheduler snapshot/accepted DTOs, `EndpointKey`, `BeginActivation`, `ReportEndpointUnreachable`, and `CreateNavigationProviderFingerprint`.

Two later mutation checks caught real boundary defects before their fixes:

```text
System.InvalidOperationException: one objective-stage episode must retain no more than five distinct endpoint keys across all objectives
System.InvalidOperationException: a descendant that cannot be requested at the current level must not redirect accepted work
```

The endpoint cap was then applied across the entire objective-stage episode, and ancestor correction was constrained to an actually eligible descendant pickup request.

### Core Context RED

After adding the focused slot-order/durability regression, this command exited 1:

```powershell
& 'D:\World of Warcraft 3.3.5a\CB\.codex-source\.dotnet-x86\dotnet.exe' build 'Tools\QuestRecoveryRegressionTests\QuestRecoveryRegressionTests.csproj' -c Release --no-restore --nologo --verbosity:minimal --no-incremental
```

The intended failure was three occurrences of:

```text
error CS0117: 'QuestRecoveryRuntime' does not contain a definition for 'CreateEquipmentFingerprint'
```

The minimal core change introduced that test seam and made live capture delegate to it without entry sorting.

### GREEN

Core recovery regression build: exit 0, 3,246 repository baseline warnings, 0 errors, and 0 warning lines in the changed core runtime/test files.

```powershell
& 'D:\World of Warcraft 3.3.5a\CB\.codex-source\.dotnet-x86\dotnet.exe' 'Tools\QuestRecoveryRegressionTests\bin\Release\net10.0-windows7.0\QuestRecoveryRegressionTests.dll'
```

```text
Quest recovery regression tests passed.
```

Wholesome scheduler regression build: exit 0, 3,272 repository baseline warnings, 0 errors, and 0 warning lines in the changed Task 2 loader, scheduler, runtime, or regression files. The scoped output contains only pre-existing linked `ProfileBuilder.cs` nullability warnings.

```powershell
& 'D:\World of Warcraft 3.3.5a\CB\.codex-source\.dotnet-x86\dotnet.exe' 'Tools\WholesomeQuestRecoveryRegressionTests\bin\Release\net10.0-windows7.0\WholesomeQuestRecoveryRegressionTests.dll'
```

```text
Wholesome scheduler recovery regression tests passed.
```

Full build (no apphost):

```powershell
& 'D:\World of Warcraft 3.3.5a\CB\.codex-source\.dotnet-x86\dotnet.exe' build 'CopilotBuddy.csproj' -c Release --no-restore --nologo --verbosity:minimal --no-incremental -p:Platform=x86 -p:UseAppHost=false
```

Result: exit 0, 3,250 repository baseline warnings, 0 errors, and no warning line in `QuestRecoveryRuntime.cs`.

## Static and Integrity Checks

- Forbidden scheduler scan found no automatic blacklist fields/methods, empty-profile loop, filename discovery, bot-tree restart, or evaluation-time `TryBeginAttempt` ownership.
- Quest-ID scan found no `867`, `875`, or `876` constants in the changed runtime loader/scheduler.
- Required-integration scan found configuration/capture, authoritative completion refresh and `MarkCompleted`, scoped evaluation, schedule/earliest retry, exact activation, endpoint reporting, and navigation fingerprint seams.
- `git diff --check` passed for every repo-local Task 2 file; external Task 2 files have no trailing whitespace.
- Every file in `D:\World of Warcraft 3.3.5a\CB\Backups\quest-recovery-wholesome-20260903-080658` still matches `manifest.sha256.txt`.

## Scope and Concerns

- No push, deployment, live binary replacement, package restore, or x86 apphost execution was performed.
- Unrelated vendor/grind worktree changes were preserved and are excluded from this task's staging.
- `QuestScheduler.cs` no longer owns or writes legacy automatic blacklist state. Review round 1 restored only the temporary narrow compatibility adapter names described below; removing those caller seams remains Task 4 work.
- `LastSchedule.Plan` now carries scoped alternate work, but the current legacy profile builder still receives a quest list. Rendering that plan is Task 3 and was deliberately not implemented here.
- The external Task 1 `DataModels.cs` and `QuestSchedulingPolicy.cs` changes remain present. Task 2 changed only the two external files listed above.
- NuGet vulnerability lookup can emit `NU1900` while the network source is unavailable; it did not cause a build failure.

## Review Round 1

Review fixes were implemented with production-linked regressions before each behavior change.

### Compatibility Compile Contract

The regression project now compiles the complete external Wholesome bot caller plus its UI/vendor dependencies, not only the scheduler subset.

RED was a Release x86 build failure at the real caller boundary:

```text
WholesomeAutoQuest.cs(212,28): error CS1061: 'QuestScheduler' does not contain a definition for 'SyncBlacklist'
WholesomeAutoQuest.cs(473,36): error CS1061: 'QuestScheduler' does not contain a definition for 'BlacklistQuestGiver'
WholesomeAutoQuest.cs(474,184): error CS1061: 'ForcedQuestPickUp' does not contain a definition for 'PickupFailureReason'
```

`SyncBlacklist` is now an obsolete internal no-op, and `BlacklistQuestGiver` is an obsolete internal adapter that reports `PickupTargetNotOffered` against the exact pickup NPC-relation key using the last captured context. Neither adapter owns a set or writes a manual/global blacklist. The caller log now uses `LastOutcome.Evidence`. A reflection regression requires both adapters to remain non-public and obsolete until Task 4 removes the calls.

### Ordinary Versus Half-Open Paths

RED direct-DLL execution failed because a near half-open endpoint demoted the entire accepted objective candidate, causing ordinary pickup work to displace it. A second RED showed that same-cell ordinary giver B inherited half-open giver A's coordinates.

Endpoint selection now separates ordinary and half-open pools. Any ordinary path wins; half-open paths are considered only when no ordinary path exists and are capped at one probe. Quest-level policy still gives all ordinary candidates precedence and applies its global one-probe budget. Relation plans now source hotspots from their selected relation instead of a shared endpoint-key coordinate cache.

### Objective Progress

RED direct-DLL execution reported:

```text
System.InvalidOperationException: completed objective clusters must be skipped before incomplete work consumes the five-key endpoint budget
```

Live descriptor counts are now captured per accepted quest in deterministic quest-ID order, stored in `QuestSchedulerAcceptedQuest.ObjectiveCounts`, and flattened into the same recovery context. A completed objective is skipped before scoped evaluation and clustering only when a present count reaches its declared required count. Missing counts conservatively retain objective work.

### Scan Expansion

The scan-expansion regression initially failed compilation with five `CS1061` errors for missing `ApplyScanExpansionBeforeFallback`.

No-selection scans now advance by `ScanStep` up to `ScanMaxDistance` and suppress timed-idle or vetted fallback during expansion. Fallback is allowed only after a scan has run at the maximum radius. Successful selection and scheduler lifecycle reset restore `ScanStartDistance`.

### Production Activation Ownership

The activation regression initially failed compilation with five `CS1061` errors for missing `ObserveActivation`.

The actual Wholesome `Pulse` path now observes `QuestOrder.Instance.CurrentBehavior`. Pickup/turn-in activation uses the exact NPC-relation key; objective activation uses the exact quest/objective-stage key. Behavior identity prevents pulse-repeat claims. A denied atomic claim clears the current behavior's POI and requests one coalesced rebuild; a won claim does neither.

### Safety and Reachability Ordering

Two compile REDs established the missing contract:

```text
error CS1061/CS0117: 'QuestEndpointCandidate' does not contain a definition for 'IsKnownReachable'/'SafetyScore'
error CS1061/CS0117: 'QuestEndpointCandidate' does not contain a definition for 'IsKnownSafe'
error CS1739: 'MaterializeSchedule' does not have a parameter named 'isKnownUnsafe'
```

Spawn and endpoint descriptors now distinguish known-safe, known-reachable, and safety score. Known unsafe/unreachable points are filtered before selection. Production also injects the existing cheap blackspot geometry check; no path generation occurs in materialization ordering or its comparator. Remaining candidates sort by safety descending, distance ascending, then stable recovery key before distinct-key cap five.

### Review Verification

Commands used the bundled x86 `dotnet.exe` and direct DLL execution only:

```powershell
& 'D:\World of Warcraft 3.3.5a\CB\.codex-source\.dotnet-x86\dotnet.exe' build 'Tools\WholesomeQuestRecoveryRegressionTests\WholesomeQuestRecoveryRegressionTests.csproj' -c Release --no-restore --nologo --verbosity:minimal --no-incremental
& 'D:\World of Warcraft 3.3.5a\CB\.codex-source\.dotnet-x86\dotnet.exe' 'Tools\WholesomeQuestRecoveryRegressionTests\bin\Release\net10.0-windows7.0\WholesomeQuestRecoveryRegressionTests.dll'
& 'D:\World of Warcraft 3.3.5a\CB\.codex-source\.dotnet-x86\dotnet.exe' build 'Tools\QuestRecoveryRegressionTests\QuestRecoveryRegressionTests.csproj' -c Release --no-restore --nologo --verbosity:minimal --no-incremental
& 'D:\World of Warcraft 3.3.5a\CB\.codex-source\.dotnet-x86\dotnet.exe' 'Tools\QuestRecoveryRegressionTests\bin\Release\net10.0-windows7.0\QuestRecoveryRegressionTests.dll'
& 'D:\World of Warcraft 3.3.5a\CB\.codex-source\.dotnet-x86\dotnet.exe' build 'CopilotBuddy.csproj' -c Release --no-restore --nologo --verbosity:minimal --no-incremental -p:Platform=x86 -p:UseAppHost=false
```

Results:

```text
Wholesome production-linked build: 0 errors
Wholesome scheduler recovery regression tests passed.
Core recovery build: 3,246 baseline warnings, 0 errors
Quest recovery regression tests passed.
Full Release x86 build: 3,250 baseline warnings, 0 errors
```

The expanded linked build exposes existing nullability warnings in untouched Wholesome UI/vendor/caller code. There are no warning/error lines in the changed scheduler, scheduling policy, loader, or regression program, and no new obsolete-call warnings because the two temporary call sites are narrowly suppressed.

External runtime files changed by review round 1:

```text
D:\World of Warcraft 3.3.5a\CB\Bots\WholesomeAutoQuest-master\DataModels.cs
D:\World of Warcraft 3.3.5a\CB\Bots\WholesomeAutoQuest-master\QuestSchedulingPolicy.cs
D:\World of Warcraft 3.3.5a\CB\Bots\WholesomeAutoQuest-master\QuestScheduler.cs
D:\World of Warcraft 3.3.5a\CB\Bots\WholesomeAutoQuest-master\WholesomeAutoQuest.cs
```

Forbidden scans found no automatic scheduler blacklist fields, file discovery, empty-profile loop, or tree restart. The only `TryBeginAttempt` call is in the production activation observer, and no reviewed production source contains fixture quest IDs. External changed files have no trailing whitespace; every rollback-backup file still matches its manifest. Unrelated vendor/grind changes remain untouched and unstaged. No push, deployment, live binary replacement, restore, or apphost execution occurred.

## Review Round 2

The remaining safety/reachability and lost-claim cleanup findings were reproduced in the production-linked regression project before changing runtime code.

### RED Evidence

The first corrected test compile failed only on the new production contracts:

```text
RED_EXIT=1
error CS1739: MaterializeSchedule has no parameter named 'navigationAssessment'
error CS0117: QuestScheduler has no definition for 'AssessNavigation'
error CS0037: null cannot be assigned to the existing non-nullable safety/reachability flags
error CS0117: WholesomeAutoQuest has no definition for 'TryClearDeniedRecoveryPoi'
```

This established that unannotated loaded records were still represented as confirmed good, materialization had no live navigation enrichment seam, and the linked production bot had no evidence-based POI ownership guard.

### Live Navigation Enrichment

`SpawnPoint` and `QuestEndpointCandidate` now represent safe/reachable as nullable facts: `false` is known bad, `true` is live-confirmed, and `null` is unknown. Unannotated JSON therefore remains unknown instead of silently becoming safe/reachable.

Production scheduling supplies `AssessNavigation` for every scan. It checks the blackspot hazard signal, then calls `PathDistance` on the active navigation provider. A complete path confirms reachability and contributes a deterministic path-detour score; no path confirms unreachable. A missing provider remains unknown, and either hazard/provider exception returns unknown rather than confirmed safe. Dataset `false` hints remain authoritative exclusions, while positive dataset hints alone cannot manufacture live confirmation.

Materialization caches this assessment by the same map/80-yard quantized cluster key used for endpoints. The loaded-data regression uses eight unannotated records in seven cells and observes exactly seven enrichment calls. Five nearer live-unsafe or unreachable cells are removed before the cap, so the farther confirmed safe/reachable cell is retained. A provider-exception cell remains unknown, and policy ranks confirmed safe/reachable endpoints before unknown fallback endpoints before applying the five-key cap. Pathfinding is not performed by the sort comparator.

### Exact POI Ownership

The real `WholesomeAutoQuest.ObserveRecoveryActivation` callback now passes the denied recovery key to `TryClearDeniedRecoveryPoi`. Cleanup requires the same behavior object to remain current, that behavior to still map to the denied exact key, and a quest-owned POI match:

- pickup/turn-in nodes must match quest and NPC relation;
- POIs without embedded quest identity must match the permitted quest POI type, NPC entry, and behavior endpoint proximity;
- objective quest POIs must match quest ID, while objective hotspots must match the current objective location.

Combat/Kill, vendor/repair/mail/trainer, unrelated, wrong-stage, and stale-endpoint POIs are never cleared by this adapter. The existing scheduler rebuild request remains coalesced. The linked regression proves an exact pickup POI is cleared once while combat, repair, stale-location, and replaced-behavior cases are left intact.

### Review Round 2 Verification

All commands used the bundled x86 `dotnet.exe`, `UseAppHost=false`, and direct DLL execution:

```text
Wholesome production-linked non-incremental build: 3,317 baseline warnings, 0 errors
Changed-file scoped diagnostics: 0
Wholesome scheduler recovery regression tests passed.
Core recovery non-incremental build: 3,246 baseline warnings, 0 errors
Quest recovery regression tests passed.
Full Release x86 non-incremental build: 3,250 baseline warnings, 0 errors
```

Static/integrity results:

```text
TRAILING_WHITESPACE_MATCHES=0
SCHEDULER_FORBIDDEN_MATCHES=0
COMPARATOR_PATH_CALLS=0
BOUNDED_PATH_CALL_SITES=1
DENIED_CLEAR_SITES=1
BACKUP_HASH_MISMATCHES=0
```

External runtime files changed by review round 2 and intentionally left local:

```text
D:\World of Warcraft 3.3.5a\CB\Bots\WholesomeAutoQuest-master\DataModels.cs
D:\World of Warcraft 3.3.5a\CB\Bots\WholesomeAutoQuest-master\QuestSchedulingPolicy.cs
D:\World of Warcraft 3.3.5a\CB\Bots\WholesomeAutoQuest-master\QuestScheduler.cs
D:\World of Warcraft 3.3.5a\CB\Bots\WholesomeAutoQuest-master\WholesomeAutoQuest.cs
```

Only the production-linked regression source and this report are repo-local review-round changes. No core runtime change was needed. Unrelated vendor/grind dirt remains untouched and unstaged. No push, deployment, live binary replacement, restore, or apphost execution occurred.
