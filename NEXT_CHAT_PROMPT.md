@GitHub

Continue `jeofwong/CopilotBuddy-private` (1367174964), draft PR51 branch `audit/next-55-equipment-observation-20260917`. Reconcile live refs; read AUDIT_RESUME, W83_CHECKPOINT/W83_EVIDENCE under docs/audit/2026-09-20, W82/W81, W80's findings and the four governing original-client/core/provenance policies. The post18 September2026 21:58 Malaysia source review is complete. Continue its fixes, not another history inventory.

Verified code/test **e518f4a32b3f4517c81a9b7c26b35d39452a102b**, tree **a66eb9543df2b85c4cf980db25f11807bacfd3e9**. Integrated35495595055/art10599609800 passes17/17; strategy Kind9/9, materialization16/16, gossip strategy14/14. Host35495595061/art10600498568 exits0 with3344 warnings/0 errors. No game attached. Documentation is newer than tested code.

Do not redo W83:
- 8efc6592 ->5242d74f: actual loader Kind rejection5/9,4 intended assertions,0unexpected ->9/9. RequiredEnum replaces unchecked parsing; only DataLoader.cs changes, all149 normalized members identical.
- c8604cd7 ->3856b39c: actual materialization9/16,7 intended assertions,0unexpected ->16/16. One cross-kind exact-owner lookup; reject unsupported/ambiguous recipes and unimplemented GameObject item protocol; bind creature item target to its source objective. Real loader/scheduler no-pack ordinary and CAST-exclusion controls are preserved. Only ProfileBuilder.cs changes,149 normalized members identical.
- 3856b39c was NOT overall green: retained gossip13/14 had one InvalidDataException because its old separate Escort test expected legacy KillMob XML. Test-onlye518f4a3 corrects that expectation, not production. Final17/17. Preserve the residual archive;147 normalized members remain identical across the test correction, with only the test and normalization-manifest.json differing. Never call the whole final chain an unchanged-fixture pair.

Preserve W81 containment/liveness/context/request/NPC/reward repairs, W82 strict cached/fresh construction83190e18/c46ede2c, W77–W79 and auction withdrawal. The final run retains AutoEquip46/46, reusable-item10/10, delete-request14/14, gossip-lifetime17/17, reward-selection18/18, plugin construction7/7 and8/8, collection34/34, normal-restart37/37, Ret9/9, equip-timeout10/10 and89 analyzer tests. Their native/controlled-boundary limits remain.

Outstanding work before merge:
**R05**: reproduce pending deletion after Stop/world/player replacement; unknown inventory must not become removal confirmation; finite continuation after refused/acknowledged deletion with multiple candidates and no timer/loot retrigger. Successful-request admission is already fixed. Keep quest/protected-item guards.
**R04**: EquipItem lifetime and physical displaced/foreign cursor/popup boundaries. Do not move an unidentified held item or invent a GUID ABI from an offset name. Preserve normal equip, timeout and successful-local-submission checks.
**R06**: same-NPC changed menu and final selection/cleanup in one checked client request. W83 only inspected GossipEvent/GossipFrame/GossipEntry; no new R06 code or Lua was shipped. Existing actor/NPC guards do not establish unchanged menu identity.
**R07**: actual compiler reference/options dependencies, static-state reload semantics and complete refresh lifecycle. Strict constructors are fixed; do not overstate that as atomic arbitrary plugin initialization.
**R03**: complete CAST recipe scheduling and dataset/collection/raw-counter mapping and execution authority. Keep unsupported work rejected/deferred; never replace a required item/cast/escort action with ordinary killing. No complete Gordunni dig/spawn/loot recipe is implemented.

Choose a bounded owner slice, demonstrate actual assertion-level red, preserve fixtures through the narrow production change and inspect exact-head integrated/host artifacts. Test expectation corrections require an explicit source reason and retained prior failure; never silently weaken guards or count source tokens as complete client lifecycle proof. Avoid broad new frameworks or unrelated features.

Original WoW3.3.5a/build12340 only, TC3.3.5 primary/AC WotLK secondary. No assumed modern/Classic APIs, desktop mouse simulation, guessed coordinates, auction/ProfessionBuddy expansion, or installed-file changes. Keep every earlier backup/exclusion/provenance/native/live acceptance requirement.

The user already conditionally authorises merge after all blockers and final validation are green. No redundant approval request, but **do not merge merely because these partial suites are green**. Recheck direct refs, preserve a backup, inspect merge preview and final exact-head artifacts, then use an exact-head conditional merge. Current last-observed masterb2324913e2499ba30b239dd67224ca2c655c05cc was not touched in W83; W77's disclosed doc add/revert remains historical. Every write requires explicit nonempty approved PR branch, reviewed content/message, expected blob/parent and force=false. No blank placeholder calls.
