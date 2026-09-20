# Resume W88 — changed gossip-menu guard verified offline; merge gates remain open

Repo `jeofwong/CopilotBuddy-private` (1367174964), draft/unmerged PR51, branch `audit/next-55-equipment-observation-20260917`. Reconcile live refs before writing. Read `docs/audit/HANDOFF_POLICY.md`, the four original-client/core/provenance policies, `docs/audit/2026-09-20/W88_CHECKPOINT.md` and `W88_EVIDENCE.json`, then W87/W86 and W80's completed review/ledger for retained requirements. Do not restart W80's140-commit review after18 September2026 21:58 Malaysia.

## Exact tested state

Final tested revision **f6ee23de269228270d0d12950b47a5a2b53535ef**, tree **a3878575232f508c6561f4f7486bc3c7e44a48ec**. Last production change **e984fe15f37ab66bcf0bf45d11b58a8fc51fbba0**; f6ee23de only adapts the old restart fixture's controlled Lua boundary. Documentation successors are not additional tested production revisions.

Integrated **35507918903/art10604537448**:17/17, gossip58/58, objective restart37/37. Host **35507918907/art10604851733**:exit0,3344 warnings/0 errors, compile only. Both completed successfully. All game_attached=false. Auxiliary C#-recorded Lua57/57 ran in stockLua5.4, not original5.1/client12340.

## Retain, do not recreate

W88 changes only production `GossipEvent.cs`: one captured complete menu, same-request comparison before selection/cleanup, actor/NPC/context checks, no cleanup borrowing unobserved UI. Typed/order-preserving observations use bounded480-character returns because the existing host reads512bytes per string. Numeric1/0 results preserve the host's string-return contract. Original actor/lifetime/NPC/completion checks remain; selection still requires later authoritative quest progress.

Test-only2dc0ed45 was19/52 with33 intended assertions; strengthened03c255ff was21/58 with37 intended assertions,0unexpected, before the repair. e984fe15 had58/58 gossip but an older restart-fixture compile failure: do not call that integrated run green. f6ee23de preserves all37 restart assertions while adapting only its controlled UI boundary.

Matched reference **8cc504ca15ebf69d16e735b5ed5ade770915c40b**, run35507944742/art10604921533, has21/58 gossip,37 intended assertions,0unexpected and37/37 restart. Against f6ee23de, all153 normalized members are byte-identical;1,835 input paths differ only in GossipEvent.cs. Reference branch `audit/next-88-gossip-unchanged-fixture-red-20260920` is intentionally red: never merge it. W88_EVIDENCE records all archives, hashes, fixture-adaptation history and limits.

Retain all W77-W87 fixes and policies: MIR instance/player/run/operation checks and unknown observations, local deletion-request confirmation, EquipItem admission, Ret registration, objective restart, dense-pull containment and auction withdrawal. W87's7f28d5fd finite scan and db4a16be busy-state preservation are two separate unchanged-fixture pairs, not one; five tests were added between them. Keep the original MIR scan harness, not the duplicate removed atb065b5e0. W87's e3ed01c0/b065b5e0 add/remove history and W77's historical documentation add/revert stay disclosed. The original W87 checkpoint/evidence remain unchanged.

## Next and merge decision

**Next R03:** actual CAST recipe admission, dataset/collection/raw-counter index mapping and loader/scheduler/XML/behavior dispatch. Use assertion-level supported sparse-index and unsupported-action cases; no ordinary-kill substitute or guessed recipes.

R04 physical displaced/foreign cursor and same-slot popup/full lifecycle and R07 actual compiler dependencies/static reload/full refresh remain. R06's named changed-menu defect is repaired offline; same-content reopen/generation identity, native bridge/concurrency and original-client acceptance are not proved. Preserve all prior provenance, quest/buff/gear/navigation/native/live-acceptance requirements and W84/W86/W87's limited R05 scope. No new Gordunni/escort recipe or coordinates exists.

Conditional merge authorization is already given. Do not ask again or merge this partial state. Before eligible merge reconcile direct refs, preserve backup, inspect exact merge preview/final integrated+host evidence and use exact-head conditional merge. Merge is not deployment.

Direct master **b2324913e2499ba30b239dd67224ca2c655c05cc**; do not use the PR's cached base SHA. W88 backup `audit/backup-pr51-before-w88-20260920` points to entry2d73392d43009dccc1a01c986fd77288e70c7fef. No master write/merge/deployment/force-push/installed-file replacement.

Original WoW3.3.5a/build12340; TC3.3.5 primary/AC WotLK secondary. Scope Wholesome questing/navigation/Singular and related safety; no AuctionHouse/ProfessionBuddy expansion, desktop mouse simulation or guessed offsets. No independent reviewer or supervised gameplay acceptance claimed. Every write requires explicit approved nonempty branch, expected parent/blob and reviewed content/message, force=false.

Always finish with updated durable pointers, a copy-ready new-chat prompt and a downloadable UTF-8 handoff.
