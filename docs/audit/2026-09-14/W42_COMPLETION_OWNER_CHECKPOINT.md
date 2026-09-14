# W42 completion-owner checkpoint — PR #39

## Current state, not a broad-audit restart

Draft PR #39 is stacked on draft #38, branch `audit/next-42-completion-owner-20260914`. Recovered parent: `3792caae261db595836eea794128c4305a241cbb`. New test-first commit: `29644fecab3211f313d05ef84a18025fa6365ce6`. Production repair: `1dccead847f330430f148609858c6f89d5724ad8`. Final production/test source verified together: `3d8493927adb76e886b1e1ccca03a93cce0cfb8b`. Any later checkpoint commit must be documentation-only and compared to that tested commit before inheriting its evidence.

Authenticated GitHub branch/tree/blob/commit/ref writes and draft PR creation succeeded in this continuation. Push events executed Windows Actions. The local container did not have an authenticated checkout or C#/.NET runtime; C# execution happened on GitHub's Windows runners, not locally.

Master remains `8382a7ec05a64212ea0a237159dca427a0767425`; the protected backup remains `c43c50d8d5d6775055f19bf018b52930a264d4a4`. PR #34 was already merged by the owner. PRs #35/#36 are inherited, not rebuilt or remerged. #25 stays excluded. No merge, deployment, installed-bot change, Navigation.dll replacement, or mesh download.

## Reproduced and repaired owner

`QuestLog.GetQuestCompletionSnapshot` previously used optional materialization to decide acceptance. An occupied descriptor plus missing cache metadata could become `accepted=false` and then borrow historical KnownComplete/KnownIncomplete.

The only production change in this PR resolves nonzero raw acceptance first. If accepted metadata is missing, it returns `accepted=true/Unknown`, never historical completion. Normal hydrated completion, genuinely unaccepted history, and descriptor/cache interruption propagation are retained. ID zero explicitly remains an empty slot, not an accepted quest. Public signatures and legacy raw/materialized lookup behavior are unchanged.

This is NOT a whole-log observation, an atomic snapshot, session provenance, or a guarantee about native completion dispatch. Cache errors, false-success memory reads, concurrent changes and downstream stale authorization still require their own owner-level work.

## Fresh red/green evidence

All listed archives were downloaded, hash-checked against GitHub metadata, and inspected through every result and full run log. See W42_COMPLETION_OWNER_EVIDENCE.json for exact SHA-256 digests and source identities.

| Execution | Commit | Run / artifact | Result |
|---|---|---|---|
| New red | `29644fec` | 34792153739 / 10328428727 | Full completion owner 10/15; five intended assertions, zero unexpected errors. Original boundary 13/24. |
| First repaired | `1dccead8` | 34792396654 / 10328117922 | Completion 15/15; original boundary 16/24, eight assertions, zero unexpected errors. |
| Final affected | `3d849392` | 34792864668 / 10328733635 | Same 15/15 and 16/24; Windows x86, .NET 10.0.12. |
| Final combined | `3d849392` | 34792864662 / 10328584617 | 13/14 entries build/run exit 0; pending W42 scheduler initializer aborts the remaining executable. Sale 23/23; 33 analyzers; 99 Singular source files compiled. |
| Final retained baseline | `3d849392` | 34792864691 / 10327968850 | Build/run 0; original Wholesome main finishes; relation 23/23, with executable Windows/x86 guard. |
| Final host build | `3d849392` | 34792864717 / 10328429901 | Host build exit 0; no tests in this host-only job. |
| Older ready/log runner, rerun on repair | `1dccead8` | 34792396655 / 10328249449 | Build 0; ready initializer 1/5; 25 main cases masked by initializer abort. |

The full-owner suite's original fourteen assertions were retained and one test-first zero-ID compatibility case added. Comparing red and green source archives verifies unchanged test/fixture/extractor bytes; only the production owner changes (plus explanatory README in the final source). These tests use actual linked QuestLog/PlayerQuest and actual history ownership, with controlled external memory/cache/world boundaries. No client attached.

## Why a supplemental baseline exists

PR #38 introduced pending scheduler checks as a module initializer. It aborts the original Wholesome executable before all old tests can run. The added WholesomePostmergeBaselineRegressionTests project explicitly links the thirteen unchanged retained test files, current production host, and the same ten Wholesome production files. It runs the old baseline without rewriting it. Original project/initializer, combined job, and all W42 red checks stay enabled and unchanged. This does not turn the combined result green or claim W42 closure.

## Remaining failures and next closure frontier

The eight remaining boundary assertions cover uncached/mixed/full-log item protection, absent dataset requirements, hydration and eviction transitions, same-count identity replacement, and new acceptance before sale dispatch. The ready fixture still confuses metadata/readiness loss with raw departure and carries readiness across player owners.

The scheduler's five pending checks report one passing empty-log control, one real null-accepted-to-pickup failure, two missing-contract assertions, and an unavailable-world NullReferenceException. Repair the fixture isolation/reporting before presenting four clean behavior reproductions. Do not count its masked retained cases, or the old ready runner's 25 masked cases, as executed.

Continue at actual owners: successful raw descriptor reads and all occupied IDs; optional metadata completeness separately; actual player/session/observation lifetime; `ScanAndRefresh -> MaterializeSchedule -> ProfileBuilder.WriteProfile -> DoScan/ProfileManager.LoadNew`; and the already-running `WholesomeExecutionGate`. Unknown/stale observations must suppress completion side effects and destructive permission, must not create free capacity or pickup eligibility, and must invalidate executing plans. Equal scans/counts do not establish atomicity or ABA immunity.

Source-confirmed cautions: `GreenMagic.Memory.ReadInternal<T>` returns default when ReadBytes returns null; a zero value alone does not prove an empty descriptor. Default AcquireFrame is not automatically a hard native frame capture. FrameLock captures an executor and manages continuous execution; native timeout/release behavior must be included before claiming an observation lease survives. Process ID or character/realm equality alone is not a session generation. Do not invent provenance merely to satisfy a test helper.

## Evidence quality and review

Source-confirmed: raw/materialized information-loss path and downstream use sites. Reproduced: cache-miss historical fallback through actual owners under controlled boundaries. Regression-verified: bounded completion repair and retained baseline above. Live-unverified: runtime frequency, original client/server acceptance, native completion behavior and moving platforms.

Self-review covered the production diff, zero identity, cancellation preservation, immutable prior completion results, source hashes and test coverage boundaries. Independent review remains pending. Build logs contain existing warnings; this is not a warning-free build claim. Boundary archives contain 16 files, of which 15 appear in the internal hash manifest, correcting the initial PR prose's count.

The exhaustive architecture/refactoring audit is NOT complete. Full W42 is NOT complete. Do not proceed to unrelated backlog repairs while these observation/publication/dispatch dependencies remain open. After W42, retain the saved order: owner-scoped item protection; atomic FILE protection reload (runtime/profile protections are not cleared by that reload); merchant/mail/discard/consumption; vendor discovery/settings; rest/restart/cancellation; special quests; roster; combat/support; native/lift work. No merge or deployment without new explicit owner approval.
