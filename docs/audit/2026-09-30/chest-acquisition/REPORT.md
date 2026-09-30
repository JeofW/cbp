# Ordinary collection source audit and chest selector repair

This continuation starts at PR62 head `3662a8f5a47178fc982e2891a45f5a3ac9a7b3ed`, on branch `audit/next-quest-acquisition-20260930` in `D:\Dev\CB-QuestAcquisition-20260930`. PR61 is already merged and deployed at master `3bc97e1e0b446aede269f7414c0c7c6358fdc192`. This change does not redeploy production or merge PR62.

## Correct item sources must accompany geometry and simulated progress

The earlier ledger could certify an ordinary collection objective from matching item/count metadata, a stored position and a passing controlled inventory/progress pipeline, even when the modeled actor could not supply that item in the primary reference. For example, the imported GameObject 1687 for quest 64 resolves to a generic sign named The Sepulcher, and GameObject 24661 for quests 12870/12882 resolves to a wooden chair. A successful simulation with supplied item observations does not prove either object is a loot source.

`quest_collection_source_335.py` now checks each modeled collection alternative against exact primary creature/GameObject and item namespaces. Ordinary creatures use `creature_template.lootid`; ordinary chests require type 3 and `gameobject_template.Data1`. The audit follows direct/reference loot with the actual reference GroupId selector, default LootMode 1, valid counts/chances and bounded cycle/depth handling. Matching loot conditions remain explicit source obligations. Fishing holes, chairs, spell-focus objects, another item's source, or an item in another reference group cannot become ordinary chest/creature evidence.

A viable other alternative does not certify an invalid actor the runtime can still select. Geometry and loot evidence must belong to the same viable candidate; a wrong actor's position cannot complete another actor's missing route. Each source record retains typed identities, template and loot-row hashes/lines, selector fields, reference edges and exact condition records. These checks produce audit obligations. They do not generate production observations, invent action recipes or claim realm equivalence.

## Source-backed chest addition

The existing repair exporter used lowercase `data1`, whereas the verified SQL column is `Data1`. The one-expression correction adds one ordinary primary point for chest entry **194341**, Dusty Journal, used by quest **13634**, The Black Knight of Silverpine?. Its chest loot selector is 26878, whose ordinary quest-required item row supplies item 45062. Existing non-chest, namespace, source-conflict, phase/event/pool and existing-position vetoes remain.

The original quest dataset is unchanged. The source-bound repair pack now has **690 spawn entries and 14,081 points**, compared with 689/14,080. All **645 delivery contracts, 154 supplemental supplied-return contracts, 369 external dependency records and two relation additions** remain. No external dependency becomes a schedulable quest. Three unchanged executable recipes still cover only quests **9066 and 9447**. Repair/strategy/knowledge hashes are rebound together; `repair-delta.json`, `repair-evidence.json.gz` and `source-contracts.json` identify the exact additions and sources.

## Exact partition and retained obligations

| Primary classification | PR62 baseline | This continuation |
|---|---:|---:|
| GENERIC-PROVEN | 3,000 | 2,924 |
| STRATEGY-PROVEN | 2 | 2 |
| DATA-INVALID/INCOMPLETE | 945 | 945 |
| SOURCE-UNCERTAIN | 314 | 391 |
| LIVE-ACCEPTANCE-REQUIRED | 16 | 16 |
| UNSUPPORTED-SCRIPTED | 58 | 57 |
| **Total** | **4,335** | **4,335** |

Quest 13634 gains a complete controlled ordinary route. Stronger primary checks reopen **77** previously generic quests. The initial acquisition triage identified 46 quests lacking any confirmed ordinary owner for at least one item; the final check also catches invalid alternative owners, loot conditions and mismatched owner/geometry pairs. There are 80 primary-classification transitions in total. The scripted category decreases because one quest also has a source obligation, which takes precedence; its scripted obligation remains and no new strategy is claimed.

All **1,409 remaining IDs** have source correlations, including the new collection-source evidence. `classification-ids.json` is the unique six-way partition; `classification-transitions.json` identifies changes; `remaining-category-ids.json` is the separate, overlapping secondary-obligation index; `coverage.json` asserts exact unique membership, total 4,335 and no missing/phantom/duplicate IDs. The uncompressed ledger SHA256 is `0a60115a1d02835a523fe5d12d87b168fb85fe58358900f2da7d7b6ed149c7dc`.

## Verification

The new regression executes the actual closure owner over a controlled 4,335-row fixture. Before the source repair, ten tests produced **eight intended assertion failures and no harness errors**. The unchanged tests pass after the repair. The chest selector has a separate retained eleven-test red with five intended failures. The complete local analyzer run executes **258 tests**, with no failures and one expected Windows symlink-permission skip.

The current actual Windows/x86 owners generated **258,279 dataset checks** with zero failures and **39 checks** for the two vetted strategy lifecycles. Their 4,076 runtime/test/compiled-input hashes were verified unchanged when staging this fixture. These initial observations were generated from a modified working tree and are explicitly labeled comparison fixtures. The complete containing commit must reproduce them using `Tools/EvidenceAudit/run_quest_closure_335.py`, pass the full local acceptance runner and receive the required hosted Windows/x86 checks before final acceptance is claimed. The exact final Git SHA and raw run/job/artifact receipts belong to the external publication record and the PR's checks, avoiding a fictitious self-referential SHA in this file.

Raw red/green and diagnostic receipts are under `D:\Dev\CopilotBuddy-Evidence\postmerge-20260930-pr61`: `chest-selector-red`, `chest-selector-green`, `collection-source-ledger-red`, `collection-source-ledger-green`, `chest-observations-v1`, `acquisition-ledger-v1`, `acquisition-correlations-v1`, and `ACQUISITION_CLOSURE_STAGED.json`. Retained intermediate artifacts were not overwritten.

## Authority and next source work

The primary authority remains TrinityCore **3.3.5** commit `95657f54779467effea8a1749a61ff93abc1d707` and checksum-verified TDB335.25101 SQL `e72c0105ca27779ea3b08792b247210a44d9004fc6ab55cd1b0099d3b10779a9`. `source-contracts.json` verifies the retained GameObjectData.h, LootMgr.cpp and ConditionMgr.h file hashes against their original pinned receipts. Relevant contracts are the chest union/GetLootId, LootStoreItem validation, reference-group processing, default-mode filtering and typed condition binding. AzerothCore remains secondary comparison only.

Continue from these exact remaining IDs. Investigate whether wrong imported actor numbers are loot-selector IDs, and admit a repair only when the source namespace, expected old objective, exact required item/count, executable ordinary behavior and conservative geometry can be bound. Unresolved custom-realm/source disagreements, profession/fishing/scripted acquisition, conditions, missing ordinary spawns, live navigation/phase/lock access and authoritative progress remain explicit. Do not invent new special actions or call these simulations live completion. Existing MinLevel/QuestLevel semantics, debug-only bounded zero-work diagnostics, inventory versus acceptance-supply distinctions, prerequisites, navigation/recovery and execution ownership gates are unchanged.
