# GitHub connection recovery — retained owner instruction

Updated: 14 September 2026. Repository: `JeofW/CopilotBuddy-private`.

## Current recovery sequence — supersedes older reconnect-first handovers

The owner's latest explicit instruction is authoritative. When private-repository reads continue working but branch/commit/PR actions become unavailable:

1. Perform one bounded action-discovery/private-read check. Do not infer publishing from a previous conversation, tool counts, or account push/admin metadata.
2. First retry with an explicit **@GitHub** invocation in the current conversation. Rediscover the legitimate publishing action once and invoke it when available; do not misuse GET for writes.
3. If publishing actions remain absent, preserve/update the exact handoff state. Continue in a **fresh ChatGPT conversation with the GitHub plugin selected**, carrying the current checkpoint and evidence. The assistant cannot open that conversation for the owner or promise work after this response.
4. **Reconnect the GitHub plugin only if that fresh conversation also lacks publishing capabilities.** Do not repeatedly ask the owner to reconnect in the same conversation. When they already completed a requested step, acknowledge it and perform the bounded retry rather than repeating the request.
5. Stop the blocked publishing attempt promptly if the bounded recovery still fails. Report the exact missing action or actual error, plus any local-only changes. Do not restart the audit, recreate saved fixes, claim a commit that did not succeed, or spend another extended session rediscovering the same limitation.

An explicit @GitHub retry, fresh conversation, or reconnection is a recovery attempt, not a guaranteed repair or proof of token expiry, a stalled connection, or inadequate repository permissions. Actual 401/403 responses must be distinguished from actions absent from the conversation.

## Evidence and capability boundaries

Native connector access and container Git/CLI access are separate. Container DNS failure, absent gh, or missing local .NET do not establish that an exposed native GitHub writer is unavailable. Conversely, reads and account permissions do not prove writes. Demonstrate publishing with an authorized substantive write, branch readback, and appropriate PR verification.

For C# execution, inspect actual Windows x86 workflows at the intended commit and their complete artifacts. No workflow-dispatch action was present in the last observed 89-action surface; existing push-triggered workflows supply the execution path. Do not infer dispatch from push access.

Never request or export tokens in chat/artifacts, print credentials, use a GET-only endpoint for writes, weaken protections, or introduce token-export workflows as a workaround. Ordinary repository-scoped workflow authentication must never be exposed outside its intended GitHub operations.

## Current continuation and authorization boundaries

Always re-read current PR heads, newer PRs, and committed checkpoints before using an attached ZIP. A failed/interrupted UI response does not establish that a remote write failed. ZIPs are historical/local transfer artifacts unless the exact files and executions are independently reconciled with GitHub.

Master `8382a7ec05a64212ea0a237159dca427a0767425` and backup `audit/backup-master-before-approved-merge-20260913` at `c43c50d8d5d6775055f19bf018b52930a264d4a4` are not write targets. PR34 is already merged. Preserve PR35/36 and the existing W42 stack, including PR38/39/40/41/42 and verified successors. Do not remerge stacked paths or recreate their repairs. PR25 remains excluded. The capability-test PR43 is not an audit dependency and must remain unmerged.

No merge, force push, deployment, installed-bot change, Navigation.dll replacement, mesh download for managed tests, or runtime-capture modification is authorized. Keep source-confirmed, controlled-reproduced, regression-verified, and live-unverified claims distinct. Link this runbook from every updated root handover; this sequence overrides reconnect-first text in older preserved checkpoints and ZIPs.

## Retained historical observations — not current instructions

Earlier continuations observed 48 read-oriented actions and later 89 actions including native writers. Successful prior publications include recovery note `b770d035b161ccee87233b9810f73ccac2f74039` and documentation checkpoint `0f4fee6058ce4a60af00b42cb7f40395bfd0a034`. The owner reported that reconnecting appeared to restore writes; causality remains unproven.

The publication plan was already committed at `99c8165ee5bcd7fce28cc4f2abd8ffc0e36702ca`, following the scan-entry repair/test source `01ec5542448ae5b5087fccd4ca2f17c61906e6d5`. A later read in this continuation found PR42 at `243a444a6292cbc48906f0f062a4e021f01374ab`, with an additional runbook note and the existing `QuestPublicationRegressionTests.cs`. That three-commit delta changes no production or workflow file. Preserve those tests rather than recreate them.

Recovered current-variant Windows run34811485458/art10334368660 at243a444a reports publication8/19,11 assertion failures,0unexpected. Its authenticated outer SHA256 is `8048398c68ff143797ca1bafdeeb9fc438c6bb5a78ade865c1d1a2b4ebc2a7e4`; complete archive CRC and all47 internal manifest entries were checked in this continuation. This is recovered execution, not a newly launched run or a completed repair. Full W42 and the exhaustive audit remain open.
