@GitHub

Continue `jeofwong/CopilotBuddy-private`, draft PR51 branch `audit/next-55-equipment-observation-20260917`, from W72. Reconcile live refs, then read `AUDIT_RESUME.md`, `docs/audit/2026-09-19/W72_CHECKPOINT.md`, `W72_EVIDENCE.json` and the governing 3.3.5/core/provenance policies.

Verified head **7a471da4d674ca1dffdc6169340b091e96a36e46**, tree **3bafec33ca952646ff1093f2a4a84d9a82c9e1bf**. Integrated **35431678604/art10580349205** passes17/17; quest-log owners **35431678616/art10579879846** pass; host **35431678735/art10580898437** is0 errors/3340 warnings.

Retain: TC335 dependent previous OR; exact negative direct parent status (accepted incomplete only); raw FailedQuestIds; safe container use/UseItemOn; all W71/W70 merchant/equipment/buff/navigation/addon evidence. Pinned AC WotLK negative-parent semantics are broader and remain an explicit divergence.

First next slice: safe single-item cursor pickup in `WoWItem`, test first. Current `PickUp()` still uses independent BagIndex/BagSlot. Original3.3.5 APIs confirmed: `GetCursorInfo`, `GetContainerItemLink`, `PickupContainerItem`, `CursorHasItem`. Require empty cursor without calling ClearCursor, GUID slot resolution/revalidation, expected-entry Lua check before pickup, and cursor-item acknowledgement after pickup. Keep same-entry ABA/live acceptance limitations explicit.

Do NOT blanket migrate callers. DeleteItems/MrItemRemover, EquipItem/AutoEquip, AuctionHouse and ProfessionBuddy each have separate ownership/acknowledgement semantics. Original3.3.5 DeleteCursorItem can trigger DELETE_ITEM / DELETE_GOOD_ITEM; quality3+ DELETE_GOOD_ITEM requires DELETE_ITEM_CONFIRM_STRING in an edit box before accept. One DeleteCursorItem invocation is not deletion success.

Do not merge PR51 without explicit approval. For connector stalls use exact SHA/run IDs, short reads and bounded retries.
