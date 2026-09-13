# Owner follow-up: Wholesome and 3.3.5a group combat

Baseline: d62f2c7b73305087dd23f45d8e881ae38d3ab3e7 (draft PR #28). Preserve prior source and evidence; no master merge, deployment or binary replacement.

## Current owner priorities

Wholesome is the primary quester. Verify missing NPC offers and already-carried objective items rather than assuming these cases are absent. Preserve original WotLK 3.3.5a build 12340 commands and Lua signatures, not modern Wrath Classic/retail APIs. Dungeon/raid combat must not initiate unrelated pulls. Paladin blessings and aura coverage need separate, stable ownership decisions. Retribution needs actual learned-spell Disease/Poison cleansing for self and group, with Cleanse's additional Magic capability and encounter exceptions.

## Source-supported findings

- Retribution Heal contains emergency/self heals but no Purify/Cleanse action. Dispelling.cs declares class capabilities but no Paladin cast call uses them. The omission is code-confirmed; the exact historical disease aura is not captured by the user's new report.
- Common.FindKingsTarget treats Mark of the Wild as Kings coverage. Greater Blessing variants are not considered. Auto Wisdom never activates; it requires an explicit setting. Three independent selectors lack one per-recipient complementary blessing decision. Aura selection does not account for other Paladins' coverage.
- DungeonEngagementPolicy.IsEngaged permits an assist-selected target. Unit's caller allows a tank/leader's current target while the player, not necessarily the enemy, is in combat. Shared dispatch and auto-attack must be audited as additional boundaries, including disabled target switching and AoE neighbors. A current-target selection is not proof of engagement.
- Wholesome QuestScheduler already captures carried inventory counts and filters item objectives by item identity. QuestPickupDialogPolicy/Tracker already distinguish unknown dialogs from a loaded missing offer and bound distinct interaction cycles. Extend those regressions and trace their actual adapters; do not falsely claim every quest works or delete legitimate prerequisites.

## Repair order and tests

1. Add linked-source Paladin support tests before modifying behavior. Use controlled external world/dispatch observations, actual Common/Retribution/TreeSharp decisions. Assertions verify the selected recipient, no swallowed exception, Greater/normal coverage, caster ownership, missing spells, raids, movement/rest preservation, and safe cleansing. Tests are not server-acceptance simulations.
2. A shared support owner chooses one useful blessing per recipient from observed coverage, recognizes Greater variants and preserves explicit settings; separate aura coverage from blessings. No speculative teammate spellbook/role APIs. Add an actual Retribution cleansing call after emergency Lay on Hands and before ordinary heals, and precombat cleansing before maintenance buffs. Select learned Purify or Cleanse using actual removable types; never remove a protected type incidentally just to cure a different type. Keep encounter-sensitive dispels manual and provide disable switches.
3. Add failing no-pull reproductions for assist-only selection, unrelated combat, target switching, direct dispatch, auto-attacks and AoE. Repair the owning boundaries without disabling ordinary solo quest pulls or battleground targeting.
4. Expand Wholesome missing-offer/inventory edge tests, record compatibility findings and remaining integration/live gaps. Run affected and combined suites before PR claims. Update the directed graph/checkpoint.

No test-first result is assumed at this commit. Broader route/native work from AUDIT_RESUME.md remains open and is not marked complete by these support tests.
