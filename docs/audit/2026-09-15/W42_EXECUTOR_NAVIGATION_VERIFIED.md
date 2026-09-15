# W42 executor and navigation repairs verified; root admission remains open

Evidence date: 14 September 2026 UTC / 15 September 2026 Malaysia time.
Repository: `jeofwong/CopilotBuddy-private`, private organization ID1367174964.
PR44 remains the existing draft stack, branch `audit/next-42-scheduler-observation-20260914`, base PR42 / `audit/next-42-scan-entry-20260914` at `e97263eef2dbf1bc6b050d80d9c3e51232ce42e2`.

## Actual published production and execution

This continuation performed real native GitHub writes and triggered new Windows x86 runs through those pushes. These are not local-only candidates or recovered executions attributed to a conversation that did not launch them.

1. `427b86d9986cac9264be5a7fc3824f5abf9d5a39`, tree `0895411a1f12673f83f97118ff0fb593c2e899ee`: executor ownership and Composite cancellation repair.
2. `f7c49c0affda1ec7fea0a180a85737703ca38b2f`, tree `a465e1db4a86063ba7dea350290ccfcd2ca5661b`: four navigation cancellation catch filters. This is the latest tested production revision.

Both commits used native create_blob/create_tree/create_commit followed by a live-head check and non-force update_ref. No test or workflow source changed in either repair. This checkpoint is a later documentation-only commit, not another claimed C# execution. No merge or deployment occurred.

**W42 and the exhaustive audit are not complete. Independent review and original-client live acceptance remain outstanding.**

## Reconciled baseline, not a recreated test suite

The supplied checkpoint pinned3dbb79af. Live PR44 had advanced to `758efb0b8133bf79c25fd0f152d5a3868f4acb69`, tree `22aaab76bbe3a7ca4dd814d752fd690fca9adc67`. Newer test-only work corrected the newly authored deferral expectations, added the16-case navigation group and fixed its import. That work was preserved, not authored again here.

Read `docs/audit/2026-09-14/W42_EXECUTOR_TEST_RECONCILIATION.md`: it supersedes the terminal-deferral wording in the older ownership plan. The retained generic executor must remain Running while deferred, issue no repeated effects, clean the active branch once, retain the behavior/node without disposal and start a fresh branch lifetime after deferral clears. Root protective scheduling must not be repaired by breaking that contract.

The earlier real scheduler publication repair at `c6d091eef533bec0eb2017da197f7c600cc90303` remains present and was not recreated or reverted. Old creation-time no-execution statements are historical, not the current execution status.

## Bounded repair details

Commit427b changes only `Bots/Quest/Actions/ForcedBehaviorExecutor.cs` (+174/-63) and `TreeSharp/Composite.cs` (+9/-7). The executor captures behavior/node/collection/branch ownership, rechecks identity across callbacks and continuing ticks, registers idempotent cleanup before branch Start, and drains its previous nested lifetime on restart. It revisits completion and deferral while Running; deferred behavior/node retention, ordinary terminal status and completion advancement remain valid. Old callbacks cannot redirect cleanup or completion into replacement work.

Composite propagates OperationCanceledException like the retained ThreadInterruptedException boundary instead of converting it into branch failure/fallback. The original signal survives ordinary cleanup failure. GroupComposite and the whole root were not broadened.

Commitf7 changes only four catches (+4/-4) in `runtime-snapshot/Bots/WholesomeAutoQuest-master/QuestScheduler.cs`: safety/navigation catches in CreateCachedNavigationAssessment and AssessNavigation. Cancellation/interruption propagate unchanged, while ordinary provider errors still produce conservative unknown observations and retain bounded caching. No late catch revokes a reentrant replacement publication. The existing16-case group counts actual provider callbacks and checks direct/cache/actual-refresh paths, replacement and ordinary-failure controls.

| Native payload | Git blob | SHA256 |
|---|---|---|
| ForcedBehaviorExecutor.cs | cc35a458dfdc2f9b796dfb93373e8eb0752fab41 | 1c1e9179b90f2b2a6bc2a1f11634cacfd0b3ccc005d54d387c51cb6f019f72e2 |
| Composite.cs | 335193c0f09269ea131c679ca1ed21229a315cce | 06a3d081673d2f88b50caddcb7cc2b5d4eab4183605c21ea58ff3f300f66717e |
| QuestScheduler.cs | 7df116d8a0cd6a630ab319e4123fff9f5c52da7a | 4a06ba4247caca621600f0aa18e928de7fac1692850afabf1cebb9a0cc9a3653 |

## Actual unchanged-fixture Windows outcomes

| Group | Live red758efb | Executor427b | Navigationf7 |
|---|---:|---:|---:|
| Executor ownership | 6/23 | 23/23 | 23/23 |
| Navigation cancellation | 4/16 | 4/16 | 16/16 |
| Quest-action freshness/root admission | 8/25 | 8/25 | 8/25 |

All three groups have zero unexpected fixture errors in these revisions. Each outcome was checked in both focused and actual integrated logs. Passing case gains are not counts of independently established bugs.

All four focused projects build at each listed revision; only Wholesome runs1. The actual integrated suite has all17 build/setup entries0,16 run entries0 and only Wholesome run1. The remaining17 quest-action assertions stay visible. Scheduler raw-publication23/23 and retained scheduler-observation5/5 continue to pass.

