# Resume at W72 — exact TC negative-parent status verified

Repo `jeofwong/CopilotBuddy-private`, draft PR51, branch `audit/next-55-equipment-observation-20260917`. Reconcile live refs first.

Verified code/test head **7a471da4d674ca1dffdc6169340b091e96a36e46**, tree **3bafec33ca952646ff1093f2a4a84d9a82c9e1bf**.

Read `docs/audit/2026-09-19/W72_CHECKPOINT.md` and `W72_EVIDENCE.json`, then W71 and the original-client/core/provenance policies.

Exact green:
- integrated **35431678604 / art10580349205** — 17/17 entries
- quest-log owners **35431678616 / art10579879846**
- host **35431678735 / art10580898437** — 0 errors / 3340 warnings

New retained scope:
- ActiveParent **39/39**
- QuestLogObservation **27/27**
- RawReady **16/16**
- dependent alternatives **14/14**
- negative exclusive dependencies **10/10**
- container slot identity **9/9**

TC-primary negative direct `PrevQuestID` now requires a current accepted parent that is neither ready/completed nor failed. Raw `FailedQuestIds` comes from the same immutable quest-log observation and is revalidated with it. Pinned AC WotLK remains broader; do not erase that divergence or infer core identity.

NEXT: native safe single-item cursor pickup. Current `WoWItem.PickUp()` still derives BagIndex/BagSlot independently. Test first a `TryPickUp` contract that reuses GUID/slot validation, requires `GetCursorInfo()==nil`, validates expected entry immediately before `PickupContainerItem`, and requires `CursorHasItem()` afterward. Never implicitly ClearCursor; caller owns it. Keep same-entry ABA explicit.

After that audit callers separately. DeleteItems/MrItemRemover need confirmation + deletion acknowledgement; EquipItem/AutoEquip need equip/bind/final slot acknowledgement; AuctionHouse needs sell/post acknowledgement; ProfessionBuddy bank/mail/stack/AH cursor transfers are intentional multi-step transactions and must not use a blanket helper.

Do not merge PR51 without explicit approval. Use exact SHA/run IDs with bounded reads for connector stalls.
