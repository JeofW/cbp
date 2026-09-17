# W54 — equipment scoring and original-client research assessment

Repository: `jeofwong/CopilotBuddy-private`; original WoW 3.3.5a/build 12340.
Review date: 17 September 2026. Read `docs/audit/WOTLK_335A_RESEARCH_POLICY.md` first.

## What is established, and what is not

The repository contains multiple equipment decision paths, not one shared specialization optimizer. The current source and controlled regression tests establish which values are read and how decisions are made. They do not establish the user's currently installed build, enabled plugins, saved settings, realm rules, or maximum-DPS stat weights.

No numerical stat weights were changed in W54. The original target/version requirement now has a dedicated committed policy. A comment saying "Pawn/Wowhead WotLK" is not sufficient provenance to certify a scale for build 12340.

## 1. SmartLootRoller

`StatWeightsPresets.cs` has this Retribution preset:

| Stat | Existing weight |
|---|---:|
| Weapon DPS | 7.5 |
| Strength | 2.7 |
| Hit rating | 2.2 |
| Expertise rating | 2.2 |
| Agility | 1.8 |
| Critical strike rating | 1.7 |
| Haste rating | 1.4 |
| Armor penetration rating | 1.2 |
| Attack power | 1.0 |

These are score points per unit, not a measured universal stat priority. In particular, a point of weapon DPS is not the same unit as a point of Strength.

`FormSettings.cs` loads the chosen preset into the individually persisted Weight_* properties. Merely having the preset in source does not prove that it is selected for the user's character. The settings source defaults the profile label to Feral Cat and numerical weights to zero; initialization of a displayed label is not automatic application of the Ret preset. The runtime decision methods read the saved weight dictionary. Do not claim automatic synchronization with Singular's current specialization.

`PawnScorer.CalculateScore` sums recognized stat quantities multiplied by configured weights, adds weighted armor and weapon DPS, and has a separate Feral AP approximation. It first uses GetItemStats via Lua, then a native-stat fallback when no nonempty Lua result was received. The core formula is linear: ten Strength and ten Agility at the Ret preset contribute 27 + 18 = 45 score points. This arithmetic and the actual mapping are covered in the retained SmartLoot tests.

The roll consumer checks configured armor/weapon rules and a positive dropped score, then compares against the slot score with a 1% buffer (or zero equipped score). Two-hand weapons are compared to both current hands; generic one-hand comparison is simplified to main hand. Rings/trinkets use the weakest applicable occupied slot, or zero when a slot is empty. This is not a combinatorial best-equipment-set search.

The separate SmartLoot auto-equip loop is optional, scans every five seconds while the bot runs, applies level/usability/BoE settings, and equips at most one item per scan. It allows an empty-slot upgrade or >1% score improvement; equal-score ties can use item level. It does not prove that missing item observations are a genuine zero score. Its BoE protections and any item-binding confirmation must remain separate safety requirements.

### Repaired loot-code and capability boundaries

The recovered earlier fix95d2fe4a preserves saved enum Pass=3 while translating it to actual client Pass=0 rather than Disenchant=3, and maps configured Mp5 to the scorer's Mp5 key. That was already committed; W54 did not recreate it.

The new W54 capability tests show that a positive score previously authorized Need without consulting canNeed. They also show unavailable Greed being submitted. The narrow repair adds the original client's separate availability checks: an unavailable Need follows the existing configured nonmatching fallback; an unavailable Greed becomes Pass. Explicit Disenchant still requires both the user's setting and observed canDisenchant. This is not a new main-spec or stat-optimization model.

The tests run the four actual scoring/settings/preset/decision source owners with controlled world/Lua observations. They do not execute a live roll, auto-equip, bind an item or write the user's settings.

## 2. AutoEquip2 and WeightSetEx use a different scale

`WeightSetEx.CurrentWeightSet` uses class and the active talent-group's largest talent-tab allocation to select XML. It caches that selection and has an ACTIVE_TALENT_GROUP_CHANGED invalidation hook. Unknown all-zero talent points defer selection. If the exact specialization XML is missing, there is still a class-only fallback; that deserves a negative-role test before being treated as safe.

`runtime-snapshot/Data/Weight Sets/Paladin-Retribution.xml` currently contains:

| Stat | XML weight |
|---|---:|
| DPS | 4.7 |
| Hit rating | 1.0 |
| Strength | 0.8 |
| Expertise rating | 0.66 |
| Critical strike rating | 0.4 |
| Attack power | 0.34 |
| Agility | 0.32 |
| Haste rating | 0.3 |
| Armor penetration rating | 0.22 |
| Spell power | 0.09 |
| Stamina | 0.001 |
| Armor | 0 |

