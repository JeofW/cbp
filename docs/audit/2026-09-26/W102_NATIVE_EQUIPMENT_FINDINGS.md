# W102 — native equipment evidence, implementation still pending

26 September 2026. The user manually connected the WoW IDA database on port 13337. Read-only MCP health checks identify the copied WoW.exe and WoW-12340.i64, and the binary hash matches the analysis input. The earlier MapleStory connection and failed automatic launch are historical; no rejected launch was retried. No game execution, debugger attachment, production CB access, project execution or source behavior change occurred in this slice.

## Input and limits

- Input: `D:\Dev\CopilotBuddy-Evidence\Build12340-IDA\WoW.exe`, 7,699,456 bytes, version resource `3, 3, 5, 12340`, PE32 x86, image base `0x400000`.
- SHA256: `bf644876709c591acc17c0da8cdf1814edcc9f1e6bc109a8c0d5c38c79dc953c`.
- Database: `D:\Dev\CopilotBuddy-Evidence\Build12340-IDA\WoW-12340.i64`. Newly generated from the user's executable; not a downloaded annotated database. Version/hash identify this input, not pristine official-release provenance.
- Endpoint: `http://127.0.0.1:13337/mcp`. Check health and exact paths before each analysis request; do not assume this endpoint always serves WoW.
- External receipts contain health, input hash, arguments, disassembly and decompiler output. `W102_NATIVE_EVIDENCE.json` inventories their hashes. The executable and database remain outside the repository.
- Hex-Rays inferred prototypes contain inconsistencies, including impossible x86 register annotations and split arguments. Addresses, raw instruction operands and corroborating callers support the conclusions below; inferred prototypes are not approved calling conventions.

## Confirmed defect: popup data is a pending-operation index

Both `runtime-snapshot/Quest Behaviors/EquipItem.cs:310` and `runtime-snapshot/Plugins/AutoEquip2/AutoEquip.cs:896` compare `tonumber(p.data)` with the one-based `_pendingEquipSlot` before clicking a bind popup. These values represent different things in this binary.

Evidence chain:

1. `0x608880` initializes event-name pointers: `0xC252EC` is EQUIP_BIND_CONFIRM, `0xC252F0` is AUTOEQUIP_BIND_CONFIRM and `0xC252FC` is CURSOR_UPDATE. At `0x52AB20`, game UI initialization passes table base `0xC24EB0` to `0x81B5F0`, which registers entries at their unchanged indices. Therefore the event IDs are `(address - 0xC24EB0) / 4`: 271, 272 and 275 respectively.
2. The swap path at `0x6DF890` obtains an index from `0x6DF850`, writes its pending record, and at `0x6DFB2E` pushes that index as the payload. It pushes event ID `0x10F` (271) at `0x6DFB34` and dispatches through `0x81B530` at `0x6DFB3D`. The dispatcher preserves the event index and converts the `%d` argument into a Lua numeric argument.
3. Pinned Build12340 FrameXML commit `9640af74c40affd56fd46d6815917f96fc5c9540`, UIParent.lua:589–604, copies the event's arg1 into dialog.data. StaticPopup.lua:1525–1560 forwards it to EquipPendingItem on accept and CancelPendingEquip on cancel/hide. The Lua parameter happens to be named `slot`; that name does not make it an inventory slot. This mirrored FrameXML source has not been compared with the installed client's UI assets.
4. Native Lua entry `0x51A530` explicitly reports `Usage: EquipPendingItem(index)` and passes the numeric value to `0x6E01A0` with accept=1. `0x51A5C0` does the same for CancelPendingEquip with accept=0.
5. At `0x6E01A7`, the value is bounded by pending-record count `0xC9EB68`; at `0x6E01BB`, it is multiplied by 32 to address the array through pointer `0xC9EB6C`. No inventory-slot conversion occurs. The allocator `0x6DF850` scans from index zero for a cleared record; freed indices can be reused.

Thus a valid pending index 0 does not match a requested equipment slot 16. Conversely, an unrelated pending index 16 could numerically match that slot if such a record exists. These are consequences of the static contract, not claims that either scenario was reproduced in a live client. The existing submission/context guards remain valuable but do not establish ownership of the pending record.

