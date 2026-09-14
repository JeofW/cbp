# CopilotBuddy audit — verified publication slice and travel frontier

Recorded 14 September 2026. Continue existing draft PR42, branch `audit/next-42-scan-entry-20260914`, base `audit/next-42-ready-owner-20260914` at `d08d0b20f6187e9a423489fbcfebed9d1b275537`. Re-read actual remote heads before writes; another conversation or an interrupted response may already have published work. Full W42 and the exhaustive audit remain open.

## Current evidence-backed state

**Tested repair:** `c237ca5b5a32e33780552784b33e36e6973b735c`, tree `fb6e821f7ca1b4a018584647149d52564c614db5`. Three production owners now keep a running-bot candidate non-executable until the actual host accepts its profile and the real refresh lease still owns publication. Cancellation and replacement controls remain; failed publication retains conservative ActiveQuestIds protection. Existing public generate-only APIs remain compatible and are not a universal host-acceptance guarantee.

**Clean red:** `f0bba89d525207b3d5511eb339bcfdbc509ce47e`: original publication9/21 and new acceptance3/9, eighteen assertions total, zero unexpected errors. At the exact repaired commit both focused and actual combined runs execute publication21/21 plus acceptance9/9. All17 combined entries build;14 run0, three broader W42 entries run1. Host build0,3278warnings/0errors, tests_run:false. This is Windows x86 controlled owner execution, not a live WoW acceptance result.

Read `docs/audit/2026-09-14/W42_PUBLICATION_CHECKPOINT.md`, `W42_PUBLICATION_EVIDENCE.json`, `W42_PUBLICATION_GRAPH.json`, the extended `W42_PUBLICATION_SLICE_PLAN.md`, and `QUEST_TRAVEL_COVERAGE.md`. The attached publication handoff includes complete red/focused/combined/host/standalone/preflight artifacts and a standard-library verification script. Re-running that verifier checks transferred evidence; it does not run new C# tests.

## Immediate continuation

Do not recreate publication tests or repair. Review the source/evidence boundary, then continue existing W42 raw occupied-quest/read-success provenance, readiness history, session/frame/reconnect/ABA and sale-dispatch freshness. Missing proposed APIs are not automatically reproduced production defects. Preserve all original assertions and ordinary-success controls.

The owner additionally requires elevation-aware safe quest routes and pickup/turn-in wind-rider parity. Source tracing found XY-based quest ranking despite XYZ endpoint/path checks, restrictive lift-discovery heuristics, no overall timeout contract in the inspected lift controller, and taxi threshold/cooldown/nearest-origin limitations. Standard pickup and turn-in both reach shared taxi-aware ground navigation; the reported live pickup detour has NOT been reproduced or fixed. Follow the actual-owner test matrix in QUEST_TRAVEL_COVERAGE.md; no blanket conversion of every Distance2D call or unconditional taxi forcing.

## Recovery and authorization

`GITHUB_CONNECTION_RECOVERY.md` is authoritative and supersedes reconnect-first wording in older files: first retry an explicit @GitHub invocation; if publishing actions remain absent, preserve handoff and use a fresh ChatGPT conversation with GitHub selected; reconnect only if that fresh conversation also lacks publishing. Do not repeatedly rediscover the limitation. Do not infer expired credentials from missing actions or equate container tooling with native connector capabilities.

No merge, force push, deployment, installed-bot/Navigation.dll/mesh/runtime-capture change is authorized. Preserve master `8382a7ec05a64212ea0a237159dca427a0767425`, backup `audit/backup-master-before-approved-merge-20260913` at `c43c50d8d5d6775055f19bf018b52930a264d4a4`, PR34's existing merge, PR35/36 and the W42 stack; exclude PR25. Capability-test PR43/sol-write-test stays separate and unmerged. No Work/Codex switch. Native/live acceptance and independent review remain pending.

The prior root handovers are preserved byte-for-byte in `docs/audit/2026-09-14/pre-publication/`. Earlier PR39/40/41/42 checkpoints, graphs, original ZIP and their attribution remain intact. Their old next-step and reconnect instructions are historical where this checkpoint supersedes them.
