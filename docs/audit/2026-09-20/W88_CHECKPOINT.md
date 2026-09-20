# W88 — same-NPC changed-menu guard verified offline; merge gates remain open

Date: 20 September 2026 (Asia/Kuala_Lumpur).

## Coordinates and scope

Repository `jeofwong/CopilotBuddy-private` (1367174964), draft/unmerged PR51, branch `audit/next-55-equipment-observation-20260917`. Entry documentation was `2d73392d43009dccc1a01c986fd77288e70c7fef`. The entry ZIP was CRC-checked; its seven historical CI archives and two separate W87 unchanged-fixture comparisons were revalidated. This is revalidation of old evidence, not a new W87 C# run.

W80's completed review of 140 commits after 18 September 2026 at 21:58 Malaysia remains complete; do not restart it. W88 addresses the next recorded R06 changed-menu defect, not all remaining remediation.

Last production change: **e984fe15f37ab66bcf0bf45d11b58a8fc51fbba0**. Final tested revision, including the retained restart-fixture adapter: **f6ee23de269228270d0d12950b47a5a2b53535ef**, tree **a3878575232f508c6561f4f7486bc3c7e44a48ec**. This checkpoint and root pointers are documentation-only successors, not another tested production revision.

Direct master was rechecked as **b2324913e2499ba30b239dd67224ca2c655c05cc**. Do not replace it with the cached PR base SHA. Backup `audit/backup-pr51-before-w88-20260920` preserves the entry head.

## Defect and narrow repair

An unchanged NPC GUID did not establish that the observed option text/type/order, greeting, available quests and active quests still described the menu being selected or closed. The old final selection was an unguarded numeric-index call; cleanup could close a replacement menu from the same NPC.

Only production file `runtime-snapshot/Quest Behaviors/GossipEvent.cs` changes. It captures a complete, typed, ordered menu observation and compares that observation again inside the same client request as selection or cleanup. The request also checks the NPC/player GUIDs and shown frame. Existing actor-instance/lifetime/disposal, NPC and authoritative quest-progress/completion checks remain. No arbitrary option fallback is introduced; zero-based recipe indexing still becomes the original client's one-based selection index.

Cleanup cannot adopt whichever menu is currently open: it requires an existing captured observation. Missing, malformed, incomplete or oversized observations provide no mutation authority. A changed menu is left alone and the attempt defers; a submitted click is still not authoritative quest completion.

The host's shared Lua bridge reads strings through `lua_tolstring`, and `GreenMagic.Memory.ReadString` reads 512 bytes per value. The owner therefore uses numeric 1/0 results and a single bounded multi-return observation with 480-character chunks, a verified length header and at most 32,768 encoded characters. Counts, types and hexadecimal UTF-8 bytes avoid ambiguous delimiters or script quoting. Limits are local safety budgets, not invented client limits. No shared memory/ABI code, new native offsets, client-global leases, general inventory framework or other `GossipFrame` callers were changed.

## Primary contract evidence

Pinned original-client FrameXML: `wowgaming/3.3.5-interface-files`, commit `d0339b17b0221db76e6acd2dc2915d224a5b62ca`, `GossipFrame.lua`, blob `401f66b9a20f09c873c4dde335410927568855b1`. It consumes ordered option pairs, available-quest groups of five and active-quest groups of four, plus `GetGossipText`, `CloseGossip` and one-based `SelectGossipOption`.

Host evidence at pre-repair `03c255ff40ea8e8f96074706e277e6c31ffe3dc8`: `Styx/WoWInternals/Lua.cs` blob `4a6873fd1a1200aabbc430e420520e9543a0e2a0`; `GreenMagic/Memory.cs` blob `47448e65cf92422f14f624ff866e8fcc0833c92c`. The unchanged numeric visibility pattern in `Styx/Logic/Inventory/Frames/Frame.cs`, blob `38b8b2d55a52c13b50d75989023035329801a47f`, was also checked. Lua 5.1's official `lua_tolstring` contract accepts strings/numbers, not raw boolean results. Permalinks are retained in W88_EVIDENCE.json.

## Assertion-level red and final evidence

All original 17 gossip-lifetime cases were retained. The first test-only commit `2dc0ed45234c25f383f940227352fb04a4284c3e` had 19/52 passing, 33 intended assertion failures, zero unexpected errors: run35506867198/art10604331374. Transport coverage at test-only `03c255ff40ea8e8f96074706e277e6c31ffe3dc8` had 21/58 passing, 37 intended assertion failures, zero unexpected errors: run35507198189/art10604371775. Both were executed before the production repair.

Production `e984fe15` produced 58/58 gossip cases, but integrated run35507445085/art10604206922 was **not aggregate green**: the older objective-restart fixture could not compile the owner because its controlled Lua class lacked `GetReturnValues`. Its host run35507445039/art10603982373 passed. Keep this failed integrated archive; do not report it as 17/17.

`f6ee23de` changes only the controlled Lua boundary in `QuestObjectiveRestartRegressionTests.cs`, preserving all 37 original cases/assertions and production byte-for-byte. To establish a complete identical-fixture comparison, isolated reference commit **8cc504ca15ebf69d16e735b5ed5ade770915c40b** applies that same adapter to pre-repair production. It is retained on `audit/next-88-gossip-unchanged-fixture-red-20260920`; **never merge this intentionally failing baseline branch**.

