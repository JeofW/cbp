# Wholesome sale boundary — test-first follow-up

Base: approved master merge 8382a7ec05a64212ea0a237159dca427a0767425. Separate from relation identity repair. No merge/deployment of this slice is authorized implicitly by the previous merge approval.

WholesomeAutoQuest.SellByQuality at 2379–2435 builds protection from scheduler.ActiveQuestIds and passes null protected names, bypassing shared file/profile/runtime protections. The merchant API uses only explicitly provided exception lists. Relevant owners: WholesomeAutoQuest, QuestLog.GetAllQuests, ProtectedItemsManager, MerchantFrame.SellItemQualities, Vendors.StartSellSession and CollectItemObjective lifetime.

The regression build extracts the exact method text from the checkout, reports its source hash and links the actual ProtectedItemsManager/DualHashSet/ValuePair sources. World, database containers, consumables and merchant dispatch are controlled boundaries. It is not a reimplementation of the selling decision and does not execute native Lua or sell real items. The full host/Wholesome suite must also compile the complete production class after repair.

Preserve selected/upcoming quest protection, but union it with every current accepted quest, including completed quests waiting for turn-in, and all shared protected IDs/names. Unknown accepted quest data, unavailable player/log/database must not authorize destructive selling. Preserve quality, food/drink, ammunition/reagent/key controls and cancellation. No blanket protection of unrelated inactive dataset records. The snapshot must be refreshed for each sale invocation.

This does not solve owner-scoped protection release in CollectItemObjective, inventory changes inside an existing Lua batch, incomplete original-client snapshots, vendor selection flags, or every sell/mail/discard caller. Those are distinct contracts and remain open. Outcomes are pending until actual test logs are inspected.
