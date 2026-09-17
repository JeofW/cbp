# W54 — original-client policy, shared buff dispatch and loot permissions

17 September 2026. Repository `jeofwong/CopilotBuddy-private` (ID1367174964), draft PR47, branch `audit/next-47-flight-owner-boundaries-20260916`.

## Current verified state

Latest verified code is **06ab57a31bf2a1634b6dafaf781fa70f73197623**, tree **53c2da7ecd31c7ea17de454d95a7d24bc96d67aa**. All four focused Windows x86 project builds/runs and all seventeen integrated entries pass on that exact code. Host compilation also passes with **0 errors and 3,296 warnings**; the host job is compile-only and explicitly reports tests_run:false, game_attached:false.

Public CI is already authorized, implemented and running. Older W51/W52 statements about pending approval or skipped current jobs are obsolete for this revision. Do not reapply the old public-CI patch or recreate the later upstream/escort/loot repairs. This continuation successfully used native GitHub publishing actions; local files are not being substituted for committed work.

Master was freshly read at **518baec545cedc8fe219afc0861c0e8cb9475a8f**, preserving its separate README-only child of the approved PR46 merge. PR47 is still draft/unmerged. No master merge, force push, deployment, installed binary or mesh replacement occurred. Preserve the earlier backup refs and PR25/43/45 exclusions; do not remerge historical PR44.

## Fixed target and durable source policy

Read **docs/audit/WOTLK_335A_RESEARCH_POLICY.md** before further research or implementation. It was committed in this continuation as **9231739e5f0a78e35ce1a3a467e55d4651ca95ad** and applies to every class/spec, item, stat, buff, quest and native/client API.

The target is original WoW **3.3.5a build12340**, not Wrath Classic3.4.x, Cataclysm or Retail. A webpage labelled WotLK is not enough. Record version, date/ref, exact supported claim and uncertainty. Separate original-compatible evidence, separately corroborated claims, comparison-only guides and unresolved mechanics. An emulator implementation is not automatic proof of the owner's realm configuration. No new numerical DPS coefficients or later-version mechanics were imported in W54.

## Repair 1 — shared buff coverage must survive yielded setup

The inherited shared helpers checked aura coverage in an outer decorator, then entered Cast, which could yield for setup. The later cast resolved its target again but no longer carried the coverage test. An aura arriving during setup could therefore fail to prevent a duplicate submission. A separate overload, BuffSelf(int), incorrectly delegated to the current-target overload.

The already committed 54-case SharedBuffDispatchRegressionTests produced genuine integrated Windows red at **053fa9f3**: **36/54 passing, 18 intended assertion failures, zero unexpected errors**. It executes the exact contiguous production Cast/Buff region with real TreeSharp and controlled observations/dispatch. Examples cover ten class-associated buff names, ID self/target overloads, caller-declared equivalents, per-caster myBuff semantics, unavailable targets, retry controls and an intentional aspect transition. It is not a complete execution of every class/spec rotation.

Production repair **e3004d6c** moves coverage admission into the selector that Cast resolves again after setup, and makes the ID self overload select the player. The 54 new cases passed, but integration correctly caught two older isolated null-input predicate failures: those tests bind factory parameters without constructing the newly derived selector closure. The overall e300 result was **not green**, even though its downloaded archive filename contains the word green.

Follow-up **70487ce1** retains explicit independent outer parameter guards before using the selector. No test or fixture was changed. The final result is **54/54**, with the original **13/13 routine-boundary cases** and all17 integrated entries passing. Comparing053fa9f3 to70487ce1, only Helpers/Spell.cs differs among source/configuration inputs; all original/generated tests and workflows remain identical.

This fixes shared dispatch correctness, not automatic discovery of every equivalence group or effective rank strength. It preserves intentional transitions rather than globally freezing all seals/aspects/presences/stances. Final logger callbacks, full actor lifetime, globally name-keyed retry state and all-class singleton ownership need their own evidence before broader claims.

## Repair 2 — equipment score cannot grant an unavailable loot roll

The actual SmartLootRoller consumer selected Need from item score without consulting the original client's canNeed flag, and could also submit unavailable Greed. The original-era UI contract was verified in `tekkub/wow-ui-source`, tag3.3.5, commit **c4e0255fc574598428ee1c25f160ab949798bc98**, `FrameXML/LootFrame.lua`: canNeed, canGreed and canDisenchant are separate fields that enable or disable their respective controls.

The existing four-owner SmartLoot harness was extended by36 cases while preserving all43 prior scenarios: the eight permission combinations across configured DE and Pass/Greed fallback policies, plus matching/nonmatching Greed controls. The initial added fixture had a local variable-name collision, corrected in test-only **2b361933** without changing expectations. Its actual Windows result is **61/79 passing, 18 intended assertion failures, zero unexpected errors**, with the other16 integrated entries passing.

