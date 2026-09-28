# W110 Ret seals, sustain and player-combat decisions

28 September 2026. Original client3.3.5a/build12340, TrinityCore3.3.5 primary and AzerothCore WotLK secondary. This extends the full prior W110 Ret/core/objective matrices; it does not replace their native, realm, route, protocol or independent-review boundaries. Exact test/publication outcomes belong to the new seal checkpoint/evidence, not this design report.

## Decision: situational seals, not a fixed seal rotation

The requested Command/AoE and Righteousness/single-target heuristic is useful but incomplete. The actual retained selector also has learned Vengeance/Corruption for sustained NPC/boss work, a low-level learned-Command fallback, explicit manual choices, and independent Judgement selection. Replacing all single-target choices with Righteousness would remove those existing tested policies without character/encounter evidence that it improves damage. No such rewrite was made.

| Situation | Current decision and rationale | Evidence/limit |
|---|---|---|
| Ordinary NPC single target / sustained boss | Retain learned Corruption, otherwise Vengeance, under the existing Auto policy. Boss status prevents incidental adds from forcing the ordinary Command threshold when a stacking seal is available. | Actual selector and retained full-tree tests. Pinned TC seal script looks up the caster's own stack contribution. This is not a measured time-to-die crossover or proof that stacking wins every short fight. |
| Multiple NPCs | Retain existing Command selection at three nearby eligible enemies, with two-target retention once Command is observed; preserve the existing area-admission check and low-level fallback when stacking seals are unavailable. | Pinned AC Command script explicitly provides cleave. The existing helper's safety policy is restricted to the dungeon Combat Bot; it is not universal open-world or PvP CC/obstacle proof. No safe-corridor or maximum-DPS claim. |
| Player target, including open-world player combat | Auto retains learned Righteousness rather than automatically introducing cleave or a stacking DoT. Explicit manual Command, Justice, Light or another learned seal remains authoritative. | Actual three-context linked player-seal tests remain unchanged. This is a conservative immediate-damage/target-control policy, not an arena build/gear simulator. |
| Optional solo NPC sustain | New opt-in Light policy enters only during eligible ungrouped Normal-context combat, in melee, outside the configured emergency-heal window and with the configured mana reserve. Separate entry/recovery thresholds prevent repeated switching near one threshold. | New complete linked-tree regression. Healing amount, proc frequency, net damage and survival improvement are not measured by controlled dispatch tests. Defaults remain off. |
| Emergency survival | This new option spends no seal cast below the larger existing LayOnHandsHealth/RetributionHealHealth threshold. Existing defensive nodes still precede the seal node, and the separate healing behavior remains unchanged. | No promise that a random sustain proc saves an emergency. The feature does not add or certify a comprehensive healing/defensive coordinator. |
| Justice utility | A manual learned Seal of Justice remains available. It is not selected automatically merely because the opponent is a player or low health. | A triggered/random control effect is not a guaranteed stun-on-demand or a measured damage optimum. The actual realm's immunity/diminishing state is not observed by a new planner here. |
| Chasing a moving player or preventing an observed fleeing target | The existing selector can choose **Judgement of Justice while retaining a damage seal**. | Both pinned core implementations apply the selected Judgement aura separately from the active seal's damage component. Seal of Justice is not a prerequisite for that choice. |

Only one selected seal is maintained by this owner. Changing it spends an actual cast opportunity and changes what later attacks can proc; cycling through every seal is not an optimization. A manual setting is retained, rather than the routine repeatedly fighting the operator's recovery/control choice. No exact mana/GCD/damage coefficient is inferred from this statement.

## New setting contract

In Singular's Paladin Retribution settings, keep **Seal=Auto** and enable **Use Solo Seal of Light** to permit the new sustain policy. The default is **false**; this change does not silently change the user's saved XML or deploy a new executable.

| Setting | Default | Meaning |
|---|---:|---|
| UseSoloSealOfLight | false | Opt in to bounded solo PvE sustain; manual seal settings always win. |
| SoloSealOfLightHealth | 50 | Start at or below this health percentage, above the emergency threshold, only with a melee opportunity. |
| SoloSealOfLightRecoveryHealth | 75 | Retain an already observed Light below this percentage; resume the ordinary damage choice at or above it. |
| SoloSealOfLightMinimumMana | 30 | Minimum observed mana percentage to start a new Light cast; the real spell layer must still establish actual usability. |

These defaults are operator-tunable policy, not a native mechanic or tested best survival/DPS threshold. Valid configuration requires0<entry<recovery<=100 and0<=mana<=100. Invalid configuration does not enable recovery. Invalid health observations do not authorize a new seal; invalid mana observations cannot start Light. An existing Light is not repeatedly recast as mana falls or the target leaves melee while recovery is still ongoing.

The optional path requires a current valid alive player and target, actual combat, Normal context, no party/raid, and a nonplayer/nonelite/nonboss target. Existing mounted/transport/casting/channeling/Food/Drink guards remain. No group/raid/battleground/PvP recovery override is added. Eligibility, chosen spell, actor/target references and GUIDs and specialization are revalidated by the same captured TacticsChoice after cast setup. A lost target, joined party, disabled option, insufficient mana or changed recovery state cannot inherit the earlier Light choice.

