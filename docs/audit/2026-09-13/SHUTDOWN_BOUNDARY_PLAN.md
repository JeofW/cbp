# Shutdown entry and cleanup boundaries

Owner-authorized continuation of WORKER_OWNERSHIP_PLAN.md, based on the promoted 22-case worker repair be623f887411f94981e2a15bda544f07f6fb8678. Inspect the new test-only run before changing production.

The first repair preserves central stop signals and excludes overlapping starts. Three neighboring boundaries still warrant direct testing: WorkerThread's initial lock/state check is outside its try/finally; a throwing stop-request subscriber prevents the subsequent interrupt; Composite/GroupComposite cleanup stops visiting owned handlers/children after the first exception. A stale WorkerThread entry also writes Stopped without checking its identity.

Add a separate Main-driven Windows x86 harness so real thread joins run after module initialization. Execute nine scenarios against the actual TreeRoot and TreeSharp owners. Preserve ordinary cleanup ordering, exception identity and success status. Do not change quest data, native thread-affinity, profile behavior or selected bot semantics. Add the new executable as the eleventh mandatory integration entry; a red suite must not be silently omitted.

Repair only reproduced contracts: keep entry validation inside cleanup ownership; never let a stale entry mutate a newer owner; deliver the stop wakeup in finally while preserving the subscriber error; drain every owned cleanup before propagating the first error, prioritizing a ThreadInterruptedException over ordinary cleanup errors. Preserve normal LIFO order and idempotence. A command already inside native code and cross-owner plugin commands remain separate acceptance gates.

Retain failing and passing runs, exact source blobs and all eleven combined entries. No merge to master, installed-file change, native binary replacement or live-game PASS is authorized by this plan.
