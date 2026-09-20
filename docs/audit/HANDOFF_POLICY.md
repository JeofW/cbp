# Required handoff for every audit continuation

Owner instruction, 20 September 2026: always provide a usable handoff when finishing a continuation, including when the work is partial or interrupted.

Update AUDIT_RESUME.md and NEXT_CHAT_PROMPT.md against the reconciled live PR head without discarding earlier evidence. The final chat response must also include a copy-paste new-chat prompt and a downloadable UTF-8 handoff file; repository pointers alone are not sufficient.

Include the repository, PR and branch; exact published and tested commits; test/run/artifact results and their evidence limits; completed fixes that must not be recreated; remaining blockers and the next specific task; and merge status/authority. Preserve original WoW 3.3.5a build 12340, TrinityCore 3.3.5 primary/AzerothCore WotLK secondary, the approved scope and all prior exclusions.

The owner already conditionally authorises merging once all review blockers and final gates are satisfied. Do not ask again, do not merge a partial state merely because existing CI is green, and do not imply deployment or live acceptance from a source merge. Reconcile direct refs before writes; every write needs an explicit nonempty approved branch and expected blob/parent identity. Never write to master as a placeholder.

W80's completed 140-commit retained-source review is not to be restarted; continue its recorded remediation from the newest durable checkpoint. When evidence or tools are unavailable, state the exact limitation and preserve the last verified state rather than inventing completion.
