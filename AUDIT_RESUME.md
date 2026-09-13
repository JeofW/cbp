# CopilotBuddy audit — post-merge resume checkpoint

Repository: `JeofW/CopilotBuddy-private`. This document supersedes the pre-merge statements in older checkpoint documents. Inspect remote heads before using any saved SHA.

## Actual merge and authorization boundary

The owner explicitly authorized merging the previously completed aggregate. **PR #34 is merged into master**, merge commit **8382a7ec05a64212ea0a237159dca427a0767425**, on 13 September 2026. The old master **c43c50d8d5d6775055f19bf018b52930a264d4a4** is preserved on `audit/backup-master-before-approved-merge-20260913`.

The merge preserved the earlier focused audit history. Some individual stacked PRs still appear open because they target other audit branches; their included source must not be merged repeatedly to change UI badges. The earlier reconciliation excluded only the unrelated capability-test PR #1 and the optional native-queue experiment PR #25. Verify ancestry again before closing or reconciling any PR.

This is a SOURCE merge, not an installed-bot update. No navigation DLL, mesh or installed files were replaced. The existing native-format compatibility issue and live acceptance gaps remain. The owner approval was for the completed aggregate, not blanket approval to merge future repairs or deploy the native candidate.

## Resume from the newer, unmerged follow-up tree

**Working branch: `audit/next-41-wholesome-postmerge-verification-20260913`.**

**Tested production/test commit: `c14bd264da90bdd384ebd9bf2bcc8f7b7a9a8217`.**

**Tested tree: `a1da20dc3ecbe98ae1fbc29986d49f7afaad0b17`.**

This tree contains master plus the following two independent follow-ups. Subsequent handover commits modify documentation only.

| Follow-up | Saved commit | Review state at handover |
|---|---|---|
| Preserve creature/game-object relation identity through scheduler, ancestor checks and generated profiles | `67a1a112d0e6a94bdf01984ef430de0e6a596100` | PR #35, open, not merged |
| Protect current accepted and scheduled quest items plus shared protected names/IDs before Wholesome selling | `8e8ebf4f623b16b3779a8fd6a3893b63611bb901` | PR #36, open, not merged |

Do not recreate those repairs. Read their diffs, tests and limitations, and build the next focused branch from this combined tree when both are required. Preserve their failing-before histories. Do not merge competing old fixture branches or silently promote optional experiments.

## Verified execution evidence

The complete archives below were recovered from GitHub Actions and their SHA-256, embedded source commit and results were checked during continuation. These are actual executions at the recorded commits, not newly invented results or live client sessions.

| Tested source | Run / artifact | Result | Archive SHA-256 |
|---|---|---|---|
| master `8382a7ec` | 34761250161 / 10319091671 | 13 integrated build/run entries exit 0; 99 Singular source files compile; 33 analyzer tests pass | `e3e1f818a656039c0df92c6cb0de452bce4b598a08cf1864b79ba0322ed24d89` |
| same master | 34761250147 / 10319221117 | 50/50 original-role query cases under Lua 5.1.5; 26/26 actual-helper adapter checks | `27c99a997dcfc5cccc7c4a7177087380d1dbb9668ba448190d71f4dbb5adfc35` |
| combined follow-up `c14bd264` | 34763063805 / 10319148422 | All 14 integrated entries exit 0, including 23/23 relation and 23/23 sale cases; 99 Singular files compile; 33 analyzer tests pass | `42ce9e2f1bffc18b7aac32cea1c6761c1d24ac976fc8c490a48c20de2d7a6121` |
| sale baseline `5b735d95` | 34762222832 / 10319506621 | 17/23 fail; six controls pass | `2a60da891f68d48d58f5416883b9d56973360aa92b93ce65389c315d05ec5bb5` |

Independent Windows host run **34763063883** also reports success at `c14bd264`. PR #35 retains the relation red/preflight/committed evidence. The combined suite has controlled world boundaries and compiled wiring checks, not exhaustive gameplay/branch coverage. It is not proof of original-client server acceptance. Counts can overlap between executables.

