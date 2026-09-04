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

## Review round 1 fixes (2026-09-04)

All seven review findings were reproduced before the fixes. The linked adapter
harness now constructs, starts, ticks, and disposes real `SafePickUp` instances
through narrow runtime and child seams; production still delegates those seams to
the real quest log, object manager, NPC database, `ForcedQuestPickUp`, POI, and
recovery manager.

The adapter now owns a POI only when a child replaces the prior POI with a pickup
POI whose reference and quest/giver/location metadata exactly match the active
candidate. Denied claims do not clear any POI. Candidate replacement, terminal
cleanup, stale rejection, and neutral disposal clear only that exact object, so a
same-quest winner replacement is preserved. Neutral disposal still abandons only
the exact attempt generation and never abandons the quest.

One outer pickup episode now spans every child replacement. Candidate discovery is
ordered live matching object, primary database location, then strict-parser
alternates; it deduplicates by the scheduler-compatible map plus 80-yard cell key
and caps the episode at five endpoints. Navigation keys persist as
`map + cell:floor(x/80):floor(y/80)`. Real cycle-tagged mismatch outcomes are
deduplicated per child/cycle and counted in one shared boundary; the third total
cycle ends the episode while all three samples remain non-episode observations.
Consumed dialog results reset the silence timer and return from that tick before a
timeout can replace the child.

Completion checks use one coherent `QuestCompletionSnapshot`. Unknown authority,
including `ForcedQuestPickUp.IsExecutionDeferred`, pauses navigation/dialog timers
and suppresses child ticks, interactions, and negative reports until authority
returns. Narrow timeout/endpoint failures are accumulated and submitted with the
terminal pickup-stage failure in one atomic generated batch.

The core generated-batch path now suppresses per-record rolling-budget additions,
applies every accepted local failure, and appends exactly one global rolling
episode for the accepted outer batch. Validation and ownership checks still occur
before any mutation. A new budget regression proves three two-record batches plus
two independent failures consume five episodes, while the next independent failure
exhausts the six-per-hour budget.

### Round 1 TDD and verification evidence

- Core RED: exit 1 at
  `three two-record generated batches plus two failures must consume five, not
  eight, rolling episodes`.
- Linked adapter RED: compilation failed with the expected missing
  `SafePickUpRuntime`, `ISafePickUpChild`, and `SafePickUpGiverCandidate` seams.
- Adapter regression GREEN: exit 0,
  `Quest recovery adapter regression tests passed.`
- Core recovery regression GREEN: exit 0,
  `Quest recovery regression tests passed.`
- Wholesome integration regression GREEN: exit 0,
  `Wholesome scheduler recovery regression tests passed.`
- Pickup policy regression GREEN: exit 0,
  `Quest pickup policy regression tests passed.`
- Full non-incremental Release x86 build: exit 0, 3,250 baseline warnings,
  0 errors.
- All four focused output directories contain zero `.exe` apphosts. Direct adapter
  DLL execution passed; SHA-256:
  `3c5f3d10b9d1bb8318b0da11ef5d820078dde51bfc5525cb51e29148b80b5be6`.
- Static forbidden scan for legacy blacklist/path/read/write/abandon APIs,
  `System.IO`, `TreeRoot.Stop`, and `TreeRoot.Start`: 0 hits. Empty-catch and private
  file-API scans: 0 hits. Scoped repository `git diff --check`: exit 0; external
  no-index check emitted no whitespace diagnostics.
- Original backup manifest was re-read and every entry re-hashed successfully at
  `D:\World of Warcraft 3.3.5a\CB\Backups\quest-recovery-adapters-20260904-132002567`.
  The manifest SHA-256 remains
  `24e67583721d535328ad8e82a7e445b68cfd8e19607cfcbe12a46532bd893c84`.
- Final installed `SafePickUp.cs` SHA-256:
  `67f2e326b8aee41d7f6e60b7d3f2b70123d38190b9dcff85f097e0c1c117fb43`.

No push, deployment, binary replacement, client launch, or live smoke was
performed. The only remaining concern is the intentionally deferred live-world
verification of actual NPC/game-object interaction timing.
