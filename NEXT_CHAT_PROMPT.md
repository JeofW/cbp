@GitHub

Continue `jeofwong/CopilotBuddy-private`, draft PR51 branch `audit/next-55-equipment-observation-20260917`, from W74. Reconcile live refs, then read `AUDIT_RESUME.md`, `docs/audit/2026-09-19/W74_CHECKPOINT.md`, `W74_EVIDENCE.json` and the governing original3.3.5/core/provenance files.

Verified tested head **67411dbbd765a6d781809b7c18d4ca8e1a20d24a**, tree **2878b68b99bacf93079f48c1a99efacff803aa61**. Integrated **35435502277/art10582149126** passes17/17; host **35435502301/art10581398494** passes; DeleteItems12/12 assertions0 unexpected0.

Retain W73 + post-W73 single-item pickup: GUID slot resolution/revalidation, empty cursor, expected entry before PickupContainerItem, exact expected cursor item acknowledgement afterward, no ClearCursor. Retain W74 DeleteItems: tick-owned exact GUID/entry pending transaction, TryPickUp success gate, exact DELETE_ITEM/DELETE_GOOD_ITEM handling, DELETE_ITEM_CONFIRM_STRING for quality3+, cursor-release + physical GUID disappearance acknowledgement, bounded timeout, fail closed without stealing cursor.

First next slice: `runtime-snapshot/Plugins/MrItemRemover2` destructive deletion only, test first. Current source has multiple ClearCursor -> PickUp -> DeleteCursorItem paths, other direct delete paths, a global DELETE_ITEM_CONFIRM handler that clicks generic StaticPopup1Button1 merely when CurrentTarget exists, and IsQuestItem derives BagIndex + 1 and BagSlot + 1 independently before destructive eligibility.

Require one pending delete owner, TryPickUp success, exact GUID/entry/cursor ownership, exact popup identity, exact high-quality edit-box confirmation, no generic popup click, no ClearCursor, bounded pending/disable reset, cursor release + actual GUID absence before completion, and stable container identity for quest-item protection. Do not refactor selling/opening/combining unless needed to preserve compatibility.

Then handle EquipItem/AutoEquip, AuctionHouse and ProfessionBuddy cursor transactions separately. Preserve all W73/W72/W71 open requirements and do not merge PR51 without explicit user approval.
