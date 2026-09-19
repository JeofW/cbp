# Resume at W71 — container submission + dependent prerequisites verified

Repo `jeofwong/CopilotBuddy-private`, draft PR51, branch `audit/next-55-equipment-observation-20260917`. Reconcile live refs first.

Verified code/test head **5db0de566cea515a21f3706fd7835b82dda9737a**, tree **f9b8a5cbb14ebad0a419ac1a53ff3ab04dafbad0**. Production prerequisite repair is **16acaaac108d817642993e48bfe25dc10958c298**.

Read `docs/audit/2026-09-19/W71_CHECKPOINT.md` and `W71_EVIDENCE.json`, then W70 plus `WOTLK_335A_RESEARCH_POLICY.md`, `TRINITYCORE_335_COMPATIBILITY.md`, `QUEST_DATA_PROVENANCE_335.md` and `ADDON_EVIDENCE_335.md`.

Exact green:
- integrated **35430867637 / art10580233586** — 17/17 entries
- host **35430867668 / art10580507599** — 0 errors / 3340 warnings
- production quest-log owners at 16acaaac: **35430209827 / art10580646500**

Retain:
- container slot identity **9/9** and `TryUseContainerItem` fail-closed GUID/slot/entry validation
- UseItemOn bounded safe-submission refusal; refusal is not invocation or quest credit
- dependent previous alternatives **14/14**
- active parent prerequisites **32/32**
- negative exclusive dependencies **10/10**
- plugin refresh **9/9**, GossipEvent **14/14**, Singular required registration **189/189**
- all older W70/W69 requirements and evidence

Dependent prerequisite semantics now follow pinned TC335 for `PreviousQuestsIds`: stored-order OR, negative group each-from-all, direct positive field independent, unknown predecessor metadata cannot authorize, ancestor correction follows only blocking roots. Dataset provenance is still not a realm DB certificate.

NEXT prerequisite slice: negative direct `PrevQuestID` status. Pinned TC335 8fda442f requires the parent to be `QUEST_STATUS_INCOMPLETE`; current Wholesome accepts any accepted parent including ready/completed-in-log. Pinned AC WotLK 8337a378 is broader. Test first using existing `QuestSchedulerAcceptedQuest.IsCompleted`; TC-primary conservative policy should require accepted + not ready. Do not silently erase the AC divergence.

After that continue cursor ownership separately: WoWItem.PickUp, delete, equip, auction and ProfessionBuddy cursor transfers are not one blanket contract. Then proceed with remaining buff/gear/water/GatherBuddy/native/live acceptance frontiers.

Do not merge PR51 without explicit user approval. For connector failures use exact SHA/run IDs, short one-shot reads and bounded retries; no blind reruns.
