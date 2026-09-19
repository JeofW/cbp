# Resume at W74 — owned DeleteItems cursor/confirmation lifecycle verified

Repo `jeofwong/CopilotBuddy-private`, draft PR51, branch `audit/next-55-equipment-observation-20260917`. Reconcile live refs first.

Verified code/test head **67411dbbd765a6d781809b7c18d4ca8e1a20d24a**, tree **2878b68b99bacf93079f48c1a99efacff803aa61**. W74 documentation is newer than the tested code head.

Read `docs/audit/2026-09-19/W74_CHECKPOINT.md` and `W74_EVIDENCE.json`, then W73/W72 and the original-client/core/provenance policies.

Exact green at tested head:
- integrated **35435502277 / art10582149126** — 17/17
- host **35435502301 / art10581398494** — success
- DeleteItems lifecycle **12/12**, assertions0, unexpected0

Retain the post-W73 pickup strengthening at **fb746ecd**: successful `TryPickUp()` must identify the expected cursor item entry, not merely a non-empty cursor.

Retain W74 DeleteItems production **153128f7**:
- mutation moved out of OnStart into a tick-owned lifecycle,
- exact `TryPickUp()` success gate,
- no `ClearCursor()`,
- exact pending GUID + entry ownership,
- exact `DELETE_ITEM` / `DELETE_GOOD_ITEM` popup identity,
- `DELETE_ITEM_CONFIRM_STRING` for high-quality delete,
- cursor release plus GUID disappearance acknowledgement,
- bounded refusal/confirmation lifetime,
- fail-closed bot stop on unresolved destructive ambiguity.

Clean behavioral red was **fd7d6f20**, integrated **35435022827/art10581607785**: DeleteItems2/12,10 intended assertions,0unexpected; real behavior compiled. Production then reached9/12; final test-only helper-declaration anchor **67411dbb** made the same assertions inspect the intended helper bodies and all12 pass.

NEXT: `runtime-snapshot/Plugins/MrItemRemover2` destructive deletion only, test first. Current risks: generic DELETE_ITEM_CONFIRM handler clicks StaticPopup1Button1 based on CurrentTarget, multiple ClearCursor/PickUp/DeleteCursorItem paths, other unguarded deletes, and IsQuestItem independently derives BagIndex+1/BagSlot+1 before destructive eligibility. One pending owner, exact popup/item identity, bounded lifecycle, actual absence acknowledgement, and stable quest-item slot identity are required.

Do not fold selling/opening/combining into the first delete repair unless compatibility requires it. After MrItemRemover, continue EquipItem/AutoEquip, AuctionHouse and ProfessionBuddy cursor transactions separately.

This remains offline/source verification; supervised original-client deletion, same-entry ABA and cross-plugin coexistence remain open. Do not merge PR51 without explicit approval. Use exact SHA/run IDs and bounded reads for connector stalls.
