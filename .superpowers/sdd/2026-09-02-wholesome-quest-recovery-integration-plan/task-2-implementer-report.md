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
- `QuestScheduler.cs` no longer provides the legacy automatic blacklist methods. Updating the existing `WholesomeAutoQuest.cs` lifecycle caller belongs to Task 4, so building the whole external bot source as a standalone plugin remains a planned cross-task integration point.
- `LastSchedule.Plan` now carries scoped alternate work, but the current legacy profile builder still receives a quest list. Rendering that plan is Task 3 and was deliberately not implemented here.
- The external Task 1 `DataModels.cs` and `QuestSchedulingPolicy.cs` changes remain present. Task 2 changed only the two external files listed above.
- NuGet vulnerability lookup can emit `NU1900` while the network source is unavailable; it did not cause a build failure.