| Revision/execution | Run | Artifact |
|---|---:|---:|
| 758efb focused current | 34868235594 | 10357478486 |
| 758efb integrated17 | 34868235659 | 10357873117 |
| 758efb host | 34868235685 | 10357263667 |
| 427b focused current | 34887958095 | 10365343485 |
| 427b integrated17 | 34887958187 | 10365821729 |
| 427b host | 34887958456 | 10365531960 |
| f7 focused current | 34889438490 | 10366202629 |
| f7 integrated17 | 34889438731 | 10366351598 |
| f7 host | 34889438520 | 10366685759 |

Both repaired revisions pass host compilation:3278warnings/0errors, explicit tests_run:false and game_attached:false. The final standalone QuestLog run34889438511/art10366226821 also builds/runs0, Windows x86, no game attached.

The two new focused runs also retain historical normalized arms art10365448257 and10366491192: pinned production2d3728fe cannot compile Wholesome with the modern fixtures, including missing CaptureSnapshot/TryLoadNew and fixture-binding errors. The other3 projects build then run1. These arms are NOT clean behavioral red and not every Actions badge is green.

## Evidence checks actually executed

The downloadable handoff contains nine primary archives and three supplemental archives with authenticated metadata, the original inputs unchanged, exact native repair payloads, local diffs, continuation instructions and standard-library verifiers. EVIDENCE.json and SUPPLEMENTAL_EVIDENCE.json retain exact run/artifact IDs and outer SHA256 values.

For each primary revision, verification checks outer digest/ZIP CRC, all56 focused and80 integrated internal-manifest entries, exact commit/tree, all result/log entries,1693 matching source/config hashes,57 nested exported sources,32 original/generated normalization pairs and36 identical normalized members. Existing/generated fixtures remain byte-identical across the two production commits. Only the two executor production files change at427b; only QuestScheduler changes atf7.

The57-member export does not contain ForcedBehaviorExecutor or Composite. Their saved native payloads separately reproduce the Git blob IDs and match both CI working-source hashes. Host/standalone archives have no internal manifest; none is claimed. The supplemental historical archives have55 internal entries each, independently verified with their distinct identities/outcomes.

Baseline executor stdout/stderr record ordering differs between focused and integrated, but unique case identities and complete outcomes agree; both orders are retained. The new verifier's12 Python utility tests pass. The older supplied verifier and its6 utility tests were also rerun. These are not additional CopilotBuddy C# tests.

The optional text-only source-input archive contains1693 reconstructed source/config files, each matching both CI input manifests. It is not an authenticated checkout, full repository or buildable distribution. Reconstruction records1600 exact local matches and93 conversions to the recorded Windows CRLF bytes.

## Remaining root/publication frontier

QAF16 now propagates actual rescan cancellation and reaches the later deferral/no-more-effects/protection checks, then fails: **combat was blocked by quest admission**. It is not a wholly passing case. Other stale continuing-publication and combat/service-admission failures remain.

Do not append raw freshness to the whole root, mutate QuestBot's shared static root as instance-specific policy, bypass the inner quest-order ActionAlwaysSucceed idle shield into roam, or leave a denied Running child controlling protective work. Preserve legitimate exclusive service owners. RefreshGate.TryApply is Running-only; completed/Idle publication and pending-refresh-alone controls require distinct continuing ownership, not an invalid Running-only permission check.

Source review found that the real TreeRoot driver starts only a non-Running root, while the existing SupportRuns test helper explicitly restarts it. New full-root tests must exercise actual continuing-selector preemption and cleanup before protective effects without relying on that restart. Several retained isolated gate fixtures replace the entire root child with a controlled leaf; these are not complete-root acceptance. Any necessary topology reconciliation must be separately explained and test-only, retaining existing assertions rather than hiding failures.

Independent review was attempted through the native reviewer action. It returned a snapshot without an error, but follow-up reads showed no requested reviewers and no reviews. No review is claimed queued/obtained, no cause is inferred, and self-review is not independent approval.

Native session/frame/ABA identity, typed-cache freshness, protection/reload/merchant acknowledgment and `docs/audit/2026-09-14/QUEST_TRAVEL_COVERAGE.md` remain open. No supervised original WoW3.3.5a build12340 live acceptance was performed.

## Protected state and next continuation

Fresh reads retain master8382a7ec05a64212ea0a237159dca427a0767425 and `audit/backup-master-before-approved-merge-20260913` atc43c50d8d5d6775055f19bf018b52930a264d4a4. Neither was a write target. Preserve34's approved merge,35/36 and W42 ancestry; exclude25; capability43/45 stay separate and unmerged.

No merge, force push, deployment, installed-bot/Navigation.dll/mesh/runtime-capture/Lua change or Work/Codex switch occurred. No credentials were requested/exported, no GET-write or private-guard removal was used, and the old six-path documentation block was not bypassed. Old root handovers were not rewritten.

Read live heads, this checkpoint and the downloadable NEXT_CHAT_PROMPT.md/ROOT_ADMISSION_FRONTIER.md before older nested handoffs. Do not recreate these two repairs or existing tests. Obtain independent review; add missing full-root/publication tests and exact-source red before the next production slice. Require exact committed-source focused+actualintegrated17+host verification and distinguish offline repair from original-client acceptance. The exhaustive audit remains incomplete.
