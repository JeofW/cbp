# Repair ordinary chest objective identities

This continuation corrects 46 imported GameObject objective identities across 35 quests. It starts from PR63 head `763c51e47b916f8303d10b6f77595d7a1feb3820` on branch `audit/next-quest-object-identity-20260930`, worktree `D:/Dev/CB-QuestObjectIdentity-20260930`. PR61 remains merged and deployed at master `3bc97e1e0b446aede269f7414c0c7c6358fdc192`; PR62 and PR63 remain separate open review milestones. This work does not change production.

## Concrete defect and repair

The imported actor number sometimes identifies a chest's loot selector instead of its `gameobject_template.entry`. For example, the old objective 1687 for quest 64 points to a sign in the primary reference. The actual required-item chest is found through the distinct loot-selector namespace. The audit now has exact item/count, chest template and loot-edge evidence for the selected replacement. The original historical exporter has not been recovered, so this is a demonstrated namespace mismatch, not a claim about which historical code produced it.

`quest_object_identity_335.py` requires an ordinary collection objective and an exact unique primary chest template whose `Data1` equals the imported actor number. It rejects a currently valid or scripted old actor, competing chest templates, changed required item/count, conflicting quest fields, special cast/event ownership, another repair on the same row, a duplicate target owner, and candidate script/event/trap ownership. Conditional loot, absent source items, nondefault loot modes and wrong reference groups cannot become ordinary acquisition proof.

Only missing geometry with conservative primary spawns is added: map and XYZ must be valid, spawn mask must include the ordinary mode, phase must be 1, respawn must be positive, and event/pool/scripted spawns are excluded. Existing geometry and negative observations are preserved. Static positions do not establish navigation, phase, lock or profession access on the live realm.

The existing source-bound repair pack gains an optional `GameObjectObjectiveRepairs` section. Each row binds quest ID, row index, objective index/type, expected old object, new object, required item/count and source reference. The actual loader validates it on the isolated clone and rejects changed expected values, duplicate ownership and overlap with a count repair. Only the effective objective ID changes; the original dataset bytes remain unchanged. All repair bytes still participate in the execution fingerprint, and strategies remain bound to the exact new repair hash.

The 46 corrections add 40 GameObject entries and 212 points, producing **730 entries / 14,293 points**. All earlier spawn records, 645 delivery contracts, 154 supplemental supplied-return contracts, 369 external dependency records and two relation additions are preserved. The three existing recipes still cover only quests 9066 and 9447. `object-identity-patch.json`, `object-identity-review.json.gz` and `repair-delta.json` contain every exact changed ID and its source evidence.

## Exact outcome

| Primary classification | PR63 | This continuation |
|---|---:|---:|
| GENERIC-PROVEN | 2,924 | 2,950 |
| STRATEGY-PROVEN | 2 | 2 |
| DATA-INVALID/INCOMPLETE | 945 | 945 |
| SOURCE-UNCERTAIN | 391 | 365 |
| LIVE-ACCEPTANCE-REQUIRED | 16 | 16 |
| UNSUPPORTED-SCRIPTED | 57 | 57 |
| Total | 4,335 | 4,335 |

Exactly 26 quests move from source-uncertain to generic-proven under controlled observations: 49, 53, 64, 134, 299, 335, 347, 365, 399, 450, 517, 529, 580, 662, 666, 689, 978, 1187, 1188, 1197, 5062, 5065, 8503, 9802, 9882, 12930. Nine repaired quests retain other obligations; repairing an objective does not remove their unrelated source/data requirements. No previously proven row is demoted by this slice. `closure-review.json` names those nine quests and the precise remaining flags.

All **1,383 remaining IDs** have regenerated source correlations. Primary classifications are a mutually exclusive partition of all 4,335 original IDs; the secondary obligation index intentionally overlaps. The uncompressed ledger SHA256 is `3bee889110cecc6c87c891a92e2ceac5124b30af69171fe65326d92d22aaf086`. The base dataset remains `f2ca79318694afaa4214fc09392e88c3d9d1eae5128f323d0d65b1b9e91a7e3f`.

