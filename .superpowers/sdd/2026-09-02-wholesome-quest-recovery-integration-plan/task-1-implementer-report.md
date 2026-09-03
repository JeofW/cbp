# Task 1 Implementer Report

Date: 2026-09-03
Task: Pure Scheduling and Endpoint Policy
Starting HEAD: `d1c6f983de7f2f838412d68416dbff624aea5c52`

## Implemented

- Added the Task 1 scheduling DTOs to `D:/World of Warcraft 3.3.5a/CB/Bots/WholesomeAutoQuest-master/DataModels.cs`.
- Added the pure scheduling and endpoint policy in `D:/World of Warcraft 3.3.5a/CB/Bots/WholesomeAutoQuest-master/QuestSchedulingPolicy.cs`.
- Added the no-apphost `net10.0-windows7.0` regression project under `Tools/WholesomeQuestRecoveryRegressionTests`.
- Covered stage priority, stage-local cooldown isolation, endpoint-local cooldown isolation, the five-endpoint cap, ordinary-work precedence over half-open probes, earliest retry/timed idle, deterministic tie-breakers, and caller-vetted grind fallback behavior.

## TDD Evidence

### RED

Command:

```powershell
& 'D:\World of Warcraft 3.3.5a\CB\.codex-source\.dotnet-sdk\dotnet.exe' run --project 'Tools\WholesomeQuestRecoveryRegressionTests\WholesomeQuestRecoveryRegressionTests.csproj' -c Release
```

Result: exit code 1. The regression project failed at the intended missing-policy boundary:

```text
CSC : error CS2001: Source file '...\Bots\WholesomeAutoQuest-master\QuestSchedulingPolicy.cs' could not be found.
The build failed. Fix the build errors and run again.
```

### GREEN

Build command:

```powershell
& 'D:\World of Warcraft 3.3.5a\CB\.codex-source\.dotnet-x86\dotnet.exe' build 'Tools\WholesomeQuestRecoveryRegressionTests\WholesomeQuestRecoveryRegressionTests.csproj' -c Release --no-restore --nologo --verbosity:minimal --no-incremental
```

Result: exit code 0, 0 errors. The build transitively performed the full Release x86 `CopilotBuddy` build. Existing baseline warning volume remains; the scoped check found 0 warning lines in `QuestSchedulingPolicy.cs` and 0 warning lines in the new DTO region of `DataModels.cs`.

Test command (direct DLL execution; no x86 apphost):

```powershell
& 'D:\World of Warcraft 3.3.5a\CB\.codex-source\.dotnet-x86\dotnet.exe' 'D:\World of Warcraft 3.3.5a\CB\.codex-source\CopilotBuddy\Tools\WholesomeQuestRecoveryRegressionTests\bin\Release\net10.0-windows7.0\WholesomeQuestRecoveryRegressionTests.dll'
```

Result: exit code 0.

```text
Wholesome scheduling policy regression tests passed.
```

## Rollback Backup

Backup directory:

```text
D:\World of Warcraft 3.3.5a\CB\Backups\quest-recovery-wholesome-20260903-080658
```

The directory contains the original `DataModels.cs`, `DataLoader.cs`, `QuestScheduler.cs`, `ProfileBuilder.cs`, `WholesomeAutoQuest.cs`, `WholesomeAQSettings.cs`, and `SettingsForm.cs`, plus `manifest.sha256.txt`. Every manifest entry was re-hashed successfully after the implementation. `QuestSchedulingPolicy.cs` is new and therefore has no pre-change copy.

## Scope and Concerns

- No push, deployment, live binary replacement, or x86 apphost execution was performed.
- Unrelated vendor/grind working-tree changes were not edited or staged.
- The Wholesome runtime source directory is intentionally outside the `CopilotBuddy` Git worktree and is linked into the regression project. Therefore the local commit records the Task 1 regression project and this report; the runtime `DataModels.cs` and `QuestSchedulingPolicy.cs` changes remain at the prescribed workspace-root paths and are protected by the backup above.
- Package vulnerability lookup emitted `NU1900` because NuGet was unreachable, and the repository emitted its accepted baseline compiler/package warnings. Neither produced a build error or a warning in newly added policy/DTO lines.
