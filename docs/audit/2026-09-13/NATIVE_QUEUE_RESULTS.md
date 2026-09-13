# Indexed native queue — verified experiment, not a deployment recommendation

Source candidate: a560eabe7acf763272aeaeff18c031b9e267687e. Base: c8c7fb59c23d627a6f55ba611e48000619d66a0e. Pinned upstream: Likon69/Navigation-C-@221dfe2877fa3f749ada49c98687e99fac74d437. The saved work was recovered after interrupted chat responses; the result below was checked against complete private artifacts and rerun portable tests, not reconstructed from the last chat summary.

## Algorithmic reproduction and functional controls

Run 34745419542, artifact 10314170476, SHA-256 3b7fde684d7f5dd301dfdac1c626b4c28bb25577a221474167e5a6a0fcdcc40e. Archive downloaded and verified during continuation.

The same pinned Detour queue/pool/allocator fixtures execute before and after the optional patch. Baseline: four functional controls pass, the bounded-update-work assertion fails. For 2,000 decreasing-key updates in a 20,000-node queue, the baseline performs 40,004,000 instrumented heap accesses; the candidate performs 6,000. Both preserve ordered draining, stale/absent/popped handles, existing equal-cost ordering, and a 20,000-operation seeded model. The candidate passes all five tests. AddressSanitizer/UndefinedBehaviorSanitizer functional executions pass for both variants. Complexity instrumentation is intentionally omitted from sanitizer executions; the printed fifth case there is a skip, not an additional complexity measurement.

These baseline, candidate and candidate sanitizer executions were repeated locally against hash-checked source during recovery, with the same functional and operation-count results. One Python comparator test also verifies rejection of altered/missing route fields. No game was attached.

## Actual mesh comparison: the expected workload speedup was NOT established

Run 34745419648, artifact 10313921620, SHA-256 ccf3e675b325f812d915e640a2185f41e0e7c655f4d0895488a0c91e558a678a. Complete artifact downloaded, verified and independently passed through compare_routes.py during continuation.

Four fresh native processes run baseline / indexed / indexed / baseline. The 80 query records match exactly across the compared semantic fields: requested endpoints, point arrays, polygons, flags, area types, partial/resource status, failure stages and counts. There are 60 comparisons against the first reference execution. Each route has six warm samples per variant; cold samples remain in the raw records but are excluded below.

| Route | Baseline warm median ms | Indexed warm median ms |
|---|---:|---:|
| grod-local-control | 0.08365 | 0.06485 |
| grod-lower-origin | 488.40310 | 529.88070 |
| grod-partial-origin | 505.78605 | 510.79435 |
| grod-reverse | 1.56440 | 1.45120 |
| lower-local-control | 0.01740 | 0.01795 |

The two expensive forward Grod cases do not improve in this sample. This falsifies the working expectation of a useful speedup for those captured queries, even though the isolated queue update becomes cheaper. It does not prove why the overall query remains slow. Do not select favorable local-control microtimings and advertise a general performance win. Resource-limited paths remain resource-limited; no route-completeness or optimality claim follows from equal outputs.

## Tradeoffs and disposition

Internal dtNode size in the portable 64-bit-polyref fixture grows from 32 to 40 bytes. Queue clear changes from constant-time size reset to visiting the current frontier to invalidate membership. The patch assumes the existing single-open-list ownership of each node pool. Neither serialized mesh layouts nor exported path-result structures change, but additional memory/cache and clear costs are real tradeoffs.

The patch remains opt-in through Build-PinnedNative.ps1 -IndexedQueue. Default pinned native compilation is unchanged, Lib/Navigation.dll is unchanged, and no installed bot file was touched. The experiment is suitable for review and future profiling, not default promotion on the evidence collected. Further profiling must measure search expansions, queue occupancy/update frequency, and node/polygon lookup work before selecting the next optimization. Do not increase the node budget or reinterpret partial results to manufacture a successful route.

## Directed provenance

NQ01 linear modify scan -> actual pinned queue fixture -> test-first failing complexity assertion -> optional indexed-membership patch -> unchanged functional/sanitizer controls -> identical real-mesh semantic records -> negative forward-route timing result -> KEEP_EXPERIMENTAL.

This is a human-reviewed evidence chain, not a complete semantic knowledge graph. Independent review, all-map coverage, live transport behavior, native cancellation and source-built deployment acceptance remain open. Nothing was merged or deployed.
