# W104 — equipment cursor lifetime and cleanup verified

27 September 2026. The cleanup repair is implemented, published and validated through the required hosted Windows/x86 gates. This supersedes the earlier W104 validation hold for this implementation slice. The hold, exact refusals, original tests and all previous evidence remain in history. The overall PR51 audit and original-client/server/independent acceptance are still open; PR51 remains draft and unmerged.

## Exact source identities

Repository `JeofW/cbp`, ID `1367174964`, PR51; approved branch `audit/next-55-equipment-observation-20260917`. Workspace `D:\Dev\CopilotBuddy-PR51`; evidence `D:\Dev\CopilotBuddy-Evidence`.

| Identity | Revision |
|---|---|
| Prior validated W103 production | `f434f7dd6ed8aad0ae7dd48d7391f1ceda4e3b26` |
| Historical W104 hold documentation | `c25b03f61b94e4507aa6f563ba25f290204603b5` |
| Original W104 test source | `680d88be7584f321991c99afe5232e5fefe63ac8` |
| Extended test baseline / production parent | `f0e5a502f8fa0afe675f0b3e188e5c46b27397e4` |
| Validated production | `6cbabba99ee523cbf2e0902f5b6964027edc410e` |
| Validated production tree | `8b585dc4272c18f7c9c82efa95c07119d24b2937` |
| Master at successful source-publication readback | `b2324913e2499ba30b239dd67224ca2c655c05cc` |

This containing checkpoint is documentation only. Its final commit is recorded externally by the publication receipt and `LATEST_CONTINUATION.md`; do not substitute it for the tested production source above.

## Proven defects and repair

The original build-12340 trace establishes that explicit equip sends a complete slot swap and clears the selected cursor. The old-item follow-up locks that item; it does not put it on the cursor. Pinned TrinityCore 3.3.5 swap code places old equipment into the original source slot. Initial pickup merely selects/locks the original item, which still appears in its bag slot. See retained `W104_NATIVE_CLEANUP_FINDINGS.md` and the 18 native/six pinned-core records in historical `W104_EVIDENCE.json`.

The old post-acknowledgement helper could put an unrelated different-entry cursor item into an empty source slot. The old timeout helper both missed an ordinary selected item in its still-occupied slot and could move a foreign same-entry cursor when the slot was empty. A selected item's GUID alone also cannot authorize a later re-pick of that same item.

Both equipment owners now arm a per-attempt cursor observer before their existing validated pickup, then recheck actor/runtime/quest context before pickup. The observer accepts the native empty-then-selected transition and revokes the attempt on a subsequent or unexpected cursor event. Submission and cancellation require the same token, active event registration and original handler plus W103's full physical GUID guard in the same executor dispatch. A missing observer, detached registration, replaced handler, different physical copy, virtual cursor or re-picked identical GUID cannot acquire authority.

Acknowledged cleanup now only observes whether the cursor has been released. It never moves a later cursor item. Timeout cancellation revokes the lifetime before releasing the original selection through `ClearCursor`; it never sends a compensating bag swap or cancels a reusable pending-equip index. Strict numeric receipts retain missing/malformed observations as unknown. Spell cursors now remain busy instead of being treated as empty.

Exactly three production files changed: `Styx/WoWInternals/Lua.cs`, `runtime-snapshot/Quest Behaviors/EquipItem.cs`, and `runtime-snapshot/Plugins/AutoEquip2/AutoEquip.cs`. W103's emitted comparison instructions, executor placement and shared return-buffer lock are unchanged. Synchronous pending-index confirmation still accepts only its bounded owned dispatch and retains cache/pop-up/changed-cursor refusal. No later-tick confirmation authority was added. Existing acknowledgement, context, disposal and deadline guards remain.

One Lua frame is reused for cursor observation; it holds no managed owner. Once the selected state has been observed, the next cursor event invalidates its token. Managed context revocation remains mutation-free and discards the managed token; a new admissible attempt re-arms the same frame with a new token. This is not an adversarial-addon guarantee or a general native concurrency framework.

## Actual hosted red and green

The original published tests were recovered rather than recreated. Integrated run **36298023144**, artifact **10924054292**, executed all **49 cleanup cases: 20/49, 29 intended assertions, zero unexpected errors**. The other expected failing groups had 3 AutoEquip context, 3 EquipItem context and 8 equipment-Lua assertions. All projects compiled; aggregate was 16/17. Host **36298023158**, artifact **10924167266**, had zero errors/3344 warnings. These previously unknown results are now established by retained current read/inspection receipts.

