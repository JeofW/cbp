# Task 1 implementer report

## Scope

Implemented only the conservative quest-abandonment and alternate-relation parsing slice:

- `Styx/Logic/Questing/Recovery/QuestAbandonmentPolicy.cs`
- `Styx/Logic/Questing/Recovery/QuestRelationParser.cs`
- `Tools/QuestRecoveryRegressionTests/Program.cs`
- `Tools/QuestRecoveryRegressionTests/QuestRecoveryRegressionTests.csproj`

The policy is pure: it does not call WoW, Lua, `QuestRecoveryManager`, `QuestLog`, or `TreeRoot`, and it does not mutate recovery state. It permits automatic abandonment only for an accepted, certainly incomplete, zero-progress, automatically quarantined quest at 0, 1, or 2 free quest-log slots. Every guard has a deterministic denial reason. A `UserExcluded` reason is denied even if supplied with an inconsistent `Quarantined` state.

The parser uses invariant culture for `entry,x,y,z` segments. It keeps valid siblings, requires a positive entry and finite coordinates, skips rejected records instead of fabricating zero coordinates, and returns ordered deterministic diagnostics.

The regression project remains no-apphost and is explicitly x86. Its output contains the test DLL and metadata/PDB only; no `QuestRecoveryRegressionTests.exe` was produced.

## TDD evidence

RED command:

```powershell
& 'D:\World of Warcraft 3.3.5a\CB\.dotnet-sdk\dotnet.exe' exec 'D:\World of Warcraft 3.3.5a\CB\.dotnet-sdk\sdk\10.0.400\dotnet.dll' run --project '.\Tools\QuestRecoveryRegressionTests\QuestRecoveryRegressionTests.csproj' -c Release --no-restore -p:UseAppHost=false -p:Platform=x86
```

Result: exit 1. The expected `CS0103`/`CS0246` failures reported missing `QuestAbandonmentPolicy`, `QuestAbandonmentContext`, and `QuestRelationParser`. No production implementation existed during this run.

GREEN used the same command after the minimal implementation. Result: exit 0 and `Quest recovery regression tests passed.`

Fresh direct no-apphost execution:

```powershell
& 'D:\World of Warcraft 3.3.5a\CB\.dotnet-sdk\dotnet.exe' exec '.\Tools\QuestRecoveryRegressionTests\bin\x86\Release\net10.0-windows7.0\QuestRecoveryRegressionTests.dll'
```

Result: exit 0 and `Quest recovery regression tests passed.`

## Build and warning evidence

Full non-incremental core command:

```powershell
& 'D:\World of Warcraft 3.3.5a\CB\.dotnet-sdk\dotnet.exe' exec 'D:\World of Warcraft 3.3.5a\CB\.dotnet-sdk\sdk\10.0.400\dotnet.dll' build '.\CopilotBuddy.csproj' -c Release --no-restore --no-incremental -p:UseAppHost=false -p:Platform=x86 -v:minimal
```

Result: exit 0, 0 errors, 3,250 repository-baseline warnings, and 0 warning lines scoped to the two new recovery files or modified recovery regression files.

A separate non-incremental build of `QuestRecoveryRegressionTests.csproj` also exited 0 with 0 errors and no compiler warning in any Task 1 `.cs` file. Its only project-scoped diagnostic was the existing offline vulnerability-feed `NU1900` warning (printed twice by the build).

`git diff --check` passed for the Task 1 files. The checkout's unrelated vendor/inventory changes were not edited or staged by this task.

## Deferred and concerns

- No push, deployment, live binary replacement, WoW/CopilotBuddy launch, or live smoke was performed, as required by the local-only boundary.
- The build still reports the existing repository warning volume and offline NuGet vulnerability-feed `NU1900`; neither originates in Task 1.
- The broader design's active-prerequisite check and persist-before-client-action rule remain adapter responsibilities because they are not inputs or side effects of this pure Task 1 policy contract.

Commit message: `feat: guard automatic quest abandonment`

## Review round 1

The policy now rejects negative free-slot counts, every defined state except exact `Quarantined`, undefined recovery states, and quarantine reasons that cannot represent automatic failure evidence. Its closed automatic-reason allowlist covers every current operational failure plus the automatic migration reason `LegacyUnknown`. It gives distinct diagnostics for invalid slot counts, known non-quarantined states, unknown states, missing reason, `TurnInQuestIncomplete` redirect, manual exclusion, and unknown/unsupported reasons.

The regression matrix now covers free slots `-1,0,1,2,3`; all seven defined recovery states plus an undefined value; all sixteen valid automatic reasons; `None`, `TurnInQuestIncomplete`, `UserExcluded`, and an undefined reason. Parser boundaries now also cover negative and uint-overflow entries, surrounding whitespace, duplicate/order preservation, and null/empty input in addition to malformed and nonfinite coordinates.

Review RED used the same no-apphost x86 regression command above. Result: exit 1 with `the -1-free-slot boundary must return its exact abandonment decision`, proving the prior policy admitted an invalid count.

Review GREEN used the same command after the minimal policy change. Result: exit 0, `Quest recovery regression tests passed.`, and 0 warning lines in Task 1 `.cs` files. The parser passed its new boundary cases without a production edit.

The fresh full non-incremental Release x86 build again exited 0 with 0 errors, 3,250 repository-baseline warnings, and 0 Task 1 source warning lines. No apphost, push, deployment, live binary change, or live execution was used.
