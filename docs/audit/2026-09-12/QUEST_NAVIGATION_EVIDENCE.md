# Quest navigation evidence ownership — continuation investigation

Baseline: `498257b58cb93470ce2c940bbdaab98b908a20d7` (verified checkout-relative quest suites). Audit finding Q03 supplements, rather than closes, the inferred poi-floor loop in GRAPH.md.

## Source-supported defects

`runtime-snapshot/Bots/WholesomeAutoQuest-master/QuestScheduler.cs` at the baseline:
- `EndpointKey` (lines 346–357) intentionally groups retries into 80-yard XY cells.
- `CreateCachedNavigationAssessment` (lines 949–998) also uses that key for combined safety/reachability/score evidence. Distinct XY positions and heights can therefore borrow a result; identical coordinates can borrow another record's metadata.
- Both `AddObjectiveWork` and `AddRelationWork` assess only `cluster.Value[0]`, then publish all cluster hotspots. Correcting the dictionary key alone cannot repair this path.
- The unsafe-query catch sets `knownSafe = null` even when live evidence or a data annotation already established false. A failed query must not erase a veto.

These source defects are not yet proof that they caused the recorded Grod incident. The navmesh/lift acquisition failures have separate owners and remain gated on live geometry.

## Bounded repair design

Keep coarse keys for retry and query budgets, never as spatial proof. Cache raw live evidence by exact map/X/Y/Z; apply each current record's metadata and unsafe-point predicate independently. Preserve at most one new native path probe per existing coarse cell per scan: another unprobed position stays unknown, not safe, reachable, or unreachable by inheritance. This does not assert a hard time bound on a synchronous native query.

Assess and filter every candidate hotspot before objective or relation grouping. Preserve the existing five-distinct-cell attempt cap, unknown-reachability bounded-probe contract, stage ownership, and 30-second non-quarantining navigation retry. Do not modify persistent recovery-key encoding or fabricate floor/landing geometry.

The fixture file exercises objective, pickup, turn-in, alternate floors, explicit vetoes, cache scope, provider failures, score ownership, map identity, fresh scans and query-count controls through the real scheduler/cache. The test-only commit precedes implementation. CI outcome and repair commit are pending; no fixed-by edge or live PASS is asserted here.

## Remaining gates

Persistent 80-yard XY recovery scopes are still coarser than a floor-aware topology. Mesa acquisition, supported boarding/exit geometry, total walk/wait/ride/onward cost, native-query wall time, and in-game route success remain separate work. More unprobed alternates can now survive as unknown; execution's existing bounded recovery and movement-support checks must be verified live before rollout.