| Evidence | Result |
|---|---|
| Matched baseline 8cc504ca, run35507944742/art10604921533 | Gossip21/58, 37 intended assertions, 0 unexpected; restart37/37; other16 integrated entries pass |
| Final f6ee23de, run35507918903/art10604537448 | Integrated17/17; gossip58/58; restart37/37; zero gossip/restart assertions or unexpected errors |
| Final host f6ee23de, run35507918907/art10604851733 | Exit0, 3344 warnings, 0 errors; compile only |
| Auxiliary actual recorded Lua | 57/57 checks in stock Lua5.4 with controlled original-API observations; no original client/game execution |

The matched `8cc504ca -> f6ee23de` comparison has all **153 normalized members byte-identical** and all **1,835 indexed input paths** equal except `GossipEvent.cs`. The earlier `03c255ff -> e984fe15` production pair also kept all153 members unchanged, but encountered the separately disclosed restart-fixture failure. The fixture-only `e984fe15 -> f6ee23de` span changes the restart fixture and its two derived normalization-manifest hashes; it is not another production repair. Do not call the whole entry-to-final span an unchanged-fixture pair.

All seven W88 archives were checked against their published outer SHA256 and CRC; all197 inner manifest entries in each of five integrated archives, exact source identities, the two host results and the named comparisons were checked. The evidence ZIP contains original archives, verification scripts, reports and reviewed diffs.

Retained final groups include MIR finite scan27/27, MIR context82/82, EquipItem context67/67, AutoEquip context46/46, collection replay34/34, Ret registration9/9 and Python analyzers89 tests/OK. Their existing acceptance limits remain.

## Evidence limits and disclosed corrections

C# ran on Windows x86 with controlled world/UI/native boundaries. The Lua probe executes the C#-recorded scripts in stock Lua5.4; its test-only `unpack` alias supplies the original5.1 name. Lua5.1/original build12340 was not executed. An attempted official5.1 source download failed; it is not verified runtime evidence. No supervised NPC interaction, native deletion, server quest acknowledgement, full native/UI lifecycle, deployment or independent reviewer result is claimed.

Exact same-content menu close/reopen (generation/ABA identity), native memory/bridge concurrency, and final original-client acceptance remain unproved. W88 establishes the named changed-menu source guard and offline regressions, not universal R06/native acceptance or merge readiness.

Local auxiliary-verifier CRLF parsing and empty-list setup errors were corrected; they were harness errors, not production regression evidence. The retained empty-list error report records 53/57 with four setup errors before unchanged scripts passed57/57. A blob-transfer checksum caught an unintended equivalent fixture copy-path expression before any tree/ref referenced it; discarded unreferenced blob848fb01c was replaced by reviewed blob51b52a96. No branch committed that transfer error. The older W77 documentation add/revert and W87 duplicate-harness add/removal history remain disclosed, not rewritten.

## Remaining gates and next action

**Next R03:** trace actual CAST recipe admission from dataset/strategy binding through scheduler collection, generated XML and behavior dispatch. Establish dataset objective index, collection index and packed raw-counter slot independently; reproduce supported sparse/mixed-index cases and unsupported CAST cases in the actual loader/scheduler/dispatch path before a narrow repair. Unsupported recipes remain rejected, never ordinary killing. No guessed Gordunni/escort coordinates or protocol.

R04 still requires physical displaced/foreign cursor, same-slot popup ownership and remaining lifecycle coverage. R07 still requires actual compiler dependency, static-state/reload and full refresh-publication evidence. Retain R06 original-client/native acceptance above, all prior prerequisites, provenance/core/addon, quest, buff, gear/loadout, navigation/native and supervised acceptance requirements. W84/W86/W87 only establish named local R05 observation/context/scan cases, not universal deletion acceptance.

Retain W77-W87 repairs, particularly actor/run/operation inventory gates, unknown-observation semantics, successful-local-request deletion confirmation, EquipItem admission, Ret registration, objective restart, dense-pull containment, auction withdrawal and W87's two separate finite-scan/busy-state pairs. Do not recreate the duplicate MIR harness removed at b065b5e0. W87_CHECKPOINT/W87_EVIDENCE, W86 and W80 remain governing retained evidence.

Original WoW3.3.5a/build12340; TrinityCore3.3.5 primary, AzerothCore WotLK secondary. Wholesome questing/navigation/Singular and related safety only. No desktop mouse simulation, guessed Lua/native offsets, AuctionHouse/ProfessionBuddy expansion, force pushes or installed-file replacement.

Merge remains conditional, already authorized only after all blockers/final gates are genuinely satisfied. Do not ask again or merge this partial state. Before an eligible merge reconcile direct refs, preserve backup, inspect the exact merge preview and exact-source integrated/host evidence, then use an exact-head conditional merge. Source merge is not deployment. W88 did not write master, merge or deploy.

Every write requires the explicit nonempty approved branch, expected parent/blob, reviewed content/message and force=false. Always update durable pointers and end with both a copy-ready new-chat prompt and downloadable UTF-8 handoff.
