# CopilotBuddy audit — resume here

Verified checkpoint: 13 September 2026. Repository: JeofW/CopilotBuddy-private.

**Working branch:** `audit/next-30-resume-checkpoint-20260913`.
**Tested production/test commit:** `6fd314cd5bb6012c6da52cbd0ac963d0db894f15`.
**Tested tree:** `46ea3862597933a7d33dfa243dc1e9d1efe74c13`.
Subsequent checkpoint commits change documentation only. This is a review/integration checkpoint, NOT a released or live-certified bot.

## Continuation protocol

Do not restart the broad audit because a chat response failed. First read this file on the named branch, inspect current remote heads, then read the relevant saved plan and failing/passing evidence for the next slice. The root CODEX_AUDIT_PROMPT.md, KNOWN_ISSUES_AND_HYPOTHESES.md and AUDIT_CONTEXT.md still govern scope; docs/audit/2026-09-12/AUDIT.md and GRAPH.md retain the original evidence. Historical checkpoint documents describe their own pinned revisions and may have superseded pending items.

A failed chat response does not prove that its preceding GitHub writes were lost or that a workflow failed. Commits, PRs and Actions artifacts are the recovery authority. Unsaved analysis is not recoverable evidence. The cause of the displayed chat error is unknown; do not diagnose it as a repository failure.

Use focused branches, tests that fail for the intended defect before repair, and recorded source/artifact identities. Do not merge master, force-push, replace installed files or silently promote the native candidate. Keep independent review and live acceptance explicit. If artifacts expire, rerun the corresponding workflow at the recorded commit rather than inventing prior outcomes. Avoid rerunning the full dataset download for ordinary managed tests.

## Exact combined state

This tree contains the previous combined managed repairs at `c8c7fb59c23d627a6f55ba611e48000619d66a0e`, plus both independently reviewed source slices:

- Lift observation continuity: `49ea729bad4f48492f3ae0162f6df54c0c8e270f`, PR #26, branch `audit/next-28-lift-observation-continuity-20260913`.
- Shared Exorcism opportunity/movement policy: `cdf47cfb0e437886df03c46d6286998b7753307f`, PR #27, branch `audit/next-27-exorcism-policy-20260913`.

The integration commit records both contributing parents. Its remote tree exactly matches the locally assembled tree; the independent slices apply without content conflicts. The underlying tree already contains the quest identity, endpoint assessment, navigation deferral, vendor backoff, rest ownership, shared casting, worker cancellation, shutdown, lift corridor and onward-route repairs from the preceding staged work. Their existing PRs remain available for focused review. This is self-reviewed code; independent reviewer approval is still pending.

**Excluded:** the optional indexed native queue experiment in draft PR #25 / `audit/next-29-native-queue-20260913`. Its corrected operation count does not establish a useful speedup on the expensive Grod routes. Do not incorporate it as a default optimization.

## Actually executed verification

Combined run **34747083607**, artifact **10314113855**, SHA-256 **226228171e0fa72af56666dafe7806d41857029bcbe22f00b8eec475eb1df408**. The complete ZIP was downloaded, its hash and embedded commit verified, and all suite logs inspected. All **eleven** required entries have build/run exit 0 and explicitly report no game attached.

Entries: QuestRecoveryRegressionTests; WholesomeQuestRecoveryRegressionTests; QuestRecoveryAdapterRegressionTests; QuestPickupPolicyRegressionTests; VendorCoreRegressionTests; NativeContractRegressionTests; ShutdownBoundaryRegressionTests; explicit Singular compatibility; portable Elevator; portable PaladinDecisions; Python evidence analyzers.

Selected coverage in that SAME combined execution:

| Contract | Verified coverage |
|---|---|
| Elevator controller | 61 scenarios: 5 existing, 6 supplied-permission, 14 corridor/finite-input, 28 continuity, 8 early-veto/reattachment; 500 seeded permission checks |
| Exorcism decisions | 39 scenarios, 192 level/observation combinations across Normal/Instance/Battleground |
| Existing Paladin decisions | 42 scenarios, 135 availability rows |
| Quest content identity and endpoint evidence | 11 identity and 13 assessment scenarios |
| Quest navigation deferral and vendor travel | 15 deferral and 25 vendor-travel scenarios |
| Rest ownership | 9 checks: four compiled call-site guards and five policy controls |
| Terminal route and timed movement | 18 route-ownership and 11 timer scenarios; 2,000 seeded timer operations |
| Cast dispatch and routine boundaries | 16 dispatch and 13 predicate/delay scenarios |
| Worker and shutdown ownership | 22 worker and 9 shutdown scenarios using real managed threads/owners |
| Native wrapper contract | 6 cases against actual x86 DLL with deliberately missing map; NOT a compatible-mesh/live test |
| Evidence tooling and routine compilation | 33 analyzer/export tests; 98 tracked Singular source files compile |

Counts overlap across executables; do not add repeated executions into a claimed number of unique gameplay tests. Some fixtures deliberately control external world/dispatch observations or inspect compiled call sites. Passing these entries is not whole-codebase branch coverage, all-quest success, all-spec approval, measured DPS or live acceptance.

