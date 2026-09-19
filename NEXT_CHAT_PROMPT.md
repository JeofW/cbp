@GitHub

Continue the W77 regression review on `jeofwong/CopilotBuddy-private`, draft PR51, branch `audit/next-55-equipment-observation-20260917`. Read live refs, `AUDIT_RESUME.md`, `docs/audit/2026-09-19/W77_REVIEW_SCOPE.md` and all original3.3.5/core/data/addon provenance policies. Do not restart from the stale W66 PR body or W76's auction next-step prose.

Owner's review cutoff is18 September2026 21:58 Asia/Kuala_Lumpur, equal to13:58 UTC. Cutoff ancestor4c6b1b2e75af4dd0eaffde11d38ebbc84a21f4c0; review-entry headbf1cd682c229a7f0143d5f3e54a6949c53ad8c44;125 intervening commits. The current scope is Wholesome questing, navigation and Singular, with custom-behavior replay after Stop/Start, item/objective completion and Gordunni Cobalt as concrete cases.

The unfinished AuctionHouse transaction experiment is being reverted in exactly its four changed paths to the pre-experiment ac03b01e versions. Keep its history/evidence but do not continue AuctionHouse or ProfessionBuddy. Verify the actual resulting SHA, full integrated retained suites and host build; a scoped rollback is not proof that legacy auction code is safe.

Review and reproduce before repairing: UseItemOn/GossipEvent reset their progress baseline and can miss already-completed objectives; generated guards admit on HasQuest alone, before transport preambles. W76 equip/delete tests mainly exercise compilation/source/pure helpers, not full cursor lifecycles. The equipped-acknowledgement branch may bypass timeout; displaced-cursor cleanup does not capture the displaced item. Do not weaken assertions or claim live behavior from source-token presence.

Use current authoritative quest/inventory state rather than a global once-ever 'done' cache. Preserve repeatable quests, unfinished objectives, unknown observation deferral, ownership/cancellation, buffs, combat, navigation and prior protections. Verify Gordunni's original-client/TC335 and AC recipe before implementing a shovel/location/spawn/loot path; a mound hotspot is not an executable recipe.

Original WoW3.3.5a build12340 only. No desktop mouse automation, modern/Classic API assumptions, PR51 merge, force push, deployment or installed file replacement. Record bounded progress and actual failures honestly; the full review is not complete merely because a CI badge is green.
