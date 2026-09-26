# W103 — guarded synchronous equipment confirmation

26 September 2026. Continued W102 without subagents. Verified the user-connected WoW MCP, completed the bounded pending-record writer trace, and repaired the proven inventory-slot/pending-index mismatch in EquipItem and AutoEquip. Hosted regression and build artifacts are inspected and retained. This closes this implementation slice, not the full R04 requirement or PR51 audit.

## Identity and publication

Repository `JeofW/cbp`, ID1367174964; PR51 OPEN/DRAFT; only branch `audit/next-55-equipment-observation-20260917`. Workspace `D:\Dev\CopilotBuddy-PR51`; external evidence `D:\Dev\CopilotBuddy-Evidence`.

Tested implementation: `f434f7dd6ed8aad0ae7dd48d7391f1ceda4e3b26`, tree `81b87f073d566731e0f878fa8c28275e32d481f4`. Parent/test-only behavioral red: `2dcf19f6886cc3b08e34d35574ecd3f71341fb5e`. Source publication receipt `W101_GREENPENDING_PUBLICATION_20260926.json` verifies the exact 13 changed paths, bytes, blobs, parent, tree and direct branch. The retained helper requires the historical W101 label prefix; these receipts belong to W103 work.

This checkpoint's containing commit is documentation only. Its final identity is recorded in external `W101_PENDINGDOCS_PUBLICATION_20260926.json` and `LATEST_CONTINUATION.md`; do not substitute it for the tested source. Master remains `b2324913e2499ba30b239dd67224ca2c655c05cc`.

## Native evidence and ownership boundary

Read-only health identified module WoW.exe, database `D:\Dev\CopilotBuddy-Evidence\Build12340-IDA\WoW-12340.i64`, and input `D:\Dev\CopilotBuddy-Evidence\Build12340-IDA\WoW.exe`. Input SHA256 is `bf644876709c591acc17c0da8cdf1814edcc9f1e6bc109a8c0d5c38c79dc953c`; version resource 3,3,5,12340; 7,699,456 bytes; PE32 x86; image base 0x400000. Health/hash/path are checked before every analysis read. This establishes input identity, not pristine official-release provenance. The executable/database remain external. No launch retry, game execution or debugger attachment occurred.

W102 established the physical cursor getter and reusable pending index. W103 adds 14 retained MCP receipts, inventoried with hashes in W103_EVIDENCE.json:

- Physical getter `0x513660` returns GUID words at `0xBD0768/0xBD076C` only for cursor kind 1 at `0xBD0748`. Raw bytes corroborate the addresses. Virtual kinds 7/9/11 can expose Lua type `item` without this physical identity.
- The pending table uses count `0xC9EB68`, pointer `0xC9EB6C`, and 32-byte records: source container GUID +0, destination container GUID +8, source/destination slots +16/+20, entry +24, item-object argument +28 on another path. Allocator `0x6DF850` reuses cleared records. There is no independent physical item GUID in the traced swap record.
- Remaining writer/callback paths `0x6DB6F0`, `0x6DB7C0`, `0x6DFC40`, `0x6DFF90`, `0x6E1A70` and teardown `0x6E4780` were read. Cache callbacks retry operations and clear records. The `0x6E1A70` auto-bind route is reachable from message dispatch `0x6E2E90`; it can produce a later event outside an owned synchronous call. No named network opcode or causal ownership is inferred for it.
- EquipCursorItem `0x51A3D0` captures cursor/source and invokes the swap path. `0x6DF890` emits EQUIP_BIND_CONFIRM with the allocated index. Dispatch `0x81B530` -> `0x81AC90` -> `0x81AA00` invokes registered handlers inline through `0x819EA0`. That supports a bounded synchronous event receipt, not persistent authority over an index.
- GetItemInfo `0x516C60` calls DBItemCache `0x67CA30` and returns no values for a missing record. The generated script requires cached item info before submission to avoid the traced cache-miss deferred route.
- Pinned Build12340 FrameXML commit `9640af74c40affd56fd46d6815917f96fc5c9540` copies event arg1 into popup.data, forwards it to EquipPendingItem on acceptance, and exposes popup.button1 via StaticPopup.xml's parentKey. XML SHA256 `a575f35cc8e9e3a585c6ccd5db40f650dbd4f153e62be6736f1101c3f33dde7b`. The mirror has not been compared with installed UI assets. Hex-Rays inferred prototypes are not accepted as ABI definitions.

## Repair

Both owners now call shared `Lua.TryEquipCursorItem(pendingGuid, entry, slot)`. Within the existing client-thread executor request, after loading the Lua chunk and before calling it, generated instructions compare cursor kind and both physical GUID words. A mismatch takes the existing no-return cleanup path. This avoids relying on a separate managed memory observation before mutation; no new native function call or hook is introduced.

The script retains entry/slot/lock checks, rejects preexisting equipment bind popups and missing item-cache data, and observes bind/cursor events only during EquipCursorItem. It accepts a popup only for one synchronous nonnegative integer pending index with matching popup kind/data and no observed cursor change. Index zero and indices different from the destination slot are valid. Multiple events, replaced popup data, absent button, cursor changes and later events cannot acquire confirmation authority. The observer unregisters immediately; no pending index survives into a later bot tick.

