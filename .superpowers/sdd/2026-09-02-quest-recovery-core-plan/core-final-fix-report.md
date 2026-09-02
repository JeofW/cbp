# Quest Recovery Core Broad Fix Report

## Broad fix round 1/5

Base: `9fba3d46a004c98659e5cd44be11c30710d2eebc`

Commit message: `fix: close quest recovery authority gaps`.

### Outcome

All four broad-review findings are closed in production-linked code and behavioral regression coverage.

1. Quest completion now has explicit `Unknown`, `KnownIncomplete`, and `KnownComplete` authority. Accepted quests use live `PlayerQuest.IsCompleted`; non-accepted quests use only a valid current-identity completed cache. A scoped, thread-local evaluation frame preserves Unknown through positive, negative, combined fast-path, Roslyn, and `CompileBatch` expressions, including nesting and exceptions. `If`, `While`, `GrindTo`, compiled custom behaviors, progress requirements, and pickup/turn-in/objective executor decisions defer without advancing or scheduling actions while authority is Unknown. Completion-dependent `OnStart` side effects are also delayed.
2. `QuestAttemptOutcome.Success(...)` and `QuestAttemptOutcomeKind` explicitly represent success. Reporting success for owned Attempting/HalfOpen work atomically returns it to Eligible, clears retry/escalation state, preserves bounded evidence, persists cleanly, and permits exactly one later owner without restart. ManualBlacklist and Completed remain terminal.
3. Pickup blocker handling uses live accepted-quest completion before historical cache authority. Its pure policy receives separate completion-known/completed inputs, and advances another quest only when both are true and an advance control is present.
4. Zero-ID title fallback requires a nonempty exact trimmed ordinal title plus proof of one unique matching offered title. Gossip selection counts matches, never picks the first duplicate, carries uniqueness proof into the selected dialog, and clears it at interaction/frame lifecycle boundaries. ID selection remains primary.

### Behavioral RED evidence

- Recovery tests initially failed to compile because `QuestCompletionState`, `QuestConditionEvaluation`, `QuestNodeCompletionPolicy`, `QuestAttemptOutcome.Success`, and the outcome discriminator did not exist.
- Pickup tests initially failed to compile because the explicit completion-authority/uniqueness `Decide` overload and unique-title helper did not exist.
- A deliberate `ForcedIf` mutation mapping Unknown to False produced the runtime failure: `ForcedIf must remain pending instead of scheduling its else body for unknown positive completion`; restoring Unknown deferral made it pass.
- The first execution-gate test failed to compile before `ForcedBehavior.IsExecutionDeferred` existed.
- The lifecycle test that invokes `ForcedGrindTo.OnStart` before `IsDone` failed at runtime with `ForcedGrindTo must defer initialization and keep the node running under unknown completion`; gating initialization before side effects made it pass.

### Final GREEN evidence

Commands used the bundled SDK/host and never launched an apphost:

```powershell
& '..\..\.dotnet-sdk\dotnet.exe' build '.\Tools\QuestRecoveryRegressionTests\QuestRecoveryRegressionTests.csproj' -c Release --no-restore --nologo -v:q -clp:ErrorsOnly
& '..\..\.dotnet-sdk\dotnet.exe' '.\Tools\QuestRecoveryRegressionTests\bin\Release\net10.0-windows7.0\QuestRecoveryRegressionTests.dll'
```

Result: build succeeded, 0 errors; `Quest recovery regression tests passed.`

```powershell
& '..\..\.dotnet-sdk\dotnet.exe' build '.\Tools\QuestPickupPolicyRegressionTests\QuestPickupPolicyRegressionTests.csproj' -c Release --no-restore --nologo -v:q -clp:ErrorsOnly
& '..\..\.dotnet-sdk\dotnet.exe' '.\Tools\QuestPickupPolicyRegressionTests\bin\Release\net10.0-windows7.0\QuestPickupPolicyRegressionTests.dll'
```

Result: build succeeded, 0 errors; `Quest pickup policy regression tests passed.`

```powershell
& '..\..\.dotnet-sdk\dotnet.exe' build '.\CopilotBuddy.csproj' -c Release -p:Platform=x86 -t:Rebuild --no-restore --nologo -v:q -clp:ErrorsOnly
```

Result: `Build succeeded`, 3250 repository baseline warnings, 0 errors. A file-logged scoped scan found no warnings for the changed implementation lines or the two new production files. The rebuilt `CopilotBuddy.dll` PE machine is `0x014C` (x86). `git diff --check` is clean apart from informational CRLF conversion notices.

### Changed files

- `Bots/Quest/Actions/ForcedBehaviorExecutor.cs`
- `Bots/Quest/QuestOrder/ForcedBehavior.cs`
- `Bots/Quest/QuestOrder/ForcedCodeBehavior.cs`
- `Bots/Quest/QuestOrder/ForcedGrindTo.cs`
- `Bots/Quest/QuestOrder/ForcedIf.cs`
- `Bots/Quest/QuestOrder/ForcedQuestPickUp.cs`
- `Bots/Quest/QuestOrder/ForcedWhile.cs`
- `Bots/Quest/QuestOrder/QuestNodeCompletionPolicy.cs`
- `Bots/Quest/QuestOrder/QuestPickupDialogPolicy.cs`
- `Styx/Logic/Profiles/Quest/ConditionHelper.cs`
- `Styx/Logic/Profiles/Quest/ProfileHelperFunctionsBase.cs`
- `Styx/Logic/Profiles/Quest/QuestConditionEvaluation.cs`
- `Styx/Logic/Questing/CustomForcedBehavior.cs`
- `Styx/Logic/Questing/QuestLog.cs`
- `Styx/Logic/Questing/Recovery/QuestRecoveryManager.cs`
- `Styx/Logic/Questing/Recovery/QuestRecoveryTypes.cs`
- `Tools/QuestPickupPolicyRegressionTests/Program.cs`
- `Tools/QuestRecoveryRegressionTests/Program.cs`
- `.superpowers/sdd/2026-09-02-quest-recovery-core-plan/core-final-fix-report.md`

### Concern

The no-client suites cover the pure selection/classification seams and production evaluation/executor policies, but cannot exercise live WoW frame transitions or memory-backed quest-log state. Before deployment, a disposable-character smoke test should verify one unique-title zero-ID dialog, one duplicate-title dialog, and an accepted-complete blocker while the historical cache refresh is unavailable. No push, deployment, or apphost launch was performed. Unrelated pre-existing vendor/grind working-tree files were neither edited for this fix nor staged.
