# GitHub connection recovery — retained user instruction

Recorded: 14 September 2026. Repository: `JeofW/CopilotBuddy-private`.

## Remind the user before another blocked continuation

The user reports that disconnecting/reconnecting the GitHub plugin restored the missing publishing actions after several blocked audit continuations. Treat reconnection as a practical recovery step to try, NOT as a proven diagnosis of token expiry, a stalled connection, or a guarantee that permissions will return.

If a future session unexpectedly loses GitHub access, exposes only read actions, or cannot publish work that previously succeeded:

1. Perform ONE bounded check of the current connector actions and a private-repository read. Do not infer the current action surface from an earlier session or from account push/admin metadata.
2. Explicitly remind the user: **Please reconnect the GitHub plugin, then ask me to retry in this conversation. Reconnecting appeared to restore the write actions previously.** The assistant cannot perform or assume completion of the user's authorization step.
3. After the user reconnects, rediscover the actual actions once and retry the legitimate blocked operation. If the tools remain stale, report the exact missing action/error rather than repeating a long read-only audit. A newly opened conversation may be tried only with this durable checkpoint, not as another broad-audit restart.
4. Keep native connector access separate from container Git/CLI access. Container DNS failures, absent `gh`, and missing local .NET do not establish that a working GitHub connector writer is unavailable. Conversely, reads and permission metadata do not prove writes.
5. Demonstrate recovery with an authorized substantive branch/file commit, read the branch back, and verify the appropriate PR result. For C# execution, verify actual Windows x86 workflow runs on the intended commit and inspect complete artifacts. Tool discovery alone is not publishing or test evidence.
6. If that single retry remains blocked, stop implementation, preserve the exact frontier and error, and ask for the necessary connection/permission repair. Do not fabricate progress, recreate saved fixes, or spend another extended session inspecting code with no execution path.

Never request tokens in chat, print credentials, use a GET-only endpoint for writes, weaken workflow/repository protections, or use temporary token-export workflows as a workaround.

## Continuation and authorization boundaries

Always re-read current PR heads and committed checkpoints before using an attached ZIP. The ZIPs are historical/local preparation unless the exact files are independently found committed. Preserve already-published work, later corrections, and failing tests.

Master `8382a7ec05a64212ea0a237159dca427a0767425` and backup `audit/backup-master-before-approved-merge-20260913` at `c43c50d8d5d6775055f19bf018b52930a264d4a4` are not write targets. PR #34 is already merged. Preserve #35/#36 and the existing W42 stack, including #38/#39/#40/#41/#42 and any verified successors; do not remerge older stacked PRs or recreate their repairs. PR #25 remains excluded. No merge, deployment, installed-bot change, Navigation.dll replacement, mesh download for managed tests, or runtime-capture modification is authorized.

The exhaustive audit/refactoring work remains in progress. Maintain source-confirmed, reproduced, regression-verified and live-unverified distinctions. Add a link to this runbook in current handover documents whenever updating them.

## Retry observation and stale-handover safeguard

On the subsequent 14 September retry after the user reported reconnecting, action discovery exposed 89 GitHub actions, including native file/GitDB/PR writers. Earlier conversation reports described 48 read-oriented actions. These counts describe observations, not a stable API contract or proof of why the surface changed. Do not diagnose token expiry or claim reconnection caused recovery without evidence.

The live repository already contained PRs #40, #41 and #42 beyond the uploaded PR #39 completion checkpoint. Therefore, do not resume from #39 automatically, repeat its repair, or normalize tests already normalized in #40. Read the latest open PR and its committed source/checkpoint, inspect its actual runs, and extend that frontier. Existing PRs, commits and historical runs belong to their original continuations; a successful read does not make them new work by the current session.

When the user has already reconnected, acknowledge that fact and perform the bounded retry; do not repeatedly ask for the same reconnect without a new actionable error. Preserve an exact handoff even when the UI reports an interrupted or failed response, because a remote commit may already have succeeded.
