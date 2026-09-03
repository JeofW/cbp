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

## Broad fix round 2 review fixes

Base: `3a4c89f88dfc6cfd6b03c0a62d5d935dc4bf2279`

### Outcome

- Attempt generations now come from a manager-level high-water mark rather than `currentRecord + 1`. The high-water mark survives identity reconfiguration and record replacement/removal.
- `LastAttemptGeneration` is persisted independently in the identity document and seeds the manager on load, so an empty record set cannot reset ownership IDs across reload.
- Success is accepted only while the matching generation actively owns an `Attempting` record. HalfOpen is an invitation to acquire a new probe, not active ownership; delayed success from the failed pre-RetryNow attempt cannot close it.

### Behavioral RED evidence

- Identity-switch regression failed with `identity replacement must issue B an ownership generation newer than A`; both identities had reused generation 1.
- Manual-blacklist replacement regression failed with `manual blacklist replacement must not reuse A's ownership generation for B`; removal of the manual record reset generation issuance.
- RetryNow regression failed with `RetryNow must reject old A success until a new half-open probe acquires ownership`; the old A generation was accepted directly against HalfOpen.
- Empty-store reload regression failed with `the ownership high-water mark must survive reload even when no record remains`; record-only persistence lost the issuance fence.

### Final GREEN scope

- The three required A/B stale-callback cases and the empty-store persistence case pass against the production manager. The prior normal A-success, B-acquire, delayed-A-rejected, valid-B-release sequence remains covered and passing.
- Existing terminal-state and success persistence/reload tests remain in the focused recovery suite.
- `QuestRecoveryRegressionTests` built with 0 errors and printed `Quest recovery regression tests passed.`; `QuestPickupPolicyRegressionTests` built with 0 errors and printed `Quest pickup policy regression tests passed.`
- Full Release x86 Rebuild printed `Build succeeded`, 3250 repository baseline warnings, and 0 errors. Scoped file-log scanning found no warnings in `QuestRecoveryManager.cs` or `QuestRecoveryTypes.cs`; `CopilotBuddy.dll` remained PE machine `0x014C`.
- `git diff --check` was clean apart from informational line-ending notices. No apphost, push, or deployment was used.
- Scoped files: `QuestRecoveryManager.cs`, `QuestRecoveryTypes.cs`, `QuestRecoveryRegressionTests/Program.cs`, and this report.

### Concern

The no-client suites cover the pure selection/classification seams and production evaluation/executor policies, but cannot exercise live WoW frame transitions or memory-backed quest-log state. Before deployment, a disposable-character smoke test should verify one unique-title zero-ID dialog, one duplicate-title dialog, and an accepted-complete blocker while the historical cache refresh is unavailable. No push, deployment, or apphost launch was performed. Unrelated pre-existing vendor/grind working-tree files were neither edited for this fix nor staged.

## Broad fix round 1 review fixes

Base: `d0bc5b09a87300efeabf9a61056252555e4cd74a`

### Outcome

- Objective completion policy now defers every Unknown completion state, including the defensive `(Unknown, accepted)` combination, and therefore never skips an accepted objective on incoherent evidence.
- `QuestCompletionSnapshot` captures `IsAccepted` and tri-state completion from one live `PlayerQuest` lookup. `ForcedBehaviorExecutor` consumes that single snapshot instead of reading acceptance and completion separately across a quest-log transition.
- Every successful ownership release now carries the monotonic `AttemptGeneration` returned by `TryBeginAttempt`. The generation is persisted in recovery records and preserved by policy/manager copies and completed-record compaction. A stale or duplicate success with a non-current generation is rejected without state mutation or false success evidence.

### Behavioral RED evidence

- Before the policy fix, the focused recovery executable failed with `an accepted objective with an incoherent unknown completion snapshot must defer, never skip` because `ForObjective(Unknown, accepted: true)` returned `Skip`.
- Before the snapshot API, the focused build failed with `CS0117: 'QuestLog' does not contain a definition for 'ResolveQuestCompletionSnapshot'`.
- Before ownership generations, the focused build failed with `CS1061` because `QuestRecoveryDecision` and `QuestRecoveryRecord` had no `AttemptGeneration` contract. The wished-for A/B/C lifecycle could not compile.

### Final GREEN evidence

- `QuestRecoveryRegressionTests`: build succeeded with 0 errors; DLL printed `Quest recovery regression tests passed.` The new lifecycle proves A can succeed, B receives a newer generation, delayed duplicate A cannot release B or append success evidence, C remains denied, and valid B success enables a still-newer C. It also proves generation persistence and monotonicity after reload.
- `QuestPickupPolicyRegressionTests`: build succeeded with 0 errors; DLL printed `Quest pickup policy regression tests passed.`
- Full `CopilotBuddy.csproj` Release x86 Rebuild: `Build succeeded`, 3250 repository baseline warnings, 0 errors. The scoped file-log scan found no warnings in the seven changed production/test files. `CopilotBuddy.dll` remained PE machine `0x014C` (x86).
- `git diff --check` was clean apart from informational line-ending notices. No apphost, push, or deployment was used.

### Changed files

- `Bots/Quest/Actions/ForcedBehaviorExecutor.cs`
- `Bots/Quest/QuestOrder/QuestNodeCompletionPolicy.cs`
- `Styx/Logic/Questing/QuestLog.cs`
- `Styx/Logic/Questing/Recovery/QuestRecoveryManager.cs`
- `Styx/Logic/Questing/Recovery/QuestRecoveryPolicy.cs`
- `Styx/Logic/Questing/Recovery/QuestRecoveryTypes.cs`
- `Tools/QuestRecoveryRegressionTests/Program.cs`
- `.superpowers/sdd/2026-09-02-quest-recovery-core-plan/core-final-fix-report.md`
