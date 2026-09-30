# Ordinary credit-source search geometry and world-space scan range

This continuation starts from fully validated PR64 `cda91e357d3c1922311b4a0c8ba8768cf31978ca`, branch `audit/next-quest-geometry-20260930`, worktree `D:/Dev/CB-QuestGeometry-20260930`. The containing Git commit is the candidate; final acceptance must resolve and test that exact identity. Production remains the verified PR61 master `3bc97e1e0b446aede269f7414c0c7c6358fdc192`. PR62, PR63 and PR64 remain separate reviewed implementation slices and must not be recreated.

## Two verified causes

Some ordinary kill objectives name a shared credit entry which has no physical creature spawns. Actual creatures advertise that credit through `KillCredit1` or `KillCredit2`; the existing original-client executor already matches those fields. Requiring a static spawn under the placeholder credit entry prevents useful search even when the source-backed producer and its ordinary location are known. The source review found this in 17 quests, including Fel Orc Scavengers (10482).

The scheduler also used X/Y alone for its scan radius and endpoint distance. A target thousands of units above the player could be admitted as nearby. Fifteen ordinary pickup/objective/turn-in tests reproduced nine assertion failures, independently of the new credit hints. Distance now includes X/Y/Z; an expanded radius can still admit another floor after its independent navigation check. This is straight-line world-space distance, not a claim about traversable path length. Existing flight-path cost models are unchanged.

## Source-bound execution boundary

The optional `ObjectiveCreditSources` repair-pack section binds each hint to an exact quest, objective row/index, required credit/count, physical producer entry and credit field. Every source and point is validated while applying the pack to an isolated clone. Duplicate owners, changed objective values, inappropriate item/GameObject objectives, cast/event flags, invalid or duplicate coordinates, extra authority fields and oversized arrays are rejected. Hash binding and the execution fingerprint include the new bytes.

`QuestDatabase.ObjectiveCreditSources` is excluded from base JSON deserialization. The catalog is readonly metadata populated only by the validated repair pack. It does not rewrite the quest's credit ID or add a fake global creature spawn. The audit exports this validated catalog explicitly in its effective-model evidence, while the base schema continues to reject its use as an injection channel.

Only an accepted, active quest with matching native objective IDs and required count can use these locations. The scheduler and existing executor retain current player/map, selection, attackability, actual credit matching, inventory, progress, recovery and navigation rules. Negative stored geometry for either the placeholder or producer wins over a positive-looking search hint. The generated profile still names the original required credit; native matching uses the actual producer. Zero or partial progress remains incomplete; only appropriate observed progress ends the objective.

Debug-only bounded diagnostics now distinguish a producer search hint from a currently observed live actor, naming producer entry, credit field, source reference, point count and fresh native matching status. Detailed producer entries are capped at eight alongside the total. These additions preserve the existing rate, candidate and output bounds.

## Primary-source exclusions

The exporter verifies pinned quest metadata and exact normal requirements, producer credit fields, creature state, ordinary map/phase/spawn masks, respawn, event and pool namespaces, per-spawn overrides and addon pose/aura state. The pinned TDB schema uses `StandState`; it is not inferred from a legacy `bytes1` column. Dead, unknown-pose, immune/nonattackable, vehicle, NPC-role and unresolved aura states are excluded. Custom C++ owners, unknown AI and nonordinary SmartAI triggers are excluded unless separately proved.

The narrow permitted SmartAI events/actions describe ordinary combat, reset and linked combat behavior only. Spell-hit, data-driven, timed-list, rescue and state-changing scripts do not become kill routes merely because they emit shared credit. Quests 11255, 11270 and 12237 remain explicitly excluded: their captive, corpse and rescue-trigger contracts require other actions and evidence. Names were not used to decide attackability or completion.

Authority is TrinityCore 3.3.5 commit `95657f54779467effea8a1749a61ff93abc1d707` and checksum-verified TDB335.25101 SQL `e72c0105ca27779ea3b08792b247210a44d9004fc6ab55cd1b0099d3b10779a9`. `Player.cpp:16190-16261` establishes ordinary kill credit and its quest/count checks; `KillRewarder.cpp:171-207` establishes the PvE reward path. UnitDefines and creature addon/spawn records establish inspected state bits. `source-contracts.json` retains immutable URLs and hashes. These references do not prove the customized live realm.

