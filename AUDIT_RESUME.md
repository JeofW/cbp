# Resume W86 — instance/run-scoped deletion verified; scan continuation next

Repo `jeofwong/CopilotBuddy-private`, draft PR51, branch `audit/next-55-equipment-observation-20260917`. Reconcile live refs before writing. Read W86_CHECKPOINT.md/W86_EVIDENCE.json under docs/audit/2026-09-20, W85–W81 and W80's completed140-commit review. Preserve all four original3.3.5a/core/provenance policies and previous exclusions.

**Required every time:** read `docs/audit/HANDOFF_POLICY.md`. Final reply must contain a copy-paste new-chat prompt and downloadable UTF-8 handoff, in addition to updated root pointers. User specifically requested this on20 September2026.

Tested production **73c418210bdfa9a01e611d84a91a428584381c97**, tree **ee724ea1f71a8d36b3b25d4fa294733ef55a5ab4**. Integrated35501234594/art10602552093:17/17, MIR context82/82. Host35501234606/art10602092819:exit0,3344 warnings,0 errors. Offline Windows, no game attached. Documentation successor is not a new production test.

W86 redae3f1b1d/run35500961319/art10602152292:4/82,78 intended assertions,0unexpected; other16 entries pass. Green fixes private static cross-instance deletion state, captures actor/run/operation identity, resets via existing start/stop events and before disable cleanup, gates mutation on valid current context and removal enabled, and rejects stale callback results. No Lua/selection/timeout/scan-policy change. All152 normalized files identical across the repair; only Methods.cs and MrItemRemover2.cs differ among1834 inputs. Initialc17 harness-normalizer failure and8e5 executedred are distinct retained artifacts; older fixture preparation precedes the unchanged pair.

NEXT: R05 finite multi-candidate pass. Drive real Pulse/CheckForItems with one initial trigger, refused first pickup, local acknowledgement/returned item and later candidates; do not depend on another timer/loot event. Keep eligibility/open/combining rules, capture a bounded pass and actor identity, preserve unknowns. Do not recreate the now-green context/request/observation guards.

Remaining R03 CAST scheduling/raw-index mapping, R04 physical cursor/popup/full lifetime, R06 same-NPC menu identity, R07 dependency/static reload/full refresh. Keep unsupported recipes rejected; no invented Gordunni/escort action or coordinates. No new feature expansion or AuctionHouse/ProfessionBuddy work.

Merge is already conditionally authorized only when all recorded blockers/final gates are cleared. They are not cleared. Do not ask again or merge on aggregate CI alone. Reconcile refs, backup, exact merge preview/checks and exact-head conditional merge when genuinely ready. No deployment/installed files/live acceptance implied. Direct master last knownb2324913e2499ba30b239dd67224ca2c655c05cc; preserve historical disclosed W77 recovery. Explicit nonempty branch/expected parent or blob/force=false for writes. Original3.3.5a/build12340, TC3.3.5 primary/AC WotLK secondary, no desktop mouse or guessed ABI/Lua.
