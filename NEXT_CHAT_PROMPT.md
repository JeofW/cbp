@GitHub

Continue `jeofwong/CopilotBuddy-private`, draft PR51 branch `audit/next-55-equipment-observation-20260917`, from W75. Reconcile live refs, then read `AUDIT_RESUME.md`, `docs/audit/2026-09-19/W75_CHECKPOINT.md`, `W75_EVIDENCE.json` and the governing original3.3.5/core/provenance files.

Verified tested head **4e7c7699d577bbc6922bdbc12adea30e17c12450**, tree **ffd735430f64cdfdf91af3ce3d1d41d2f53f94db**. Integrated **35436934243/art10582366699** passes17/17; host **35436934248/art10582840978** passes; container identity20/20 and MrItemRemover delete14/14, assertions0 unexpected0.

Retain W73/W74/W75 cursor contracts: GUID slot resolution/revalidation, expected cursor item entry after TryPickUp, no ClearCursor, exact popup identity, DELETE_ITEM_CONFIRM_STRING for good-item confirmation, cursor release + exact GUID absence acknowledgement, bounded ownership, fail-closed quest-item slot observation, and no generic popup click.

First next slice: `runtime-snapshot/Quest Behaviors/EquipItem.cs` and `runtime-snapshot/Plugins/AutoEquip2/AutoEquip.cs`, test first. Require stable item GUID/entry/slot ownership, no independent BagIndex/BagSlot reads for explicit equip, no ClearCursor, exact cursor entry before EquipCursorItem, exact/owned popup handling where required, bounded pending lifetime, and acknowledgement from the intended equipment slot before completion. EquipItem must not mark done immediately after fire-and-forget. AutoEquip must not start multiple simultaneous equip transactions.

Preserve existing ammo path, scoring, weapon-style decisions, loot-roll policy and bag selection. Do not refactor gear valuation in this cursor slice. Do not include AuctionHouse or ProfessionBuddy yet.

Then review AuctionHouse and ProfessionBuddy cursor transactions separately. Preserve all W71-W75 open requirements and do not merge PR51 without explicit user approval.
