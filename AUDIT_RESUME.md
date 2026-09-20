# Resume W87 — finite MIR scan verified; remaining merge gates stay open

Repo `jeofwong/CopilotBuddy-private` (1367174964), draft PR51 branch `audit/next-55-equipment-observation-20260917`. Reconcile the live ref before writing. Read `docs/audit/HANDOFF_POLICY.md`, the four original-client/core/provenance policies, `docs/audit/2026-09-20/W87_CHECKPOINT.md`, `W87_EVIDENCE.json`, W86 and W80's completed review/ledger. Do not restart the completed140-commit review after18 September2026 21:58 Malaysia.

## Exact tested code

**db4a16beca29b6decf1b580f53343aa19c4321b7**, tree **1aff8741017c2f7127643e38f7539aeb65219f90**. Documentation successors are not additional tested production revisions.

Integrated **35503906697/art10602953497**:17/17, MIR finite scan27/27,0 assertions/0 unexpected. Host **35503906699/art10603700944**:exit0,3344 warnings/0 errors, compile only. No game attached. Digests, source identities, original archives and evidence limits are in W87_EVIDENCE.

Do not recreate:
- W86's instance/player/run/operation deletion checks at73c41821 and handoff policy.
- **7f28d5fd**: one captured finite original inventory queue per trigger, continuation after refusal/returned item/local disappearance, original candidate identity and run/player checks. Existing22-case red6ccba967 was10/22,12 intended assertions,0unexpected ->22/22 and17/17. Only MIR Methods.cs/main Pulse change;153 normalized members unchanged.
- **0f4a072f -> db4a16be**: five added actual Pulse tests caught the first repair discarding queued/remaining work during combat/casting/pause. Red22/27,5 assertions,0unexpected ->27/27 and17/17. The production repair is one nine-line Pulse guard after existing pending-delete processing. All153 normalized members unchanged across this second pair; only MrItemRemover2.cs changes.

The whole6ccba967-to-db4a16be span is not an unchanged-fixture pair: five tests were added between the two production pairs. All22 earlier cases remain unchanged. Fourteen prior deletion/protection/selling method bodies and all existing Lua expressions remain unchanged by W87. Finite candidates do not imply a new wall-clock bound for every legacy sleep/native call.

Reconciliation correction: e3ed01c0 unnecessarily added a duplicate scan harness; b065b5e0 removed only it and restored entry tree91aac55947fb04a4537409c84ab252cc61b951d4 exactly. Keep the original MrItemRemoverScanRegressionTests; do not re-add the duplicate or hide these history commits.

## Next priority and merge decision

**Next: R06 same-NPC changed-menu identity at final selection/cleanup**, retaining W81 actor/NPC and W78 completion checks. Do not infer unchanged menu contents from an unchanged NPC GUID. Original-client APIs only; no first-option fallback or desktop mouse simulation.

Other blockers: R03 CAST recipe scheduling and dataset/collection/raw-counter mapping/dispatch; R04 physical displaced/foreign cursor and same-slot popup/full lifecycle; R07 actual compiler dependencies/static reload/full refresh. W84/W86/W87 cover named local R05 observation, context and finite-scan cases, not full native inventory/deletion acceptance. Keep all prior provenance, quest/buff/gear/navigation/native/live-acceptance requirements and auction withdrawal. No new Gordunni/escort recipe or guessed coordinates exists.

Conditional merge authorization is already given. Do not ask again, and do not merge this partial state. When all blockers are resolved or explicitly contained with evidence, reconcile direct refs, preserve backup, inspect exact merge preview and final integrated/host evidence, then perform an exact-head conditional merge. Merge is not deployment.

Master last directly verified **b2324913e2499ba30b239dd67224ca2c655c05cc**. W87 did not write master, force-push, merge, deploy or replace installed files. Preserve W77's disclosed historical documentation add/revert, backups and exclusions. All writes require explicit nonempty approved PR branch, expected parent/blob and reviewed content/message, force=false.

Original WoW3.3.5a/build12340; TC3.3.5 primary/AC WotLK secondary; Wholesome questing/navigation/Singular plus related safety. Do not expand AuctionHouse/ProfessionBuddy. No independent reviewer or supervised game acceptance claimed.

**Always end with both a copy-ready new-chat prompt and a downloadable UTF-8 handoff, alongside updated repository pointers.**
