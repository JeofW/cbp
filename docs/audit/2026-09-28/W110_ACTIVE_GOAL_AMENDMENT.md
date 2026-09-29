# Active W110 Goal amendment - 28 September 2026

Authority: the user's latest explicit instruction in the current continuation. This supplement takes precedence over the older generic no-identical-retry wording for NEW operations only. It does not edit or erase the original saved Goal, frozen evidence, or any historical refusal.

## User's amended provider-refusal rule

For any NEW tool operation that returns exactly:

> This tool call was blocked by OpenAI because we couldn't determine the safety status of the request.

Retry the EXACT SAME call unchanged up to 3 total attempts. If attempt 2 or 3 succeeds, continue normally and record the earlier refusal as intermittent/provider-side. If all 3 identical attempts are refused, stop, preserve all three refusals and leave that operation unverified.

Do not rephrase, fragment, rename, wrap or reroute the operation to evade the refusal. Verify ambiguous mutations before retrying.

This supersedes the Goal's previous generic instruction saying not to identically retry a newly refused operation. It does NOT unfreeze historical W104-W110 transactions already explicitly preserved as "do not replay" unless the user separately authorizes that specific historical operation.

This bounded retry rule also applies to exec_command, write_stdin, artifact retrieval and session_finish.

## Execution safeguards and scope

- Three means the initial attempt plus at most two unchanged retries, not three additional attempts. Keep the exact tool name/arguments and all returned outcomes. Stop sooner if independently observed execution makes repetition unsafe or unnecessary.
- A successful retry does not prove why the provider refused the earlier attempt or that any earlier call executed. Record the observed intermittent outcome, not a guessed safety/transport root cause.
- A timeout, partial response or ambiguous mutation is not this exact refusal. Reconcile its actual side effects before any repeat; do not duplicate a successful publication, file mutation or finish event.
- All historical IDA-health, native-map, old document-package, ref/status, watcher/download, comparison and finish refusals remain frozen. This amendment does not authorize creating an equivalent historical output by another route.
- Original build12340/core/provenance policies, the no-subagent/local-project-execution/production-CB/native-mutation/merge restrictions and numeric preparation budget 3 historical / 0 new / 0 remaining are unchanged. The retry count is not a numeric-preparation budget.
- Use session_finish only at a genuinely completed current-work checkpoint. New instructions extend W110, not a reason for a progress or queue-collection call. HELD is not RELEASED; no ordinary final before actual release or manual stop.

Original saved Goal: Library Pasted text(2).txt, version 1, file_0000000098388230b420e8084c0b4ae9, read in full again. The present amendment is the active execution authority; the current eight-function Core schema has no saved-Goal editor, so no separate CoS UI persistence action is claimed. Retain this amendment in LATEST_CONTINUATION and the next relevant published instructions so subsequent continuations see it before older generic rules.
