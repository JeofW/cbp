# Recovery action acknowledgement and arbitration

## Problem and scope

The retained October 1 21:11 log submits Divine Protection at21:12:52.889 and21:12:55.897,3.008seconds apart. The old Singular name/time dictionary expires after2.5seconds and does not establish an outcome. The user's self-heal plus potion symptom exposed the same missing shared ownership: Singular spell, Singular item and DrinkPotions callers could independently submit conflicting recovery. A submitted cast, item-use reply or behavior-tree success is not an observed heal or defensive effect.

This scope adds shared recovery ownership across named/ID Singular casts, Heal/Buff factories, Singular potions/healthstones and DrinkPotions. It does not tune health thresholds, rotation priorities, spell coefficients, talents or expected healing amounts. It does not certify quest10161, all class rotations or live-realm completion. PR90–94 and all older fixes remain preserved history.

The source baseline is merged/deployed PR94 `2d123d63eec35b675822571396a067e68cb7d281`. The separate action worktree preserves the original potion fixture in967737d6 and reconciles master through9a1a5dd69b35765a0c71ddb3913e4b80793d6be6. Final candidate, publication and installation identities must come from fresh Git and the external receipts, not this precommit report.

## Behavior

`RecoveryActionLedger` separates preparation, actual native entry, observed casting, cast success awaiting an effect, acknowledgement, known rejection/interruption, replacement and unacknowledged timeout. It stores context/generation, spell/item/recipient, native target, creation/submission/deadline and first/last/count of conflicting requests. A final native-entry check prevents reentrant or stale preparation from executing; submitted calls with unknown replies remain reserved. Only an explicit container guard refusal can turn an entered request into a known unexecuted rejection.

Health recovery conflicts per recipient, including a healing item against an in-flight spell and vice versa. Mana-only items remain independent of health spells; items with unknown effects never become proven mana-only. The two consumable owners serialize item requests. Buff acknowledgement uses its intended recipient even when a combo-point spell dispatches against the current enemy. Unrelated ordinary casts and another recipient's healing remain available. Ordinary resurrection retains the existing ability to target a dead friendly recipient.

The acknowledgement budget is the existing ten-second direct-heal continuation bound, extended only for a longer observed cast time. It is neither a fabricated healing estimate nor an emergency health threshold. Duplicate requests cannot slide this deadline. Timeout produces an explicit unproven-outcome diagnosis and releases the bounded reservation for a fresh attempt. Unknown inputs remain nonfatal deferral; Stop, interruption and actual process/executor ownership loss propagate. Actor, map, routine, profile, run, memory/handle or spell-owner changes revoke old action ownership.

The host shared pulse advances the same ledger while a routine yields. Recovery timing is included in the slow-pulse breakdown. Advisory conflict checking happens before additional Lua metadata queries, while the final reservation check remains atomic. Twenty repeated conflicting heal, health-item or defensive admissions in the same observation interval add no Lua queries.

## Original-client acknowledgement evidence

The retained primary evidence is the official LibHealComm4.0v1.6.6 archive, published June28,2010 for3.3.5, and the pinned original UI source. `D:/Dev/CopilotBuddy-Evidence/action-flight-20261002/original-cast-sources.json` records URLs, immutable identities and hashes. `ORIGINAL_CONTRACT_BYTES_VERIFIED.json` rehashes all four retained files. Those third-party files were not executed, installed or copied as implementation.

The original unit-cast events carry unit, name, rank and cast counter. SENT's fourth field is a target name, so SENT and STOP are not acknowledgement events. The original library explicitly rejects unowned or zero success counters. The collector requires a matching observed START and SUCCEEDED plus the expected direct SPELL_HEAL source/recipient/spell before acknowledging a direct heal. A late or zero-counter instant success cannot acquire a new request's ownership. Such incomplete instant/other evidence remains pending until an authoritative observation, a context change or diagnosed timeout; it is not claimed fully correlated.

Defensives require complete aura coverage and the expected spell/caster/recipient. Consumable use requires a complete pre/post owned quantity decrease and new active cooldown; a health item additionally requires its selected effect's direct heal. Consumption alone does not prove health recovery landed. Indirect/periodic effects without a proven matching contract remain conservative and may reach the bounded timeout; no IDs, coefficients or script outcomes were invented.

The private Lua collector has one reused owned frame, a128-event queue, counted losses and at most five events per reply. Returning a batch does not consume it: only the next confirmed managed cursor acknowledges earlier events. Partial registration is cleaned up; retries reuse the frame. Foreign globals, old cleanup callbacks, stale tokens, changed actors and clock/sequence contradictions cannot publish current evidence. A counted gap prevents causal promotion.

## Complete Lua observations

Inspection of the existing memory string reader found that a failed/short read could become empty text, malformed UTF8 could be replaced, and a nonterminated field could be truncated. These are unsafe foundations for an acknowledgement protocol, especially an empty rank string.

