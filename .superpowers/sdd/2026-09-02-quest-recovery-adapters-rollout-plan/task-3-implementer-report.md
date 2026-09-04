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
