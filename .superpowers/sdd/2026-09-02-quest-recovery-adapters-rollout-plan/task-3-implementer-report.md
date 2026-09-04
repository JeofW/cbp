# Task 3 implementer report

## Scope and behavior

Implemented only the SafeTurnIn adapter slice. The installed runtime source remains
external to Git at `D:/World of Warcraft 3.3.5a/CB/Quest Behaviors/SafeTurnIn.cs`.

The adapter now claims the exact turn-in quest-stage key and generation after one
coherent quest-state snapshot and recovery-context capture. A denied claim finishes
without touching an unrelated POI. Every owned exit reports exact-generation
success, failure, or redirect, or neutrally abandons that exact attempt generation.
The actual accepted-but-incomplete branch uses `CreateIncompleteRedirect` with
`TurnInQuestIncomplete`, `IsFailureEpisode=false`, releases ownership, and performs
no blacklist, escalation, failure-batch, or quest abandonment.

Unknown completion authority and deferred nested execution pause all navigation and
dialog timers and suppress child ticks, interactions, and negative reporting. A
known completed accepted quest resolves enders in live-object, primary-database,
then strict-parser alternate order. Candidates are capped at five and deduplicated
by the stable current-map plus 80-yard endpoint cell key.

One unreachable location remains a navigation endpoint failure and a wholly absent
ender remains a turn-in NPC-relation failure. Natural quest-dialog mismatches advance
to ordered alternatives. Real interaction cycles are counted once across child
replacement; three cycles end one generated batch containing accumulated narrow
facts and one outer turn-in stage failure. The core ownership fence was extended
only for same-quest TurnIn/NPC-relation and Navigation/endpoint children; regressions
also prove pickup relations and non-navigation endpoints remain rejected.

POI cleanup requires the exact object reference plus turn-in type, candidate entry,
and candidate location, so denied claims, stale terminal rejection, and concurrent
same-quest replacements are preserved. `Dispose` releases only an unfinished exact
attempt and disposes the nested real child. `ForcedQuestTurnIn` exposes a monotonic
interaction-cycle ID incremented only immediately before a real `Interact` call.

Automatic abandonment is routed exclusively through `QuestAbandonmentPolicy`. It is
permitted only for a certain, accepted, incomplete, zero-progress automatic
`Quarantined` record with at most two free quest-log slots. Objective counters and
required carried-item counts come from the same live snapshot. Completed, progressed,
manual, uncertain, unpressured, or unpersisted cases are retained. The policy reason
is logged, and the recovery manager must successfully flush state before the client
abandon API is invoked.

All legacy blacklist path/read/write code and unused file I/O are removed. The
adapter does not stop or restart `TreeRoot`.

## TDD evidence

The production-linked harness first referenced
`SafeTurnIn.CreateIncompleteRedirect(876)`. The initial RED build exited 1 with
`CS0117` because the factory did not exist; the implemented real incomplete branch
uses that tested factory. The lifecycle test was then written against missing
`SafeTurnInRuntime`, `ISafeTurnInChild`, ender-candidate, snapshot, and dialog seams;
the expected compile RED preceded their implementation.

The core TurnIn child-authority regression initially exited 1 with
`ArgumentException: The failure target is outside the attempt owner's quest
hierarchy`. The narrow authorization change made it green while new negative cases
retain the surrounding ownership fence.

Two additional lifecycle REDs caught production-boundary defects before acceptance:

- the automatic quarantine test failed because the client abandon happened without
  a preceding recovery-state flush;
- the alternate-advance test failed because a real cycle observed on one pulse was
  mistaken for an already-classified dialog result on the next pulse.

The minimal fixes separated interaction-budget observation from dialog-result
consumption and required a successful manager flush before abandonment. The linked
suite now also proves flush failure withholds the client mutation.

## Fresh verification

All builds used the bundled SDK DLL, `UseAppHost=false`, Release, and x86. Fresh
results:

- linked adapter regression: exit 0, `Quest recovery adapter regression tests passed.`
- core recovery regression: exit 0, `Quest recovery regression tests passed.`
- Wholesome integration regression: exit 0,
  `Wholesome scheduler recovery regression tests passed.`
- pickup policy regression: exit 0, `Quest pickup policy regression tests passed.`
- full `CopilotBuddy.csproj` non-incremental/no-restore build: exit 0,
  `Build succeeded.`, 3,250 repository-baseline warnings, 0 errors.