The added explicit `GetObservedReturnValues` path retains legacy API compatibility while requiring complete bounded scalar values. It verifies the prepared query bytes, captures the process/executor/Lua-state ownership, uses per-call output storage, bounds native pointer output to64values, validates consistent status/count, and decodes exact UTF8 fields up to384bytes. Known empty strings and known empty results are distinguished from unavailable data. Nil/table/function results are rejected. Cancellation and fatal ownership loss survive nested transport and diagnostic catches. Existing cursor guards and item-slot entry checks remain intact.

Native code execution, full client heap lifetime and live event ordering are not proved by controlled replay. The stock Lua5.1 and process-read tests prove the generated protocol and the managed/native-entry owners under their declared boundaries. A new live session is required before claiming game acceptance.

## Retained regression evidence

All receipts below are under `D:/Dev/CopilotBuddy-Evidence/action-flight-20261002`. Compiler, fixture and selector setup errors remain recorded separately and are not behavioral-red evidence.

| Failure family | Failing-before receipt | Corrected evidence |
| --- | --- | --- |
| DrinkPotions conflicts and false use claims | `recovery-potion-boundary-red-eb0cd104`:10assertions | Actual complete plugin/policy15/15 |
| Incomplete string/scalar observations | `recovery-lua-observation-red2`:7reader,7Lua-wrapper and1packet-bound assertions | Reader12/12,wrapper12/12,strict protocol124/124 |
| Replaced process handle and contradictory replies | `recovery-lua-owner-red`:4assertions | Actual old/new Lua transport269/269 |
| Event ownership and frame lifecycle | `recovery-collector-ownership-red`, `recovery-adapter-adjacent-red` | Collector/query47/47,coordinator69/69 |
| Native spell submission/control flow | `recovery-native-spell-marker-red`:6assertions | Complete spell observation/dispatch group286/286 |
| Lua native entry and container outcome | `recovery-spell-green-lua-marker-red`, `recovery-lua-marker-green-container-red` | Actual container/UseItemOn/Lua5.1 group64/64 |
| Singular Cast/Buff/Heal admission | `recovery-container-green-singular-red`:9buff and10Heal assertions | Shared buff63/63,all five Heal factories89/89 |
| Ordinary resurrection compatibility | `recovery-resurrection-admission-red`:1assertion | Actual coordinator positive resurrection and negative dead-heal controls |
| Singular items and shared pulse | `recovery-singular-items-and-shared-pulse-red2`:10item assertions; corrected `recovery-shared-pulse-owner-red3`:6pulse assertions | Actual item selector/factory13/13,shared pulse/events27/27 |
| Repeated admission cost | `recovery-shared-green-query-cost-red`:3assertions | Same-pulse repeats add zero Lua queries |

`recovery-integrated-focused-green` passes all17selected groups with source stable and no unexpected failures. It includes the69coordinator,34ledger,47Lua collector,124protocol,24reader/wrapper,269Lua transport,286spell observation,64container,15plugin,13Singular item,63buff,89Heal,27shared-pulse,12worker,144cast-continuation,582Hunter and97dispatch-receipt cases. These are complete group sizes, not a claim that every case is new. Whole local34-stage, exact4335-quest closure, runtime compilation and exact-SHA hosted gates remain separate publication requirements.

## Delivery and continuing limits

### Complete-gate and subsequent review

The full local gate on intermediate8f4f3663 completed32/34stages with stable source. It exposed a missing combat namespace import during complete Singular compilation and one container source-wiring assertion that recognized only the previous Lua API. The import is corrected. The source assertion now requires the existing GUID revalidation followed by recovery admission and the strict observed query; it retains the underlying protection.

Subsequent inline review added20failing cases for fatal process/executor loss during inner state/status/count/pointer/string reads and two failing aura-precondition cases. Those inner readers now preserve fatal ownership, and existing aura coverage from any caster—including an unavailable caster identity—prevents duplicate application. Our submitted action still requires its own known caster for acknowledgement. `recovery-final-review-red` retains22intended assertions; `recovery-final-review-green` passes all8selected groups, with289transport and72coordinator cases and complete Singular registration/compilation. A fresh full candidate gate is required; the earlier17-group counts describe their exact intermediate source and are not substituted for later validation.

The release must compile and stage14components, including the modified DrinkPotions plugin; the prior13-component package was insufficient for this scope. The additional production-file comparison was blocked twice before reaching CoS. `EXTRA_PAYLOAD_COMPARISON_HELD.md` retains the exact limitation. No action deployment is established by a candidate or merged build, and existing user plugin files must not be replaced without their baseline being verified.

Production remains the last hash-verified PR94 installation, with434payload files and2015protected files. The latest available live log still predates this action work. Preserve live acceptance for no repeated Start, aura metadata diagnostics, defensive/heal/item acknowledgement, quest10161 authoritative collection/turn-in, successor-node travel, post-combat continuation, food/drink, death/resurrection and quarantine scope. The exact4335execution population and zero formal full-execution promotions are unchanged.
