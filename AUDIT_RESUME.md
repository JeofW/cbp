# Resume W83 — strategy safeguards verified; conditional merge still blocked

Repository `jeofwong/CopilotBuddy-private` (1367174964), draft PR51 branch `audit/next-55-equipment-observation-20260917`. Reconcile live refs before writing. Read `docs/audit/2026-09-20/W83_CHECKPOINT.md`, `W83_EVIDENCE.json`, W82/W81, W80's completed review/ledger, and the four original3.3.5a/core/provenance policies. Do not restart the completed140-commit review.

**Tested head e518f4a32b3f4517c81a9b7c26b35d39452a102b**, tree **a66eb9543df2b85c4cf980db25f11807bacfd3e9**. Production last changed at3856b39c; e518f4a3 corrects one retained test expectation. Documentation is newer than the tested code/test head.

Integrated35495595055/art10599609800 passes17/17: strategy Kind9/9, materialization16/16, gossip strategy14/14. Host35495595061/art10600498568 exits0,3344 warnings/0 errors, no game/tests. Digests,193 inner hashes, fixture comparisons and the residual non-green artifact are recorded in W83_EVIDENCE/checkpoint.

Do not recreate:
- **5242d74f**: reuse existing RequiredEnum for recipe Kind. Real loader red8efc6592 is5/9,4 intended assertions,0unexpected ->9/9;149 normalized members unchanged, only DataLoader.cs changes.
- **3856b39c**: one exact quest/objective recipe lookup across kinds; reject unsupported/ambiguous fallback; generated item-use creature must match its source objective anchor; GameObject/ground item protocol remains disabled. Actual materialization redc8604cd7 is9/16,7 intended assertions,0unexpected ->16/16. Real loader/scheduler ordinary and CAST-exclusion controls pass.149 normalized members unchanged across this production pair.
- **e518f4a3**: the production endpoint3856b39c was not aggregate green: a separate retained gossip test still explicitly expected Escort-to-KillMob fallback. Correct only that expectation to InvalidDataException. Final17/17; no production change. The final whole c8604cd7-to-e518 span is NOT an unchanged-fixture pair. Retain the13/14 gossip residual artifact and exact one-test plus normalization-manifest delta.

Retain W81's disabled dense-pull provider, reusable-item deadline, AutoEquip context, successful-delete-request gate, gossip actor/NPC checks and final reward-selection identity. Retain W82's strict cached construction83190e18/fresh compilec46ede2c, W77–W79 repairs, all backups/exclusions and auction withdrawal. Do not infer full native lifecycle from their narrower tests.

**Remaining merge gates:** R03 CAST recipe scheduling and dataset/collection/raw-counter mapping/execution; R04 EquipItem lifetime plus physical displaced/foreign cursor/popup ownership; R05 MIR plugin/player context, bounded scan continuation and unknown inventory; R06 same-NPC changed-menu/final selection/cleanup; R07 compiler dependency fingerprints, static-state/reload and full refresh lifecycle. W83 closes only named R03 subcases. No new Gordunni/escort recipe or coordinates exist.

Next repairs must stay in one demonstrated owner domain at a time. For R05 do not repeat the successful-request guard: reproduce pending Stop/world/player changes, unknown inventory and a multi-candidate scan with no extra timer/loot trigger. For R04 avoid moving unidentified cursor contents. For R06 compare the current exact menu in the final client request, not merely an earlier NPC check. Keep R03 unsupported actions rejected until a complete strategy contract is established; do not unblock CAST by ordinary killing. Avoid another broad plugin/strategy framework.

The user already conditionally authorises merge once all blockers and final gates are green. Do not ask again, but **do not merge this partial state**. Before merge reconcile direct refs, preserve a backup, inspect merge preview and exact integrated/host evidence, and use an exact-head conditional merge. Merge is not deployment/live acceptance.

Original WoW3.3.5a/build12340, TC3.3.5 primary/AC WotLK secondary; questing/navigation/Singular and related safety only. No desktop mouse simulation, guessed Lua/ABI, auction/ProfessionBuddy expansion or installed-file replacement. Every write needs an explicit nonempty approved PR branch, expected parent/blob, reviewed content/message and force=false. Last direct master **b2324913e2499ba30b239dd67224ca2c655c05cc**; W83 did not write it. W77's disclosed empty-doc add/revert history remains.
