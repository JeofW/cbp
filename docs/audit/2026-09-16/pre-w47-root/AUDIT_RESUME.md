# CopilotBuddy audit — scheduler observation continuation

14 September 2026. Resume draft PR44, branch `audit/next-42-scheduler-observation-20260914`, stacked on PR42 at e97263eef2dbf1bc6b050d80d9c3e51232ce42e2. Re-read current heads before writing. Do not resume from the older publication-only handoff or recreate raw/readiness/sale/fixture fixes recovered from interrupted continuations.

## Current state

**Test-first reproduction exists; proposed production repair is not verified or promoted.**

New tested commit35cc917435f74d33558d0250e566bcb7ad9a6641:23 actual raw-memory/scheduler/XML/file/loader/running-gate cases execute,7pass/16assertions/0unexpected. Existing scheduler2/5 remains. Actual combined17 builds all succeed;16 executions pass and the Wholesome aggregate fails. Host builds with0errors/3278warnings, tests_run:false. Complete artifacts and matching1690 source/config hashes were inspected;33 normalized outputs match after their expected directory-layout mapping.

Candidate preflight at9a84d797ec6a77f9967c68ffe2117946e10d6843, run34831727583, failed twice BEFORE runner assignment: zero steps, runner_id0. No candidate C# build/run occurred. The error annotation text is not available through the connector. Inspect that annotation in GitHub Actions; do not repeat blind retries or mislabel this as missing GitHub publishing actions.

## Read in order

1. `docs/audit/2026-09-14/W42_SCHEDULER_OBSERVATION_CHECKPOINT.md` — exact state, recovered vs new attribution, verification and next action.
2. `W42_SCHEDULER_OBSERVATION_EVIDENCE.json` and `W42_SCHEDULER_OBSERVATION_GRAPH.json` in the same directory.
3. `W42_SCHEDULER_OBSERVATION_PLAN.md`, `W42_SCHEDULER_CANDIDATE_UNVERIFIED.diff`, `NEXT_CHAT_PROMPT.md` and `GITHUB_CONNECTION_RECOVERY.md`.
4. Existing raw/readiness/sale/publication plans and all older evidence, plus `QUEST_TRAVEL_COVERAGE.md`.

The candidate preparation script and temporary preflight workflow are deliberately retained to resume exact verification. They have not changed production. Remove them only when promoting a verified source blob. Previous root handovers are retained byte-for-byte under `docs/audit/2026-09-14/pre-scheduler/`.

All original assertions and successful controls remain. The actual XYZ/cliff/lift/wind-rider complaints, continuous execution freshness without combat suppression, true session/frame/ABA provenance, typed cache freshness, broader protection/merchant lifecycle, independent review and live original3.3.5a acceptance remain open. No merge/deployment/native/mesh/installed change. Master8382a7ec and backupc43c50d8 protected;34 already merged,35/36 retained,25 excluded,43 separate. Work remains in ChatGPT/GitHub.