It also assigns socket scores. The hit/crit/attack-power aliases are mappings, not a license to count the same observed stat twice.

The two scales are not simply the same weights multiplied by a constant: Strength/Agility is 1.5 in SmartLoot and 2.5 in this XML. Neither ratio was derived or benchmarked in W54.

AutoEquip2's Need decision has an additional primary-stat rule. With this XML it selects Strength as the primary stat, and can refuse an Agility-bearing item with no Strength even if its aggregate score is greater. That is a conservative rolling policy, not proof that every Agility-only piece is inferior for Ret. Its weapon-style, wanted-armor and client canNeed checks further restrict eligibility.

Running different auto-equip owners can still cause disagreement. SmartLoot's code attempts to detach competing loot-event handlers; this does not establish that all competing bag-scanning/equipping paths are disabled. Which plugins are enabled in an old settings capture does not establish the user's present state.

## 3. A separate legacy host AutoEquipper requires a reachability check

The reviewed `Styx/CommonBot/CharacterManagement/AutoEquipper.cs` initializes a class-weight dictionary but does not use that dictionary in its EvaluateItem body. That method scores armor/weapon DPS and recursively evaluates an equipped item's ItemInfo through the same comparison routine. For a populated slot this has a potential nonterminating self-comparison path.

A source-wide scan of the retained export found the EvaluateRewards declaration and lazy AutoEquipper construction in CharacterManager, but no direct EvaluateRewards caller. This is a source finding, not a reproduced active quest-reward failure. Do not rewrite it or attribute live reward selection to it without proving its call path and obtaining regression evidence. The actual current quest-reward integration remains part of the audit.

## 4. Original-version mechanics versus recommendations

Primary evidence inspected:

1. Original UI mirror `tekkub/wow-ui-source`, tag3.3.5, commit `c4e0255fc574598428ee1c25f160ab949798bc98`, `FrameXML/LootFrame.lua`, blob `28fe1b491a6ae1e8355f246f412a4a550286a1c5`. The original-era UI reads canNeed/canGreed/canDisenchant separately and disables the corresponding controls. The seventh/eighth Lua values are not guessed from a modern API that may add fields. This establishes the narrow original-era API contract used by the W54 repair; it is not evidence of private realm custom behavior.
   https://github.com/tekkub/wow-ui-source/blob/c4e0255fc574598428ee1c25f160ab949798bc98/FrameXML/LootFrame.lua

2. AzerothCore explicitly identifies its implementation as a3.3.5a emulator. Inspected `src/server/game/Entities/Unit/StatSystem.cpp`, blob `24c025ac6d6d6e17fa5c75cb93178cb8de92a282`. Its Paladin melee-attack-power formula uses twice Strength, while its physical critical-strike update includes GetMeleeCritFromAgility. This supports treating Strength and Agility as different contributions rather than interchangeable raw primary stats. It does not derive either bundled numerical scale, establish the user's realm configuration, or prove which item wins at a particular hit/expertise cap.
   https://www.azerothcore.org/doxygen/d1/db0/StatSystem_8cpp.html
   https://github.com/azerothcore/azerothcore-wotlk/blob/master/src/server/game/Entities/Unit/StatSystem.cpp

3. The previously supplied Wowhead WotLK rotation page and Blizzard's2023 Wrath Classic balance announcement are comparison-only unless an individual claim is independently corroborated for original3.3.5a. Later Classic Reckoning-glyph and seal-stacking changes are not imported by this policy. The W50 review contains the earlier source assessment. No later-version numerical coefficient was copied in W54.

## 5. Recommended next architectural work, not yet implemented

Keep a single equipment decision policy per active character/loadout, shared by rolling, auto-equipping and quest-reward choice, while retaining separate dispatch permissions. Its inputs must include trusted item data, actual specialization, supported weapon setup, replaced slots, user/group loot rules and observed client eligibility.

Separate observation failure from a true zero-valued stat. Validate numerical weights and scores for finite values. A heuristic scale must remain labelled heuristic; cap-aware hit/expertise value, set bonuses, procs/on-use effects, enchants/gems, weapon-speed interactions, fight length and PvE/PvP priorities require their own supported model and examples. User overrides must not bypass a client prohibition or item-binding policy.

Do not assign a new global Strength/Agility ratio merely because a current guide calls one stat better. First add paired realistic3.3.5a item/loadout cases and investigate the existing scorer disagreements. Preserve original-client version provenance and present uncertainty rather than claiming a universal maximum-DPS optimizer.
