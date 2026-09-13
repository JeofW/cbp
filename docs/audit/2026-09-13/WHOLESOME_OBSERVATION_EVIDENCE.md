# Wholesome carried-item and pickup observation repair

Continuation from checkpoint d62f2c7b73305087dd23f45d8e881ae38d3ab3e7. This slice does not replace quest datasets, remove prerequisites, or certify all quests. No new Lua command, client offset, native binary or mesh is introduced.

## Reproduced ownership defects

1. QuestScheduler.AddObjectiveWork checked source support before current completion evidence. An already-satisfied item with an unsupported unused source could acquire UnsupportedObjective failure evidence. Both CollectItem and CollectFromGameObject reproduce at the required quantity and above. The scheduler already used carried quantities by item ID; the repair changes validation order, not that working observation contract.
2. QuestPickupMismatchTracker used the full diagnostic string as its absence key. Offer ordering, duplicates, unrelated additions/removals and unrelated shown titles could reset the same requested quest/NPC absence indefinitely. The diagnostic snapshot remains ordered, copied and immutable; the semantic absence key is now requested quest + NPC + reason. Wrong-quest-shown evidence retains its existing independent behavior.
3. The tracker and WholesomePickupMonitor rejected only the most recent repeated cycle, allowing older replayed cycles to count or erase current evidence. Older cycles are now rejected before mutation; explicit reset/new owned generations still allow fresh numbering.
4. Unknown Wait and a positively identified target waiting for its button were conflated. The decision now carries positive target evidence. The real ForcedQuestPickUp adapter records it before waiting and clears its pending output when the current tracker clears. Unknown dialogs remain non-rejections; possession does not fabricate whole-quest completion or turn-in readiness.

## Actual test history

- Initial recovered 42ed7c6d suite had a Wholesome missing-using compile failure. That was a fixture defect, not a reproduced gameplay failure.
- Import repair 96652a10a0950721ab91d6b8957c9be57cf58ed9, run 34753740187, artifact 10316219740, SHA-256 29eb08e10ba2f5829e4357875a7d203be4bf5c0640e5b9571ddf743de702dbde: all eleven projects/entries built; pickup 5/10 and inventory/pickup 14/16. Earlier throwing module initializers prevented later new groups from executing.
- Recovered test groups were made to run through one aggregating entry per executable. A conflicting draft expectation that unrelated offers should reset the still-absent target was explicitly corrected before repair; see QUEST_FIXTURE_RECONCILIATION.md. No original wrong-dialog/reset assertion was removed.
- Test-entry run 34754155181, artifact 10316454705, SHA-256 cfde481c8ba44079fd807068bae81ce081a64e9712e348e717a1e3d865ba9e56: all five groups executed. Pickup observation 4/10, missing-offer evidence 8/16, Wholesome inventory/pickup 14/16, owned collection 4/8, inventory materialization 12/12. Its preparation step retained the expected nonzero shell exit; no source blobs were staged by that failed job.
- Controlled exact-source red/green run 34754559993 at b4ecce5b9f238d602551e70207f52f8bd0a902fd, artifact 10317165497, SHA-256 21fd708221cbae0893d7fddd70bdcd2323cfbed7793b7040a5d333ae892c0ae9: unchanged-source red repeats the same 20 failures; repaired source passes all 62 recovered cases and all eleven validation entries, including original quest/adapter/vendor/lifecycle/lift/Paladin/native-wrapper tests and Singular compatibility. The complete red/green artifact and verified-blobs.json were downloaded, hash-verified and inspected.

The 62 cases are 10 pickup observations, 16 missing-offer, 16 inventory/pickup, 8 owned collection and 12 inventory-materialization cases (with 12 quantity rows). They overlap in behavior and are not 62 unique quests or 20 independent bugs. No game was attached. The adapter wiring is source-inspected and covered by existing publication tests; the policy/monitor/scheduler cases execute actual owners against controlled observations.

## Source identity after repair

- QuestPickupDialogPolicy.cs: ea22cb939546da15fabad856f73d25a54aef168b
- ForcedQuestPickUp.cs: 920face77d065542452bd8496c2581504f98c610
- QuestScheduler.cs: 17911b4069a0e157e54b96a892e332aacbf87aa7
- WholesomeAutoQuest.cs: 9e34f658923b3f35cf997579d5321a2f4352af54

Temporary preparation scripts, patches and write-token workflows are removed by this promotion. The committed-tree execution is a separate check; preflight is not silently described as a final committed or combined-support execution.

## Remaining boundaries

Live NPC list readiness, original-client memory layout, inventory observation freshness, special/scripted quests, shared runtime item-protection ownership, broad data reconciliation and end-to-end quest travel remain acceptance/investigation work. Do not permanently quarantine all quests lacking static sources; an unmet unsupported objective must still report its genuine limitation. No native/Grod completion, all-command compatibility, all-quest success, independent review or deployment is implied.
