# Original-client quest availability conditions

This smaller continuation implements complete source-bound pickup conditions for 58 quests, containing 115 predicates. A quest that requires another quest to be rewarded is withheld until the matching completion history is authoritative. The generated pickup profile loses execution permission when those observations change or become unknown. Already accepted objective work and turn-in are not gated by new-pickup conditions.

The branch starts from PR65 `d36bddce5600bb67418c8180a38fdb8c40346f61`, in `D:/Dev/CB-QuestConditions-20260930`, branch `audit/next-quest-conditions-20260930`. Resolve the containing candidate SHA through Git; clean-commit acceptance and publication receipts are external. Production remains the verified PR61 master `3bc97e1e0b446aede269f7414c0c7c6358fdc192`.

## Evidence and implementation

Pinned TrinityCore3.3.5 revision `95657f54779467effea8a1749a61ff93abc1d707`, TDB335.25101, supplies the full conditions table and the exact condition/status semantics. The source SQL SHA256 remains `e72c0105ca27779ea3b08792b247210a44d9004fc6ab55cd1b0099d3b10779a9`. SourceType19, SourceGroup0, SourceId0 and player ConditionTarget0 identify the supported quest-availability owner. Within an ElseGroup every predicate must pass; alternative groups are ORed. Unknown observations remain unknown after negation.

Supported predicates are QUESTREWARDED8, QUESTTAKEN9, QUEST_NONE14, QUEST_COMPLETE28 and QUESTSTATE47. Taken means incomplete status3, not any accepted quest. Complete requires raw complete status1 and no reward. State masks retain NONE0, COMPLETE1, INCOMPLETE3, FAILED5 and REWARDED6; a reward mask is checked independently of an accepted state. Only ordinary QuestType2, nonrepeatable and nonseasonal referenced quests are admitted to this permanent-history contract. Other lifecycles remain unmodeled.

The loader accepts the optional typed QuestAvailabilityConditions repair member only after exact source/data binding and strict schema validation. It rejects duplicate subjects, groups and predicates, unknown fields/types, invalid masks/negation, missing reference metadata and unsupported history lifecycles. Base JSON cannot inject this internal metadata. All conditions are validated before the cloned model is published, and the existing repair hash participates in execution identity.

The scheduler captures raw accepted/complete/failed flags separately from computed quest completion. Its existing complete-log and authoritative-history gates remain. The condition decision participates in pickup admission, publication acceptance and the running profile's execution permission. The current player GUID, memory owner, in-world validity and raw log identity are checked around each fresh observation. Debug-only bounded/rate-limited diagnostics now include each condition's exact group, predicate, reference ID, raw state, reward authority, result and source reference. Existing ownership, cancellation, recovery, navigation and zero-work diagnostics remain.

Only the 58 complete source contracts are appended. All previous data, geometry, scoped credit sources, delivery/supplemental contracts, dependencies, relations and objective repairs are unchanged. Only the vetted9066/9447 strategies remain; their recipes are unchanged and rebound to the exact new repair bytes. This reference does not establish the customized live realm's configuration.

## Read-only IDA verification

The enabled IDA MCP was freshly verified against the 32-bit original build12340 binary SHA256 `bf644876709c591acc17c0da8cdf1814edcc9f1e6bc109a8c0d5c38c79dc953c`. Registration and decompilation receipts are retained in source-contracts.json. GetQuestLogTitle at0x5E5CC0 supplies the quest ID as its ninth result, but its completion result can use a computed helper; it cannot replace raw server-state observations. The failed flag has priority over a retained completed flag, as also observed in the server's fail transition.

GetItemCount at0x51C2E0 has separate bank/charge options and returns zero when no valid player is present. No bank/item condition was added from that partial semantic evidence. Exact GetQuestID and GetQuestLogIndexByID registration names were absent from the inspected binary, so no later-client API was invented. The implementation reuses the already validated raw QuestLog and authoritative completion-history owners and adds no native entrypoint. IDA was not used to infer server SQL or scripted quest recipes.

