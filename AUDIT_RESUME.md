# Resume at W76 — owned EquipItem/AutoEquip cursor transactions verified

Repo `jeofwong/CopilotBuddy-private`, draft PR51, branch `audit/next-55-equipment-observation-20260917`. Reconcile live refs first.

Verified code/test head **698068344bbc51f79d81f76e7d3b91453f29de0c**, tree **77dd5c926b45a82f2961f89f95e058111d7cc0d1**. W76 documentation is newer than the tested code head.

Read `docs/audit/2026-09-19/W76_CHECKPOINT.md` and `W76_EVIDENCE.json`, then W75/W74 and the governing original3.3.5/core/provenance files.

Exact W76 green:
- integrated **35441935790 / art10583728386** — 17/17
- host **35441935725 / art10583953031** — success
- Equip cursor ownership **19/19**, assertions0, unexpected0
- W75 container identity20/20 and MrItemRemover delete14/14 retained

Primary clean red was **6622f782**, integrated **35437261168/art10583270460**: Equip4/17,13 intended assertions,0unexpected; both owners compiled. Host **35437261170** green.

Residual original-3.3.5 popup red was **86333b35**, integrated **35441689706/art10583628215**: Equip17/19,2 intended assertions,0unexpected. Host **35441689712/art10583752980** green.

Retain W76 production:
- **1315fecf** validated `TryPickUp(out bag, out slot)` source location,
- **fb11f02d** owned `EquipItem` transaction,
- **6bf484f2** owned `AutoEquip2` transaction,
- **69806834** exact bind-popup type + `dialog.data` equipment-slot ownership; unknown-slot bind confirmation fails closed.

Pinned original UI evidence: `wowgaming/3.3.5-interface-files@d0339b17...` — EQUIP_BIND/AUTOEQUIP_BIND call `EquipPendingItem(slot)`; `StaticPopup_Show` stores `dialog.data`.

NEXT: **AuctionHouse core only**, test first:
- `Styx/WoWInternals/Misc/AuctionHouse.cs`
- `Styx/Logic/Inventory/Frames/AuctionHouse/AuctionHouse.cs`

Current risks: raw `ClearCursor`, raw/caller bag-slot pickup, no stable GUID/entry transfer proof, no sell-slot acknowledgement, no late AuctionFrame context check before `StartAuction`. Preserve search/browse/bid/buyout/cancel. Do not include ProfessionBuddy in this repair.

After AuctionHouse core, audit ProfessionBuddy separately, beginning with `runtime-snapshot/Bots/ProfessionBuddy/Composites/SellItemOnAhAction.cs`.

Retain all W71-W76 open requirements, including core/data provenance, dependency semantics, authoritative recipe/event/escort work, buffs/PallyPower, gear/loadout/caps, addon terrain, underwater/GatherBuddy, native LOS/ABI, same-entry ABA/cross-owner coexistence and supervised original-client acceptance.

Do not merge PR51 without explicit approval. Use exact SHA/run/artifact IDs and bounded reads.
