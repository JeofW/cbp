# Task 4 implementer report: Zygor recovery adapter

## Outcome

The installed `Plugins/ZygorProfileRecovery/ZygorProfileRecovery.cs` now uses the
shared quest recovery manager for exact objective ownership, progress, repeated
death/no-progress outcomes, and policy-locked abandonment. The old private
blacklist writer, restart timer, and global `TreeRoot.Stop`/`TreeRoot.Start`
recovery path were removed.

The real installed adapter is linked into
`Tools/QuestRecoveryAdapterRegressionTests`. The resulting tests exercise the
production plugin lifecycle and synchronization boundary through an injectable
runtime seam rather than source-text assertions.

## Implemented behavior

- `OnEnable` calls `QuestRecoveryRuntime.EnsureConfigured()` and installs one
  cached death delegate. Repeated/concurrent enable and disable calls configure,
  subscribe, unsubscribe, and release ownership exactly once per lifecycle.
- A concrete `ForcedQuestObjective` is mapped to the exact
  `(quest id, objective index)` recovery key and claimed with the manager-issued
  attempt generation. A denied claim remains passive for that exact behavior
  activation; it neither retries each pulse nor advances/clears another owner's
  work or POI.
- Each sample contains the descriptor objective counters followed by current
  counts for every required/intermediate quest item. Any increase is reported
  with the same immutable vector through `ReportProgress` and resets local death,
  active-time, and cluster episode state.
- Pre-death attribution requires the same exact behavior/key and manager
  generation, a fresh in/approaching-area sample, and no loading, corpse, taxi,
  transport, resting, pause, vendor/repair/mail/trainer, unrelated POI, or
  unrelated-combat state. Area proximity without exact owned execution is not
  sufficient.
- `ZygorProfileRecovery.ClassifyDeath(...)` is the production classifier used by
  the event handler. The first attributable death leaves normal recovery in
  control, the second queues one alternate-cluster request, and the third inside
  15 minutes without progress reports one exact-generation `RepeatedDeaths`
  failure episode. No permanent/private quest death dictionary remains.
- No-progress reporting requires eight minutes of bounded active samples across
  at least two cluster identities. Inactive gaps do not advance the clock.
- Accepted failure results alone queue exact continuation for the next normal
  plugin pulse. Cleanup revalidates the same behavior, quest, objective, and POI
  object identity; stale/rejected results cannot clear a newer or reused POI.
  Continuation disposes and advances only the proven failed current behavior.
- Quarantine abandonment is requested only through
  `QuestRecoveryManager.TryExecuteAutomaticAbandonment`, with the live quest
  snapshot recaptured inside the manager lock. The shared policy therefore keeps
  the quest unless state is certain, objective progress is zero, the automatic
  quarantine is authoritative, and free quest-log slots are at most two.

## TDD evidence

The implementation was developed against the real linked adapter:

- Initial RED: the linked adapter project failed with six compiler errors because
  `ZygorObjectiveExecution`, `ZygorRecoveryRuntime`, and the new production
  classifier/lifecycle surface did not exist.
- Attribution RED: the next focused build failed with eight missing-member errors
  for the requested production POI attribution classifier.
- Ownership RED: after selecting the correct x86 output artifact, direct execution
  exited 1 at `a denied exact claim must become passive for that activation and
  yield to its existing owner`. The implementation previously queued continuation
  for a denied claim; the narrow fix records the activation as passive instead.
- GREEN: the linked adapter executable now covers death-classification boundaries,
  every pre-death exclusion, unrelated POIs/combat, exact claim identity,
  concurrent lifecycle calls, coherent progress vectors, episode reset,
  15-minute death expiry, alternate-cluster/no-progress behavior, exact
  accepted/stale/rejected cleanup, and both allowed and withheld policy-locked
  abandonment.

The first direct-run attempt exposed a verification-path issue rather than a code
result: x86 builds are emitted under `bin/x86` and require the bundled x86 host.
Fresh evidence below uses only that artifact and runtime.