## Exact coverage and tests

| Classification | PR65 | This continuation |
|---|---:|---:|
| GENERIC-PROVEN | 2,963 | 2,999 |
| STRATEGY-PROVEN | 2 | 2 |
| DATA-INVALID/INCOMPLETE | 932 | 932 |
| SOURCE-UNCERTAIN | 365 | 329 |
| LIVE-ACCEPTANCE-REQUIRED | 16 | 16 |
| UNSUPPORTED-SCRIPTED | 57 | 57 |
| Total | 4,335 | 4,335 |

Exactly36 quests move from source-uncertain to generic-proven. The 1,334 remaining IDs all have regenerated source correlations; secondary obligations remain separate. The ledger's uncompressed SHA256 is `2ccc4db738e8f89f5cf74b00232cb3b0077a5f8fd8cc6ebba0b6d7e63e0933b7`. Of58 condition-supported quests,39 have controlled full pipelines; three of those still have other source obligations, so only36 change top-level classification. Nineteen retain blocked pipelines and their other exact obligations.

The corrected runtime red reproduced24 intended assertion failures with zero unexpected exceptions. The repaired40-case loader/scheduler suite passes. Seven additional actual-memory/history/scan/profile-load/execution-lease cases pass, including revocation on lost/unknown history, change during load, raw failed/complete precedence and fresh-scan recovery. Existing publication and raw-observation regression groups also pass. The first test build and earlier mixed red failure remain separately retained as harness corrections, not behavioral proof.

Ten source-exporter tests and eight receipt-validation tests add group, scope, history-kind, malformed/partial contract, stale-contract and missing/duplicate/failed case coverage. The full analyzer run has305 tests with one expected Windows permission skip and no failures. Across all4,335 rows the actual Windows/x86 owners pass259,156 checks; both vetted strategies pass39. These include671 explicit availability checks covering every referenced quest in all five original states, group/negation rules, missing-observation revocation and source/prerequisite fixture consistency. Two source-stable diagnostic runs reproduce identical simulation/model/dependency/strategy bytes.

Controlled satisfying states are selected from the pinned source truth table while preserving existing prerequisite constraints. These states exist only in the test harness; production never receives synthesized rewards or quest progress. Simulation PASS establishes bot behavior under supplied observations, not live travel, combat, phase availability or server completion.

Final clean-candidate validation must reproduce the retained fixture through the four-stage quest-closure runner, all34 local commands and the applicable hosted Windows/x86 workflows. Hosted integrated validation has17 suite records; those are different accounting units. Raw source/run/job/status/artifact receipts are stored externally to avoid a circular commit identity. No unexecuted hosted or publication result is claimed by this pre-acceptance report.

## Remaining work and separate publication block

The source frontier contains125 quests and263 availability rows. This implementation accounts for58 complete contracts; the other67 retain exact reasons in remaining-condition-obligations.json. They include primary-versus-dataset conflicts, repeatable/seasonal history, item and bank observations, daily completion, spell/skill/area/level predicates and unsupported scope. A supported subset never erases the remaining rows of a condition group. Continue these families only after establishing their complete source and current-observation contracts.

The four reputation repairs are preserved independently in `D:/Dev/CB-QuestEligibility-20260930`. That sibling adds faction922 thresholds for9145/9155/9173/9192 and has its own1369-ID ledger, not the condition branch's1334-ID ledger. Its exact commit transaction was rejected twice by the provider safety-status check and stopped; see external ELIGIBILITY_COMMIT_PROVIDER_BLOCK_20260930.json. It has not been copied into this branch or declared published. Reconciling the two approved slices later requires actual source/dataset regeneration, not adding their independent counts.

Continue the exhaustive audit from this branch's exact remaining IDs. Preserve PR61 deployment and separately scoped PR62–65, source-bound data, current inventory/history/progress, originalbuild12340, TC335primary/ACsecondary, and all live/source/script obligations. No4,335-of4,335 live-completion claim is made.
