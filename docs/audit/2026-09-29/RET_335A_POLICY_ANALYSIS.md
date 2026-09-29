# Ret policy decisions for original WotLK 3.3.5a

This analysis supplements [the post-merge audit report](POSTMERGE_AUDIT_REPORT.md). The executable model is `Tools/EvidenceAudit/ret_policy_simulation.py`; its numeric inputs are `fixtures/ret_335a_mechanics.json`. Actual routine behavior is tested separately by `PaladinDecisionRegressionTests` and the broader support, engagement and runtime-compatibility suites.

## What the observations can establish

The principal log records five Exorcism casts and one Consecration cast, but cast counts alone are not an efficiency measure. Cooldowns, movement, learned abilities, procs, remaining enemy health, mana, range and safe area admission all affect whether a cast is useful or available. The observed 34 Ret talent points also explain why Crusader Strike and Divine Storm are not yet learned; blindly inserting calls to those spells cannot repair that allocation. The mismatched Talented template is repaired separately.

The shared Spell-record overread affects the reliability of aura observations. Fixing that producer is essential before interpreting low proc-spell frequency as a priority defect. Unknown aura identity is not converted into a guaranteed absence or an instant-cast proc.

## Mechanics and production choices

| Ability or decision | Original-client/core evidence and resulting policy |
|---|---|
| Exorcism | Ordinary cast time is 1.5 seconds, with a 15-second cooldown and 8% base-mana cost. The original server code gives it a guaranteed critical strike against Undead/Demons. Rank-one Art of War proc 53489 reduces cast time by 750 ms; rank-two 59578 makes it instant. Only the latter permits a moving instant window. A stationary rank-one cast remains a filler below ready melee attacks. Unconditional melee hard-casting is not restored. |
| Crusader Strike | The original spell costs 5% base mana and has a four-second cooldown. It retains high priority when learned and admitted. The source does not require Divine Storm to be unavailable before using it. |
| Divine Storm | The original spell costs 12% base mana and has a ten-second cooldown. The pinned primary core corrects the raw target count to four. Its healing component and multi-target value are included qualitatively and in the sensitivity experiment. Runtime area safety remains a separate admission requirement. |
| Judgements | Low-mana damaging Judgements retain priority; talented Judgements of the Wise can restore base mana. Wisdom/Light assignments honor active external caster ownership, and Justice remains useful for fleeing/moving player targets. A preferred Judgement is not selected merely because the user has a similarly named seal. |
| Hammer of Wrath | The existing execute window remains at 20% target health with real spell availability, range and cooldown admission. There is no reason to hold a useful instant execute solely to reintroduce Exorcism frequency. |
| Consecration | The original cost is 22% base mana, with damage delivered over its ground duration rather than immediately. The configured three-target threshold and boss exception remain, together with mana above the configured recovery threshold, melee proximity, stationary actor/target and safe-area checks. A second cast requires another useful admitted window, not merely that three enemies were once present. |
| Holy Wrath | A 20% base-mana cost is significant for damage-only filler. Eligible Undead/Demons and the three-second stun give it control value under pressure. The new reserve avoids spending below the configured mana threshold at healthy HP, while HP at or below 70% retains the defensive opportunity. Moving does not itself prohibit this instant spell, but mounted travel does. |
| Divine Plea and healing | Plea's mana recovery competes with its 50% healing penalty. The existing healthy-actor admission is retained rather than forcing it during emergency healing. Emergency heals, defenses, Freedom, cleansing and crowd control retain their existing priority and ownership protections. |

These costs are percentages of **base** mana. Runtime thresholds are percentages of **current maximum** mana. The model keeps those quantities separate; it does not confuse a “20% mana” threshold with an ability costing 20% of the equipped character's maximum pool.

## Comparison experiment

The run contains 1,440 encounter configurations and five policies per configuration: **7,200 experiments**. Dimensions are level 60/80; 1, 2, 3, 4 and 6 enemies; short, ordinary and durable packs; low, intermediate and full starting mana; Undead/Demon versus ordinary targets; moving versus stationary combat; high versus low health pressure; and Judgements of the Wise availability. Separate seal comparisons cover six seals, four target counts and four durability assumptions.

The policies are the retained priority model, Consecration starting at two targets, an additional six-second short-pack reserve, a Holy Wrath mana reserve, and unconditional stationary Exorcism filler. Gear, incoming damage, mana regeneration, deterministic proc realization and drinking speed are explicit synthetic assumptions. The model is not a client/server combat emulator, and “completed” means the synthetic encounter completed within its time/health bounds.