Direct execution of the linked adapter DLL exited 0. All four focused output
directories contain zero `.exe` apphosts. SHA-256 values:

- installed `SafeTurnIn.cs`:
  `ac4be6bd7ce3bae8d35c189e466a41b66c87d07932bfda03028c84d0f9e90f77`
- linked adapter-test DLL:
  `f3dc6f087b3b67f21630f01ab525a8b308fcc10e0a50b27a53d093d5478e92f2`

The forbidden scan found zero hits for legacy blacklist names, `quest_blacklist.txt`,
`System.IO`, private file/directory/path access, `TreeRoot.Stop`, `TreeRoot.Start`,
and empty catches. The sole `AbandonQuestById` occurrence is the production runtime
seam reached only after policy approval and successful persistence. Scoped repository
`git diff --check` passed; the untracked test has no trailing whitespace; the
external no-index check emitted no whitespace diagnostic.

The original backup was freshly re-read and all three entries re-hashed successfully:

`D:/World of Warcraft 3.3.5a/CB/Backups/quest-recovery-adapters-20260904-132002567`

Manifest SHA-256 remains
`24e67583721d535328ad8e82a7e445b68cfd8e19607cfcbe12a46532bd893c84`;
the original SafeTurnIn entry remains
`547a93da5458bd9c6d12499804997e1d1be673bd220c645e6f293fc5342a80c2`.

## Boundaries and concerns

No push, deployment, installed binary replacement, client launch, process launch,
or live smoke was performed. The only remaining concern is the intentionally
deferred live-world validation of NPC/game-object interaction timing; the real
adapter and nested child compile through the linked harness, while lifecycle,
concurrency, redirect, alternate, authority, persistence, and abandonment behavior
is covered deterministically through the production seams.

## Review round 1 fixes (2026-09-04)

The incomplete redirect is now a generation-fenced terminal transition despite
remaining a non-failure episode. `Report` accepts it only from an exact active
TurnIn quest-stage owner, with an exact current generation and an authorized target.
A valid redirect atomically returns the owned record to `Eligible`, records
`TurnInQuestIncomplete` evidence, releases ownership, persists normally, and adds
neither an episode nor a rolling failure. Stale, delayed, ownerless, or malformed
scope redirects return the current authoritative decision without any record,
evidence, escalation, ownership, or POI mutation.

Quest-wide terminal lookup is now authoritative before exact automatic lookup.
`Completed` wins first, followed by `ManualBlacklist`, then an exact stage/endpoint
record, then legacy control fallback. The manager exposes that same selection via
`GetRecord`, so `Evaluate`, attempt acquisition, reports, and consumers receive a
consistent decision. Removing a canonical manual record reveals the preserved exact
automatic turn-in and endpoint records rather than orphaning or rewriting them.

`ProductionSafeTurnInRuntime.GetRecoveryRecord` now calls the manager's authoritative
getter. A production-representation regression loads an automatic TurnIn quarantine
beside a canonical Pickup manual blacklist, applies two-slot pressure, and proves
the selected manual terminal prevents both persistence and the runtime abandon seam.

### Round 1 TDD and verification evidence

- Redirect RED: exit 1 at `a stale incomplete redirect must not release or mutate
  the exact active owner`; the unfenced redirect changed A to `Eligible`.
- Terminal precedence RED: compilation exited 1 with six expected `CS1061` errors
  because the authoritative `QuestRecoveryManager.GetRecord` API did not exist.
- Production representation RED: exit 1 at `production SafeTurnIn must use
  quest-wide manual precedence`; the prior exact-first adapter lookup selected the
  automatic quarantine.
- Malformed-owner RED: exit 1 because an endpoint-scope key carrying a TurnIn stage
  enum was accepted; the source fence now also requires `QuestStage` scope.
- Linked adapter regression: exit 0,
  `Quest recovery adapter regression tests passed.`
- Core recovery regression: exit 0,
  `Quest recovery regression tests passed.`
- Wholesome integration regression: exit 0,
  `Wholesome scheduler recovery regression tests passed.`
- Pickup policy regression: exit 0,
  `Quest pickup policy regression tests passed.`
