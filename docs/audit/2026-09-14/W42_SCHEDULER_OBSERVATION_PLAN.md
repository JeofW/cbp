# W42 scheduler observation continuation

14 September 2026. Continue the owner's approved evidence-led audit; no merge or deployment.

## Reconciled starting point and isolation

PR42 moved from 1d08c16ce29c4901e60c11fa4c5d8e3d9a598fda to e97263eef2dbf1bc6b050d80d9c3e51232ce42e2 while this continuation was inspecting it. The latter already promotes the two verified completion/publication fixture fixes and removes the temporary paired workflow. Do not recreate that work or attribute it to this continuation.

This continuation created audit/next-42-scheduler-observation-20260914 at e97263eef2dbf1bc6b050d80d9c3e51232ce42e2 to avoid overwriting the still-advancing PR42 branch. Review this as a new stacked slice against PR42, not master. Re-read parent heads before final handoff; never silently overwrite another continuation's checkpoints.

Recovered paired run34829035555/art10341398183 at1d08c16c has authenticated SHA256 73ec9bb867fbad1a6115b2cfa5a56e8387c18bd6cf89bafe92e889f5851f9971. This continuation downloaded and checked the complete113-file archive,112 outer-manifest entries and both50-entry inner manifests, source identities, all four project outcomes,32 identical normalized outputs and1690 source/config hashes. Only WholesomeAutoQuest.cs differs red/green. Sale11/28 red (17 assertions,0 unexpected) versus28/28 green; original observation23/25 versus25/25; boundary16/24 versus24/24. Publication21/21+acceptance9/9, completion15/15, raw18/18, raw-ready16/16 and ready5/5 remain. Scheduler2/5 is the only failing normalized owner group in the repaired variant. Local-only paired commits are not published commits. This is recovered execution, not a new C# run by this continuation.

Preserve the already-published raw/readiness repair1bee278c, sale repair1a74f798, publicationc237ca5b, completion and earlier scan repairs. The PR42 root handovers still point at86ba6832 and must not be mistaken for the live source frontier.

## Goal and owner design

The existing ScanAndRefresh still consumes GetAllQuests, which can omit occupied entries. MaterializeSchedule treats null AcceptedQuests as empty and has no input-completeness contract. Thus adding a raw snapshot owner elsewhere has not closed this scheduler consumer.

Use the existing real QuestLog.CaptureSnapshot and IsSnapshotCurrent APIs in the actual scheduler. Keep identity and hydrated metadata completeness distinct. Require complete observations before selecting work; revalidate the same observation before publishing output/loading and before final execution authorization. Preserve the real refresh lease at every mutation and after reentrant host events. An obsolete continuation must neither overwrite nor revoke a replacement. Missing observations must retain conservative item protection and bounded retry, not grant pickup or validated-grind permission.

Retain the public pure-snapshot scheduling API: explicitly constructed complete snapshots default to complete for compatibility, while the live producer sets completeness from actual raw observations. Null or explicitly incomplete accepted input is rejected before completion marks, recovery evaluation, data-failure reports or navigation probes. Do not add an unused property merely to turn missing-contract tests green.

This slice covers acquisition and changes observed during scan/publication, including an already-running child while that scan fails. It does not claim ongoing session/ABA provenance, atomic native reads, all later out-of-scan changes, or an atomic transaction over every host/recovery side effect. A future continuous execution-observation gate must preserve combat/service behavior: the current outer gate also encloses the combat tree, while RunPendingRefresh defers in combat. Do not casually add a per-tick whole-root veto that can prevent combat while a refresh is blocked.

## Test-first work

- [ ] Retain every original assertion/control, all30 publication cases and the existing five scheduler tests.
- [ ] Supply real matching object/descriptor GUID bytes in the existing allocated publication fixture, without replacing production readers or weakening its deliberate invalid-XML case. Verify that fixture-only change before using it as clean-red input.
- [ ] Add actual raw-memory -> ScanAndRefresh -> materializer -> XML/file -> DoScan/ProfileManager -> running-gate cases. Cover missing metadata, duplicate/invalid IDs, unreadable raw descriptors, mismatched/zero owner GUID, same-count/progress/owner changes during navigation and XML preparation, changes during real loader events, cancellation, stale/replacement generations, recovery after uncertainty, normal empty/turn-in and validated-grind controls.
- [ ] Execute a current-variant unchanged-production red baseline on Windows x86. Distinguish behavioral assertions from missing contracts and fixture failures; require zero unexpected failures in new cases. The matrix's historical pinned baseline is not this baseline.
- [ ] Implement only the justified scheduler repair, using the existing owner APIs and lease. Keep final clean-red fixtures identical to repaired-source fixtures.
- [ ] Verify repaired focused, actual combined17 and host at one committed source; inspect complete artifacts, exact source identities, every result and internal/outer hashes. Preserve any remaining failures honestly.
- [ ] Publish checkpoint/evidence/directed graph, exact new-chat pointer and independent-review request; provide a verified downloadable handoff. No independent or live review is claimed by self-review.

## Protected state and retained requirements

Master8382a7ec05a64212ea0a237159dca427a0767425 and backupc43c50d8d5d6775055f19bf018b52930a264d4a4 were reread unchanged. Preserve PR34's prior merge,35/36, the entire W42 stack and separate unmerged capability-test43; exclude25. No merge, force push, installed-bot/Navigation.dll/mesh/runtime-capture/Lua change, credential export, GET write or Work/Codex switch.

Keep QUEST_TRAVEL_COVERAGE.md authoritative for the owner's XYZ, cliff, lift-lifecycle and pickup/turn-in wind-rider concerns. Those remain source-traced requirements, not fixes delivered by this scheduler-observation slice. Retain true session/frame provenance, later executing-plan freshness, protection lifecycle, merchant acknowledgment and the broader post-W42 dependency order.

Recovery: explicit @GitHub retry first; if writers remain absent, preserve the exact checkpoint and use a fresh ChatGPT conversation with GitHub selected; reconnect only if that fresh conversation also lacks publishing. Do not repeat the old reconnect loop.
