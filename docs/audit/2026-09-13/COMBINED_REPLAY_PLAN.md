# Combined managed/native replay

The managed production tree now includes all prior integration repairs, cast-dispatch repair 25993efe, worker ownership be623f88 and shutdown ownership fc271247. This step changes only test instrumentation, read-only CI and evidence documentation. Do not merge master or replace Lib/Navigation.dll.

Run all eleven managed/analyzer entries and replay the captured five route requests four times each using the latest built host. Compare the checked-in DLL and an unchanged source build pinned to Likon69/Navigation-C-@221dfe2877fa3f749ada49c98687e99fac74d437 in separate processes. Assert the module actually loaded is the selected DLL; record its hash and the host assembly hash. Fetch/hash-verify only the original 989 Kalimdor LFS objects.

Preserve the raw status and failure step, report resource-limit and out-of-nodes details explicitly, and separate native time, lock waiting, managed/cleanup time and total caller delay. Check timing consistency on every request. Require all twenty requests to execute and all eight nearby controls to complete. A partial captured path is not full-route success and is not an unreachable verdict. Existing mesh/native data stay unchanged.

Results are pending until the actual commit's artifacts are inspected. This is not a claim of all-map coverage, full quest acceptance, a complete movement owner for every native/plugin call, or optimal lift acquisition. The candidate library remains in private CI artifacts, not installed software. Independent review and live attachment/exit remain open.
