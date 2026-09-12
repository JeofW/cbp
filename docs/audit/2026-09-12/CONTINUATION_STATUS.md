# Audit continuation status — 2026-09-12

This is a verified partial repair set, not completion of the whole audit or certification of every quest, class/spec, or lift route. The immutable baseline is `AUDIT.md` / `GRAPH.md` at `ec5809cae12429a721e3df14a71e0f96bd015ef7` in PR #2. It records processing of all 62 logs, syntax extraction over all 1,541 C# files and static checks over all 4,335 global quests. Type-bound call resolution, full all-spec decision coverage and live acceptance remain open. This continuation reproduced selected source defects before repairing them.

## Staged PR dependency map

| PR | Branch / tested production commit | Verified scope | Base |
|---|---|---|---|
| #2 | audit/01-evidence-baseline-20260912 | Existing evidence/graph baseline; incomplete gates explicitly retained | master |
| #3 | audit/02-build-isolation-20260912 / d8698c7926c0cc8182ec16d3a7f4d5d2af984af9 | Existing host snapshot isolation and Windows build repair | #2 |
| #4 | audit/03-lift-boarding-safety-20260912 / 83b63b6fe5a4308e5b42b6e2be90856b9b2abbf3 | Six new plus five existing controller cases; supplied corridor-safety revocation | #3 |
| #5 | audit/04-quest-validation-20260912 / 498257b58cb93470ce2c940bbdaab98b908a20d7 | Five quest/vendor suites plus explicit compilation of 98 tracked Singular files | #3 |
| #6 | audit/05-quest-navigation-evidence-20260912 / b09ad350a7f283f06b75dd340b6091338c5e2927 | Thirteen new per-point evidence cases plus all six validation entries | #5 |
| #7 | audit/06-spell-lifecycle-20260912 / 2cfe1e5a1103e3bce2550fc9ffe00b4152670150 | Ten lifecycle cases, quest core and explicit Singular checks | #5 |

PR #4 is a sibling of #5. PRs #6 and #7 are siblings based on #5. **The last branch does not contain all fixes.** No combined deployment branch or merged-stack live run was created. Integrate only after review, respecting dependencies, and rerun all affected suites on the final combined tree. No PR was merged and master was not modified by this work.

## Additional lift caller-boundary finding — N05

At PR #4 head, `MeshNavigator.cs` blob `8dd6ceb1fa3fb2a0d80a1596c548c47a8a557101` lines 1907–1909 computes `boardingPathSafe` using the corridor to **WaitPoint**. The controller may then emit MoveToBoard toward the **live platform**, and lines 1943–1946 command that target without a separate segment check. Lines 471–490 reduce the wait-point check to current ground support when already within 0.75 yards of the waiting point.

**Confirmed source contract mismatch:** the checked and commanded destinations can differ. **Unverified historical impact:** no native collision/platform replay proves that this caused a captured fall or the Grod incident. Earlier landing validation and dock confirmation are counterevidence, but do not prove the current remaining segment. This finding is recorded in PR #4 comment 5646278888. It is not fixed by PR #4's input-revocation guard.

The follow-on must separate approach-to-wait permission from boarding-to-platform permission, validate the actual commanded segment immediately before movement, and revoke boarding/dock evidence on failure. Do not block walking to a safe wait point merely because the lift is away. Required cases include a safe wait point with an unsafe boarding gap, moving/departing platform, support loss/recovery, fresh dwell and both travel directions. Native platform-support semantics require live validation before claiming end-to-end safety.

## What remains for smart, efficient lift routing

The captured Mesa/Grod failure remains open. The logged 49.78-yard distance is 3D; the preference gate is horizontal. A radius-only change is not justified. Discovery still depends on landing samples from an existing mesh route and does not compare complete approach/wait/ride/exit/onward costs. A controller test cannot prove lift acquisition or globally optimal routing.

The target design remains: stable quest/vendor intent -> typed route outcome -> validated walk/transition graph -> one cancellable movement owner. Compare complete validated alternatives, not nearest lift distance. Distinguish missing geometry, an unreachable destination, a temporarily unavailable transport and physical obstruction. Unknown endpoints remain unknown; fabricated coordinates and partial-path success claims are prohibited.

To close the live gate, use a reviewed build with known source identity and the 3.3.5a client plus its actual navigation tiles. Capture the Grod destination (-1152.76, 71.41, 145.87), the observed origin(s), entry 4171's reported and animated transforms, candidate rejection reasons, XY/XYZ distances, both landings, selected GUID/attachment, ground and corridor probes, tile/poly IDs, route revisions and full query/tick timings. Observe approach, docking, boarding, riding, supported exit and resumption in both directions. Exercise pause/stop in each phase under supervision. Include a validated ramp or alternative lift route before asserting the chosen route is cheapest. Existing logs lack some of these fields; recording candidate rejection and route identity requires additional instrumentation, not just a repeat of the old capture.

## Other open work

Q01 content-based dataset identity; persistent recovery scopes coarser than actual floors; native cancellation and stale command ownership; mount/buff arbitration; shared Singular target validation/reselection; Retribution priority/opportunity-cost decisions; all-class/spec behavior matrices; uncensored plugin/path/tick measurements; and final combined-tree/live acceptance. None is marked fixed by a build or by a related regression. Missing static quest references are investigation candidates, not an instruction to quarantine all affected quests.

## Traceability and reproduction

`CONTINUATION_GRAPH.json` gives directed symptom/evidence/owner/hypothesis/regression/fix/PR chains for the reproduced N03, Q03 and L01 changes and an explicitly unresolved N05 chain. It is a human-reviewed overlay, not a replacement for the whole-codebase syntax graph or a claim of semantic binding. Follow source hashes, run IDs and private artifacts in the per-PR evidence. Q03 has additional detailed geometry/evidence edges in `QUEST_NAVIGATION_GRAPH.json` on PR #6's branch.

All reported green outcomes came from actual Actions execution of the recorded commits. Read-only CI remains; temporary write-token patch-preflight workflows were removed. Local sandbox tools could not build .NET or attach the game, so no local or live execution is claimed. No independent review was available in this session. This work stops at the review/live gates specified by the audit brief rather than treating them as passed.
