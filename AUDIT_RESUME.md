# Resume W84 — deletion observation repaired; merge gates remain

Repo `jeofwong/CopilotBuddy-private` (1367174964), draft PR51, branch `audit/next-55-equipment-observation-20260917`. Reconcile live refs. Read `docs/audit/2026-09-20/W84_CHECKPOINT.md`, `W84_EVIDENCE.json`, W83/W82/W81 and W80's completed review/ledger, plus the four original3.3.5a/core/provenance policies. Do not restart the140-commit review or recreate interrupted work already committed.

Tested head **9f2064769b177eef2ffd43536f6eec35eb2470e4**, tree **36604b85337e112447c06b65ca57eb88a0502d17**. Integrated35497665080/art10601597315 passes17/17, deletion observations18/18. Host35497665006/art10601202604 exits0,3344 warnings/0 errors; no game/tests. Documentation is newer than this tested head.

W84: test-only1ab19b62/run35497245564/art10600602214 is7/18,11 intended assertions,0unexpected. cfca67f3 makes MIR's existing pending-item observer tri-state, preserves unknown reads, samples one inventory view and fences observation to player/pending identity. Known absence is local tracking evidence, not server-confirmed deletion.9f206476 restores one accidentally changed vendor log word. Final red/green inputs differ only in the two intended Methods.cs methods;150 normalized members identical,1832 inputs each,194 inner hashes verified per integrated ZIP. Lua, eligibility/protection and tests unchanged.

Retain W83:5242d74f strict Kind parsing;3856b39c exact cross-kind recipe ownership, unsupported/ambiguous fallback rejection and creature-anchor binding; e518f4a3 corrects the obsolete Escort-to-KillMob test expectation. Keep the residual non-green artifact and do not call the full W83 span an unchanged-fixture pair. W84 reopened W83's final baseline archive and confirmed17/17.

Retain W81 disabled automatic dense-pull provider, reusable-item deadline, AutoEquip context, delete-request gate, gossip actor/NPC checks and reward-selection identity. Retain W82 strict cached/fresh plugin construction, W77–W79 repairs, backups/exclusions and auction withdrawal.

Remaining: R03 CAST recipe scheduling and dataset/collection/raw-counter mapping/execution; R04 EquipItem actor/quest lifetime plus physical displaced/foreign cursor/popup ownership; R05 MIR plugin/player mutation admission and bounded scan continuation; R06 same-NPC changed-menu final selection/cleanup; R07 compiler dependency fingerprints, static-state/reload and full refresh. W84 fixes only R05 observation semantics, not the entire destructive plugin lifecycle. No complete Gordunni/escort recipe or guessed coordinates exist.

Next bounded repair: EquipItem's captured actor/quest lifetime. Preserve explicit-profile semantics and successful valid-owner operations. Do not change gear weights or invent native cursor identity. Add actual tracked tick/submission/confirmation tests before production. R05 scan continuation and R06 final-menu identity remain separately testable work.

The user already authorises merge once all review blockers and final gates are green. Do not ask again, but do not merge a partial state. Before any merge reconcile direct refs, preserve backup, inspect merge preview and exact-head integrated/host evidence. Merge is not deployment/live acceptance.

Original WoW3.3.5a/build12340, TC3.3.5 primary/AC WotLK secondary; questing/navigation/Singular and directly related safety. No desktop mouse simulation, guessed Lua/ABI, auction/ProfessionBuddy expansion or installed-file replacement. Writes require explicit nonempty approved PR branch, expected parent/blob, reviewed content/message, force=false. Direct master was b2324913e2499ba30b239dd67224ca2c655c05cc at entry; recheck before reporting current state. W77's disclosed empty-doc add/revert remains in history.
