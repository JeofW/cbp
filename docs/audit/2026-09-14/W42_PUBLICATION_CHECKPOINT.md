# W42 publication-owner checkpoint — verified bounded repair

14 September 2026. Existing draft PR42; no merge/deployment. Tested source `c237ca5b5a32e33780552784b33e36e6973b735c`, tree `fb6e821f7ca1b4a018584647149d52564c614db5`. Full W42, exhaustive refactoring, native/live acceptance and independent review remain open.

## Attribution and recovered state

The attachment's99c8165e head was stale. This continuation read PR42 at243a444a with the original publication tests already committed; it recovered that run's19 cases rather than claiming them as new. Two further level-refusal cases were retained from another saved update. This continuation updated the recovery runbook, added nine acceptance cases at `f0bba89d525207b3d5511eb339bcfdbc509ce47e`, inspected a clean unchanged-production red result, prepared and tested three exact source blobs, and committed them via native GitDB as c237ca5b. All prior PR39/40/41/42 work remains. No broad-audit restart or replay of old stacked merges occurred.

## Reproduced defect and minimal repair

Before repair, ScanAndRefresh assigned executable LastSchedule and ActiveQuestIds before XML serialization/output and actual host acceptance. Failed/no-output publication could re-authorize an already-running selected/grind child and release prior item protection. A successful obsolete scan could overwrite replacement state/output. The void host loader could silently refuse compilation/level selection while the caller treated return as success.

QuestScheduler now prepares a local candidate, checks the actual refresh lease at mutating preparation/output boundaries, calls actual host acceptance, rechecks ownership after synchronous host events and publishes LastSchedule last. WholesomeAutoQuest supplies the real RefreshGate.TryApply and the loader; vendor results are applied only while current. ProfileManager retains void LoadNew signatures and adds TryLoadNew returning actual compilation/selection/reference acceptance. Exceptions still propagate. No unconditional late invalidation can erase a replacement.

Public direct generation APIs retain their old generate-only role. This is not a claim that every host side effect is transactional, all threads/session changes are covered, or all observation/protection provenance is fixed. Existing profile compiler/selector, refresh/execution gate and owner bodies remain real in the tests; external player/memory observations are controlled and no game is attached.

## Actual Windows execution and inspected artifacts

| Evidence | Commit | Run / artifact | Inspected result |
|---|---|---|---|
| New clean red, current variant | f0bba89d |34813174582 /10335921112|Original publication9/21,12 assertions; acceptance3/9,6 assertions; zero unexpected in both. All four suites build.|
| Candidate preflight, not a committed repair |2e70cf17|34814280250 /10336147195|Exact three-source candidate builds; publication21/21+acceptance9/9; broader scheduler group remains red.|
| Repaired focused, current variant |c237ca5b|34814906143 /10335859057|Publication21/21+acceptance9/9, zero assertions/unexpected; retained scan entry15/15, failure14/14, retry6/6, revocation13/13, relation23/23.|
| Actual combined17 |c237ca5b|34814906088 /10335973778|All17 build/setup0;14 run0; Wholesome, observation and boundary W42 entries run1. Both publication groups actually execute and pass here.|
| Host compilation |c237ca5b|34814906071 /10335639675|Build0;3278warnings/0errors; tests_run:false; no attached game.|
| Standalone observation owner |c237ca5b|34814906117 /10335704291|Build0/run1; same retained observation4/25 and ready1/5 frontier, not an infrastructure failure.|

All six outer SHA256 digests and CRCs were checked; see W42_PUBLICATION_EVIDENCE.json. Focused red/green each49 archive members/48 internal-manifest entries; combined73/72. Host4 and standalone5 members have no internal manifest; no nonexistent verification is claimed. Preflight12 members has source-specific manifests rather than an all-file manifest.

All1683 working-source/config hashes match repaired focused versus combined. Red/green key sets match and only the three intended production files differ; no tests/normalizer/boundary changes. All28 normalized outputs match red/green and focused/combined. Each nested focused export has48 files matching its working manifest; its two Wholesome production files change, while ProfileManager is separately confirmed by the full manifest and exact preflight blob. Do not claim the nested export contains that host file. The three promoted blob SHA256 values equal the inspected candidate and final working-source hashes. Temporary preparation workflow/script are absent in the repaired tree; comparison from f0bba89d shows only those three production paths.

Retained combined coverage includes original sale23/23, completion15/15, relation23/23,33 Python analyzer tests and99 compiled Singular source files. Existing managed lift tests, lift continuation17/17 and terminal route ownership18/18 remain; these are not new live lift acceptance.

## Still-red boundaries

Observation main4/25 includes19 missing proposed CaptureSnapshot contracts plus2 sale assertions; ready1/5 has4 assertions. Scheduler2/5 includes2 missing proposed contracts plus1 null-accepted pickup assertion. Extracted boundary16/24 has8 assertions/0unexpected. Counts overlap and are not unique defect totals. The Wholesome main still prints its old success line after groups execute; the aggregate correctly exits1 for the retained scheduler group. Use aggregate exit and full group results, not the last success line.

Raw occupied identity/read-success independent of metadata, player/session/frame provenance, reconnect/ABA, ready history and sale-dispatch freshness remain open. Maintain the prior post-W42 ordering and original assertions. No reconstructed binary identity or native shutdown repair is implied.

## New owner travel concerns

Read QUEST_TRAVEL_COVERAGE.md. Standard pickup and turn-in share XYZ-aware movement and the same taxi entry; no general pickup-disable branch was found. Taxi checking has a >400-yard trigger, shared15-second cooldown and nearest-origin limitation. Quest candidate ranking is XY-based despite XYZ endpoint probes. Lift safeguards exist, but discovery radii, phase-specific explicit guards and bounded terminal behavior need actual owner tests. The reported cliff/pickup detour is not reproduced or repaired by this publication change. No “every edge case” or “all routes verified” claim is justified.

## Handoff and protected state

AUDIT_RESUME.md and NEXT_CHAT_PROMPT.md point here; prior roots are retained byte-for-byte under pre-publication/. Earlier scan-entry/checkpoint/graph/evidence files and original ZIP remain. The downloadable transfer includes complete archives, source excerpts, verification output and a standard-library verifier. It verifies saved evidence, not new C# execution.

GITHUB_CONNECTION_RECOVERY.md supersedes older reconnect-first instructions: explicit @GitHub retry -> fresh GitHub-selected ChatGPT conversation -> reconnect only if the fresh conversation lacks publishing. No credentials were requested/exported. Native writes and readbacks succeeded. The temporary preflight used ordinary repository-scoped workflow authentication solely to store immutable verified blobs, never to publish refs, and was removed at promotion.

Master8382a7ec and backupc43c50d8 were rechecked and remain untouched; PR34 already merged,35/36 and the W42 stack preserved,25 excluded,43 capability test separate/unmerged. No merge, force push, deployment, installed/native/mesh/runtime-capture or Lua changes. Keep the PR draft pending broader verification and owner review.
