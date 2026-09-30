# Preserve required quest materials and protect them during sales

Recovery note: the original bank, availability-conflict and aggregate loot-group reads now succeeded through the normal tool;2004groups contain zero saturated equal-chance groups. The broad data-family request still received the provider safety-status error twice and was stopped. Exact outcomes are in `source-review-recovery-20261001.json`. Historical hold descriptions below are retained; those recovered reads do not change the stock repair, classification counts or establish bank absence/prerequisite equivalence.

This separate scope starts from reviewed donor PR79 `5036010ee45ca0daa5c54723d916c9a9b4e1d973`; its merge is master `363f096764c61e1faf050fd6a638867f901e8dfd`. Earlier PR61–79 work is preserved. Production remains the verified PR61 package; source integration does not deploy this change.

## Resulting behavior

Five quests still lacked declared return materials after their ordinary collection routes were repaired. A new repair-only `QuestRequiredStock` contract records ten exact item requirements without inventing acquisition actions. The scheduler requires those materials already carried before pickup and again before turn-in. Current inventory is rechecked at profile publication and during continuing execution. Unknown, missing, partial or changed stock cannot authorize the constrained work. Already accepted ordinary objectives can continue after stock loss; return materials cannot fabricate authoritative quest readiness.

The five contracts are565(Bolt of Woolen Cloth2997x1, Fine Thread2321x1, Hillman's Cloak3719x1),2746(Clara's Fresh Apple8683x2),10757and10763(Fel Iron Bar23445x4 and Arcane Dust22445x2 each),13906(Venomhide Baby Tooth47196x20 and Rugged Leather8170x20). Quest13906 keeps its independent46362 supplied-hatchling contract. Ordinary collection objectives and all previous repairs remain unchanged. No crafting, vendor purchase, pet, daily or scripted acquisition action is inferred from an item name.

The actual `SellByQuality` protection previously covered StartItem and collection objectives but omitted pure-delivery materials and the additional stock lists. Accepted and scheduled quests now also protect DeliveryItems, AcceptanceSupplies, SupplementalSupply and RequiredStockItems. Malformed material data prevents a sale; unrelated inactive quest data does not blanket-protect inventory. Existing player, merchant, quest-log, quality, cancellation and currentness checks are preserved.

The shared repair integer parser also now checks JSON numeric type before TryGetInt32. A boolean quantity is rejected as invalid repair data rather than escaping as an unrelated InvalidOperationException. All new stock metadata is repair/source-bound, excluded from base JSON injection, non-destructive and part of the existing execution fingerprint.

## Verification and coverage

The typed stock suite passes38 scenarios after22 intended preimplementation failures with zero unexpected exceptions in the corrected red run. The initial red retained one fixture exception separately. A later green attempt exposed the boolean-count parser exception; its failing receipt is retained before the parser correction. Five actual shipped-knowledge cases first failed for the missing contracts and now pass. Existing delivery, supplemental-supply, inventory publication and collection-profile groups also pass.

The extracted actual sale method passes31 cases after seven added cases reproduced omitted protections and unsafe malformed-input handling. All23 original cases remain. Fifteen source-export tests and six receipt-validation tests pass after their retained red runs. The analyzer suite ran421 tests, with420 passes and one expected Windows permission skip. Existing compiler warnings are retained in raw logs; they are not claimed eliminated.

The actual dataset owner passes261306 checks across4335quests, and both vetted strategies pass39 lifecycle checks. The97 additional passing cases affect only the five repaired subjects:65 explicit required-stock owner cases and32 additional pipeline/start-item cases. No old case was removed and no same-named case changes status. The first sweep exposed a fixture that discarded other stock when adding StartItem for13906; the corrected fixture keeps other observations fixed and adds a negative start-item-only case. The failure and correction are documented without treating it as a production regression.

The new ledger explicitly retains `data:required-stock-acquisition-route-missing` for all five. Representing materials and testing pre-carried inventory does not prove automatic acquisition. Quest13906 also retains its money/acceptance obligation. There are no primary classification transitions:3028generic/2strategy/921data/311source/16live/57scripted=4335, with1305remaining IDs. Ledgers, secondary obligation indexes, coverage, exact IDs and correlations are regenerated. The unchanged81availability contracts/146predicates and only9066/9447recipes remain.

## Evidence boundaries and remaining work

Reference data is pinned TC335 `95657f54779467effea8a1749a61ff93abc1d707`, TDB335.25101, SQL SHA256 `e72c0105ca27779ea3b08792b247210a44d9004fc6ab55cd1b0099d3b10779a9`. Exact per-quest/item row hashes and quantities are retained. This scope reuses the verified original32-bit build12340 complete carried-inventory observer; it adds no native API or dispatch and synthesizes no production observation. Controlled starting inventory exists only in tests.

The separate bank source probe, aggregate loot-group review, availability/source-conflict review and broad data-family inspection remain stopped after provider blocks and identical retries. Their exact payloads are retained in the parent scope and external receipts. This work used the independently recovered five-subject evidence; it did not split or route around those requests. The remaining43 availability subject conflicts and bank-inclusive subject4023 are unresolved. An empty conservative export is not proof that every remaining offline repair is exhausted.

Final clean-commit local34-command acceptance, the four-stage exact closure runner and applicable hosted Windows/x86 workflows must identify the actual containing candidate. External `REQUIRED_STOCK_*` receipts record final publication/integration. Comparison fixtures alone are not final acceptance. No game was attached, no new release was deployed and no live quest completion is claimed.
