@GitHub

Continue `jeofwong/CopilotBuddy-private`, draft PR51 branch `audit/next-55-equipment-observation-20260917`, from W73. Reconcile live refs, then read `AUDIT_RESUME.md`, `docs/audit/2026-09-19/W73_CHECKPOINT.md`, `W73_EVIDENCE.json` and governing 3.3.5/core/provenance files.

Verified head **3c76cddb12dbaef94ae7e6f121bb2678387f3471**, tree **154ce386ab94df0065b27a87c66c1416caa01847**. Integrated **35432153183/art10580649556** passes17/17; host **35432153030/art10580249866** is0 errors/3342 warnings; container slot/pickup group16/16.

Retain `TryPickUp`: empty-cursor GetCursorInfo gate, GUID slot resolution/revalidation, expected-entry Lua check, PickupContainerItem, CursorHasItem acknowledgement, no ClearCursor. Do not recreate or broaden it into multi-step transaction ownership.

First next slice: standalone `runtime-snapshot/Quest Behaviors/DeleteItems.cs`, test first with an actual compiled behavior/control boundary. It currently mutates only in OnStart, calls item.PickUp + DeleteCursorItem for every requested ID, then marks done. Original3.3.5 DELETE_ITEM_CONFIRM opens DELETE_ITEM below quality3 or DELETE_GOOD_ITEM at quality3+; both need a second owned delete action and DELETE_GOOD_ITEM requires DELETE_ITEM_CONFIRM_STRING. StaticPopup_FindVisible exists. Require safe pickup, exact popup ownership, actual item disappearance acknowledgement, bounded pending/retry and no completion on invocation alone.

Do NOT repair MrItemRemover in the same commit. Its plugin attaches DELETE_ITEM_CONFIRM and generically clicks StaticPopup1Button1 when a current target exists; it needs its own regression. EquipItem/AutoEquip, AuctionHouse and ProfessionBuddy likewise remain separate.

Preserve all W72/W71 requirements and do not merge PR51 without user approval.