- Full non-incremental Release x86 build: exit 0, 3,250 baseline warnings,
  0 errors.
- All four focused output directories contain zero `.exe` apphosts. Static legacy,
  file-I/O, empty-catch, and `TreeRoot` restart scans remain clean. The single client
  abandon call remains behind the policy, authoritative terminal lookup, and
  successful persistence gates.
- Backup verification re-hashed all three original entries successfully. Manifest
  SHA-256 remains
  `24e67583721d535328ad8e82a7e445b68cfd8e19607cfcbe12a46532bd893c84`.
- Final installed `SafeTurnIn.cs` SHA-256:
  `bd904526a222426432e73083ec2c5ca04b70b74a684bf1772556fa4f4640463e`.
- Final linked adapter-test DLL SHA-256:
  `e1060c7af798b9e4780ad5f82d2cfaedc41c6f2fb49737a228b8576913944683`.

No push, deployment, binary replacement, client launch, or live smoke was performed.
Live-world timing remains intentionally deferred.

## Review round 2 fixes (2026-09-04)

The recovery manager now exposes `TryReportOwnedRedirect` with a structured
`Accepted` result. An incomplete turn-in redirect is accepted only when its key is
exactly the attempt key, that key is a TurnIn quest-stage key, and the record is the
current `Attempting` generation. Accepted redirects atomically return that owned
record to `Eligible` and persist evidence without episode or rolling-window
escalation. Stale generations, child relations, endpoints, and malformed scopes
return `Accepted=false` with zero mutation. SafeTurnIn clears its exact POI and
releases local ownership only on acceptance. A rejected delayed owner detaches its
stale POI reference, retains its local generation until neutral disposal, and cannot
clear a newer owner even when the newer owner reuses the identical POI object.

Automatic quest abandonment is now one manager-locked compare-and-act operation.
The manager freshly recaptures accepted/completed/certain/progress/slot state,
re-resolves authoritative `Completed > ManualBlacklist > exact automatic` recovery
state, re-evaluates `QuestAbandonmentPolicy`, successfully persists dirty recovery
JSON, and invokes the supplied minimal client action before releasing the lock.
Manual/completed terminal winners, newly completed or progressed live state, and
persistence failure all suppress the action. SafeTurnIn no longer has an exact-first
record lookup, separate flush, or separate abandon seam.

Manual exclusion now uses a distinct persisted `QuestTerminal` key. Setting a
manual terminal therefore leaves an existing canonical Pickup automatic record
untouched, including its state, reason, timestamps, counters, objective snapshot,
fingerprints, generation, and evidence. Both records survive reload and compaction;
removing the terminal reveals the original automatic record exactly. Legacy files
whose canonical Pickup record itself is `ManualBlacklist` remain authoritative and
are migrated to the distinct terminal key when the exclusion is reapplied.

### Round 2 TDD evidence

- Redirect API RED: the core test build exited 1 with the expected `CS1061` errors
  because `TryReportOwnedRedirect` did not exist.
- Adapter seam RED: the production-linked test build failed before the structured
  redirect seam existed. After the first implementation, the reused-object race
  failed because the stale adapter cleared the identical replacement POI; the
  tick-plus-dispose regression also failed until local owner A was retained for an
  exact-generation neutral release.
- Atomic abandonment RED: the core test build exited 1 with the expected missing
  `QuestAbandonmentLiveSnapshot` and `TryExecuteAutomaticAbandonment` errors. The
  linked adapter then failed to compile until its former lookup/flush/action sequence
  was replaced by that manager API.
- Manual preservation RED: the core build exited 1 with `CS0117` because the desired
  `QuestRecoveryKey.ForManualTerminal` representation did not exist. The first green
  run then exposed and corrected older exact-key expectations while preserving their
  ownership-generation guarantees.

Fresh final verification used the bundled SDK DLL, Release, x86,
`UseAppHost=false`, `--no-restore`, and `--no-incremental`:

- linked adapter regression: exit 0, `Quest recovery adapter regression tests passed.`
- core recovery regression: exit 0, `Quest recovery regression tests passed.`
- Wholesome integration regression: exit 0,
  `Wholesome scheduler recovery regression tests passed.`
- pickup policy regression: exit 0, `Quest pickup policy regression tests passed.`
- full `CopilotBuddy.csproj`: exit 0, 3,250 repository-baseline warnings, 0 errors.

