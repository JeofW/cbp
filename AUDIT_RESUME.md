# Resume W81 — recovered fixes and reward selection verified; merge blockers remain

Repo `jeofwong/CopilotBuddy-private` (1367174964), draft PR51, branch `audit/next-55-equipment-observation-20260917`. Reconcile live refs before every new slice and write. Read the four original-client/core/provenance policies under `docs/audit/`, then `docs/audit/2026-09-20/W81_CHECKPOINT.md`, `W81_EVIDENCE.json`, W80's review/ledger and `NEXT_CHAT_PROMPT.md`.

**Verified code/test d72c557691c66563f791f3b9e848a9a1e3355a72; tree bf65dce7dde08e2fd3dade05bafbff18639a2044.** Documentation may be newer. Exact integrated35490438405/art10598957028 passes17/17; host35490438279/art10598314425 exits0 with3344 warnings/0 errors. Reward lifetime18/18; all146 normalized members unchanged from94c1f0c7 red; only ActionSelectReward.cs differs among1828 inputs. Intermediate461bf601 compile failure and its one-line import correction are recorded, not hidden.

W80's140-commit/30-path source review is already complete. Entry94c1f0c7 contained16 later commits and stale W80 pointers. Reconciled/reverified and retained:
- R01 provider containmentc3a229ec,9/9; not actual-route acceptance.
- R02 reusable-item acknowledgementbffef4bd + exact unrelated-label restoration1cb478c8,10/10.
- R05 request-flag admissiona46f4d20,14/14; context/scan work remains.
- R06 actor/NPC lifetime6fd1b69d + finite-anchor restoratione91a08b7,17/17; final same-NPC menu ownership remains.
- R04 AutoEquip captured-context346f133b,46/46; retained popup stub correction9d09cb2c is separate, not an unchanged-suite pair.
- R08 reward-selection461bf601 + using-Styx correctiond72c5576,18/18. Scoring unchanged; final original-client Lua choice-set/request guard added. No quest completion or Lua/game execution in the fixture.

Do not recreate these changes. Five recovered targeted fixtures match their clean red archives byte-for-byte at final W81. Keep earlier Ret9/9, collection34/34, normal objective37/37, timeout10/10, popup18/18 and analyzer89 tests.

**Still open:** R03 real loader/scheduler/materializer target/index/unsupported-kind/GameObject contract; R04 EquipItem lifetime and physical displaced-cursor proof; R05 current plugin/player/context, scan continuation and unknown inventory versus removal; R06 final menu generation/selection/cleanup; R07 actual compiler inputs, reload semantics and atomic replacement. Retain all previous original-client, provenance, native/LOS, terrain/Z, buffs, gear/loadout/caps, escort, underwater and remount requirements. No Gordunni recipe/coordinates or auction/ProfessionBuddy expansion.

**Merge permission changed:** the user conditionally authorises merging once all review blockers and exact-head verification are green. Do not ask again for that same conditional permission, but its condition is NOT met at W81. Close or explicitly contain the remaining findings, verify final host/integrated evidence and merge preview, preserve a backup ref, then use exact-head merge. CI green alone is not sufficient. Direct master remains b2324913e2499ba30b239dd67224ca2c655c05cc, approved tree552eeab1233c7c282897dca0ed9f4334c5e8ed43. No master writes in W81.

Every write requires an explicit nonempty approved branch, meaningful content/message and current blob/parent identity; force=false. Preserve the disclosed W77 empty-doc add/revert history and earlier backups/exclusions. No installed files, deployment, independent/live/exhaustive acceptance claims.
