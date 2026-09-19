# Resume at W75 — owned MrItemRemover deletion verified

Repo `jeofwong/CopilotBuddy-private`, draft PR51, branch `audit/next-55-equipment-observation-20260917`. Reconcile live refs first.

Verified code/test head **4e7c7699d577bbc6922bdbc12adea30e17c12450**, tree **ffd735430f64cdfdf91af3ce3d1d41d2f53f94db**. W75 documentation is newer than the tested code head.

Read `docs/audit/2026-09-19/W75_CHECKPOINT.md` and `W75_EVIDENCE.json`, then W74/W73 and the governing original3.3.5/core/provenance files.

Exact green at tested head:
- integrated **35436934243 / art10582366699** — 17/17
- host **35436934248 / art10582840978** — success
- container slot identity **20/20**, assertions0, unexpected0
- MrItemRemover delete **14/14**, assertions0, unexpected0

Retain W74 DeleteItems and W73 single-item pickup contracts. New W75 production:
- **7cb5dcbc** adds validated GUID/slot/entry `GetContainerItemQuestInfo` preserving original 3.3.5 `isQuestItem, questId, isActive`,
- **6335967d** adds one owned MrItemRemover delete transaction, exact cursor entry, exact delete popup identities, high-quality confirmation, absence acknowledgement, timeout and no ClearCursor,
- **4e7c7699** services pending delete state from Pulse and resets managed ownership on enable/disable without stealing the cursor.

Clean W75 behavioral red was **f0b2cba9**, integrated **35436213745/art10582725289**: MrItemRemover2/14,12 intended assertions,0unexpected; plugin compiled. Host **35436213791** was green.

NEXT: `runtime-snapshot/Quest Behaviors/EquipItem.cs` + `runtime-snapshot/Plugins/AutoEquip2/AutoEquip.cs`, test first and scoped to equip cursor ownership. Current risks: fire-and-forget completion, independent BagIndex/BagSlot reads, AutoEquip ClearCursor, generic StaticPopup1Button1, and no exact item/cursor/equipment-slot acknowledgement. Preserve ammo/scoring/gear-selection behavior; do not fold AuctionHouse or ProfessionBuddy into this slice.

After EquipItem/AutoEquip, review AuctionHouse and ProfessionBuddy cursor transactions separately.

Retain W71-W75 open requirements: prerequisite provenance/core differences, positive ExclusiveGroup repeatable/cooldown semantics, class/skill/reputation/breadcrumb planning inputs, buffs, gear/loadout/caps, addon terrain, underwater, GatherBuddy, native LOS/ABI, Escort/event chains, same-entry ABA/cross-owner cursor coexistence and supervised original-client acceptance.

Do not merge PR51 without explicit approval. Use exact SHA/run IDs and bounded reads for connector stalls.
