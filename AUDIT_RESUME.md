# Resume at W70 — GossipEvent and plugin refresh reuse verified

Repo `jeofwong/CopilotBuddy-private`, draft PR51, branch `audit/next-55-equipment-observation-20260917`. Reconcile live refs first.

Verified code **2456369563478b1f0c11c902649d7b308169ddfc**, tree **90501a9a3233d9bd043621abf2ae5671c658cf05**.
Read `docs/audit/2026-09-19/W70_CHECKPOINT.md` and `W70_EVIDENCE.json`, then W69 and the original-client/provenance policies.

Exact-head green:
- integrated **35425658391 / art10578078516**
- host **35425658309 / art10578774562**
- quest-log owners **35425658361 / art10578159821**

New retained groups: PluginRefreshReuse9/9 and GossipEvent14/14. Earlier PullIsolation12/12, AuraCount8/8, CollectThingsBreath7/7, QuestStrategyExecution9/9, equipment groups remain green.

NEXT: native container slot identity. Current WoWItem.UseContainerItem independently reads BagIndex and BagSlot; unresolved BagIndex=-1 aliases backpack. Prepared test blob **0970290ab69cc54bc8a39f98e8814f7f6c081420**. Prepared production blob **ffc12d34d2971c944ff32196f3fd1ede83361f5e** is off-branch only and must not be published before a clean intended red. After repair, audit authoritative UseItemOn/callers so failed safe slot resolution cannot count as a successful dispatch.

Escort stays unwired. All-class buff strength/ownership, equipment caps/loadout, raw addon terrain, full underwater/GatherBuddy, native cursor/LOS and live acceptance remain open.

For GitHub polling failures: exact head + run ID, short one-shot reads, bounded retries; no long polling or blind reruns.