## Fresh verification

All final builds used
`../.dotnet-x86/dotnet.exe`, Release, x86, `UseAppHost=false`, `--no-restore`,
and `--no-incremental`. The focused output directories were cleaned first so the
no-apphost assertion could not pass on stale state.

- Linked adapter build: exit 0, 3,246 repository-baseline warnings, 0 errors.
- Core recovery build: exit 0, 3,246 repository-baseline warnings, 0 errors.
- Wholesome integration build: exit 0, 3,261 repository-baseline warnings,
  0 errors.
- Pickup policy build: exit 0, 3,246 repository-baseline warnings, 0 errors.
- Direct linked adapter DLL: exit 0,
  `Quest recovery adapter regression tests passed.`
- Direct core recovery DLL: exit 0,
  `Quest recovery regression tests passed.`
- Direct Wholesome DLL: exit 0,
  `Wholesome scheduler recovery regression tests passed.`
- Direct pickup policy DLL: exit 0,
  `Quest pickup policy regression tests passed.`
- Full `CopilotBuddy.csproj` x86 build: exit 0, 3,250
  repository-baseline warnings, 0 errors.
- All four focused output trees contain zero `.exe` apphosts.

Final SHA-256 values:

- Installed `ZygorProfileRecovery.cs`:
  `62abf3957b1448e01bb52113c08063e515c7ab030d47db5991e5bb41d0bc92c2`
- Linked adapter regression DLL:
  `f44aa8cc5efbaf5e7d226302d7f623a88021a451b0b1673ee292e26238deac5b`
- Core recovery regression DLL:
  `6dccbabe9d00a58bc7d1a72dffbed4756bd40402651a861913f96f28db59c45f`
- Wholesome regression DLL:
  `360f6e09d2fc3b8f367662a18bd5ae3f32e23574aec1ed3ade54ec00b049750f`
- Pickup policy regression DLL:
  `71718fe232993c5b34851ec3b03b17647ef390a965b834506ef13847cf59fa08`

The installed adapter scan found zero legacy blacklist names, file/directory APIs,
restart timers, `TreeRoot.Stop`, `TreeRoot.Start`, or empty catches. The sole
`AbandonQuestById` occurrence is the action delegate supplied to the manager's
locked compare-and-act API. The apparent `Path.` text hit is only the local
`path.IndexOf(...)` profile-name check, not `System.IO.Path`.

The original backup at
`Backups/quest-recovery-adapters-20260904-132002567` was re-read and all three
manifest entries matched. The manifest SHA-256 remains
`24e67583721d535328ad8e82a7e445b68cfd8e19607cfcbe12a46532bd893c84`;
the original Zygor source entry remains
`a1bfce47208f4b357b51d01714680ccf1762759a3db6677fbde446af1459dc36`.

Scoped repository whitespace checks passed, the new linked test has no trailing
whitespace, and the external no-index check emitted no whitespace diagnostics.

## Boundaries and remaining concern

No push, deployment, installed binary replacement, game/client launch, or live
smoke was performed. The remaining concern is intentionally deferred live-world
validation of Zygor objective-area and hotspot queue behavior; the production
adapter compiles through the real linked harness, while lifecycle, concurrency,
ownership generation, progress, death, stale cleanup, and abandonment boundaries
are covered deterministically.

## Review round 1

The review findings were reproduced and closed with additional linked and core
regressions.

- `GrindArea.TryAdvanceCurrentHotspot` now changes the real private active
  hotspot under one lock, updates `LastHotSpot`, and advances the circular queue
  without dropping entries. The Zygor runtime calls that production method from
  its coalesced normal-pulse alternate request and derives the cluster identity
  from `CurrentHotSpot`, not the nearest point.
- The real-`GrindArea` linked regression uses three spaced hotspots and three
  targets. It proves the first attributable death leaves the queue alone, the
  second advances A to B exactly once with the queue rotated to C/A/B, and eight
  active minutes observe two cluster identities without another skip or wrap.