The enabled read-only IDA verification is retained in the preceding object-identity milestone: exact build12340 input SHA256 `bf644876709c591acc17c0da8cdf1814edcc9f1e6bc109a8c0d5c38c79dc953c`. The existing original-client credit matching primitive is reused. No speculative API, native address or new scripted recipe is added.

## Exact data and coverage

The new section contains **56 source records across 17 quests**, **48 physical producer entries**, and **1,237 quest-scoped search points**. These are not counted as global spawn additions. All **730 existing global spawn entries / 14,293 points**, 46 earlier GameObject identity repairs, 645 deliveries, 154 supplemental return-item contracts, 369 metadata-only external dependencies and two relation additions are unchanged. Only strategies 9066 and 9447 remain vetted; their recipes are unchanged and rebound to the new repair bytes.

| Primary classification | PR64 | This continuation |
|---|---:|---:|
| GENERIC-PROVEN | 2,950 | 2,963 |
| STRATEGY-PROVEN | 2 | 2 |
| DATA-INVALID/INCOMPLETE | 945 | 932 |
| SOURCE-UNCERTAIN | 365 | 365 |
| LIVE-ACCEPTANCE-REQUIRED | 16 | 16 |
| UNSUPPORTED-SCRIPTED | 57 | 57 |
| Total | 4,335 | 4,335 |

Exactly thirteen quests gain generic proof under controlled observations: 9935, 9936, 10482, 10702, 10703, 10836, 10864, 11283, 11598, 11938, 12462, 12476, 12546. Four quests gain the source route but keep independent obligations, recorded verbatim in `closure-review.json`. No previously proven classification is demoted by this slice. All **1,370 remaining IDs** have regenerated correlations, including actual producer actors for any retained credit hints. The uncompressed ledger SHA256 is `121d5789cfb095cd1d21953d9202d88af6252f46419deb4d516b1874f2a92510`.

## Verification

The new actual loader/scheduler/profile/progress fixture initially had 31 cases with fourteen intended failures and no unexpected errors. It now has 33 passing cases; two added comparison tests first exposed a dropped catalog and missing explicit evidence export. The fifteen ordinary range cases pass after their nine intended failures. Seven focused runtime groups pass, preserving original live-observation, endpoint, repair and prior GameObject tests. No assertion was weakened to make vertical separation or source identity pass.

The analyzer suite has **287 tests**, zero failures and one expected Windows permission skip. Separate failing-before cases cover actual StandState and loaded hint re-derivation. A transient missing JsonIgnore namespace caused a retained compilation failure; the attribute was qualified correctly. This is separate from intended behavioral red evidence.

The actual Windows/x86 dataset owner passed **258,485 checks** across all 4,335 rows. New checks exercise the existing native credit-alias matching primitive for the 56 supplied producers and preserve zero/partial/complete acknowledgement. Both vetted strategies passed **39 lifecycle checks**. Initial dirty-tree receipts are comparison fixtures only. `closure-fixture-manifest.json` requires identical final outputs at the clean committed candidate; the final local 34-command pipeline and applicable hosted workflows must pass before a verified-publication claim. Final SHA/run/job/artifact receipts remain external to avoid circular commit identity.

No native game was attached, no live kills or quest completions were performed, and the existing compiler warnings are not claimed eliminated. A controlled alias match is a test of the supplied cache observation; it is not proof that a creature is currently loaded, hostile, reachable or available on the configured realm.

## Remaining review

The frontier review examined 784 geometry targets and 208 missing giver/ender rows. No additional direct ordinary-geometry repair with matching quest fields was established. Thirty-three source-position candidates conflict with quest fields. Only two missing-role rows have primary relations, both for quest13908, whose SpecialFlags, item and condition obligations remain unresolved. Conditional, absent and conflicting locations cannot be inserted indiscriminately.

Continue from the exact1,370 remaining IDs. The next concrete offline repair is four omitted reputation requirements (9145,9155,9173,9192), all with null modeled fields and matching primary base fields. Investigate their baseline-protection path, preserve existing facts, and test the actual missing/below/at/above reputation gates. Auxiliary items are often keys, bait or intermediate crafting inputs; absence from the final required-item list is not permission to delete them. Continue those source/script owners separately rather than declaring them redundant. Keep live path/phase/lock availability, inventory, native acknowledgement and realm configuration explicit.