## Repairs completed at this checkpoint

**Lift continuity:** unsafe exit observations before the initial dwell completes now reset it; every detached exit observation discards platform dwell before a later reattachment. Earlier saved continuity work also revokes permission on falling, attachment loss/change, missing transform, motion within the dock radius, backward time or an observation gap over two seconds. Grounded detached completion remains possible. The old fixture's immediate-exit expectation after unsafe time was replaced with explicit fresh 750 ms safe-dwell boundary assertions; unsafe-corridor checks remain. Conservative gap handling can delay transit under slow pulses and requires live review.

The saved intermediate run **34745518034**, artifact **10314370870**, SHA-256 **4545406a3b57c72b647818fcdaf5f6374e4ca5a7a15dfe5b1f758f95d04d6224**, failed all eight additional early-veto/reattachment cases. Final slice run **34746823439**, artifact **10314148434**, SHA-256 **5576ffcf80c31b922870f20b9e2f37d75d98859018d93616532cf6166f08682d**, and the combined run pass them. Complete artifacts were inspected.

**Exorcism policy:** the saved shared builder protects active melee opportunities and movement from unprocced hard-cast attempts while preserving stationary ranged openers and observed instant procs. An auto-attack flag alone at range is not a veto. The conservative policy is not a DPS benchmark; target life, mana/gear, swing reset, passive talent discovery and server outcomes remain live measurements. Test-first run **34743243847**, artifact **10313532452**, SHA-256 **354de633bc3efc46b51f071772ae5ef9579fad0fe9b1c2ddb71196b874fc3306**, failed 19/39 new scenarios; repair run **34744322637**, artifact **10313569029**, SHA-256 **0d7c422372db19806ab04c8fa9cb36f7e0eb9e50f37b2d5c65b6566b4d856fd7**, passed all 39 and all eleven entries. Both archives were inspected during recovery.

## Meshes were already used, not just read as LFS pointers

The complete installed dataset was previously downloaded and verified: 6,054 files / 2,985,832,908 bytes. Later controlled native jobs fetched the needed 989 Kalimdor objects with hashes rather than all maps. The checked-in DLL rejects the inspected format-6 tile headers; a pinned source-built candidate can query them. Lib/Navigation.dll and installed files are still unchanged.

Real replays retain partial/resource-limited results for the captured forward Grod requests. The destination-side polygon and nearby controls exist, but that does not prove a complete traversable route. Out-of-nodes is not an unreachable verdict. Reproducing two-point partial output is not completing the quest/vendor trip or boarding a real moving platform.

Native queue experiment: run **34745419542** passes functional/complexity/sanitizer controls with the documented sanitizer complexity skip; run **34745419648**, artifact **10313921620**, SHA-256 **ccf3e675b325f812d915e640a2185f41e0e7c655f4d0895488a0c91e558a678a**, preserves 80 semantic route records. Expensive forward warm medians are **488.403 -> 529.881 ms** and **505.786 -> 510.794 ms**. Node size grows 32 -> 40 bytes in the portable fixture and clear visits the frontier. Keep the candidate experimental. See PR #25 and NATIVE_QUEUE_RESULTS.md on its own branch. No measured route-performance win is claimed.

## Remaining work, ordered by dependency rather than restarting

1. **Native compatibility and search diagnostics:** create an explicit, reviewable source-built loader selection/package with provenance and startup compatibility checks; do not silently replace binaries. Profile expansions, search budget, queue occupancy/update frequency and node/polygon lookup costs. Do not increase budgets or call partial results complete merely to obtain green results.
2. **Complete route acquisition/cost:** distinguish resource exhaustion, missing evidence, transport waits and unreachable conclusions. Compare validated approach/wait/ride/exit/onward alternatives. Actual landing/support geometry, platform transforms and timing must be observed; no fabricated coordinates or nearest-lift-only optimality claim.
3. **Cross-boundary command ownership:** central managed cancellation is repaired, but commands already inside native injection, arbitrary extension catches, delayed native commands and all route/attempt generations are not fully owned. Add reproductions at each boundary before changing it.
4. **Quest/data and combat breadth:** reconcile remaining zone/global data differences without automatically deleting prerequisites/quarantining legitimate scripted or item-started quests; expand special-objective and transition coverage. Finish full multi-tick target identity, mount/buff arbitration, remaining class/spec policies and measured action/latency telemetry.
5. **Acceptance:** review all affected diffs, run final combined tests after each source addition, verify actual client pickup/objective/turn-in/vendor trips and both-direction lift phases with stop/pause/death/loading interruptions. Mesh files alone do not provide live attachment, collision or server confirmation.

## Resume instruction

Open `JeofW/CopilotBuddy-private`, read `AUDIT_RESUME.md` on `audit/next-30-resume-checkpoint-20260913`, verify its saved commit/PR/Actions evidence, and continue the first unfinished dependency. Preserve all completed work and failing/passing history. Do not restart the broad audit or redownload all meshes for ordinary managed tests. No automatic merge/deployment.