Removed both later-tick ConfirmOwnedEquipPopup helpers and calls. Kept admission/lifecycle rechecks, acknowledgements, deadlines and existing cleanup behavior. True means a local submission receipt, not successful equipment or server acknowledgement. Slot zero remains allowed for the existing ammo/autoequip mapping.

The shared Lua return-buffer allocation, clearing and result copying now occur under the existing executor AssemblyLock, preventing another managed caller sharing that executor from overwriting this result before copying it. No general claim about executor replacement, native concurrency, malicious addon API replacement or arbitrary native memory writes is made.

## Actual validation

The first test-only commit `bc9e4ffbca402dd353d62f48d9ea4fceb9efd419` produced a harness compile failure: the extracted bridge lacked a Logging stub. Integrated run36234901179 is retained but is **not behavioral red evidence**. A one-line harness correction produced `2dcf19f6886cc3b08e34d35574ecd3f71341fb5e` while production still matched the previous implementation.

Corrected red integrated run36235220068: pending-index cases **0/22, 22 intended assertions, 0 unexpected errors**. Valid index0/index7/autobind0 were missed, and later foreign/reused index16 could be clicked. Other 16 aggregate groups passed. The host build also passed (run36235220072). All intermediate receipts and archives are retained.

Final integrated run [36235663375](https://github.com/JeofW/cbp/actions/runs/36235663375), job108386838885, artifact10904186966: **17/17 aggregate groups**, no game attached. Archive909879 bytes; SHA256 `9254b29274b063c60cc1f645ebf8a1425d061ca2af8f0cb71369d07153f373a4`, matching metadata. All211 members/210 inner hashes verified. All1848 source input hashes match the local tested checkout.

Verified final cases:

| Coverage | Result | Boundary |
| --- | --- | --- |
| Pending-index ownership, both callers | 24/24 | Exact owners/generated Lua, stock Lua5.1, controlled synchronous events |
| Physical cursor guard | 8/8 | Actual generated comparisons/branches evaluated against controlled words; no assembled native execution |
| Equipment Lua5.1 boundary | 83/83 | Existing generated scripts and managed conversion |
| Equip continuation / AutoEquip context / EquipItem context | 46/46, 34/34, 54/54 | Controlled lifecycle and observation boundaries |
| Later-tick popup submission | 18/18 | Actual tick control flow; recording request boundary |
| Retained gossip lifetime / matching recipient | 59/59, 13/13 | Existing controlled owners/refusal path |
| Retained W100 Lua / generated dispatch / restart | 63/63, 18/18, 37/37 | Existing offline boundaries |

The final pending fixture adds two cache-miss cases to the corrected red's22, and final adds the separate8-case physical guard fixture. Eight existing fixture/helper inputs changed to follow the shared synchronous API; one fixture was added. The artifact has166 normalized members:10 changed (eight fixtures/helpers plus generated registration/manifest), one added,155 unchanged. Do not claim identical red/green fixtures. Exactly four production source inputs changed: GlobalOffsets.cs, Lua.cs, EquipItem.cs and AutoEquip.cs. See W103_EVIDENCE.json for exact paths and comparisons.

Final Windows host run [36235663412](https://github.com/JeofW/cbp/actions/runs/36235663412), job108386839093, artifact10903853440: Release/x86 build **0 errors,3344 warnings**; tests_run=false; game_attached=false. Archive82043 bytes; SHA256 `9e2efa0eab34bbcff4ec6675055d62c732795892cfdf7b6ca40d3b1da0e09433`, matching metadata. Actual result.json and build log inspected.

No project code was executed locally. Local checks were source review, whitespace, immutable evidence/hash inspection and publication readback. Direct self-review against native receipts and pinned FrameXML is not independent acceptance.

## Remaining work and restrictions

Full R04 remains open: displaced-item return and timeout restoration still depend on older entry/type/location checks; by-name equip remains separate. This repair does not establish ownership of delayed/cache/network-originated bind events and deliberately does not auto-accept them. Successful native execution, installed-client behavior and server acknowledgement remain untested. R06 first-menu causal attribution remains unresolved; retain R03 deferrals, R07 limits, R08 work and all wider audit/independent/supervised gates.

Next bounded task: trace the physical identity/lifetime of the displaced item after an acknowledged equip and the cancellation/timeout paths, then compare those findings with ReturnDisplacedCursorItem/RestorePendingEquipCursor in both owners. Establish an evidence-backed exact-item mutation boundary before changing these remaining cleanup operations. Preserve this synchronous repair and normal successful behavior. Do not claim full R04 from the new instruction-contract tests.

Preserve W101 and W100–W93 fixes, W80 ledger, W91/W92 evidence, earlier receipts and failed intermediates. No subagents, local project execution, production CB access, rejected IDA launch retry, blanket confirmation suppression, broad audit restart, PR58 recreation or excluded PR25 work. Hosted Windows/x86 only. The historical provider refusal is not globally resolved; do not replay refused operations or use an alternate route to evade refusal. Numeric preparation budget remains3 historical/0 new/0 remaining. Approved-branch ordinary fast-forward only after expected-parent checks and exact remote readback; checked non-LFS paths may use the retained process-scoped LFS skip with hooks/environment preserved. No force-push, master write, merge or deployment. Original WoW3.3.5a/build12340; TrinityCore3.3.5 primary/AzerothCore WotLK secondary. No active Goal was created in this continuation.