Repair **06ab57a3** adds only client capability admission to SmartLootRoller.cs. A scored upgrade with unavailable Need follows the existing configured nonmatching fallback; unavailable Greed becomes Pass. Explicit DE still requires the user's setting and observed capability. It preserves the earlier saved-enum Pass3-to-clientPass0 repair and Mp5 mapping, and changes no stat weights, armor/weapon policy, auto-equip behavior or item-binding settings.

The unchanged79 cases now pass. The red/green pair differs only in SmartLootRoller.cs among source inputs, with identical generated tests/workflows and the other79 aggregate group outcomes unchanged. These tests use the actual scoring/settings/preset/decision owners with controlled item/world/Lua boundaries; no live roll or equipment binding is claimed.

## Exact-source final evidence

| Execution | Run | Artifact | Result |
|---|---:|---:|---|
| Integrated |35205551275|10490106473|17/17 build/run entries pass|
| Focused current |35205551161|10489912107|4/4 projects build/run successfully|
| Host |35205551093|10489852215|Build0, 3,296 warnings, 0 errors; compile only|

The final focused and integrated archives have **1,761 identical source/configuration hashes**, **92 identical normalized members** after their known directory-prefix normalization, and **80 matching aggregate group outcome inventories**. The focused archive's115 exported source files all match those input hashes. All112 focused and136 integrated internal-manifest members, authenticated outer digests and ZIP CRCs were checked. The host archive has no internal manifest; none is invented.

Seven original archives are retained, including both clean red runs, the intermediate compatibility failure, and the final focused/integrated/host set. Two unchanged-fixture red/green comparisons are independently reproduced by the portable verifier. Its12 Python utility tests pass; they are evidence-verifier tests, not additional bot C# scenarios. Included changed production and test payloads are also bound to actual CI input hashes. See W54_EVIDENCE.json and the offline INDEX.json/VERIFIED.json for full IDs, hashes and scope.

The separate historical-baseline workflow arm is not included in these current pass claims and was not newly reverified here. Submission success, server acknowledgement, emulator behavior and original-client acceptance remain different evidence levels.

## Equipment answer: Strength and Agility are weighted, but policies disagree

See **W54_GEAR_RESEARCH.md** for the complete source and research assessment. SmartLoot's existing Ret preset assigns WeaponDps7.5, Strength2.7, Hit2.2, Expertise2.2, Agility1.8, Crit1.7, Haste1.4, ArmorPenetration1.2 and AttackPower1.0. The runtime uses saved individual weights; the existence of a preset does not establish the user's currently selected profile or automatic spec synchronization.

AutoEquip2 uses a different talent-selected XML scale: DPS4.7, Hit1, Strength0.8, Expertise0.66, Crit0.4, AP0.34, Agility0.32 and other entries. It also applies a primary-stat gate that can reject an Agility-only item for Need when Strength is primary. These are not simply normalized copies of one model. A positive weighted score is not proof of optimal DPS or authority to ignore main-spec/client loot rules.

A3.3.5a emulator's Strength-to-AP and Agility-to-physical-crit code supports distinct contributions, but does not validate either bundled coefficient set. No coefficient was changed without a supported level/gear/talent/cap/encounter model. Missing/partial stats, cap-aware valuation, set/proc effects, competing equipment owners and actual quest-reward dispatch remain open. A separate legacy AutoEquipper recursion finding is source-only until its live caller path is established.

## Escort, buffs and prior repairs: retain rather than recreate

The latest verified run retains **47/47 Escort observation cases**, **63/63 blessing contribution cases**, the original **44/44 Paladin support controls**, **35/35 aura-cancellation cases**, and the later database/packed-row/merchant/loot-range/rest groups. The earlier manual-Might Battle Shout bypass is already repaired. Its conservative deferral is not an effective-rank optimizer.

Legacy Escort threat/arrival/defense corrections are now committed and tested; they do **not** add an automatic Wholesome escort objective strategy. Quest-specific start information, captured NPC identity, bounded following/reacquisition, protection against scheduler/vendor interruption and authoritative credit still require integration. Do not infer automatic support from Escort.cs existing or player arrival alone.

Retain the existing behavior-tree executor. Further work should strengthen trustworthy observations, role permission, contextual decisions and final dispatch admission, not add an uncoordinated second rotation engine. Original3.3.5a research policy applies throughout.

## Remaining acceptance requirements

Continue supported automatic escort/event quest integration; rank/magnitude-aware and mutually exclusive buffs across all specs; actor-safe utility/threat-aura cleanup; unified cap/loadout-aware equipment policies with separate loot/equip permissions; underwater air/obstacle/shoreline recovery; remaining selective upstream compatibility and merchant/native lifetime work; independent review, representative performance and supervised original-client acceptance.

The two W54 fixes are committed and have no pending unexecuted test at the verified code head. This is not an exhaustive-completion, universal buff-intelligence, maximum-DPS or release-ready claim. Old W53 local proposals must be compared with the new committed helpers before any replay. Preserve all earlier unmet requirements and recorded failures.
