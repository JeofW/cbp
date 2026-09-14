# W42 continuation recheck — runner diagnostic required

14 September 2026. Continue draft PR44 on `audit/next-42-scheduler-observation-20260914`, not the older publication handoff. Before this documentation write, PR44 head was `e22874dd01ed1f703785ad555f71b45304852917`; its base PR42 was `e97263eef2dbf1bc6b050d80d9c3e51232ce42e2`. Both remain open and unmerged. Re-read live heads before further work.

## Recovered work, not newly implemented

The interrupted continuations published additional raw/readiness and sale repairs on PR42 and the scheduler investigation on PR44. Preserve them. The publication-only root handover on PR42 is stale; PR44's `AUDIT_RESUME.md`, `NEXT_CHAT_PROMPT.md` and `W42_SCHEDULER_OBSERVATION_CHECKPOINT.md` describe the actual continuation. Do not recreate the existing tests or candidate.

This recheck downloaded and verified the complete final PR42 artifacts at e97263ee:

| Evidence | Run / artifact | Authenticated SHA256 |
|---|---|---|
| Focused current |34829563361 /10342115105|ed355d4d8554b753cb93f7b6f3427cd898db6f1e03c62c61e25ff175636c5cc5|
| Combined |34829563454 /10341284520|91c7c9d51d894245c8680b9c7ddd46d81bf6989f5fd60b5c26de1db114083310|
| Host |34829563382 /10341284229|831cfee1ad337adaedf9ac6e3cfcd3d569f395a1d540e7ee49fa4791dfc401f6|

Outer digests, ZIP CRCs, all 52 focused and 76 combined internal hashes, all result records and run logs were inspected. All 1689 working-source hashes and 32 normalized outputs match focused/combined after directory-layout mapping. The 53-file nested focused source export matches its working-source manifest. Host has four members and no internal manifest.

At that recovered PR42 source, all 17 combined entries build; 16 execute successfully and only Wholesome's retained scheduler-completeness group fails. Observation25/25, raw-ready16/16, ready5/5, sale-observation28/28, original sale23/23, publication21/21 plus acceptance9/9, completion15/15 and boundary24/24 pass. Host builds and explicitly runs no tests. These are recovered Windows executions, not new C# runs by this recheck. Do not confuse these PR42 counts with PR44's later 1690-input/33-normalized-output evidence.

PR44 already records 23 actual scheduler-publication scenarios at test source `35cc917435f74d33558d0250e566bcb7ad9a6641`: seven pass, sixteen intended assertions fail, zero unexpected errors. That evidence and the saved one-file candidate predate this recheck. The candidate is NOT compiled, executed, verified, promoted or deployed.

## Current blocker rechecked

The latest job returned for preflight run `34831727583` remains `103937050245`, completed/failure, with no steps or logs exposed. The latest commit check for source `9a84d797ec6a77f9967c68ffe2117946e10d6843` reports failure from 10:11:48Z to 10:11:51Z, one annotation, and null title/summary/text. Its explicit annotations URL is:

https://api.github.com/repos/JeofW/CopilotBuddy-private/check-runs/103937050245/annotations

The connector again rejected that URL with HTTP 400 INVALID_ARGUMENT: GitHub Fetch URL is not an allowed public GitHub repository or search endpoint. This is a connector URL restriction, not a GitHub 401/403 or a diagnosis of the runner-start failure. The annotation's contents are still unknown. No new blind workflow retry was launched.

Open the failed run in GitHub and obtain the displayed failure annotation:

https://github.com/JeofW/CopilotBuddy-private/actions/runs/34831727583

Do not infer billing, runner capacity, workflow policy, or token expiry without that diagnostic. Publishing actions are exposed; this legitimate documentation commit, once read back, provides current write evidence. Reconnecting the plugin is not the established remedy for this runner failure.

## Exact next action and boundaries

Obtain the runner-start error, address its actual cause, then retry the retained preflight once. Require the identical red fixtures, all 23 new and five retained scheduler cases, and every focused group before promoting the exact tested scheduler blob. Remove temporary preparation files only with verified promotion; then run focused, combined17 and host at the same committed repair. Preserve the existing unverified candidate and checkpoint meanwhile.

No production, test or workflow change was made by this recheck. No new C# execution, merge, deployment, native/mesh/runtime-capture/Lua change or Work/Codex switch. Master `8382a7ec05a64212ea0a237159dca427a0767425` and backup `c43c50d8d5d6775055f19bf018b52930a264d4a4` were reread and preserved. Keep PR34's prior merge, PR35/36, W42 ancestry and separate capability PR43; exclude PR25. XYZ/cliff/lift/wind-rider work, independent review and live acceptance remain open.