All four focused output trees contain zero `.exe` apphosts. The external forbidden
scan found zero legacy blacklist/file-I/O/`TreeRoot` restart/empty-catch hits. Its
sole `AbandonQuestById` occurrence is the minimal action supplied to the locked
manager API. Scoped repository `git diff --check` exited 0; the external no-index
check emitted no whitespace diagnostics.

Final SHA-256 values:

- installed `SafeTurnIn.cs`:
  `9801536870a3ace086d4c867dfc25d001cebc2a84bda66d80b49fd3e97c643f3`
- linked adapter-test DLL:
  `bb9541c26abd9f1628bc5d779ae0b3bf80acd0e3cf69d81b17e497340015cb49`

The backup recheck verified all three manifest entries. Manifest SHA-256 remains
`24e67583721d535328ad8e82a7e445b68cfd8e19607cfcbe12a46532bd893c84`;
the original SafeTurnIn entry remains
`547a93da5458bd9c6d12499804997e1d1be673bd220c645e6f293fc5342a80c2`.

No push, deployment, installed binary replacement, client/process launch, or live
smoke was performed. The remaining concern is intentionally deferred live-world
validation of NPC/game-object interaction timing.

## Review round 3 fixes (2026-09-04)

All SafeTurnIn terminal cleanup is now acceptance-fenced. The manager exposes
`TryReportOwnedOutcome`, which accepts success, exact stage failure, or the existing
incomplete redirect only for the exact key in the current `Attempting` generation.
SafeTurnIn uses the structured result for stage failures and successes. Generated
failure batches continue to use their existing atomic boolean result. A rejected
batch, stage failure, success, or redirect detaches the stale adapter's local POI
reference, marks the behavior done, and retains the old local generation only until
neutral disposal; it cannot clear or mutate a newer owner even when that owner
reuses the identical `BotPoi` instance. Accepted reports alone release local
ownership and clear the exact installed POI.

The two manual-removal contracts are now deliberately distinct. Direct
`TrySetManualBlacklist(questId, false)` removes only the distinct manual overlay and
reveals the preserved automatic Pickup record. Wholesome's one-click combined
`TryClearExclusion(selectedAutomaticKey)` atomically removes the same-quest overlay
and that selected automatic exclusion while retaining other stages, other quests,
and Completed records. The restored production SettingsForm regression was built
and executed directly.

Destructive abandonment action exceptions are contained at both boundaries. After
successful persistence, the manager catches an exception from the supplied client
action, emits its diagnostic through the configured logger, and returns a structured
`MayAbandon=false` result containing the failure. `ReportExhausted` also uses
structured `try/catch/finally` cleanup so an unexpected runtime-seam exception is
logged, the accepted terminal lifecycle is finalized once, and later ticks cannot
repeat the batch or destructive attempt.

### Round 3 TDD evidence

- Owned-terminal API RED: core compilation exited 1 with four expected `CS1061`
  errors because `TryReportOwnedOutcome` did not exist; the linked adapter build
  independently exited 1 with `CS0115` because its desired runtime override had no
  production seam.
- Combined Clear RED: the core executable exited 1 at `one-click Clear must remove
  both overlay and selected automatic only`; the overlay was removed but the chosen
  automatic record remained.
- Destructive-action RED: the core executable exited 1 when
  `InvalidOperationException: abandon executor boom` escaped
  `TryExecuteAutomaticAbandonment`. With the production ReportExhausted cleanup
  temporarily at its pre-fix form, the linked executable separately exited 1 with
  the same exception escaping `SafeTurnIn.TickForTesting`.

Fresh final verification used the bundled SDK DLL, Release, x86,
`UseAppHost=false`, `--no-restore`, and `--no-incremental`:

- linked adapter regression: exit 0, `Quest recovery adapter regression tests passed.`
- core recovery regression: exit 0, `Quest recovery regression tests passed.`
- Wholesome integration regression direct DLL: exit 0,
  `Wholesome scheduler recovery regression tests passed.`
- pickup policy regression direct DLL: exit 0,
  `Quest pickup policy regression tests passed.`
- full `CopilotBuddy.csproj`: exit 0, 3,250 repository-baseline warnings, 0 errors.

