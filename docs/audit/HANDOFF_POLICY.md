# Required handoff for every audit continuation

Owner instruction, 20 September 2026: always provide a usable handoff when finishing a continuation, including when the work is partial or interrupted.

Update AUDIT_RESUME.md and NEXT_CHAT_PROMPT.md against the reconciled live PR head without discarding earlier evidence. The final chat response must also include a copy-paste new-chat prompt and a downloadable UTF-8 handoff file; repository pointers alone are not sufficient.

Include the repository, PR and branch; exact published and tested commits; test/run/artifact results and their evidence limits; completed fixes that must not be recreated; remaining blockers and the next specific task; and merge status/authority. Preserve original WoW 3.3.5a build 12340, TrinityCore 3.3.5 primary/AzerothCore WotLK secondary, the approved scope and all prior exclusions.

The owner already conditionally authorises merging once all review blockers and final gates are satisfied. Do not ask again, do not merge a partial state merely because existing CI is green, and do not imply deployment or live acceptance from a source merge. Reconcile direct refs before writes; every write needs an explicit nonempty approved branch and expected blob/parent identity. Never write to master as a placeholder.

W80's completed 140-commit retained-source review is not to be restarted; continue its recorded remediation from the newest durable checkpoint. When evidence or tools are unavailable, state the exact limitation and preserve the last verified state rather than inventing completion.

## Operation outcomes and resumable publication

Added 21 September 2026 for the owner's enabled Goal. This section changes no tool permissions and does not promise uninterrupted execution.

- Record the intended repository, branch, expected parent and exact allowed paths before publication. Keep a request-content hash and returned commit/session identity. Use the approved official tools; do not access or print credentials.
- A successful mutation needs read-back of the commit parent, changed paths, file bytes and live branch. A command exit code or an outer tool response alone is not sufficient. Keep tested source distinct from later documentation commits.
- A returned running session means poll that session. A failed poll does not cancel or rerun the earlier command. Retained/background output belongs to its stated session, not automatically to the latest failed call.
- UNKNOWN_TOOL with an explicit not-dispatched result permits at most one bounded same-request retry after checking current exposed names. If still failing, perform one rediscovery and verify any alternative is a documented in-scope route. Do not cycle indefinitely among names or helpers.
- After a timeout, partial result, interrupted wrapper or other ambiguous mutation, read remote state before any repeat. If the intended change exists, verify it and continue without duplication. If the branch moved differently, reconcile; do not overwrite, force-update or assume that a request identifier is a server-side deduplication guarantee.
- Stop the affected operation on an explicit safety refusal, authorization/permission denial or unchanged rate-limit block. Do not disguise the payload, switch accounts/routes to evade it, weaken permissions or retry until accepted. Record the exact blocker and continue independent authorized work where possible.
- Distinguish application-domain refusals and expected regression failures from tool-layer failures. A deliberately refused native interaction in a passing fixture is not a rejected GitHub publication. Cite the actual results rather than classify messages by the word refused alone.
- Publish related handoff pointers/checkpoint/evidence together where supported. Prefer reviewed UTF-8 document files and an official CLI request body over a large quoting-sensitive inline command; this improves inspectability, not authorization. Keep a bounded local receipt outside the repository and verify after interruption before recreating anything.
- Update the Goal frontier after a verified slice. Do not mark COMPLETE or MERGE_READY merely because the source review/CI portion is green. Keep native/original-client/server and independent-review gates explicit; pause rather than loop on a hard blocker.
