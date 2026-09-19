# Resume at W73 — validated single-item cursor pickup verified

Repo `jeofwong/CopilotBuddy-private`, draft PR51, branch `audit/next-55-equipment-observation-20260917`. Reconcile live refs first.

Verified code/test head **3c76cddb12dbaef94ae7e6f121bb2678387f3471**, tree **154ce386ab94df0065b27a87c66c1416caa01847**.

Read `docs/audit/2026-09-19/W73_CHECKPOINT.md` and `W73_EVIDENCE.json`, then W72 and the original-client/core/provenance policies.

Exact green:
- integrated **35432153183 / art10580649556** — 17/17
- host **35432153030 / art10580249866** — 0 errors / 3342 warnings
- container slot identity + pickup **16/16**

New retained contract: `WoWItem.TryPickUp()` reuses GUID slot resolution/revalidation, requires empty `GetCursorInfo()`, validates expected item entry before `PickupContainerItem`, requires `CursorHasItem()` after, never ClearCursor, and public PickUp delegates to it. Same-entry ABA/live acceptance remain open.

NEXT: standalone `runtime-snapshot/Quest Behaviors/DeleteItems.cs` first, test-first with a dedicated compiled-behavior harness. Current OnStart loops matching items, calls PickUp/DeleteCursorItem and immediately marks done. Original3.3.5 DeleteCursorItem can open DELETE_ITEM or DELETE_GOOD_ITEM; quality3+ requires DELETE_ITEM_CONFIRM_STRING in the exact popup edit box. One invocation is not deletion acknowledgement.

Required next repair: safe pickup success gate, exact cursor/item ownership, exact popup identity, actual item absence acknowledgement, bounded pending/retry, no done-on-invocation. Do not fold MrItemRemover into the same repair. Its generic DELETE_ITEM_CONFIRM handler and many ClearCursor/delete sites need a separate owner audit.

Then EquipItem/AutoEquip, AuctionHouse and ProfessionBuddy cursor transactions separately. Do not merge PR51 without explicit approval. Use exact SHA/run IDs and bounded reads for connector stalls.