- The shared abandonment snapshot/context now carries a tri-state prerequisite
  result. Missing relation authority (`Unknown`) and a guide/accepted-chain match
  (`Active`) deny with distinct reasons; only authoritative `NotActive` reaches
  the remaining pressure/quarantine guards. Both installed adapters default
  missing data to `Unknown`. Zygor and SafeTurnIn use the shared active
  profile/quest-relation authority.
- Recovery progress is an element-wise persisted maximum. Failure contexts also
  merge their coherent objective/item vector into the authoritative record, and
  the manager checks all exact records for the same quest while holding its
  abandonment lock. Consumed required/intermediate items therefore cannot make
  prior progress disappear across reload.

### Round 1 TDD evidence

- Prerequisite RED: the core and linked adapter builds failed because the
  tri-state contract and adapter capture methods did not exist.
- Hotspot RED: the linked test first failed because the alternate request did
  not mutate the private current hotspot. The initial public-constructor test
  setup also exposed the expected offline `StyxWoW` dependency; the corrected
  harness creates a detached real `GrindArea` and invokes its production method.
- Historical-progress RED: direct core execution exited 1 at `failure snapshots
  must retain per-counter historical maxima when live required-item counters
  return to zero`. After merging failure-context counters, the same direct DLL
  passed.

### Round 1 fresh verification

All builds used `../.dotnet-x86/dotnet.exe`, Release, x86,
`UseAppHost=false`, `--no-restore`, and `--no-incremental`, after cleaning each
focused output tree.

- Linked adapter: exit 0, 3,246 baseline warnings, 0 errors; direct DLL passed.
- Core recovery: exit 0, 3,246 baseline warnings, 0 errors; direct DLL passed.
- Wholesome integration: exit 0, 3,261 baseline warnings, 0 errors; direct DLL
  passed.
- Pickup policy: exit 0, 3,246 baseline warnings, 0 errors; direct DLL passed.
- Full `CopilotBuddy.csproj`: exit 0, 3,250 baseline warnings, 0 errors.
- All four focused output trees contain zero `.exe` apphosts.
- Installed Zygor forbidden legacy writer/restart scan: zero hits. The only
  Zygor `AbandonQuestById` remains the action delegate supplied to the manager's
  locked compare-and-act API; SafeTurnIn follows the same boundary.
- Scoped repository and external no-index whitespace checks emitted no
  diagnostics.
- The original backup manifest still matches all three entries. Manifest hash:
  `24e67583721d535328ad8e82a7e445b68cfd8e19607cfcbe12a46532bd893c84`.

Round 1 SHA-256 values:

- Installed `ZygorProfileRecovery.cs`:
  `4bbc54c97c541c481fdfe08b3a90eaa8c082d3b73def121cceb32e5f82467704`
- Installed `SafeTurnIn.cs`:
  `4b1995c6f495545d6180bb4addcedb5b72dc7d341a9e8894f2257d5b26a40a71`
- Linked adapter regression DLL:
  `5b9b9baed17ebc903cb0482b7b957d9a5dea7cba43d820bf1a307aa8053c2a7d`
- Core recovery regression DLL:
  `abea6a0ab96850f42960480290905a53bb12b2dcb4ac1bab73d22b7af5bec504`
- Wholesome regression DLL:
  `f58498ce8ce82d9ba0706ad4754e900c59a11bf0f485e34083374c394599fec0`
- Pickup policy regression DLL:
  `282fa9c09b101826ade283e4c783677ffb7fc5d6505062ecce536ef0ce27e5ae`

No push, deployment, installed binary replacement, game/client launch, or live
smoke was performed. The remaining concern is limited to deferred live-world
validation of the active guide relation and GrindArea queue against the actual
game client; both paths are covered through the real linked sources and
production methods in deterministic offline regressions.
