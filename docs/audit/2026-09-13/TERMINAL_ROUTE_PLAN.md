# Terminal route ownership: directed repair plan

Authorization and architecture: continue the owner-approved MESH_PHASE_PLAN.md and CODEX_AUDIT_PROMPT.md. Preserve existing public MoveResult values, keep physical obstruction handling separate from search/geometry failures, do not fabricate lift geometry, and do not merge or deploy.

## Evidence before implementation

Combined source baseline acdd805b15bbef87056cb30e1f37e65af0fa6416 passed all nine entries in run 34728159474. Artifact 10308072804 SHA-256 b84067762be3e3faec4db7d79ced479f56fd76bf72d1a15fb3440a1843b18f2f was downloaded and inspected.

Test-first commit efb5131f106579bef8ff1e3672a9e5ad6d5a1e8f, run 34728424060: 16/18 terminal scenarios fail. Three invoke the real MeshNavigator terminal owner and demonstrate one unsolicited physical unstick; invalid complete coordinates also fail behaviorally. Remaining failures expose missing typed status/retry/reset contracts. Complete-route and active-prefix controls pass. Artifact 10308271921 SHA-256 9d0f7c136cf4a9a7251025a42a1f051cc97d13cb202ddea32be9459f9535de70 was inspected. Missing-member failures are new-contract checks, not falsely described as 16 independently reproduced production bugs.

Real format-6 replay independently returns DT_OUT_OF_NODES plus DT_PARTIAL_RESULT on captured forward Grod queries. This disproves treating a partial prefix as proof of physical obstruction or disconnected geometry. Live transport discovery and attachment remain separate hypotheses.

## Focused design and alternatives

Split the terminal evidence owner into MeshNavigator.RouteOutcomes.cs (same production type, no new global manager). Keep raw status on actual movement path installation, not general endpoint probes. On terminal exhaustion return Failed with a typed reason and a three-second, map/origin/destination-scoped retry deadline; observations do not slide it. Changing origin/destination by more than two yards in 3D, changing map, replacing/clearing the route, active transit, clock rollback or deadline expiry permits progress. Valid active path prefixes remain consumable. Preserve native resource-exhaustion detail before classifying vertical or same-floor partials.

Rejected: increasing lift radii without geometry; using UnstuckAttempt as a search result; marking unknown goals unreachable; quarantining quest data; indefinitely extending retries; moving native calls onto an unproven worker thread; claiming a native-query wall-clock bound from a retry interval.

## Execution checklist

- [x] Read current source, original audit briefs, recorded native status and combined tests.
- [x] Inspect actual failing terminal regression execution before implementing.
- [ ] Run the exact hash-locked owner patch against all nine validation entries.
- [ ] Inspect failures, controls and final source hashes; only then promote the verified blobs.
- [ ] Remove temporary patch/preflight inputs, rerun committed-tree tests and open a focused PR.
- [ ] Trace the result through quest/vendor consumers, recording any remaining escalation rather than claiming whole-route closure.

## Boundaries

The retry window prevents immediate identical movement-query repetition; it neither proves route optimality nor cancels an already-running native query. Physical recovery remains for actual commanded ground failure. Quest progress monitoring, automatic lift acquisition, changing blackspot geometry, all-class combat policy and live acceptance require their own evidence and tests. New outcomes are additive, not a breaking change to plugin MoveResult contracts.
