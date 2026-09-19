# W77 — post-cutoff regression review; questing scope only

Read `docs/audit/2026-09-19/W77_REVIEW_SCOPE.md` FIRST, then `docs/audit/WOTLK_335A_RESEARCH_POLICY.md`, `TRINITYCORE_335_COMPATIBILITY.md`, `QUEST_DATA_PROVENANCE_335.md`, `ADDON_EVIDENCE_335.md`, and the earlier W69-W76 evidence.

Repository `jeofwong/CopilotBuddy-private`, draft/unmerged PR51, branch `audit/next-55-equipment-observation-20260917`. Reconcile live refs before writing. The owner now requests review of all changes after 18 September 2026 21:58 Malaysia time (13:58 UTC), not further auction feature development.

Review entry head: bf1cd682c229a7f0143d5f3e54a6949c53ad8c44, 125 commits after cutoff ancestor4c6b1b2e75af4dd0eaffde11d38ebbc84a21f4c0. The unfinished five-commit auction experiment after ac03b01e is being withdrawn from the active tree only; history and evidence remain. This rollback does not certify legacy auction behavior.

Last preceding integrated-green code698068344bbc51f79d81f76e7d3b91453f29de0c/run35441935790 had17/17 entries and host35441935725 success. These are real CI results but NOT full equip/delete runtime acceptance: the new cursor groups mainly compile owners and check source/pure helpers. Do not repeat W76's stronger lifecycle claims without actual execution evidence.

Current priorities: custom-behavior restart/completed-objective admission, inventory-aware progress, UseItemOn/GossipEvent safety, source-qualified Gordunni Cobalt shovel/location/spawn/loot support, navigation and Singular review. Review W76 displaced-cursor and timeout risks. Do not write a universal once-ever completed-behavior cache.

Do not continue AuctionHouse/ProfessionBuddy. No desktop mouse automation, master/backup writes, force push, PR51 merge, deployment or installed file replacement. Original WoW3.3.5a build12340; TC3.3.5 primary/AC WotLK secondary. Unknown state remains unknown. Full review and live acceptance remain open until verified and documented.
