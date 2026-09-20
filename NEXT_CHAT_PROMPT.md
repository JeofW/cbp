@GitHub

Continue `jeofwong/CopilotBuddy-private`, PR51 branch `audit/next-55-equipment-observation-20260917`. Reconcile live refs, then read AUDIT_RESUME, W82_CHECKPOINT/W82_EVIDENCE under docs/audit/2026-09-20, W81 and the W80 finding ledger plus four governing original-client/core/provenance policies. The post18 September2026 21:58 Malaysia review is already complete; fix its remaining findings, not another history inventory.

Verified tested code **c46ede2c756c56618532c6af89932b73455bb14c**, tree62f812bba43f4647cfa889518de24be7c53fcbd4. Integrated35493042467/art10600005968 passes17/17, fresh compilation8/8 and cached construction7/7. Host35493042471/art10599936166 exits0 with3344 warnings/0 errors; no game attached. Checkpoint documentation is newer than tested production.

DO NOT redo83190e18 cached-construction repair or c46ede2c fresh-compiler repair. Their exact clean reds are eb2561401/7 (6 assertions) and0d2029945/8 (3 assertions),0unexpected. All147/148 normalized fixtures respectively remain unchanged; only PluginManager.cs changes in each repair. The shared loaders, source-cache fingerprint policy and gameplay were not changed. R07 is not fully closed merely because construction is now strict.

Preserve W81 recovered R01 containment, R02 liveness, R04 AutoEquip context, R05 request gate, R06 actor/NPC lifetime and R08 final reward selection, plus W77–W79 repairs and the auction withdrawal. Outstanding gates:
- R03 actual loader -> scheduler -> materializer -> execution contract; CAST recipe admission without ordinary kills; target/location authority, objective/raw-slot mapping, undefined/unimplemented kinds and GameObject dispatch.
- R04 EquipItem lifetime and physical displaced/foreign cursor and same-slot popup proof. A different held entry at an empty source slot is not ownership.
- R05 MIR plugin/player context, bounded scan continuation after refusal/acknowledgement, unknown inventory versus requested deletion.
- R06 same-NPC menu and final option/cleanup ownership in the client request.
- R07 compiler dependency/reload inputs, static-state semantics and complete refresh publication/lifecycle. Constructor failure handling is repaired; do not overstate that boundary.

Obtain actual assertion-level red and unchanged-fixture narrow repair pairs, inspect exact-head integrated/host archives, and keep fixes within the reviewed contracts. Preserve original WoW3.3.5a/build12340, TC3.3.5 primary/AC WotLK secondary. Do not invent Lua APIs/native offsets, desktop mouse actions, Gordunni/escort recipes, coordinates or new auction/ProfessionBuddy work.

The user conditionally authorises merge once all blockers and final gates are green. Do not ask again, but **do not merge on this partial state**. Reconcile direct refs, preserve a backup, inspect merge preview and exact final CI before using an exact-head merge. Source checkpoint merge is not deployment. Last direct master b2324913e2499ba30b239dd67224ca2c655c05cc, approved tree552eeab1233c7c282897dca0ed9f4334c5e8ed43. No W82 master write. Explicit nonempty PR branch, reviewed content/message, expected blob/parent and force=false for every write; never send a blank placeholder action.
