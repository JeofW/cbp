@GitHub

Continue `jeofwong/CopilotBuddy-private`, draft PR51 branch `audit/next-55-equipment-observation-20260917`, from W76. Reconcile live refs, then read `AUDIT_RESUME.md`, `docs/audit/2026-09-19/W76_CHECKPOINT.md`, `W76_EVIDENCE.json` and the governing original3.3.5/core/provenance files.

Verified tested head **698068344bbc51f79d81f76e7d3b91453f29de0c**, tree **77dd5c926b45a82f2961f89f95e058111d7cc0d1**. Integrated **35441935790/art10583728386** passes17/17; host **35441935725/art10583953031** passes; Equip cursor ownership19/19 assertions0 unexpected0. Retain W75 container identity20/20 and MrItemRemover delete14/14.

Retain W76 equip contracts: stable GUID/entry/intended-slot ownership; shared validated `TryPickUp(out bag,out slot)`; no independent BagIndex/BagSlot mutation; no `ClearCursor`; exact cursor entry before `EquipCursorItem`; intended equipment-slot GUID acknowledgement; bounded pending transaction; guarded displaced-item return; one managed AutoEquip transaction; exact EQUIP_BIND/AUTOEQUIP_BIND popup type plus `dialog.data` slot match; unknown-slot bind prompts fail closed.

First next slice: **AuctionHouse core cursor/post ownership only**, test first. Audit:
- `Styx/WoWInternals/Misc/AuctionHouse.cs`
- `Styx/Logic/Inventory/Frames/AuctionHouse/AuctionHouse.cs`

Require stable physical item GUID/entry/container identity, safe cursor admission, current AuctionFrame/sell context, acknowledgement that the intended item reached the auction sell slot before posting, no raw `ClearCursor`, bounded pending/no-progress handling, and late context revalidation immediately before `StartAuction`. Preserve query/search/bid/buyout/cancel behavior.

Do **not** include ProfessionBuddy in the AuctionHouse core repair. After that is separately green, audit ProfessionBuddy's independent cursor paths beginning with `SellItemOnAhAction.cs`.

Preserve original WoW 3.3.5a build12340; TrinityCore3.3.5 primary and AzerothCore WotLK secondary for server semantics. Do not infer mechanics from Wrath Classic/modern sources. Preserve all W71-W76 open requirements and do not merge PR51 without explicit user approval.
