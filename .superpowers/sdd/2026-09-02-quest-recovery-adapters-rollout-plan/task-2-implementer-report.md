# Task 2 implementer report

## Scope and behavior

Implemented only the SafePickUp adapter slice. The installed runtime source is
`D:/World of Warcraft 3.3.5a/CB/Quest Behaviors/SafePickUp.cs`; it remains an
external local file and is not part of the Git commit.

The adapter now:

- configures and captures the shared recovery runtime at `OnStart`, then atomically
  claims the exact pickup quest-stage key;
- logs a denied recovery decision, completes the profile node, and clears only a
  `QuestPickUp` POI whose `PickUpNode.QuestId` matches this behavior;
- never reads or writes the legacy blacklist, never abandons a WoW quest, and never
  stops or starts `TreeRoot`;
- parses optional `AlternateGivers` through the core strict parser, logs each rejected
  segment once, and keeps valid siblings;
- resolves candidates in the required order: matching live unit/game object, primary
  NPC database location, then supplied alternate coordinates;
- does not treat a missing primary database row as failure when live or alternate
  evidence supplies a usable giver;
- consumes each `ForcedQuestPickUp` outcome exactly once, preserving its target,
  shown quest, giver, offered IDs, evidence, and interaction-cycle ID;
- reports pre-boundary mismatch samples as observations and the real third cycle as
  one `PickupTargetNotOffered` or `PickupWrongQuestShown` failure episode;
- reports reached-giver/no-dialog expiry as `InteractionTimedOut`, and navigation
  expiry first as a narrow `PathGenerationFailed` endpoint before a final
  `EndpointUnreachable` stage outcome only after alternatives are exhausted;
- reports `NpcMissingFromDatabase` only when live lookup, the primary database
  location, and valid alternate coordinates produce no candidate;
- retains the exact pickup-stage attempt key/generation on generated relation,
  endpoint, and terminal reports; and
- neutrally calls `AbandonAttempt` with that exact generation from `Dispose` if the
  behavior exits without a terminal success/failure report. The nested
  `ForcedQuestPickUp` remains disposed.

The core ownership fence was narrowly extended so a pickup quest-stage owner can
authorize only same-quest pickup/NPC and navigation/endpoint child failures. Existing
cross-quest, cross-scope, target-ownership, stale-generation, and atomic batch guards
remain in force.

## Required backup

Created before the first external source edit and never overwritten:

`D:/World of Warcraft 3.3.5a/CB/Backups/quest-recovery-adapters-20260904-132002567`

Manifest SHA-256:

`24e67583721d535328ad8e82a7e445b68cfd8e19607cfcbe12a46532bd893c84`

Fresh manifest verification reported three entries and `BACKUP_VERIFIED=True`:

- `dd9231623997d655ba978937eded1615bd34b33ebad7f6c88c5413a04c370c01`  `Quest Behaviors/SafePickUp.cs`
- `547a93da5458bd9c6d12499804997e1d1be673bd220c645e6f293fc5342a80c2`  `Quest Behaviors/SafeTurnIn.cs`
- `a1bfce47208f4b357b51d01714680ccf1762759a3db6677fbde446af1459dc36`  `Plugins/ZygorProfileRecovery/ZygorProfileRecovery.cs`

Post-change installed SafePickUp SHA-256:

`9b8fdae516e9233880f8fa49d761d597dc8674cc0dc324618e0f94ca2f2b5d51`

## TDD evidence

The production-linked adapter harness was created first as a
`net10.0-windows7.0`, x86, `UseAppHost=false` project linking the installed
`SafePickUp.cs` and referencing `CopilotBuddy.csproj`.

Initial RED command:

```powershell
& 'D:\World of Warcraft 3.3.5a\CB\.dotnet-sdk\dotnet.exe' exec 'D:\World of Warcraft 3.3.5a\CB\.dotnet-sdk\sdk\10.0.400\dotnet.dll' run --project '.\Tools\QuestRecoveryAdapterRegressionTests\QuestRecoveryAdapterRegressionTests.csproj' -c Release -p:UseAppHost=false -p:Platform=x86
```

Result: exit 1 with two expected `CS0117` errors because
`SafePickUp.CreateUnavailableOutcome` did not exist.

The core ownership RED used the same bundled SDK-DLL/no-apphost invocation against
`QuestRecoveryRegressionTests.csproj`. Result: exit 1 with the expected
`ArgumentException: The failure target is outside the attempt owner's quest
hierarchy` for a pickup-stage-owned NPC relation.

A second adapter RED added the reached-giver timeout contract. Result: exit 1 with
the expected `CS0117` because `CreateInteractionTimeoutOutcome` did not exist.

After the minimal production changes, all three RED cases turned GREEN. The adapter
factory tests assert literal key scope, target/shown/giver/offered values, exact
evidence, cycle ID, owner generation, reason, outcome kind, and episode flag. The
core regression asserts accepted narrow pickup relation and endpoint failures while
the exact stage owner remains active, followed by an exact terminal stage release.

## Fresh verification

All focused projects were rebuilt and run with the bundled SDK DLL,
`UseAppHost=false`, and x86:

- adapter regression: exit 0, `Quest recovery adapter regression tests passed.`
- core recovery regression: exit 0, `Quest recovery regression tests passed.`
- Wholesome integration regression: exit 0,
  `Wholesome scheduler recovery regression tests passed.`
- pickup policy regression: exit 0, `Quest pickup policy regression tests passed.`

Fresh direct execution of
`Tools/QuestRecoveryAdapterRegressionTests/bin/x86/Release/net10.0-windows7.0/QuestRecoveryAdapterRegressionTests.dll`
also exited 0. Its SHA-256 is
`25063bf4d1202c9091320c588b4485884afd87a277adeab1c62cd495a3aca78a`.

The full non-incremental build command was:

```powershell
& 'D:\World of Warcraft 3.3.5a\CB\.dotnet-sdk\dotnet.exe' exec 'D:\World of Warcraft 3.3.5a\CB\.dotnet-sdk\sdk\10.0.400\dotnet.dll' build '.\CopilotBuddy.csproj' -c Release --no-restore --no-incremental -p:UseAppHost=false -p:Platform=x86 -v:minimal
```

Result: exit 0, `Build succeeded.`, 3,250 repository-baseline warnings, 0 errors,
and 0 warning lines scoped to Task 2 files. All four tested output directories contain
0 `.exe` apphosts.

The SafePickUp forbidden scan covered legacy path/read/write methods,
`quest_blacklist.txt`, `System.IO`, `TreeRoot.Stop`, `TreeRoot.Start`,
`AbandonQuestById`, and empty catches. Result: 0 hits. Scoped `git diff --check`
passed, the external diff produced no whitespace diagnostic, and the new harness
sources contain no trailing whitespace.

## Boundaries and concerns

- No push, deployment, binary replacement, process launch, or live smoke was
  performed.
- SafePickUp is runtime-compiled outside the repository, so the Git commit contains
  only the harness, report, core ownership contract, and core regression test.
- `NU1900` remains because the offline environment cannot query NuGet vulnerability
  metadata. The full core warning volume is unchanged baseline output; Task 2 adds no
  scoped compiler diagnostic.
- Live world interaction was intentionally not exercised. Candidate switching and
  dynamic nested-branch dispatch are compiled through the real linked adapter, while
  actual NPC/game-object behavior remains for the later authorized live smoke phase.