The additive baseline `f0e5a502` retained every original case and added eight handler/registration-detachment cases. Two isolated Lua fixtures gained controlled observer methods, and the 18-case popup fixture gained only missing compilation stubs. No original expectation was weakened. Integrated **36301432933**, artifact **10925622429**, produced **20/57 cleanup, 37 intended assertions, zero unexpected**; the other 14 expected assertions remained. Host **36301432936**, artifact **10926195554**, passed compilation.

Final integrated run **36301825983**, job **108570741928**, artifact **10926102133**, is **17/17**. Final host **36301825975**, job **108570742106**, artifact **10926016261**, compiled Release/x86 with **zero errors and 3344 warnings**; it is compile-only, `tests_run=false`.

| Required boundary | Verified result |
|---|---|
| Cleanup / selection lifetime | 57/57, zero assertions/unexpected errors |
| Synchronous pending-index ownership | 24/24 |
| Emitted physical GUID guard | 8/8 |
| Generated equipment Lua / strict transport | 87/87 |
| AutoEquip context | 37/37 |
| EquipItem context | 57/57 |
| Continuation / acknowledged timeout / later-popup refusal | 46/46; 10/10; 18/18 |

All three integrated archives have **212 members, 211 verified inner hashes and 1,849 captured source inputs**. Every final input hash matches the local tested checkout. The final failing/passing pair has **all 167 normalized fixture members byte-identical**, no added/removed inputs, and only the three intended production changes. The original 680d88be-to-final span is not an identical-fixture pair because the eight extra cases and adapter updates in four test files were published first.

Final integrated archive SHA256: `3cdaa665ce6e2502480164c03a423ec79a9140493a698b9ed2110112871c5932`.
Final host archive SHA256: `a0ba5f9e8a43a27ad0d4b1ef8d2206e31431b0524662327421c9ead67601228a`.
Both match GitHub metadata. Full red/intermediate/green receipts, source hashes, summaries and comparison are in `W104_VERIFIED_EVIDENCE.json`. No separate CRC check or reproducible native-DLL build is claimed.

## Native evidence and transport history

Fresh IDA health again identified the same copied `WoW.exe` / `WoW-12340.i64`, base `0x400000`, input SHA256 `bf644876709c591acc17c0da8cdf1814edcc9f1e6bc109a8c0d5c38c79dc953c`. No executable/IDB mutation, launch or debugger attachment occurred. New string/xref receipts corroborate observer API names. Handler/registration validation reuses the W100 pattern and pinned Build12340 UI usage at `tekkub/wow-ui-source` commit `9640af74c40affd56fd46d6815917f96fc5c9540`; `W100_API_PROVENANCE_20260926.json` retains exact file hashes.

The earlier combined CI/status/audit refusal remains unchanged in the historical hold. This continuation also received an archive-inspection refusal and an IDA table-byte refusal; their exact payloads/output are retained in external `W104_RESUMPTION_20260927.md`, whose hash is included in the verified evidence manifest. Neither refused transaction was replayed or relabelled successful. The user's new focused current-state reads established current hosted evidence. No credential, account, permission or route change was used; no global read-only/disconnection diagnosis is made. The unavailable IDA bytes are not used as ABI evidence.

All project compilation/test execution occurred on GitHub-hosted Windows/x86. Local operations were source editing/review, evidence inspection and checked publication. Direct self-review is not independent acceptance.

## Next work and retained gates

Continue the remaining PR51 audit now that the equipment successor is validated. Prioritize the current wholesome questing/navigation/combat and single-target attack paths, following existing requirement/evidence mappings rather than reopening completed W80/W92 work. Implement only new source/client-evidenced defects, with hosted failing/passing regression evidence and exact publication receipts.

Original-client execution, server acknowledgement, independent review and supervised acceptance remain required. This cursor repair does not establish ownership of delayed/cache/network-originated bind records; by-name equip is separate. R06 first-menu request attribution remains unresolved. Keep R03's explicit unsupported raw-slot/recipient/recipe deferrals, R07's documented runtime/concurrency limits, R01 dense-pull containment, and repaired R02/R05/R08 boundaries. Do not convert an unreviewed path into an arbitrary deferral or claim all-class/quest/navigation acceptance from this passing slice.

Preserve every prior repair, assertion, source policy, failed intermediate, backup and mandatory gate. No subagents, local project execution, production CB access, speculative mechanics, W80/W92 restart, PR58 recreation, excluded PR25, force-push, master write, merge or deployment. Numeric preparation remains **3 historical / 0 new / 0 remaining**. `session_finish` has not been called because the overall requested audit is still in progress.
