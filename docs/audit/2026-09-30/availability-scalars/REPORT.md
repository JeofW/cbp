# Source-backed player-level and auto-complete reward conditions

This independent continuation starts from condition PR67 `0722b9c4d7e2d958a23c0986e05864f2f6b68a0a`. It adds three complete contracts for quests11596,11597,25286 and preserves all58 previously validated contracts. The bundle contains61contracts/126predicates. It does not include the separate reputation/inventory/live-objective branch or finalize the provider-held combined integration.

## Corrected behavior

Pinned TrinityCore335 Player::GetQuestRewardStatus distinguishes repeatable and seasonal lifecycle, not QuestType0 versus2. The exporter and loader had rejected the permanent reward reference to auto-complete quest8743. Type8 can now use that source-confirmed nonrepeatable/nonseasonal reward history; types9/14/28/47 still require ordinary active-state semantics. Readiness, presence in the quest log and source promises never become a historical reward. Unsupported reference types, repeatable and seasonal histories remain rejected.

Condition27 now evaluates the five pinned numeric player-level comparisons, preserving negation and unknown observations. Its threshold is not a referenced quest ID or a QuestLevel acceptance ceiling. Quest25286 requires observed level>=75. The current observation owner includes and rechecks the actual player's level; publication loses permission when a fresh observation no longer satisfies the contract. Already accepted turn-in remains independent of pickup conditions.

The dataset fixture selects source-consistent player levels without consulting the production evaluator. The evidence validator requires unknown/below/at/above level cases and rejects a borrowed quest-status receipt for the number75. Existing status/group/negation, prerequisites, inventory, recovery, navigation, publication and strategy gates remain intact.

## Validation and exact classification

The source exporter first failed seven intended assertions. The actual loader/scheduler/current-observation fixture failed39 intended assertions across45scenarios with zero unexpected errors. The new receipt validator failed two intended assertions. The corrected source/runtime/receipt tests pass; all316 analyzers pass with one expected Windows permission skip. The prior test that rejected every QuestType0 reference was corrected to retain rejection for raw-state predicates while separately testing valid permanent reward semantics.

The actual dataset sweep passes259201checks across4335rows; both existing strategies pass39lifecycle checks. The increase from259156 is exactly22cases for11596,17for11597 and6for25286, with no removed cases; see simulation-count-delta.json. All716availability-specific checks pass. Each newly supported quest retains its successful scheduler/generated-profile/behavior/progress/turn-in/next-scheduling pipeline under supplied observations.

The exclusive classes are3002generic/2strategy/932data/326source/16live/57scripted=4335. Exactly11596,11597,25286 move fromSOURCE-UNCERTAIN toGENERIC-PROVEN, leaving1331exactremainingIDs. This is this branch's ledger. It must not be combined arithmetically with master or the pending integration ledger. All unrelated repair families, original dataset bytes and the two9066/9447recipes are unchanged; only their exact repair binding is updated.

These precommit outputs are comparison fixtures. Full clean-commit local and hosted Windows/x86 acceptance must identify the containing final candidate and reproduce the exact hashes; those receipts are retained externally. Source data and controlled simulation do not prove customized-realm availability, navigation, interaction or completion.

## Remaining work

There are64unresolved availability quests: subject source conflicts, carried/bank item requirements, spell, daily and area observation families. Source row disagreements require evidence-based reconciliation, not blind overwrites. The separately validated complete inventory observer supplies a future foundation for carried-item conditions once the held integration is completed and tested. Per-quest daily/area/spell observations still need their own exact client/server contracts and tests. No new special quest action or native API is inferred from an item, name or flag.

Masterb91c548ba8a773ed60d6f138f1439180916ecc79 contains the separately validated PR62–66,68,69 chain and has passed its own local and hosted gates. Production remains deployed PR61 master3bc97e1e0b446aede269f7414c0c7c6358fdc192. The pending combined worktree and its blocked final staging are preserved in a verified recovery archive; this scope does not reroute that operation.