All four focused output trees contain zero `.exe` apphosts. The external forbidden
scan found zero legacy blacklist/file-I/O/`TreeRoot` restart/empty-catch hits; its
sole `AbandonQuestById` occurrence remains the minimal manager-supplied action.
Final SHA-256 values:

- installed `SafeTurnIn.cs`:
  `15ec373ee5519b96efda6316c3ca6481b38dad91a1d8599c31f75a1ae9fd3fb8`
- linked adapter-test DLL:
  `734c3c3e64f6dfa0a20949f17cdcb4f39d7c0290f83284fc60a1ee0dfc111889`

The backup recheck verified all three manifest entries. Manifest SHA-256 remains
`24e67583721d535328ad8e82a7e445b68cfd8e19607cfcbe12a46532bd893c84`;
the original SafeTurnIn entry remains
`547a93da5458bd9c6d12499804997e1d1be673bd220c645e6f293fc5342a80c2`.

No push, deployment, installed binary replacement, client/process launch, or live
smoke was performed. The remaining concern is intentionally deferred live-world
validation of NPC/game-object interaction timing.

## Review round 4 fixes (2026-09-04)

`TryReportOwnedRedirect` now has its own closed input boundary. It delegates to the
general owned-terminal path only for an exact `Redirect` carrying
`TurnInQuestIncomplete` with `IsFailureEpisode=false`; success, observation,
failure-flagged redirect, every failure outcome/reason, and every other redirect
reason return `Accepted=false`. The existing shared ownership checks continue to
require exact `Key == AttemptKey`, a current positive generation, and a TurnIn
quest-stage owner. `TryReportOwnedOutcome` remains the general success/failure API,
and SafeTurnIn behavior and its external source are unchanged.

The focused core misuse matrix uses a fresh real manager per case. It verifies that
each rejected misuse preserves the `Attempting` state, exact generation, ownership,
episode count, and evidence. After first persisting the owned fixture, it replaces
the settings root with a file; a clean rejection still makes `TryFlush` succeed by
short-circuiting, while any hidden dirty-state mutation would attempt persistence
and fail. This directly covers the zero-dirty-change requirement in addition to the
observable record checks.

### Round 4 TDD and verification evidence

- RED: the core regression reported every exact-current `Success` and all 19
  `Failure` reason variants as incorrectly accepted through
  `TryReportOwnedRedirect`, including `None` and `TurnInQuestIncomplete`.
- GREEN: the same core regression passes after the single redirect-shape boundary
  was added. The existing owned-terminal regression also passes, proving success
  and failure remain accepted through `TryReportOwnedOutcome`.
- Fresh non-incremental Release x86 focused builds, with `UseAppHost=false` and
  `--no-restore`: adapter 3,246 baseline warnings/0 errors; core 3,246/0;
  Wholesome 3,261/0; pickup 3,246/0.
- Direct DLL execution: adapter, core, Wholesome, and pickup each exited 0 with
  their expected regression-suite pass message.
- Full `CopilotBuddy.csproj` non-incremental Release x86 build: exit 0,
  3,250 repository-baseline warnings, 0 errors.
- All four focused output trees contain zero `.exe` apphosts. The external
  SafeTurnIn forbidden scan found zero legacy blacklist/file-I/O/`TreeRoot`
  restart/empty-catch hits; its sole `AbandonQuestById` remains the manager-supplied
  action.
- External `SafeTurnIn.cs` SHA-256 remains unchanged at
  `15ec373ee5519b96efda6316c3ca6481b38dad91a1d8599c31f75a1ae9fd3fb8`.
- Linked adapter-test DLL SHA-256 remains
  `734c3c3e64f6dfa0a20949f17cdcb4f39d7c0290f83284fc60a1ee0dfc111889`;
  core regression DLL SHA-256 is
  `02c766e8fb285575df97614ca0a12765f5ef3d4d74a11e04622f9f7c96ab9785`.
- The backup recheck matched all three manifest entries. Manifest SHA-256 remains
  `24e67583721d535328ad8e82a7e445b68cfd8e19607cfcbe12a46532bd893c84`;
  the original SafeTurnIn entry remains
  `547a93da5458bd9c6d12499804997e1d1be673bd220c645e6f293fc5342a80c2`.

No external source change, push, deployment, binary replacement, app/client launch,
or live smoke was performed. Live-world timing remains intentionally deferred.
