# W104 native equipment cleanup findings — implementation pending

27 September 2026. This is read-only original-client and pinned-core evidence, not a completed repair, a passing new regression run, or live client/server acceptance. W103 production remains unchanged.

## Verified input and evidence custody

Every W104 IDA analysis request first checked the live server's module, image base, input path and IDB path and rehashed the input executable. The endpoint was `http://127.0.0.1:13337/mcp`; module `WoW.exe`, base `0x400000`; input `D:\Dev\CopilotBuddy-Evidence\Build12340-IDA\WoW.exe`; IDB `D:\Dev\CopilotBuddy-Evidence\Build12340-IDA\WoW-12340.i64`. Input SHA256: `bf644876709c591acc17c0da8cdf1814edcc9f1e6bc109a8c0d5c38c79dc953c`.

The evidence helper `D:\Dev\CopilotBuddy-Evidence\w104-ida-read.ps1` retains complete response, input identity, hash, arguments and capture time under a unique filename. No executable/IDB modification, launch, debugger attachment, local project build/test or subagent was used. The copied input identity does not independently establish pristine official provenance. Hex-Rays inferred prototypes are not new ABI evidence; the critical equip and cursor-set call sequences were also read as x86 instructions.

## Normal explicit-slot equip has no displaced cursor to return

`EquipCursorItem` at `0x51A3D0` captures the selected physical GUID and source coordinates, resolves the intended equipped slot, validates equip, calls `0x6DF890`, then calls `0x513740` for the prior destination item. The x86 sequence `0x51A4CE` / `0x51A4DB` corroborates those calls.

The swap helper's successful paths emit opcode `0x10D` for same-container swapping or `0x10C` for container swapping, send the packet, then call cursor clear `0x519280`. The follow-up `0x513740` looks up an item and calls `0x706FE0`: that function sets bit 0 of the item's field at offset 916 and notifies observers. It is an item lock operation, not the physical cursor setter `0x520770`.

Pinned TrinityCore **3.3.5** commit `8fda442f6c30ca21a622638063ab8b28376f1b25` corroborates the protocol:

- `src/server/game/Server/Protocol/Opcodes.h:297-298`: `CMSG_SWAP_ITEM=0x10C`, `CMSG_SWAP_INV_ITEM=0x10D`.
- `src/server/game/Handlers/ItemHandler.cpp:66-160`: both handlers validate source/destination and invoke `Player::SwapItem`.
- `src/server/game/Entities/Player/Player.cpp:12917-12935`: a real swap removes both items, puts the source item into the destination and puts the previous destination into the original source. The empty-destination path at 12708-12755 is also handled by the server.

Pinned AzerothCore commit `8337a378ac325e62a6a91e00c6a5e944205e8536` agrees on opcode values and handler dispatch (`Opcodes.h:298-299`, `ItemHandler.cpp:62-150`). This continuation did not inspect AzerothCore's complete storage implementation or run either core. Do not upgrade this corroboration to server acceptance.

Exact source bytes were fetched using GitHub contents API at those commits, not moving master or a Wrath Classic branch. Retained source files are `w104-tc335-item-handler.cpp`, `w104-tc335-player.cpp`, `w104-tc335-opcodes.cpp`, `w104-ac-item-handler.cpp`, `w104-ac-player.cpp`, and `w104-ac-opcodes.cpp` in the external evidence directory. `W104_EVIDENCE.json` records their hashes and pinned repository identities alongside the native receipt inventory.

**Consequence:** a later cursor with a different item entry is not evidence that the bot owns a displaced item. Both current `ReturnDisplacedCursorToSource` implementations can put that foreign cursor into a now-empty source slot. The correct post-acknowledgement boundary is observation of cursor release, not another inventory move. The intended equipment GUID acknowledgement and bounded lifetime must remain.

## Pickup does not vacate the source slot

The original `PickupContainerItem` at `0x5D7FF0`, with an empty cursor and an unlocked source item, calls `0x520770` to select it and `0x513740` to lock it. That branch does not send a slot-swap packet or remove the source inventory entry.

`0x520770` first calls clear `0x519280` (which resets kind and emits `CURSOR_UPDATE`), then writes the physical GUID/source coordinates and kind 1, and emits another `CURSOR_UPDATE`. The x86 callsites are `0x5207BF` and `0x520870`. This establishes the empty-then-selected transition for this ordinary pickup path; unexpected extra/missing events must revoke admission, not be ignored.

**Consequence:** both current timeout helpers' `not GetContainerItemLink(source)` condition prevents normal cleanup while the original item is merely selected and still appears in its bag slot. When that condition does happen to be true, matching the entry still admits a different physical copy or a later cursor selection.

## Cancellation releases a selection; it is not a compensating bag move

The `ClearCursor` registration at `0xAC8270` points to `0x51A3B0`, which calls `0x519280` with unlock enabled. In the kind-1 path, clear calls `0x513770`, restores presentation, zeros the physical GUID/source coordinates and kind, and emits `CURSOR_UPDATE`. `0x513770` calls `0x707020`, which clears the item lock bit at offset 916. It does not relocate the item.

`0x6E01A0` retains W103's pending-index semantics. Acceptance follows stored location records; cancellation resolves the currently stored locations and releases locks, then zeros/recycles the record. It provides no durable item GUID or generation suitable for later timeout ownership. Do not capture a popup index now and cancel it on a later tick.

**Repair design, not yet implemented:** arm a per-attempt observer before the existing validated pickup, observe the native empty/item transition, and permanently invalidate it on another cursor update. Recheck managed context after observer setup and before pickup. Equip and timeout cancellation must require that same attempt plus W103's full physical-GUID guard in the same executor dispatch. Cancellation invalidates the observer before `ClearCursor`. A different entry, a same-entry physical copy, or re-picking the identical GUID must never revive the old attempt. Missing observations remain refusal, not success.

The existing temporary bind observer and synchronous pending-index confirmation remain separate and unchanged in authority. Normal unbound and synchronous bind-confirmed equip must still complete. No delayed bind acceptance, generic popup clicking, new native ABI or direct game-memory write is proposed.

## Read-only IDA receipt inventory

All are under `D:\Dev\CopilotBuddy-Evidence`:

`w104-equip-cursor.json`, `w104-item-swap.json`, `w104-cursor-set.json`, `w104-container-api-strings.json`, `w104-displaced-followup.json`, `w104-cursor-clear.json`, `w104-pending-cancel.json`, `w104-container-pickup-xrefs.json`, `w104-container-pickup.json`, `w104-displaced-item-lock.json`, `w104-cursor-release-item.json`, `w104-clear-cursor-registration.json`, `w104-clear-api-xrefs.json`, `w104-item-unlock.json`, `w104-clear-api-table.json`, `w104-equip-cursor-instructions.json`, `w104-clear-cursor-api.json`, `w104-cursor-set-instructions.json`.

See `W104_VALIDATION_HOLD.md` for the exact published test frontier and the subsequent provider block. The remainder of R04, R06 and the wider PR51 audit is not declared complete.