## PvP: pressure plus deliberate control, not random seal cycling

The existing player-combat factory already puts target/LOS/facing, autoattack and the shared interrupt helper before seal maintenance and ordinary attacks. For a currently observed interruptible cast, the helper can use learned Hammer of Justice and then eligible Repentance, while excluding Protection-only Avenger's Shield from Ret. Actor/target/spec and continuing cast eligibility are captured and rechecked. That behavior remains intact; the new Light option cannot preempt it.

The routine also retains Hand of Freedom, flag-preserving Divine Shield, Forbearance-aware defensive predicates, learned cooldown/readiness checks, proc Exorcism and execute-range Hammer of Wrath. Those existing branches do not amount to a complete PvP strategy. The helper is reactive to its observed casting/interruptibility conditions; it does not newly implement offensive kill-window stuns, off-target healer CC, teammate assignments, trinket prediction, observed DR reset timers or an arena risk planner.

Do not hard-code the claim that Seal of Justice necessarily shares Hammer of Justice's diminishing category. The inspected pinned TC SpellInfo.cpp2428-2429 distinguishes triggered stuns from controlled stuns. Exact spell data, cast context and realm rules are required to establish a particular interaction. Conversely, that distinction does not mean either effect is immune to diminishing returns or that this routine now tracks them. No invented immunity duration or infinite-lockdown claim is admitted.

A comprehensive proactive-control extension needs actual original-client cast/CC/immunity observations, exact learned spells and talents, target/teammate intent, DR provenance and strict revalidation/negative tests. It must also avoid breaking damage-sensitive CC with current attacks or cleave. This is a specific remaining client/server/protocol requirement, not a missing excuse to activate an untested control planner.

## Primary source provenance

The repository source and linked fixtures establish what CopilotBuddy does. The pinned emulator sources establish their own implementation, not the actual user's realm or client result.

1. **TrinityCore/TrinityCore**,3.3.5 reference commit `8fda442f6c30ca21a622638063ab8b28376f1b25`, `src/server/scripts/Spells/spell_paladin.cpp`: Judgement1199-1247; Righteousness1654-1689; own-caster Vengeance/Corruption1691-1785. Retained source `W110-core/TC-spell_paladin.cpp`. URL: https://raw.githubusercontent.com/TrinityCore/TrinityCore/8fda442f6c30ca21a622638063ab8b28376f1b25/src/server/scripts/Spells/spell_paladin.cpp
2. **TrinityCore/TrinityCore**, same commit, `src/server/game/Spells/SpellInfo.cpp`: Paladin-specific diminishing limit2380-2388 and mechanic-to-triggered/controlled-stun distinction2428-2429. Retained source `W110-core/TC-SpellInfo.cpp`. URL: https://raw.githubusercontent.com/TrinityCore/TrinityCore/8fda442f6c30ca21a622638063ab8b28376f1b25/src/server/game/Spells/SpellInfo.cpp
3. **azerothcore/azerothcore-wotlk**, reference commit `8337a378ac325e62a6a91e00c6a5e944205e8536`, `src/server/scripts/Spells/spell_paladin.cpp`: Command176-245, Light proc admission263-277, Judgement949-1019. Retained `W110_SEAL_AC_PALADIN_20260928.cpp`, SHA256 `adb05669e67ef36e0ecbcaa6c92aa6f5a67862208927873f0764f3581b9446db`. URL: https://raw.githubusercontent.com/azerothcore/azerothcore-wotlk/8337a378ac325e62a6a91e00c6a5e944205e8536/src/server/scripts/Spells/spell_paladin.cpp

The AC Judgements-of-the-Just/Command extension is recorded as AC behavior, not silently substituted for the shorter TC implementation. Three additional known-path TC effect/unit files were retained for a targeted symbol search; a missing name match is not evidence that the mechanics do not exist elsewhere. Mixed Classic/TBC/SoD/Cataclysm search results were not admitted as original335 mechanics or authority to change source.

## Full W110 disposition and acceptance

The current collection/caller, native timer, memory-read/address, metadata, AutoEquip, dispatch and terrain repairs were already fully verified at6f74e3cd/d9d76af1 before this extension. Their exact historical artifacts are preserved. Earlier450f1fe1, broad native-map/package, status/ref/comparison and finish refusals remain frozen at their own identities. This new seal report is not equivalent reconstruction of those denied outputs.

The published W110_COLLECTION_GOAL_DISPOSITION.md continues to map every original objective to available evidence and exact remaining native/client, server, realm-data, world/route, protocol and independent-review inputs. This extension adds the requested seal/sustain/PvP review and bounded option only. DensePullIsolationValidated remainsfalse. No current native/IDB mutation, local project execution, productionCB access, subagent, merge/deployment, full-gameplay certificate or invented W111 is supplied. Final source/test/artifact/publication results must be read from the new checkpoint before claiming this option is implemented and verified.