| Comparison against the retained model, matched completed scenarios | Faster kills | Slower kills | Better kill-plus-refill time | Worse kill-plus-refill time |
|---|---:|---:|---:|---:|
| Consecration at two enemies | 29 | 3 | 18 | 15 |
| Added short-pack reserve | 0 | 0 | 0 | 0 |
| Holy Wrath mana reserve | 16 | 48 | 41 | 34 |
| Unconditional stationary Exorcism filler | 13 | 23 | 10 | 26 |

The retained model completes 1,271 of 1,440 scenarios; unconditional stationary Exorcism completes 1,269. Other listed alternatives complete 1,271. These totals are experiment results, not a predicted success rate on the user's realm. Aggregate medians can obscure the opposing individual results, so they are not used as a universal optimization claim.

Consecration at two enemies helps some cases and spends unnecessary resources in others. That is insufficient support for “two mobs means always Consecration.” The added short-pack heuristic changes no decisions in this particular matrix; it is **not** promoted as a proven improvement or implemented as a new production time-to-die predictor. The existing priority, duration, movement and mana constraints already eliminate those opportunities in this model.

Holy Wrath's reserve is a resource/control policy, not a claim that reserving mana always kills faster. Its additional ground/actor admission and retained defensive stun are tested at the actual routine entrypoints. The reserve deliberately trades some healthy damage-only opportunities for recovery headroom.

Exorcism hard-casting has mixed outcomes and slightly fewer completed synthetic encounters. Its value depends on auto-attack opportunity cost, proc rank, enemy type and competing ready abilities. The actual repair uses verified proc identity; it does not increase cast frequency to mimic an older version of Singular.

## Seals, Judgements and fight duration

Command's cleave, Righteousness's immediate per-hit damage and Vengeance/Corruption's stack ramp are materially different. The original primary core distinguishes the stacking seal's white-attack stack application from its melee special effects. It also limits whose aura stacks count. Vengeance and Corruption have equivalent faction counterparts in the numerical model; their names alone are not additional damage choices to rotate through.

In the synthetic level-60 single-target short fight, Command and Righteousness finish at 1.75 seconds while Vengeance takes 3.25. For the durable single-target example, Command takes 36.25 seconds and Vengeance 42.25, but their kill-plus-refill results reverse: 50.70 versus 48.35. These examples show why fight time and sustainable resource use cannot be collapsed into “one target always Righteousness” or “stacking always wins on a longer fight.” Actual gear, hit timing, talents and server corrections can move those comparisons.

The current automatic selection retains its tested protections: deliberate manual assignments stay authoritative; grouped mana recovery does not automatically replace a damage seal with Wisdom; optional solo Light uses health hysteresis and sufficient mana; player targets prefer immediate, controlled Righteousness when learned; safe Command cleave and the existing two/three-target hysteresis prevent repeated switching at a count boundary; bosses and learned stacking alternatives retain their established selection behavior. The audit does not add an unvalidated live health-slope predictor or claim that the existing heuristic is globally optimal for every short fight.

Wisdom and Light provide utility rather than being emergency-heal substitutes. Their simple proc realization in the model is explicitly assumed, so its output cannot justify a new production switching threshold. Justice's escape-control use is evaluated by the existing PvP/fleeing and Judgement ownership tests rather than assigning an invented DPS value to crowd control. Automatic PvP cleave/DoT behavior remains conservative because breaking crowd control or prolonging a combat state can outweigh raw damage.

Aura, blessing and Judgement regressions retain stronger-existing-buff protection, conflicting-buff rules, own-versus-other caster attribution, multiple-paladin assignments, Greater options and previous Ret restrictions. No tank-oriented ability is added to the Ret priority as part of this audit.

## Boundaries and reproducibility

Run `python Tools/EvidenceAudit/ret_policy_simulation.py --output <new-evidence-directory>` for the matrix, and `python -m unittest discover -s Tools/EvidenceAudit -p "test_*.py"` for the analyzer/model checks. The latter validates evidence identity, deterministic behavior, resource/time invariants, no ground patch while moving, no Holy Wrath damage against ordinary enemies, faction seal equivalence, drinking-speed sensitivity, invalid-input handling and the actual Ret template's tier/rank progression.

The model does not resolve latency, hit/expertise, raid buffs, set bonuses, random proc variance, exact healing ranks, PvP diminishing returns, kiting geometry or private-server overrides. Cast-induced melee delay is an explicit sensitivity assumption, not an IDA-verified swing-timer contract. The final release retains those limits instead of claiming measured DPS gains. Native reads were needed for the shared Spell decoder; no new combat offsets or invented client APIs were introduced.

The exact original-client/core identities are recorded in the numeric fixture and the audit report. Raw client DBC files and proprietary tooltip text are not distributed with this source.