## Actual tests and acceptance requirements

The actual loader/profile fixture ran before implementation: 20 cases, four intended assertion failures, zero unexpected errors. The same C# fixtures now pass 20/20, including generated-profile target identity, source cloning, fingerprint changes and late rejection. The focused acceptance ran five actual groups covering the new loader, existing pack validation, knowledge loading, supplied return items and delivery policy. The complete Python analyzer suite ran 268 tests with zero failures and one expected Windows permission skip. The first Python red asserted the absent exporter; its synthetic loot fixture then gained explicit LootMode/QuestRequired fields, so Python red/green byte identity is not claimed.

The actual Windows/x86 owners completed **258,279 dataset checks** and **39 strategy lifecycle checks** with no failures. Initial dirty-tree outputs are comparison fixtures only. `closure-fixture-manifest.json` binds the four shipped knowledge files, original controlled observations and exact expected model/dependency/simulation/strategy hashes. Final acceptance must reproduce those outputs at the clean containing commit and pass the full local 34-command pipeline and applicable hosted Windows/x86 workflows. Final full SHA, run/job/artifact and publication receipts are retained externally rather than introducing a self-referential commit identity.

The build has existing compiler warnings; zero compilation errors is not a zero-warning claim. Native navigation meshes and actual game completion are not exercised by these tests. Review here is direct implementation review, not an independent reviewer or supervised realm acceptance.

## Newly enabled IDA verification

The enabled MCP endpoint was checked against the 32-bit original client input SHA256 `bf644876709c591acc17c0da8cdf1814edcc9f1e6bc109a8c0d5c38c79dc953c`, image base 0x400000. All new analysis is read-only. `ida-api-review.json` records exact registered functions, traced consumers, raw receipt hashes and limitations.

`GetQuestLogTitle` at 0x5E5CC0 returns the quest ID in the ninth ordinary result; the existing profile helper and its regression already consume it correctly. `GetAvailableQuestInfo` returns three flags and the gossip list returns five fields per offer, without a quest ID. `IsQuestCompletable` checks current dialog state and live required-item counts, so it cannot replace an attributed per-quest completion observation or completed-history authority. `GetQuestLogCompletionText` is text, not a completion Boolean.

`GetQuestLogRequiredMoney` is a useful potential diagnostic input, but its zero result also covers invalid or missing cache identity. `GetQuestLogSpecialItemInfo` exposes an indexed special-item link/icon; its third value was traced to an item field and is not adopted as an ordinary stack count. No guessed raw native ABI, universal item-use recipe or new production API is introduced. The existing numeric GameObject mapping address is 0xA36F30; comments stating 0xA38F90 are inconsistent, but the executable address was not changed without consumer proof.

These findings identify safe existing observations and constrain future API work. Client disassembly does not prove server SQL, the customized realm, fresh dialogue attribution or actual quest completion.

## Source and remaining work

Primary authority remains TrinityCore 3.3.5 commit `95657f54779467effea8a1749a61ff93abc1d707`, checksum-verified TDB335.25101 SQL `e72c0105ca27779ea3b08792b247210a44d9004fc6ab55cd1b0099d3b10779a9`. The verified GameObjectData.h, LootMgr.cpp and ConditionMgr.h receipts remain in `source-contracts.json`. AzerothCore is secondary comparison only.

Continue from the exact 1,383 remaining IDs, not a new dataset sweep. Unresolved families include missing ordinary geometry, delivery acquisition, auxiliary or unrepresented item ownership, primary prerequisite/eligibility disagreement, server conditions, script/event recipes and live availability/acknowledgement. The remaining 16 primary live-required rows are not the only quests needing live acceptance; every offline proof remains limited to its supplied observations. Next investigate remaining creature acquisition and ambiguous or conditional chest sources with explicit source and ownership tests. Preserve all prior repair, inventory, completion, navigation, recovery, MinLevel/QuestLevel and debug-only zero-work diagnostic gates.