The sale regression extracts the actual checkout method and links the real protected-items manager; it does not reimplement the decision. The complete Wholesome class is compiled separately by the broader suite. Temporary source-preparation/write-token workflows were removed before the follow-up production commits.

## First unfinished dependency: complete quest observations and protection ownership

**Confirmed source information-loss path, not yet behaviorally reproduced:** `Styx/Logic/Questing/QuestLog.cs:101-125` walks 25 slots and drops null `GetQuest` results. A nonzero occupied slot reaches `PlayerQuest.FromId`; `Styx/Logic/Questing/PlayerQuest.cs:169-174` returns null when quest cache metadata is unavailable. An accepted quest can therefore be omitted from the materialized list without a completeness indication. Historical frequency is unverified.

This affects both scheduler and mutation permissions. In particular, a null-list fixture in the new sale suite does NOT prove the real list is complete. Preserve raw occupied quest identities independently from optional cached metadata; make incomplete/changed observations explicit and test before implementation. Do not infer abandonment, completion or permission to sell from a missing cache record. Keep the old public API compatible where possible. Recheck player/session/log identity at side-effect boundaries.

**Protection ownership:** `CollectItemObjective` adds an item to the shared runtime set and unconditionally removes it on disposal; `ProtectedItemsManager.Add/Remove` are plain set operations. A scoped owner must not release another owner's protection. Reproduce two simultaneous collectors, pre-existing manual protection, disposal order, duplicate disposal, cancellation and reset before introducing compatible leases. Also investigate reload clearing protection before successful file parsing.

These gaps are NOT closed by PR #36. It repairs missing current/shared exceptions at its caller boundary; it does not certify complete quest snapshots, mutations inside a native sale batch or all destructive inventory callers.

## Continuing verification, not another census

Read `docs/audit/2026-09-13/WHOLESOME_BEHAVIOR_VERIFICATION.md` for the coverage frontier and `POSTMERGE_GRAPH.json` for directed evidence links. Continue every active Wholesome behavior and the host owners it calls; distinguish source-reviewed, reproduced, regression-verified and live-unverified work. Keep a checked inventory so untested branches cannot disappear from the scope.

Priority after the two contracts above: merchant/mail/discard/consumption revalidation and acknowledgment; vendor data discovery/settings; restart/rest/merchant state; extension cancellation; prerequisites, unsupported/special/scripted objectives and bounded recovery; group roster identity; original-client APIs; remaining combat/support boundaries; native compatibility, route resources and validated lift alternatives. Do not quarantine valid quests or invent landing coordinates to make tests pass.

## Operational constraints and restart protocol

World of Warcraft **3.3.5a build 12340**, Windows x86, .NET 10, runtime-compiled extensions. Validate original Lua 5.1 APIs/return schemas, not retail or modern Wrath Classic. No live client is attached here. Full multi-tick target identity, active pets/persistent AoE, simultaneous Paladin/rank/locale/encounter behavior and physical transport support still need acceptance.

`mmaps/` is the installed LFS dataset; actual bytes were previously pulled and verified (6,054 files / 2,985,832,908 bytes). Pull needed tiles only for mesh-backed jobs and verify hashes. Managed tests do not need another 3 GB download. The checked-in DLL rejects inspected format-6 headers; the source-built candidate remains unpromoted. Partial/out-of-nodes is neither successful arrival nor proof of unreachability. PR #25 did not establish useful Grod-query speedup and remains excluded.

Read the original `CODEX_AUDIT_PROMPT.md`, `KNOWN_ISSUES_AND_HYPOTHESES.md`, `AUDIT_CONTEXT.md` and existing audit/graph evidence before extending scope. Preserve corrected counterevidence rather than trusting every original hypothesis. Use tests that fail for the intended defect, focused repairs, affected plus combined verification, and recorded hashes/PR links. Save a concise checkpoint after each coherent slice. If artifacts expire, rerun the pinned workflow instead of assuming outcomes. Independent review has not occurred; never label self-review as independent approval.

The long chat's failure does not undo committed work. Resume from GitHub, not unsaved analysis. See `NEXT_CHAT_PROMPT.md`. No new follow-up merge, force push, installation or binary replacement is authorized by this handover.