`Tools/WholesomeQuestRecoveryRegressionTests/EquipPopupSubmissionRegressionTests.cs:62` explicitly expects the same slot-based comparison. Its passing result cannot validate the native popup contract. Do not weaken assertions to green an implementation: replace the mistaken contract with evidence-backed cases covering distinct slot/index values, index zero, stale index reuse, foreign same-slot requests and ordinary successful confirmation.

## Physical cursor identity is available, but atomic ownership is unresolved

- `0x513660` returns EDX:EAX loaded from `0xBD0768/0xBD076C` only when cursor kind at `0xBD0748` equals 1; otherwise both words are zero.
- `0x520770` clears prior cursor state and stores a supplied 64-bit item identifier in those words. Its callers and Lua GetCursorInfo at `0x515200` pass that identifier to object lookup `0x4D4DB0` with item type mask 2. This supports identifying it as the physical cursor-item GUID, rather than an item entry ID.
- `0x5136D0` returns the separate 32-bit virtual-item field `0xBD0758`.
- GetCursorInfo's kind-1 branch resolves the physical object, then exposes its entry ID and item link to Lua. Kinds 7, 9 and 11 can also expose the Lua type string `item` using the separate virtual-item field. Lua type/entry equality alone is not physical GUID equality.
- `0x519280` clears the physical GUID words on its kind-1 path and eventually resets cursor kind and emits CURSOR_UPDATE. This does not authorize unconditional cursor clearing.

The pending swap records inspected through `0x6DF850`, `0x6DF890` and `0x6E01A0` are 32 bytes: source container GUID at +0, destination container GUID at +8, source and destination indices at +16/+20, item entry at +24 and a discriminator/argument used by another path at +28. The examined swap path does not store a separate physical item GUID in that record. Other pending paths and every writer have not yet been exhaustively traced.

Reading a GUID in managed code and then executing a separate Lua mutation still permits intervening changes. A reusable pending index is not a unique request token. Neither observation alone closes R04, and no native integration or memory-read implementation is approved by this report.

## Next bounded work

Trace the remaining pending-record writers and the existing client-thread execution boundary. Determine how to bind the physical item, source location, destination and newly created pending index to one guarded operation, with revalidation at acceptance and explicit invalidation on cursor change, record reuse and lifecycle changes. Preserve ordinary successful equip. Avoid a patch that only removes the slot check, substitutes another equality check, or disables every confirmation.

Then implement the smallest evidence-backed repair with hosted Windows/x86 regression tests. R06 first-gossip-response attribution remains separately unresolved and was not analyzed in this slice. R03 deferred mappings, R07 limits, and native/client/server/independent/supervised gates remain as recorded in W101_REQUIREMENT_MAPPING.md. Preserve W100–W93 and earlier repairs, the W80 ledger, W91/W92 evidence and R08 work.

Suggested user-set goal: establish the Build12340 cursor and pending-equip ownership contract, repair the proven slot/index mismatch with normal success preserved, and verify the repair on hosted Windows/x86 while documenting any remaining native acceptance gaps. No goal was created; the Goal tool returned no current goal during this continuation.

## Repository and validation status

Repository JeofW/cbp, ID1367174964; PR51 OPEN/DRAFT; approved branch `audit/next-55-equipment-observation-20260917`. Before this documentation update, local HEAD, direct remote branch and PR head all matched `01991ced2c96c835f88106d2ca20eeed07d5b983`; master remained `b2324913e2499ba30b239dd67224ca2c655c05cc` and the working tree was clean. The containing checkpoint is documentation only; its final publication identity is recorded externally.

Final tested/production source remains `67b4898406a806b2b4a65360ca416a96dff7e1fc`. Retained W101 integrated run36231317916 passed17/17 groups, gossip59/59 and matching-recipient13/13; host run36231317886 compiled Release/x86 with0 errors/3344 warnings. These are historical results, not a new W102 run or proof of the native contract. W101_EVIDENCE.json retains artifact IDs, hashes and1846 source inputs. No new CI was requested for this documentation-only slice.

Keep hosted-only project execution, no subagents, approved-branch expected-parent publication and exact readback. Historical provider refusal is not globally resolved; do not replay refused operations or retry the rejected IDA launch. Preserve exhausted numeric API-preparation constraints. No force-push, master write, merge or deployment; the audit remains incomplete.
